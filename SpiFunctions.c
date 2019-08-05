/**
  * Copyright (c) Analog Devices Inc, 2018 - 2019
  * All Rights Reserved.
  * 
  * THIS SOFTWARE UTILIZES LIBRARIES DEVELOPED
  * AND MAINTAINED BY CYPRESS INC. THE LICENSE INCLUDED IN
  * THIS REPOSITORY DOES NOT EXTEND TO CYPRESS PROPERTY.
  * 
  * Use of this file is governed by the license agreement
  * included in this repository.
  * 
  * @file		SpiFunctions.c
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		This file contains all generic SPI read/write function implementations.
 **/

#include "SpiFunctions.h"

/* Tell the compiler where to find the needed globals */
extern CyU3PDmaChannel ChannelFromPC;
extern CyU3PDmaChannel ChannelToPC;
extern uint8_t USBBuffer[4096];
extern uint8_t BulkBuffer[12288];
extern CyU3PDmaBuffer_t ManualDMABuffer;
extern BoardState FX3State;
extern StreamState StreamThreadState;

/**
  * @brief This function performs a protocol agnostic SPI bi-directional SPI transfer of (1, 2, 4) bytes
  *
  * @param writeData The data to transmit on the MOSI line.
  *
  * @return A status code indicating the success of the function.
  *
  * This function performs a bi-directional SPI transfer, on up to 4 bytes of data. The transfer length is
  * determined by the current SPI config word length setting. The status and data recieved on the MISO line
  * are sent to the PC over EP0 following the transfer.
 **/
CyU3PReturnStatus_t AdiTransferBytes(uint32_t writeData)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint8_t writeBuffer[4];
	uint8_t readBuffer[4];
	uint32_t transferSize;

	//populate the writebuffer
	writeBuffer[0] = writeData & 0xFF;
	writeBuffer[1] = (writeData & 0xFF00) >> 8;
	writeBuffer[2] = (writeData & 0xFF0000) >> 16;
	writeBuffer[3] = (writeData & 0xFF000000) >> 24;

	//Calculate number of bytes to transfer
	transferSize = FX3State.SpiConfig.wordLen / 8;

	//perform SPI transfer
	status = CyU3PSpiTransferWords(writeBuffer, transferSize, readBuffer, transferSize);

	/* Send status and data back via control endpoint */
	USBBuffer[0] = status & 0xFF;
	USBBuffer[1] = (status & 0xFF00) >> 8;
	USBBuffer[2] = (status & 0xFF0000) >> 16;
	USBBuffer[3] = (status & 0xFF000000) >> 24;
	USBBuffer[4] = readBuffer[0];
	USBBuffer[5] = readBuffer[1];
	USBBuffer[6] = readBuffer[2];
	USBBuffer[7] = readBuffer[3];
	CyU3PUsbSendEP0Data (8, USBBuffer);

	return status;
}

/**
  * @brief This function reads a single 16 bit SPI word from a slave device.
  *
  * @param addr The address to send to the DUT in the first SPI transaction.
  *
  * @return A status code indicating the success of the function.
  *
  * This function reads a single word over SPI. Note that reads are not "full duplex"
  * and will require a discrete read to set the address to be read from (two 16 bit transactions per read).
 **/
CyU3PReturnStatus_t AdiReadRegBytes(uint16_t addr)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint8_t tempBuffer[2];

	/* Set the address to read from */
	tempBuffer[0] = (0x7F) & addr;
	/* Set the second byte to 0's */
	tempBuffer[1] = 0;
	/* Send SPI Read command */
	status = CyU3PSpiTransmitWords(tempBuffer, 2);
	/* Check that the transfer was successful and end function if failed */
	if (status != CY_U3P_SUCCESS)
	{
        CyU3PDebugPrint (4, "Error! CyU3PSpiTransmitWords failed, error code: 0x%s\r\n", status);
	}

	/* Stall for user-specified time */
	AdiSleepForMicroSeconds(FX3State.StallTime);

	/* Receive the data requested */
	status = CyU3PSpiReceiveWords(tempBuffer, 2);
	/* Check that the transfer was successful and end function if failed */
	if (status != CY_U3P_SUCCESS)
	{
        CyU3PDebugPrint (4, "Error! CyU3PSpiReceiveWords failed! Status Code 0x%X\r\n", status);
	}

	/* Send status and data back via control endpoint */
	USBBuffer[0] = status & 0xFF;
	USBBuffer[1] = (status & 0xFF00) >> 8;
	USBBuffer[2] = (status & 0xFF0000) >> 16;
	USBBuffer[3] = (status & 0xFF000000) >> 24;
	USBBuffer[4] = tempBuffer[0];
	USBBuffer[5] = tempBuffer[1];
	CyU3PUsbSendEP0Data (6, USBBuffer);

	return status;
}

/**
  * @brief This function writes a single byte of data over the SPI bus
  *
  * @param addr The DUT address to write data to (7 bits).
  *
  * @param data The byte of data to write to the address
  *
  * @return A status code indicating the success of the function.
 **/
CyU3PReturnStatus_t AdiWriteRegByte(uint16_t addr, uint8_t data)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint8_t tempBuffer[2];
	tempBuffer[0] = 0x80 | addr;
	tempBuffer[1] = data;
	status = CyU3PSpiTransmitWords (tempBuffer, 2);
	/* Check that the transfer was successful and end function if failed */
	if (status != CY_U3P_SUCCESS)
	{
        CyU3PDebugPrint (4, "Error! CyU3PSpiTransmitWords failed! Status code 0x%x\r\n", status);
        //AdiAppErrorHandler (status);
	}
	/* Send write status over the control endpoint */
	USBBuffer[0] = status & 0xFF;
	USBBuffer[1] = (status & 0xFF00) >> 8;
	USBBuffer[2] = (status & 0xFF0000) >> 16;
	USBBuffer[3] = (status & 0xFF000000) >> 24;
	CyU3PUsbSendEP0Data (4, USBBuffer);

	return status;
}

/**
  * @brief This function performs a bulk register transfer using the bulk in and out endpoints.
  *
  * @param numBytes The total number of bytes to read
  *
  * @param bytesPerCapture The total number of bytes to read per data ready, if DrActive is true.
  *
  * @return A status code indicating the success of the function.
  *
  * This function handles calls in the IRegInterface which require more than one register read/write operation,
  * allowing for better performance.
 **/
CyU3PReturnStatus_t AdiBulkByteTransfer(uint16_t numBytes, uint16_t bytesPerCapture)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint16_t loopCounter = 0;
	uint8_t buffValue;
	uint8_t *bufPntr;
	uint8_t TxBuffer[2];
	uint32_t transferStatus = 0;
	CyU3PGpioIntrMode_t waitType = CY_U3P_GPIO_INTR_POS_EDGE;

	//Transfer in data via ChannelFromPC
	ManualDMABuffer.buffer = BulkBuffer;
	ManualDMABuffer.size = sizeof(BulkBuffer);
	ManualDMABuffer.count = numBytes;
	ManualDMABuffer.status = 0;

	status = CyU3PDmaChannelSetupRecvBuffer(&ChannelFromPC, &ManualDMABuffer);

	//Wait for the DMA channel to finish (20ms timeout)
	while(transferStatus != CY_U3P_DMA_CB_RECV_CPLT && loopCounter < 4)
	{
		CyU3PEventGet(&(ChannelFromPC.flags), CY_U3P_DMA_CB_RECV_CPLT,  CYU3P_EVENT_OR, &transferStatus, 5);
		loopCounter++;
	}

	//Reset the DMA channel
	CyU3PDmaChannelReset(&ChannelFromPC);

	//Set the bytesPerCapture depending on DrActive
	if(!FX3State.DrActive)
	{
		bytesPerCapture = numBytes;
	}
	else
	{
		//Set the transition type depending on the DrPolarity
		if(FX3State.DrPolarity)
		{
			waitType = CY_U3P_GPIO_INTR_POS_EDGE;;
		}
		else
		{
			waitType = CY_U3P_GPIO_INTR_NEG_EDGE;
		}
	}

	//Loop through rest of the transfers
	bufPntr = BulkBuffer;
	loopCounter = 0;
	while(loopCounter < numBytes)
	{
		//Wait for data ready if needed and run through one set of registers
		if(FX3State.DrActive)
		{
			AdiWaitForPin(FX3State.DrPin, waitType, CYU3P_WAIT_FOREVER);
		}
		//For first transfer don't read back
		if(BulkBuffer[loopCounter] & 0x80)
		{
			//Case of a SPI write
			TxBuffer[0] = BulkBuffer[loopCounter];
			TxBuffer[1] = BulkBuffer[loopCounter + 1];
			CyU3PSpiTransmitWords(TxBuffer, 2);
			BulkBuffer[loopCounter] = 0;
			BulkBuffer[loopCounter + 1] = 0;
		}
		else
		{
			//Case of a SPI read
			TxBuffer[0] = BulkBuffer[loopCounter];
			TxBuffer[1] = 0;
			CyU3PSpiTransmitWords(TxBuffer, 2);
		}
		loopCounter+=2;

		AdiSleepForMicroSeconds(FX3State.StallTime);

		//Loop through rest of the reads
		while(loopCounter < bytesPerCapture)
		{
			//Get the value out of the bulk buffer
			buffValue = BulkBuffer[loopCounter];

			if(buffValue & 0x80)
			{
				//If its a write command, perform write
				//perform SPI write
				TxBuffer[0] = buffValue;
				TxBuffer[1] = BulkBuffer[loopCounter + 1];
				CyU3PSpiTransmitWords(TxBuffer, 2);
				//Store 0 in the Bulk Buffer
				BulkBuffer[loopCounter] = 0;
				BulkBuffer[loopCounter + 1] = 0;
			}
			else
			{
				//If it's a read command, perform SPI read and write and store value in bulk buffer
				TxBuffer[0] = buffValue;
				TxBuffer[1] = 0;
				CyU3PSpiTransferWords(TxBuffer, 2, bufPntr, 2);
			}
			bufPntr += 2;
			loopCounter += 2;
			AdiSleepForMicroSeconds(FX3State.StallTime);
		}
		//Receive the last two bytes
		if(buffValue & 0x80)
		{
			BulkBuffer[loopCounter] = 0;
			BulkBuffer[loopCounter + 1] = 0;
		}
		else
		{
			CyU3PSpiReceiveWords(bufPntr, 2);
		}
		//Increment loop counter
		loopCounter += 2;
	}

	//Send the data back over ChannelToPC
	status = CyU3PDmaChannelSetupSendBuffer(&ChannelToPC, &ManualDMABuffer);

	return status;
}

/**
  * @brief This function resets the SPI FIFO and disables the SPI block after completion.
  *
  * @param isTx Boolean to indicate if you're clearing the TX FIFO
  *
  * @param isRx Boolean to indicate if you're clearing the RX FIFO
  *
  * @return The success of the SPI reset FIFO operation.
  *
  * It is a copy of the private CyU3PSpiResetFifo() function which bypasses some input sanitization which the Cypress
  * libraries perform. This is required due to our high-speed, register-initiated transfers.
  *
 **/
CyU3PReturnStatus_t AdiSpiResetFifo(CyBool_t isTx, CyBool_t isRx)
{
	uint32_t intrMask;
	uint32_t ctrlMask = 0;
	uint32_t temp;

	/* No lock is acquired or error checked */

	/* Temporarily disable interrupts. */
	intrMask = SPI->lpp_spi_intr_mask;
	SPI->lpp_spi_intr_mask = 0;

	if (isTx)
	{
		ctrlMask = CY_U3P_LPP_SPI_TX_CLEAR;
	}
	if (isRx)
	{
		ctrlMask |= CY_U3P_LPP_SPI_RX_CLEAR;
	}

	/* Disable the SPI block and reset. */
	temp = ~(CY_U3P_LPP_SPI_RX_ENABLE | CY_U3P_LPP_SPI_TX_ENABLE |
		CY_U3P_LPP_SPI_DMA_MODE | CY_U3P_LPP_SPI_ENABLE);
	SPI->lpp_spi_config &= temp;
	while ((SPI->lpp_spi_config & CY_U3P_LPP_SPI_ENABLE) != 0);

	/* Clear the FIFOs and wait until they have been cleared. */
	SPI->lpp_spi_config |= ctrlMask;
	if (isTx)
	{
		while ((SPI->lpp_spi_status & CY_U3P_LPP_SPI_TX_DONE) == 0);
	}
	if (isRx)
	{
		while ((SPI->lpp_spi_status & CY_U3P_LPP_SPI_RX_DATA) != 0);
	}
	SPI->lpp_spi_config &= ~ctrlMask;

	/* Clear all interrupts and re-enable them. */
	SPI->lpp_spi_intr |= CY_U3P_LPP_SPI_TX_DONE;
	SPI->lpp_spi_intr_mask = intrMask;

	return CY_U3P_SUCCESS;
}

/**
  * @brief This function handles vendor commands to get the current SPI configuration from the FX3
  *
  * @return A status code indicating the success of the function.
  *
  * This function allows the FX3 API to verify that the FX3 board has the same SPI settings as the
  * current FX3 connection instance. The current configuration is sent to the PC via EP0.
 **/
CyU3PReturnStatus_t AdiGetSpiSettings()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint8_t USBBuffer[23];
	//Clock first
	USBBuffer[0] = FX3State.SpiConfig.clock & 0xFF;
	USBBuffer[1] = (FX3State.SpiConfig.clock & 0xFF00) >> 8;
	USBBuffer[2] = (FX3State.SpiConfig.clock & 0xFF0000) >> 16;
	USBBuffer[3] = (FX3State.SpiConfig.clock & 0xFF000000) >> 24;
	USBBuffer[4] = FX3State.SpiConfig.cpha;
	USBBuffer[5] = FX3State.SpiConfig.cpol;
	USBBuffer[6] = FX3State.SpiConfig.isLsbFirst;
	USBBuffer[7] = FX3State.SpiConfig.lagTime;
	USBBuffer[8] = FX3State.SpiConfig.leadTime;
	USBBuffer[9] = FX3State.SpiConfig.ssnCtrl;
	USBBuffer[10] = FX3State.SpiConfig.ssnPol;
	USBBuffer[11] = FX3State.SpiConfig.wordLen;
	USBBuffer[12] = FX3State.StallTime & 0xFF;
	USBBuffer[13] = (FX3State.StallTime & 0xFF00) >> 8;
	USBBuffer[14] = FX3State.DutType;
	USBBuffer[15] = (CyBool_t) FX3State.DrActive;
	USBBuffer[16] = (CyBool_t) FX3State.DrPolarity;
	USBBuffer[17] = FX3State.DrPin & 0xFF;
	USBBuffer[18] = (FX3State.DrPin & 0xFF00) >> 8;
	USBBuffer[19] = MS_TO_TICKS_MULT & 0xFF;
	USBBuffer[20] = (MS_TO_TICKS_MULT & 0xFF00) >> 8;
	USBBuffer[21] = (MS_TO_TICKS_MULT & 0xFF0000) >> 16;
	USBBuffer[22] = (MS_TO_TICKS_MULT & 0xFF000000) >> 24;
	status = CyU3PUsbSendEP0Data (23, USBBuffer);
	return status;
}

/**
  * @brief This function handles a vendor command request to update the SPI/DR Pin configuration.
  *
  * @param index The wIndex from the control endpoint transaction which indicates which parameter to update
  *
  * @param value The wValue from the control endpoint transaction which holds the SPI value to set for the selected parameter.
  *
  * @param length The length of the Data In phase of the control endpoint transaction
  *
  * @return A boolean indicating if the SPI configuration was a success
  *
  * This function provides an API for maintaining synchronicity in SPI and data ready triggering settings between the FX3 API and
  * the firmware. Any time a setting is changed on the FX3 API, this function will be invoked to reflect that change.
 **/
CyBool_t AdiSpiUpdate(uint16_t index, uint16_t value, uint16_t length)
{
    uint32_t clockFrequency;
    uint16_t *bytesRead = 0;
	CyBool_t isHandled = CyTrue;
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	CyU3PUsbGetEP0Data(length, USBBuffer, bytesRead);
	switch(index)
	{
	case 0:
		//Clock setting
		if(length != 4)
		{
			//Reasonable Default if data frame isn't set properly
			FX3State.SpiConfig.clock = 2000000;
		}
		else
		{
			clockFrequency = USBBuffer[3];
			clockFrequency += USBBuffer[2] << 8;
			clockFrequency += USBBuffer[1] << 16;
			clockFrequency += USBBuffer[0] << 24;
			FX3State.SpiConfig.clock = clockFrequency;
			status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
			CyU3PDebugPrint (4, "SCLK = %d\r\n", clockFrequency);
#endif
		}
		break;

	case 1:
		//cpol
		FX3State.SpiConfig.cpol = (CyBool_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "cpol = %d\r\n", value);
#endif
		break;

	case 2:
		//cpha
		FX3State.SpiConfig.cpha = (CyBool_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "cpha = %d\r\n", value);
#endif
		break;

	case 3:
		//Chip Select Polarity
		FX3State.SpiConfig.ssnPol = (CyBool_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "ssnPol = %d\r\n", value);
#endif
		break;

	case 4:
		//Chip Select Control
		FX3State.SpiConfig.ssnCtrl = (CyU3PSpiSsnCtrl_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "ssnCtrl = %d\r\n", value);
#endif
		break;

	case 5:
		//Lead Time
		FX3State.SpiConfig.leadTime = (CyU3PSpiSsnLagLead_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "leadTime = %d\r\n", value);
#endif
		break;

	case 6:
		//Lag Time
		FX3State.SpiConfig.lagTime = (CyU3PSpiSsnLagLead_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "lagTime = %d\r\n", value);
#endif
		break;

	case 7:
		//Is LSB First
		FX3State.SpiConfig.isLsbFirst = (CyBool_t) value;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "isLsbFirst = %d\r\n", value);
#endif
		break;

	case 8:
		//Word Length
		FX3State.SpiConfig.wordLen = value & 0xFF;
		status = CyU3PSpiSetConfig (&FX3State.SpiConfig, NULL);
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "wordLen = %d\r\n", value);
#endif
		break;

	case 9:
		//Stall time in ticks (received in ticks from the PC, each tick = 1us)
		FX3State.StallTime = value;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "stallTime = %d\r\n", value);
#endif
		break;

	case 10:
		//DUT type
		FX3State.DutType = value;
		switch(FX3State.DutType)
		{
		case ADcmXL3021:
			StreamThreadState.BytesPerFrame = 200; /* (32 word x 3 axis) + 4 word status/counter/etc */
			break;
		case ADcmXL2021:
			StreamThreadState.BytesPerFrame = 152; /* (32 word x 2 axis) + 8 word padding + 4 word status/counter/etc */
			break;
		case ADcmXL1021:
			StreamThreadState.BytesPerFrame = 88; /* 32 word + 8 word padding + 4 word status/counter/etc */
			break;
		case Other:
		default:
			StreamThreadState.BytesPerFrame = 200;
			break;
		}
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "bytesPerFrame = %d\r\n", StreamThreadState.BytesPerFrame);
#endif
		break;

	case 11:
		//DR polarity
		FX3State.DrPolarity = (CyBool_t) value;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "DrPolarity = %d\r\n", value);
#endif
		break;

	case 12:
		//DR active
		FX3State.DrActive = (CyBool_t) value;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "DrActive = %d\r\n", value);
#endif
		break;

	case 13:
		//Ready pin
		FX3State.DrPin = value;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "DrPin = %d\r\n", value);
#endif
		break;

	default:
		//Invalid Command
		isHandled = CyFalse;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "ERROR: Invalid SPI config command!\r\n");
#endif
		break;
	}

	//Check that the configuration was successful
	if(status != CY_U3P_SUCCESS)
	{
		isHandled = CyFalse;
	}

	return isHandled;
}

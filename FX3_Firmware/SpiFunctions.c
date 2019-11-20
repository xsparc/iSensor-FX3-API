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
extern uint8_t USBBuffer[4096];
extern uint8_t BulkBuffer[12288];
extern BoardState FX3State;
extern StreamState StreamThreadState;
extern CyU3PDmaBuffer_t ManualDMABuffer;
extern CyU3PDmaChannel ChannelToPC;

/**
  * @brief This function restarts the SPI controller.
  *
  * @return A status code indicating the success of the setConfig call after re-initialization.
  *
  * This function can be used to restore hardware SPI functionality after overriding the SPI pins
  * to act as a bitbanged SPI port. This function can be called before or after the SPI controller
  * has been initialized without causing problems.
 **/
CyU3PReturnStatus_t AdiRestartSpi()
{
	/* Deactivate SPI controller */
	CyU3PSpiDeInit();
	/* Restore pins */
	CyU3PDeviceGpioRestore(53);
	CyU3PDeviceGpioRestore(54);
	CyU3PDeviceGpioRestore(55);
	CyU3PDeviceGpioRestore(56);
	/* Reinitialized */
	CyU3PSpiInit();
	/* Set the prior config */
	return CyU3PSpiSetConfig(&FX3State.SpiConfig, NULL);
}


/**
  * @brief This function handles bit bang SPI requests from the control endpoint.
  *
  * @returns A status code indicating the success of the SPI bitbang operation.
  *
  * This function requires all data to have been retrieved from the control endpoint before being
  * called.
 **/
CyU3PReturnStatus_t AdiBitBangSpiHandler()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	BitBangSpiConf config;
	uint32_t bytesPerTransfer;
	uint32_t bitsPerTransfer;
	uint32_t numTransfers;
	uint32_t transferCounter;

	/* Buffer pointers */
	uint8_t * MOSIPtr;
	uint8_t * MISOPtr;

	/* Parse data from the USB buffer */
	config.SCLK = USBBuffer[0];
	config.CS = USBBuffer[1];
	config.MOSI = USBBuffer[2];
	config.MISO = USBBuffer[3];
	config.HalfClockDelay = USBBuffer[4];
	config.HalfClockDelay |= (USBBuffer[5] << 8);
	config.HalfClockDelay |= (USBBuffer[6] << 16);
	config.HalfClockDelay |= (USBBuffer[7] << 24);
	config.CSLeadDelay = USBBuffer[8];
	config.CSLeadDelay |= (USBBuffer[9] << 8);
	config.CSLagDelay = USBBuffer[10];
	config.CSLagDelay |= (USBBuffer[11] << 8);
	bitsPerTransfer = USBBuffer[12];
	bitsPerTransfer |= (USBBuffer[13] << 8);
	bitsPerTransfer |= (USBBuffer[14] << 16);
	bitsPerTransfer |= (USBBuffer[15] << 24);
	numTransfers = USBBuffer[16];
	numTransfers |= (USBBuffer[17] << 8);
	numTransfers |= (USBBuffer[18] << 16);
	numTransfers |= (USBBuffer[19] << 24);

	/* Calculate bytes per transfer */
	bytesPerTransfer = bitsPerTransfer >> 3;
	/* Account for non-byte aligned transfers */
	if(bitsPerTransfer & 0x7)
	{
		bytesPerTransfer++;
	}

	/* Memclear the bulk buffer */
	CyU3PMemSet (BulkBuffer, 0, sizeof(BulkBuffer));

	/* Start MISO pointer at bulk buffer */
	MISOPtr = BulkBuffer;

	/* Start MOSI pointer at USBBuffer[20] */
	MOSIPtr = USBBuffer;
	MOSIPtr += 20;

	/* Setup the GPIO selected */
	status = AdiBitBangSpiSetup(config);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Bit bang SPI setup failed, error code: = 0x%x\r\n", status);
	}
	else
	{
		/* Perform transfers */
		for(transferCounter = 0; transferCounter < numTransfers; transferCounter++)
		{
			/* Transfer data */
			AdiBitBangSpiTransfer(MOSIPtr, MISOPtr, bitsPerTransfer, config);
			/* Wait for stall time */
			CyFx3BusyWait(FX3State.StallTime - 2);
			/* Update buffer pointers */
			MOSIPtr += bytesPerTransfer;
			MISOPtr += bytesPerTransfer;
		}
	}

	/* Return MISO data over bulk buffer */
	ManualDMABuffer.buffer = BulkBuffer;
	ManualDMABuffer.size = sizeof(BulkBuffer);
	ManualDMABuffer.count = numTransfers * bytesPerTransfer;

	/* Send the data to PC */
	status = CyU3PDmaChannelSetupSendBuffer(&ChannelToPC, &ManualDMABuffer);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Sending DR data to PC failed!, error code = 0x%x\r\n", status);
	}

	return status;
}

/**
  * @brief Configures all pins and timers needed to bitbang a SPI connection.
  *
  * @param config A structure containing all the relevant bit banged SPI configuration parameters.
 **/
CyU3PReturnStatus_t AdiBitBangSpiSetup(BitBangSpiConf config)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	if(!CyU3PIsGpioValid(config.MOSI))
	{
		CyU3PDebugPrint (4, "Error! Invalid MOSI GPIO pin number: %d\r\n", config.MOSI);
		return CY_U3P_ERROR_BAD_ARGUMENT;
	}
	if(!CyU3PIsGpioValid(config.SCLK))
	{
		CyU3PDebugPrint (4, "Error! Invalid SCLK GPIO pin number: %d\r\n", config.SCLK);
		return CY_U3P_ERROR_BAD_ARGUMENT;
	}
	if(!CyU3PIsGpioValid(config.CS))
	{
		CyU3PDebugPrint (4, "Error! Invalid CS GPIO pin number: %d\r\n", config.CS);
		return CY_U3P_ERROR_BAD_ARGUMENT;
	}
	if(!CyU3PIsGpioValid(config.MISO))
	{
		CyU3PDebugPrint (4, "Error! Invalid MISO GPIO pin number: %d\r\n", config.MISO);
		return CY_U3P_ERROR_BAD_ARGUMENT;
	}

	/* Output config */
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.outValue = CyTrue;
	gpioConfig.inputEn = CyFalse;
	gpioConfig.driveLowEn = CyTrue;
	gpioConfig.driveHighEn = CyTrue;
	gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;

	/* Set MOSI, CS, SCLK as output pins driven high */
	status = CyU3PGpioSetSimpleConfig(config.SCLK, &gpioConfig);
	if(status != CY_U3P_SUCCESS)
	{
		/* Override the pin to act as simple GPIO */
		CyU3PDeviceGpioOverride(config.SCLK, CyTrue);
		/* Set the config again */
		status = CyU3PGpioSetSimpleConfig(config.SCLK, &gpioConfig);
	}

	status = CyU3PGpioSetSimpleConfig(config.CS, &gpioConfig);
	if(status != CY_U3P_SUCCESS)
	{
		/* Override the pin to act as simple GPIO */
		CyU3PDeviceGpioOverride(config.CS, CyTrue);
		/* Set the config again */
		status = CyU3PGpioSetSimpleConfig(config.CS, &gpioConfig);
	}

	status = CyU3PGpioSetSimpleConfig(config.MOSI, &gpioConfig);
	if(status != CY_U3P_SUCCESS)
	{
		/* Override the pin to act as simple GPIO */
		CyU3PDeviceGpioOverride(config.MOSI, CyTrue);
		/* Set the config again */
		status = CyU3PGpioSetSimpleConfig(config.MOSI, &gpioConfig);
	}

	/* Set MISO as input pin */
	gpioConfig.outValue = CyFalse;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
	status = CyU3PGpioSetSimpleConfig(config.MISO, &gpioConfig);
	if(status != CY_U3P_SUCCESS)
	{
		/* Override the pin to act as simple GPIO */
		CyU3PDeviceGpioOverride(config.MISO, CyTrue);
		/* Set the config again */
		status = CyU3PGpioSetSimpleConfig(config.MISO, &gpioConfig);
	}

	/* Ensure 10MHz clock is operating in correct mode */
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status &= ~(CY_U3P_LPP_GPIO_INTRMODE_MASK);

	/* reset the timer register */
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;

	return status;
}

/**
  * @brief Performs a single bit banged SPI transfer. Pins must already be configured as needed.
  *
  * @param MOSI A pointer to the master out data buffer. This data will be transmitted MSB first, over the MOSI line in config.
  *
  * @param MISO A pointer to the data receive (rx) buffer. The data received from a slave will be placed here.
  *
  * @param BitCount The number of bits to transfer.
  *
  * @param config The configuration settings to use for the transfer.
  *
  * This function allows a user to use any pins on the FX3 as a low speed SPI master. The API
  * provided is similar to the Cypress API for the hardware SPI.
 **/
void AdiBitBangSpiTransfer(uint8_t * MOSI, uint8_t* MISO, uint32_t BitCount, BitBangSpiConf config)
{
	/* Track the number of bits clocked */
	uint32_t bitCounter, byteCounter, highMask, lowMask, MOSIMask, finalTime, tempCnt;
	uint8_t bytePosition;
	register uint32_t MISOValue;
	register uvint32_t cycleTimer;
	uvint32_t *SCLKPin, *CSPin, *MISOPin, *MOSIPin;

	/* Set pin pointers */
	MOSIPin = &GPIO->lpp_gpio_simple[config.MOSI];
	MISOPin = &GPIO->lpp_gpio_simple[config.MISO];
	CSPin = &GPIO->lpp_gpio_simple[config.CS];
	SCLKPin = &GPIO->lpp_gpio_simple[config.SCLK];

	/* Set the high and low masks (from SCLK initial value)*/
	highMask = *SCLKPin;
	highMask |= CY_U3P_LPP_GPIO_OUT_VALUE;
	lowMask = highMask & ~CY_U3P_LPP_GPIO_OUT_VALUE;

	/* Set the MOSI mask and clear output bit */
	MOSIMask = *MOSIPin;
	MOSIMask &= ~CY_U3P_LPP_GPIO_OUT_VALUE;

	/* Calculate wait value for short half of period */
	finalTime = config.HalfClockDelay + BITBANG_HALFCLOCK_OFFSET;

	/* Drop chip select */
	*CSPin = lowMask;

	/* Wait for CS lead delay */
	cycleTimer = 0;
	while(cycleTimer < config.CSLeadDelay)
	{
		cycleTimer++;
	}

	/* main transmission loop */
	bytePosition = 7;
	byteCounter = 0;
	tempCnt = 0;
	for(bitCounter = 0; bitCounter < BitCount; bitCounter++)
	{
		/* Place output data bit on MOSI pin (approx. 150ns) */
		*MOSIPin = MOSIMask | ((MOSI[byteCounter] >> bytePosition) & 0x1);

		/* Toggle SCLK low */
		*SCLKPin = lowMask;

		/* Wait HalfClock period */
		cycleTimer = 0;
		while(cycleTimer < finalTime)
		{
			cycleTimer++;
		}

		/* Toggle SCLK high */
		*SCLKPin = highMask;

		/* Sample MISO pin - just sampling takes 200ns */
		MISOValue = (*MISOPin & CY_U3P_LPP_GPIO_IN_VALUE) >> 1;
		MISO[byteCounter] |= (MISOValue << bytePosition);

		/* Wait HalfClock period */
		if(config.HalfClockDelay)
		{
			cycleTimer = 0;
			while(cycleTimer < config.HalfClockDelay)
			{
				cycleTimer++;
			}
		}

		/* Update counters (approx. 50ns) */
		tempCnt++;
		byteCounter += (tempCnt >> 3);
		tempCnt &= 0x7;
		bytePosition = 7 - tempCnt;
	}

	/* Wait for CS lag delay */
	cycleTimer = 0;
	while(cycleTimer < config.CSLagDelay)
	{
		cycleTimer++;
	}

	/* Restore CS, SCLK, MOSI to high */
	*CSPin = highMask;
	*SCLKPin = highMask;
	*MOSIPin = highMask;
}

/**
  * @brief This function parses the SPI control registers into an easier to work with config struct.
  *
  * @return The current SPI config, as set in the hardware.
 **/
CyU3PSpiConfig_t AdiGetSpiConfig()
{
	CyU3PSpiConfig_t conf;
	uint32_t CONFIG;

	AdiWaitForSpiNotBusy();

	/* Read SPI config register */
	CONFIG = SPI->lpp_spi_config;

	/* Parse out SPI config */
	conf.wordLen = (CONFIG >> CY_U3P_LPP_SPI_WL_POS) & 0x3F;
	conf.ssnPol = (CONFIG >> 16) & 0x1;
	conf.lagTime = (CONFIG >> CY_U3P_LPP_SPI_LAG_POS) & 0x3;
	conf.leadTime = (CONFIG >> CY_U3P_LPP_SPI_LEAD_POS) & 0x3;
	conf.cpha = (CONFIG >> 11) & 0x1;
	conf.cpol = (CONFIG >> 10) & 0x1;
	conf.ssnCtrl = (CONFIG >> CY_U3P_LPP_SPI_SSNCTRL_POS) & 0x3;
	conf.isLsbFirst = (CONFIG >> 3) & 0x1;

	/* use existing clock setting */
	conf.clock = FX3State.SpiConfig.clock;
	return conf;
}

/**
  * @brief Prints a given SPI config over the UART debug port.
 **/
void AdiPrintSpiConfig(CyU3PSpiConfig_t config)
{
	CyU3PDebugPrint (4, "SPI Clock Frequency: %d\r\n", config.clock);
	CyU3PDebugPrint (4, "SPI Clock Phase: %d\r\n", config.cpha);
	CyU3PDebugPrint (4, "SPI Clock Polarity: %d\r\n", config.cpol);
	CyU3PDebugPrint (4, "SPI LSB First Mode: %d\r\n", config.isLsbFirst);
	CyU3PDebugPrint (4, "SPI CS Lag Time (SCLK periods): %d\r\n", config.lagTime);
	CyU3PDebugPrint (4, "SPI CS Lead Time (SCLK periods): %d\r\n", config.leadTime);
	CyU3PDebugPrint (4, "SPI CS Control Mode: %d\r\n", config.ssnCtrl);
	CyU3PDebugPrint (4, "SPI CS Polarity: %d\r\n", config.ssnPol);
	CyU3PDebugPrint (4, "SPI Word Length: %d\r\n", config.wordLen);
}

/**
  * @brief This function performs a protocol agnostic SPI bi-directional SPI transfer of (1, 2, 4) bytes
  *
  * @param writeData The data to transmit on the MOSI line.
  *
  * @return A status code indicating the success of the function.
  *
  * This function performs a bi-directional SPI transfer, on up to 4 bytes of data. The transfer length is
  * determined by the current SPI config word length setting. The status and data received on the MISO line
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

	/* Set the second byte to 0's */
	tempBuffer[0] = 0;
	/* Set the address to read from */
	tempBuffer[1] = (0x7F) & addr;
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
	tempBuffer[0] = data;
	tempBuffer[1] = 0x80 | addr;
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
  * @brief Sets the SPI controller word length (4 - 32 bits)
  *
  * @param wordLength The number of bits to transfer in a single SPI transaction
  *
  * This function writes directly to the SPI control register. It does not cause the toggle
  * on the chip select line seen using the cypress API for setting the SPI word length.
 **/
void AdiSetSpiWordLength(uint8_t wordLength)
{
	uint32_t spiConf;
	/* Truncate input to 6 bits */
	wordLength &= 0x3F;
	/* Wait for any previous transactions */
	AdiWaitForSpiNotBusy();
	/* Read the SPI configuration register */
	spiConf = SPI->lpp_spi_config;
	/* Clear word length field*/
	spiConf &= ~(0x3F << CY_U3P_LPP_SPI_WL_POS);
	/* Sets bits 17 - 22 to wordlength */
	spiConf |= wordLength << CY_U3P_LPP_SPI_WL_POS;
	/* Write the new config value */
	SPI->lpp_spi_config = spiConf;
}

/**
  * @brief Waits for the SPI controller busy bit to be not set
 **/
void AdiWaitForSpiNotBusy()
{
	while(SPI->lpp_spi_status & 1<<28);
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
	USBBuffer[19] = S_TO_TICKS_MULT & 0xFF;
	USBBuffer[20] = (S_TO_TICKS_MULT & 0xFF00) >> 8;
	USBBuffer[21] = (S_TO_TICKS_MULT & 0xFF0000) >> 16;
	USBBuffer[22] = (S_TO_TICKS_MULT & 0xFF000000) >> 24;
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

	case 14:
		//enable watchdog
		FX3State.WatchDogEnabled = CyTrue;
		FX3State.WatchDogPeriodMs = 1000 * value;
		AdiConfigureWatchdog();
		break;

	case 15:
		//disable watchdog
		FX3State.WatchDogEnabled = CyFalse;
		FX3State.WatchDogPeriodMs = 1000 * value;
		AdiConfigureWatchdog();
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

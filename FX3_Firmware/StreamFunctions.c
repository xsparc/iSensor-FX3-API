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
  * @file		StreamFunctions.c
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		This file contains all functions used to start/stop data streams from a DUT.
 **/

#include "StreamFunctions.h"

/* Tell the compiler where to find the needed globals */
extern CyU3PEvent EventHandler;
extern CyU3PDmaChannel StreamingChannel;
extern CyU3PDmaChannel MemoryToSPI;
extern uint8_t USBBuffer[];
extern uint8_t BulkBuffer[];
extern CyU3PDmaBuffer_t SpiDmaBuffer;
extern BoardState FX3State;
extern volatile CyBool_t KillStreamEarly;
extern StreamState StreamThreadState;

/**
  * @brief This function sets a flag to notify the streaming thread that the user requested to cancel streaming.
  *
  * @return A status code indicating the success of the function.
  *
  * This function can be used to stop any stream operation. The variable KillStreamEarly is defined as
  * volatile to prevent any compiler optimizations.
 **/
CyU3PReturnStatus_t AdiStopAnyDataStream()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	KillStreamEarly = CyTrue;
	//TODO: Check if the stream thread is active and return status based on the thread state
	return status;
}

/**
  * @brief This function prints all the stream state variables to the terminal if VERBOSE_MODE is defined
  *
  * @return A status code indicating if the print occurred (returns false if VERBOSE_MODE is not defined)
  *
  * This function prints a significant amount of data, and as such is fairly slow in VERBOSE_MODE
 **/
CyBool_t AdiPrintStreamState()
{
	CyBool_t verboseMode = CyFalse;

#ifdef VERBOSE_MODE
	verboseMode = CyTrue;
	CyU3PDebugPrint (4, "Endpoint Transfer Size: %d\r\n", StreamThreadState.TransferByteLength);
	CyU3PDebugPrint (4, "NumCaptures: %d NumBuffers: %d Bytes Per USB Packet: %d\r\n", StreamThreadState.NumCaptures, StreamThreadState.NumBuffers, StreamThreadState.BytesPerUsbPacket);
	CyU3PDebugPrint (4, "DrActive is %d, with the data ready pin set to GPIO[%d]. DrPolarity is %d\r\n", FX3State.DrActive, FX3State.DrPin, FX3State.DrPolarity);
	AdiPrintSpiConfig(AdiGetSpiConfig());
#endif

	return verboseMode;
}

/**
  * @brief Starts a protocol agnostic SPI transfer stream.
  *
  * @return A status code indicating the success of the stream start.
  *
  * This is used to implement the ISpi32Interface. The stream info is read in from EP0 into
  * the USBBuffer. This includes stream parameters and the MOSI data.
 **/
CyU3PReturnStatus_t AdiTransferStreamStart()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint16_t bytesRead;

	/* Get the data from the control endpoint */
	status = CyU3PUsbGetEP0Data(StreamThreadState.TransferByteLength, USBBuffer, &bytesRead);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Failed to read configuration data from control endpoint!, error code = 0x%x\r\n", status);
	}

	//Parse control endpoint data. The data is formatted as follows
	//NumCaptures[0-3], NumBuffers[4-7], BytesPerUSBBuffer[8-11], MOSIData.Count()[12-13], MOSIData[14 - ...]

	/* Number of times to transfer the MOSI data list per data ready*/
	StreamThreadState.NumCaptures = USBBuffer[0];
	StreamThreadState.NumCaptures |= (USBBuffer[1] << 8);
	StreamThreadState.NumCaptures |= (USBBuffer[2] << 16);
	StreamThreadState.NumCaptures |= (USBBuffer[3] << 24);

	/* Total number of buffers to transfer (one buffer is going through MOSIData numCaptures times)*/
	StreamThreadState.NumBuffers = USBBuffer[4];
	StreamThreadState.NumBuffers |= (USBBuffer[5] << 8);
	StreamThreadState.NumBuffers |= (USBBuffer[6] << 16);
	StreamThreadState.NumBuffers |= (USBBuffer[7] << 24);

	/* Number of bytes to place in a single USB packet before transmitting */
	StreamThreadState.BytesPerUsbPacket = USBBuffer[8];
	StreamThreadState.BytesPerUsbPacket |= (USBBuffer[9] << 8);
	StreamThreadState.BytesPerUsbPacket |= (USBBuffer[10] << 16);
	StreamThreadState.BytesPerUsbPacket |= (USBBuffer[11] << 24);

	/* This is just the number of bytes in MOSI data */
	StreamThreadState.BytesPerBuffer = USBBuffer[12];
	StreamThreadState.BytesPerBuffer |= (USBBuffer[13] << 8);

	AdiPrintStreamState();

	/* Disable VBUS ISR */
	CyU3PVicDisableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Disable GPIO interrupt before attaching interrupt to pin */
	CyU3PVicDisableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);

	//If using DR triggering configure the selected pin as an input with the correct polarity
	if(FX3State.DrActive)
	{
		//Configure the pin as an input with interrupts enabled on the selected edge
		AdiConfigureDrPin();
	}

	/* Flush the streaming endpoint */
	status = CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Flushing the ADI_STREAMING_ENDPOINT failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Configure the StreamingChannel DMA (SPI to PC) */
	CyU3PDmaChannelConfig_t dmaConfig;
	CyU3PMemSet ((uint8_t *)&dmaConfig, 0, sizeof(dmaConfig));
	dmaConfig.size 				= FX3State.UsbBufferSize;
	dmaConfig.count 			= 16;
	dmaConfig.prodSckId 		= CY_U3P_CPU_SOCKET_PROD;
	dmaConfig.consSckId 		= CY_U3P_UIB_SOCKET_CONS_1;
	dmaConfig.dmaMode 			= CY_U3P_DMA_MODE_BYTE;
	dmaConfig.prodHeader    	= 0;
	dmaConfig.prodFooter    	= 0;
	dmaConfig.consHeader    	= 0;
	dmaConfig.notification  	= 0;
	dmaConfig.cb            	= NULL;
	dmaConfig.prodAvailCount	= 0;

	status = CyU3PDmaChannelCreate(&StreamingChannel, CY_U3P_DMA_TYPE_MANUAL_OUT, &dmaConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Configuring the StreamingChannel DMA for generic stream failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Set DMA transfer mode */
	status = CyU3PDmaChannelSetXfer(&StreamingChannel, 0);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "CyU3PDmaChannelSetXfer failed, Error Code = 0x%x\r\n", status);
		return status;
	}

	//Enable timer interrupts
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status &= (~CY_U3P_LPP_GPIO_INTRMODE_MASK);
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_GPIO_INTR_TIMER_THRES << CY_U3P_LPP_GPIO_INTRMODE_POS;

	//Set the timer pin threshold to correspond with the stall time
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].threshold = (FX3State.StallTime * 10) - ADI_GENERIC_STALL_OFFSET;
	//Set the timer pin period (useful for error case, timer register is manually reset)
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].period = (FX3State.StallTime * 10) - ADI_GENERIC_STALL_OFFSET + 1;

	//Enable generic data capture thread
	status = CyU3PEventSet (&EventHandler, ADI_TRANSFER_STREAM_ENABLE, CYU3P_EVENT_OR);

	return status;
}

/**
  * @brief Cleans up a protocol agnostic transfer stream.
  *
  * @return A status code indicating the success of the function.
  *
  * Resets the streaming endpoint, destroys the DMA channel, and restores the interrupt
  * source states to their standard operating condition. Must be explicitly invoked via a
  * vendor command when all "buffers" or captured, or indirectly via a TransferStream cancel.
 **/
CyU3PReturnStatus_t AdiTransferStreamFinished()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	//TODO: Finish implementing with custom logic if needed
	status = AdiGenericStreamFinished();

	//If DrActive restore the data ready to normal (input, no interrupts) mode

	//Destroy DMA channel

	//Re-enable interrupts

	//Reset timer configuration
	return status;
}

/**
  * @brief Starts a real time stream for ADcmXLx021 DUTs
  *
  * @return The status of starting a real-time stream.
  *
  * This function kicks off a real-time stream by configuring interrupts, SPI, and end points.
  * It also optionally toggles the SYNC/RTS pin if requested. At the end of the function, the
  * bit assigned to enable the capture thread is toggled to signal the streaming thread to start producing data.
 **/
CyU3PReturnStatus_t AdiRealTimeStreamStart()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint16_t bytesRead;
	uint8_t tempWriteBuffer[2];
	uint8_t tempReadBuffer[2];

	//Disable GPIO ISR
	CyU3PVicDisableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);

	//Disable VBUS ISR
	CyU3PVicDisableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	//Clear all interrupt flags
	CyU3PVicClearInt();

	//Make sure the BUSY pin is configured as input (DIO2)
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.outValue = CyFalse;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	gpioConfig.intrMode = CY_U3P_GPIO_INTR_POS_EDGE;
	CyU3PGpioSetSimpleConfig(FX3State.DrPin, &gpioConfig);

	//Get number of frames to capture from control endpoint
	CyU3PUsbGetEP0Data(5, USBBuffer, &bytesRead);
	StreamThreadState.NumRealTimeCaptures = USBBuffer[0];
	StreamThreadState.NumRealTimeCaptures += (USBBuffer[1] << 8);
	StreamThreadState.NumRealTimeCaptures += (USBBuffer[2] << 16);
	StreamThreadState.NumRealTimeCaptures += (USBBuffer[3] << 24);

	//Get pin start setting
	StreamThreadState.PinStartEnable = (CyBool_t) USBBuffer[4];

	//Flush streaming end point
	CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);

	/* Configure RTS channel DMA */
	CyU3PDmaChannelConfig_t dmaConfig;
	CyU3PMemSet ((uint8_t *)&dmaConfig, 0, sizeof(dmaConfig));
	dmaConfig.size 				= FX3State.UsbBufferSize;
	dmaConfig.count 			= 64;
	dmaConfig.prodSckId 		= CY_U3P_LPP_SOCKET_SPI_PROD;
	dmaConfig.consSckId 		= CY_U3P_UIB_SOCKET_CONS_1;
	dmaConfig.dmaMode 			= CY_U3P_DMA_MODE_BYTE;
	dmaConfig.prodHeader    	= 0;
	dmaConfig.prodFooter    	= 0;
	dmaConfig.consHeader    	= 0;
	dmaConfig.notification  	= 0;
	dmaConfig.cb            	= NULL;
	dmaConfig.prodAvailCount	= 0;

    /* Configure DMA for RealTimeStreamingChannel */
    status = CyU3PDmaChannelCreate(&StreamingChannel, CY_U3P_DMA_TYPE_AUTO, &dmaConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Configuring the RTS DMA failed, Error Code = 0x%x\r\n", status);
		return status;
	}

	//Clear the DMA buffers
	CyU3PDmaChannelReset(&StreamingChannel);

	if(StreamThreadState.PinExitEnable)
	{
		//Disable starting the capture by raising SYNC/RTS
		//If this is not done before setting SYNC/RTS high, things will break

		//If pin start is disabled (we're starting the capture with GLOB_CMD)
		if(!StreamThreadState.PinStartEnable)
		{
			//Read MSC_CTRL register
			tempReadBuffer[1] = (0x64);
			tempReadBuffer[0] = (0x00);
			status = CyU3PSpiTransmitWords(tempReadBuffer, 2);
			if (status != CY_U3P_SUCCESS)
			{
						return status;
			}
			AdiSleepForMicroSeconds(FX3State.StallTime);
			status = CyU3PSpiReceiveWords(tempReadBuffer, 2);
			if (status != CY_U3P_SUCCESS)
			{
				return status;
			}
			AdiSleepForMicroSeconds(FX3State.StallTime);

			//Clear bit 12 (bit enables/disables starting a capture using SYNC pin)
			tempReadBuffer[1] = tempReadBuffer[1] & (0xEF);

			//Write modified buffer to MSC_CTRL
			tempWriteBuffer[1] = (0x80) | (0x64);
			tempWriteBuffer[0] = tempReadBuffer[0];
			status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
			if (status != CY_U3P_SUCCESS)
			{
				return status;
			}
			AdiSleepForMicroSeconds(FX3State.StallTime);
			tempWriteBuffer[1] = (0x80) | (0x65);
			tempWriteBuffer[0] = tempReadBuffer[0];
			status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
			if (status != CY_U3P_SUCCESS)
			{
				return status;
			}
			AdiSleepForMicroSeconds(FX3State.StallTime);

			//Configure SYNC/RTS as an output and set high
			CyU3PGpioSimpleConfig_t gpioConfig;
			gpioConfig.outValue = CyTrue;
			gpioConfig.inputEn = CyFalse;
			gpioConfig.driveLowEn = CyTrue;
			gpioConfig.driveHighEn = CyTrue;
			gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
			status = CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);

			//If config fails try to override and reconfigure
			if(status != CY_U3P_SUCCESS)
			{
				status = CyU3PDeviceGpioOverride (FX3State.BusyPin, CyTrue);
				status = CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);
			}

			//Check that the pin is configured to act as an output, return if not
			status = CyU3PGpioSimpleSetValue(FX3State.BusyPin, CyTrue);
			if(status != CY_U3P_SUCCESS)
			{
				return status;
			}
		}
	}

	//If pin start is enabled, set bit 12 in MISC_CTRL and toggle SYNC, otherwise send 0x0800 to COMMAND
	if(StreamThreadState.PinStartEnable)
	{
		//Read MSC_CTRL register
		tempReadBuffer[1] = (0x64);
		tempReadBuffer[0] = (0x00);
		status = CyU3PSpiTransmitWords(tempReadBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
					return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);
		status = CyU3PSpiReceiveWords(tempReadBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
			return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);

		//Set bit 12 (bit enables/disables starting a capture using SYNC pin)
		tempReadBuffer[1] = tempReadBuffer[1] | (0x10);

		//Write modified buffer to MSC_CTRL
		tempWriteBuffer[1] = (0x80) | (0x64);
		tempWriteBuffer[0] = tempReadBuffer[0];
		status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
			return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);
		tempWriteBuffer[1] = (0x80) | (0x65);
		tempWriteBuffer[0] = tempReadBuffer[1];
		status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
			return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);

		//Configure SYNC/RTS as an output and set high
		CyU3PGpioSimpleConfig_t gpioConfig;
		gpioConfig.outValue = CyTrue;
		gpioConfig.inputEn = CyFalse;
		gpioConfig.driveLowEn = CyTrue;
		gpioConfig.driveHighEn = CyTrue;
		gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
		status = CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);

		//If config fails try to override and reconfigure
		if(status != CY_U3P_SUCCESS)
		{
			status = CyU3PDeviceGpioOverride (FX3State.BusyPin, CyTrue);
			status = CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);
		}

		//Check that the pin is configured to act as an output, return if not
		status = CyU3PGpioSimpleSetValue(FX3State.BusyPin, CyTrue);
		if(status != CY_U3P_SUCCESS)
		{
			return status;
		}
	}
	else
	{
		//Send COMMAND 0x800
		//Command is Page 0, Address 62
		tempWriteBuffer[1] = (0x80) | (0x3E);
		tempWriteBuffer[0] = 0;
		status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
			return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);
		tempWriteBuffer[1] = (0x80) | (0x3F);
		tempWriteBuffer[0] = 0x08;
		status = CyU3PSpiTransmitWords(tempWriteBuffer, 2);
		if (status != CY_U3P_SUCCESS)
		{
			return status;
		}
		AdiSleepForMicroSeconds(FX3State.StallTime);
	}

	//Manually reset SPI Rx/Tx FIFO
	AdiSpiResetFifo(CyTrue, CyTrue);

	/* Set the SPI config for streaming mode (8 bit transactions) */
	AdiSetSpiWordLength(8);

	/* Print the stream state */
#ifdef VERBOSE_MODE
	AdiPrintStreamState();
#endif

	//Set infinite DMA transfer on streaming channel
	CyU3PDmaChannelSetXfer(&StreamingChannel, 0);

	//Set the real-time data capture thread flag
	CyU3PEventSet (&EventHandler, ADI_RT_STREAM_ENABLE, CYU3P_EVENT_OR);

	return status;
}

/**
  * @brief This function cleans up resources allocated for a real time stream.
  *
  * @return The status of the cancel operation.
  *
  * This function cleans up after a real-time stream by resetting the SPI port, triggering the
  * SYNC/RTS pin (if asked to do so), and notifying the host that the cancel operation was successful.
 **/
CyU3PReturnStatus_t AdiRealTimeStreamFinished()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	//Pull SYNC/RTS pin low to force x021 out of RT mode
	if(StreamThreadState.PinExitEnable || StreamThreadState.PinStartEnable)
	{
		//Configure SYNC/RTS as an output and set high
		CyU3PGpioSimpleConfig_t gpioConfig;
		gpioConfig.outValue = CyFalse;
		gpioConfig.inputEn = CyFalse;
		gpioConfig.driveLowEn = CyTrue;
		gpioConfig.driveHighEn = CyTrue;
		gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
		CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);

		//Reset flag for next run
		StreamThreadState.PinExitEnable = CyFalse;
	}

	//Stop pin output drive and enable as input
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	CyU3PGpioSetSimpleConfig(FX3State.BusyPin, &gpioConfig);

	//Disable SPI DMA mode
	CyU3PSpiDisableBlockXfer(CyTrue, CyTrue);

	//Reset the SPI controller
	SPI->lpp_spi_config &= ~(CY_U3P_LPP_SPI_RX_ENABLE | CY_U3P_LPP_SPI_TX_ENABLE | CY_U3P_LPP_SPI_DMA_MODE | CY_U3P_LPP_SPI_ENABLE);
	while ((SPI->lpp_spi_config & CY_U3P_LPP_SPI_ENABLE) != 0);

    /* Tear down the RT streaming channel */
    status = CyU3PDmaChannelDestroy(&StreamingChannel);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Tearing down the RTS DMA failed, Error Code = 0x%x\r\n", status);
		return status;
	}

	//Flush streaming end point
	CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);

	//Clear all interrupt flags
	CyU3PVicClearInt();

	//Re-enable relevant ISRs
	CyU3PVicEnableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);
	CyU3PVicEnableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Restore the SPI state */
	status = CyU3PSpiSetConfig(&FX3State.SpiConfig, NULL);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Restoring SPI config after burst stream mode failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	//Additional clean-up after a user requests an early cancellation
	if(KillStreamEarly)
	{
		//Send status back over control endpoint to end USB transaction and signal cancel was completed successfully
		USBBuffer[0] = status & 0xFF;
		USBBuffer[1] = (status & 0xFF00) >> 8;
		USBBuffer[2] = (status & 0xFF0000) >> 16;
		USBBuffer[3] = (status & 0xFF000000) >> 24;
		CyU3PUsbSendEP0Data (4, USBBuffer);

		//Reset KillStreamEarly flag in case the user wants to capture data again
		KillStreamEarly = CyFalse;
	}

	return status;
}

/**
  * @brief Starts a burst stream for IMU products.
  *
  * @return The status of starting a real-time stream.
  *
  * This function kicks off a burst stream by configuring a pin interrupt on a user-specified pin, configuring
  * the SPI and USB DMAs to handle the incoming data, and enabling the streaming function.It can be configured
  * for "Blackfin" or "ADuC" burst using vendor requests.
 **/
CyU3PReturnStatus_t AdiBurstStreamStart()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint16_t bytesRead;

	/* Disable VBUS ISR */
	CyU3PVicDisableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Disable GPIO interrupt before attaching interrupt to pin */
	CyU3PVicDisableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);

	/* Make sure the global data ready pin is configured as an input and attach the interrupt to the correct edge */
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.outValue = CyTrue;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	if (FX3State.DrPolarity)
	{
		gpioConfig.intrMode = CY_U3P_GPIO_INTR_POS_EDGE;
	}
	else
	{
		gpioConfig.intrMode = CY_U3P_GPIO_INTR_NEG_EDGE;
	}
	status = CyU3PGpioSetSimpleConfig(FX3State.DrPin, &gpioConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Setting burst stream pin interrupt failed!, error code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Get the number of buffers, trigger word, and transfer length from the control endpoint */
	CyU3PUsbGetEP0Data(8, USBBuffer, &bytesRead);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Failed to get EP0 data!, error code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}
	StreamThreadState.NumBuffers = USBBuffer[0];
	StreamThreadState.NumBuffers += (USBBuffer[1] << 8);
	StreamThreadState.NumBuffers += (USBBuffer[2] << 16);
	StreamThreadState.NumBuffers += (USBBuffer[3] << 24);

	/* Get number of words to capture per transfer (minus the trigger word) */
	StreamThreadState.TransferWordLength = USBBuffer[6];
	StreamThreadState.TransferWordLength += (USBBuffer[7] << 8);

	/* Calculate the total number of bytes to transfer and add the trigger word */
	/* We're going to overwrite the first two bytes with the trigger word, so make the burst length 2 bytes larger */
	StreamThreadState.TransferByteLength = (StreamThreadState.TransferWordLength * 2) + 2;

	/* Set regList memory to correct length plus trigger word */
	StreamThreadState.RegList = CyU3PDmaBufferAlloc(sizeof(uint8_t) * StreamThreadState.TransferByteLength);

	/* Clear (zero) contents of regList memory. Burst transfers are DNC, so we're sending zeros */
	CyU3PMemSet(StreamThreadState.RegList, 0, sizeof(uint8_t) * StreamThreadState.TransferByteLength);

	/* Append burst trigger word to the first two bytes of regList */
	StreamThreadState.RegList[0] = USBBuffer[4];
	StreamThreadState.RegList[1] = USBBuffer[5];

	/* Calculate the required memory block (in bytes) to be a multiple of 16 */
	uint16_t remainder = StreamThreadState.TransferByteLength % 16;
	if (remainder == 0)
	{
		StreamThreadState.RoundedByteTransferLength = StreamThreadState.TransferByteLength;
	}
	else
	{
		StreamThreadState.RoundedByteTransferLength = StreamThreadState.TransferByteLength + 16 - remainder;
	}

#ifdef VERBOSE_MODE
	 CyU3PDebugPrint (4, "Starting burst stream");
	 CyU3PDebugPrint (4, "burstTriggerUpper:  %d\r\n", StreamThreadState.RegList[0]);
	 CyU3PDebugPrint (4, "burstTriggerLower:  %d\r\n", StreamThreadState.RegList[1]);
	 CyU3PDebugPrint (4, "roundedTransferLength:  %d\r\n", StreamThreadState.RoundedByteTransferLength);
	 CyU3PDebugPrint (4, "transferByteLength:  %d\r\n", StreamThreadState.TransferByteLength);
	 CyU3PDebugPrint (4, "transferWordLength:  %d\r\n", StreamThreadState.TransferWordLength);
	 CyU3PDebugPrint (4, "numBuffers:  %d\r\n", StreamThreadState.NumBuffers);
#endif

	/* Configure the Burst DMA Streaming Channel (SPI to PC) for Auto DMA */
	CyU3PDmaChannelConfig_t dmaConfig;
	CyU3PMemSet ((uint8_t *)&dmaConfig, 0, sizeof(dmaConfig));
	dmaConfig.size 				= FX3State.UsbBufferSize;
	dmaConfig.count 			= 16;
	dmaConfig.prodSckId 		= CY_U3P_LPP_SOCKET_SPI_PROD;
	dmaConfig.consSckId 		= CY_U3P_UIB_SOCKET_CONS_1;
	dmaConfig.dmaMode 			= CY_U3P_DMA_MODE_BYTE;
	dmaConfig.prodHeader    	= 0;
	dmaConfig.prodFooter    	= 0;
	dmaConfig.consHeader    	= 0;
	dmaConfig.notification  	= 0;
	dmaConfig.cb            	= NULL;
	dmaConfig.prodAvailCount	= 0;

	status = CyU3PDmaChannelCreate(&StreamingChannel, CY_U3P_DMA_TYPE_AUTO, &dmaConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Configuring the Burst Stream DMA failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Configure SPI TX DMA (CPU memory to SPI for burst mode)
	 * Transfer length must equal length of message to be sent
	 * Count not required since the DMA will be run in override mode */
	CyU3PMemSet ((uint8_t *)&dmaConfig, 0, sizeof(dmaConfig));
    dmaConfig.size 				= StreamThreadState.RoundedByteTransferLength;
    dmaConfig.count 			= 0;
    dmaConfig.prodSckId 		= CY_U3P_CPU_SOCKET_PROD;
    dmaConfig.consSckId 		= CY_U3P_LPP_SOCKET_SPI_CONS;
    dmaConfig.dmaMode 			= CY_U3P_DMA_MODE_BYTE;
    dmaConfig.prodHeader     	= 0;
    dmaConfig.prodFooter     	= 0;
    dmaConfig.consHeader     	= 0;
    dmaConfig.notification   	= 0;
    dmaConfig.cb             	= NULL;
    dmaConfig.prodAvailCount 	= 0;

    status = CyU3PDmaChannelCreate(&MemoryToSPI, CY_U3P_DMA_TYPE_MANUAL_OUT, &dmaConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Configuring the SPI TX DMA failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Flush the streaming endpoint */
	status = CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Flushing the ADI_STREAMING_ENDPOINT failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Manually reset the SPI Rx/Tx FIFO */
	AdiSpiResetFifo(CyTrue, CyTrue);

	/* Set the SPI config for streaming mode (8 bit transactions) */
	AdiSetSpiWordLength(8);

	/* Configure SpiDmaBuffer and feed it regList*/
	CyU3PMemSet ((uint8_t *)&SpiDmaBuffer, 0, sizeof(SpiDmaBuffer));
	SpiDmaBuffer.count = StreamThreadState.TransferByteLength;
	SpiDmaBuffer.size = StreamThreadState.RoundedByteTransferLength;
	SpiDmaBuffer.buffer = StreamThreadState.RegList;
	SpiDmaBuffer.status = 0;

	/* Enable an infinite DMA transfer on the streaming channel */
	status = CyU3PDmaChannelSetXfer(&StreamingChannel, 0);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Setting CyU3PDmaChannelSetXfer failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Set the burst stream flag to notify the streaming thread it should take over */
	CyU3PEventSet (&EventHandler, ADI_BURST_STREAM_ENABLE, CYU3P_EVENT_OR);

	return status;
}

/**
  * @brief Cleans up resources allocated for a IMU burst stream.
  *
  * @return The status of the cancel operation.
  *
  * This function cleans up after a burst stream by resetting the SPI port and notifying the host
  * that the cancel operation was successful.
 **/
CyU3PReturnStatus_t AdiBurstStreamFinished()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	/* Reset the SPI controller */
	SPI->lpp_spi_config &= ~(CY_U3P_LPP_SPI_RX_ENABLE | CY_U3P_LPP_SPI_TX_ENABLE | CY_U3P_LPP_SPI_DMA_MODE | CY_U3P_LPP_SPI_ENABLE);
	while ((SPI->lpp_spi_config & CY_U3P_LPP_SPI_ENABLE) != 0);

	/* Remove the interrupt from the global data ready pin */
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.outValue = CyTrue;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
	CyU3PGpioSetSimpleConfig(FX3State.DrPin, &gpioConfig);

	/* Destroy MemoryToSpi DMA channel */
    status = CyU3PDmaChannelDestroy(&MemoryToSPI);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Destroying the SPI TX DMA failed, Error Code = 0x%x\r\n", status);
		return status;
	}

    /* Destroy the burst DMA channel (and recover a LOT of memory) */
    status = CyU3PDmaChannelDestroy(&StreamingChannel);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Tearing down the Streaming DMA channel failed, Error Code = 0x%x\r\n", status);
		return status;
	}

	/* Flush the streaming end point */
	CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);

	/* Clear all interrupt flags */
	CyU3PVicClearInt();

	/* Re-enable relevant ISRs */
	CyU3PVicEnableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);
	CyU3PVicEnableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Restore the SPI state */
	AdiSetSpiWordLength(FX3State.SpiConfig.wordLen);

	/* Additional clean-up after a user requests an early cancellation */
	if(KillStreamEarly)
	{
		/* Send status back over control endpoint to end USB transaction and signal cancel was completed successfully */
		USBBuffer[0] = status & 0xFF;
		USBBuffer[1] = (status & 0xFF00) >> 8;
		USBBuffer[2] = (status & 0xFF0000) >> 16;
		USBBuffer[3] = (status & 0xFF000000) >> 24;
		CyU3PUsbSendEP0Data (4, USBBuffer);

		/* Reset KillStreamEarly flag in case the user wants to capture data again */
		KillStreamEarly = CyFalse;
	}

	return status;
}

/**
  * @brief Starts a register read/write stream, with options to trigger on a data ready.
  *
  * @return The status of starting a generic stream.
  *
  * This function kicks off a generic data stream by configuring interrupts, SPI, and end points.
  * At the end of the function, the ADI_GENERIC_STREAM_ENABLE flag is set such that the
  * generic streaming thread knows to start producing data.
 **/
CyU3PReturnStatus_t AdiGenericStreamStart()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	/* Disable VBUS ISR */
	CyU3PVicDisableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Disable GPIO interrupt before attaching interrupt to pin */
	CyU3PVicDisableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);

	if(FX3State.DrActive)
	{
		status = AdiConfigureDrPin();
	}
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Setting generic stream pin interrupt failed!, error code = 0x%x\r\n", status);
	}

	/* Get the number of buffers (number of times to read each set of registers) */
	StreamThreadState.NumBuffers = USBBuffer[0];
	StreamThreadState.NumBuffers += (USBBuffer[1] << 8);
	StreamThreadState.NumBuffers += (USBBuffer[2] << 16);
	StreamThreadState.NumBuffers += (USBBuffer[3] << 24);

	/* Get the number of captures of the address list (number of times to capture the list of registers per buffer) */
	StreamThreadState.NumCaptures = USBBuffer[4];
	StreamThreadState.NumCaptures += (USBBuffer[5] << 8);
	StreamThreadState.NumCaptures += (USBBuffer[6] << 16);
	StreamThreadState.NumCaptures += (USBBuffer[7] << 24);

	/* Calculate the number of bytes per buffer */
	/* Number of times to read each set of registers * (number of registers - control registers) */
	StreamThreadState.BytesPerBuffer = StreamThreadState.NumCaptures * (StreamThreadState.TransferByteLength - 8);

	/* Set the reglist (just use the Bulk buffer - gives defined behavior)*/
	StreamThreadState.RegList = BulkBuffer;

	/* Copy the register list */
	CyU3PMemCopy(StreamThreadState.RegList, USBBuffer + 8, StreamThreadState.TransferByteLength - 8);

	/* Zero the last values */
	StreamThreadState.RegList[StreamThreadState.TransferByteLength - 7] = 0;
	StreamThreadState.RegList[StreamThreadState.TransferByteLength - 8] = 0;

	//Find number of register "buffers" which fit in a USB buffer
	if(StreamThreadState.BytesPerBuffer > FX3State.UsbBufferSize)
	{
		StreamThreadState.BytesPerUsbPacket = FX3State.UsbBufferSize;
	}
	else
	{
		StreamThreadState.BytesPerUsbPacket = ((FX3State.UsbBufferSize / StreamThreadState.BytesPerBuffer) * StreamThreadState.BytesPerBuffer);
	}

	/* Flush the streaming endpoint */
	status = CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Flushing the ADI_STREAMING_ENDPOINT failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Configure the StreamingChannel DMA (SPI to PC) */
	CyU3PDmaChannelConfig_t dmaConfig;
	CyU3PMemSet ((uint8_t *)&dmaConfig, 0, sizeof(dmaConfig));
	dmaConfig.size 				= FX3State.UsbBufferSize;
	dmaConfig.count 			= 16;
	dmaConfig.prodSckId 		= CY_U3P_CPU_SOCKET_PROD;
	dmaConfig.consSckId 		= CY_U3P_UIB_SOCKET_CONS_1;
	dmaConfig.dmaMode 			= CY_U3P_DMA_MODE_BYTE;
	dmaConfig.prodHeader    	= 0;
	dmaConfig.prodFooter    	= 0;
	dmaConfig.consHeader    	= 0;
	dmaConfig.notification  	= 0;
	dmaConfig.cb            	= NULL;
	dmaConfig.prodAvailCount	= 0;

	status = CyU3PDmaChannelCreate(&StreamingChannel, CY_U3P_DMA_TYPE_MANUAL_OUT, &dmaConfig);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Configuring the StreamingChannel DMA for generic stream failed, Error Code = 0x%x\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Set DMA transfer mode */
	status = CyU3PDmaChannelSetXfer(&StreamingChannel, 0);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "CyU3PDmaChannelSetXfer failed, Error Code = 0x%x\r\n", status);
		return status;
	}

	/* Print stream state after all config */
	AdiPrintStreamState();

	//Enable timer interrupts
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status &= (~CY_U3P_LPP_GPIO_INTRMODE_MASK);
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_GPIO_INTR_TIMER_THRES << CY_U3P_LPP_GPIO_INTRMODE_POS;

	//Set the timer pin threshold to correspond with the stall time
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].threshold = (FX3State.StallTime * 10) - ADI_GENERIC_STALL_OFFSET;
	//Set the timer pin period (useful for error case, timer register is manually reset)
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].period = (FX3State.StallTime * 10) - ADI_GENERIC_STALL_OFFSET + 1;

	//Enable generic data capture thread
	status = CyU3PEventSet (&EventHandler, ADI_GENERIC_STREAM_ENABLE, CYU3P_EVENT_OR);

	return status;

}

/**
  * @brief This function cleans up after a generic stream and notifies the host that the cancel operation was successful if requested.
  *
  * @return The status of the cancel operation.
  *
  * This function must be explicitly invoked via a vendor command after the PC has finished reading all data from the FX3. This is done
  * to ensure data consistency and prevent a race condition between the FX3 API and the firmware.
 **/
CyU3PReturnStatus_t AdiGenericStreamFinished()
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

	/* Remove the interrupt from the global data ready pin */
	CyU3PGpioSimpleConfig_t gpioConfig;
	gpioConfig.outValue = CyTrue;
	gpioConfig.inputEn = CyTrue;
	gpioConfig.driveLowEn = CyFalse;
	gpioConfig.driveHighEn = CyFalse;
	gpioConfig.intrMode = CY_U3P_GPIO_NO_INTR;
	CyU3PGpioSetSimpleConfig(FX3State.DrPin, &gpioConfig);

    /* Destroy the StreamingChannel channel (and recover a LOT of memory) */
    status = CyU3PDmaChannelDestroy(&StreamingChannel);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Tearing down the Streaming DMA channel failed, Error Code = 0x%x\r\n", status);
	}

	/* Flush the streaming endpoint */
	CyU3PUsbFlushEp(ADI_STREAMING_ENDPOINT);

	/* Clear all interrupt flags */
	CyU3PVicClearInt();

	/* Re-enable relevant ISRs */
	CyU3PVicEnableInt(CY_U3P_VIC_GPIO_CORE_VECTOR);
	CyU3PVicEnableInt(CY_U3P_VIC_GCTL_PWR_VECTOR);

	/* Additional clean-up after a user requests an early cancellation */
	if(KillStreamEarly)
	{
		/* Send status back over control endpoint to end USB transaction and signal cancel was completed successfully */
		USBBuffer[0] = status & 0xFF;
		USBBuffer[1] = (status & 0xFF00) >> 8;
		USBBuffer[2] = (status & 0xFF0000) >> 16;
		USBBuffer[3] = (status & 0xFF000000) >> 24;
		CyU3PUsbSendEP0Data (4, USBBuffer);

		/* Reset KillStreamEarly flag in case the user wants to capture data again */
		KillStreamEarly = CyFalse;

		//print debug message
		CyU3PDebugPrint (4, "Generic stream terminated early! \r\n");
	}
	return status;
}

/**
  * @brief Configures the data ready pin as an input with edge interrupt triggering enabled.
  *
  * @return The status of the pin configure operation.
  *
  * Configures the data ready pin (FX3State.DrPin) as an input, with edge interrupts enabled
  * based on FX3State.DrPolarity. It is advisable to disable the GPIO interrupt vector before
  * calling this function. If you do not, the GPIO ISR may be triggered.
 **/
CyU3PReturnStatus_t AdiConfigureDrPin()
{
	return AdiConfigurePinInterrupt(FX3State.DrPin, FX3State.DrPolarity);
}

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
  * @file		StreamThread.c
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		This file contains the functions directly associated with StreamThread.
 **/

#include "StreamThread.h"

/* Tell the compiler where to find the needed globals */
extern CyU3PEvent EventHandler;
extern CyU3PDmaChannel StreamingChannel;
extern CyU3PDmaChannel MemoryToSPI;
extern CyU3PDmaBuffer_t SpiDmaBuffer;
extern BoardState FX3State;
extern volatile CyBool_t KillStreamEarly;
extern StreamState StreamThreadState;
extern uint8_t USBBuffer[];

/**
  * @brief The entry point function for the StreamThread. Handles all streaming data captures.
  *
  * @param input Unused input required by the thread manager
  *
  * This function runs in its own thread and handles real-time, burst, generic, and transfer streaming processes.
  * Either type of stream can be kicked off by executing the appropriate set-up routine and then
  * triggering the corresponding event flag.
 **/
void AdiStreamThreadEntry(uint32_t input)
{
	/* Set the event mask to the stream enable events */
	uint32_t eventMask = ADI_GENERIC_STREAM_ENABLE|ADI_RT_STREAM_ENABLE|ADI_BURST_STREAM_ENABLE|ADI_TRANSFER_STREAM_ENABLE;

	/* Variable to receive the event arguments into */
	uint32_t eventFlag;

	for (;;)
	{
		//Wait indefinitely for any flag to be set
		if (CyU3PEventGet (&EventHandler, eventMask, CYU3P_EVENT_OR_CLEAR, &eventFlag, CYU3P_WAIT_FOREVER) == CY_U3P_SUCCESS)
		{
			/* Transfer stream case */
			if(eventFlag & ADI_TRANSFER_STREAM_ENABLE)
			{
				AdiTransferStreamWork();
#ifdef VERBOSE_MODE
				CyU3PDebugPrint (4, "Finished transfer stream work\r\n");
#endif
			}
			/* Generic register stream case */
			else if (eventFlag & ADI_GENERIC_STREAM_ENABLE)
			{
				AdiGenericStreamWork();
#ifdef VERBOSE_MODE
				CyU3PDebugPrint (4, "Finished generic stream work\r\n");
#endif
			}
			/* Real-time (ADcmXL) stream case */
			else if (eventFlag & ADI_RT_STREAM_ENABLE)
			{
				AdiRealTimeStreamWork();
#ifdef VERBOSE_MODE
				CyU3PDebugPrint (4, "Finished real time stream work\r\n");
#endif
			}
			/* Burst stream case */
			else if (eventFlag & ADI_BURST_STREAM_ENABLE)
			{
				AdiBurstStreamWork();
#ifdef VERBOSE_MODE
				CyU3PDebugPrint (4, "Finished burst stream work\r\n");
#endif
			}
			else
			{
				/* Shouldnt be able to get here */
				CyU3PDebugPrint (4, "ERROR: Unhandled StreamThread event generated. eventFlag: 0x%x\r\n", eventFlag);
			}
		}
        /* Allow other ready threads to run. */
        CyU3PThreadRelinquish ();
	}
}

/**
  * @brief This is the worker function for the generic stream.
  *
  * @return A status code representing the success of the generic stream operation.
  *
  * This function performs all the SPI and USB transfers for a single "buffer" of a generic stream.
  * One buffer is considered to be numCapture reads of the register list provided.
 **/
CyU3PReturnStatus_t AdiGenericStreamWork()
{
	uint16_t regIndex, captureCount;
	CyU3PReturnStatus_t status;

	/* Track the current position within the MISO (streaming DMA) buffer*/
	static uint8_t *MISOPtr;

	/* track the current position within the MOSI (reglist) buffer */
	uint8_t * MOSIPtr;

	/* Track the number of buffers read */
	static uint32_t numBuffersRead;

	/* Track the number of bytes read into the current DMA buffer */
	static uint32_t byteCounter;

	/* DMA buffer structure for the active buffer for the streaming DMA channel */
	static CyU3PDmaBuffer_t StreamChannelBuffer;

	/* If the stream channel buffer has not been set, get a new buffer */
	if (MISOPtr == 0)
	{
		status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &StreamChannelBuffer, CYU3P_WAIT_FOREVER);
		if (status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = %d\r\n", status);
		}
		MISOPtr = StreamChannelBuffer.buffer;
	}

	//Wait for DR if enabled
	if (FX3State.DrActive)
	{
		//Clear GPIO interrupts
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
		//Loop until interrupt is triggered
		while(!(GPIO->lpp_gpio_intr0 & (1 << FX3State.DrPin)));
		//Clear GPIO interrupt bit
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
	}

	/* Run through the register list numCaptures times - this is one buffer */
	for(captureCount = 0; captureCount < StreamThreadState.NumCaptures; captureCount++)
	{
		/* Set the MOSI pointer to the bottom of the register list */
		MOSIPtr = StreamThreadState.RegList;

		/* Transmit the first words without reading back */
		CyU3PSpiTransmitWords(MOSIPtr, 2);
		/* Increment the MOSI pointer*/
		MOSIPtr += 2;

		//Set the timer to 0
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;
		//clear interrupt flag
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

		//iterate through the rest of the register list
		for(regIndex = 0; regIndex < (StreamThreadState.TransferByteLength - 8); regIndex += 2)
		{
			//Wait for the complex GPIO timer to reach the stall time
			while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));

			//transfer words
			status = CyU3PSpiTransferWords(MOSIPtr, 2, MISOPtr, 2);

			//Set the pin timer to 0
			GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;
			//clear interrupt flag
			GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

			/* If check if a readback is needed for the last transfer */
			if(regIndex == (StreamThreadState.TransferByteLength - 12))
			{
				/* If the write bit was set skip the read back*/
				if(MOSIPtr[1] & 0x80)
				{
					regIndex += 2;
					MOSIPtr += 2;
					MISOPtr += 2;
					byteCounter += 2;
				}
			}

			//Update counters
			MOSIPtr += 2;
			MISOPtr += 2;
			byteCounter += 2;

			//Check if a transmission is needed
			if (byteCounter >= (StreamThreadState.BytesPerUsbPacket - 1))
			{
				status = CyU3PDmaChannelCommitBuffer (&StreamingChannel, FX3State.UsbBufferSize, 0);
				if (status != CY_U3P_SUCCESS)
				{
					CyU3PDebugPrint (4, "CyU3PDmaChannelCommitBuffer in loop failed, Error code = 0x%x\r\n", status);
				}

				status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &StreamChannelBuffer, CYU3P_WAIT_FOREVER);
				if (status != CY_U3P_SUCCESS)
				{
					CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = 0x%x\r\n", status);
				}
				MISOPtr = StreamChannelBuffer.buffer;
				byteCounter = 0;
			}
		}

		//Wait for the complex GPIO timer to reach the stall time
		while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));

		//Set the pin timer to 0
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;
		//clear interrupt flag
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

	}

	//Check to see if we've captured enough buffers or if we were asked to stop data capture early
	if ((numBuffersRead >= (StreamThreadState.NumBuffers - 1)) || KillStreamEarly)
	{
		//Reset values
		numBuffersRead = 0;
		/* Signal getting a new buffer */
		MISOPtr = 0;
		if (byteCounter)
		{
#ifdef VERBOSE_MODE
			CyU3PDebugPrint (4, "Commiting last USB buffer with %d bytes.\r\n", byteCounter);
#endif
			status = CyU3PDmaChannelCommitBuffer (&StreamingChannel, FX3State.UsbBufferSize, 0);
			if (status != CY_U3P_SUCCESS)
			{
				CyU3PDebugPrint (4, "CyU3PDmaChannelCommitBuffer in loop failed, Error code = 0x%x\r\n", status);
			}
			byteCounter = 0;
		}

		//Clear GPIO interrupts
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;

		//Clear timer interrupt
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

		//update the threshold and period
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].threshold = 0xFFFFFFFF;
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].period = 0xFFFFFFFF;

		//Disable interrupts
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status &= ~(CY_U3P_LPP_GPIO_INTRMODE_MASK);

#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "Exiting stream thread, %d generic stream buffers read.\r\n", numBuffersRead + 1);
#endif

		//Set stream done flag if kill early event was processed (otherwise must be explicitly invoked by FX3 API)
		if(KillStreamEarly)
		{
			CyU3PEventSet (&EventHandler, ADI_GENERIC_STREAM_DONE, CYU3P_EVENT_OR);
		}
	}
	else
	{
		//Increment buffer counter
		numBuffersRead++;
		//Wait for the complex GPIO timer to reach the stall time if no data ready
		if(!FX3State.DrActive)
		{
			while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));
		}
		//Reset flag
		CyU3PEventSet (&EventHandler, ADI_GENERIC_STREAM_ENABLE, CYU3P_EVENT_OR);
	}
	return status;
}

/**
  * @brief This is the worker function for the real time stream.
  *
  * @return A status code representing the success of the real time stream operation.
 **/
CyU3PReturnStatus_t AdiRealTimeStreamWork()
{
	CyU3PReturnStatus_t status;
	CyBool_t interruptTriggered;

	/* Static variables persist through function calls, are initialized to 0*/
	static uint32_t numFramesCaptured;

	//Clear GPIO interrupts
	GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
	//Wait for GPIO interrupt flag to be set and pin to be positive (interrupt configured for positive edge)
	interruptTriggered = CyFalse;
	while(!interruptTriggered)
	{
		interruptTriggered = ((CyBool_t)(GPIO->lpp_gpio_intr0 & (1 << FX3State.DrPin)) && (CyBool_t)(GPIO->lpp_gpio_simple[FX3State.DrPin] & CY_U3P_LPP_GPIO_IN_VALUE));
	}

	//Set the config for DMA mode
	SPI->lpp_spi_config |= CY_U3P_LPP_SPI_DMA_MODE;

	//Set the Tx/Rx count
	SPI->lpp_spi_tx_byte_count = 0;
	SPI->lpp_spi_rx_byte_count = StreamThreadState.BytesPerFrame;

	//Enable Rx and Tx as required
	SPI->lpp_spi_config |= CY_U3P_LPP_SPI_RX_ENABLE;

	//Enable the SPI block
	SPI->lpp_spi_config |= CY_U3P_LPP_SPI_ENABLE;

	//Wait for transfer to finish
	status = CyU3PSpiWaitForBlockXfer(CyTrue);

	if (status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Waiting for the block xfer to finish failed!, error code = %d\r\n", status);
	}

	//Check that we haven't captured the desired number of frames or were asked to kill the thread early
	if((numFramesCaptured >= (StreamThreadState.NumRealTimeCaptures - 1)) || KillStreamEarly)
	{
		//Disable SPI DMA transfer
		status = CyU3PSpiDisableBlockXfer(CyTrue, CyTrue);
		if(status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "Disabling block transfer failed!, error code = %d\r\n", status);
		}
		//Clear GPIO interrupts
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
		//Send whatever is in the buffer over to the PC
		status = CyU3PDmaChannelSetWrapUp(&StreamingChannel);
		if(status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "Wrapping up the streaming DMA channel failed!, error code = %d\r\n", status);
		}

#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "Exiting stream thread, %d real time frames read.\r\n", numFramesCaptured + 1);
#endif

		//Reset frame counter
		numFramesCaptured = 0;

		//Set stream done flag if kill early event was processed (otherwise must be explicitly invoked by FX3 API)
		if(KillStreamEarly)
		{
			CyU3PEventSet(&EventHandler, ADI_RT_STREAM_DONE, CYU3P_EVENT_OR);
		}
	}
	else
	{
		//increment the frame counter
		numFramesCaptured++;
		//Reset real-time data capture thread flag
		CyU3PEventSet (&EventHandler, ADI_RT_STREAM_ENABLE, CYU3P_EVENT_OR);
	}
	return status;
}

/**
  * @brief This is the worker function for the burst stream.
  *
  * @return A status code representing the success of the burst stream operation.
  *
  * This function performs all the SPI and USB transfers for a single burst in IMU or
  * machine health burst mode.
 **/
CyU3PReturnStatus_t AdiBurstStreamWork()
{
	CyU3PReturnStatus_t status;
	CyBool_t interruptTriggered;

	/* Static variables persist through function calls, are initialized to 0*/
	static uint32_t numBuffersRead;

	/* Set up DMA to read registers from CPU memory */
	status = CyU3PDmaChannelSetupSendBuffer(&MemoryToSPI, &SpiDmaBuffer);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Setting up the MemoryToSpi buffer channel failed!, error code = %d\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Wait for DR if enabled */
	if (FX3State.DrActive)
	{
		/* Clear GPIO interrupts */
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
		/* Loop until interrupt is triggered */
		interruptTriggered = CyFalse;
		while(!interruptTriggered)
		{
			interruptTriggered = ((CyBool_t)(GPIO->lpp_gpio_intr0 & (1 << FX3State.DrPin)));
		}
	}

	/* Set the config for DMA mode with RX and TX enabled */
	SPI->lpp_spi_config |= CY_U3P_LPP_SPI_DMA_MODE;

	/* Set the Tx/Rx count */
	SPI->lpp_spi_tx_byte_count = StreamThreadState.TransferByteLength;
	SPI->lpp_spi_rx_byte_count = StreamThreadState.TransferByteLength;

	/* Enable SPI Rx and Tx */
	SPI->lpp_spi_config |= (CY_U3P_LPP_SPI_RX_ENABLE | CY_U3P_LPP_SPI_TX_ENABLE);

	/* Enable the SPI block */
	SPI->lpp_spi_config |= CY_U3P_LPP_SPI_ENABLE;

	/* Wait for SPI transfer to finish */
	status = CyU3PSpiWaitForBlockXfer(CyTrue);
	if(status != CY_U3P_SUCCESS)
	{
		CyU3PDebugPrint (4, "Waiting for the block xfer to finish failed!, error code = %d\r\n", status);
		AdiAppErrorHandler(status);
	}

	/* Check that we haven't captured the desired number of frames or that we were asked to kill the thread early */
	if((numBuffersRead >= (StreamThreadState.NumBuffers - 1)) || KillStreamEarly)
	{
		/* Disable the SPI DMA transfer */
		status = CyU3PSpiDisableBlockXfer(CyTrue, CyTrue);
		if(status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "Disabling block transfer failed!, error code = %d\r\n", status);
			AdiAppErrorHandler(status);
		}

		/* Send whatever is in the buffer over to the PC */
		status = CyU3PDmaChannelSetWrapUp(&StreamingChannel);
		if(status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "Wrapping up the streaming DMA channel failed!, error code = %d\r\n", status);
			AdiAppErrorHandler(status);
		}

#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "Exiting stream thread, %d burst stream buffers read.\r\n", numBuffersRead + 1);
#endif

		/* Clear GPIO interrupts */
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
		/* Reset frame counter */
		numBuffersRead = 0;

		//Set stream done flag if kill early event was processed (otherwise must be explicitly invoked by FX3 API)
		if(KillStreamEarly)
		{
			CyU3PEventSet(&EventHandler, ADI_BURST_STREAM_DONE, CYU3P_EVENT_OR);
		}
	}
	else
	{
		/* Increment the frame counter */
		numBuffersRead++;
		/* Reset the real-time data capture thread flag */
		CyU3PEventSet (&EventHandler, ADI_BURST_STREAM_ENABLE, CYU3P_EVENT_OR);
	}
	return status;
}

/**
  * @brief This is the worker function for the transfer stream.
  *
  * @return A status code representing the success of the transfer stream operation.
 **/
CyU3PReturnStatus_t AdiTransferStreamWork()
{
	/* The MOSI data is stored in USBBuffer[14 ...] prior to this function being called */

	/* Return status code */
	CyU3PReturnStatus_t status;

	/* Track index within the USBBuffer */
	uint16_t MOSIDataCount;

	/* Track current capture count */
	uint16_t captureCount;

	/* Number of bytes per SPI transfer */
	uint32_t bytesPerSpiTransfer;

	/* array to hold the MOSI data */
	uint8_t* MOSIData;

	/* Track the current position within the DMA buffer*/
	static uint8_t *bufPtr;

	/* Track the number of buffers read */
	static uint32_t numBuffersRead;

	/* Track the number of bytes read into the current DMA buffer */
	static uint32_t byteCounter;

	/* DMA buffer structure for the active buffer for the streaming DMA channel */
	static CyU3PDmaBuffer_t StreamChannelBuffer;

	/* If the stream channel buffer has not been set, get a new buffer */
	if (bufPtr == 0)
	{
		/* get the buffer */
		status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &StreamChannelBuffer, CYU3P_WAIT_FOREVER);
		if (status != CY_U3P_SUCCESS)
		{
			CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = %d\r\n", status);
		}
		bufPtr = StreamChannelBuffer.buffer;
#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "Got the first transfer stream DMA buffer, address = 0x%x\r\n", bufPtr);
#endif
	}

	/* Check the number of bytes per SPI transfer */
	bytesPerSpiTransfer = FX3State.SpiConfig.wordLen >> 3;

	//Wait for DR if enabled
	if (FX3State.DrActive)
	{
		//Clear GPIO interrupts
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
		//Loop until interrupt is triggered
		while(!(GPIO->lpp_gpio_intr0 & (1 << FX3State.DrPin)));
		//Clear GPIO interrupt bit
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;
	}

	//Set the pin timer to 0
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;
	//clear interrupt flag
	GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

	for(captureCount = 0; captureCount < StreamThreadState.NumCaptures; captureCount++)
	{
		/* Set the MOSI pointer to the base address of the USB Buffer*/
		MOSIData = USBBuffer;
		/* Increment by 14 so it now points at first MOSI data value */
		MOSIData += 14;
		for(MOSIDataCount = 0; MOSIDataCount < StreamThreadState.BytesPerBuffer; MOSIDataCount += bytesPerSpiTransfer)
		{
			/* Wait for the complex GPIO timer to reach the stall time */
			while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));

			/* Transfer data */
			status = CyU3PSpiTransferWords(MOSIData, bytesPerSpiTransfer, bufPtr, bytesPerSpiTransfer);

			/* Set the pin timer to 0 */
			GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;

			/* clear timer interrupt flag */
			GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

			//Update counters / pointers
			bufPtr += bytesPerSpiTransfer;
			byteCounter += bytesPerSpiTransfer;
			MOSIData += bytesPerSpiTransfer;

			//Check if a transmission is needed
			if (byteCounter >= (StreamThreadState.BytesPerUsbPacket - 1))
			{
#ifdef VERBOSE_MODE
				CyU3PDebugPrint (4, "Transfer steam DMA transmit started. Buffers Read = %d\r\n", numBuffersRead);
#endif
				status = CyU3PDmaChannelCommitBuffer (&StreamingChannel, FX3State.UsbBufferSize, 0);
				if (status != CY_U3P_SUCCESS)
				{
					CyU3PDebugPrint (4, "CyU3PDmaChannelCommitBuffer in loop failed, Error code = 0x%x\r\n", status);
				}

				status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &StreamChannelBuffer, CYU3P_WAIT_FOREVER);
				if (status != CY_U3P_SUCCESS)
				{
					CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = 0x%x\r\n", status);
				}
				bufPtr = StreamChannelBuffer.buffer;
				byteCounter = 0;
			}
		}
	}

	//Check to see if we've captured enough buffers or if we were asked to stop data capture early
	if ((numBuffersRead >= (StreamThreadState.NumBuffers - 1)) || KillStreamEarly)
	{

#ifdef VERBOSE_MODE
		CyU3PDebugPrint (4, "Exiting stream thread, %d transfer stream buffers read.\r\n", numBuffersRead + 1);
#endif

		//Reset values
		numBuffersRead = 0;
		/* Signal getting a new buffer */
		bufPtr = 0;
		if (byteCounter)
		{
			status = CyU3PDmaChannelCommitBuffer (&StreamingChannel, FX3State.UsbBufferSize, 0);
			if (status != CY_U3P_SUCCESS)
			{
				CyU3PDebugPrint (4, "CyU3PDmaChannelCommitBuffer in loop failed, Error code = %d\r\n", status);
			}
			byteCounter = 0;
		}

		//Clear GPIO interrupts
		GPIO->lpp_gpio_simple[FX3State.DrPin] |= CY_U3P_LPP_GPIO_INTR;

		//Clear timer interrupt
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

		//update the threshold and period
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].threshold = 0xFFFFFFFF;
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].period = 0xFFFFFFFF;

		//Disable interrupts
		GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status &= ~(CY_U3P_LPP_GPIO_INTRMODE_MASK);

		//Set stream done flag if kill early event was processed (otherwise must be explicitly invoked by FX3 API)
		if(KillStreamEarly)
		{
			CyU3PEventSet (&EventHandler, ADI_TRANSFER_STREAM_DONE, CYU3P_EVENT_OR);
		}
	}
	else
	{
		//Increment buffer counter
		numBuffersRead++;
		//Wait for the complex GPIO timer to reach the stall time if no data ready
		if(!FX3State.DrActive)
		{
			while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));
		}
		//Reset flag
		CyU3PEventSet (&EventHandler, ADI_TRANSFER_STREAM_ENABLE, CYU3P_EVENT_OR);
	}
	return status;
}



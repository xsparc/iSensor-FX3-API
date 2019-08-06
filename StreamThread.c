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
	uint32_t eventMask = ADI_GENERIC_STREAM_ENABLE|ADI_RT_STREAM_ENABLE|ADI_BURST_STREAM_ENABLE|ADI_TRANSFER_STREAM_ENABLE;
	uint8_t tempData[2];
	uint32_t numBuffersRead = 0;
	uint32_t eventFlag;

	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	uint32_t numFramesCaptured;
	CyBool_t interruptTriggered;
	uint16_t regIndex;

	/* Generic stream local variables */
	CyU3PDmaBuffer_t genBuf_p;
	uint32_t byteCounter = 0;
	uint8_t *tempPtr;
	CyBool_t firstRun = CyTrue;

	for (;;)
	{
		//Wait indefinitely for any flag to be set
		if (CyU3PEventGet (&EventHandler, eventMask, CYU3P_EVENT_OR_CLEAR, &eventFlag, CYU3P_WAIT_FOREVER) == CY_U3P_SUCCESS)
		{
			/* Generic register stream case */
			if (eventFlag & ADI_GENERIC_STREAM_ENABLE)
			{
				if (firstRun)
				{
					status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &genBuf_p, CYU3P_WAIT_FOREVER);
					if (status != CY_U3P_SUCCESS)
					{
						CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = %d\r\n", status);
					}
					tempPtr = genBuf_p.buffer;
					firstRun = CyFalse;
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

				//Transmit first word without reading back
				tempData[0] = StreamThreadState.RegList[0];
				tempData[1] = StreamThreadState.RegList[1];
				CyU3PSpiTransmitWords(tempData, 2);

				//Set the pin timer to 0
				GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;
				//clear interrupt flag
				GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

				//Read the registers in the register list into regList
				for(regIndex = 0; regIndex < (StreamThreadState.BytesPerBuffer - 1); regIndex += 2)
				{
					//Wait for the complex GPIO timer to reach the stall time
					while(!(GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status & CY_U3P_LPP_GPIO_INTR));

					//Prepare, transmit, and receive SPI words
					//TODO: Adjust SPI transactions to work with variable word lengths
					tempData[0] = StreamThreadState.RegList[regIndex + 2];
					tempData[1] = StreamThreadState.RegList[regIndex + 3];

					//tranfer words
					status = CyU3PSpiTransferWords(tempData, 2, tempPtr, 2);

					//Set the pin timer to 0
					GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].timer = 0;

					//clear interrupt flag
					GPIO->lpp_gpio_pin[ADI_TIMER_PIN_INDEX].status |= CY_U3P_LPP_GPIO_INTR;

					//Update counters
					tempPtr += 2;
					byteCounter += 2;

					//Check if a transmission is needed
					if (byteCounter >= (StreamThreadState.BytesPerUsbPacket - 1))
					{
						status = CyU3PDmaChannelCommitBuffer (&StreamingChannel, FX3State.UsbBufferSize, 0);
						if (status != CY_U3P_SUCCESS)
						{
							CyU3PDebugPrint (4, "CyU3PDmaChannelCommitBuffer in loop failed, Error code = 0x%x\r\n", status);
						}

						status = CyU3PDmaChannelGetBuffer (&StreamingChannel, &genBuf_p, CYU3P_WAIT_FOREVER);
						if (status != CY_U3P_SUCCESS)
						{
							CyU3PDebugPrint (4, "CyU3PDmaChannelGetBuffer in generic capture failed, Error code = 0x%x\r\n", status);
						}
						tempPtr = genBuf_p.buffer;
						byteCounter = 0;
					}
				}
				//Check to see if we've captured enough buffers or if we were asked to stop data capture early
				if ((numBuffersRead >= (StreamThreadState.NumBuffers - 1)) || KillStreamEarly)
				{

#ifdef VERBOSE_MODE
					CyU3PDebugPrint (4, "Exiting stream thread, %d generic stream buffers read.\r\n", numBuffersRead + 1);
#endif

					//Reset values
					numBuffersRead = 0;
					firstRun = CyTrue;
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
			}

			/* Real-time (ADcmXL) stream case */
			if (eventFlag & ADI_RT_STREAM_ENABLE)
			{
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
			}

			/* Burst stream case */
			if (eventFlag & ADI_BURST_STREAM_ENABLE)
			{
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

				/*
				 * Note this section of code accomplishes the same as above, only
				 * the statements above circumvent the API and are performed much faster.
				 * The SPI FIFO must be properly cleaned up using AdiSpiResetFifo() before
				 * initiating a transfer.
				status = CyU3PSpiSetBlockXfer(transferByteLength, transferByteLength);
				if(status != CY_U3P_SUCCESS)
				{
					CyU3PDebugPrint (4, "Setting block xfer failed, Error code = %d\r\n", status);
				}
				*/

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
			}
		}
        /* Allow other ready threads to run. */
        CyU3PThreadRelinquish ();
	}
}

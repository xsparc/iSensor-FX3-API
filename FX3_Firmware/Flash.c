/**
  * Copyright (c) Analog Devices Inc, 2018 - 2020
  * All Rights Reserved.
  *
  * THIS SOFTWARE UTILIZES LIBRARIES DEVELOPED
  * AND MAINTAINED BY CYPRESS INC. THE LICENSE INCLUDED IN
  * THIS REPOSITORY DOES NOT EXTEND TO CYPRESS PROPERTY.
  *
  * Use of this file is governed by the license agreement
  * included in this repository.
  *
  * @file		Flash.c
  * @date		04/30/2020
  * @author		A. Nolan (alex.nolan@analog.com)
  * @brief		FX3 flash interfacing module.
 **/

#include "Flash.h"

static uint16_t GetFlashDeviceAddress(uint32_t ByteAddress, CyBool_t isRead);
static void FlashTransfer(uint32_t Address, uint16_t NumBytes, uint8_t* Buf, CyBool_t isRead);

/** I2C Tx DMA channel handle */
static CyU3PDmaChannel flashTxHandle;

/** I2C Rx DMA channel handle */
static CyU3PDmaChannel flashRxHandle;

/**
  * @brief
  *
  * @return Status code indication the success of the flash init operation
 **/
CyU3PReturnStatus_t AdiFlashInit()
{
    CyU3PI2cConfig_t i2cConfig;
    CyU3PDmaChannelConfig_t i2cDmaConfig;
    CyU3PReturnStatus_t status = CY_U3P_SUCCESS;

    /* Initialize and configure the I2C master module. */
    status = CyU3PI2cInit ();
    if (status != CY_U3P_SUCCESS)
    {
        return status;
    }

    /* Start the I2C master block. Set i2c clock of 100KHz, DMA mode */
    CyU3PMemSet ((uint8_t *)&i2cConfig, 0, sizeof(i2cConfig));
    i2cConfig.bitRate    = 100000;
    i2cConfig.busTimeout = 0xFFFFFFFF;
    i2cConfig.dmaTimeout = 0xFFFF;
    i2cConfig.isDma      = CyTrue;

    status = CyU3PI2cSetConfig (&i2cConfig, NULL);
    if (status != CY_U3P_SUCCESS)
    {
#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "Setting I2C configuration failed!\r\n");
#endif
        return status;
    }

    /* Now create the DMA channels required for read and write. */
    CyU3PMemSet ((uint8_t *)&i2cDmaConfig, 0, sizeof(i2cDmaConfig));
    i2cDmaConfig.size           = FLASH_PAGE_SIZE;
    i2cDmaConfig.count          = 0;
    i2cDmaConfig.prodAvailCount = 0;
    i2cDmaConfig.dmaMode        = CY_U3P_DMA_MODE_BYTE;
    i2cDmaConfig.prodHeader     = 0;
    i2cDmaConfig.prodFooter     = 0;
    i2cDmaConfig.consHeader     = 0;
    i2cDmaConfig.notification   = 0;
    i2cDmaConfig.cb             = NULL;

    /* Create a channel to write to the EEPROM. */
    i2cDmaConfig.prodSckId = CY_U3P_CPU_SOCKET_PROD;
    i2cDmaConfig.consSckId = CY_U3P_LPP_SOCKET_I2C_CONS;
    status = CyU3PDmaChannelCreate (&flashTxHandle, CY_U3P_DMA_TYPE_MANUAL_OUT, &i2cDmaConfig);
    if (status != CY_U3P_SUCCESS)
    {
#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "Setting I2C Tx DMA channel failed!\r\n");
#endif
        return status;
    }

    /* Create a channel to read from the EEPROM. */
    i2cDmaConfig.prodSckId = CY_U3P_LPP_SOCKET_I2C_PROD;
    i2cDmaConfig.consSckId = CY_U3P_CPU_SOCKET_CONS;
    status = CyU3PDmaChannelCreate (&flashRxHandle, CY_U3P_DMA_TYPE_MANUAL_IN, &i2cDmaConfig);
    if (status != CY_U3P_SUCCESS)
    {
#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "Setting I2C Rx DMA channel failed!\r\n");
#endif
        return status;
    }

    /* Return status code */
    return status;
}

/**
  * @brief
  *
  * @return void
 **/
void AdiFlashWrite(uint32_t Address, uint16_t NumBytes, uint8_t* WriteBuf)
{
	FlashTransfer(Address, NumBytes, WriteBuf, CyFalse);
}

/**
  * @brief
  *
  * @return void
 **/
void AdiFlashRead(uint32_t Address, uint16_t NumBytes, uint8_t* ReadBuf)
{
	FlashTransfer(Address, NumBytes, ReadBuf, CyTrue);
}

static void FlashTransfer(uint32_t Address, uint16_t NumBytes, uint8_t* Buf, CyBool_t isRead)
{
    CyU3PDmaBuffer_t buf_p;
    CyU3PI2cPreamble_t preamble;

    /* Get device address (upper two address bits encoded into device address) */
    uint16_t device_address = GetFlashDeviceAddress(Address, isRead);

    /* Mask out unused upper address bits */
    Address &= 0xFFFF;

    /* Calculate page count */
    uint16_t pageCount = (NumBytes / FLASH_PAGE_SIZE);

    /* Return for zero transfer */
    if(NumBytes == 0)
    {
        return;
    }

    /* Check if extra bytes which fall onto another page */
    if((NumBytes % FLASH_PAGE_SIZE) != 0)
    {
        pageCount ++;
    }

#ifdef VERBOSE_MODE
    CyU3PDebugPrint (2, "I2C access: address: 0x%x, size: 0x%x, pages: 0x%x read: %d\r\n", Address, NumBytes, pageCount, isRead);
#endif

    /* Update the buffer address and status. */
    buf_p.buffer = Buf;
    buf_p.status = 0;

    while (pageCount != 0)
    {
    	if(isRead)
    	{
            /* Update the preamble information. */
            preamble.length    = 4;
            preamble.buffer[0] = device_address;
            preamble.buffer[1] = (uint8_t)(Address >> 8);
            preamble.buffer[2] = (uint8_t)(Address & 0xFF);
            preamble.buffer[3] = (device_address | 0x01);
            preamble.ctrlMask  = 0x0004;

            buf_p.size = FLASH_PAGE_SIZE;
            buf_p.count = FLASH_PAGE_SIZE;

            /* Send read command */
            CyU3PI2cSendCommand (&preamble, FLASH_PAGE_SIZE, CyTrue);
            /* Set up DMA to receive read data */
            CyU3PDmaChannelSetupRecvBuffer (&flashRxHandle, &buf_p);
            /* Wait for finish */
            CyU3PDmaChannelWaitForCompletion(&flashRxHandle, FLASH_TIMEOUT_MS);
    	}
    	else
    	{
            /* Update the preamble information. */
            preamble.length    = 3;
            preamble.buffer[0] = device_address;
            preamble.buffer[1] = (uint8_t)(Address >> 8);
            preamble.buffer[2] = (uint8_t)(Address & 0xFF);
            preamble.ctrlMask  = 0x0000;

            buf_p.size = FLASH_PAGE_SIZE;
            buf_p.count = FLASH_PAGE_SIZE;

            /* Setup DMA transmit buffer */
            CyU3PDmaChannelSetupSendBuffer (&flashTxHandle, &buf_p);
            /* Send write command */
            CyU3PI2cSendCommand (&preamble, FLASH_PAGE_SIZE, CyFalse);
            /* Wait for completion */
            CyU3PDmaChannelWaitForCompletion(&flashTxHandle,FLASH_TIMEOUT_MS);

            /* Stall for 10ms */
            CyU3PThreadSleep(10);
    	}
        /* decrement page count */
        pageCount --;
    }
}

static uint16_t GetFlashDeviceAddress(uint32_t ByteAddress, CyBool_t isRead)
{
	uint16_t address = 0xA0;
	/* mask out all but bits 17 - 16 in the byte address and shift down (leave one bit up) */
	ByteAddress &= 0x30000;
	ByteAddress = ByteAddress >> 15;
	address |= ByteAddress;
	if(isRead)
		address |= 1;

	return address;
}

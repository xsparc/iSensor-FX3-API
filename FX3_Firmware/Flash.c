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

static uint16_t GetFlashDeviceAddress(uint32_t ByteAddress);
static CyU3PReturnStatus_t FlashTransfer(uint32_t Address, uint16_t NumBytes, uint8_t* Buf, CyBool_t isRead);

/** Global USB Buffer (Control Endpoint) */
extern uint8_t USBBuffer[4096];

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
    status = CyU3PI2cInit();
    if (status != CY_U3P_SUCCESS)
    {
#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "I2C init failed! 0x%x\r\n", status);
#endif
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
    	CyU3PDebugPrint (4, "Setting I2C configuration failed! 0x%x\r\n", status);
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
    status = CyU3PDmaChannelCreate(&flashTxHandle, CY_U3P_DMA_TYPE_MANUAL_OUT, &i2cDmaConfig);
    if (status != CY_U3P_SUCCESS)
    {
#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "Setting I2C Tx DMA channel failed! 0x%x\r\n", status);
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
    	CyU3PDebugPrint (4, "Setting I2C Rx DMA channel failed! 0x%x\r\n", status);
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
void AdiFlashDeInit()
{
	CyU3PI2cDeInit();
	CyU3PDmaChannelDestroy(&flashTxHandle);
	CyU3PDmaChannelDestroy(&flashRxHandle);
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

/**
  * @brief Handles flash read requests from control endpoint
  *
  * @param Address The byte address in flash to start reading at
  *
  * @param NumBytes The number of bytes to read. Max 4KB
  *
  * @return void
  *
  * The data read from flash is returned over the control endpoint.
  * This limits a single read to 4KB. If greater than a 4KB read
  * is needed, multiple transactions should be sent.
 **/
void AdiFlashReadHandler(uint32_t Address, uint16_t NumBytes)
{
	/* Perform transfer */
	FlashTransfer(Address, NumBytes, USBBuffer, CyTrue);
	/* Return over USB control endpoint */
	CyU3PUsbSendEP0Data(NumBytes, USBBuffer);
}

/**
  * @brief
  *
  * @return void
 **/
static CyU3PReturnStatus_t FlashTransfer(uint32_t Address, uint16_t NumBytes, uint8_t* Buf, CyBool_t isRead)
{
    CyU3PDmaBuffer_t buf_p;
    CyU3PI2cPreamble_t preamble;
    CyU3PReturnStatus_t status;

    uint16_t dmaCount;
    uint16_t lastCount;

    /* device address (upper two address bits encoded into device address) */
    uint16_t device_address;

    /* Calculate page count */
    uint16_t pageCount = (NumBytes / FLASH_PAGE_SIZE);

    /* Return for zero transfer */
    if(NumBytes == 0)
        return CY_U3P_SUCCESS;

    /* Check if extra bytes which fall onto another page */
    lastCount = (NumBytes % FLASH_PAGE_SIZE);
    if(lastCount != 0)
        pageCount ++;
    else
    	lastCount = FLASH_PAGE_SIZE;

    /* Init flash */
    AdiFlashInit();

    /* Update the buffer status. */
    buf_p.status = 0;
	/* Update buffer address */
	buf_p.buffer = Buf;

    while (pageCount != 0)
    {
    	/* Get device addr */
    	device_address = GetFlashDeviceAddress(Address);
    	/* Get transfer count */
    	if(pageCount > 1)
    		dmaCount = FLASH_PAGE_SIZE;
    	else
    		dmaCount = lastCount;

#ifdef VERBOSE_MODE
    	CyU3PDebugPrint (4, "I2C access: Dev addr: 0x%x Byte Addr: 0x%x, size: 0x%x, pages: 0x%x read: %d\r\n", device_address, Address, dmaCount, pageCount, isRead);
#endif

    	if(isRead)
    	{
            /* Update the preamble information. */
            preamble.length    = 4;
            preamble.buffer[0] = device_address;
            preamble.buffer[1] = (uint8_t)((Address & 0xFF00) >> 8);
            preamble.buffer[2] = (uint8_t)(Address & 0xFF);
            preamble.buffer[3] = (device_address | 0x01);
            preamble.ctrlMask  = 0x0004;

            buf_p.size = FLASH_PAGE_SIZE;
            buf_p.count = FLASH_PAGE_SIZE;

            /* Send read command */
            status = CyU3PI2cSendCommand (&preamble, dmaCount, CyTrue);
#ifdef VERBOSE_MODE
            if(status != CY_U3P_SUCCESS)
            	CyU3PDebugPrint (4, "I2C send read command failed: 0x%x\r\n", status);
#endif
            /* Set up DMA to receive read data */
            status = CyU3PDmaChannelSetupRecvBuffer (&flashRxHandle, &buf_p);
#ifdef VERBOSE_MODE
            if(status != CY_U3P_SUCCESS)
            	CyU3PDebugPrint (4, "I2C DMA Rx channel setup failed: 0x%x\r\n", status);
#endif
    	}
    	else
    	{
            /* Update the preamble information. */
            preamble.length    = 3;
            preamble.buffer[0] = device_address;
            preamble.buffer[1] = (uint8_t)((Address & 0xFF00) >> 8);
            preamble.buffer[2] = (uint8_t)(Address & 0xFF);
            preamble.ctrlMask  = 0x0000;

            buf_p.size = FLASH_PAGE_SIZE;
            buf_p.count = dmaCount;

            /* Setup DMA transmit buffer */
            status = CyU3PDmaChannelSetupSendBuffer (&flashTxHandle, &buf_p);
#ifdef VERBOSE_MODE
            if(status != CY_U3P_SUCCESS)
            	CyU3PDebugPrint (4, "I2C DMA Tx channel setup failed: 0x%x\r\n", status);
#endif
            /* Send write command */
            status = CyU3PI2cSendCommand (&preamble, dmaCount, CyFalse);
#ifdef VERBOSE_MODE
            if(status != CY_U3P_SUCCESS)
            	CyU3PDebugPrint (4, "I2C send write command failed: 0x%x\r\n", status);
#endif
    	}
        /* Stall for 20ms */
        CyU3PThreadSleep(20);
        /* Wait for finish */
        status = CyU3PI2cWaitForBlockXfer(isRead);
#ifdef VERBOSE_MODE
        if(status != CY_U3P_SUCCESS)
        	CyU3PDebugPrint (4, "I2C DMA wait for completion failed: 0x%x\r\n", status);
#endif

        /* decrement page count */
        pageCount --;
        /* Increment address */
        Address += dmaCount;
        buf_p.buffer += dmaCount;
    }

#ifdef VERBOSE_MODE
    CyU3PDebugPrint (4, "Flash transfer complete!\r\n", status);
#endif

    /* De-Init flash */
    AdiFlashDeInit();

    /* Return the status code */
    return status;
}

/**
  * @brief
  *
  * @return void
 **/
static uint16_t GetFlashDeviceAddress(uint32_t ByteAddress)
{
	uint16_t address = 0xA0;
	/* mask out all but bits 17 - 16 in the byte address and shift down (leave one bit up) */
	ByteAddress = ByteAddress >> 15;
	ByteAddress &= 0x6;
	address |= ByteAddress;
	return address;
}

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
  * @file		ErrorLog.h
  * @date		12/6/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @brief 		Implementation file for error logging capabilities
 **/

#include "ErrorLog.h"

/* Tell the compiler where to find the needed globals */
extern BoardState FX3State;
extern uint8_t FirmwareID;

void AdiLogError(FileIdentifier File, uint32_t Line, uint32_t ErrorCode)
{
	ErrorMsg error;

	/* Set the file code */
	error.File = File;

	/* Set the line */
	error.Line = Line;

	/* Set the error code */
	error.ErrorCode = ErrorCode;

	/* Set the boot time */
	error.BootTimeCode = FX3State.BootTime;

	/* Set the firmware version */
	FindFirmwareVersion(error.FirmwareVersion);

	/* Print to debug */
	WriteLogToDebug(&error);

	/* Store to flash */
	WriteLogToFlash(&error);
}

void FindFirmwareVersion(uint8_t * outBuf)
{

}

void WriteLogToFlash(ErrorMsg * msg)
{
	uint32_t logAddr, logCount;

	/* Get the starting address of the next record based on the number of logs stored */
	logAddr = GetNewLogAddress(&logCount);

	/* Transfer log to flash */
	FlashWriteBytes((uint8_t) msg, sizeof(ErrorMsg), logAddr);

	/* Increment log count and store back to flash */
	logCount++;
	WriteLogCount(logCount);
}

void WriteLogToDebug(ErrorMsg * msg)
{

}

uint32_t GetNewLogAddress(uint32_t * TotalLogCount)
{
	/* Get the total lifetime log count from flash */
	*TotalLogCount = GetLogCount();

	/* Clear upper bits (over 4096) of log count to find number actually stored */
	uint32_t logFlashCount = *TotalLogCount & 0x1FFF;

	/* 32 bytes per log */
	uint32_t addr = logFlashCount * 32;

	/* Add offset */
	addr += LOG_BASE_ADDR;

	return addr;
}

uint32_t GetLogCount()
{
	uint8_t buf[4];
	uint32_t count;
	FlashReadBytes(buf, 4, LOG_COUNT_ADDR);
	/* Values are stored little endian in flash */
	count = buf[0];
	count |= (buf[1] << 8);
	count |= (buf[2] << 16);
	count |= (buf[3] << 24);
	return count;
}

void WriteLogCount(uint32_t count)
{

}

CyU3PReturnStatus_t FlashWriteBytes(uint8_t * writeData, uint32_t numBytes, uint32_t startAddr)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	if(startAddr >= FLASH_SIZE)
		return CY_U3P_ERROR_INVALID_ADDR;

	if(numBytes == 0)
		return CY_U3P_ERROR_BAD_ARGUMENT;


	return status;

}

CyU3PReturnStatus_t FlashReadBytes(uint8_t * outBuf, uint32_t numBytes, uint32_t startAddr)
{
	CyU3PReturnStatus_t status = CY_U3P_SUCCESS;
	if(startAddr >= FLASH_SIZE)
		return CY_U3P_ERROR_INVALID_ADDR;

	if(numBytes == 0)
		return CY_U3P_ERROR_BAD_ARGUMENT;

	return status;
}

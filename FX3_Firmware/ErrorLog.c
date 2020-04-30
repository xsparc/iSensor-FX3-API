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
extern uint8_t FirmwareID[32];

/* Helper functions */
static void FindFirmwareVersion(uint8_t* buf);
static void WriteLogToFlash(ErrorMsg* msg);
static void WriteLogToDebug(ErrorMsg* msg);
static uint32_t GetNewLogAddress();
static uint32_t GetLogCount();
static void WriteLogCount(uint32_t count);

/**
  * @brief
  *
  * @return void
 **/
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

/**
  * @brief
  *
  * @return void
 **/
static void FindFirmwareVersion(uint8_t* outBuf)
{
	uint32_t offset = 12;
	for(int i = 0; i < 8; i++)
	{
		outBuf[i] = FirmwareID[offset + i];
	}
}

/**
  * @brief
  *
  * @return void
 **/
static void WriteLogToFlash(ErrorMsg* msg)
{
	uint32_t logAddr, logCount;

	/* Get the starting address of the next record based on the number of logs stored */
	logAddr = GetNewLogAddress(&logCount);

	/* Transfer log to flash */
	AdiFlashWrite(logAddr, 32, (uint8_t*) msg);

	/* Increment log count and store back to flash */
	logCount++;
	WriteLogCount(logCount);
}

/**
  * @brief
  *
  * @return void
 **/
static void WriteLogToDebug(ErrorMsg* msg)
{
	CyU3PDebugPrint (4, "Error occurred on line %d of file %d. Error code: 0x%x\r\n", msg->Line, msg->File, msg->ErrorCode);
}

/**
  * @brief
  *
  * @return void
 **/
static uint32_t GetNewLogAddress(uint32_t* TotalLogCount)
{
	/* Get the total lifetime log count from flash */
	uint32_t count = GetLogCount();

	/* Clear upper bits (over 4095) of log count to find number actually stored */
	uint32_t logFlashCount = count & LOG_CAPACITY;

	/* 32 bytes per log */
	uint32_t addr = logFlashCount * 32;

	/* Add offset */
	addr += LOG_BASE_ADDR;

	/* Return total count by reference */
	*TotalLogCount = count;

	/* Return address to write the new log to */
	return addr;
}

/**
  * @brief
  *
  * @return void
 **/
static uint32_t GetLogCount()
{
	uint8_t buf[4];
	uint32_t count;
	AdiFlashRead(LOG_COUNT_ADDR, 4, buf);
	/* Count values are stored little endian in flash */
	count = buf[0];
	count |= (buf[1] << 8);
	count |= (buf[2] << 16);
	count |= (buf[3] << 24);
	return count;
}

/**
  * @brief
  *
  * @return void
 **/
static void WriteLogCount(uint32_t count)
{
	uint8_t writeData[4];
	writeData[0] = count & 0xFF;
	writeData[1] = (count & 0xFF00) >> 8;
	writeData[2] = (count & 0xFF0000) >> 16;
	writeData[3] = (count & 0xFF000000) >> 24;
	AdiFlashWrite(LOG_COUNT_ADDR, 4, writeData);
}

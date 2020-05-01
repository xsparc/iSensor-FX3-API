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

/** Error log buffer */
uint8_t LogBuffer[FLASH_PAGE_SIZE];

/* Helper functions */
static void FindFirmwareVersion(uint8_t* buf);
static void WriteLogToFlash(ErrorMsg* msg);
static void WriteLogToDebug(ErrorMsg* msg);
static uint32_t GetNewLogAddress();
static uint32_t GetLogCount();

/**
  * @brief
  *
  * @return void
 **/
void AdiLogError(FileIdentifier File, uint32_t Line, uint32_t ErrorCode)
{
	ErrorMsg error = {};

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
void WriteErrorLogCount(uint32_t count)
{
	LogBuffer[0] = count & 0xFF;
	LogBuffer[1] = (count & 0xFF00) >> 8;
	LogBuffer[2] = (count & 0xFF0000) >> 16;
	LogBuffer[3] = (count & 0xFF000000) >> 24;
	AdiFlashWrite(LOG_COUNT_ADDR, 4, LogBuffer);
}

/**
  * @brief
  *
  * @return void
 **/
static void FindFirmwareVersion(uint8_t* outBuf)
{
	uint32_t offset = 12;
	for(int i = 0; i < 12; i++)
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
	uint8_t* memPtr;

	/* Copy the error message to the Log Buffer */
	memPtr = (uint8_t *) msg;
	for(int i = 0; i < 32; i++)
	{
		LogBuffer[i] = memPtr[i];
		CyU3PDebugPrint (4, "i: %d: 0x%x\r\n", i, LogBuffer[i]);
	}

	/* Get the starting address of the next record based on the number of logs stored */
	logAddr = GetNewLogAddress(&logCount);

	/* Transfer log to flash */
	AdiFlashWrite(logAddr, 32, LogBuffer);

	/* Increment log count and store back to flash */
	logCount++;
	WriteErrorLogCount(logCount);
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

#ifdef VERBOSE_MODE
	CyU3PDebugPrint (4, "Error log count: 0x%x\r\n", count);
#endif

	/* Find location of "front" */
	uint32_t logFlashCount = count % LOG_CAPACITY;

	/* 32 bytes per log */
	uint32_t addr = logFlashCount * 32;

	/* Add offset */
	addr += LOG_BASE_ADDR;

	/* Return total count by reference */
	*TotalLogCount = count;

#ifdef VERBOSE_MODE
	CyU3PDebugPrint (4, "New Log Address: 0x%x\r\n", addr);
#endif

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
	uint32_t count;

	/* Perform DMA flash read (4 bytes) */
	AdiFlashRead(LOG_COUNT_ADDR, 4, LogBuffer);

	/* Count values are stored little endian in flash */
	count = LogBuffer[0];
	count |= (LogBuffer[1] << 8);
	count |= (LogBuffer[2] << 16);
	count |= (LogBuffer[3] << 24);

	/* Handle un-initialized log */
	if(count == 0xFFFFFFFF)
	{
		count = 0;
	}
	return count;
}

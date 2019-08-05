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
  * @file		AppThread.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		Header file for all the primary application thread functions.
 **/

#ifndef APP_THREAD_H
#define APP_THREAD_H

/* Include the main header file */
#include "main.h"

void AdiAppThreadEntry(uint32_t input);
void AdiDebugInit(void);
void AdiAppInit(void);

// App thread stack size
#define APPTHREAD_STACK       					(0x0800)

// App thread priority
#define APPTHREAD_PRIORITY    						(8)

/*
 * ADI Event Handler Definitions
 */
#define ADI_RT_STREAMING_START					(1 << 0)
#define ADI_RT_STREAMING_DONE					(1 << 1)
#define ADI_RT_STREAMING_STOP					(1 << 2)
#define ADI_GENERIC_STREAMING_START				(1 << 3)
#define ADI_GENERIC_STREAMING_STOP				(1 << 4)
#define ADI_GENERIC_STREAMING_DONE				(1 << 5)
#define ADI_GENERIC_STREAM_ENABLE				(1 << 6)
#define ADI_REAL_TIME_STREAM_ENABLE				(1 << 7)
#define ADI_KILL_THREAD_EARLY					(1 << 8)	//Currently unused.
#define ADI_BURST_STREAMING_START				(1 << 9)
#define ADI_BURST_STREAMING_STOP				(1 << 10)
#define ADI_BURST_STREAMING_DONE				(1 << 11)
#define ADI_BURST_STREAM_ENABLE					(1 << 12)

#endif

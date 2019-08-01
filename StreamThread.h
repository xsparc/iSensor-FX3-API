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
  * @file		StreamThread.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief
 **/

#ifndef STREAM_THREAD_H
#define STREAM_THREAD_H

/* Include the main header file */
#include "main.h"

void AdiStreamThreadEntry(uint32_t input);

// Real time thread stack size
#define STREAMTHREAD_STACK					(0x0800)

// Real time thread priority
#define STREAMTHREAD_PRIORITY					(8)

#endif

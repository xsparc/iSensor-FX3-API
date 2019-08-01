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
  * @file		StreamFunctions.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief
 **/

#ifndef STREAM_FUNCTIONS_H
#define STREAM_FUNCTIONS_H

/* Include the main header file */
#include "main.h"

//Real-time data stream functions.
CyU3PReturnStatus_t AdiRealTimeStreamStart();
CyU3PReturnStatus_t AdiRealTimeStreamFinished();

//Generic data stream functions.
CyU3PReturnStatus_t AdiGenericStreamStart();
CyU3PReturnStatus_t AdiGenericStreamFinished();

//Transfer stream functions
CyU3PReturnStatus_t AdiTransferStreamStart();
CyU3PReturnStatus_t AdiTransferStreamFinish();

//Burst stream functions.
CyU3PReturnStatus_t AdiBurstStreamStart();
CyU3PReturnStatus_t AdiBurstStreamFinished();

//General stream functions.
CyU3PReturnStatus_t AdiStopAnyDataStream();

/*
 * Stream action commands
 */
#define ADI_STREAM_DONE_CMD						0
#define ADI_STREAM_START_CMD					1
#define ADI_STREAM_STOP_CMD						2

#endif

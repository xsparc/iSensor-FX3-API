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
  * @file		SpiFunctions.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		Header file for all SPI related functions.
 **/

#ifndef SPI_FUNCTIONS_H
#define SPI_FUNCTIONS_H

/* Include the main header file */
#include "main.h"

/* SPI configuration functions */
CyU3PReturnStatus_t AdiGetSpiSettings();
CyBool_t AdiSpiUpdate(uint16_t index, uint16_t value, uint16_t length);
CyU3PReturnStatus_t AdiSpiResetFifo(CyBool_t isTx, CyBool_t isRx);
void AdiWaitForSpiNotBusy();
void AdiSetSpiWordLength(uint8_t wordLength);

/* SPI data transfer functions */
CyU3PReturnStatus_t AdiTransferBytes(uint32_t writeData);
CyU3PReturnStatus_t AdiWriteRegByte(uint16_t addr, uint8_t data);
CyU3PReturnStatus_t AdiReadRegBytes(uint16_t addr);
CyU3PReturnStatus_t AdiBulkByteTransfer(uint16_t numBytes, uint16_t bytesPerCapture);

#endif

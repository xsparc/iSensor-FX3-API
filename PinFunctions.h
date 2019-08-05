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
  * @file		PinFunctions.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief		Header file for all the pin I/O and timing related functions
 **/

#ifndef PIN_FUNCTIONS_H
#define PIN_FUNCTIONS_H

/* Include the main header file */
#include "main.h"

CyU3PReturnStatus_t AdiPulseDrive();
CyU3PReturnStatus_t AdiPulseWait(uint16_t transferLength);
CyU3PReturnStatus_t AdiSetPin(uint16_t pinNumber, CyBool_t polarity);
CyU3PReturnStatus_t AdiMeasureDR();
CyU3PReturnStatus_t AdiWaitForPin(uint32_t pinNumber, CyU3PGpioIntrMode_t interruptSetting, uint32_t timeoutTicks);
CyU3PReturnStatus_t AdiPinRead(uint16_t pin);
CyU3PReturnStatus_t AdiReadTimerValue();
uint32_t AdiMStoTicks(uint32_t desiredStallTime);
CyU3PReturnStatus_t AdiSleepForMicroSeconds(uint32_t numMicroSeconds);
CyU3PReturnStatus_t AdiConfigurePWM(CyBool_t EnablePWM);
CyU3PReturnStatus_t AdiMeasureBusyPulse(uint16_t transferLength);

/*
 * GPIO Pin mapping definitions
 */
//GPIO pins used on the FX3 evaluation board (sanitized for iSensor use)
#define ADI_PIN_RESET							(0x0)	// Wired to the hardware reset on most iSensor products
#define ADI_PIN_DIO1							(0x4)	// Commonly data ready on IMUs
#define ADI_PIN_DIO2							(0x3)	// Commonly BUSY on ADcmXL devices
#define ADI_PIN_DIO3							(0x2)
#define ADI_PIN_DIO4							(0x1)
#define FX3_PIN_GPIO1							(0x5)	// Misc pin(s) used for triggering from test equipment
#define FX3_PIN_GPIO2							(0x6)	// Misc pin
#define FX3_PIN_GPIO3							(0x7)	// Misc pin
#define FX3_PIN_GPIO4							(0x12)	// Misc pin (shared complex block with DIO1, typically a data ready pin)

/*
 * ADI GPIO Event Handler Definitions
 */
#define ADI_DIO1_INTERRUPT_FLAG					(1 << 0)
#define ADI_DIO2_INTERRUPT_FLAG					(1 << 1)
#define ADI_DIO3_INTERRUPT_FLAG					(1 << 2)
#define ADI_DIO4_INTERRUPT_FLAG					(1 << 3)
#define ADI_DIO5_INTERRUPT_FLAG					(1 << 4)
#define ADI_DIO6_INTERRUPT_FLAG					(1 << 5)
#define ADI_DIO7_INTERRUPT_FLAG					(1 << 6)

#endif

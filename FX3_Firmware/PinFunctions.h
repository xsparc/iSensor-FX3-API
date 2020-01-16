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

/** Enum of possible DUT voltages */
typedef enum DutVoltage
{
	Off = 0,
	On3_3Volts = 1,
	On5_0Volts = 2
}DutVoltage;

/* Function definitions */
CyU3PReturnStatus_t AdiPulseDrive();
CyU3PReturnStatus_t AdiPulseWait(uint16_t transferLength);
CyU3PReturnStatus_t AdiSetPin(uint16_t pinNumber, CyBool_t polarity);
CyU3PReturnStatus_t AdiMeasurePinFreq();
CyU3PReturnStatus_t AdiWaitForPin(uint32_t pinNumber, CyU3PGpioIntrMode_t interruptSetting, uint32_t timeoutTicks);
CyU3PReturnStatus_t AdiPinRead(uint16_t pin);
CyU3PReturnStatus_t AdiReadTimerValue();
uint32_t AdiMStoTicks(uint32_t desiredStallTime);
CyU3PReturnStatus_t AdiSleepForMicroSeconds(uint32_t numMicroSeconds);
CyU3PReturnStatus_t AdiConfigurePWM(CyBool_t EnablePWM);
CyU3PReturnStatus_t AdiMeasureBusyPulse(uint16_t transferLength);
CyU3PReturnStatus_t AdiSetDutSupply(DutVoltage SupplyMode);
CyU3PReturnStatus_t AdiConfigurePinInterrupt(uint16_t pin, CyBool_t polarity);
uint32_t AdiReadTimerRegValue();
CyU3PReturnStatus_t AdiMeasurePinDelay(uint16_t transferLength);
CyBool_t AdiIsValidGPIO(uint16_t GpioId);
void AdiReturnBulkEndpointData(CyU3PReturnStatus_t status, uint16_t length);

/*
 * GPIO Pin mapping definitions
 */

/** Control pins for power management circuit. 3.3V enable pin */
#define ADI_3_3V_EN								(33)

/** Control pins for power management circuit. 5V enable pin */
#define ADI_5V_EN 								(34)

/*
 * ADI GPIO Event Handler Definitions
 */

/** Event flag indicating a GPIO interrupt has triggered on DIO1 */
#define ADI_DIO1_INTERRUPT_FLAG					(1 << 0)

/** Event flag indicating a GPIO interrupt has triggered on DIO2 */
#define ADI_DIO2_INTERRUPT_FLAG					(1 << 1)

/** Event flag indicating a GPIO interrupt has triggered on DIO3 */
#define ADI_DIO3_INTERRUPT_FLAG					(1 << 2)

/** Event flag indicating a GPIO interrupt has triggered on DIO4 */
#define ADI_DIO4_INTERRUPT_FLAG					(1 << 3)

/** Event flag indicating a GPIO interrupt has triggered on FX3_GPIO1 */
#define FX3_GPIO1_INTERRUPT_FLAG					(1 << 4)

/** Event flag indicating a GPIO interrupt has triggered on FX3_GPIO2 */
#define FX3_GPIO2_INTERRUPT_FLAG					(1 << 5)

/** Event flag indicating a GPIO interrupt has triggered on FX3_GPIO3 */
#define FX3_GPIO3_INTERRUPT_FLAG					(1 << 6)

/** Event flag indicating a GPIO interrupt has triggered on FX3_GPIO4 */
#define FX3_GPIO4_INTERRUPT_FLAG					(1 << 7)

#endif

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
  * @file		main.h
  * @date		8/1/2019
  * @author		A. Nolan (alex.nolan@analog.com)
  * @author 	J. Chong (juan.chong@analog.com)
  * @brief Main header file for the Analog Devices iSensor FX3 Demonstration Platform firmware.
 **/

#ifndef MAIN_H
#define MAIN_H

/*
 * This macro is used to set verbose mode during compile time.
 * Ensure that it is commented out for release versions.
 */
//#define VERBOSE_MODE									(0)

/* Include all needed Cypress libraries */
#include "cyu3types.h"
#include "cyu3usbconst.h"
#include "cyu3externcstart.h"
#include "cyu3system.h"
#include "cyu3os.h"
#include "cyu3dma.h"
#include "cyu3error.h"
#include "cyu3usb.h"
#include "cyu3i2c.h"
#include "cyu3spi.h"
#include "cyu3uart.h"
#include "cyu3utils.h"
#include "cyu3gpio.h"
#include "cyu3vic.h"
#include "cyu3pib.h"
#include <stdlib.h>
#include <sys/unistd.h>

/* Include all Analog Devices produced project header files */
#include "AppThread.h"
#include "PinFunctions.h"
#include "SpiFunctions.h"
#include "StreamFunctions.h"
#include "StreamThread.h"

//Lower level register access includes
#include "gpio_regs.h"
#include "spi_regs.h"
#include "gctlaon_regs.h"

//Initialization and configuration functions.
void AdiAppStart();
void AdiAppStop();
void AdiAppErrorHandler (CyU3PReturnStatus_t status);

/* Event Handlers */
CyBool_t AdiControlEndpointHandler(uint32_t setupdat0, uint32_t setupdat1);
void AdiBulkEndpointHandler(CyU3PUsbEpEvtType evType,CyU3PUSBSpeed_t usbSpeed, uint8_t epNum);
void AdiUSBEventHandler(CyU3PUsbEventType_t evtype, uint16_t evdata);
CyBool_t AdiLPMRequestHandler(CyU3PUsbLinkPowerMode link_mode);
void AdiGPIOEventHandler(uint8_t gpioId);

/* Misc functions */
void AdiConfigureWatchdog();
void WatchDogTimerCb (uint32_t nParam);
void AdiGetBuildDate(uint8_t * outBuf);

/** Enum for the available part (DUT) types */
typedef enum PartTye
{
	/** 0 for ADcmXL1021 (single axis) */
	ADcmXL1021 = 0,

	/** 1 for ADcmXL2021 (two axis) */
	ADcmXL2021,

	/** 2 for ADcmXL3021 (three axis) */
    ADcmXL3021,

    /** 3 Other DUTs (IMU) */
    IMU,

    /** 4 Legacy IMU family (ADIS16448, etc) */
    LegacyIMU

}PartType;

/** Struct to store the current board state (SPI config, USB speed, etc) */
typedef struct BoardState
{
	/** Track the SPI configuration */
	CyU3PSpiConfig_t SpiConfig;

	/** Track the part type */
	PartType DutType;

	/** Track the USB buffer size for the current USB speed setting*/
	uint16_t UsbBufferSize;

	/** Track main application execution state*/
	CyBool_t AppActive;

	/** Bit mask of the starting timer pin configuration */
	uint32_t TimerPinConfig;

	/** Track the stall time in microseconds. This is the same as the FX3Api stall time setting */
	uint32_t StallTime;

	/** Track the data ready pin number */
	uint16_t DrPin;

	/** Track the busy pin number */
	uint16_t BusyPin;

	/** Track if data ready triggering is active (True = active, False = inactive) */
	CyBool_t DrActive;

	/** Track data ready polarity (True = trigger on rising edge, False = trigger on falling edge) */
	CyBool_t DrPolarity;

	/** Track if the watchdog timer is enabled */
	CyBool_t WatchDogEnabled;

	/** Track the watchdog timer period (ms) */
	uint32_t WatchDogPeriodMs;

	/** Track the watchdog timer ticks */
	uint32_t WatchDogTicks;

	/** Store the Unix Timestamp for the boot time. Used for error logging */
	uint32_t BootTime;

}BoardState;

/** Struct to store the current data stream state information */
typedef struct StreamState
{
	/** Track the number of bytes per real time frame */
	uint32_t BytesPerFrame;

	/** Track the pin exit setting for RT stream mode (True = enabled, False = disabled) */
	CyBool_t PinExitEnable;

	/** Track the pin start setting for RT stream mode (True = enabled, False = disabled) */
	CyBool_t PinStartEnable;

	/** Track the number of real-time captures to record (0 = Infinite) */
	uint32_t NumRealTimeCaptures;

	/** Track the total size of generic stream transfer in 16-bit words */
	uint16_t TransferWordLength;

	/** Track the total size of generic and burst stream transfers in bytes */
	uint32_t TransferByteLength;

	/** Track the total size of a generic or burst stream rounded to a multiple of 16 */
	uint16_t RoundedByteTransferLength;

	/** Track the number of captures requested for the generic data stream */
	uint32_t NumCaptures;

	/** Track the number of buffers requested for the generic data stream */
	uint32_t NumBuffers;

	/** Track the number of bytes to be read per buffer */
	uint16_t BytesPerBuffer;

	/** Pointer to byte array of registers needing to be read by the generic data stream */
	uint8_t *RegList;

	/** Number of bytes per USB packet in generic data stream mode */
	uint16_t BytesPerUsbPacket;
}StreamState;

/*
 * Vendor Command Request Code Definitions
 */

/** Return FX3 firmware ID (defined below) */
#define ADI_FIRMWARE_ID_CHECK					(0xB0)

/** Hard-reset the FX3 firmware (return to bootloader mode) */
#define ADI_HARD_RESET							(0xB1)

/** Set FX3 SPI configuration */
#define ADI_SET_SPI_CONFIG						(0xB2)

/** Return FX3 SPI configuration */
#define ADI_READ_SPI_CONFIG						(0xB3)

/** Return the current status of the FX3 firmware */
#define ADI_GET_STATUS							(0xB4)

/** Return the FX3 unique serial number */
#define ADI_SERIAL_NUMBER_CHECK					(0xB5)

/** Soft-reset the FX3 firmware (don't return to bootloader mode) */
#define ADI_WARM_RESET							(0xB6)

/** Set the DUT supply voltage (only works on ADI FX3 boards) */
#define ADI_SET_DUT_SUPPLY  					(0xB7)

/** Get firmware build date / time */
#define ADI_GET_BUILD_DATE 						(0xB8)

/** Set the boot time code */
#define ADI_SET_BOOT_TIME 						(0xB9)

/** Start/stop a generic data stream */
#define ADI_STREAM_GENERIC_DATA					(0xC0)

/** Start/stop a burst data stream */
#define ADI_STREAM_BURST_DATA					(0xC1)

/** Read the value of a user-specified GPIO */
#define ADI_READ_PIN							(0xC3)

/** Read the current FX3 timer register value */
#define ADI_READ_TIMER_VALUE					(0xC4)

/** Drive a user-specified GPIO for a user-specified time */
#define ADI_PULSE_DRIVE							(0xC5)

/** Wait for a user-specified pin to reach a user-specified level (with timeout) */
#define ADI_PULSE_WAIT							(0xC6)

/** Drive a user-specified GPIO */
#define ADI_SET_PIN								(0xC7)

/** Return the pulse frequency (data ready) on a user-specified pin */
#define ADI_MEASURE_DR	 						(0xC8)

/** Measure the propagation time from a sync edge tto data ready edge */
#define ADI_PIN_DELAY_MEASURE					(0xCF)

/** Start/stop a real-time stream */
#define ADI_STREAM_REALTIME						(0xD0)

/** Do nothing (default case) */
#define ADI_NULL_COMMAND						(0xD1)

/** Read a word at a specified address and return the data over the control endpoint */
#define ADI_READ_BYTES							(0xF0)

/** Write one byte of data to a user-specified address */
#define ADI_WRITE_BYTE							(0xF1)

/** Used to transfer bytes without any intervention/protocol management */
#define ADI_TRANSFER_BYTES						(0xCA)

/** Starts a transfer stream for the ISpi32Interface */
#define ADI_TRANSFER_STREAM						(0xCC)

/** Command to enable or disable a PWM signal */
#define ADI_PWM_CMD  							(0xC9)

/** Command to trigger an event on the DUT and measure a subsequent pulse */
#define ADI_BUSY_MEASURE						(0xCB)

/** Bitbang a SPI message on the selected pins */
#define ADI_BITBANG_SPI							(0xCD)

/** Reset the hardware SPI controller */
#define ADI_RESET_SPI							(0xCE)

/*
 * Clock defines
 */

/** Conversion factor from clock ticks to seconds on GPIO timer (avoids some error) */
#define S_TO_TICKS_MULT							(10078400)

/** Conversion factor from clock ticks to milliseconds on GPIO timer */
#define MS_TO_TICKS_MULT						S_TO_TICKS_MULT	/ 1000

/** Offset to take away from the timer period for generic stream stall time. In 10MHz timer ticks */
#define ADI_GENERIC_STALL_OFFSET				(90) //Previously 76 with optimized SPI

/** Minimum possible sleep time  */
#define ADI_MICROSECONDS_SLEEP_OFFSET			(14)

/** Complex GPIO assigned as a timer input */
#define ADI_TIMER_PIN							(0x8)
#define ADI_TIMER_PIN_INDEX						(0x0) //ADI_TIMER_PIN % 8

/*
 * Endpoint Related Defines
 */

/** BULK-IN endpoint (data goes from FX3 into PC) */
#define ADI_STREAMING_ENDPOINT					(0x81)

/** BULK-OUT endpoint (general data from PC to FX3) */
#define ADI_FROM_PC_ENDPOINT					(0x1)

/** BULK-IN endpoint (general data from FX3 to PC) */
#define ADI_TO_PC_ENDPOINT						(0x82)

/** Burst size for SS operation only */
#define CY_FX_BULK_BURST               			(8)

/*
 * Error handling location defines
 */
#define ERROR_GENERAL_INIT						1
#define ERROR_CACHE_CONTROL						2
#define ERROR_PIN_INIT							3
#define ERROR_GPIO_MATRIX						4
#define ERROR_THREAD_START						5
#define ERROR_OTHER								0

/*
 * USB Descriptor buffers
 */
extern const uint8_t CyFxUSB20DeviceDscr[];
extern const uint8_t CyFxUSB30DeviceDscr[];
extern const uint8_t CyFxUSBDeviceQualDscr[];
extern const uint8_t CyFxUSBFSConfigDscr[];
extern const uint8_t CyFxUSBHSConfigDscr[];
extern const uint8_t CyFxUSBBOSDscr[];
extern const uint8_t CyFxUSBSSConfigDscr[];
extern const uint8_t CyFxUSBStringLangIDDscr[];
extern const uint8_t CyFxUSBManufactureDscr[];
extern const uint8_t CyFxUSBProductDscr[];
extern uint8_t CyFxUSBSerialNumDesc[];

#include <cyu3externcend.h>

#endif

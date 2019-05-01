/*
 ## Analog Devices Inc. FX3 Interface Header (AdiFX3Firmware.h)
 ## ===========================
 ##
 ##  Copyright Analog Devices Incorporated, 2018-2019,
 ##  All Rights Reserved
 ##
 ##  THIS SOFTWARE IS BUILT AROUND LIBRARIES DEVELOPED
 ##	 AND MAINTAINED BY CYPRESS INC.
 ##
 ##  Use of this file is governed
 ##  by the license agreement included
 ##	 in this repository.
 ##
 ##	 Juan Chong (juan.chong@analog.com), Alex Nolan (alex.nolan@analog.com)
 ##
 ## ===========================
 */

#ifndef ADIFX3FIRMWARE_H_
#define ADIFX3FIRMWARE_H_

//Cypress Library Includes
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
#include <sys/unistd.h>
#include <stdlib.h>

//Lower level register access includes
#include "gpio_regs.h"
#include "spi_regs.h"
#include "gctlaon_regs.h"


/*
 * Function Declarations
 */
//Initialization and configuration functions.
CyU3PReturnStatus_t AdiGetSpiSettings();
CyU3PReturnStatus_t AdiSpiInit();
void AdiDebugInit();
void AdiSetDefaultSpiConfig();
CyBool_t AdiSpiUpdate(uint16_t index, uint16_t value, uint16_t length);
CyU3PReturnStatus_t AdiConfigureEndpoints();
CyU3PReturnStatus_t AdiGPIOInit();
CyU3PReturnStatus_t AdiDeviceInit();
void AdiFatalErrorHandler(uint32_t ErrorType);
CyBool_t AdiLPMRequestHandler(CyU3PUsbLinkPowerMode link_mode);
CyU3PReturnStatus_t AdiCreateEventFlagGroup();

//Pin functions.
CyU3PReturnStatus_t AdiPulseDrive();
CyU3PReturnStatus_t AdiPulseWait();
CyU3PReturnStatus_t AdiSetPin(uint16_t pinNumber, CyBool_t polarity);
CyU3PReturnStatus_t AdiMeasureDR();
CyU3PReturnStatus_t AdiWaitForPin(uint32_t pinNumber, CyU3PGpioIntrMode_t interruptSetting, uint32_t timeoutTicks);
CyU3PReturnStatus_t AdiPinRead(uint16_t pin);
CyU3PReturnStatus_t AdiReadTimerValue();
uint32_t AdiMStoTicks(uint32_t desiredStallTime);
void AdiWaitForTimerTicks(uint32_t numTicks);
void AdiGPIOEventHandler(uint8_t gpioId);

//Peripheral read-write functions.
CyU3PReturnStatus_t AdiWriteRegByte(uint16_t addr, uint8_t data);
CyU3PReturnStatus_t AdiReadRegBytes(uint16_t addr);
CyU3PReturnStatus_t AdiBulkByteTransfer(uint16_t numBytes, uint16_t bytesPerCapture);
CyU3PReturnStatus_t AdiWritePageReg(uint16_t pageNumber);
CyU3PReturnStatus_t AdiReadSpiReg(uint16_t address, uint16_t page, uint16_t numBytes, uint8_t  *buffer);
CyU3PReturnStatus_t AdiWriteSpiReg(uint16_t address, uint16_t page, uint16_t numBytes, uint8_t  *buffer);
void AdiBulkEndpointHandler(CyU3PUsbEpEvtType evType,CyU3PUSBSpeed_t usbSpeed, uint8_t epNum);
CyBool_t AdiControlEndpointHandler(uint32_t setupdat0, uint32_t setupdat1);
void AdiUSBEventHandler(CyU3PUsbEventType_t evtype, uint16_t evdata);

//Application entry points.
void AppThread_Entry(uint32_t input);
void AdiDataStream_Entry(uint32_t input);

//Real-time data stream functions.
CyU3PReturnStatus_t AdiRealTimeStart();
CyU3PReturnStatus_t AdiRealTimeFinished();

//Generic data stream functions.
CyU3PReturnStatus_t AdiGenericDataStreamStart();
CyU3PReturnStatus_t AdiGenericDataStreamFinished();

//Burst stream functions.
CyU3PReturnStatus_t AdiBurstStreamStart();
CyU3PReturnStatus_t AdiBurstStreamFinished();

//General stream functions.
void AdiStopAnyDataStream();

//Enum for part type (used in streaming modes)
typedef enum PartTye
{
	ADcmXL1021 = 0,			//0 for ADcmXL1021 (single axis)
	ADcmXL2021,				//1 for ADcmXL2021 (two axis)
    ADcmXL3021,				//2 for ADcmXL3021 (three axis)
    Other					//Other DUT's (IMUs)
}PartType;

//Boolean enum
typedef enum Boolean
{
	True = 1,
	False = 0
}Boolean;

//Struct to store relevant board parameters
struct BoardConfig
{
	//Not used yet. Plan to use once more hardware revisions are implemented.
};


/*
 * Vendor Command Request Code Definitions
 */
//Return FX3 firmware ID (defined below)
#define ADI_FIRMWARE_ID_CHECK					(0xB0)

//Reset the FX3 firmware
#define ADI_FIRMWARE_RESET						(0xB1)

//Set FX3 SPI configuration
#define ADI_SET_SPI_CONFIG						(0xB2)

//Return FX3 SPI configuration
#define ADI_READ_SPI_CONFIG						(0xB3)

//Return the current status of the FX3 firmware
#define ADI_GET_STATUS							(0xB4)

//Start/stop a generic data stream
#define ADI_STREAM_GENERIC_DATA					(0xC0)

//Start/stop a burst data stream
#define ADI_STREAM_BURST_DATA					(0xC1)

//Read the value of a user-specified GPIO
#define ADI_READ_PIN							(0xC3)

//Read the current FX3 timer register value
#define ADI_READ_TIMER_VALUE					(0xC4)

//Drive a user-specified GPIO for a user-specified time
#define ADI_PULSE_DRIVE							(0xC5)

//Wait for a user-specified pin to reach a user-specified level (with timeout)
#define ADI_PULSE_WAIT							(0xC6)

//Drive a user-specified GPIO
#define ADI_SET_PIN								(0xC7)

//Return the pulse frequency (data ready) on a user-specified pin
#define ADI_MEASURE_DR	 						(0xC8)

//Start/stop a real-time stream
#define ADI_STREAM_REALTIME						(0xD0)

//Do nothing (default case)
#define ADI_NULL_COMMAND						(0xD1)

//Read a word at a specified address and return the data over the control endpoint
#define ADI_READ_BYTES							(0xF0)

//Write one byte of data to a user-specified address
#define ADI_WRITE_BYTE							(0xF1)

//Return data over a bulk endpoint before a bulk read/write operation
#define ADI_BULK_REGISTER_TRANSFER				(0xF2)


/*
 * Thread Parameter Definitions
 */
// App thread stack size
#define APPTHREAD_STACK       					(0x0800)

// App thread priority
#define APPTHREAD_PRIORITY    						(8)

// Real time thread stack size
#define STREAMINGTHREAD_STACK					(0x0800)

// Real time thread priority
#define STREAMINGTHREAD_PRIORITY					(8)


/*
 * GPIO Pin mapping definitions
 */
//GPIO pins used on the FX3 evaluation board (sanitized for iSensor use)
#define ADI_PIN_RESET							(0x0)	// Wired to the hardware reset on most iSensor products
#define ADI_PIN_DIO1							(0x4)	// Commonly data ready on IMUs
#define ADI_PIN_DIO2							(0x3)	// Commonly BUSY on ADcmXL devices
#define ADI_PIN_DIO3							(0x2)
#define ADI_PIN_DIO4							(0x1)
#define ADI_PIN_DIO5							(0x5)	// Misc pin(s) used for triggering from test equipment
#define ADI_PIN_DIO6							(0x6)	// Misc pin
#define ADI_PIN_DIO7							(0x7)	// Misc pin

//Complex GPIO pin configured to act as a high-speed timer
#define ADI_TIMER_PIN							(0x8)


/*
 * Endpoint Related Defines
 */
//BULK-IN endpoint (data goes from FX3 into PC)
#define ADI_STREAMING_ENDPOINT					(0x81)

//BULK-OUT endpoint (general data from PC to FX3)
#define ADI_FROM_PC_ENDPOINT					(0x1)

//BULK-IN endpoint (general data from FX3 to PC)
#define ADI_TO_PC_ENDPOINT						(0x82)

//Burst size for SS operation only
#define CY_FX_BULK_BURST               			(8)


/*
 * Clock defines
 */
//Conversion factor from clock ticks to milliseconds on GPIO timer
#define MS_TO_TICKS_MULT						(1000) //(Previously 953)

 //Minimum achievable stall time in microseconds (limited by the high-speed, complex GPIO)
#define ADI_STALL_OFFSET						(14)


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
 * Bit defines
 */
#define bit0									(1 << 0)
#define bit1									(1 << 1)
#define bit2									(1 << 2)
#define bit3									(1 << 3)
#define bit4									(1 << 4)
#define bit5									(1 << 5)
#define bit6									(1 << 6)
#define bit7									(1 << 7)
#define bit8									(1 << 8)
#define bit9									(1 << 9)
#define bit10									(1 << 10)
#define bit11									(1 << 11)
#define bit12									(1 << 12)
#define bit13									(1 << 13)
#define bit14									(1 << 14)
#define bit15									(1 << 15)
#define bit16									(1 << 16)
#define bit17									(1 << 17)
#define bit18									(1 << 18)
#define bit19									(1 << 19)
#define bit20									(1 << 20)
#define bit21									(1 << 21)
#define bit22									(1 << 22)
#define bit23									(1 << 23)
#define bit24									(1 << 24)
#define bit25									(1 << 25)
#define bit26									(1 << 26)
#define bit27									(1 << 27)
#define bit28									(1 << 28)
#define bit29									(1 << 29)
#define bit30									(1 << 30)
#define bit31									(1 << 31)


/*
 * ADI Event Handler Definitions
 */
#define ADI_RT_STREAMING_START					(1 << 0)
#define ADI_RT_STREAMING_DONE					(1 << 1)
#define ADI_RT_STREAMING_STOP					(1 << 2)
#define ADI_DATA_STREAMING_START				(1 << 3)
#define ADI_DATA_STREAMING_STOP					(1 << 4)
#define ADI_DATA_STREAMING_DONE					(1 << 5)
#define ADI_GENERIC_STREAM_ENABLE				(1 << 6)
#define ADI_REAL_TIME_STREAM_ENABLE				(1 << 7)
#define ADI_KILL_THREAD_EARLY					(1 << 8)	//Currently unused.
#define ADI_BURST_STREAMING_START				(1 << 9)
#define ADI_BURST_STREAMING_STOP				(1 << 10)
#define ADI_BURST_STREAMING_DONE				(1 << 11)
#define ADI_BURST_STREAM_ENABLE					(1 << 12)


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

#include <cyu3externcend.h>

#endif /* ADIFX3FIRMWARE_H_ */

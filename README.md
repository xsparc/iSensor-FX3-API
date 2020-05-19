# iSensor FX3 API - A .NET API for the iSensor FX3 Firmware

## Test Status

![Most Recent Test Results](https://raw.githubusercontent.com/ajn96/iSensor-FX3-Test/master/Results/test_status.png)

The test repository for the iSensor FX3 API and FX3 Firmware is hosted [here](https://github.com/ajn96/iSensor-FX3-Test). This respository includes source for all test cases (C# running on NUnit 2.6.4) and a simple CI script for a Windows host. All test cases are system level tests running on target hardware.

## Overview

The iSensor FX3 Firmware and API are designed to provide users with a means of reliably acquiring sensor data over a high-speed USB connection in any .NET compatible application. This firmware was designed for use on the Cypress FX3 SuperSpeed Explorer Kit and relies on the open source libraries provided by Cypress to operate. The freely-available, Eclipse-based, Cypress EZ USB Suite was used for all firmware development. 

## System Architecture

The iSensor FX3 firmware attempts to follow the Cypress program workflow and relies on FX3 system threading, execution priority, and event flags to execute firmware subroutines and transmit sensor data. Unique vendor commands trigger subroutines embedded in the iSensor FX3 firmware that read and write SPI data, measure external pulses, generate clock signals, and manage board configuration. Different SPI streaming modes are implemented which allow applications to easily communicate to most products in the iSensor portfolio. 

A .NET-compatible API (this repository) has been developed in parallel to simplify interfacing with the iSensor FX3 firmware. 

## API Documentation

Sandcastle-generated documentation for the FX3 API class can be found [https://juchong.github.io/iSensor-FX3-API/](https://juchong.github.io/iSensor-FX3-API/). 

## iSensor FX3 Evaluation Board

This firmware was designed around the Cypress EZ-USB FX3 SuperSpeed USB Controller (CYUSB3014 and CYUSB2014) family of USB interface ICs offered by Cypress. 

The next-generation FX3-based interface board now includes many additional firmware and hardware features that make testing, characterizing, and developing software for iSensor products easy. Firmware and API support for this board are included in release v2.5 onward. 

![iSensor FX3 Evaluation Board](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/hardware/pictures/img6.jpg)

Some of the board's features include:

- Dedicated, onboard 3.3V, 2A regulator designed for high-transient applications
- USB-C connector
- Onboard, field-programmable EEPROM with USB bootloader fallback
- Software-selectable OFF / 3.3V / 5V sensor supply output with overcurrent and short protection
- JST-XH-2 external supply connector
- iSensor standard, 16-pin, 2mm connector for compatibility with most iSensor breakout boards and adapters
- Additional 10-pin, 2mm connector for feature expansion. The current firmware and API include support for:
  - FX3 UART debugging
  - Four GPIO for external test equipment triggering and sensing
  - Internal 3.3V and 5V supplies are broken out separately from the DUT supply to power external level shifters, interface ICs, etc.
  - An extra "bit-banged" SPI port to allow for "exotic" SPI configurations and communication with external hardware (ADCs, DACs, protocol interface ICs, etc.)
- Multi-board data capture capability. Multiple boards can be connected to the same PC and can concurrently capture data independently of each other
- Very low CPU usage while capturing data, even on older Windows machines
- 1.5" x 1.75" PCB footprint

![iSensor FX3 Evaluation Board with IMU ](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/hardware/pictures/img7.jpg)

Design files for the breakout board is available in the [hardware](https://github.com/juchong/iSensor-FX3-Firmware/tree/master/hardware) folder of this repository. 

## SuperSpeed Explorer Kit Breakout Board

A breakout board designed for interfacing iSensor devices with the Cypress SuperSpeed Explorer Kit (CYUSB3KIT-003) was introduced as a temporary solution while a more feature-rich offering was developed.  Both boards will continue to be supported in future firmware revisions. 

![CYUSB3KIT-003 and Breakout Board](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/hardware/pictures/img2.jpg)

Design files for both breakout boards are available in the [hardware](https://github.com/juchong/iSensor-FX3-Firmware/tree/master/hardware) folder of this repository. 

## SuperSpeed Explorer Kit Jumper Configuration

The Explorer Kit requires **three** jumpers to be installed to operate correctly as shown in the image below. **Jumpers J2, J3, and J5 must be installed** when using the SuperSpeed Explorer Kit. **Jumper J4 must be open** to allow booting from the onboard EEPROM. 

 ![FX3 Jumper Locations](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/hardware/pictures/JumperLocations.jpg)

## Getting Started

#### Bootloader Firmware Stage

The API is designed such that custom bootloader firmware is loaded into FX3 RAM prior to executing the  `Connect()`function. This custom bootloader exposes LED blinking commands, a unique FX3 serial number in the USB vendor descriptor, and more importantly allows the main application firmware to be loaded over the bootloader firmware without a reboot. These features allow multiple FX3 boards to communicate with multiple application instances on a single machine. They also provide a means of visually identifying multiple FX3 boards when preparing to initiate a connection. All connected boards should be running the bootloader firmware to be considered as a "valid" board by the API. If an FX3 board is identified by the Cypress driver, but is not running the custom bootloader, then it will be ignored by the API. *FX3 boards must be running the custom bootloader firmware prior to loading application firmware~*

#### Application Firmware Loading Stage

The API provides functions to detect, identify, and program FX3 boards in user applications. Once a valid board has been identified, the `Connect()` function will push the application firmware into FX3 RAM, overwriting the bootloader firmware. The function also verifies whether communication with the FX3 board is as expected.

#### API Features Overview

The FX3 API translates high-level user commands into the necessary low-level Cypress API calls required to communicate with the FX3 firmware. The API also simplifies SPI configuration, data ready behavior, and device management. Using the vendor command structure outlined by Cypress, different SPI capture and streaming modes can be called based upon the user's requirements. Additional functionality such as generating clocks and pulses, measuring the time between pin pulses, waiting for external triggers, and a few other features have also been baked into the firmware and API.

#### Example Application

An example application was developed to provide users with a simple starting point. The application repository can be found here.

## Drivers

As of v1.0.6, custom, signed, Analog Devices drivers must be used to communicate with the iSensor FX3 Firmware. The driver installation package can be found in the [drivers](https://github.com/juchong/iSensor-FX3-API/tree/master/drivers) folder in this repository or downloaded directly from [here](https://github.com/juchong/iSensor-FX3-API/raw/master/drivers/FX3DriverSetup.exe). 

## Supporting Repositories

As mentioned above, the two repositories listed below were developed alongside this firmware and provide an easy way to implement iSensor FX3 Firmware features in a .NET application. It is *highly* recommended that the FX3 Firmware and API versions match!

1. [iSensor FX3 Firmware](https://github.com/juchong/iSensor-FX3-Firmware)

2. [iSensor FX3 Example Gui](https://github.com/juchong/iSensor-FX3-ExampleGui)

## iSensor-Specific Library Information

The FX3 API implements in-house closed-source interface libraries (IRegInterface and IPinFcns) included in the `resources` folder in this repository. These libraries are required to maintain control and consistency with other iSensor devices and are defined in the AdisApi. 

This API (and the FX3-specific classes) should be used in place of the AdisBase and iSensorSpi classes for performing read/write operations. Unlike iSensorSpi, this class includes all the connection and SPI setup functions defined internally. This allows the FX3 API to perform the device connection and enumeration operations without having to pass the class an instance of AdisBase. 

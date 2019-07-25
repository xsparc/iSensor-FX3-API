# iSensor FX3 API - A .NET API for the iSensor FX3 Firmware

## Overview

The iSensor FX3 Firmware and API are designed to provide users with a means of reliably acquiring sensor data over a high-speed USB connection in any .NET compatible application. This firmware was designed for use on the Cypress FX3 SuperSpeed Explorer Kit and relies on the open source libraries provided by Cypress to operate. The freely-available, Eclipse-based, Cypress EZ USB Suite was used for all firmware development. 

## System Architecture

The iSensor FX3 firmware attempts to follow the Cypress program workflow and relies on FX3 system threading, execution priority, and event flags to execute firmware subroutines and transmit sensor data. Unique vendor commands trigger subroutines embedded in the iSensor FX3 firmware that read and write SPI data, measure external pulses, generate clock signals, and manage board configuration. Different SPI streaming modes are implemented which allow applications to easily communicate to most products in the iSensor portfolio. 

A .NET-compatible API (this repository) has been developed in parallel to simplify interfacing with the iSensor FX3 firmware. 

## API Documentation

Sandcastle-generated documentation for the FX3 API class can be found [https://juchong.github.io/iSensor-FX3-API/](https://juchong.github.io/iSensor-FX3-API/). 

## Hardware Requirements

This firmware was designed using the Cypress SuperSpeed Explorer Kit (CYUSB3KIT-003), but should operate on a bare CYUSB3014 device assuming the correct hardware resources are externally available. 

Design files for a breakout board designed to adapt the Explorer Kit's pins to a standard, 16-pin, 2mm connector used on most iSensor evaluation boards is available in the [documentation](https://github.com/juchong/iSensor-FX3-Firmware/tree/master/Documentation) folder of this repository. 

 ![FX3 Jumper Locations](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/documentation/pictures/JumperLocations.jpg)

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

2. [iSensor FX3 Example Gui](https://github.com/juchong/iSensor-FX3-Example-Gui)

## iSensor-Specific Library Information

The FX3 API implements in-house closed-source interface libraries (IRegInterface and IPinFcns) included in the `resources` folder in this repository. These libraries are required to maintain control and consistency with other iSensor devices and are defined in the AdisApi. 

This API (and the FX3-specific classes) should be used in place of the AdisBase and iSensorSpi classes for performing read/write operations. Unlike iSensorSpi, this class includes all the connection and SPI setup functions defined internally. This allows the FX3 API to perform the device connection and enumeration operations without having to pass the class an instance of AdisBase. 

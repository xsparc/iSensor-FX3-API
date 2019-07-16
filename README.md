# iSensor FX3 API - A .NET API for the iSensor FX3 Firmware

## Overview

The iSensor FX3 firmware and API provide you with a means of acquiring sensor data over a high-speed USB connection in any application that supports .NET libraries. This firmware was designed for the FX3 SuperSpeed Explorer Kit and relies on the open source libraries provided by Cypress to operate. The firmware was written using the freely-available Cypress EZ USB Suite, allowing anyone to modify the base firmware to fit their needs. 

The FX3 firmware is entirely event-driven and communicates with the PC using a vendor command structure. This event structure calls subroutines within the FX3 firmware to measure signals, configure SPI communication, acquire data, etc. This firmware also relies on the FX3 API to establish communication with the FX3 firmware. 

Using both the FX3 firmware and the FX3 API enables you to acquire sensor data quickly while giving the freedom to add custom features to your interface software. 

## API Documentation

Full documentation for the public FX3 API can be found [here](https://juchong.github.io/iSensor-FX3-API/). 

## Hardware Requirements

The firmware is designed to be built and run on a Cypress SuperSpeed Explorer Kit (CYUSB3KIT-003). A breakout board designed to convert the Explorer Kit's pins to a standard, 16-pin, 2mm connector used on most iSensor evaluation should be available soon. A schematic showing how to connect iSensor products to the Explorer Kit can be found in the Documentation folder of the iSensor FX3 firmware repository [here](https://github.com/juchong/iSensor-FX3-Firmware/tree/master/Documentation). 

The Explorer Kit requires two jumpers to be installed before the API will communicate. The image below shows where the jumpers must be installed.

 ![FX3 Jumper Locations](https://raw.githubusercontent.com/juchong/iSensor-FX3-Firmware/master/Documentation/pictures/JumperLocations.jpg)

## Getting Started

The Explorer Kit must be programmed every time that it is powered off/on since the firmware is loaded into RAM by default. Executing the Connect() function in the FX3Connection class will automatically push the firmware image to the FX3 once the correct path has been set. 

Once connected, the functions included within the FX3Connection class allow you to capture data, configure SPI, trigger GPIO, and many other functions from any .NET-compatible application.

An example VB.NET application can be found [here](https://github.com/juchong/FX3Gui). 

## Drivers

USB drivers for the FX3 Explorer Kit should be installed automatically if the EZ USB Suite is installed. If you would like to communicate with the FX3 without installing the entire suite, the drivers are available on the ADI Wiki site [here](https://wiki.analog.com/_media/resources/eval/user-guides/inertial-mems/fx3driver.zip).

## Additional Repositories

Two additional repositories complement this API, the FX3 Firmware and the example project where these libraries are implemented. Links to both of these repositories are shown below.

1. [FX3 Firmware](https://github.com/juchong/iSensor-FX3-Firmware)

2. [FX3 ADcmXL GUI Example (FX3Gui)](https://github.com/juchong/iSensor-ADcmXL-FX3Gui)

## Additional Library Information

The FX3 API implements additional iSensor-specific interface libraries (IRegInterface and IPinFcns) included in the `Resources` folder in this repository. These libraries are required to maintain control and consistency with other iSensor devices and are defined in the AdisApi. 

This library (and the FX3-specific classes) should be used in place of the AdisBase and iSensorSpi classes for performing read/write operations. Unlike iSensorSpi, this class includes all the connection and SPI setup functions defined internally. This allows the FX3 API to perform the device connection and enumeration operations without having to pass the class an instance of AdisBase. 

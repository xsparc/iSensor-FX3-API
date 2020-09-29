# ADI Bootloader Firmware

## Overview

This project is a custom USB bootloader for the ADI iSensor FX3 Demonstration Platform.

## Usage

This bootloader image is stored on the I2C EEPROM of the FX3 board. When the FX3 board first powers up, this image is loaded, and the board will identify itself as an Analog Devices iSensor FX3 Bootloader. When a user connects to the FX3 board using the FX3 API, 
the bootloader loads the FX3 Firmware image into the FX3 RAM and then jumps to the FX3 Firmware entry point.
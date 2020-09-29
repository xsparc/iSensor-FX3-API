# Flash Programmer Firmware

## Overview

This project is a modified version of the Cypress provided flash programmer firmware. It features an extra vendor command which allows the FX3 API to force a hard reboot.

## Usage

When the FX3 API detects an FX3 board connected to the system which does not have the ADI bootloader, or does not have the latest version of the ADI bootloader, this image is loaded into
the FX3 RAM. Once the flash programmer firmware finishes booting, a vendor command is sent to program the flash EEPROM with the latest version of the ADI FX3 bootloader firmware. A hard reset command is
then sent to the flash programmer firmware which forces a reboot from the freshly programmed bootloader image in flash.

This entire process is invisible to the end user.
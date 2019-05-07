/*
 ## Cypress FX3 Boot Firmware Example Source file (main.c)
 ## ===========================
 ##
 ##  Copyright Cypress Semiconductor Corporation, 2010-2018,
 ##  All Rights Reserved
 ##  UNPUBLISHED, LICENSED SOFTWARE.
 ##
 ##  CONFIDENTIAL AND PROPRIETARY INFORMATION
 ##  WHICH IS THE PROPERTY OF CYPRESS.
 ##
 ##  Use of this file is governed
 ##  by the license agreement included in the file
 ##
 ##     <install>/license/license.txt
 ##
 ##  where <install> is the Cypress software
 ##  installation root directory path.
 ##
 ## ===========================
*/

#include "cyfx3usb.h"
#include "cyfx3device.h"
#include "cyfx3utils.h"
#include "cyfx3gpio.h"

/* Define LED GPIO */
#define APP_LED_GPIO    (54)

/* Enable this for booting off the USB */
#define USB_BOOT
#ifdef USB_BOOT
extern void
myUsbBoot (
        void);
#endif

/****************************************************************************
 * main:
 ****************************************************************************/
int
main (
        void)
{
    CyFx3BootErrorCode_t status;
    CyFx3BootIoMatrixConfig_t  ioCfg;
    CyFx3BootGpioSimpleConfig_t gpioConf;

    /* HW and SW initialization code  */
    CyFx3BootDeviceInit (CyTrue);

    ioCfg.isDQ32Bit = CyFalse;
    ioCfg.useUart   = CyFalse;
    ioCfg.useI2C    = CyFalse;
    ioCfg.useI2S    = CyFalse;
    ioCfg.useSpi    = CyFalse;
    ioCfg.gpioSimpleEn[0] = 0;
    ioCfg.gpioSimpleEn[1] = (1 << (APP_LED_GPIO - 32));

    status = CyFx3BootDeviceConfigureIOMatrix (&ioCfg);
    if (status != CY_FX3_BOOT_SUCCESS)
    {
        CyFx3BootDeviceReset ();
        return status;
    }

    CyFx3BootGpioInit ();

    /* Configure the GPIO for driving the LED. */
    gpioConf.inputEn     = CyFalse;
    gpioConf.driveLowEn  = CyTrue;
    gpioConf.driveHighEn = CyTrue;
    gpioConf.outValue    = CyFalse;
    gpioConf.intrMode    = CY_FX3_BOOT_GPIO_NO_INTR;

    status = CyFx3BootGpioSetSimpleConfig (APP_LED_GPIO, &gpioConf);
    if (status != CY_FX3_BOOT_SUCCESS)
        return status;

#ifdef USB_BOOT
    /* Enable this for booting off the USB */
    myUsbBoot ();
#endif

    while (1)
    {
#ifdef USB_BOOT
        /* Enable this piece of code when using the USB module.
         * Call the new wrapper function which handles all state changes as required.
         */
        CyFx3BootUsbHandleEvents ();
#endif
        /* Flash the LED */
        CyFx3BootGpioSetValue (APP_LED_GPIO, CyTrue);
        CyFx3BootBusyWait (65534);
        CyFx3BootGpioSetValue (APP_LED_GPIO, CyFalse);
        CyFx3BootBusyWait (65534);

    }

    return 0;
}


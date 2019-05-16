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

CyBool_t blinkLed = CyFalse;
CyBool_t ledState = CyFalse;

/* Enable this for booting off the USB */
extern void
myUsbBoot (
        void);
extern uint8_t glCheckForDisconnect;
extern uint8_t glInCompliance;

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
    gpioConf.outValue    = CyTrue;
    gpioConf.intrMode    = CY_FX3_BOOT_GPIO_NO_INTR;

    status = CyFx3BootGpioSetSimpleConfig (APP_LED_GPIO, &gpioConf);
    if (status != CY_FX3_BOOT_SUCCESS)
        return status;

    /* Enable this for booting off the USB */
    myUsbBoot ();

    while (1)
    {
        /* Enable this piece of code when using the USB module.
         * Call the new wrapper function which handles all state changes as required.
         */
        CyFx3BootUsbHandleEvents ();

        /* Flash the LED */
        if (blinkLed)
        {
            CyFx3BootGpioSetValue (APP_LED_GPIO, CyTrue);
            CyFx3BootBusyWait (65534);
            CyFx3BootGpioSetValue (APP_LED_GPIO, CyFalse);
            CyFx3BootBusyWait (65534);
        }
        else
        {
        	if (ledState)
        	{
        		CyFx3BootGpioSetValue (APP_LED_GPIO, CyFalse);
        	}
        	else
        	{
        		CyFx3BootGpioSetValue (APP_LED_GPIO, CyTrue);
        	}
        }
    }

    return 0;
}


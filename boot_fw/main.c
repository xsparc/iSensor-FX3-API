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
#include "main.h"

uint16_t mode = 0;

/* Enable this for booting off the USB */
extern void
myUsbBoot (
        void);

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
    CyFx3BootDeviceInit(CyTrue);

    ioCfg.isDQ32Bit = CyFalse;
    ioCfg.useUart   = CyFalse;
    ioCfg.useI2C    = CyFalse;
    ioCfg.useI2S    = CyFalse;
    ioCfg.useSpi    = CyFalse;
    ioCfg.gpioSimpleEn[0] = 0;
    /* LED (54) */
    ioCfg.gpioSimpleEn[1] = (1 << (APP_LED_GPIO - 32));

    status = CyFx3BootDeviceConfigureIOMatrix(&ioCfg);
    if (status != CY_FX3_BOOT_SUCCESS)
    {
        CyFx3BootDeviceReset();
        return status;
    }

    CyFx3BootGpioInit();

    /* Set SCLK pull up */
    CyFx3BootGpioSetIoMode(APP_SCLK_GPIO, CY_FX3_GPIO_IO_MODE_WPU);

    /* Set CS pull up */
    CyFx3BootGpioSetIoMode(APP_LED_GPIO, CY_FX3_GPIO_IO_MODE_WPU);

    /* Configure the GPIO for driving the LED. */
    gpioConf.inputEn     = CyFalse;
    gpioConf.driveLowEn  = CyTrue;
    gpioConf.driveHighEn = CyTrue;
    gpioConf.outValue    = CyTrue;
    gpioConf.intrMode    = CY_FX3_BOOT_GPIO_NO_INTR;
    CyFx3BootGpioSetSimpleConfig(APP_LED_GPIO, &gpioConf);

    /* Enable this for booting off the USB */
    myUsbBoot();

    while (1)
    {
        /* Enable this piece of code when using the USB module.
         * Call the new wrapper function which handles all state changes as required.
         */
        CyFx3BootUsbHandleEvents();

        /* Handle blinking the LED */

        switch(mode)
		{
        case 1: /* Blink LED */
            CyFx3BootGpioSetValue(APP_LED_GPIO, CyTrue);
            CyFx3BootBusyWait(65534);
            CyFx3BootGpioSetValue(APP_LED_GPIO, CyFalse);
            CyFx3BootBusyWait(65534);
            break;

        case 2: /* Turn LED on */
        	CyFx3BootGpioSetValue(APP_LED_GPIO, CyFalse);
        	break;

        case 3: /* Turn LED off*/
        	CyFx3BootGpioSetValue(APP_LED_GPIO, CyTrue);
        	break;

        default: /* Turn LED off*/
        	CyFx3BootGpioSetValue(APP_LED_GPIO, CyTrue);
		}

    }

    return 0;
}


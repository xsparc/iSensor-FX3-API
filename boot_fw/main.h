/*
 * main.h
 *
 *  Created on: May 8, 2019
 *      Author: jchong
 */

#ifndef MAIN_H_
#define MAIN_H_

/* Keep track of LED mode */
extern uint16_t mode;

/* Define LED GPIO */
#define APP_LED_GPIO   			(54)

/* Define SCK GPIO */
#define	APP_SCLK_GPIO			(53)

/*
 * Bootloader Vendor Commands
 */
/* Hard-reset the FX3 firmware (return to bootloader mode) */
#define ADI_HARD_RESET			(0xB1)

/* Turn on APP_LED_GPIO solid */
#define ADI_LED_ON				(0xEC)

/* Turn off APP_LED_GPIO */
#define ADI_LED_OFF				(0xED)

/* Turn off APP_LED_GPIO blinking */
#define ADI_LED_BLINKING_OFF	(0xEE)

/* Turn on APP_LED_GPIO blinking */
#define ADI_LED_BLINKING_ON		(0xEF)

#endif /* MAIN_H_ */

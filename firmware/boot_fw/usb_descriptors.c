/*
 ## Cypress FX3 Boot Firmware Example Source file (usb_descriptors.c)
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

/* Device, config and string descriptors. */
unsigned char gbDevDesc[] =
{
    0x12,                           /* Descriptor Size */
    0x01,                           /* Device Descriptor Type */
    0x10,0x02,                      /* USB 2.10 */
    0x00,                           /* Device Class */
    0x00,                           /* Device Sub-class */
    0x00,                           /* Device protocol */
    0x40,                           /* Maxpacket size for EP0 : 64 bytes */
    0x56,0x04,                      /* Vendor ID */
    0x02,0xEF,                      /* Product ID */
    0x00,0x00,                      /* Device release number */
    0x01,                           /* Manufacture string index */
    0x02,                           /* Product string index */
    0x03,                           /* Serial number string index */
    0x01                            /* Number of configurations */
};

unsigned char gbDevQualDesc[] =
{
    0x0A,                           /* Descriptor Size */
    0x06,                           /* Device Qualifier Descriptor Type */
    0x00,0x02,                      /* USB 2.0 */
    0xFF,                           /* Device Class */
    0xFF,                           /* Device Sub-class */
    0xFF,                           /* Device protocol */
    0x40,                           /* Maxpacket size for EP0 : 64 bytes */
    0x01,                           /* Number of configurations */
    0x00                            /* Reserved */
};

unsigned char gbCfgDesc[] =
{
    0x09,                           /* Descriptor Size */
    0x02,                           /* Configuration Descriptor Type */
    0x12,0x00,                      /* Length of this descriptor and all sub descriptors */
    0x01,                           /* Number of interfaces */
    0x01,                           /* Configuration number */
    0x00,                           /* COnfiguration string index */
    0x80,                           /* Config characteristics - Bus powered */
    0x32,                           /* Max power consumption of device (in 2mA unit) : 100mA */

    /* Interface Descriptor */
    0x09,                           /* Descriptor size */
    0x04,                           /* Interface Descriptor type */
    0x00,                           /* Interface number */
    0x00,                           /* Alternate setting number */
    0x00,                           /* Number of end points */
    0xFF,                           /* Interface class */
    0x00,                           /* Interface sub class */
    0x00,                           /* Interface protocol code */
    0x00,                           /* Interface descriptor string index */
};

unsigned char gbLangIDDesc[] =
{
    0x04,                           /* Descriptor Size */
    0x03,                           /* Device Descriptor Type */
    0x09,0x04                       /* Language ID supported */
};

/* Standard Manufacturer String Descriptor */
unsigned char gbManufactureDesc[] =
{
    0x1E,                           /* Descriptor Size */
    0x03,                           /* Device Descriptor Type */
    'A',0x00,
    'n',0x00,
    'a',0x00,
    'l',0x00,
    'o',0x00,
    'g',0x00,
    ' ',0x00,
    'D',0x00,
    'e',0x00,
    'v',0x00,
    'i',0x00,
    'c',0x00,
    'e',0x00,
    's',0x00
};

unsigned char gbProductDesc[] =
{
    0x2C,                           /* Descriptor Size */
    0x03,                           /* Device Descriptor Type */
    'F',0x00,
    'X',0x00,
    '3',0x00,
    ' ',0x00,
    'B',0x00,
    'o',0x00,
    'o',0x00,
    't',0x00,
    'l',0x00,
    'o',0x00,
    'a',0x00,
    'd',0x00,
    'e',0x00,
    'r',0x00,
    ' ',0x00,
    'v',0x00,
    '1',0x00,
    '.',0x00,
    '0',0x00,
    '.',0x00,
    '1',0x00
};

unsigned char gbSerialNumDesc [] = 
{
    0x22,                           /* bLength */
    0x03,                           /* bDescType */
    '0',0x00,'0',0x00,'0',0x00,'0',0x00,
    '0',0x00,'0',0x00,'0',0x00,'0',0x00,
    '0',0x00,'0',0x00,'0',0x00,'0',0x00,
    '0',0x00,'0',0x00,'0',0x00,'0',0x00,
};

/* SuperSpeed descriptors */

/* Binary Device Object Store Descriptor */
unsigned char gbBosDesc[] =
{
    0x05,                           /* Descriptor Size */
    0x0F,                           /* Device Descriptor Type */
    0x16,0x00,                      /* Length of this descriptor and all sub descriptors */
    0x02,                           /* Number of device capability descriptors */

    /* USB 2.0 Extension */
    0x07,                           /* Descriptor Size */
    0x10,                           /* Device Capability Type descriptor */
    0x02,                           /* USB 2.0 Extension Capability Type */
    0x02,0x00,0x00,0x00,            /* Supported device level features - LPM Support */

    /* SuperSpeed Device Capability */
    0x0A,                           /* Descriptor Size */
    0x10,                           /* Device Capability Type descriptor */
    0x03,                           /* SuperSpeed Device Capability Type */
    0x00,                           /* Supported device level features  */
    0x0E,0x00,                      /* Speeds Supported by the device : SS, HS and FS */
    0x03,                           /* Functionality support */
    0x00,                           /* U1 Device Exit Latency */
    0x00,0x00                       /* U2 Device Exit Latency */
};

/* Standard Super Speed Configuration Descriptor */
unsigned char gbSsConfigDesc[] =
{
    /* Configuration Descriptor Type */
    0x09,                           /* Descriptor Size */
    0x02,                           /* Configuration Descriptor Type */
    0x12,0x00,                      /* Length of this descriptor and all sub descriptors */
    0x01,                           /* Number of interfaces */
    0x01,                           /* Configuration number */
    0x00,                           /* Configuration string index */
    0x80,                           /* Config characteristics - D6: Self power; D5: Remote Wakeup */
    0x32,                           /* Max power consumption of device (in 8mA unit) : 400mA */

    /* Interface Descriptor */
    0x09,                           /* Descriptor size */
    0x04,                           /* Interface Descriptor type */
    0x00,                           /* Interface number */
    0x00,                           /* Alternate setting number */
    0x00,                           /* Number of end points */
    0xFF,                           /* Interface class */
    0x00,                           /* Interface sub class */
    0x00,                           /* Interface protocol code */
    0x00,                           /* Interface descriptor string index */
};

/* Standard Device Descriptor for USB 3.0 */
unsigned char gbSsDevDesc[] =
{
    0x12,                           /* Descriptor Size */
    0x01,                           /* Device Descriptor Type */
    0x10,0x03,                      /* USB 3.10 */
    0x00,                           /* Device Class */
    0x00,                           /* Device Sub-class */
    0x00,                           /* Device protocol */
    0x09,                           /* Maxpacket size for EP0 : 2^9 */
    0x56,0x04,                      /* Vendor ID */
    0x02,0xEF,                      /* Product ID */
    0x00,0x00,                      /* Device release number */
    0x01,                           /* Manufacture string index */
    0x02,                           /* Product string index */
    0x03,                           /* Serial number string index */
    0x01                            /* Number of configurations */
};

/* Standard Full Speed Configuration Descriptor */
unsigned char gbFsConfigDesc[] =
{
    /* Configuration Descriptor Type */
    0x09,                           /* Descriptor Size */
    0x02,                           /* Configuration Descriptor Type */
    0x12,0x00,                      /* Length of this descriptor and all sub descriptors */
    0x01,                           /* Number of interfaces */
    0x01,                           /* Configuration number */
    0x00,                           /* COnfiguration string index */
    0x80,                           /* Config characteristics - Bus powered */
    0x32,                           /* Max power consumption of device (in 2mA unit) : 100mA */

    /* Interface Descriptor */
    0x09,                           /* Descriptor size */
    0x04,       					/* Interface Descriptor type */
    0x00,                           /* Interface number */
    0x00,                           /* Alternate setting number */
    0x00,                           /* Number of end points */
    0xFF,                           /* Interface class */
    0x00,                           /* Interface sub class */
    0x00,                           /* Interface protocol code */
    0x00,                           /* Interface descriptor string index */
};


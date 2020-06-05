using FX3Api;
using RegMapClasses;
using adisInterface;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace FX3ApiWrapper
{
    [ComVisible(true)]
    public class DutInterfaceWrapper
    {
        private FX3Connection m_FX3;
        private RegMapCollection m_RegMap;
        private IDutInterface m_Dut;
        private uint m_NumBytes;
        private List<RegClass> m_StreamRegs;

        /// <summary>
        /// DUT interface wrapper constructor
        /// </summary>
        /// <param name="FX3">FX3 device. Should be connected and configured to use the desired sensor type</param>
        /// <param name="RegMapPath">Path to regmap file for connected sensor</param>
        public DutInterfaceWrapper(FX3Connection FX3, string RegMapPath)
        {
            m_FX3 = FX3;
            UpdateDutType();
            UpdateRegMap(RegMapPath);
        }

        /// <summary>
        /// Update the IDutInterface type based on the current FX3 setting.
        /// For SensorType of ADcmXL will use the appropriate ADcmXL Interface (ADcmXLx021)
        /// For SensorType of AutomotiveSpi will use ZeusInterface (ADIS1655x)
        /// For SensorType of IMU, PartType LegacyIMU will use aducInterface (ADIS1644x)
        /// For SensorType of IMU, PartType IMU, will use adbfInterface (ADIS1647x, ADIS1649x, ADIS1650x)
        /// </summary>
        public void UpdateDutType()
        {
            /* Set number of bytes per reg to 2 */
            if(m_FX3.SensorType == DeviceType.ADcmXL)
            {
                if(m_FX3.PartType == DUTType.ADcmXL1021)
                {
                    m_Dut = new AdcmInterface1Axis(m_FX3, null);
                }
                else if(m_FX3.PartType == DUTType.ADcmXL2021)
                {
                    m_Dut = new AdcmInterface2Axis(m_FX3, null);
                }
                else
                {
                    /* 3021 or other */
                    m_Dut = new AdcmInterface3Axis(m_FX3, null);
                }
            }
            else if(m_FX3.SensorType == DeviceType.AutomotiveSpi)
            {
                m_Dut = new ZeusInterface(m_FX3, null);
            }
            else
            {
                if(m_FX3.PartType == DUTType.LegacyIMU)
                {
                    m_Dut = new aducInterface(m_FX3, null);
                }
                else
                {
                    m_Dut = new adbfInterface(m_FX3, null);
                }
            }
        }

        /// <summary>
        /// Reload the regmap based on provided CSV path
        /// </summary>
        /// <param name="RegMapPath">Path to RegMap CSV file</param>
        public void UpdateRegMap(string RegMapPath)
        {
            m_RegMap = new RegMapCollection();
            m_RegMap.ReadFromCSV(RegMapPath);
        }

        #region "Register Read"

        /// <summary>
        /// Read single unsigned register
        /// </summary>
        /// <param name="RegName">Name of register to read. Must be in RegMap</param>
        /// <returns>Unsigned register value</returns>
        public uint ReadUnsigned(string RegName)
        {
            return m_Dut.ReadUnsigned(m_RegMap[RegName]);
        }

        /// <summary>
        /// Read multiple unsigned registers
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <returns>Array of register read values</returns>
        public uint[] ReadUnsigned(string[] RegNames)
        {
            return ReadUnsigned(RegNames, 1, 1);
        }

        /// <summary>
        /// Read set of multiple unsigned registers, numCaptures times
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <param name="NumCaptures">Number of times to read the register list</param>
        /// <returns>Array of register read values</returns>
        public uint[] ReadUnsigned(string[] RegNames, uint NumCaptures)
        {
            return ReadUnsigned(RegNames, NumCaptures, 1);
        }

        /// <summary>
        /// Read set of multiple unsigned registers, numCaptures times per data ready, numBuffers total captures
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <param name="NumCaptures">Number of times to read the register list</param>
        /// <param name="NumBuffers">Number of register captures to read</param>
        /// <returns>Array of register read values</returns>
        public uint[] ReadUnsigned(string[] RegNames, uint NumCaptures, uint NumBuffers)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            return m_Dut.ReadUnsigned(RegList, NumCaptures, NumBuffers);
        }

        /// <summary>
        /// Read single signed register
        /// </summary>
        /// <param name="RegName">Name of register to read. Must be in RegMap</param>
        /// <returns>Signed register value</returns>
        public long ReadSigned(string RegName)
        {
            return m_Dut.ReadSigned(m_RegMap[RegName]);
        }

        /// <summary>
        /// Read multiple signed registers
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <returns>Array of register read values</returns>
        public long[] ReadSigned(string[] RegNames)
        {
            return ReadSigned(RegNames, 1, 1);
        }

        /// <summary>
        /// Read set of multiple signed registers, numCaptures times
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <param name="NumCaptures">Number of times to read the register list</param>
        /// <returns>Array of register read values</returns>
        public long[] ReadSigned(string[] RegNames, uint NumCaptures)
        {
            return ReadSigned(RegNames, NumCaptures, 1);
        }

        /// <summary>
        /// Read set of multiple signed registers, numCaptures times per data ready, numBuffers total captures
        /// </summary>
        /// <param name="RegNames">Names of all registers to read</param>
        /// <param name="NumCaptures">Number of times to read the register list</param>
        /// <param name="NumBuffers">Number of register captures to read</param>
        /// <returns>Array of register read values</returns>
        public long[] ReadSigned(string[] RegNames, uint NumCaptures, uint NumBuffers)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            return m_Dut.ReadSigned(RegList, NumCaptures, NumBuffers);
        }

        #endregion

        #region "Register Write"

        /// <summary>
        /// Write an unsigned value to a single register in the RegMap
        /// </summary>
        /// <param name="RegName">Name of register to write</param>
        /// <param name="WriteData">Data to write to the register</param>
        public void WriteUnsigned(string RegName, uint WriteData)
        {
            m_Dut.WriteUnsigned(m_RegMap[RegName], WriteData);
        }

        /// <summary>
        /// Write unsigned values to multiple registers in the RegMap
        /// </summary>
        /// <param name="RegNames">Names of registers to write</param>
        /// <param name="WriteData">Data to write to the registers</param>
        public void WriteUnsigned(string[] RegNames, uint[] WriteData)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_Dut.WriteUnsigned(RegList, WriteData);
        }

        /// <summary>
        /// Write a signed value to a single register in the RegMap
        /// </summary>
        /// <param name="RegName">Name of register to write</param>
        /// <param name="WriteData">Data to write to the register</param>
        public void WriteSigned(string RegName, int WriteData)
        {
            m_Dut.WriteSigned(m_RegMap[RegName], WriteData);
        }

        /// <summary>
        /// Write signed values to multiple registers in the RegMap
        /// </summary>
        /// <param name="RegNames">Names of registers to write</param>
        /// <param name="WriteData">Data to write to the registers</param>
        public void WriteSigned(string[] RegNames, int[] WriteData)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_Dut.WriteSigned(RegList, WriteData);
        }

        #endregion

        #region "Register Stream"

        /// <summary>
        /// Start an asynchronous buffered register read stream
        /// </summary>
        /// <param name="RegNames">List of register names to read</param>
        /// <param name="NumCaptures">Number of times to read register list per data ready</param>
        /// <param name="NumBuffers">Total number of captures to perform</param>
        /// <param name="TimeoutSeconds">Stream timeout time, in seconds</param>
        public void StartBufferedStream(string[] RegNames, uint NumCaptures, uint NumBuffers, int TimeoutSeconds)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_StreamRegs = RegList;
            m_Dut.StartBufferedStream(RegList, NumCaptures, NumBuffers, 10, null);
        }

        /// <summary>
        /// Get a buffered stream data packet
        /// </summary>
        /// <returns>A single buffer from a stream. Will be null if no data available</returns>
        public ushort[] GetBufferedStreamDataPacket()
        {
            return m_Dut.GetBufferedStreamDataPacket();
        }

        /// <summary>
        /// Converted buffered stream data packet to 32-bit array, based on the size of the registers read
        /// </summary>
        /// <param name="buf">Raw buffer packet to convert</param>
        /// <returns>32-bit unsigned array representing the value of each register read</returns>
        public uint[] ConvertBufferDataToU32(ushort[] buf)
        {
            return m_Dut.ConvertReadDataToU32(m_StreamRegs, buf);
        }

        #endregion
    }
}

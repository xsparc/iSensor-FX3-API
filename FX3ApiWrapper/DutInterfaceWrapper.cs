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

        public DutInterfaceWrapper(FX3Connection FX3, string RegMapPath)
        {
            m_FX3 = FX3;
            UpdateDutType();
            UpdateRegMap(RegMapPath);
        }

        public void UpdateDutType()
        {
            /* Set number of bytes per reg to 2 */
            RegNumBytes = 2;
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
                /* 4 bytes per reg */
                RegNumBytes = 4;
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

        public void UpdateRegMap(string RegMapPath)
        {
            m_RegMap = new RegMapCollection();
            m_RegMap.ReadFromCSV(RegMapPath);
        }

        public uint RegNumBytes
        {
            get
            {
                return m_NumBytes;
            }
            set
            {
                m_NumBytes = value;
            }
        }

        #region "Register Read"

        public uint ReadUnsigned(string RegName)
        {
            return m_Dut.ReadUnsigned(m_RegMap[RegName]);
        }

        public uint[] ReadUnsigned(string[] RegNames)
        {
            return ReadUnsigned(RegNames, 1, 1);
        }

        public uint[] ReadUnsigned(string[] RegNames, uint NumCaptures)
        {
            return ReadUnsigned(RegNames, NumCaptures, 1);
        }

        public uint[] ReadUnsigned(string[] RegNames, uint NumCaptures, uint NumBuffers)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            return m_Dut.ReadUnsigned(RegList, NumCaptures, NumBuffers);
        }

        public long ReadSigned(string RegName)
        {
            return m_Dut.ReadSigned(m_RegMap[RegName]);
        }

        public long[] ReadSigned(string[] RegNames)
        {
            return ReadSigned(RegNames, 1, 1);
        }

        public long[] ReadSigned(string[] RegNames, uint NumCaptures)
        {
            return ReadSigned(RegNames, NumCaptures, 1);
        }

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

        public void WriteUnsigned(string RegName, uint WriteData)
        {
            m_Dut.WriteUnsigned(m_RegMap[RegName], WriteData);
        }

        public void WriteUnsigned(string[] RegNames, uint[] WriteData)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_Dut.WriteUnsigned(RegList, WriteData);
        }

        public void WriteSigned(string[] RegNames, int[] WriteData)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_Dut.WriteSigned(RegList, WriteData);
        }

        public void WriteSigned(string RegName, int WriteData)
        {
            m_Dut.WriteSigned(m_RegMap[RegName], WriteData);
        }

        #endregion

        #region "Register Stream"

        public void StartBufferedStream(string[] RegNames, uint NumCaptures, uint NumBuffers, int TimeoutSeconds)
        {
            List<RegClass> RegList = new List<RegClass>();
            foreach (string name in RegNames)
            {
                RegList.Add(m_RegMap[name]);
            }
            m_Dut.StartBufferedStream(RegList, NumCaptures, NumBuffers, 10, null);
        }

        public ushort[] GetBufferedStreamDataPacket()
        {
            return m_Dut.GetBufferedStreamDataPacket();
        }

        #endregion
    }
}

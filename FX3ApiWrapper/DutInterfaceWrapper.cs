using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FX3Api;
using RegMapClasses;
using adisInterface;

namespace FX3ApiWrapper
{
    public class DutInterfaceWrapper
    {
        private FX3Connection m_FX3;
        private RegMapCollection m_RegMap;
        private IDutInterface m_Dut;

        public DutInterfaceWrapper(FX3Connection FX3, string RegMapPath)
        {
            m_FX3 = FX3;
            UpdateDutType();
            UpdateRegMap(RegMapPath);
        }

        public void UpdateDutType()
        {
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

        public void UpdateRegMap(string RegMapPath)
        {
            m_RegMap = new RegMapCollection();
            m_RegMap.ReadFromCSV(RegMapPath);
        }
    }
}

using System;
using System.Linq;
using FX3Api;
using System.Runtime.InteropServices;

namespace FX3ApiWrapper
{
    [ComVisible(true)]
    public class FX3Wrapper
    {
        /// <summary>
        /// Underlying FX3 connection object
        /// </summary>
        public FX3Connection FX3;

        public FX3Wrapper(string FX3ResourcePath)
        {
            FX3 = new FX3Connection(FX3ResourcePath, FX3ResourcePath, FX3ResourcePath);
            ConnectToBoard();
        }

        public void Disconnect()
        {
            FX3.Disconnect();
        }

        private void ConnectToBoard()
        {
            FX3.WaitForBoard(2);
            if(FX3.AvailableFX3s.Count() > 0)
            {
                FX3.Connect(FX3.AvailableFX3s[0]);
            }
            else if(FX3.BusyFX3s.Count() > 0)
            {
                FX3.ResetAllFX3s();
                FX3.WaitForBoard(5);
                ConnectToBoard();
            }
            else
            {
                throw new Exception("No FX3 board connected!");
            }
        }
    }
}

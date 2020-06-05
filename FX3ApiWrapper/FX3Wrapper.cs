using System;
using System.Linq;
using FX3Api;
using System.Runtime.InteropServices;

namespace FX3ApiWrapper
{
    /// <summary>
    /// Simplified wrapper around FX3 connection object
    /// </summary>
    [ComVisible(true)]
    public class FX3Wrapper
    {
        /// <summary>
        /// Underlying FX3 connection object
        /// </summary>
        public FX3Connection FX3;

        /// <summary>
        /// FX3Wrapper constructor
        /// </summary>
        /// <param name="FX3ResourcePath">Path the FX3 firmware binaries</param>
        public FX3Wrapper(string FX3ResourcePath)
        {
            FX3 = new FX3Connection(FX3ResourcePath, FX3ResourcePath, FX3ResourcePath);
            ConnectToBoard();
        }

        /// <summary>
        /// Class destructor. Disconnects FX3
        /// </summary>
        ~FX3Wrapper()
        {
            Disconnect();
        }

        /// <summary>
        /// Disconnect FX3 board
        /// </summary>
        public void Disconnect()
        {
            FX3.Disconnect();
        }

        /// <summary>
        /// Connect to FX3 board
        /// </summary>
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

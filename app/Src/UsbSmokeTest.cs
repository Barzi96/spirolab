using System;
using SpiroLab.Device;
namespace SpiroLab.Tests
{
    internal static class UsbSmokeTest
    {
        private static int Main()
        {
            // Identification only: no FVC start, reset, flash or raw commands.
            using(DeviceSession session=new DeviceSession())
            {
                session.Line+=Console.WriteLine;
                string error;
                if(!session.Connect(out error)) { Console.WriteLine("NOT CONNECTED: "+error); return 2; }
                Console.WriteLine("CONNECTED: "+session.ModelName+" "+session.SerialNumber);
                Console.WriteLine("Log: "+session.LogPath); return 0;
            }
        }
    }
}

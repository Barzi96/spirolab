using System;
using System.IO;

internal static class SelfTestHost
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 1) throw new ArgumentException("output directory required");
            SpiroLab.DeploymentSelfTest.Run(args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                if (args.Length > 0)
                {
                    Directory.CreateDirectory(args[0]);
                    File.WriteAllText(Path.Combine(args[0], "selftest-host-failed.txt"), ex.ToString());
                }
            }
            catch { }
            return 1;
        }
    }
}

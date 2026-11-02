using System;
using System.Windows.Forms;
using System.Threading;
using SpiroLab.Ui;

namespace SpiroLab
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
#if SPIROLAB_TEST
            if(args.Length>=2 && args[0]=="--self-test")
            {
                try { DeploymentSelfTest.Run(args[1]); Environment.ExitCode=0; }
                catch(Exception ex) { System.IO.Directory.CreateDirectory(args[1]); System.IO.File.WriteAllText(System.IO.Path.Combine(args[1],"selftest-failed.txt"),ex.ToString()); Environment.ExitCode=1; }
                return;
            }
#endif
            if(args.Length>=2 && args[0]=="--diagnostics")
            { System.IO.File.WriteAllText(args[1],Device.DeviceDiagnostics.Read().SafeText(),System.Text.Encoding.UTF8); return; }
            bool created;
            using (Mutex mutex = new Mutex(true, "Local\\SpiroLab2.SingleInstance", out created))
            {
                if (!created) { MessageBox.Show("SpiroLab از قبل باز است؛ از همان پنجره استفاده کنید.", "SpiroLab"); return; }
                try { Run(args); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void Run(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool connect = false;
            if (args != null)
                foreach (string a in args)
                    if (string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)) connect = true;

            try
            {
                MainForm f = new MainForm();
                if (connect) f.Shown += delegate { f.ConnectOnLaunch(); };
                f.ProbeFirefoxSession();
                Application.Run(f);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "خطای اجرا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

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
            if(args.Length>=2 && args[0]=="--self-test")
            {
                try { DeploymentSelfTest.Run(args[1]); Environment.ExitCode=0; }
                catch(Exception ex) { System.IO.Directory.CreateDirectory(args[1]); System.IO.File.WriteAllText(System.IO.Path.Combine(args[1],"selftest-failed.txt"),ex.ToString()); Environment.ExitCode=1; }
                return;
            }
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

            bool demo = false;
            bool connect = false;
            int tab = -1;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                    if (string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)) connect = true;
                    if (a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(a.Substring(6), out v)) tab = v;
                    }
                }
            }

            try
            {
                MainForm f = new MainForm();
                if (tab >= 0) f.Shown += delegate { f.SelectTab(tab); };
                if (demo)
                {
                    f.Shown += delegate { f.RunDemoForTest(); };
                }
                else if (connect) f.Shown += delegate { f.ConnectOnLaunch(); };
                Application.Run(f);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "خطای اجرا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}

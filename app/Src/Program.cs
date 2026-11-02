using System;
using System.Windows.Forms;
using System.Threading;
using SpiroLab.Ui;
using SpiroLab.HLine;

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
            bool allowProductionWrites = false;
            string testNationalId = "", testRecordId = "";
            int tab = -1;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase)) demo = true;
                    if (string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)) connect = true;
                    if (string.Equals(a, "--hline-write=all", StringComparison.OrdinalIgnoreCase)) allowProductionWrites = true;
                    if (a.StartsWith("--hline-test=", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = a.Substring("--hline-test=".Length);
                        int colon = value.IndexOf(':');
                        if (colon > 0) { testNationalId = value.Substring(0, colon).Trim(); testRecordId = value.Substring(colon + 1).Trim(); }
                    }
                    if (a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase))
                    {
                        int v;
                        if (int.TryParse(a.Substring(6), out v)) tab = v;
                    }
                }
            }
            // Two independent write scopes must never be combined by accident. A partial
            // test scope is also refused instead of silently becoming production or no-write.
            bool partialTest = (testNationalId.Length == 0) != (testRecordId.Length == 0);
            if (allowProductionWrites && (testNationalId.Length > 0 || testRecordId.Length > 0))
            {
                MessageBox.Show("حالت «ارسال تولیدی» و «پروندهٔ تست» را نمی‌توان هم‌زمان فعال کرد. برنامه بسته می‌شود.", "SpiroLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (partialTest || (testNationalId.Length > 0 && (!System.Text.RegularExpressions.Regex.IsMatch(testNationalId, "^[0-9]{10}$") || testRecordId.Length == 0)))
            {
                MessageBox.Show("پروندهٔ تست باید به شکل --hline-test=<کد ملی ۱۰ رقمی>:<شناسهٔ پرونده> باشد. برنامه بسته می‌شود.", "SpiroLab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                MainForm f = allowProductionWrites
                    ? new MainForm(new HLineClient(), testNationalId, testRecordId, true)
                    : testNationalId.Length > 0 && testRecordId.Length > 0
                    ? new MainForm(new HLineClient(), testNationalId, testRecordId, false)
                    : new MainForm();
                if (tab >= 0) f.Shown += delegate { f.SelectTab(tab); };
                if (demo)
                {
                    f.Shown += delegate { f.RunDemoForTest(); };
                }
                else if (connect) f.Shown += delegate { f.ConnectOnLaunch(); };
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

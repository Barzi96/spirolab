using System;
using System.IO;
using System.Windows.Forms;

namespace MirParityLab.App
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    WriteStartupError(e.Exception);
                    MessageBox.Show("MIR Parity Lab encountered an error.\n\n" + e.Exception.Message,
                        "MIR Parity Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                MainForm form = new MainForm();
                Application.Run(form);
            }
            catch (Exception ex)
            {
                WriteStartupError(ex);
                MessageBox.Show("MIR Parity Lab could not start.\n\n" + ex.Message,
                    "MIR Parity Lab", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteStartupError(Exception ex)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-error.log");
                File.WriteAllText(path, DateTime.UtcNow.ToString("o") + Environment.NewLine + ex);
            }
            catch { }
        }
    }
}

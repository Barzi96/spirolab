using System;
using System.Reflection;
using System.Windows.Forms;
using MirParityLab.App;
using MirParityLab.Core;

internal static class UiSmoke
{
    private static int _fail;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs e)
        {
            Console.WriteLine("FAIL  thread exception: " + e.Exception.Message);
            Environment.Exit(2);
        };
        MainForm form = new MainForm();
        form.Show();
        Application.DoEvents();
        Call(form, "LoadSyntheticDemo");
        Application.DoEvents();

        ComparisonReport report = (ComparisonReport)Field(form, "_report");
        DataGridView compare = (DataGridView)Field(form, "_compareGrid");
        DataGridView packets = (DataGridView)Field(form, "_packetGrid");
        DataGridView fields = (DataGridView)Field(form, "_fieldGrid");
        Check(report != null && report.RowCount == 6, "synthetic demo produces 6 comparison rows");
        Check(compare.Rows.Count == 6, "comparison grid is bound to 6 rows");
        Check(packets.Rows.Count == 120, "packet grid is bound to 120 rows");
        Check(fields.Rows.Count == 6, "field grid is bound to 6 rows");
        Check(compare.Rows.Count > 0 && Convert.ToString(compare.Rows[0].Cells[1].Value) == "FVC", "comparison grid exposes parameter names");

        Control tab = null, header = null, status = null;
        foreach (Control c in form.Controls)
        {
            if (c is TabControl) tab = c;
            else if (c is StatusStrip) status = c;
            else if (c is Panel && c.Dock == DockStyle.Top) header = c;
        }
        Check(header != null && tab != null && status != null, "header, tab and status controls exist");
        if (header != null && tab != null && status != null)
        {
            Check(tab.Top >= header.Bottom, "tab starts below header");
            Check(tab.Bottom <= status.Top, "tab ends above status bar");
            Check(tab.Height > 300, "tab has usable height");
        }

        Console.WriteLine("UI SMOKE RESULT: " + (_fail == 0 ? "PASS" : "FAIL"));
        form.Close();
        return _fail == 0 ? 0 : 1;
    }

    private static object Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void Call(object target, string name)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name);
        if (!ok) _fail++;
    }
}

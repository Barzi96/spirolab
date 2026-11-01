using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SpiroLab.Device;
namespace SpiroLab.Ui
{
    public sealed class DiagnosticsForm : Form
    {
        public DiagnosticsForm(DeviceDiagnostics state)
        {
            Text="وضعیت دستگاه"; Size=new Size(760,480); StartPosition=FormStartPosition.CenterParent; RightToLeft=RightToLeft.Yes; Font=new Font("Tahoma",10);
            TextBox text=new TextBox { Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=state.SafeText(),BackColor=Color.White };
            Button save=new Button { Text="ذخیره گزارش عیب‌یابی",Dock=DockStyle.Bottom,Height=40 };
            save.Click+=delegate { using(SaveFileDialog dialog=new SaveFileDialog { Filter="گزارش بدون اطلاعات شخصی|*.txt",FileName="SpiroLab-diagnostics.txt" }) if(dialog.ShowDialog(this)==DialogResult.OK) File.WriteAllText(dialog.FileName,state.SafeText(),new UTF8Encoding(true)); };
            Controls.Add(text); Controls.Add(save);
        }
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;
namespace SpiroLab.Ui
{
    public sealed class SettingsForm : Form
    {
        public AppSettings Value { get; private set; }
        public SettingsForm(AppSettings current)
        {
            Text="تنظیمات"; Size=new Size(650,230); StartPosition=FormStartPosition.CenterParent; Font=new Font("Tahoma",10); RightToLeft=RightToLeft.Yes;
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(15),ColumnCount=2,RowCount=3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            TextBox center=new TextBox { Text=current.CenterName,Dock=DockStyle.Fill,MaxLength=200 },op=new TextBox { Text=current.OperatorName,Dock=DockStyle.Fill,MaxLength=100 };
            layout.Controls.Add(new Label { Text="نام مرکز",AutoSize=true },0,0); layout.Controls.Add(center,1,0);
            layout.Controls.Add(new Label { Text="کارشناس پیش‌فرض",AutoSize=true },0,1); layout.Controls.Add(op,1,1);
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Fill,AutoSize=true };
            Button save=new Button { Text="ذخیره",AutoSize=true },restore=new Button { Text="بازنشانی پیش‌فرض",AutoSize=true };
            restore.Click+=delegate { if(MessageBox.Show(this,"تنظیمات پیش‌فرض بازگردانده شود؟","تنظیمات",MessageBoxButtons.YesNo)==DialogResult.Yes) { center.Text=AppSettings.DefaultCenter; op.Clear(); } };
            save.Click+=delegate { try { AppSettings settings=new AppSettings { CenterName=center.Text.Trim(),OperatorName=op.Text.Trim() }; settings.Save(); Value=settings; DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(this,ex.Message); } };
            actions.Controls.Add(save); actions.Controls.Add(restore); layout.Controls.Add(actions,0,2); layout.SetColumnSpan(actions,2); Controls.Add(layout);
        }
    }
}

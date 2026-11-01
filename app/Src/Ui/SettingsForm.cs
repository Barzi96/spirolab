using System;
using System.Drawing;
using System.Windows.Forms;
using SpiroLab.Device;
namespace SpiroLab.Ui
{
    public sealed class SettingsForm : Form
    {
        public AppSettings Value { get; private set; }
        public bool RunDevelopmentDemo { get; private set; }
        public SettingsForm(AppSettings current,Func<DeviceDiagnostics> readDiagnostics=null,Action<Action> connect=null)
        {
            ClinicalStyle.Form(this,"تنظیمات",new Size(740,640));
            Panel body=new Panel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White }; Controls.Add(body);
            TableLayoutPanel table=ClinicalStyle.FormTable(); table.RowCount=0; body.Controls.Add(table);
            TextBox center=ClinicalStyle.TextField("DefaultCenter",current.CenterName),op=ClinicalStyle.TextField("DefaultOperator",current.OperatorName); center.MaxLength=200; op.MaxLength=100;
            TextBox firefox=ClinicalStyle.TextField("FirefoxProfilePath",current.FirefoxProfile); firefox.MaxLength=400;
            ClinicalStyle.Row(table,"نام مرکز",center,46); ClinicalStyle.Row(table,"کارشناس پیش‌فرض",op,46);
            ClinicalStyle.Row(table,"پروفایل فایرفاکس (اختیاری)",firefox,46);
            ClinicalStyle.Row(table,"راهنما",new Label { Text="خالی بگذارید تا پروفایلی که نشست معتبر اچ‌لاین دارد خودکار انتخاب شود.",AutoSize=true },36);
            ClinicalStyle.Row(table,"معاینه‌های قبلی",new Label { Text="این تنظیمات برای معاینهٔ جدید است؛ سوابق قبلی تغییر نمی‌کنند.",AutoSize=true },55);
            DeviceDiagnostics diagnostics=readDiagnostics==null?new DeviceDiagnostics():readDiagnostics();
            Label state=new Label { Name="DeviceState",Text=diagnostics.PersianSummary,AutoSize=true };
            ClinicalStyle.Row(table,"وضعیت دستگاه",state,58);
            Label driver=new Label { Text=diagnostics.DriverName+"  "+diagnostics.DriverVersion+" | "+diagnostics.DriverState,AutoSize=true,RightToLeft=RightToLeft.No };
            ClinicalStyle.Row(table,"درایور MIRUSB",driver,45);
            Button connectButton=ClinicalStyle.Button("بررسی اتصال دستگاه",null,false); connectButton.Name="TestDeviceConnection"; connectButton.Enabled=connect!=null;
            connectButton.Click+=delegate { connectButton.Enabled=false; state.Text="در حال بررسی اتصال…"; connect(delegate { if(IsDisposed) return; diagnostics=readDiagnostics(); state.Text=diagnostics.PersianSummary; driver.Text=diagnostics.DriverName+" "+diagnostics.DriverVersion+" | "+diagnostics.DriverState; connectButton.Enabled=true; }); };
            ClinicalStyle.Row(table,"ارتباط",connectButton,50);
            Button details=ClinicalStyle.Button("جزئیات و ذخیرهٔ گزارش تشخیصی",delegate { using(DiagnosticsForm form=new DiagnosticsForm(readDiagnostics==null?diagnostics:readDiagnostics())) form.ShowDialog(this); },false); details.Name="DeviceDiagnostics";
            ClinicalStyle.Row(table,"گزارش بدون اطلاعات بیمار",details,50);
            LinkLabel development=new LinkLabel { Text="توسعه و آزمون",AutoSize=true,LinkColor=Color.FromArgb(104,121,137),LinkBehavior=LinkBehavior.HoverUnderline };
            ContextMenuStrip menu=new ContextMenuStrip(); menu.Items.Add("افزودن مانور نمایشی — دادهٔ ساختگی",null,delegate { RunDevelopmentDemo=true; DialogResult=DialogResult.Ignore; }); development.LinkClicked+=delegate { menu.Show(development,new Point(0,development.Height)); };
            ClinicalStyle.Row(table,"فقط برای بررسی نرم‌افزار",development,40);
            ClinicalStyle.Row(table,"نسخهٔ ۳۲بیتی",new Label { Text="سقف فنی فایل معاینه: ۴۰ MiB؛ تعداد مانورها محدود نیست.",AutoSize=true },44);
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,Padding=new Padding(12,4,12,4) };
            Button save=ClinicalStyle.Button("ذخیره تنظیمات",delegate { try { AppSettings value=new AppSettings { CenterName=center.Text.Trim(),OperatorName=op.Text.Trim(),FirefoxProfile=firefox.Text.Trim() }; value.Save(); Value=value; DialogResult=DialogResult.OK; } catch(Exception ex) { MessageBox.Show(this,ex.Message); } },true);
            Button restore=ClinicalStyle.Button("بازنشانی پیش‌فرض",delegate { if(MessageBox.Show(this,"تنظیمات پیش‌فرض بازگردانده شود؟","تنظیمات",MessageBoxButtons.YesNo)==DialogResult.Yes) { center.Text=AppSettings.DefaultCenter; op.Clear(); } },false);
            Button cancel=ClinicalStyle.Button("بستن",delegate { DialogResult=DialogResult.Cancel; },false); actions.Controls.Add(save); actions.Controls.Add(restore); actions.Controls.Add(cancel); Controls.Add(actions); CancelButton=cancel;
            FormClosed+=delegate { menu.Dispose(); };
        }
    }
}

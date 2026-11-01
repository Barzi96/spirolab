using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpiroLab.HLine;
namespace SpiroLab.Ui
{
    public sealed class HLineLoginForm : Form
    {
        readonly HLineClient client;
        readonly TextBox username,password,mfa;
        readonly Label status;
        readonly Button login;
        bool working;
        public HLineLoginForm(HLineClient client)
        {
            this.client=client; ClinicalStyle.Form(this,"ورود به HLine",new Size(590,340));
            TableLayoutPanel table=ClinicalStyle.FormTable(); Controls.Add(table);
            username=ClinicalStyle.TextField("HLineUsername",""); password=ClinicalStyle.TextField("HLinePassword",""); password.UseSystemPasswordChar=true;
            mfa=ClinicalStyle.TextField("HLineMfa",""); mfa.MaxLength=6;
            ClinicalStyle.Row(table,"نام کاربری",username,48); ClinicalStyle.Row(table,"رمز عبور",password,48); ClinicalStyle.Row(table,"کد تأیید دومرحله‌ای",mfa,48);
            status=new Label {Name="HLineStatus",Dock=DockStyle.Bottom,Height=40,Text=client.StatusText,Padding=new Padding(15,5,15,5)}; Controls.Add(status);
            login=ClinicalStyle.Button("ورود به سامانه",async delegate { await Login(); },true); login.Name="HLineLogin"; login.Dock=DockStyle.Bottom; Controls.Add(login); AcceptButton=login;
            FormClosing+=delegate(object s,FormClosingEventArgs e) {e.Cancel=working;};
        }
        async Task Login()
        {
            working=true; login.Enabled=false; status.Text="در حال ورود…";
            string u=username.Text,p=password.Text,c=mfa.Text;
            password.Clear(); mfa.Clear();
            try { await Task.Factory.StartNew(delegate { client.Login(u,p,c); }); status.Text=client.StatusText; DialogResult=DialogResult.OK; }
            catch(HLineException e) {status.Text=e.Message;}
            catch {status.Text="عدم دسترسی به سامانه";}
            finally {p=c=null; working=false;login.Enabled=true;if(client.Status==SessionStatus.Connected) Close();}
        }
    }
    public sealed class MedicalRecordForm : Form
    {
        public MedicalRecord Value {get;private set;}
        public MedicalRecordForm(IList<MedicalRecord> records,MedicalRecord suggested)
        {
            ClinicalStyle.Form(this,"انتخاب و تأیید پروندهٔ مقصد",new Size(660,340));
            ListBox list=new ListBox {Name="MedicalRecords",Dock=DockStyle.Fill,IntegralHeight=false}; foreach(var r in records) list.Items.Add(r); list.SelectedIndex=-1; Controls.Add(list);
            Label info=new Label {Dock=DockStyle.Top,Height=60,Text="پروندهٔ همین مراجعه را تأیید کنید. ترتیب بر اساس تاریخ پذیرش است؛ شناسهٔ بزرگ‌تر معیار تازگی نیست."}; Controls.Add(info);
            FlowLayoutPanel buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=58};
            buttons.Controls.Add(ClinicalStyle.Button("تأیید پرونده",delegate {if(list.SelectedItem!=null) {Value=(MedicalRecord)list.SelectedItem;DialogResult=DialogResult.OK;}},true));
            buttons.Controls.Add(ClinicalStyle.Button("انصراف",delegate {DialogResult=DialogResult.Cancel;},false)); Controls.Add(buttons);
            if(suggested!=null) list.SelectedItem=suggested;
        }
    }
    public sealed class SubmissionForm : Form
    {
        public SubmissionForm(SubmissionDraft draft,string blockedReason)
        {
            ClinicalStyle.Form(this,"بازبینی ارسال به سایت",new Size(720,650));
            TextBox preview=new TextBox {Name="SubmissionPreview",Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Text=draft.Preview,BackColor=Color.White,TabStop=false}; Controls.Add(preview);
            Shown+=delegate {preview.Select(0,0);};
            Label status=new Label {Dock=DockStyle.Bottom,Height=65,Text=blockedReason,Padding=new Padding(10),ForeColor=Color.DarkRed}; Controls.Add(status);
            FlowLayoutPanel actions=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=60};
            Button confirm=ClinicalStyle.Button("تأیید و ارسال",delegate {DialogResult=DialogResult.OK;},true); confirm.Name="ConfirmSubmission"; confirm.Enabled=string.IsNullOrEmpty(blockedReason); actions.Controls.Add(confirm);
            actions.Controls.Add(ClinicalStyle.Button("پیش‌نمایش JPG",delegate {using(ReportForm report=new ReportForm(draft.Snapshot)) report.ShowDialog(this);},false));
            actions.Controls.Add(ClinicalStyle.Button("انصراف",delegate {DialogResult=DialogResult.Cancel;},false)); Controls.Add(actions);
        }
    }
}

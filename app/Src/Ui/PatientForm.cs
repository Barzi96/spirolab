using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
namespace SpiroLab.Ui
{
    public sealed class PatientForm : Form
    {
        readonly Patient original;
        readonly TextBox nid,first,last,age,height,weight;
        readonly ComboBox sex,origin;
        readonly Label error;
        public Patient Value { get; private set; }
        public PatientForm(Patient patient=null,bool readOnly=false)
        {
            original=patient==null?new Patient { Origin=Origin.Caucasian }:patient.Clone();
            ClinicalStyle.Form(this,readOnly?"مشخصات مراجع":"ثبت مراجع جدید",new Size(630,570));
            Panel body=new Panel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White }; Controls.Add(body);
            TableLayoutPanel table=ClinicalStyle.FormTable(); table.RowCount=0; body.Controls.Add(table);
            nid=ClinicalStyle.TextField("NationalId",patient==null?"":original.NationalId); nid.MaxLength=10;
            first=ClinicalStyle.TextField("FirstName",patient==null?"":original.FirstName); first.MaxLength=100;
            last=ClinicalStyle.TextField("LastName",patient==null?"":original.LastName); last.MaxLength=100;
            age=ClinicalStyle.TextField("Age",patient==null?"":original.Age.ToString());
            height=ClinicalStyle.TextField("Height",patient==null?"":Fmt.N(original.HeightCm,1));
            weight=ClinicalStyle.TextField("Weight",patient==null?"":Fmt.N(original.WeightKg,1));
            sex=new ComboBox { Name="Sex",DropDownStyle=ComboBoxStyle.DropDownList }; sex.Items.AddRange(new object[] {"مرد","زن"}); sex.SelectedIndex=(int)original.Sex;
            origin=new ComboBox { Name="Origin",DropDownStyle=ComboBoxStyle.DropDownList }; origin.Items.AddRange(new object[] {"Caucasian","انتخاب نشده","سایر"}); origin.SelectedIndex=(int)original.Origin;
            ClinicalStyle.Row(table,"کد ملی",nid,44); ClinicalStyle.Row(table,"نام",first,44); ClinicalStyle.Row(table,"نام خانوادگی",last,44);
            ClinicalStyle.Row(table,"سن (سال)",age,44); ClinicalStyle.Row(table,"جنسیت",sex,44); ClinicalStyle.Row(table,"گروه مرجع",origin,44);
            ClinicalStyle.Row(table,"قد (سانتی‌متر)",height,44); ClinicalStyle.Row(table,"وزن (کیلوگرم)",weight,44);
            foreach(Control c in new Control[] {nid,first,last,age,height,weight}) c.Enabled=!readOnly;
            sex.Enabled=origin.Enabled=!readOnly;
            error=new Label { Name="ValidationMessage",Dock=DockStyle.Bottom,Height=35,ForeColor=Color.FromArgb(165,55,55),Padding=new Padding(16,0,16,0) }; Controls.Add(error);
            foreach(TextBox field in new TextBox[] {nid,first,last,age,height,weight}) field.TextChanged+=delegate { error.Text=""; };
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,Padding=new Padding(12,4,12,4) };
            Button save=ClinicalStyle.Button(readOnly?"بستن":"ذخیره و ادامه",delegate { if(readOnly) { DialogResult=DialogResult.Cancel; return; } Patient p; string message; if(TryRead(out p,out message)) { Value=p; DialogResult=DialogResult.OK; } else error.Text=message; },true); save.Name="SavePatient";
            Button cancel=ClinicalStyle.Button("انصراف",delegate { DialogResult=DialogResult.Cancel; },false); cancel.Name="CancelPatient";
            actions.Controls.Add(save); if(!readOnly) actions.Controls.Add(cancel); Controls.Add(actions); AcceptButton=save; CancelButton=cancel;
        }
        public static bool ValidatePatient(Patient p,out string message)
        {
            message="";
            if(p==null || !Regex.IsMatch(p.NationalId??"", "^[0-9]{10}$")) { message="کد ملی ده‌رقمی را وارد کنید."; return false; }
            if(string.IsNullOrWhiteSpace(p.FirstName) || string.IsNullOrWhiteSpace(p.LastName)) { message="نام و نام خانوادگی را وارد کنید."; return false; }
            return p.IsUsable(out message);
        }
        public bool TryRead(out Patient p,out string message)
        {
            p=original.Clone(); message="";
            double a=Fmt.Parse(age.Text,double.NaN);
            if(double.IsNaN(a) || a!=Math.Floor(a) || a<5 || a>110) { message="سن صحیح بین ۵ تا ۱۱۰ سال وارد کنید."; return false; }
            p.NationalId=Fmt.NormalizeNumber(nid.Text); p.FirstName=first.Text.Trim(); p.LastName=last.Text.Trim(); p.Age=(int)a;
            p.Sex=(Sex)sex.SelectedIndex; p.Origin=(Origin)origin.SelectedIndex; p.HeightCm=Fmt.Parse(height.Text,double.NaN); p.WeightKg=Fmt.Parse(weight.Text,double.NaN);
            return ValidatePatient(p,out message);
        }
    }
}

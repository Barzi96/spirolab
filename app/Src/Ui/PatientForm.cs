using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Threading.Tasks;
using SpiroLab.HLine;
namespace SpiroLab.Ui
{
    public sealed class PatientForm : Form
    {
        readonly Patient original;
        readonly TextBox nid,first,last,age,height,weight,birth;
        readonly ComboBox sex,origin;
        readonly Label error;
        readonly HLineClient hline;
        readonly DateTime examinationDate;
        CheckBox confirmAnthropometry;
        Label serverName;
        bool fetching;
        public Patient Value { get; private set; }
        public PatientForm(Patient patient=null,bool readOnly=false,HLineClient hline=null,DateTime? examinationDate=null)
        {
            this.hline=hline; this.examinationDate=examinationDate??DateTime.Today;
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
            birth=ClinicalStyle.TextField("BirthDate",original.BirthDate.HasValue?ExaminationDates.Jalali(original.BirthDate.Value):"");
            ClinicalStyle.Row(table,"تولد (شمسی یا میلادی؛ اختیاری)",birth,44);birth.Enabled=!readOnly; Height+=44;
            if(!readOnly && hline!=null)
            {
                serverName=new Label {Text=original.ServerFullName,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft};ClinicalStyle.Row(table,"نام کامل سایت",serverName,44);Height+=44;
                Button fetch=ClinicalStyle.Button("دریافت مشخصات از سایت",async delegate {await Fetch();},false);fetch.Name="FetchHLinePatient";ClinicalStyle.Row(table,"HLine",fetch,48);
                confirmAnthropometry=new CheckBox {Name="ConfirmAnthropometry",Text="قد و وزن و جنسیت برای این معاینه بررسی و تأیید شد",AutoSize=true}; ClinicalStyle.Row(table,"تأیید مشخصات",confirmAnthropometry,44);
                Height+=90;
            }
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
            FormClosing+=delegate(object s,FormClosingEventArgs e) {e.Cancel=fetching;};
        }
        async Task Fetch()
        {
            string code=Fmt.NormalizeNumber(nid.Text); fetching=true; Enabled=false;
            try
            {
                if(hline.Status!=SessionStatus.Connected) {using(HLineLoginForm login=new HLineLoginForm(hline)) if(login.ShowDialog(this)!=DialogResult.OK) return;}
                HLinePerson person=await Task.Factory.StartNew(delegate {return hline.Lookup(code);});
                if(person.Records.Count==0) {error.Text="پروندهٔ پزشکی برای این شخص یافت نشد.";return;}
                MedicalRecord selected;
                using(MedicalRecordForm records=new MedicalRecordForm(person.Records,HLineClient.LatestUnambiguous(person.Records))) {if(records.ShowDialog(this)!=DialogResult.OK) return;selected=records.Value;}
                Patient p=await Task.Factory.StartNew(delegate {return hline.PopulateRecord(person,selected);});
                p.SetAgeAt(examinationDate); CopyIdentity(p);
                nid.Text=p.NationalId;first.Text=p.FirstName;last.Text=p.LastName;
                age.Text=p.BirthDate.HasValue?p.Age.ToString():"";height.Text=double.IsNaN(p.HeightCm)?"":Fmt.N(p.HeightCm,1);weight.Text=double.IsNaN(p.WeightKg)?"":Fmt.N(p.WeightKg,1);sex.SelectedIndex=p.SexUnconfirmed?-1:(int)p.Sex;
                birth.Text=p.BirthDate.HasValue?ExaminationDates.Jalali(p.BirthDate.Value):"";serverName.Text=p.ServerFullName;
                confirmAnthropometry.Checked=false;
                error.Text=(p.ServerFullName.Length>0?p.ServerFullName+" | ":"")+"مشخصات و قد و وزن این مراجعه را بررسی کنید.";
            }
            catch(HLineException e) {error.Text=e.Message;}
            catch {error.Text="دریافت مشخصات انجام نشد؛ اطلاعات واردشده حفظ شد.";}
            finally {fetching=false;Enabled=true;}
        }
        void CopyIdentity(Patient p)
        {
            original.HLinePersonId=p.HLinePersonId;original.HLineRecordId=p.HLineRecordId;original.HLineRecordDate=p.HLineRecordDate;original.HLineRecordStatus=p.HLineRecordStatus;
            original.BirthDate=p.BirthDate;original.ServerFullName=p.ServerFullName;original.DemographicSource=p.DemographicSource;original.AnthropometrySource=p.AnthropometrySource;original.NationalId=p.NationalId;
            original.FirstName=p.FirstName;original.LastName=p.LastName;original.Sex=p.Sex;
        }
        public static bool ValidatePatient(Patient p,out string message)
        {
            message="";
            if(p==null || !Regex.IsMatch(p.NationalId??"", "^[0-9]{10}$")) { message="کد ملی ده‌رقمی را وارد کنید."; return false; }
            if((string.IsNullOrWhiteSpace(p.FirstName) || string.IsNullOrWhiteSpace(p.LastName)) && string.IsNullOrWhiteSpace(p.ServerFullName)) { message="نام و نام خانوادگی را وارد کنید."; return false; }
            return p.IsUsable(out message);
        }
        public bool TryRead(out Patient p,out string message)
        {
            p=original.Clone(); message="";
            if(birth.Text.Trim().Length>0) {DateTime d;if(!ExaminationDates.TryParse(birth.Text,out d)) {message="تاریخ تولد معتبر وارد کنید.";return false;}p.BirthDate=d;}
            else {p.BirthDate=null;p.ReferenceAgeYears=null;}
            double a=Fmt.Parse(age.Text,double.NaN);
            if(p.BirthDate.HasValue) {try {p.SetAgeAt(examinationDate);a=p.Age;} catch(ArgumentException e) {message=e.Message;return false;}}
            if(double.IsNaN(a) || a!=Math.Floor(a) || a<5 || a>110) { message="سن صحیح بین ۵ تا ۱۱۰ سال وارد کنید."; return false; }
            p.NationalId=Fmt.NormalizeNumber(nid.Text); p.FirstName=first.Text.Trim(); p.LastName=last.Text.Trim(); p.Age=(int)a;
            p.Sex=(Sex)sex.SelectedIndex; p.Origin=(Origin)origin.SelectedIndex; p.HeightCm=Fmt.Parse(height.Text,double.NaN); p.WeightKg=Fmt.Parse(weight.Text,double.NaN);
            if(sex.SelectedIndex<0) {message="جنسیت را مشخص کنید.";return false;}
            if(p.HLinePersonId.Length>0)
            {
                if(p.NationalId!=original.NationalId) {message="با تغییر کد ملی، مشخصات را دوباره از سایت دریافت کنید.";return false;}
                if(confirmAnthropometry!=null && !confirmAnthropometry.Checked) {message="قد و وزن و جنسیت این مراجعه را تأیید کنید.";return false;}
                p.AnthropometryConfirmed=true;p.SexUnconfirmed=false;p.DemographicSource=p.BirthDate!=original.BirthDate || p.FirstName!=original.FirstName || p.LastName!=original.LastName || p.Sex!=original.Sex?"operator_edited_after_hline_lookup":"hline_person_operator_reviewed";p.AnthropometrySource="operator_confirmed_for_examination";
            }
            return ValidatePatient(p,out message);
        }
    }
}

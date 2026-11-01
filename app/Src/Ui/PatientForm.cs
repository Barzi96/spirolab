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
        Patient original;
        readonly TextBox nid,first,last,age,height,weight,birth;
        readonly ComboBox sex,origin;
        readonly Label error;
        readonly Button fetch;
        string lastLookupError="";
        readonly HLineClient hline;
        readonly DateTime examinationDate;
        readonly System.Windows.Forms.Timer lookupTimer;
        readonly Button save;
        Label serverName;
        bool fetching,populating;
        int lookupVersion;
        string boundCode="";
        public Patient Value { get; private set; }
        public PatientForm(Patient patient=null,bool readOnly=false,HLineClient hline=null,DateTime? examinationDate=null)
        {
            this.hline=hline; this.examinationDate=examinationDate??DateTime.Today;
            original=patient==null?new Patient { Origin=Origin.Caucasian }:patient.Clone();
            if(original.HLinePersonId.Length>0) boundCode=Fmt.NormalizeNumber(original.NationalId);
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
                lookupTimer=new System.Windows.Forms.Timer {Interval=400};
                lookupTimer.Tick+=async delegate {lookupTimer.Stop();await Fetch(false);};
            }
            ClinicalStyle.Row(table,"سن (سال)",age,44); ClinicalStyle.Row(table,"جنسیت",sex,44); ClinicalStyle.Row(table,"گروه مرجع",origin,44);
            ClinicalStyle.Row(table,"قد (سانتی‌متر)",height,44); ClinicalStyle.Row(table,"وزن (کیلوگرم)",weight,44);
            foreach(Control c in new Control[] {nid,first,last,age,height,weight}) c.Enabled=!readOnly;
            sex.Enabled=origin.Enabled=!readOnly;
            error=new Label { Name="ValidationMessage",Dock=DockStyle.Bottom,Height=35,ForeColor=Color.FromArgb(165,55,55),Padding=new Padding(16,0,16,0) }; Controls.Add(error);
            foreach(TextBox field in new TextBox[] {nid,first,last,age,height,weight}) field.TextChanged+=delegate { error.Text=""; };
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Bottom,Height=58,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,Padding=new Padding(12,4,12,4) };
            save=ClinicalStyle.Button(readOnly?"بستن":"ذخیره و ادامه",async delegate { if(readOnly) { DialogResult=DialogResult.Cancel; return; } await SaveAndContinue(); },true); save.Name="SavePatient";
            fetch=ClinicalStyle.Button("دریافت از سایت",async delegate { await Fetch(true); },false); fetch.Name="FetchHLinePatient";
            Button cancel=ClinicalStyle.Button("انصراف",delegate { DialogResult=DialogResult.Cancel; },false); cancel.Name="CancelPatient";
            if(!readOnly && hline!=null) actions.Controls.Add(fetch);
            actions.Controls.Add(save); if(!readOnly) actions.Controls.Add(cancel); Controls.Add(actions); AcceptButton=save; CancelButton=cancel;
            FormClosing+=delegate(object s,FormClosingEventArgs e) {e.Cancel=fetching;};
            if(lookupTimer!=null)
            {
                nid.TextChanged+=delegate
                {
                    if(populating || Fmt.NormalizeNumber(nid.Text)==boundCode) return;
                    lookupVersion++;lookupTimer.Stop();boundCode="";lastLookupError="";
                    original=new Patient {Origin=original.Origin,HeightCm=double.NaN,WeightKg=double.NaN,SexUnconfirmed=true};
                    first.Clear();last.Clear();age.Clear();height.Clear();weight.Clear();birth.Clear();serverName.Text="";sex.SelectedIndex=-1;
                    ScheduleLookup();
                };
                nid.KeyDown+=async delegate(object s,KeyEventArgs e) {if(e.KeyCode==Keys.Enter && boundCode!=Fmt.NormalizeNumber(nid.Text)) {lookupTimer.Stop();e.Handled=e.SuppressKeyPress=true;await Fetch(true);}};
                Shown+=delegate {if(boundCode.Length==0) ScheduleLookup();};
                Disposed+=delegate {lookupTimer.Dispose();};
            }
        }
        void ScheduleLookup()
        {
            // Typing pause only refreshes an existing session; it never opens a dialog.
            if(!Regex.IsMatch(Fmt.NormalizeNumber(nid.Text),"^[0-9]{10}$")) return;
            if(!fetching) lookupTimer.Start();
        }
        // interactive=true (Enter, "دریافت از سایت", Save) may open the login dialog.
        async Task Fetch(bool interactive)
        {
            if(fetching || IsDisposed) return;
            string code=Fmt.NormalizeNumber(nid.Text);int version=lookupVersion;
            if(!Regex.IsMatch(code,"^[0-9]{10}$")) return;
            if(code==boundCode && !interactive) return;
            if(hline.Status!=SessionStatus.Connected)
            {
                if(!interactive) { error.Text=hline.Status==SessionStatus.Expired?"نشست منقضی شده؛ دوباره وارد شوید.":"برای دریافت مشخصات، «ورود به سامانه» را بزنید."; return; }
                error.Text="در حال ورود به سامانه…";
            }
            fetching=true;save.Enabled=false;fetch.Enabled=false;error.Text="در حال دریافت مشخصات…";
            foreach(Control c in new Control[] {first,last,age,height,weight,birth,sex,origin}) c.Enabled=false;
            try
            {
                Patient p=await Lookup(code,interactive);
                if(p==null || version!=lookupVersion || code!=Fmt.NormalizeNumber(nid.Text)) return;
                p.SetAgeAt(examinationDate);original=p.Clone();boundCode=code;populating=true;
                nid.Text=p.NationalId;first.Text=p.FirstName;last.Text=p.LastName;
                age.Text=p.BirthDate.HasValue?p.Age.ToString():"";height.Text=double.IsNaN(p.HeightCm)?"":Fmt.N(p.HeightCm,1);weight.Text=double.IsNaN(p.WeightKg)?"":Fmt.N(p.WeightKg,1);sex.SelectedIndex=p.SexUnconfirmed?-1:(int)p.Sex;
                birth.Text=p.BirthDate.HasValue?ExaminationDates.Jalali(p.BirthDate.Value):"";serverName.Text=p.ServerFullName;
                lastLookupError="";error.Text="مشخصات دریافت شد.";
            }
            catch(ArgumentException e) { lastLookupError=e.Message; if(version==lookupVersion) error.Text=e.Message; }
            catch { lastLookupError="دریافت مشخصات انجام نشد."; if(version==lookupVersion) error.Text=lastLookupError; }
            finally
            {
                fetching=populating=false;
                if(!IsDisposed) { save.Enabled=true;fetch.Enabled=true;foreach(Control c in new Control[] {first,last,age,height,weight,birth,sex,origin}) c.Enabled=true; }
                if(version!=lookupVersion && !IsDisposed) ScheduleLookup();
            }
        }
        // Authenticates on demand, retries once after an expired session, and resumes the same lookup.
        async Task<Patient> Lookup(string code,bool interactive)
        {
            for(int attempt=0;attempt<2;attempt++)
            {
                if(hline.Status!=SessionStatus.Connected)
                {
                    if(!interactive) { lastLookupError="نشست منقضی شده؛ دوباره وارد شوید.";if(!IsDisposed) error.Text=lastLookupError;return null; }
                    if(!HLineLoginForm.SignIn(hline,this))
                    { lastLookupError="ورود به سامانه انجام نشد.";if(!IsDisposed) error.Text=lastLookupError;return null; }
                }
                try { return await Task.Factory.StartNew(delegate {return hline.LoadLatestPatient(code);}); }
                catch(HLineException e)
                {
                    if((e.Code=="expired" || e.Code=="session") && attempt==0) continue;
                    lastLookupError=e.Message;if(!IsDisposed) error.Text=e.Message;return null;
                }
                catch { lastLookupError="دریافت مشخصات انجام نشد.";if(!IsDisposed) error.Text=lastLookupError;return null; }
            }
            return null;
        }
        async Task SaveAndContinue()
        {
            if(fetching) return;
            string code=Fmt.NormalizeNumber(nid.Text);
            if(hline!=null && boundCode!=code)
            {
                await Fetch(true);
                if(boundCode!=code && lastLookupError.Length>0) return; // real cause already displayed
            }
            Patient p;string message;
            if(TryRead(out p,out message)) { Value=p; DialogResult=DialogResult.OK; }
            else error.Text=message;
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
            if(hline!=null && (fetching || boundCode!=Fmt.NormalizeNumber(nid.Text) || original.HLinePersonId.Length==0 || original.HLineRecordId.Length==0)) {message=lastLookupError.Length>0?lastLookupError:"مشخصات از سامانه دریافت نشده است؛ «دریافت از سایت» را بزنید.";return false;}
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
                p.SexUnconfirmed=false;p.DemographicSource=p.BirthDate!=original.BirthDate || p.FirstName!=original.FirstName || p.LastName!=original.LastName || p.Sex!=original.Sex?"operator_edited_after_hline_lookup":"hline_person";
                if(p.HeightCm!=original.HeightCm || p.WeightKg!=original.WeightKg) {p.AnthropometrySource="operator_edited_after_hline_lookup";p.AnthropometryConfirmed=false;}
            }
            return ValidatePatient(p,out message);
        }
    }
}

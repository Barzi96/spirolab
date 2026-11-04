using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;
using SpiroLab.Ui;
using Timer=System.Windows.Forms.Timer;
class HLineIntegrationTests
{
    static int checks;static string root;
    static void Check(bool ok,string name) {if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Refuses(Action a,string name) {bool refused=false;try {a();} catch(Exception) {refused=true;} Check(refused,name);}
    static object Field(object o,string name) {return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static object Call(object o,string name,params object[] args) {return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args);}
    static void PumpUntil(Func<bool> done)
    {var clock=System.Diagnostics.Stopwatch.StartNew();while(!done()) {if(clock.ElapsedMilliseconds>5000) throw new Exception("UI async timeout");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();}
    static MedicalRecord TestDestination() {return new MedicalRecord {Id="2",PersonId="TEST-P",NationalId="0000000001",DateText="2026/11/01",Status="complete"};}
    static void PumpFor(int milliseconds) {var clock=System.Diagnostics.Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds) {Application.DoEvents();Thread.Sleep(5);}}
    static TextBox TextField(Form f,string name) {return (TextBox)f.Controls.Find(name,true)[0];}
    static void AutomaticPatientTests(HLineClient client,MockServer mock)
    {
        mock.Mode="vitals";
        using(var form=new PatientForm(null,false,client,new DateTime(2026,5,16)))
        {
            form.Show();Application.DoEvents();int calls=mock.Calls.Count;
            TextField(form,"NationalId").Text="000000000";PumpFor(450);
            Check(mock.Calls.Count==calls,"incomplete national ID never starts automatic lookup");
            TextField(form,"NationalId").Text="۰۰۰۰۰۰۰۰۰۱";
            PumpUntil(delegate {return (string)Field(form,"boundCode")=="0000000001" && !(bool)Field(form,"fetching");});
            Patient p;string error;
            Check(form.TryRead(out p,out error) && p.HLinePersonId=="TEST-P" && p.HLineRecordId=="2" && p.HeightCm==164 && p.WeightKg==74,"typing national ID automatically populates exact person and latest record without a click");
            Check(form.Controls.Find("FetchHLinePatient",true).Length==1 && form.Controls.Find("ConfirmAnthropometry",true).Length==0 && Application.OpenForms.Count==1,"patient form has one manual fetch button and no confirmation checkbox or record dialog");
            TextField(form,"Height").Text="166";calls=mock.Calls.Count;PumpFor(450);
            Check(form.TryRead(out p,out error) && p.HeightCm==166 && mock.Calls.Count==calls,"manual field correction is preserved without refetch or approval");Shot(form,"03-automatic-patient");
            TextField(form,"NationalId").Text="0000000002";
            PumpUntil(delegate {return !(bool)Field(form,"fetching") && form.Controls.Find("ValidationMessage",true)[0].Text.Contains("پیدا نشد");});
            Check(TextField(form,"Height").Text=="" && TextField(form,"BirthDate").Text=="" && !form.TryRead(out p,out error),"changing national ID clears old measurements and destination even if lookup fails");
        }
        using(var started=new ManualResetEvent(false)) using(var form=new PatientForm(null,false,client))
        {
            mock.SearchStarted=started;mock.SearchDelay=250;form.Show();TextField(form,"NationalId").Text="0000000001";
            PumpUntil(delegate {return started.WaitOne(0);});TextField(form,"NationalId").Text="000000000";
            PumpUntil(delegate {return !(bool)Field(form,"fetching");});
            Check((string)Field(form,"boundCode")=="" && TextField(form,"Height").Text=="" && TextField(form,"LastName").Text=="","late lookup response cannot populate a changed national ID");
            mock.SearchDelay=0;mock.SearchStarted=null;
        }
        mock.RecordsOverride="{\"data\":["+MockServer.Record("999","2026/11/01")+","+MockServer.Record("2","2026/11/01")+"]}";
        bool ambiguous=false;try {client.LoadLatestPatient("0000000001");} catch(HLineException e) {ambiguous=e.Code=="latest_record";}
        Check(ambiguous,"ambiguous latest medical record gives a clear error without guessing");mock.RecordsOverride=null;
        mock.RecordsOverride="{\"data\":["+MockServer.Record("999","2020/01/01")+","+MockServer.Record("2","unknown")+"]}";
        Refuses(delegate {client.LoadLatestPatient("0000000001");},"unknown record date cannot silently select a historical record");mock.RecordsOverride=null;
        Check(HLineClient.LatestUnambiguous(new MedicalRecord[] {new MedicalRecord {Id="2",Date=new DateTime(2026,11,1),Status=""}}).Id=="2","unique actual date does not require a record-status confirmation");
        mock.VitalsOverride="{\"data\":[{\"medical_record_id\":\"2\",\"test_date\":\"2026-10-01\",\"height\":180,\"weight\":90},{\"medical_record_id\":\"2\",\"test_date\":\"2026-11-01\",\"height\":164,\"weight\":null}]}";
        var patient=client.LoadLatestPatient("0000000001");Check(patient.HeightCm==164 && double.IsNaN(patient.WeightKg),"latest incomplete measurement row never borrows weight from an older row");
        mock.VitalsOverride="{\"data\":[{\"medical_record_id\":\"2\",\"created_at\":\"2026-11-01T08:00:00Z\",\"height\":165,\"weight\":75},{\"medical_record_id\":\"2\",\"created_at\":\"2026-10-01T08:00:00Z\",\"height\":180,\"weight\":90}]}";
        patient=client.LoadLatestPatient("0000000001");Check(patient.HeightCm==165 && patient.WeightKg==75,"same-record rows are resolved by actual timestamps regardless of input order");
        mock.VitalsOverride="{\"data\":[{\"medical_record_id\":\"2\",\"test_date\":\"2026-11-01\",\"height\":164,\"weight\":74},{\"medical_record_id\":\"2\",\"test_date\":\"2026-11-01\",\"height\":180,\"weight\":90}]}";
        patient=client.LoadLatestPatient("0000000001");Check(double.IsNaN(patient.HeightCm) && double.IsNaN(patient.WeightKg),"ambiguous anthropometry remains empty and editable");
        mock.VitalsOverride="{\"data\":[{\"medical_record_id\":\"2\",\"height\":999,\"weight\":-1}]}";
        patient=client.LoadLatestPatient("0000000001");Check(double.IsNaN(patient.HeightCm) && double.IsNaN(patient.WeightKg),"unusable anthropometry is not presented as valid data");mock.VitalsOverride=null;
        mock.VitalsOverride="{\"data\":null}";patient=client.LoadLatestPatient("0000000001");Check(patient.HLineRecordId=="2" && double.IsNaN(patient.HeightCm) && double.IsNaN(patient.WeightKg),"empty vitals response still loads identity and leaves measurements blank");mock.VitalsOverride=null;
    }
    static Timer AutoLogin()
    {
        Timer timer=new Timer {Interval=20};
        timer.Tick+=delegate
        {
            HLineLoginForm dialog=Application.OpenForms.Cast<Form>().OfType<HLineLoginForm>().FirstOrDefault();
            if(dialog==null) return;
            TextField(dialog,"HLineUsername").Text="synthetic";
            TextField(dialog,"HLinePassword").Text="synthetic-password";
            TextField(dialog,"HLineMfa").Text="123456";
            ((Button)dialog.Controls.Find("HLineLogin",true)[0]).PerformClick();
            timer.Stop();
        };
        timer.Start();return timer;
    }
    static void LoginUiTests()
    {
        // 1. Visible toolbar button, modal Persian dialog, username/password/MFA, connected state.
        var mock=new MockServer {Mode="vitals"};
        using(var client=new HLineClient(mock)) using(var form=new MainForm(client,null,null))
        {
            form.Show();Application.DoEvents();
            Button login=(Button)Field(form,"btnLogin");bool opened=false,modal=false,firefoxActions=false;
            using(var timer=new Timer {Interval=20})
            {
                timer.Tick+=delegate
                {
                    HLineLoginForm dialog=Application.OpenForms.Cast<Form>().OfType<HLineLoginForm>().FirstOrDefault();
                    if(dialog==null) return;
                    opened=true;modal=dialog.Modal;
                    firefoxActions=dialog.Controls.Find("HLineFirefoxConnect",true).Length==1 && dialog.Controls.Find("HLineFirefoxOpen",true).Length==1 && dialog.Controls.Find("HLineSessionHint",true).Length==1;
                    TextField(dialog,"HLineUsername").Text="synthetic";TextField(dialog,"HLinePassword").Text="synthetic-password";TextField(dialog,"HLineMfa").Text="123456";
                    ((Button)dialog.Controls.Find("HLineLogin",true)[0]).PerformClick();timer.Stop();
                };
                timer.Start();login.PerformClick();
            }
            Check(login.Text=="ورود به سامانه" && login.Visible,"toolbar login button is visible with the Persian caption");
            Check(opened && modal && client.Status==SessionStatus.Connected,"login button opens the modal login dialog and authenticates username, password and MFA");
            Check(firefoxActions,"login dialog offers Firefox session adoption and browser launch without credentials");
            Check(((Label)Field(form,"lblStatus")).Text=="متصل به سامانه","successful login reports متصل به سامانه");
            Check(((Label)Field(form,"lblSiteConnection")).Text.Contains("متصل به سامانه"),"site indicator reflects the authenticated session");
            Shot(form,"07-login-connected");
        }
        // 2. Unauthenticated national ID + Enter opens login, then resumes and populates.
        mock=new MockServer {Mode="vitals"};
        using(var client=new HLineClient(mock)) using(var form=new PatientForm(null,false,client,new DateTime(2026,5,16)))
        {
            form.Show();Application.DoEvents();
            TextField(form,"NationalId").Text="0000000001";
            Check(client.Status!=SessionStatus.Connected,"patient lookup starts unauthenticated");
            using(var timer=AutoLogin())
            {
                typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(TextField(form,"NationalId"),new object[] {new KeyEventArgs(Keys.Enter)});
                PumpUntil(delegate {return (string)Field(form,"boundCode")=="0000000001" && !(bool)Field(form,"fetching");});
            }
            Patient p;string issue;
            Check(client.Status==SessionStatus.Connected && form.TryRead(out p,out issue) && p.HLineRecordId=="2" && p.HeightCm==164,"national ID lookup opens login, resumes without retyping and populates the form");
            TextField(form,"Height").Text="166";
            ((Button)form.Controls.Find("SavePatient",true)[0]).PerformClick();
            Check(form.DialogResult==DialogResult.OK && form.Value!=null && form.Value.HeightCm==166,"operator edits populated fields and Save and Continue accepts them");
        }
        // 3. Save on a logged-out form authenticates and retrieves before validating.
        mock=new MockServer {Mode="vitals"};
        using(var client=new HLineClient(mock)) using(var form=new PatientForm(null,false,client,new DateTime(2026,5,16)))
        {
            form.Show();Application.DoEvents();
            TextField(form,"NationalId").Text="0000000001";
            using(var timer=AutoLogin())
            {
                ((Button)form.Controls.Find("SavePatient",true)[0]).PerformClick();
                PumpUntil(delegate {return form.DialogResult==DialogResult.OK;});
            }
            Check(form.Value!=null && form.Value.NationalId=="0000000001" && form.Value.HeightCm==164 && form.Value.HLineRecordId=="2","Save and Continue authenticates and retrieves before validating");
        }
        // 4. Expired session prompts login again and resumes the same lookup.
        mock=new MockServer {Mode="vitals"};
        using(var client=new HLineClient(mock))
        {
            client.Login("synthetic","synthetic","123456");mock.Expired=true;
            using(var form=new PatientForm(null,false,client,new DateTime(2026,5,16)))
            {
                form.Show();Application.DoEvents();
                TextField(form,"NationalId").Text="0000000001";
                using(var timer=AutoLogin())
                {
                    typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(TextField(form,"NationalId"),new object[] {new KeyEventArgs(Keys.Enter)});
                    PumpUntil(delegate {return (string)Field(form,"boundCode")=="0000000001" && !(bool)Field(form,"fetching");});
                }
                Check(client.Status==SessionStatus.Connected && mock.Posts==2,"expired session prompts login again and resumes the same lookup");
            }
        }
        // 5. Genuine failure reports the real cause, never the misleading pre-auth sentence.
        mock=new MockServer {Mode="missing"};
        using(var client=new HLineClient(mock))
        {
            client.Login("synthetic","synthetic","123456");
            using(var form=new PatientForm(null,false,client,new DateTime(2026,5,16)))
            {
                form.Show();Application.DoEvents();
                TextField(form,"NationalId").Text="0000000001";
                string message="";
                PumpUntil(delegate {message=((Label)form.Controls.Find("ValidationMessage",true)[0]).Text;return message.Contains("پیدا نشد");});
                Patient p;string issue;bool refused=!form.TryRead(out p,out issue);
                Check(refused && issue.Contains("پیدا نشد") && !issue.Contains("ابتدا مشخصات") && !issue.Contains("ابتدا باید"),"failed lookup reports the actual cause instead of the misleading confirmation message");
            }
        }
        // 6. Wrong credentials keep the dialog open with the real error and never echo secrets.
        var rejected=new MockServer {LoginError="invalid_credentials"};
        using(var badClient=new HLineClient(rejected)) using(var dialog=new HLineLoginForm(badClient))
        {
            dialog.Show();Application.DoEvents();
            TextField(dialog,"HLineUsername").Text="synthetic";TextField(dialog,"HLinePassword").Text="wrong-secret";TextField(dialog,"HLineMfa").Text="123456";
            ((Button)dialog.Controls.Find("HLineLogin",true)[0]).PerformClick();
            PumpUntil(delegate {return badClient.Status==SessionStatus.AuthenticationError;});
            Label status=(Label)dialog.Controls.Find("HLineStatus",true)[0];
            Check(dialog.Visible && status.Text=="خطای احراز هویت" && !status.Text.Contains("wrong-secret") && TextField(dialog,"HLinePassword").Text.Length==0,"login failure keeps the dialog open with the real error and no secret");
        }
        // 7. Server asks for MFA: prompt for the code, then finish without retyping the password.
        var mfaMock=new MockServer {RequireMfa=true};
        using(var mfaClient=new HLineClient(mfaMock))
        {
            HLineLoginForm dialog=new HLineLoginForm(mfaClient);
            try
            {
                dialog.Show();Application.DoEvents();
                TextField(dialog,"HLineUsername").Text="synthetic";TextField(dialog,"HLinePassword").Text="synthetic-password";
                ((Button)dialog.Controls.Find("HLineLogin",true)[0]).PerformClick();
                PumpUntil(delegate {return mfaClient.Status==SessionStatus.AuthenticationError && !(bool)Field(dialog,"working");});
                Label mfaStatus=(Label)dialog.Controls.Find("HLineStatus",true)[0];
                Check(dialog.Visible && mfaStatus.Text.Contains("دومرحله") && TextField(dialog,"HLinePassword").Text.Length==0,"server MFA request prompts for the code and keeps the dialog open");
                TextField(dialog,"HLineMfa").Text="123456";
                ((Button)dialog.Controls.Find("HLineLogin",true)[0]).PerformClick();
                PumpUntil(delegate {return mfaClient.Status==SessionStatus.Connected;});
                Check(mfaMock.Posts==2 && mfaClient.Status==SessionStatus.Connected,"MFA retry completes authentication without retyping the password");
            }
            finally { try { if(!dialog.IsDisposed) dialog.Dispose(); } catch { } }
        }
    }
    static Dictionary<string,object> Json(ExaminationSnapshot s) {return (Dictionary<string,object>)new JavaScriptSerializer {MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters}.DeserializeObject(s.ToJson());}
    static Dictionary<string,object> Obj(object o) {return (Dictionary<string,object>)o;}
    static double? N(object o) {return o==null?null:(double?)Convert.ToDouble(o);}
    static Trial VextTrial(string id,int sequence,double? ml,bool simulated=false,bool imported=false)
    {
        Trial t=new Trial {ManeuverId=id,AcquisitionSequence=sequence,Simulated=simulated,Imported=imported};
        t.Result=new AnalysisResult {BevAvailable=ml.HasValue,Bev=ml.HasValue?ml.Value/1000.0:0};
        return t;
    }
    static Exam SelectionExam(int workflow, params Trial[] trials)
    {
        Exam e=SpiroLab.Exam.CreateNew(new AppSettings {OperatorName="بازبین آزمایشی"});
        e.SelectionWorkflowVersion=workflow;
        foreach(Trial t in trials) e.Trials.Add(t);
        e.EnsureManeuverIdentities();
        e.ReportManeuverIds.Clear();
        e.ReportSelectionManual=true;
        return e;
    }
    static VextSelection SelectedVext(Exam e, params string[] ids)
    {
        e.ReportManeuverIds.Clear();
        foreach(string id in ids) e.ReportManeuverIds.Add(id);
        return ClinicVext.SelectForExam(e);
    }
    static void SelectedVextTests()
    {
        Trial m1=VextTrial("m1",1,110),m2=VextTrial("m2",2,132),m3=VextTrial("m3",3,105),m4=VextTrial("m4",4,185);
        Exam e=SelectionExam(4,m1,m2,m3,m4);
        VextSelection chosen=SelectedVext(e,"m1","m2","m3");
        Check(chosen.Available && chosen.ValueMl==132 && chosen.ManeuverId=="m2" && chosen.AcquisitionSequence==2,"selected 110/132/105 gives VEXT 132 with the correct source");
        m4.Result.Bev=.185; chosen=SelectedVext(e,"m1","m2","m3");
        Check(chosen.ValueMl==132 && chosen.ManeuverId=="m2","unselected fourth EVol 185 does not change VEXT 132");
        m4.Result.Bev=.199; chosen=SelectedVext(e,"m1","m2","m3");
        Check(chosen.ValueMl==132 && chosen.ManeuverId=="m2","unselected fourth EVol 199 does not change VEXT 132");
        m4.OperatorRejected=true;m4.Result.Bev=.300;chosen=SelectedVext(e,"m1","m2","m4");
        Check(chosen.ValueMl==132 && chosen.ManeuverId=="m2","a selected rejected maneuver with higher EVol is ignored");
        m4.OperatorRejected=false;m4.Imported=true;chosen=SelectedVext(e,"m1","m2","m4");
        Check(chosen.ValueMl==132 && chosen.ManeuverId=="m2","a selected replayed maneuver with higher EVol is ignored");

        Trial d1=VextTrial("d1",1,110),d2=VextTrial("d2",2,132),d3=VextTrial("d3",3,105),d4=VextTrial("d4",4,300);
        Exam discarded=SelectionExam(4,d1,d2,d3,d4);
        SelectedVext(discarded,"d1","d2","d4");
        discarded.DiscardManeuver("d4");
        Check(ClinicVext.SelectForExam(discarded).ValueMl==132 && !discarded.ReportManeuverIds.Contains("d4"),"a discarded maneuver can neither remain selected nor supply VEXT");

        Trial c1=VextTrial("c1",1,110),c2=VextTrial("c2",2,132),c3=VextTrial("c3",3,105),c4=VextTrial("c4",4,150);
        Exam changed=SelectionExam(4,c1,c2,c3,c4);
        Check(SelectedVext(changed,"c1","c2","c3").ValueMl==132,"initial selected set gives VEXT 132");
        VextSelection replacement=SelectedVext(changed,"c1","c3","c4");
        Check(replacement.ValueMl==150 && replacement.ManeuverId=="c4" && !changed.ReportManeuverIds.Contains("c2"),"changing the selected three immediately changes VEXT and its source");
        Check(SelectedVext(changed,"c1","c2","c3").ValueMl==132,"restoring the previous selection restores VEXT 132 without a cached value");

        Trial t1=VextTrial("t1",1,110),t2=VextTrial("t2",2,132),t3=VextTrial("t3",3,199);
        Exam two=SelectionExam(4,t1,t2,t3);
        Check(SelectedVext(two,"t1","t2").ValueMl==132,"two selected maneuvers are enough; VEXT uses the higher selected EVol");
        Check(SelectedVext(two,"t2").ValueMl==132 && SelectedVext(two,"t1").ValueMl==110,"one selected maneuver supplies VEXT without requiring three");

        Exam none=SelectionExam(4,VextTrial("n1",1,200),VextTrial("n2",2,0),VextTrial("n3",3,-5));
        VextSelection missing=SelectedVext(none,"n1","n2","n3");
        Check(!missing.Available && missing.ValueMl==0 && missing.ManeuverId.Length==0 && missing.Problem==VextSelection.NoEligibleSelectedProblem,"no eligible EVol in the selected set returns unavailable without a fake zero");

        Exam legacy=SelectionExam(3,VextTrial("l1",1,110),VextTrial("l2",2,132),VextTrial("l3",3,105),VextTrial("l4",4,185));
        SelectedVext(legacy,"l1","l2","l3");
        VextSelection historical=ClinicVext.SelectForExam(legacy);
        Check(historical.Available && historical.ValueMl==185 && historical.ManeuverId=="l4","workflow-3 VEXT keeps its historical all-active-maneuver scope");
    }
    static void VextRuleTests()
    {
        // Clinic regression examples: highest EVol strictly below 200 mL.
        Check(ClinicVext.SelectValue(new double?[] {110,132,105})==132,"clinic VEXT picks 132 from 110/132/105");
        Check(ClinicVext.SelectValue(new double?[] {230,185,160})==185,"clinic VEXT ignores values above 200 mL");
        Check(ClinicVext.SelectValue(new double?[] {240,220,199})==199,"clinic VEXT accepts 199 mL");
        Check(ClinicVext.SelectValue(new double?[] {200,180,150})==180,"exactly 200 mL is excluded");
        Check(ClinicVext.SelectValue(new double?[] {240,230,210})==null,"no eligible EVol returns nothing instead of zero");
        Check(ClinicVext.SelectValue(new double?[] {null,125,90})==125,"missing EVol is skipped");
        Check(ClinicVext.SelectValue(new double?[] {0,-5,double.NaN,double.PositiveInfinity,200})==null,"zero, negative, nonfinite and 200 mL are all rejected");
        VextSelection chosen=ClinicVext.Select(new Trial[] {VextTrial("m1",1,110),VextTrial("m2",2,132),VextTrial("m3",3,null),VextTrial("m4",4,230)});
        Check(chosen.Available && chosen.ValueMl==132 && chosen.ManeuverId=="m2" && chosen.AcquisitionSequence==2 && chosen.Problem.Length==0,"selection keeps value and source maneuver identity");
        Check(!ClinicVext.Select(new Trial[] {VextTrial("sim",1,150,true,false)}).Available && !ClinicVext.Select(new Trial[] {VextTrial("rep",1,150,false,true)}).Available,"simulated and replayed maneuvers never provide the clinic VEXT");
        Check(ClinicVext.Select(new Trial[0]).Problem==VextSelection.NoEligibleProblem,"empty examination reports the actual problem");
    }
    static void VextUiTests()
    {
        using(var client=new HLineClient(new MockServer {Mode="vitals"})) using(var form=new MainForm(client,new Gateway(),TestDestination()))
        {
            form.Show();Application.DoEvents();
            Exam exam=Exam(false);foreach(Trial t in exam.Trials) {t.Result.BevAvailable=true;t.Result.Bev=.25;}
            typeof(MainForm).GetField("_exam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,exam);
            typeof(MainForm).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,Path.Combine(root,"vext-problem.spiro"));
            typeof(MainForm).GetField("_hasPatient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
            foreach(string method in new string[] {"PopulateMetadata","RenderTrials","RestorePrimaryPreview"}) typeof(MainForm).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
            ((Button)Field(form,"btnReport")).PerformClick();PumpUntil(delegate {return !(bool)Field(form,"_busy");});
            string message=((Label)Field(form,"lblStatus")).Text;
            Check(message.Contains("هیچ مانور واقعی") && Application.OpenForms.Count==1,"missing clinic VEXT surfaces the real problem in the main window without a dialog");
        }
    }
    static void OneClickMainTests()
    {
        foreach(int outcome in new int[] {0,1,2,3})
        {
            bool partial=outcome==1,changed=outcome>=2;
            var mock=new MockServer {Mode="vitals"};var client=new HLineClient(mock);client.Login("synthetic","synthetic","123456");var gateway=new Gateway {FailAssociation=partial};
            if(changed) mock.RecordsOverride="{\"data\":["+MockServer.Record("2","2026/11/01")+","+MockServer.Record("NEW",outcome==2?"2026/11/02":"2026/11/01")+"]}";
            using(var form=new MainForm(client,gateway,TestDestination())) using(var timer=new System.Windows.Forms.Timer {Interval=25})
            {
                form.Size=new Size(1280,1000);form.Show();Exam exam=Exam(false);exam.Patient.AnthropometryConfirmed=false;
                typeof(MainForm).GetField("_exam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,exam);
                typeof(MainForm).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,Path.Combine(root,"one-click-"+outcome+".spiro"));
                typeof(MainForm).GetField("_hasPatient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
                foreach(string method in new string[] {"PopulateMetadata","RenderTrials","RestorePrimaryPreview"}) typeof(MainForm).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
                bool extra=false;timer.Tick+=delegate {foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(dialog!=form) {extra=true;dialog.Close();}};timer.Start();
                ((Button)Field(form,"btnReport")).PerformClick();PumpUntil(delegate {return !(bool)Field(form,"_busy");});timer.Stop();
                Check(!extra && gateway.Uploads==(changed?0:1) && gateway.Creates==(changed?0:1) && exam.ExportReferences.Count==1,"main button handles pipeline without approval dialogs "+outcome);
                Check(exam.Submission.State==(changed?SubmissionState.Draft:partial?SubmissionState.FailedOrUncertain:SubmissionState.Confirmed),"changed latest record or partial result cannot become success "+outcome);
                Check(outcome==0?((Label)Field(form,"lblStatus")).Text.Contains("ثبت و بررسی شد"):!((Label)Field(form,"lblStatus")).Text.Contains("ثبت و بررسی شد"),"success text appears only after full readback "+outcome);
                if(outcome==0) Shot(form,"04-one-click-submission");
            }
        }
    }
    static void Shot(Form f,string file) {f.Show();Application.DoEvents();using(Bitmap b=new Bitmap(f.Width,f.Height)) {f.DrawToBitmap(b,new Rectangle(Point.Empty,f.Size));b.Save(Path.Combine(root,file+".png"));}f.Hide();}
    sealed class MockServer : IHLineTransport
    {
        public string LoginError="",Mode="";public bool Network;public List<string> Calls=new List<string>();public int Posts;
        public bool Expired;
        public bool RequireMfa;
        public string RecordsOverride,VitalsOverride;
        public int SearchDelay;
        public ManualResetEvent SearchStarted;
        public HLineResponse Send(string method,string path,string body,string csrf)
        {
            Calls.Add(method+" "+path.Split('?')[0]);
            if(Network) throw new IOException("DO_NOT_LEAK_SECRET");
            if(path=="/auth/csrf") return R(200,"{\"csrf\":\"synthetic-csrf\"}");
            if(Expired && method=="GET") return R(401,"{}");
            if(method=="POST")
            {
                Posts++;if(path!="/auth/login" || csrf!="synthetic-csrf") throw new Exception("Forbidden mock write");
                var p=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(body);
                Check(p.ContainsKey("username") && p.ContainsKey("password") && Convert.ToInt32(p["mfa_token_method"])==1,"verified authentication request keys and CSRF");
                if(RequireMfa && Convert.ToString(p["mfa_token"]).Length==0) return R(422,"{\"errors\":[\"missing_mfa_token\"]}");
                if(LoginError.Length==0) {Expired=false;return R(200,"{\"data\":{\"data\":{\"user\":{\"id\":\"synthetic-user\"}}}}");}
                return R(422,"{\"errors\":[\""+LoginError+"\"]}");
            }
            if(path=="/me") return R(200,Mode=="rawme"?"{\"data\":{\"id\":\"synthetic-user\"}}":"{\"data\":{\"user\":{\"id\":\"synthetic-user\"}}}");
            if(path.StartsWith("/people?"))
            {
                if(SearchStarted!=null) SearchStarted.Set();if(SearchDelay>0) Thread.Sleep(SearchDelay);
                string row="{\"id\":\"TEST-P\",\"person_id_no\":\"0000000001\"}";
                return R(200,Mode=="malformed"?"{\"data\":false}":Mode=="missing"?"{\"data\":[]}":Mode=="nonmatch"?"{\"data\":[{\"id\":\"OTHER\",\"person_id_no\":\"0000000002\"}]}":"{\"data\":["+row+(Mode=="duplicate"?","+row:"")+"]}");
            }
            if(path=="/people/TEST-P") return R(200,"{\"data\":{\"id\":\"TEST-P\",\"person_id_no\":\""+(Mode=="identity"?"0000000002":"0000000001")+"\",\"full_name\":\"نام چندبخشی ساختگی\",\"birth_date\":\"1990/05/17\",\"gender\":\"female\"}}");
            if(path.StartsWith("/medical-records?")) return R(200,RecordsOverride??"{\"data\":["+Record("999","2020/01/01")+","+Record("2","2026/11/01")+"]}");
            if(path=="/medical-records/2") return R(200,"{\"data\":"+Record("2","2026/11/01")+"}");
            if(path.EndsWith("/vitals/history")) return R(200,VitalsOverride??"{\"data\":[{\"medical_record_id\":\"999\",\"height\":190,\"weight\":100}"+(Mode=="vitals"?",{\"medical_record_id\":\"2\",\"height\":164,\"weight\":74}":"")+"]}");
            if(path.EndsWith("/spirometry/history")) return R(200,Mode=="existing"?"{\"data\":[{\"medical_record_id\":\"2\",\"file_id\":\"TEST-F\"}]}":"{\"data\":[]}");
            throw new Exception("Unrelated mock endpoint");
        }
        public static string Record(string id,string date) {return "{\"id\":\""+id+"\",\"person_id\":\"TEST-P\",\"person_id_no\":\"0000000001\",\"admission_date\":\""+date+"\",\"status\":\"complete\"}";}
        public static HLineResponse R(int status,string body) {return new HLineResponse {Status=status,Body=body};}
        public void Dispose() { }
    }
    sealed class Gateway : ISubmissionGateway
    {
        public bool Verified=true,TimeoutAfterCreate,FailAssociation,FailUpload,ChangedDestination,UnverifiedJpg,BadModule;
        public int Uploads,Creates; public RemoteSpirometry Saved;
        public ManualResetEvent UploadStarted,ContinueUpload;
        public bool ContractVerified {get{return Verified;}}
        public void VerifyDestination(SubmissionDraft d) {if(ChangedDestination) throw new Exception("destination_changed");}
        public void VerifySaved(SubmissionDraft d) {if(BadModule) throw new HLineException("module_status","module is still empty");}
        public RemoteSpirometry Read(SubmissionDraft d) {return Saved;}
        public string UploadJpeg(SubmissionDraft d,string reference) {Uploads++;if(UploadStarted!=null) {UploadStarted.Set();if(!ContinueUpload.WaitOne(5000)) throw new Exception("mock timeout");}if(FailUpload) throw new IOException("timeout");return "TEST-FILE";}
        public void Create(SubmissionDraft d,string file,string reference)
        {
            Creates++;Saved=new RemoteSpirometry {PersonId=d.PersonId,NationalId=d.NationalId,RecordId=d.RecordId,RecordDate=d.RecordDate,RecordStatus=d.RecordStatus,ExaminationDate=d.ExaminationDate,Fields=d.Fields,FileId=FailAssociation?"OTHER-FILE":file,Jpeg=UnverifiedJpg?new byte[0]:d.JpegBytes};
            if(TimeoutAfterCreate) throw new IOException("uncertain");
        }
    }
    static Exam Exam(bool simulated)
    {
        Exam e=SpiroLab.Exam.CreateNew(new AppSettings {OperatorName="بازبین آزمایشی"});
        e.SelectionWorkflowVersion=4; // Preserve the existing selected-scope contract regression fixtures.
        e.Patient=new Patient {FirstName="مراجع",LastName="ساختگی",NationalId="0000000001",Origin=Origin.Caucasian,HeightCm=164,WeightKg=74,Age=44,HLinePersonId="TEST-P",HLineRecordId="2",HLineRecordDate="2026/11/01",HLineRecordStatus="complete",AnthropometryConfirmed=true};
        e.ExaminationDate=new DateTime(2026,10,19);
        for(int i=0;i<5;i++) {Trial t=Simulator.Build(e.Patient,100+i);t.Result=SpiroAnalysis.Analyze(t,e.Patient);if(!simulated) SyntheticDeviceEvidence.Attach(t,t.Result.Fvc,t.Result.Fev1,t.Result.Pef,t.Result.Fet,SyntheticDeviceEvidence.FixtureSerial,i+1);else t.Simulated=true;e.AddManeuver(t);}
        double?[] evol={110,132,105,230,null};
        for(int i=0;i<5;i++) {e.Trials[i].Result.BevAvailable=evol[i].HasValue;e.Trials[i].Result.Bev=evol[i].HasValue?evol[i].Value/1000.0:0;}
        Trial site=ExamSummary.Build(e.Trials).BestTrial; site.Result.Fet=6;site.Result.Cough=false;
        site.ReviewedVextMl=111;site.ReviewedVextSource="synthetic mock fixture";site.VextReviewer="بازبین آزمایشی";site.VextManuallyReviewed=true;
        e.SelectSiteManeuver(site.ManeuverId);
        // Keep the fixture deterministic for selected-scope VEXT: the first three
        // trials carry EVol 110/132/105 and the fourth carries 230.
        e.ReportManeuverIds.Clear();
        for(int i=0;i<3;i++) e.ReportManeuverIds.Add(e.Trials[i].ManeuverId);
        e.ReportSelectionManual=true;
        return e;
    }
    static SubmissionDraft Draft(Exam e) {var s=ExaminationSnapshot.Capture(e);return new SubmissionDraft(s,ResultExport.SaveToDirectory(root,s));}
    static void HttpCookieTest()
    {
        TcpListener portFinder=new TcpListener(IPAddress.Loopback,0);portFinder.Start();int port=((IPEndPoint)portFinder.LocalEndpoint).Port;portFinder.Stop();
        string url="http://127.0.0.1:"+port+"/";Exception serverError=null;bool cookieSeen=false,csrfSeen=false;
        using(HttpListener server=new HttpListener())
        {
            server.Prefixes.Add(url);server.Start();
            Thread worker=new Thread(delegate()
            {
                try
                {
                    for(int i=0;i<5;i++)
                    {
                        var c=server.GetContext();string body;
                        if(i==0 || i==2) {c.Response.Headers.Add("Set-Cookie","synthetic_session=memory_only; Path=/; HttpOnly");body="{\"csrf\":\"local-csrf\"}";}
                        else if(i==1) {csrfSeen=c.Request.Headers["X-CSRF-Token"]=="local-csrf";body="{\"data\":{\"user\":{\"id\":\"TEST-U\"}}}";}
                        else if(i==3) {cookieSeen=c.Request.Headers["Cookie"]!=null && c.Request.Headers["Cookie"].Contains("synthetic_session=memory_only");body="{\"data\":{\"user\":{\"id\":\"TEST-U\"}}}";}
                        else if(i==4) {c.Response.StatusCode=401;body="{}";}
                        else body="{\"data\":{\"user\":{\"id\":\"TEST-U\"}}}";
                        byte[] bytes=Encoding.UTF8.GetBytes(body);c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;c.Response.OutputStream.Write(bytes,0,bytes.Length);c.Response.Close();
                    }
                }
                catch(Exception ex) {serverError=ex;}
            }) {IsBackground=true}; worker.Start();
            var transport=(IHLineTransport)Activator.CreateInstance(typeof(HLineHttpTransport),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] {new Uri(url)},null);
            using(HLineClient client=new HLineClient(transport))
            {
                client.Login("synthetic-user","synthetic-password","123456");Check(cookieSeen && csrfSeen && client.Status==SessionStatus.Connected,"real loopback HTTP mock verifies cookie jar and CSRF header across login");
                Refuses(client.VerifySession,"loopback HTTP expired session refused");Check(client.Status==SessionStatus.Expired,"expired-session state explicit");
            }
            Check(worker.Join(3000) && serverError==null,"loopback mock completed without external calls");
        }
    }
    // ---- Firefox session sharing (synthetic profiles, no real browser) ----
    const string CookieSchema="CREATE TABLE moz_cookies (name TEXT,value TEXT,host TEXT,path TEXT,expiry INTEGER,isSecure INTEGER,isHttpOnly INTEGER,lastAccessed INTEGER)";
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.StdCall)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.StdCall)] static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.StdCall)] static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.StdCall)] static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.StdCall)] static extern int sqlite3_close(IntPtr db);
    static void Exec(IntPtr db,string sql)
    {
        IntPtr statement;
        if(sqlite3_prepare_v2(db,sql,-1,out statement,IntPtr.Zero)!=0) throw new Exception("sqlite prepare failed");
        try {if(sqlite3_step(statement)!=101) throw new Exception("sqlite step failed");}
        finally {sqlite3_finalize(statement);}
    }
    static string Esc(string value) {return (value??"").Replace("'","''");}
    static void MakeCookieDb(string path,string[][] rows)
    {
        IntPtr db;if(sqlite3_open_v2(path,out db,6,IntPtr.Zero)!=0) throw new Exception("sqlite open failed");
        try
        {
            Exec(db,CookieSchema);
            foreach(string[] row in rows)
                Exec(db,"INSERT INTO moz_cookies VALUES ('"+Esc(row[0])+"','"+Esc(row[1])+"','"+Esc(row[2])+"','"+Esc(row[3])+"',"+row[4]+","+row[5]+","+row[6]+","+row[7]+")");
        }
        finally {sqlite3_close(db);}
    }
    static void FirefoxStoreTest()
    {
        string baseDir=Path.Combine(root,"ff-store");if(Directory.Exists(baseDir)) Directory.Delete(baseDir,true);
        string older=Path.Combine(baseDir,"Profiles","older.default"),newer=Path.Combine(baseDir,"Profiles","newer.default-release");
        Directory.CreateDirectory(older);Directory.CreateDirectory(newer);
        File.WriteAllText(Path.Combine(baseDir,"profiles.ini"),
            "[General]\r\nStartWithLastProfile=1\r\nVersion=2\r\n\r\n[Profile0]\r\nName=older\r\nIsRelative=1\r\nPath=Profiles/older.default\r\n\r\n[Profile1]\r\nName=newer\r\nIsRelative=1\r\nPath=Profiles/newer.default-release\r\nDefault=1\r\n");
        MakeCookieDb(Path.Combine(older,"cookies.sqlite"),new string[][]
        {
            new string[] {"sid","older-value","hline.ssu.ac.ir","/","0","1","1","1000"}
        });
        MakeCookieDb(Path.Combine(newer,"cookies.sqlite"),new string[][]
        {
            new string[] {"sid","newer-value","hline.ssu.ac.ir","/","0","1","1","9000"},
            new string[] {"crumb","crumb-value",".ssu.ac.ir","/","0","1","1","8000"},
            new string[] {"other","other-value","app.samanesalamat.com","/","0","1","1","9999"},
            new string[] {"stale","stale-value","hline.ssu.ac.ir","/","1000","0","0","7000"}
        });
        var profiles=FirefoxSessions.Profiles(baseDir);
        Check(profiles.Count==2 && profiles.Exists(delegate(FirefoxProfile p) {return p.Name=="newer" && p.IsDefault;}),"firefox profiles parsed from profiles.ini with the default marker");
        Check(FirefoxSessions.MatchesDomain("hline.ssu.ac.ir","hline.ssu.ac.ir") && FirefoxSessions.MatchesDomain(".ssu.ac.ir","hline.ssu.ac.ir"),"cookie host scoped to the site or its direct parent");
        Check(!FirefoxSessions.MatchesDomain("app.samanesalamat.com","hline.ssu.ac.ir") && !FirefoxSessions.MatchesDomain("evil-ssu.ac.ir","hline.ssu.ac.ir") && !FirefoxSessions.MatchesDomain("ssu.ac.ir.attacker.test","hline.ssu.ac.ir"),"unrelated or look-alike cookie hosts rejected");
        string problem;
        var preferred=FirefoxSessions.Candidates("hline.ssu.ac.ir",older,out problem,baseDir);
        Check(preferred.Count==2 && preferred[0].Profile.Directory==older && preferred[0].Cookies.Count==1,"configured profile wins the one-time profile setting");
        var automatic=FirefoxSessions.Candidates("hline.ssu.ac.ir",null,out problem,baseDir);
        Check(automatic.Count==2 && automatic[0].Profile.Name=="newer" && automatic[0].Cookies.Count==2,"newest profile with live hline cookies chosen automatically");
        Check(!automatic[0].Cookies.Exists(delegate(SessionCookie c) {return c.Name=="other" || c.Name=="stale";}),"other-domain and expired cookies never adopted");
        Check(automatic[0].Cookies.Exists(delegate(SessionCookie c) {return c.Name=="crumb" && c.Domain==".ssu.ac.ir";}),"parent-domain cookie keeps its original scope");
        var otherDomain=FirefoxSessions.Candidates("app.samanesalamat.com",null,out problem,baseDir);
        Check(otherDomain.Count==1 && otherDomain[0].Cookies.Count==1 && otherDomain[0].Cookies[0].Name=="other","cookie set is filtered per requested domain");
        Check(FirefoxSessions.Candidates("no-such-host.test",null,out problem,baseDir).Count==0 && problem!=null,"missing session reports a concrete problem");
    }
    static void FirefoxAdoptionTest()
    {
        string baseDir=Path.Combine(root,"ff-adopt");if(Directory.Exists(baseDir)) Directory.Delete(baseDir,true);
        string profile=Path.Combine(baseDir,"Profiles","adopt.default");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(baseDir,"profiles.ini"),"[Profile0]\r\nName=adopt\r\nIsRelative=1\r\nPath=Profiles/adopt.default\r\nDefault=1\r\n");
        MakeCookieDb(Path.Combine(profile,"cookies.sqlite"),new string[][]
        {
            new string[] {"sid","adopted-value","127.0.0.1","/","0","0","1","5000"}
        });
        TcpListener portFinder=new TcpListener(IPAddress.Loopback,0);portFinder.Start();int port=((IPEndPoint)portFinder.LocalEndpoint).Port;portFinder.Stop();
        string url="http://127.0.0.1:"+port+"/";Exception serverError=null;int requests=0;bool singleCookie=true,noCsrf=true;
        using(HttpListener server=new HttpListener())
        {
            server.Prefixes.Add(url);server.Start();
            Thread worker=new Thread(delegate()
            {
                try
                {
                    for(int i=0;i<3;i++)
                    {
                        var context=server.GetContext();string header=context.Request.Headers["Cookie"]??"";
                        if(i>0)
                        {
                            int occurrences=System.Text.RegularExpressions.Regex.Matches(header,"(^|; )sid=").Count;
                            if(occurrences!=1 || !header.Contains("sid=rotated-value")) singleCookie=false;
                            if(context.Request.Headers["X-CSRF-Token"]!=null) noCsrf=false;
                        }
                        if(i==0) context.Response.Headers.Add("Set-Cookie","sid=rotated-value; Path=/; HttpOnly");
                        string body="{\"data\":{\"user\":{\"id\":\"TEST-U\"}}}";byte[] bytes=Encoding.UTF8.GetBytes(body);
                        context.Response.ContentType="application/json";context.Response.ContentLength64=bytes.Length;context.Response.OutputStream.Write(bytes,0,bytes.Length);context.Response.Close();
                        requests++;
                    }
                }
                catch(Exception ex) {serverError=ex;}
            }) {IsBackground=true};worker.Start();
            var transport=(IHLineTransport)Activator.CreateInstance(typeof(HLineHttpTransport),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[] {new Uri(url)},null);
            using(HLineClient client=new HLineClient(transport))
            {
                string problem;
                var sessions=FirefoxSessions.Candidates("127.0.0.1",null,out problem,baseDir);
                Check(sessions.Count==1 && sessions[0].Cookies.Count==1,"loopback profile exposes exactly one adopted cookie");
                Check(client.AdoptCookies(sessions[0].Cookies) && client.Status==SessionStatus.Connected,"adopted Firefox cookie validates against /me without a password login");
                client.VerifySession();client.VerifySession();
                Check(client.Status==SessionStatus.Connected,"session stays valid across repeated reads");
                Check(client.StatusText=="متصل به سامانه","compact connected status text");
            }
            Check(worker.Join(4000) && serverError==null && requests==3,"loopback adoption completed without external calls");
        }
        Check(singleCookie,"rotated host-only cookie replaces the adopted copy instead of duplicating it");
        Check(noCsrf,"read-only requests never consume a CSRF token");
        using(HLineClient fresh=new HLineClient(new MockServer()))
            Check(fresh.StatusText=="نیاز به ورود در فایرفاکس","compact disconnected status asks for a Firefox login");
    }
    static void NormalMainTests()
    {
        using(var ordinary=new MainForm())
        {
            Check((bool)Field(ordinary,"_allowProductionWrites") && Field(ordinary,"_submissionGateway") is LiveSubmissionGateway && Field(ordinary,"_testRecord")==null,"ordinary MainForm default constructs the live production gateway");
        }
        var mock=new MockServer {Mode="vitals"};var client=new HLineClient(mock);client.Login("synthetic","synthetic","123456");
        var gateway=new Gateway();
        using(var form=new MainForm(client,gateway,null))
        {
            form.Size=new Size(1280,1000);form.Show();Application.DoEvents();
            Check((bool)Field(form,"_allowProductionWrites") && string.IsNullOrEmpty((string)Field(form,"_testNationalId")) && string.IsNullOrEmpty((string)Field(form,"_testRecordId")) && Field(form,"_testRecord")==null,"ordinary startup exposes production submission with no test scope");
            Check((Button)Field(form,"btnReport")!=null && ((Button)Field(form,"btnReport")).Text=="ارسال به سایت","ordinary startup exposes the normal send button");
            Exam a=Exam(false);string ap=Path.Combine(root,"normal-a.spiro");
            typeof(MainForm).GetField("_exam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,a);
            typeof(MainForm).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,ap);
            typeof(MainForm).GetField("_hasPatient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);
            foreach(string method in new string[] {"PopulateMetadata","RenderTrials","RestorePrimaryPreview"}) typeof(MainForm).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null);
            using(var timer=new System.Windows.Forms.Timer {Interval=25})
            {
                bool extra=false;timer.Tick+=delegate {foreach(Form dialog in Application.OpenForms.Cast<Form>().ToArray()) if(dialog!=form) {extra=true;dialog.Close();}};timer.Start();
                ((Button)Field(form,"btnReport")).PerformClick();PumpUntil(delegate {return !(bool)Field(form,"_busy");});timer.Stop();
                Check(!extra && gateway.Uploads==1 && gateway.Creates==1 && a.ExportReferences.Count==1 && a.Submission.State==SubmissionState.Confirmed,"normal button submits the current verified patient without a test scope");
                Check(gateway.Saved!=null && gateway.Saved.NationalId==a.Patient.NationalId && gateway.Saved.RecordId==a.Patient.HLineRecordId && gateway.Saved.PersonId==a.Patient.HLinePersonId,"normal submission targets only the displayed patient's exact record");
            }
            Exam b=Exam(false);b.Patient.NationalId="0000000002";b.Patient.HLinePersonId="TEST-P2";b.Patient.HLineRecordId="3";b.Patient.FirstName="مراجع دوم";
            typeof(MainForm).GetField("_exam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,b);
            typeof(MainForm).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,Path.Combine(root,"normal-b.spiro"));
            typeof(MainForm).GetField("_hasPatient",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,true);Call(form,"PopulateMetadata");Call(form,"RenderTrials");
            Check(gateway.Uploads==1 && gateway.Creates==1 && b.Submission.State==SubmissionState.Draft && b.ExportReferences.Count==0 && a.Submission.State==SubmissionState.Confirmed,"switching to Patient B never auto-submits A or a batch");
            Check(!ReferenceEquals(a.Submission,b.Submission) && !ReferenceEquals(a.Trials,b.Trials) && b.Trials.Count==a.Trials.Count,"Patient A and B examinations remain isolated after switching");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            HttpCookieTest();
            FirefoxStoreTest();
            FirefoxAdoptionTest();
            foreach(string code in new string[] {"missing_mfa_token","invalid_mfa_token","invalid_credentials","needs_mfa_setup"})
            {
                var server=new MockServer {LoginError=code};using(var client=new HLineClient(server)) {bool caught=false;try {client.Login("test","secret","123456");} catch(HLineException ex) {caught=ex.Code==code && !ex.Message.Contains("secret");}Check(caught && client.Status==SessionStatus.AuthenticationError,"mock login fail-closed "+code);}
            }
            var mock=new MockServer();using(var client=new HLineClient(mock))
            {
                client.Login("synthetic","synthetic","123456");Check(client.Status==SessionStatus.Connected,"valid authenticated session");
                mock.Mode="rawme";client.VerifySession();Check(client.Status==SessionStatus.Connected,"frontend raw /me user shape verified against login identity");
                foreach(string mode in new string[] {"missing","nonmatch","duplicate","identity","malformed"}) {mock.Mode=mode;Refuses(delegate {client.Lookup("0000000001");},"patient lookup rejects "+mode);}
                mock.Mode="";var person=client.Lookup("۰۰۰۰۰۰۰۰۰۱");Check(person.Patient.HLinePersonId=="TEST-P" && person.Patient.FirstName=="" && person.Patient.LastName=="" && person.Patient.ServerFullName.Length>0,"exact match retains combined full name without arbitrary splitting");
                Check(person.Patient.BirthDate==new DateTime(1990,5,17) && person.Patient.Sex==Sex.Female,"verified birth-date and gender mappings");
                var latest=HLineClient.LatestUnambiguous(person.Records);Check(latest.Id=="2","latest record selected by admission date despite smaller ID");
                var missing=client.PopulateRecord(person,latest);Check(double.IsNaN(missing.HeightCm) && double.IsNaN(missing.WeightKg),"no historical anthropometry borrowed from another record");
                mock.Mode="vitals";var populated=client.LoadLatestPatient("0000000001");Check(populated.HeightCm==164 && populated.WeightKg==74 && !populated.AnthropometryConfirmed && populated.HLineRecordId=="2","latest person and record populate automatically without fabricated operator confirmation");
                populated.SetAgeAt(new DateTime(2026,5,16));Check(populated.Age==35 && populated.ReferenceAgeYears>35,"source birth-date computes age at selected examination before birthday");
                using(PatientForm f=new PatientForm(populated,false,client,new DateTime(2026,5,16)))
                {
                    Patient p;string issue;Check(f.TryRead(out p,out issue),"fetched measurements need no confirmation checkbox");
                    ((TextBox)f.Controls.Find("Height",true)[0]).Text="165";
                    Check(f.TryRead(out p,out issue) && p.HeightCm==165 && p.HLineRecordId=="2" && p.AnthropometrySource=="operator_edited_after_hline_lookup","optional operator correction keeps correct person-record link");Shot(f,"03-patient-lookup");
                }
                AutomaticPatientTests(client,mock);
                mock.Mode="existing";Check(client.HasSpirometry(latest),"existing spirometry detected for exact record");
                person.Records.Add(new MedicalRecord {Id="TIE",Date=latest.Date,DateText=latest.DateText,Status="complete"});Check(HLineClient.LatestUnambiguous(person.Records)==null,"latest-date ambiguity requires explicit resolution");
                Check(mock.Posts==1 && mock.Calls.All(c=>c.StartsWith("GET ") || c=="POST /auth/login"),"read integration mutates no clinical module");
                mock.Network=true;bool safe=false;try {client.Lookup("0000000001");} catch(HLineException ex) {safe=!ex.Message.Contains("DO_NOT_LEAK_SECRET");}Check(safe && client.Status==SessionStatus.Unavailable,"network errors sanitized without secrets");
            }
            using(var client=new HLineClient(new MockServer())) using(var login=new HLineLoginForm(client)) Shot(login,"02-hline-login");
            LoginUiTests();
            VextRuleTests();
            SelectedVextTests();
            VextUiTests();
            OneClickMainTests();
            NormalMainTests();
            Exam exam=Exam(false);exam.SelectSiteManeuver(exam.Trials[0].ManeuverId);SubmissionDraft draft=Draft(exam);
            VextSelection vx=ClinicVext.SelectForExam(exam); ExamSummary best=ExamSummary.BuildSelected(exam.Trials,exam.ReportManeuverIds);
            Check(draft.RecordId=="2" && draft.ExaminationDate==new DateTime(2026,10,19) && vx.Available && vx.ValueMl==132 && draft.Fields["vext"]==132 && draft.Fields["vext"]!=111 && draft.Vext.ManeuverId==vx.ManeuverId && draft.ManeuverId==best.BestValues.Fvc.SourceManeuverId,"submission binds exact record, Gregorian date, clinic VEXT and best-FVC source");
            var doc=Json(draft.Snapshot);var bestJson=Obj(doc["bestValues"]);var siteJson=Obj(doc["siteExport"]);var manualVext=Obj(Obj(doc["spirometry"])["VEXT"]);var selectionJson=Obj(doc["vextSelection"]);
            Check(N(Obj(bestJson["fvc"])["value"])==best.BestValues.Fvc.Value.Value && N(Obj(bestJson["fev1"])["value"])==best.BestValues.Fev1.Value.Value && N(Obj(bestJson["fev1Fvc"])["value"])==best.BestValues.Fev1Fvc.Value.Value,"JSON bestValues are the same independent aggregate used by the report");
            Check(bestJson["scope"].ToString()=="operator_selected_max_three_only_not_all_eligible_maneuvers" && bestJson["qualityLimited"]!=null,"JSON bestValues declare selected-three scope and quality status");
            Check(siteJson["status"].ToString()=="retired_single_site_maneuver_use_bestValues" && Obj(siteJson["measurements"]).Count==0,"workflow4 retires the single site export");
            Check(manualVext["value"]==null && manualVext["verificationStatus"].ToString()=="not_validated","legacy per-maneuver review does not become an exam-level manual VEXT");
            Check(selectionJson["rule"].ToString().Contains("EVol") && selectionJson["qualityGate"].ToString().StartsWith("none") && N(selectionJson["valueMl"])==132 && selectionJson["sourceManeuverId"].ToString()==vx.ManeuverId,"clinic rule and quality separation are recorded with provenance");

            // Workflow 4: HLine, JSON and the provenance gate all consume the same
            // selected-three VEXT source, not the higher unselected EVol values.
            Exam selectedExam=Exam(false);
            selectedExam.Trials[3].Result.Bev=.185;selectedExam.Trials[4].Result.BevAvailable=true;selectedExam.Trials[4].Result.Bev=.199;
            double bestBefore=ExamSummary.BuildSelected(selectedExam.Trials,selectedExam.ReportManeuverIds).Fvc;
            ExaminationSnapshot selectedSnapshot=ExaminationSnapshot.Capture(selectedExam);
            VextSelection selectedValue=ClinicVext.SelectForExam(selectedExam);
            Check(selectedValue.Available && selectedValue.ValueMl==132 && selectedExam.ReportManeuverIds.Contains(selectedValue.ManeuverId),"VEXT is extracted only from the selected three even with higher unselected EVol 185 and 199");
            Check(Math.Abs(ExamSummary.BuildSelected(selectedExam.Trials,selectedExam.ReportManeuverIds).Fvc-bestBefore)<1e-12,"VEXT selection never changes the selected BEST VALUES");
            SubmissionDraft selectedDraft=new SubmissionDraft(selectedSnapshot,ResultExport.SaveToDirectory(Path.Combine(root,"selected-vext"),selectedSnapshot));
            Check(selectedDraft.Fields["vext"]==132 && selectedDraft.Vext.ManeuverId==selectedValue.ManeuverId && selectedDraft.Vext.AcquisitionSequence==selectedValue.AcquisitionSequence,"HLine VEXT matches the selected-three calculation and source");
            var selectedDoc=Json(selectedSnapshot);var selectedJson=Obj(selectedDoc["vextSelection"]);
            object[] exportedSelection=(object[])Obj(selectedDoc["reportSelection"])["maneuverIds"];
            Check(N(selectedJson["valueMl"])==132 && selectedJson["sourceManeuverId"].ToString()==selectedValue.ManeuverId && exportedSelection.Any(x=>Convert.ToString(x)==selectedValue.ManeuverId),"workflow-4 JSON records VEXT value and source from the selected maneuver");
            Check(selectedJson["scope"].ToString().Contains("selected_max_three") && selectedJson["rule"].ToString().Contains("selected"),"workflow-4 JSON metadata declares selected_max_three scope");
            Check(DeviceProvenanceValidator.Validate(selectedSnapshot).Allowed,"provenance accepts the selected-three VEXT source");
            Exam badSource=Exam(false);badSource.Trials[1].DeviceCaptureSequence=0;
            ExaminationSnapshot badSourceSnapshot=ExaminationSnapshot.Capture(badSource);
            DeviceProvenanceResult badSourceGate=DeviceProvenanceValidator.Validate(badSourceSnapshot);
            Check(!badSourceGate.Allowed && badSourceGate.Issues.Any(i=>i.Metric=="VEXT" && i.ManeuverId==badSource.Trials[1].ManeuverId),"provenance rejects the exact selected VEXT source when its device capture sequence is invalid");
            using(Image selectedJpeg=Image.FromStream(new MemoryStream(selectedDraft.JpegBytes)))
                Check(selectedJpeg.Width==1800 && selectedJpeg.Height==2546 && ResultExport.JpegQuality==70,"selected VEXT patch keeps JPEG quality 70 and 1800x2546 dimensions");

            Exam unselectedInvalid=Exam(false);
            unselectedInvalid.Trials[3].Result.Bev=.185;unselectedInvalid.Trials[3].DeviceValues=null;
            ExaminationSnapshot unselectedInvalidSnapshot=ExaminationSnapshot.Capture(unselectedInvalid);
            Check(ClinicVext.SelectForExam(unselectedInvalid).ValueMl==132 && DeviceProvenanceValidator.Validate(unselectedInvalidSnapshot).Allowed,"an invalid unselected maneuver with higher EVol neither supplies VEXT nor blocks submission");

            Exam legacyExam=Exam(false);legacyExam.SelectionWorkflowVersion=3;legacyExam.Trials[3].Result.Bev=.185;
            ExaminationSnapshot legacySnapshot=ExaminationSnapshot.Capture(legacyExam);
            var legacyJson=Obj(Json(legacySnapshot)["vextSelection"]);
            Check(N(legacyJson["valueMl"])==185 && legacyJson["scope"].ToString()=="active_real_usb_live_maneuvers_only" && legacyJson["rule"].ToString().Contains("real maneuvers of this examination"),"workflow-3 JSON keeps its historical all-active VEXT scope and value");

            Check(Math.Abs(draft.Fields["fvc_amount"].Value-best.BestValues.Fvc.Value.Value)<1e-12 && Math.Abs(draft.Fields["fev1_amount"].Value-best.BestValues.Fev1.Value.Value)<1e-12 && Math.Abs(draft.Fields["fev1_fvc_percent"].Value-best.BestValues.Fev1Fvc.Value.Value)<1e-12 && Math.Abs(draft.Fields["fef2575_amount"].Value-best.BestValues.Fef2575.Value.Value)<1e-12 && Math.Abs(draft.Fields["pef_amount"].Value-best.BestValues.Pef.Value.Value)<1e-12,"all five HLine fields use independent session best values, not one radio maneuver");
            byte[] image=draft.JpegBytes;image[0]=0;Check(draft.JpegBytes[0]==255,"exact JPEG bytes retained immutably");
            var other=ExaminationSnapshot.Capture(Exam(false));var otherPackage=ResultExport.SaveToDirectory(root,other);Refuses(delegate {new SubmissionDraft(draft.Snapshot,otherPackage);},"JPG from another snapshot cannot be attached to selected examination");
            Check(draft.JpegFilename=="Spirometry-Report.jpg" && SubmissionDraft.JpegMimeType=="image/jpeg","submission retains JPEG filename and MIME without a preview approval");
            Check(typeof(MainForm).Assembly.GetType("SpiroLab.Ui.SubmissionForm")==null && typeof(MainForm).Assembly.GetType("SpiroLab.Ui.MedicalRecordForm")==null,"extra record and submission confirmation dialogs removed");
            var g=new Gateway();var journal=new SubmissionJournal();int persisted=0;var coordinator=new SubmissionCoordinator(g,journal,delegate {persisted++;},TestDestination());
            coordinator.Submit(draft);Check(coordinator.State==SubmissionState.Confirmed && g.Uploads==1 && g.Creates==1 && persisted>=5,"single call submits and succeeds only after numeric and exact attachment readback");
            coordinator.Submit(draft);Check(coordinator.AlreadyPresent && coordinator.State==SubmissionState.Confirmed && g.Uploads==1 && g.Creates==1,"duplicate clicks reconcile without repeating writes");Check(coordinator.State==SubmissionState.Confirmed,"duplicate click preserves verified success state");
            using(var started=new ManualResetEvent(false)) using(var release=new ManualResetEvent(false))
            {
                var concurrent=new Gateway {UploadStarted=started,ContinueUpload=release};var c=new SubmissionCoordinator(concurrent,new SubmissionJournal(),delegate {},TestDestination());Exception failure=null;
                var worker=new Thread(delegate() {try {c.Submit(draft);} catch(Exception ex) {failure=ex;}});worker.Start();Check(started.WaitOne(3000),"concurrent submission reached single upload");Refuses(delegate {c.Submit(draft);},"simultaneous double click blocked");release.Set();Check(worker.Join(3000) && failure==null && concurrent.Creates==1,"concurrent clicks create exactly one result");
            }
            var uncertain=new Gateway {TimeoutAfterCreate=true};var uj=new SubmissionJournal();var uc=new SubmissionCoordinator(uncertain,uj,delegate {},TestDestination());Refuses(delegate {uc.Submit(draft);},"timeout after committed create remains uncertain");
            Check(uc.State==SubmissionState.FailedOrUncertain && uncertain.Creates==1,"no automatic retry after uncertain response");
            var restarted=new SubmissionCoordinator(uncertain,uj,delegate {},TestDestination());restarted.Submit(draft);Check(restarted.State==SubmissionState.Confirmed && uncertain.Creates==1 && restarted.AlreadyPresent,"uncertain journal is reconciled read-only on the next click without resend");Check(restarted.Recheck(draft) && restarted.State==SubmissionState.Confirmed && uncertain.Creates==1,"read-only recheck resolves uncertain committed result");
            // Production recheck must work without a designated test record, and it
            // must never turn its read-only readback into a second write.
            var noTestGateway=new Gateway {TimeoutAfterCreate=true};var noTestJournal=new SubmissionJournal();
            var noTest=new SubmissionCoordinator(noTestGateway,noTestJournal,delegate {},null);
            Refuses(delegate {noTest.Submit(draft);},"uncertain production submission without a designated test record is not retried");
            Check(noTest.State==SubmissionState.FailedOrUncertain && noTestGateway.Uploads==1 && noTestGateway.Creates==1,"production uncertain journal keeps its single write attempt");
            var readOnlyRecheck=new SubmissionCoordinator(noTestGateway,noTestJournal,delegate {},null);
            Check(readOnlyRecheck.Recheck(draft) && readOnlyRecheck.State==SubmissionState.Confirmed && noTestGateway.Uploads==1 && noTestGateway.Creates==1,"read-only recheck without a test record verifies the stored result and performs no second upload or POST");
            Check(readOnlyRecheck.Recheck(draft) && noTestGateway.Uploads==1 && noTestGateway.Creates==1,"repeated read-only recheck remains idempotent");
            Exam otherExam=Exam(false);otherExam.Patient.NationalId="0000000002";otherExam.Patient.HLinePersonId="OTHER-P";otherExam.Patient.HLineRecordId="OTHER-R";
            SubmissionDraft otherDraft=Draft(otherExam);
            Check(!new SubmissionCoordinator(noTestGateway,noTestJournal,delegate {},null).Recheck(otherDraft) && noTestGateway.Uploads==1 && noTestGateway.Creates==1,"read-only recheck cannot reconcile another patient's stored result");
            exam.Submission=uj;string journalPath=Path.Combine(root,"submission-journal.spiro");ExamStore.Save(journalPath,exam);var reloaded=ExamStore.Load(journalPath);Check(reloaded.Submission.WriteAttempted && reloaded.Submission.Reference==uj.Reference && reloaded.Submission.FileId==uj.FileId,"submission reference and attempted-write journal survive actual XML restart");
            foreach(int failure in new int[] {0,1,2})
            {
                var gateway=new Gateway {FailAssociation=failure==0,UnverifiedJpg=failure==1,FailUpload=failure==2};var j=new SubmissionJournal();var c=new SubmissionCoordinator(gateway,j,delegate {},TestDestination());Refuses(delegate {c.Submit(draft);},"partial or unverifiable submission "+failure);Check(c.State==SubmissionState.FailedOrUncertain && gateway.Uploads==1,"partial operation never reported success "+failure);
            }
            var existing=new Gateway {Saved=g.Saved};var ej=new SubmissionJournal();var ec=new SubmissionCoordinator(existing,ej,delegate {},TestDestination());ec.Submit(draft);Check(ec.AlreadyPresent && ec.State==SubmissionState.Confirmed && existing.Uploads==0 && existing.Creates==0,"identical existing result is recognized without overwrite or resubmit");
            var different=new Gateway {Saved=g.Saved};different.Saved.Fields=new Dictionary<string,double?>(different.Saved.Fields);different.Saved.Fields["fvc_amount"]=different.Saved.Fields["fvc_amount"]+0.1;Refuses(delegate {new SubmissionCoordinator(different,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"different existing clinical result never silently overwritten");Check(different.Uploads==0 && different.Creates==0,"conflicting existing result performs no writes");
            var unverified=new Gateway {Verified=false};Refuses(delegate {new SubmissionCoordinator(unverified,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"unverified write contract blocks all writes");
            var changed=new Gateway {ChangedDestination=true};Refuses(delegate {new SubmissionCoordinator(changed,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"wrong or changed destination blocks writes");
            var normalProduction=new Gateway();var normalCoordinator=new SubmissionCoordinator(normalProduction,new SubmissionJournal(),delegate {},null);normalCoordinator.Submit(draft);Check(normalCoordinator.State==SubmissionState.Confirmed && normalProduction.Uploads==1 && normalProduction.Creates==1,"normal production path submits only after gateway destination verification without a test record");
            using(SubmissionTiming timing=new SubmissionTiming("test"))
            {
                var measured=new SubmissionCoordinator(new Gateway(),new SubmissionJournal(),delegate {},TestDestination(),timing);measured.Submit(draft);timing.Complete("confirmed");
                string stages=timing.Summary;
                Check(stages.Contains("draft_validate") && stages.Contains("jpg_upload") && stages.Contains("create_post") && stages.Contains("server_readback") && stages.Contains("module_verify"),"submission timing records distinct acquisition, upload, create, readback and module stages");
                string log=File.Exists(SubmissionTiming.LogPath)?File.ReadAllText(SubmissionTiming.LogPath):"";
                Check(log.Contains("operation=test") && log.Contains("draft_validate=") && log.IndexOf(draft.NationalId,StringComparison.Ordinal)<0 && log.IndexOf(draft.PersonId,StringComparison.Ordinal)<0 && log.IndexOf(draft.JpegSha256,StringComparison.Ordinal)<0,"timing log contains generic stages only, never patient, record or payload identifiers");
            }
            var restricted=new Gateway();
            var wrongTest=TestDestination();wrongTest.Id="OTHER";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"integration writes restricted to designated test medical record");
            wrongTest=TestDestination();wrongTest.PersonId="OTHER";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"test record must belong to exact designated person");
            wrongTest=TestDestination();wrongTest.NationalId="0000000002";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"test record must match exact designated national ID");Check(restricted.Uploads==0 && restricted.Creates==0,"test-scope rejection performs no writes");
            Refuses(delegate {Draft(Exam(true)).ValidateForSubmission();},"simulated source cannot be submitted to actual HLine");
            foreach(Trial t in exam.Trials) {t.Result.BevAvailable=true;t.Result.Bev=.25;}
            SubmissionDraft noVext=Draft(exam);Exception vextProblem=null;try {noVext.ValidateForSubmission();}catch(InvalidOperationException ex) {vextProblem=ex;}
            Check(!noVext.Fields["vext"].HasValue && noVext.Vext.ValueMl==0 && noVext.Vext.ManeuverId.Length==0,"unavailable clinic VEXT stays null and is never zero-filled");
            Check(vextProblem!=null && vextProblem.Message.Contains("هیچ مانور واقعی") && !exam.Trials.Exists(t=>t.Result.BevAvailable && t.Result.Bev*1000<200),"no eligible clinic EVol blocks with the actual problem instead of inventing a value");
            Console.WriteLine("ALL "+checks+" HLINE INTEGRATION CHECKS PASSED");return 0;
        }
        catch(Exception ex) {Console.WriteLine(ex);return 1;}
    }
}

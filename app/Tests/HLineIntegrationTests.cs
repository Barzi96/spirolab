using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;
using SpiroLab.Ui;
class HLineIntegrationTests
{
    static int checks;static string root;
    static void Check(bool ok,string name) {if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Refuses(Action a,string name) {bool refused=false;try {a();} catch(Exception) {refused=true;} Check(refused,name);}
    static object Field(object o,string name) {return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
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
            Check(form.Controls.Find("FetchHLinePatient",true).Length==0 && form.Controls.Find("ConfirmAnthropometry",true).Length==0 && Application.OpenForms.Count==1,"automatic patient flow has no fetch button, checkbox or record dialog");
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
        public string RecordsOverride,VitalsOverride;
        public int SearchDelay;
        public ManualResetEvent SearchStarted;
        public HLineResponse Send(string method,string path,string body,string csrf)
        {
            Calls.Add(method+" "+path.Split('?')[0]);
            if(Network) throw new IOException("DO_NOT_LEAK_SECRET");
            if(path=="/auth/csrf") return R(200,"{\"csrf\":\"synthetic-csrf\"}");
            if(method=="POST")
            {
                Posts++;if(path!="/auth/login" || csrf!="synthetic-csrf") throw new Exception("Forbidden mock write");
                var p=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(body);
                Check(p.ContainsKey("username") && p.ContainsKey("password") && Convert.ToInt32(p["mfa_token_method"])==1,"verified authentication request keys and CSRF");
                return LoginError.Length==0?R(200,"{\"data\":{\"data\":{\"user\":{\"id\":\"synthetic-user\"}}}}"):R(422,"{\"errors\":[\""+LoginError+"\"]}");
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
        public bool Verified=true,TimeoutAfterCreate,FailAssociation,FailUpload,ChangedDestination,UnverifiedJpg;
        public int Uploads,Creates; public RemoteSpirometry Saved;
        public ManualResetEvent UploadStarted,ContinueUpload;
        public bool ContractVerified {get{return Verified;}}
        public void VerifyDestination(SubmissionDraft d) {if(ChangedDestination) throw new Exception("destination_changed");}
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
        e.Patient=new Patient {FirstName="مراجع",LastName="ساختگی",NationalId="0000000001",Origin=Origin.Caucasian,HeightCm=164,WeightKg=74,Age=44,HLinePersonId="TEST-P",HLineRecordId="2",HLineRecordDate="2026/11/01",HLineRecordStatus="complete",AnthropometryConfirmed=true};
        e.ExaminationDate=new DateTime(2026,10,19);
        for(int i=0;i<5;i++) {Trial t=Simulator.Build(e.Patient,100+i);t.Simulated=simulated;e.AddManeuver(t);}
        Trial site=e.OperatorSelectedManeuver; site.Result.Fet=6;site.Result.Cough=false;
        site.ReviewedVextMl=132;site.ReviewedVextSource="synthetic mock fixture";site.VextReviewer="بازبین آزمایشی";site.VextManuallyReviewed=true;
        e.SelectSiteManeuver(site.ManeuverId);return e;
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
                        else if(i==1) {cookieSeen=c.Request.Headers["Cookie"]!=null && c.Request.Headers["Cookie"].Contains("synthetic_session=memory_only");csrfSeen=c.Request.Headers["X-CSRF-Token"]=="local-csrf";body="{\"data\":{\"user\":{\"id\":\"TEST-U\"}}}";}
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
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            HttpCookieTest();
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
            OneClickMainTests();
            Exam exam=Exam(false);SubmissionDraft draft=Draft(exam);
            Check(draft.RecordId=="2" && draft.ExaminationDate==new DateTime(2026,10,19) && draft.Fields["vext"]==132,"submission binds exact record, Gregorian examination date and same-maneuver VEXT");
            Check(draft.Fields["fvc_amount"]==exam.OperatorSelectedManeuver.Result.Fvc && draft.Fields["fev1_amount"]==exam.OperatorSelectedManeuver.Result.Fev1 && draft.Fields["fev1_fvc_percent"]==exam.OperatorSelectedManeuver.Result.Fev1Fvc && draft.Fields["fef2575_amount"]==exam.OperatorSelectedManeuver.Result.Fef2575 && draft.Fields["pef_amount"]==exam.OperatorSelectedManeuver.Result.Pef,"all five measured core fields use only radio-selected maneuver");
            byte[] image=draft.JpegBytes;image[0]=0;Check(draft.JpegBytes[0]==255,"exact JPEG bytes retained immutably");
            var other=ExaminationSnapshot.Capture(Exam(false));var otherPackage=ResultExport.SaveToDirectory(root,other);Refuses(delegate {new SubmissionDraft(draft.Snapshot,otherPackage);},"JPG from another snapshot cannot be attached to selected examination");
            Check(draft.JpegFilename=="Spirometry-Report.jpg" && SubmissionDraft.JpegMimeType=="image/jpeg","submission retains JPEG filename and MIME without a preview approval");
            Check(typeof(MainForm).Assembly.GetType("SpiroLab.Ui.SubmissionForm")==null && typeof(MainForm).Assembly.GetType("SpiroLab.Ui.MedicalRecordForm")==null,"extra record and submission confirmation dialogs removed");
            var g=new Gateway();var journal=new SubmissionJournal();int persisted=0;var coordinator=new SubmissionCoordinator(g,journal,delegate {persisted++;},TestDestination());
            coordinator.Submit(draft);Check(coordinator.State==SubmissionState.Confirmed && g.Uploads==1 && g.Creates==1 && persisted>=5,"single call submits and succeeds only after numeric and exact attachment readback");
            Refuses(delegate {coordinator.Submit(draft);},"duplicate clicks cannot repeat writes");Check(coordinator.State==SubmissionState.Confirmed,"blocked duplicate preserves verified success state");
            using(var started=new ManualResetEvent(false)) using(var release=new ManualResetEvent(false))
            {
                var concurrent=new Gateway {UploadStarted=started,ContinueUpload=release};var c=new SubmissionCoordinator(concurrent,new SubmissionJournal(),delegate {},TestDestination());Exception failure=null;
                var worker=new Thread(delegate() {try {c.Submit(draft);} catch(Exception ex) {failure=ex;}});worker.Start();Check(started.WaitOne(3000),"concurrent submission reached single upload");Refuses(delegate {c.Submit(draft);},"simultaneous double click blocked");release.Set();Check(worker.Join(3000) && failure==null && concurrent.Creates==1,"concurrent clicks create exactly one result");
            }
            var uncertain=new Gateway {TimeoutAfterCreate=true};var uj=new SubmissionJournal();var uc=new SubmissionCoordinator(uncertain,uj,delegate {},TestDestination());Refuses(delegate {uc.Submit(draft);},"timeout after committed create remains uncertain");
            Check(uc.State==SubmissionState.FailedOrUncertain && uncertain.Creates==1,"no automatic retry after uncertain response");
            var restarted=new SubmissionCoordinator(uncertain,uj,delegate {},TestDestination());Refuses(delegate {restarted.Submit(draft);},"uncertain journal blocks resend after coordinator restart");Check(restarted.Recheck(draft) && restarted.State==SubmissionState.Confirmed && uncertain.Creates==1,"read-only recheck resolves uncertain committed result");
            exam.Submission=uj;string journalPath=Path.Combine(root,"submission-journal.spiro");ExamStore.Save(journalPath,exam);var reloaded=ExamStore.Load(journalPath);Check(reloaded.Submission.WriteAttempted && reloaded.Submission.Reference==uj.Reference && reloaded.Submission.FileId==uj.FileId,"submission reference and attempted-write journal survive actual XML restart");
            foreach(int failure in new int[] {0,1,2})
            {
                var gateway=new Gateway {FailAssociation=failure==0,UnverifiedJpg=failure==1,FailUpload=failure==2};var j=new SubmissionJournal();var c=new SubmissionCoordinator(gateway,j,delegate {},TestDestination());Refuses(delegate {c.Submit(draft);},"partial or unverifiable submission "+failure);Check(c.State==SubmissionState.FailedOrUncertain && gateway.Uploads==1,"partial operation never reported success "+failure);
            }
            var existing=new Gateway {Saved=g.Saved};Refuses(delegate {new SubmissionCoordinator(existing,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"existing clinical result never silently overwritten");
            var unverified=new Gateway {Verified=false};Refuses(delegate {new SubmissionCoordinator(unverified,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"unverified write contract blocks all writes");
            var changed=new Gateway {ChangedDestination=true};Refuses(delegate {new SubmissionCoordinator(changed,new SubmissionJournal(),delegate {},TestDestination()).Submit(draft);},"wrong or changed destination blocks writes");
            var restricted=new Gateway();Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},null).Submit(draft);},"no writes until dedicated test destination supplied");
            var wrongTest=TestDestination();wrongTest.Id="OTHER";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"integration writes restricted to designated test medical record");
            wrongTest=TestDestination();wrongTest.PersonId="OTHER";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"test record must belong to exact designated person");
            wrongTest=TestDestination();wrongTest.NationalId="0000000002";Refuses(delegate {new SubmissionCoordinator(restricted,new SubmissionJournal(),delegate {},wrongTest).Submit(draft);},"test record must match exact designated national ID");Check(restricted.Uploads==0 && restricted.Creates==0,"test-scope rejection performs no writes");
            Refuses(delegate {Draft(Exam(true)).ValidateForSubmission();},"simulated source cannot be submitted to actual HLine");
            exam.OperatorSelectedManeuver.ReviewedVextMl=null;Refuses(delegate {Draft(exam).ValidateForSubmission();},"missing VEXT is never fabricated");
            Console.WriteLine("ALL "+checks+" HLINE INTEGRATION CHECKS PASSED");return 0;
        }
        catch(Exception ex) {Console.WriteLine(ex);return 1;}
    }
}

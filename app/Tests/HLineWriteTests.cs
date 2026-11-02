using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;

class HLineWriteTests
{
    static int checks;
    static string root;
    static void Check(bool ok,string name) {if(!ok) throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static object Field(object o,string name) {return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static MedicalRecord Destination() {return new MedicalRecord {Id="R-1",PersonId="P-1",NationalId="0000000001",DateText="2026/11/01",Status="complete"};}
    static HLineClient Client(MockHLine server,out HLineHttpTransport transport)
    {
        ConstructorInfo ctor=typeof(HLineHttpTransport).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new Type[] {typeof(Uri)},null);
        transport=(HLineHttpTransport)ctor.Invoke(new object[] {new Uri(server.BaseUrl)});
        HLineClient client=new HLineClient(transport);
        client.Login("test-user","test-password","123456");
        return client;
    }
    static Exam Exam()
    {
        Exam e=SpiroLab.Exam.CreateNew(new AppSettings {OperatorName="آزمون ساختگی"});
        e.Patient=new Patient {FirstName="مراجع",LastName="ساختگی",NationalId="0000000001",Origin=Origin.Caucasian,HeightCm=170,WeightKg=70,Age=44,HLinePersonId="P-1",HLineRecordId="R-1",HLineRecordDate="2026/11/01",HLineRecordStatus="complete",AnthropometryConfirmed=true};
        e.ExaminationDate=new DateTime(2026,10,19);
        for(int i=0;i<3;i++) {Trial t=Simulator.Build(e.Patient,100+i);t.Simulated=false;e.AddManeuver(t);t.Result.BevAvailable=t.Result.BevOk=true;t.Result.Bev=0.132;t.Result.Cough=false;}
        Trial site=e.Trials[0];site.Result.Fet=6;site.ReviewedVextMl=132;site.ReviewedVextSource="synthetic write test";site.VextReviewer="آزمون ساختگی";site.VextManuallyReviewed=true;
        e.SelectSiteManeuver(site.ManeuverId);return e;
    }
    static SubmissionDraft Draft(Exam e) {ExaminationSnapshot s=ExaminationSnapshot.Capture(e);return new SubmissionDraft(s,ResultExport.SaveToDirectory(root,s));}
    static SubmissionCoordinator Coordinator(ISubmissionGateway g) {return new SubmissionCoordinator(g,new SubmissionJournal(),delegate {},Destination());}
    static Dictionary<string,object> Json(string text) {return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(text);}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
            using(MockHLine server=new MockHLine())
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);
                    Exam exam=Exam();SubmissionDraft draft=Draft(exam);
                    SubmissionCoordinator first=Coordinator(gateway);first.Submit(draft);
                    Check(first.State==SubmissionState.Confirmed && !first.AlreadyPresent,"live gateway submits and verifies one result");
                    Check(server.Uploads==1 && server.Creates==1,"one multipart upload and one spirometry POST");
                    Check(server.UploadedModule=="spirometry" && server.UploadedType=="image/jpeg" && server.UploadedFilename.EndsWith(".jpg") && server.FileBytes.Length==draft.JpegBytes.Length,"multipart carries module, filename, MIME and exact JPG");
                    Dictionary<string,object> payload=server.Saved;
                    Check(Convert.ToString(payload["test_date"])=="2026/10/19" && Convert.ToString(payload["performed_by"])=="USER-1","Gregorian test date and authenticated user id");
                    Check(payload.ContainsKey("fvc_amount") && payload.ContainsKey("fev1_amount") && payload.ContainsKey("vext") && !payload.ContainsKey("fev1_fvc_percent"),"writes amounts and VEXT without inventing the derived ratio field");
                    Check(Convert.ToDouble(payload["vext"])==132.0,"VEXT comes from the clinic selection, not zero");
                    Check(payload["result"]!=null && Convert.ToString(payload["result"]).Length>0,"interpretation is mapped to a HLine result value");
                    Check(payload["bronchodilator_type"]==null && payload["srp"]==null && payload["notes"]==null,"empty optional sections stay null");

                    // A second click against the identical server result must not write again.
                    SubmissionCoordinator second=Coordinator(gateway);second.Submit(draft);
                    Check(second.AlreadyPresent && second.State==SubmissionState.Confirmed && server.Uploads==1 && server.Creates==1,"identical existing result is read back with no resubmit");

                    // Different existing clinical values are a hard conflict.
                    double old=Convert.ToDouble(server.Saved["fvc_amount"]);server.Saved["fvc_amount"]=old+0.2;
                    bool conflict=false;try {Coordinator(gateway).Submit(draft);} catch(InvalidOperationException) {conflict=true;}
                    Check(conflict && server.Uploads==1 && server.Creates==1,"different existing result stops with no overwrite");
                    server.Saved["fvc_amount"]=old;
                }
            }

            // CSRF rotation: the first upload is rejected with 419, then retried once with a fresh token.
            using(MockHLine server=new MockHLine {ExpireNextWrite=true})
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);SubmissionDraft draft=Draft(Exam());
                    SubmissionCoordinator c=Coordinator(gateway);c.Submit(draft);
                    Check(c.State==SubmissionState.Confirmed && server.CsrfRefreshes>=2 && server.Uploads==1,"419 refreshes CSRF and retries the write once");
                }
            }

            // Lost upload response is reconciled by listing and hashing the new file.
            using(MockHLine server=new MockHLine {AbortAfterUpload=true})
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);SubmissionDraft draft=Draft(Exam());
                    SubmissionCoordinator c=Coordinator(gateway);c.Submit(draft);
                    Check(c.State==SubmissionState.Confirmed && server.Uploads==1 && server.Creates==1,"lost upload response is reconciled without a duplicate upload");
                }
            }

            // Lost create response remains uncertain, then read-only Recheck resolves it.
            using(MockHLine server=new MockHLine {AbortAfterCreate=true})
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);SubmissionDraft draft=Draft(Exam());
                    SubmissionCoordinator c=Coordinator(gateway);bool failed=false;try {c.Submit(draft);} catch(HLineException) {failed=true;}
                    Check(failed && c.State==SubmissionState.FailedOrUncertain && server.Creates==1,"lost create response is recorded as uncertain, never retried");
                    Check(c.Recheck(draft) && c.State==SubmissionState.Confirmed && server.Creates==1,"read-only recheck resolves the committed result");
                }
            }

            // Wrong center and missing file scope are refused before any write.
            using(MockHLine server=new MockHLine {Center="OTHER"})
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);SubmissionDraft draft=Draft(Exam());
                    bool refused=false;try {Coordinator(gateway).Submit(draft);} catch(HLineException) {refused=true;}
                    Check(refused && server.Uploads==0 && server.Creates==0,"wrong medical center is refused before upload");
                }
            }
            using(MockHLine server=new MockHLine {Scopes=new string[] {"mrsp_u","mrsp_p"}})
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);SubmissionDraft draft=Draft(Exam());
                    bool refused=false;try {Coordinator(gateway).Submit(draft);} catch(HLineException) {refused=true;}
                    Check(refused && server.Uploads==0 && server.Creates==0,"missing file permission is refused before upload");
                }
            }

            // Free text is preserved as Other/result_other instead of being forced into a category.
            using(MockHLine server=new MockHLine())
            {
                HLineHttpTransport transport;using(HLineClient client=Client(server,out transport))
                {
                    LiveSubmissionGateway gateway=new LiveSubmissionGateway(client);Exam e=Exam();e.ReviewedInterpretation="متن بازپزشک برای آزمون";SubmissionDraft draft=Draft(e);
                    gateway.VerifyDestination(draft);string id=gateway.UploadJpeg(draft,"test");
                    MethodInfo build=typeof(LiveSubmissionGateway).GetMethod("BuildPayload",BindingFlags.Instance|BindingFlags.NonPublic);
                    Dictionary<string,object> payload=(Dictionary<string,object>)build.Invoke(gateway,new object[] {draft,id});
                    Check(Convert.ToString(payload["result"])=="Other" && Convert.ToString(payload["result_other"])=="متن بازپزشک برای آزمون","free-text interpretation is preserved as Other without inventing a category");
                }
            }
            Console.WriteLine("ALL "+checks+" HLINE WRITE CHECKS PASSED");return 0;
        }
        catch(Exception e) {Console.WriteLine(e);return 1;}
    }

    sealed class MockHLine : IDisposable
    {
        readonly HttpListener listener=new HttpListener();
        readonly Thread worker;
        public string BaseUrl {get;private set;}
        public string Center="C-1";
        public string UserCenter="C-1";
        public string[] Scopes=new string[] {"mrsp_u","mrsp_p","mrsp_i","mrsp_nf","mrsp_rf","mrsp_dm"};
        public bool ExpireNextWrite,AbortAfterUpload,AbortAfterCreate;
        public int Uploads,Creates,CsrfRefreshes;
        public string UploadedModule,UploadedFilename,UploadedType;
        public byte[] FileBytes=new byte[0];
        public Dictionary<string,object> Saved;
        string csrf="csrf-1";
        public MockHLine()
        {
            TcpListener probe=new TcpListener(IPAddress.Loopback,0);probe.Start();int port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();
            BaseUrl="http://127.0.0.1:"+port+"/";listener.Prefixes.Add(BaseUrl);listener.Start();
            worker=new Thread(Loop);worker.IsBackground=true;worker.Start();
        }
        void Loop()
        {
            while(listener.IsListening)
            {
                HttpListenerContext c;
                try {c=listener.GetContext();} catch {return;}
                try {Handle(c);} catch {try {c.Response.StatusCode=500;c.Response.Close();} catch {}}
            }
        }
        void Handle(HttpListenerContext c)
        {
            string path=c.Request.Url.AbsolutePath;
            if(path=="/api/auth/csrf") {CsrfRefreshes++;Write(c,200,"{\"csrf\":\""+csrf+"\"}");return;}
            if(path=="/api/auth/login") {Write(c,200,"{\"data\":{\"data\":{\"user\":{\"id\":\"USER-1\"}}}}");return;}
            if(path=="/api/me") {StringBuilder s=new StringBuilder("{\"data\":{\"id\":\"USER-1\",\"medical_center_id\":\""+UserCenter+"\",\"scopes\":[");for(int i=0;i<Scopes.Length;i++){if(i>0)s.Append(',');s.Append('"').Append(Scopes[i]).Append('"');}s.Append("]}}");Write(c,200,s.ToString());return;}
            if(path=="/api/people") {Write(c,200,"{\"data\":[{\"id\":\"P-1\",\"person_id_no\":\"0000000001\"}]}");return;}
            if(path=="/api/people/P-1") {Write(c,200,"{\"data\":{\"id\":\"P-1\",\"person_id_no\":\"0000000001\",\"full_name\":\"Test Patient\",\"birth_date\":\"1980/01/01\",\"gender\":\"male\"}}");return;}
            if(path=="/api/medical-records") {Write(c,200,"{\"data\":["+Record()+"]}");return;}
            if(path=="/api/medical-records/R-1") {Write(c,200,"{\"data\":"+Record()+"}");return;}
            if(path=="/api/medical-records/R-1/spirometry")
            {
                if(c.Request.HttpMethod=="GET") {if(Saved==null) Write(c,404,"{\"data\":null}");else Write(c,200,"{\"data\":"+new JavaScriptSerializer().Serialize(Saved)+"}");return;}
                if(c.Request.HttpMethod=="POST" || c.Request.HttpMethod=="PATCH")
                {
                    if(!Csrf(c)) return;
                    if(c.Request.HttpMethod=="POST" && Saved!=null) {Write(c,422,"{\"errors\":[\"duplicate_record\"]}");return;}
                    string body=new StreamReader(c.Request.InputStream,Encoding.UTF8).ReadToEnd();
                    Saved=Json(body);Saved["file_id"]="F-1";
                    if(c.Request.HttpMethod=="POST") Creates++;
                    if(AbortAfterCreate && c.Request.HttpMethod=="POST") {c.Response.Abort();return;}
                    Write(c,c.Request.HttpMethod=="POST"?201:200,"{\"data\":{\"id\":\"S-1\"}}");return;
                }
            }
            if(path=="/api/medical-records/R-1/files" && c.Request.HttpMethod=="POST")
            {
                if(!Csrf(c)) return;
                byte[] body=ReadAll(c.Request.InputStream);string boundary=Boundary(c.Request.ContentType);
                ParseMultipart(body,boundary);
                Uploads++;
                if(AbortAfterUpload) {c.Response.Abort();return;}
                Write(c,201,"{\"data\":{\"id\":\"F-1\"}}");return;
            }
            if(path=="/api/medical-records/R-1/files/spirometry")
            {
                string list=FileBytes.Length==0?"{\"data\":[]}":"{\"data\":[{\"id\":\"F-1\",\"name\":\""+UploadedFilename+"\",\"size\":"+FileBytes.Length+"}]}";
                Write(c,200,list);return;
            }
            if(path=="/api/medical-records/R-1/files/F-1/dl")
            {
                c.Response.StatusCode=200;c.Response.ContentType="image/jpeg";c.Response.ContentLength64=FileBytes.Length;c.Response.OutputStream.Write(FileBytes,0,FileBytes.Length);c.Response.Close();return;
            }
            Write(c,404,"{}");
        }
        bool Csrf(HttpListenerContext c)
        {
            if(c.Request.Headers["X-CSRF-Token"]!=csrf || ExpireNextWrite)
            {
                ExpireNextWrite=false;csrf="csrf-"+(CsrfRefreshes+2);Write(c,419,"{}");return false;
            }
            return true;
        }
        string Record() {return "{\"id\":\"R-1\",\"person_id\":\"P-1\",\"person_id_no\":\"0000000001\",\"admission_date\":\"2026/11/01\",\"status\":\"complete\",\"medical_center_id\":\""+Center+"\"}";}
        static void Write(HttpListenerContext c,int status,string body)
        {
            byte[] data=Encoding.UTF8.GetBytes(body);c.Response.StatusCode=status;c.Response.ContentType="application/json; charset=utf-8";c.Response.ContentLength64=data.Length;c.Response.OutputStream.Write(data,0,data.Length);c.Response.Close();
        }
        static byte[] ReadAll(Stream s) {using(MemoryStream m=new MemoryStream()){byte[] b=new byte[8192];int n;while((n=s.Read(b,0,b.Length))>0)m.Write(b,0,n);return m.ToArray();}}
        static string Boundary(string type) {foreach(string part in (type??"").Split(';')){string p=part.Trim();if(p.StartsWith("boundary=",StringComparison.OrdinalIgnoreCase))return p.Substring(9).Trim('"');}throw new Exception("boundary missing");}
        void ParseMultipart(byte[] body,string boundary)
        {
            string text=Encoding.GetEncoding(28591).GetString(body);string marker="--"+boundary;int start=text.IndexOf(marker);
            while(start>=0)
            {
                int next=text.IndexOf(marker,start+marker.Length);if(next<0)break;
                string section=text.Substring(start+marker.Length,next-start-marker.Length);
                if(section.IndexOf("filename=\"",StringComparison.OrdinalIgnoreCase)>=0)
                {
                    int split=section.IndexOf("\r\n\r\n",StringComparison.Ordinal);
                    string headers=section.Substring(0,split),value=section.Substring(split+4);
                    UploadedFilename=Filename(headers);UploadedType=ContentType(headers);
                    int length=value.Length;if(value.EndsWith("\r\n",StringComparison.Ordinal))length-=2;
                    FileBytes=Encoding.GetEncoding(28591).GetBytes(value.Substring(0,length));break;
                }
                start=next;
            }
            UploadedModule=FieldValue(text,boundary,"module_id");
        }
        static string Filename(string headers) {int i=headers.IndexOf("filename=\"",StringComparison.OrdinalIgnoreCase);if(i<0)return "";i+=10;return headers.Substring(i,headers.IndexOf('"',i)-i);}
        static string ContentType(string headers) {foreach(string line in headers.Split(new string[] {"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Type:",StringComparison.OrdinalIgnoreCase))return line.Substring(13).Trim();return "";}
        static string FieldValue(string text,string boundary,string name)
        {
            string marker="--"+boundary;int start=text.IndexOf(marker);while(start>=0){int next=text.IndexOf(marker,start+marker.Length);if(next<0)break;string section=text.Substring(start+marker.Length,next-start-marker.Length);if(section.Contains("name=\""+name+"\"")){int split=section.IndexOf("\r\n\r\n",StringComparison.Ordinal);string value=section.Substring(split+4);if(value.EndsWith("\r\n",StringComparison.Ordinal))value=value.Substring(0,value.Length-2);return value;}start=next;}return "";
        }
        public void Dispose() {try {listener.Stop();listener.Close();} catch {}if(worker!=null)worker.Join(2000);}
    }
}

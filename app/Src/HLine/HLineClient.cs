using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SpiroLab.HLine
{
    public enum SessionStatus { Disconnected, Connected, Expired, AuthenticationError, Unavailable }
    public sealed class HLineException : Exception
    { public readonly string Code; public HLineException(string code,string message):base(message) { Code=code; } }
    public sealed class HLineResponse
    { public int Status; public string Body; }
    public interface IHLineTransport : IDisposable
    { HLineResponse Send(string method,string path,string body,string csrf); }
    // Implemented by transports that can replace their cookie jar with a session
    // adopted from the operator's browser (no password login involved).
    public interface IHLineCookieSink
    { int AdoptCookies(IEnumerable<SessionCookie> cookies); }
    // Clinical writes are a separate, explicitly named surface. The ordinary
    // IHLineTransport.Send guard remains read-only.
    internal interface IHLineWriteChannel
    {
        HLineResponse SendWriteJson(string method,string path,string body,string csrf);
        HLineResponse SendWriteMultipart(string path,string moduleId,byte[] jpeg,string filename,string description,string csrf);
        byte[] ReadFileBytes(string path,out int status);
    }
    public sealed class HLineHttpTransport : IHLineTransport, IHLineCookieSink, IHLineWriteChannel
    {
        readonly Uri origin;
        CookieContainer cookies=new CookieContainer();
        internal CookieContainer CookieJar {get {return cookies;} set {cookies=value;}}
        public HLineHttpTransport(string baseUrl)
        {
            origin=new Uri(baseUrl);
            if(origin.Scheme!="https" || !origin.IsDefaultPort || (origin.Host!="hline.ssu.ac.ir" && origin.Host!="app.samanesalamat.com") || origin.AbsolutePath!="/" || origin.UserInfo.Length>0 || origin.Query.Length>0) throw new ArgumentException("نشانی سامانه معتبر نیست.");
            ServicePointManager.SecurityProtocol=(SecurityProtocolType)3072;
        }
        internal HLineHttpTransport(Uri loopbackForTests)
        {if(!loopbackForTests.IsLoopback || loopbackForTests.Scheme!="http") throw new ArgumentException("Loopback tests only");origin=loopbackForTests;}
        public HLineResponse Send(string method,string path,string body,string csrf)
        {
            // Only authentication may mutate through this read-side transport.
            if((method!="GET" && !(method=="POST" && path=="/auth/login")) || !path.StartsWith("/") || path.StartsWith("//") || path.Contains(":") || path.Contains("\\")) throw new InvalidOperationException("عملیات شبکه مجاز نیست.");
            HttpWebRequest request=(HttpWebRequest)WebRequest.Create(new Uri(origin,"api"+path));
            request.Method=method; request.CookieContainer=cookies; request.AllowAutoRedirect=false; request.Timeout=15000; request.ReadWriteTimeout=15000;
            request.Accept="application/json"; request.Referer=origin.AbsoluteUri; request.Headers["Origin"]=origin.GetLeftPart(UriPartial.Authority); request.UserAgent="SpiroLab/5.3.1";
            // The site treats a consumed CSRF token as spent: send it only with
            // the mutating login POST, never on read-only GETs.
            if(method!="GET" && !string.IsNullOrEmpty(csrf)) request.Headers["X-CSRF-Token"]=csrf;
            try
            {
                if(body!=null) { byte[] bytes=Encoding.UTF8.GetBytes(body); request.ContentType="application/json"; request.ContentLength=bytes.Length; using(Stream s=request.GetRequestStream()) s.Write(bytes,0,bytes.Length); }
                try { using(HttpWebResponse r=(HttpWebResponse)request.GetResponse()) return Read(r); }
                catch(WebException e) { if(e.Response==null) throw new HLineException("network","عدم دسترسی به سامانه"); using(HttpWebResponse r=(HttpWebResponse)e.Response) return Read(r); }
            }
            catch(HLineException) { throw; }
            catch { throw new HLineException("network","عدم دسترسی به سامانه"); }
        }
        static readonly Regex JsonWritePath=new Regex(@"^/medical-records/[A-Za-z0-9_-]{1,100}/spirometry$",RegexOptions.CultureInvariant);
        static readonly Regex MultipartWritePath=new Regex(@"^/medical-records/[A-Za-z0-9_-]{1,100}/files$",RegexOptions.CultureInvariant);
        static readonly Regex FileReadPath=new Regex(@"^/medical-records/[A-Za-z0-9_-]{1,100}/files/[A-Za-z0-9_-]{1,100}/dl\?inline=1$",RegexOptions.CultureInvariant);
        static void RequireWrite(string method,string path,bool multipart)
        {
            if(!path.StartsWith("/") || path.StartsWith("//") || path.Contains("\\") || path.Contains(":")) throw new InvalidOperationException("مسیر نوشتن مجاز نیست.");
            bool ok=multipart?method=="POST"&&MultipartWritePath.IsMatch(path):(method=="POST"||method=="PATCH")&&JsonWritePath.IsMatch(path);
            if(!ok) throw new InvalidOperationException("عملیات نوشتن مجاز نیست.");
        }
        HLineResponse SendRaw(string method,string path,byte[] body,string csrf,string contentType,int timeout)
        {
            HttpWebRequest request=(HttpWebRequest)WebRequest.Create(new Uri(origin,"api"+path));
            request.Method=method; request.CookieContainer=cookies; request.AllowAutoRedirect=false; request.Timeout=timeout; request.ReadWriteTimeout=timeout;
            request.Accept="application/json"; request.Referer=origin.AbsoluteUri; request.Headers["Origin"]=origin.GetLeftPart(UriPartial.Authority); request.UserAgent="SpiroLab/5.3.1";
            request.ServicePoint.Expect100Continue=false;
            if(!string.IsNullOrEmpty(csrf)) request.Headers["X-CSRF-Token"]=csrf;
            if(body!=null) { request.ContentType=contentType; request.ContentLength=body.Length; using(Stream s=request.GetRequestStream()) s.Write(body,0,body.Length); }
            try
            {
                try { using(HttpWebResponse r=(HttpWebResponse)request.GetResponse()) return Read(r); }
                catch(WebException e) { if(e.Response==null) throw new HLineException("network","عدم دسترسی به سامانه"); using(HttpWebResponse r=(HttpWebResponse)e.Response) return Read(r); }
            }
            catch(HLineException) { throw; }
            catch { throw new HLineException("network","عدم دسترسی به سامانه"); }
        }
        public HLineResponse SendWriteJson(string method,string path,string body,string csrf)
        {
            RequireWrite(method,path,false);
            return SendRaw(method,path,body==null?null:Encoding.UTF8.GetBytes(body),csrf,"application/json; charset=utf-8",30000);
        }
        public HLineResponse SendWriteMultipart(string path,string moduleId,byte[] jpeg,string filename,string description,string csrf)
        {
            RequireWrite("POST",path,true);
            if(jpeg==null || jpeg.Length==0) throw new ArgumentException("فایل تصویر خالی است.");
            string boundary="----SpiroLab"+Guid.NewGuid().ToString("N");
            using(MemoryStream payload=new MemoryStream())
            {
                WritePart(payload,"--"+boundary+"\r\nContent-Disposition: form-data; name=\"module_id\"\r\n\r\n"+moduleId+"\r\n");
                WritePart(payload,"--"+boundary+"\r\nContent-Disposition: form-data; name=\"medical_record_file\"; filename=\""+SafeFilename(filename)+"\"\r\nContent-Type: image/jpeg\r\n\r\n");
                payload.Write(jpeg,0,jpeg.Length); WritePart(payload,"\r\n");
                if(!string.IsNullOrEmpty(description)) WritePart(payload,"--"+boundary+"\r\nContent-Disposition: form-data; name=\"desc\"\r\n\r\n"+description+"\r\n");
                WritePart(payload,"--"+boundary+"--\r\n");
                return SendRaw("POST",path,payload.ToArray(),csrf,"multipart/form-data; boundary="+boundary,60000);
            }
        }
        public byte[] ReadFileBytes(string path,out int status)
        {
            if(!FileReadPath.IsMatch(path)) throw new InvalidOperationException("مسیر دریافت فایل مجاز نیست.");
            HttpWebRequest request=(HttpWebRequest)WebRequest.Create(new Uri(origin,"api"+path));
            request.Method="GET"; request.CookieContainer=cookies; request.AllowAutoRedirect=false; request.Timeout=30000; request.ReadWriteTimeout=30000;
            request.Accept="*/*"; request.Referer=origin.AbsoluteUri; request.UserAgent="SpiroLab/5.3.1";
            try
            {
                try { using(HttpWebResponse r=(HttpWebResponse)request.GetResponse()) return ReadBytes(r,out status); }
                catch(WebException e) { if(e.Response==null) throw new HLineException("network","عدم دسترسی به سامانه"); using(HttpWebResponse r=(HttpWebResponse)e.Response) return ReadBytes(r,out status); }
            }
            catch(HLineException) { throw; }
            catch { throw new HLineException("network","عدم دسترسی به سامانه"); }
        }
        static byte[] ReadBytes(HttpWebResponse response,out int status)
        {
            status=(int)response.StatusCode;
            using(Stream s=response.GetResponseStream()) using(MemoryStream b=new MemoryStream())
            {
                byte[] chunk=new byte[8192]; int n;
                while((n=s.Read(chunk,0,chunk.Length))>0) { if(b.Length+n>6*1024*1024) throw new HLineException("response_size","فایل پاسخ بیش از حد بزرگ است."); b.Write(chunk,0,n); }
                return b.ToArray();
            }
        }
        static void WritePart(Stream target,string text) { byte[] data=Encoding.UTF8.GetBytes(text); target.Write(data,0,data.Length); }
        static string SafeFilename(string name)
        {
            string value=string.IsNullOrEmpty(name)?"Spirometry-Report.jpg":Path.GetFileName(name);
            value=value.Replace("\"","").Replace("\\","").Replace("\r","").Replace("\n","");
            if(!value.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)) value="Spirometry-Report.jpg";
            return value;
        }
        static HLineResponse Read(HttpWebResponse response)
        {
            using(Stream s=response.GetResponseStream()) using(MemoryStream b=new MemoryStream())
            {
                byte[] chunk=new byte[8192]; int n;
                while((n=s.Read(chunk,0,chunk.Length))>0) { if(b.Length+n>2*1024*1024) throw new HLineException("response_size","پاسخ سامانه بیش از حد بزرگ است."); b.Write(chunk,0,n); }
                return new HLineResponse {Status=(int)response.StatusCode,Body=Encoding.UTF8.GetString(b.ToArray())};
            }
        }
        public int AdoptCookies(IEnumerable<SessionCookie> source)
        {
            if(source==null) throw new ArgumentNullException("source");
            CookieContainer adopted=new CookieContainer(); int count=0;
            foreach(SessionCookie item in source)
            {
                if(item==null || string.IsNullOrEmpty(item.Name) || !AllowedDomain(item.Domain)) continue;
                bool hostOnly=!item.Domain.TrimStart().StartsWith(".");
                if(hostOnly && item.Value.IndexOf(';')<0 && item.Value.IndexOf('\r')<0 && item.Value.IndexOf('\n')<0)
                {
                    // Host-only cookies must be added exactly as Firefox stored them,
                    // otherwise the rotated cookie of the next response would live
                    // beside a stale copy and both would be sent.
                    StringBuilder header=new StringBuilder(item.Name).Append('=').Append(item.Value);
                    header.Append("; Path=").Append(string.IsNullOrEmpty(item.Path)?"/":item.Path);
                    if(item.Secure) header.Append("; Secure");
                    if(item.HttpOnly) header.Append("; HttpOnly");
                    if(item.ExpiresUtc!=DateTime.MinValue) header.Append("; Expires=").Append(item.ExpiresUtc.ToString("R",CultureInfo.InvariantCulture));
                    try { adopted.SetCookies(origin,header.ToString()); count++; } catch(CookieException) { }
                }
                else
                {
                    Cookie cookie=new Cookie(item.Name,item.Value,string.IsNullOrEmpty(item.Path)?"/":item.Path,item.Domain);
                    cookie.Secure=item.Secure; cookie.HttpOnly=item.HttpOnly;
                    if(item.ExpiresUtc!=DateTime.MinValue) cookie.Expires=item.ExpiresUtc;
                    try { adopted.Add(cookie); count++; } catch(CookieException) { }
                }
            }
            if(count==0) throw new HLineException("cookies","کوکی نشست اچ‌لاین برای این سامانه پیدا نشد.");
            cookies=adopted; return count;
        }
        // Only the site itself or one of its parent domains may supply cookies.
        bool AllowedDomain(string domain)
        {
            string source=(domain??"").Trim().TrimStart('.').ToLowerInvariant();
            string host=origin.Host.ToLowerInvariant();
            if(source.Length==0) return false;
            if(source==host) return true;
            return host.EndsWith("."+source,StringComparison.Ordinal);
        }
        public void Dispose() { cookies=new CookieContainer(); }
    }
    public sealed class MedicalRecord
    {
        public string Id="",PersonId="",NationalId="",DateText="",Status="";
        public DateTime? Date;
        public override string ToString() { return Id+" | "+DateText+" | "+Status; }
    }
    public sealed class HLinePerson
    {
        public Patient Patient;
        public List<MedicalRecord> Records=new List<MedicalRecord>();
    }
    public sealed class HLineClient : IDisposable
    {
        readonly object sessionLock=new object();
        readonly Func<IList<FirefoxSession>> firefoxCandidates;
        bool recovering;
        DateTime nextRecoveryUtc=DateTime.MinValue;
        public bool IdentityChangePending {get;private set;}
        readonly IHLineTransport transport;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=2*1024*1024};
        string csrf,userId,centerId;
        readonly HashSet<string> scopes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public SessionStatus Status {get;private set;}
        public string SessionOrigin {get;private set;}   // "firefox" | "password" (never a secret)
        public string FirefoxProfileName {get;private set;}
        public string PreferredFirefoxProfile {get;set;} // optional one-time setting; empty = automatic
        public HLineClient():this(new HLineHttpTransport("https://hline.ssu.ac.ir/")) { }
        public HLineClient(IHLineTransport transport):this(transport,null) { }
        public HLineClient(IHLineTransport transport,Func<IList<FirefoxSession>> candidates) { this.transport=transport;firefoxCandidates=candidates; }
        public string StatusText {get { switch(Status) {case SessionStatus.Connected:return "متصل به سامانه";case SessionStatus.Expired:return "نیاز به ورود در فایرفاکس";case SessionStatus.AuthenticationError:return IdentityChangePending?"تغییر هویت یا مرکز؛ تأیید لازم است":"خطای احراز هویت";case SessionStatus.Unavailable:return "عدم دسترسی موقت؛ نشست حذف نشده است";default:return "نیاز به ورود در فایرفاکس";} } }
        internal bool CanWrite {get {return transport is IHLineWriteChannel;}}
        internal string CurrentUserId {get {return userId??"";}}
        internal string CurrentMedicalCenterId {get {return centerId??"";}}
        internal bool HasScope(string code)
        {
            if(scopes.Count==0) VerifySession();
            return !string.IsNullOrEmpty(code) && scopes.Contains(code);
        }
        void CaptureMe(Dictionary<string,object> me)
        {
            Dictionary<string,object> data=OptionalDict(me,"data"), user=OptionalDict(data,"user");
            string id=Text(user,"id"); if(id.Length==0) id=Text(data,"id"); if(id.Length==0) id=Text(me,"id");
            if(id.Length==0) id=Text(OptionalDict(me,"user"),"id");
            userId=id;
            string center=Text(data,"medical_center_id"); if(center.Length==0) center=Text(user,"medical_center_id"); if(center.Length==0) center=Text(me,"medical_center_id");
            if(center.Length==0) center=Text(OptionalDict(me,"user"),"medical_center_id");
            centerId=center;
            scopes.Clear();
            CaptureScopes(data); CaptureScopes(user); CaptureScopes(me);
        }
        void CaptureScopes(Dictionary<string,object> source)
        {
            object raw; if(!source.TryGetValue("scopes",out raw) || !(raw is object[])) return;
            foreach(object item in (object[])raw) { string code=Convert.ToString(item,CultureInfo.InvariantCulture); if(!string.IsNullOrEmpty(code)) scopes.Add(code); }
        }
        internal string EnsureCsrf()
        { lock(sessionLock) {
            if(IdentityChangePending) throw new HLineException("identity_changed","تغییر هویت یا مرکز نشست هنوز تأیید نشده است؛ ارسال مجاز نیست.");
            if(!string.IsNullOrEmpty(csrf)) return csrf;
            csrf=Text(Dict(Request("GET","/auth/csrf",null)),"csrf");
            if(string.IsNullOrEmpty(csrf)) throw new HLineException("csrf","پاسخ CSRF معتبر نیست.");
            return csrf;
        } }
        internal HLineResponse WriteJson(string method,string path,object body)
        { lock(sessionLock) { try {
            IHLineWriteChannel channel=transport as IHLineWriteChannel;
            if(channel==null) throw new HLineException("readonly","این مسیر ارتباطی از ارسال پشتیبانی نمی‌کند.");
            string token=EnsureCsrf(), payload=json.Serialize(body);
            HLineResponse response=channel.SendWriteJson(method,path,payload,token);
            if(response.Status==419) { csrf=null; token=EnsureCsrf(); response=channel.SendWriteJson(method,path,payload,token); }
            if(response.Status==401) { Status=SessionStatus.Expired; throw new HLineException("expired","نشست منقضی شده؛ دوباره وارد شوید."); }
            if(response.Status==403) throw new HLineException("forbidden","مجوز ثبت اسپیرومتری برای این حساب وجود ندارد.");
            if(response.Status==419) {Status=SessionStatus.Expired;throw new HLineException("expired","نشست یا CSRF تأیید نشد؛ نوشتن دیگری تکرار نشد.");}
            return response;
        } catch(HLineException ex){if(ex.Code=="network")Status=SessionStatus.Unavailable;throw;} } }
        internal HLineResponse WriteFile(string recordId,byte[] jpeg,string filename,string description)
        { lock(sessionLock) { try {
            IHLineWriteChannel channel=transport as IHLineWriteChannel;
            if(channel==null) throw new HLineException("readonly","این مسیر ارتباطی از ارسال پشتیبانی نمی‌کند.");
            string path="/medical-records/"+Segment(recordId)+"/files", token=EnsureCsrf();
            HLineResponse response=channel.SendWriteMultipart(path,"spirometry",jpeg,filename,description,token);
            if(response.Status==419) { csrf=null; token=EnsureCsrf(); response=channel.SendWriteMultipart(path,"spirometry",jpeg,filename,description,token); }
            if(response.Status==401) { Status=SessionStatus.Expired; throw new HLineException("expired","نشست منقضی شده؛ دوباره وارد شوید."); }
            if(response.Status==403) throw new HLineException("forbidden","مجوز بارگذاری فایل برای این حساب وجود ندارد.");
            if(response.Status==419) {Status=SessionStatus.Expired;throw new HLineException("expired","نشست یا CSRF تأیید نشد؛ بارگذاری دیگری تکرار نشد.");}
            return response;
        } catch(HLineException ex){if(ex.Code=="network")Status=SessionStatus.Unavailable;throw;} } }
        internal Dictionary<string,object> RecordDetail(string recordId) { return DataObject(Request("GET","/medical-records/"+Segment(recordId),null)); }
        internal Dictionary<string,object> FindSpirometry(string recordId)
        {
            try
            {
                object value=Request("GET","/medical-records/"+Segment(recordId)+"/spirometry",null);
                Dictionary<string,object> envelope=Dict(value);object nested;
                if(!envelope.TryGetValue("data",out nested)) throw new HLineException("malformed","وضعیت نتیجهٔ اسپیرومتری در پاسخ سامانه مشخص نیست.");
                if(nested==null) return null;
                Dictionary<string,object> data=Dict(nested);
                return data.Count==0?null:data;
            }
            catch(HLineException ex) { if(ex.Code=="not_found") return null; throw; }
        }
        internal List<Dictionary<string,object>> Files(string recordId)
        { return Rows(Request("GET","/medical-records/"+Segment(recordId)+"/files/spirometry",null)); }
        // The record screen exposes module state; a successful payload must not leave
        // the spirometry module in NoData. Returns "" when the module row is absent.
        internal string SpirometryModuleStatus(string recordId)
        {
            Dictionary<string,object> record=RecordDetail(recordId);
            object modules;
            if(!record.TryGetValue("modules",out modules) || modules==null) return "";
            System.Collections.IEnumerable rows=modules as System.Collections.IEnumerable;
            if(rows==null || modules is string) return "";
            foreach(object row in rows)
            {
                Dictionary<string,object> item=row as Dictionary<string,object>;
                if(item!=null && Text(item,"module_id")=="spirometry") return Text(item,"status");
            }
            return "";
        }
        internal byte[] FileBytes(string recordId,string fileId,out int status)
        { lock(sessionLock) {
            RequireId(fileId);
            IHLineWriteChannel channel=transport as IHLineWriteChannel;
            if(channel==null) throw new HLineException("readonly","این مسیر ارتباطی از دریافت فایل پشتیبانی نمی‌کند.");
            byte[] data=channel.ReadFileBytes("/medical-records/"+Segment(recordId)+"/files/"+Segment(fileId)+"/dl?inline=1",out status);
            if(status==401) { Status=SessionStatus.Expired; throw new HLineException("expired","نشست منقضی شده؛ دوباره وارد شوید."); }
            return data;
        } }
        internal bool VerifySubmissionDestination(SubmissionDraft draft) { return VerifySubmission(draft,true); }
        // The designated test record may be historical. Its exact identity is still
        // verified, but the latest-record rule is relaxed only for that opt-in scope.
        internal bool VerifySubmissionRecord(SubmissionDraft draft) { return VerifySubmission(draft,false); }
        bool VerifySubmission(SubmissionDraft draft,bool requireLatest)
        { lock(sessionLock) {
            VerifySession();
            if(requireLatest) VerifyLatestRecord(draft.Snapshot.Data.Patient);
            else VerifyRecord(new MedicalRecord {Id=draft.RecordId,PersonId=draft.PersonId,NationalId=draft.NationalId,DateText=draft.RecordDate,Status=draft.RecordStatus},draft.PersonId,draft.NationalId);
            Dictionary<string,object> record=RecordDetail(draft.RecordId);
            if(Text(record,"id")!=draft.RecordId || Text(record,"person_id")!=draft.PersonId || Fmt.NormalizeNumber(Text(record,"person_id_no"))!=draft.NationalId || Text(record,"admission_date")!=draft.RecordDate || Text(record,"status")!=draft.RecordStatus) throw new HLineException("destination_changed","مشخصات پرونده در سامانه تغییر کرده است؛ ارسال متوقف شد.");
            string recordCenter=Text(record,"medical_center_id");
            if(!string.IsNullOrEmpty(recordCenter) && !string.IsNullOrEmpty(centerId) && recordCenter!=centerId) throw new HLineException("center","پرونده به مرکز درمانی این حساب تعلق ندارد.");
            if(!HasScope("mrsp_u") || !HasScope("mrsp_p") || !HasScope("mrsp_nf")) throw new HLineException("forbidden","مجوز ثبت نتیجه، انجام‌دهنده یا فایل اسپیرومتری برای این حساب کامل نیست.");
            return true;
        } }
        object Request(string method,string path,object body)
        {
            lock(sessionLock)
            {
                try {return RequestCore(method,path,body);}
                catch(HLineException ex)
                {
                    if(ex.Code!="expired" || method!="GET" || recovering || string.IsNullOrEmpty(userId) || !(transport is IHLineCookieSink) || DateTime.UtcNow<nextRecoveryUtc) throw;
                    nextRecoveryUtc=DateTime.UtcNow.AddSeconds(30);recovering=true;
                    try
                    {
                        string problem;
                        if(TryAdoptFirefoxSessionCore(PreferredFirefoxProfile,out problem,false))
                        {
                            object result=RequestCore(method,path,body);
                            // Back off failed recovery, not a later independent expiry.
                            nextRecoveryUtc=DateTime.MinValue;return result;
                        }
                        if(IdentityChangePending) throw new HLineException("identity_changed","هویت کاربر یا مرکز نشست فایرفاکس متفاوت است؛ تأیید کارشناس برای تغییر نشست لازم است.");
                        throw;
                    }
                    finally {recovering=false;}
                }
            }
        }
        object RequestCore(string method,string path,object body)
        {
            HLineResponse r;
            try { r=transport.Send(method,path,body==null?null:json.Serialize(body),csrf); }
            catch { Status=SessionStatus.Unavailable; throw new HLineException("network","عدم دسترسی به سامانه"); }
            if((r.Status==401 && path!="/auth/login") || r.Status==419 || (r.Status>=300 && r.Status<400)) { Status=SessionStatus.Expired; throw new HLineException("expired","نشست منقضی شده؛ دوباره وارد شوید."); }
            if(r.Status==403) throw new HLineException("forbidden","مجوز دسترسی به این بخش وجود ندارد.");
            if(r.Status==404) throw new HLineException("not_found","مورد درخواستی در سامانه پیدا نشد.");
            if((r.Status<200 || r.Status>=300) && !(path=="/auth/login" && (r.Status==400 || r.Status==401 || r.Status==422))) throw new HLineException("server","پاسخ ناموفق سامانه؛ اطلاعات محلی حفظ شد.");
            try { return json.DeserializeObject(r.Body); } catch { throw new HLineException("malformed","ساختار پاسخ سامانه معتبر نیست."); }
        }
        public void Login(string username,string password,string mfa)
        { lock(sessionLock) {
            IdentityChangePending=false;
            Status=SessionStatus.Disconnected; csrf=userId=centerId=null;scopes.Clear(); SessionOrigin=null; FirefoxProfileName=null;
            if(string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new HLineException("credentials","نام کاربری و رمز عبور را وارد کنید.");
            try
            {
                csrf=Text(Dict(Request("GET","/auth/csrf",null)),"csrf");
                if(string.IsNullOrEmpty(csrf)) throw new HLineException("csrf","پاسخ CSRF معتبر نیست.");
                Dictionary<string,object> response=Dict(Request("POST","/auth/login",new {username=username.Trim(),password=password,mfa_token=Fmt.NormalizeNumber(mfa),mfa_token_method=1}));
                Dictionary<string,object> data=OptionalDict(response,"data"), inner=OptionalDict(data,"data");
                if(OptionalDict(data,"user").Count==0 && OptionalDict(inner,"user").Count==0)
                {
                    string code="authentication"; object errors; if(response.TryGetValue("errors",out errors) && errors is object[] && ((object[])errors).Length>0) code=Convert.ToString(((object[])errors)[0]);
                    throw new HLineException(code,code=="missing_mfa_token"?"کد تأیید دومرحله‌ای را وارد کنید.":code=="invalid_mfa_token"?"کد دومرحله‌ای اشتباه یا منقضی شده است.":code=="needs_mfa_setup"?"راه‌اندازی احراز هویت دومرحله‌ای را در سامانه تکمیل کنید.":"خطای احراز هویت");
                }
                userId=Text(OptionalDict(data,"user"),"id");if(userId.Length==0) userId=Text(OptionalDict(inner,"user"),"id");
                if(userId.Length==0) throw new HLineException("authentication","هویت کاربر در پاسخ ورود معتبر نیست.");
                csrf=Text(Dict(Request("GET","/auth/csrf",null)),"csrf");
                if(csrf.Length==0) throw new HLineException("csrf","پاسخ CSRF معتبر نیست.");
                Status=SessionStatus.Connected; SessionOrigin="password"; VerifySession();
            }
            catch(HLineException e) { if(e.Code!="network") Status=SessionStatus.AuthenticationError; throw; }
        } }
        public void VerifySession()
        { lock(sessionLock) {
            if(IdentityChangePending) throw new HLineException("identity_changed","هویت کاربر یا مرکز نشست متفاوت است؛ تأیید کارشناس لازم است.");
            if(string.IsNullOrEmpty(userId) || (Status!=SessionStatus.Connected && Status!=SessionStatus.Unavailable && Status!=SessionStatus.Expired)) throw new HLineException("session","ابتدا وارد سامانه شوید.");
            string expectedCenter=centerId;
            Dictionary<string,object> me=Dict(Request("GET","/me",null)), data=OptionalDict(me,"data");
            string actual=Text(OptionalDict(me,"user"),"id");if(actual.Length==0) actual=Text(OptionalDict(data,"user"),"id");if(actual.Length==0) actual=Text(data,"id");
            if(actual.Length==0) {Status=SessionStatus.Expired;throw new HLineException("expired","هویت نشست تأیید نشد؛ دوباره وارد شوید.");}
            if(actual!=userId) {IdentityChangePending=true;Status=SessionStatus.AuthenticationError;throw new HLineException("identity_changed","هویت کاربر نشست تغییر کرده است؛ تأیید کارشناس لازم است.");}
            CaptureMe(me);
            if(expectedCenter!=null && centerId!=expectedCenter) {centerId=expectedCenter;IdentityChangePending=true;Status=SessionStatus.AuthenticationError;throw new HLineException("identity_changed","مرکز درمانی نشست تغییر کرده است؛ تأیید کارشناس لازم است.");}
            Status=SessionStatus.Connected;
        } }
        // Replaces the HTTP cookie jar with the browser session and validates it
        // against /me. A valid Firefox session means no password/MFA prompt.
        public bool AdoptCookies(IEnumerable<SessionCookie> session)
        {lock(sessionLock) {return AdoptCookies(session,false);}}
        bool AdoptCookies(IEnumerable<SessionCookie> session,bool allowIdentityChange)
        {
            IHLineCookieSink sink=transport as IHLineCookieSink;
            if(sink==null) throw new HLineException("cookies","این مسیر ارتباطی از نشست فایرفاکس پشتیبانی نمی‌کند.");
            string oldUser=userId,oldCenter=centerId,oldCsrf=csrf;
            string[] oldScopes=new List<string>(scopes).ToArray();
            HLineHttpTransport http=transport as HLineHttpTransport;
            CookieContainer oldJar=http==null?null:http.CookieJar;
            sink.AdoptCookies(session);csrf=null;
            try
            {
                // A read-only session needs no CSRF token; fetching one would only
                // rotate the site's anti-forgery cookies for nothing.
                Dictionary<string,object> me=Dict(Request("GET","/me",null)),data=OptionalDict(me,"data");
                CaptureMe(me);
                if(!allowIdentityChange && !string.IsNullOrEmpty(oldUser) && (userId!=oldUser || centerId!=(oldCenter??"")))
                {IdentityChangePending=true;throw new HLineException("identity_changed","هویت کاربر یا مرکز نشست فایرفاکس متفاوت است؛ تأیید کارشناس لازم است.");}
                string actual=userId??"";
                if(actual.Length==0) actual=Text(OptionalDict(me,"user"),"id");
                if(actual.Length==0) actual=Text(OptionalDict(data,"user"),"id");
                if(actual.Length==0) actual=Text(data,"id");
                if(actual.Length==0) throw new HLineException("expired","نشست فایرفاکس معتبر نیست؛ دوباره در فایرفاکس وارد سامانه شوید.");
                userId=actual;IdentityChangePending=false; Status=SessionStatus.Connected; return true;
            }
            catch(HLineException ex)
            {
                if(http!=null) http.CookieJar=oldJar;
                csrf=oldCsrf;userId=oldUser;centerId=oldCenter;scopes.Clear();foreach(string scope in oldScopes) scopes.Add(scope);
                Status=ex.Code=="network"?SessionStatus.Unavailable:IdentityChangePending?SessionStatus.AuthenticationError:SessionStatus.Expired;throw;
            }
        }
        // Tries every Firefox profile holding HLine cookies, newest first, and
        // stops at the first one the server accepts. Never logs cookie values.
        public bool TryAdoptFirefoxSession(string preferredProfile,out string problem)
        {return TryAdoptFirefoxSession(preferredProfile,out problem,false);}
        public bool TryAdoptFirefoxSession(string preferredProfile,out string problem,bool allowIdentityChange)
        {
            lock(sessionLock)
            {
                bool before=recovering;recovering=true;
                try {return TryAdoptFirefoxSessionCore(preferredProfile,out problem,allowIdentityChange);}
                finally {recovering=before;}
            }
        }
        bool TryAdoptFirefoxSessionCore(string preferredProfile,out string problem,bool allowIdentityChange)
        {
            problem=null;
            if(!allowIdentityChange && !string.IsNullOrEmpty(userId) && (Status==SessionStatus.Connected || Status==SessionStatus.Unavailable))
            {
                try {VerifySession();return true;}
                catch(HLineException ex) {if(ex.Code!="expired" && ex.Code!="session") {problem=ex.Message;return false;}}
            }
            if(!(transport is IHLineCookieSink)) { problem="این مسیر ارتباطی از نشست فایرفاکس پشتیبانی نمی‌کند."; return false; }
            if(string.IsNullOrEmpty(preferredProfile)) preferredProfile=PreferredFirefoxProfile;
            string readProblem;
            readProblem=null;
            IList<FirefoxSession> candidates=firefoxCandidates==null?FirefoxSessions.Candidates(FirefoxSessions.HLineDomain,preferredProfile,out readProblem):firefoxCandidates();
            if(candidates.Count==0) { problem=readProblem; return false; }
            int attempts=0;
            foreach(FirefoxSession candidate in candidates)
            {
                if(++attempts>3) break;
                try
                {
                    AdoptCookies(candidate.Cookies,allowIdentityChange);
                    FirefoxProfileName=candidate.Profile.Name; SessionOrigin="firefox";
                    return true;
                }
                catch(HLineException exception) {problem=exception.Message;if(exception.Code=="network" || exception.Code=="identity_changed" || exception.Code=="forbidden") return false;}
                catch { problem="اتصال به سامانه با نشست فایرفاکس انجام نشد."; }
            }
            Status=SessionStatus.Expired;
            if(problem==null) problem="نشست فایرفاکس معتبر نیست؛ دوباره در فایرفاکس وارد سامانه شوید.";
            return false;
        }
        public bool EnsureSession(out string problem)
        {
            lock(sessionLock)
            {
                if(!string.IsNullOrEmpty(userId))
                {
                    try {VerifySession();problem=null;return true;}
                    catch(HLineException ex) {problem=ex.Message;return false;}
                }
                return TryAdoptFirefoxSession(PreferredFirefoxProfile,out problem);
            }
        }
        public HLinePerson Lookup(string nationalId)
        { lock(sessionLock) {
            nationalId=Fmt.NormalizeNumber(nationalId); if(!Regex.IsMatch(nationalId,"^[0-9]{10}$")) throw new HLineException("national_id","کد ملی ده‌رقمی را وارد کنید.");
            VerifySession();
            List<Dictionary<string,object>> rows=Rows(Request("GET","/people?search="+nationalId+"&page_size=25&page_no=1",null));
            List<Dictionary<string,object>> matches=rows.FindAll(delegate(Dictionary<string,object> p) {return Fmt.NormalizeNumber(Text(p,"person_id_no"))==nationalId;});
            if(matches.Count==0) throw new HLineException("not_found","مراجعی با این کد ملی پیدا نشد.");
            if(matches.Count>1) throw new HLineException("duplicate_person","چند شخص با همین کد ملی یافت شد؛ شناسهٔ صحیح باید در سامانه رفع ابهام شود.");
            if(rows.Count>=25 || FullCount(rows)>25) throw new HLineException("pagination","نتایج جستجو کامل نیست؛ رفع ابهام در سامانه لازم است.");
            string pid=Text(matches[0],"id"); RequireId(pid);
            Dictionary<string,object> detail=DataObject(Request("GET","/people/"+Segment(pid),null));
            if(Text(detail,"id")!=pid || Fmt.NormalizeNumber(Text(detail,"person_id_no"))!=nationalId) throw new HLineException("identity","هویت جزئیات شخص با جستجو مطابقت ندارد.");
            Patient patient=new Patient {NationalId=nationalId,FirstName=Text(detail,"first_name"),LastName=Text(detail,"last_name"),ServerFullName=Text(detail,"full_name"),HLinePersonId=pid,HeightCm=double.NaN,WeightKg=double.NaN,Origin=Origin.Caucasian,DemographicSource="hline_person"};
            DateTime birth; if(TryDate(Text(detail,"birth_date"),out birth)) patient.BirthDate=birth;
            string gender=Text(detail,"gender").Trim().ToLowerInvariant();
            if(Array.IndexOf(new string[] {"male","m","مرد","مذکر"},gender)>=0) patient.Sex=Sex.Male;
            else if(Array.IndexOf(new string[] {"female","f","زن","مونث","خانم"},gender)>=0) patient.Sex=Sex.Female;
            else patient.SexUnconfirmed=true;
            HLinePerson result=new HLinePerson {Patient=patient};
            rows=Rows(Request("GET","/medical-records?person_id_no="+nationalId+"&person_id_no_exact=true&page_size=200&page_no=1",null));
            if(rows.Count>=200 || FullCount(rows)>200) throw new HLineException("pagination","فهرست پرونده‌ها کامل نیست؛ انتخاب خودکار مجاز نیست.");
            HashSet<string> ids=new HashSet<string>();
            foreach(Dictionary<string,object> row in rows)
            {
                MedicalRecord record=ParseRecord(row,pid,nationalId);
                if(!ids.Add(record.Id)) throw new HLineException("records","پروندهٔ تکراری در پاسخ سامانه.");
                result.Records.Add(record);
            }
            return result;
        } }
        public static MedicalRecord LatestUnambiguous(IList<MedicalRecord> records)
        {
            if(records.Count==0) return null;
            MedicalRecord latest=null;
            foreach(MedicalRecord r in records)
            {
                if(!r.Date.HasValue) return null;
                if(latest==null || r.Date>latest.Date) latest=r;
            }
            int ties=0; foreach(MedicalRecord r in records) if(r.Date==latest.Date) ties++;
            return ties==1?latest:null;
        }
        public MedicalRecord VerifyRecord(MedicalRecord selected,string pid,string nid)
        {
            RequireId(selected.Id);
            MedicalRecord actual=ParseRecord(DataObject(Request("GET","/medical-records/"+Segment(selected.Id),null)),pid,nid);
            if(actual.Id!=selected.Id || actual.DateText!=selected.DateText || actual.Status!=selected.Status) throw new HLineException("destination_changed","مشخصات پرونده تغییر کرده است؛ مقصد را دوباره انتخاب کنید.");
            return actual;
        }
        public Patient LoadLatestPatient(string nationalId)
        { lock(sessionLock) {
            HLinePerson person=Lookup(nationalId);
            MedicalRecord latest=LatestUnambiguous(person.Records);
            if(latest==null) throw new HLineException("latest_record","آخرین پروندهٔ پزشکی با تاریخ مشخص و یکتا یافت نشد.");
            return PopulateRecord(person,latest);
        } }
        public void VerifyLatestRecord(Patient patient)
        { lock(sessionLock) {
            HLinePerson person=Lookup(patient.NationalId);
            MedicalRecord latest=LatestUnambiguous(person.Records);
            if(latest==null) throw new HLineException("latest_record","آخرین پروندهٔ پزشکی مشخص نیست؛ ارسال انجام نشد.");
            if(person.Patient.HLinePersonId!=patient.HLinePersonId || latest.Id!=patient.HLineRecordId || latest.DateText!=patient.HLineRecordDate || latest.Status!=patient.HLineRecordStatus) throw new HLineException("destination_changed","برای این مراجعه، آخرین پروندهٔ تأییدشدهٔ HLine انتخاب نشده است؛ مشخصات را دوباره از سایت دریافت کنید. مقصد بی‌صدا تغییر نکرد.");
            VerifyRecord(latest,patient.HLinePersonId,patient.NationalId);
        } }
        public Patient PopulateRecord(HLinePerson person,MedicalRecord record)
        { lock(sessionLock) {
            VerifyRecord(record,person.Patient.HLinePersonId,person.Patient.NationalId);
            Patient p=person.Patient.Clone(); p.HLineRecordId=record.Id; p.HLineRecordDate=record.DateText; p.HLineRecordStatus=record.Status;
            object history=Request("GET","/medical-records/"+Segment(record.Id)+"/vitals/history",null);
            var envelope=history as Dictionary<string,object>;object data;
            List<Dictionary<string,object>> vitals=envelope!=null && envelope.TryGetValue("data",out data) && data==null?new List<Dictionary<string,object>>():Rows(history);
            var same=vitals.FindAll(delegate(Dictionary<string,object> r) {return Text(r,"medical_record_id")==record.Id;});
            Dictionary<string,object> selected=same.Count==1?same[0]:null;
            if(same.Count>1)
            {
                DateTimeOffset latest=DateTimeOffset.MinValue;bool ambiguous=false;
                foreach(var row in same)
                {
                    DateTimeOffset date;
                    if(!VitalDate(row,out date)) {selected=null;ambiguous=true;break;}
                    if(date>latest) {latest=date;selected=row;ambiguous=false;}
                    else if(date==latest) ambiguous=true;
                }
                if(ambiguous) selected=null;
            }
            // Keep the latest row together; never backfill one missing measurement from an older row.
            if(selected!=null)
            {
                double height=Number(selected,"height"),weight=Number(selected,"weight");
                p.HeightCm=height>=100 && height<=230?height:double.NaN;
                p.WeightKg=weight>=10 && weight<=300?weight:double.NaN;
                p.AnthropometrySource="hline_latest_row_in_latest_record";
            }
            else p.AnthropometrySource="missing_or_ambiguous";
            return p;
        } }
        static bool VitalDate(Dictionary<string,object> row,out DateTimeOffset date)
        {
            string text=Text(row,"test_date");if(text.Length==0) text=Text(row,"created_at");
            DateTime day;if(TryDate(text,out day)) {date=new DateTimeOffset(day,TimeSpan.Zero);return true;}
            return DateTimeOffset.TryParseExact(text,new string[] {"yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK","yyyy-MM-dd'T'HH:mm:ssK","yyyy-MM-dd HH:mm:ss","yyyy/MM/dd HH:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date) && date.Year>=1900 && date.Year<=2200;
        }
        public bool HasSpirometry(MedicalRecord record)
        { lock(sessionLock) {
            List<Dictionary<string,object>> rows=Rows(Request("GET","/medical-records/"+Segment(record.Id)+"/spirometry/history",null));
            foreach(var row in rows) { if(Text(row,"medical_record_id").Length==0) throw new HLineException("history","انتساب نتیجهٔ قبلی به پرونده نامشخص است."); if(Text(row,"medical_record_id")==record.Id) return true; }
            return false;
        } }
        static MedicalRecord ParseRecord(Dictionary<string,object> row,string pid,string nid)
        {
            if(Text(row,"person_id")!=pid || Fmt.NormalizeNumber(Text(row,"person_id_no"))!=nid) throw new HLineException("record_identity","انتساب پرونده به شخص و کد ملی تأیید نشد.");
            string id=Text(row,"id"); RequireId(id); string date=Text(row,"admission_date"); DateTime parsed;
            return new MedicalRecord {Id=id,PersonId=pid,NationalId=nid,DateText=date,Date=TryDate(date,out parsed)?(DateTime?)parsed:null,Status=Text(row,"status")};
        }
        public static bool TryDate(string text,out DateTime date)
        { return DateTime.TryParseExact(text,new string[] {"yyyy/MM/dd","yyyy-MM-dd"},CultureInfo.InvariantCulture,DateTimeStyles.None,out date) && date.Year>=1900 && date.Year<=2200; }
        static string Segment(string id) {RequireId(id);return Uri.EscapeDataString(id);}
        static void RequireId(string id) {if(string.IsNullOrWhiteSpace(id) || !Regex.IsMatch(id,"^[A-Za-z0-9_-]{1,100}$")) throw new HLineException("identifier","شناسهٔ سامانه معتبر نیست.");}
        internal static Dictionary<string,object> Dict(object value) {var d=value as Dictionary<string,object>;if(d==null) throw new HLineException("malformed","ساختار پاسخ سامانه معتبر نیست.");return d;}
        internal static string Text(Dictionary<string,object> d,string key) {object v;return d.TryGetValue(key,out v) && v!=null && !(v is IDictionary) && !(v is object[])?Convert.ToString(v,CultureInfo.InvariantCulture):"";}
        static Dictionary<string,object> OptionalDict(Dictionary<string,object> d,string key) {object v;return d.TryGetValue(key,out v) && v is Dictionary<string,object>?(Dictionary<string,object>)v:new Dictionary<string,object>();}
        static Dictionary<string,object> DataObject(object value) {var d=Dict(value);object v;return d.TryGetValue("data",out v)?Dict(v):d;}
        static Dictionary<string,object> OptionalDataObject(object value)
        {
            try
            {
                var d=Dict(value);object v;
                if(!d.TryGetValue("data",out v) || v==null) return new Dictionary<string,object>();
                var data=v as Dictionary<string,object>;
                return data??new Dictionary<string,object>();
            }
            catch(HLineException) { return new Dictionary<string,object>(); }
        }
        static double Number(Dictionary<string,object> d,string key) {return Fmt.Parse(Text(d,key),double.NaN);}
        static int FullCount(List<Dictionary<string,object>> rows) {return rows.Count==0?0:(int)Fmt.Parse(Text(rows[0],"full_count"),rows.Count);}
        static List<Dictionary<string,object>> Rows(object value)
        {
            if(value is Dictionary<string,object>)
            {
                var d=Dict(value); object nested=null;
                foreach(string k in new string[] {"rows","records","items","results","list","data"}) if(d.TryGetValue(k,out nested)) return Rows(nested);
                if(d.ContainsKey("id")) return new List<Dictionary<string,object>> {d};
                throw new HLineException("malformed","فهرست پاسخ سامانه معتبر نیست.");
            }
            object[] array=value as object[]; if(array==null) throw new HLineException("malformed","فهرست پاسخ سامانه معتبر نیست.");
            var result=new List<Dictionary<string,object>>(); foreach(object item in array) result.Add(Dict(item)); return result;
        }
        public void Dispose() { lock(sessionLock) {csrf=userId=null;Status=SessionStatus.Disconnected;transport.Dispose();} }
    }
}

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
    public sealed class HLineHttpTransport : IHLineTransport
    {
        readonly Uri origin;
        CookieContainer cookies=new CookieContainer();
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
            if(!string.IsNullOrEmpty(csrf)) request.Headers["X-CSRF-Token"]=csrf;
            try
            {
                if(body!=null) { byte[] bytes=Encoding.UTF8.GetBytes(body); request.ContentType="application/json"; request.ContentLength=bytes.Length; using(Stream s=request.GetRequestStream()) s.Write(bytes,0,bytes.Length); }
                try { using(HttpWebResponse r=(HttpWebResponse)request.GetResponse()) return Read(r); }
                catch(WebException e) { if(e.Response==null) throw new HLineException("network","عدم دسترسی به سامانه"); using(HttpWebResponse r=(HttpWebResponse)e.Response) return Read(r); }
            }
            catch(HLineException) { throw; }
            catch { throw new HLineException("network","عدم دسترسی به سامانه"); }
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
        readonly IHLineTransport transport;
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=2*1024*1024};
        string csrf,userId;
        public SessionStatus Status {get;private set;}
        public HLineClient():this(new HLineHttpTransport("https://hline.ssu.ac.ir/")) { }
        public HLineClient(IHLineTransport transport) { this.transport=transport; }
        public string StatusText {get { switch(Status) {case SessionStatus.Connected:return "متصل به سامانه";case SessionStatus.Expired:return "نشست منقضی شده";case SessionStatus.AuthenticationError:return "خطای احراز هویت";case SessionStatus.Unavailable:return "عدم دسترسی به سامانه";default:return "متصل نیست";} } }
        object Request(string method,string path,object body)
        {
            HLineResponse r;
            try { r=transport.Send(method,path,body==null?null:json.Serialize(body),csrf); }
            catch { Status=SessionStatus.Unavailable; throw new HLineException("network","عدم دسترسی به سامانه"); }
            if((r.Status==401 && path!="/auth/login") || r.Status==419 || (r.Status>=300 && r.Status<400)) { Status=SessionStatus.Expired; throw new HLineException("expired","نشست منقضی شده؛ دوباره وارد شوید."); }
            if(r.Status==403) throw new HLineException("forbidden","مجوز دسترسی به این بخش وجود ندارد.");
            if((r.Status<200 || r.Status>=300) && !(path=="/auth/login" && (r.Status==400 || r.Status==401 || r.Status==422))) throw new HLineException("server","پاسخ ناموفق سامانه؛ اطلاعات محلی حفظ شد.");
            try { return json.DeserializeObject(r.Body); } catch { throw new HLineException("malformed","ساختار پاسخ سامانه معتبر نیست."); }
        }
        public void Login(string username,string password,string mfa)
        {
            Status=SessionStatus.Disconnected; csrf=userId=null;
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
                Status=SessionStatus.Connected; VerifySession();
            }
            catch(HLineException e) { if(e.Code!="network") Status=SessionStatus.AuthenticationError; throw; }
        }
        public void VerifySession()
        {
            if(Status!=SessionStatus.Connected) throw new HLineException("session","ابتدا وارد سامانه شوید.");
            Dictionary<string,object> me=Dict(Request("GET","/me",null)), data=OptionalDict(me,"data");
            string actual=Text(OptionalDict(me,"user"),"id");if(actual.Length==0) actual=Text(OptionalDict(data,"user"),"id");if(actual.Length==0) actual=Text(data,"id");
            if(actual.Length==0 || actual!=userId) { Status=SessionStatus.Expired; throw new HLineException("expired","هویت نشست تأیید نشد؛ دوباره وارد شوید."); }
        }
        public HLinePerson Lookup(string nationalId)
        {
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
        }
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
        {
            HLinePerson person=Lookup(nationalId);
            MedicalRecord latest=LatestUnambiguous(person.Records);
            if(latest==null) throw new HLineException("latest_record","آخرین پروندهٔ پزشکی با تاریخ مشخص و یکتا یافت نشد.");
            return PopulateRecord(person,latest);
        }
        public void VerifyLatestRecord(Patient patient)
        {
            HLinePerson person=Lookup(patient.NationalId);
            MedicalRecord latest=LatestUnambiguous(person.Records);
            if(latest==null) throw new HLineException("latest_record","آخرین پروندهٔ پزشکی مشخص نیست؛ ارسال انجام نشد.");
            if(person.Patient.HLinePersonId!=patient.HLinePersonId || latest.Id!=patient.HLineRecordId || latest.DateText!=patient.HLineRecordDate || latest.Status!=patient.HLineRecordStatus) throw new HLineException("destination_changed","پروندهٔ مقصد تغییر کرده است؛ مشخصات را دوباره از سایت دریافت کنید.");
            VerifyRecord(latest,patient.HLinePersonId,patient.NationalId);
        }
        public Patient PopulateRecord(HLinePerson person,MedicalRecord record)
        {
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
        }
        static bool VitalDate(Dictionary<string,object> row,out DateTimeOffset date)
        {
            string text=Text(row,"test_date");if(text.Length==0) text=Text(row,"created_at");
            DateTime day;if(TryDate(text,out day)) {date=new DateTimeOffset(day,TimeSpan.Zero);return true;}
            return DateTimeOffset.TryParseExact(text,new string[] {"yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK","yyyy-MM-dd'T'HH:mm:ssK","yyyy-MM-dd HH:mm:ss","yyyy/MM/dd HH:mm:ss"},CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out date) && date.Year>=1900 && date.Year<=2200;
        }
        public bool HasSpirometry(MedicalRecord record)
        {
            List<Dictionary<string,object>> rows=Rows(Request("GET","/medical-records/"+Segment(record.Id)+"/spirometry/history",null));
            foreach(var row in rows) { if(Text(row,"medical_record_id").Length==0) throw new HLineException("history","انتساب نتیجهٔ قبلی به پرونده نامشخص است."); if(Text(row,"medical_record_id")==record.Id) return true; }
            return false;
        }
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
        public void Dispose() {csrf=userId=null;Status=SessionStatus.Disconnected;transport.Dispose();}
    }
}

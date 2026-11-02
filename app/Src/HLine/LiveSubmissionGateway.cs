using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using SpiroLab.Ui;

namespace SpiroLab.HLine
{
    // Direct HTTP implementation of the HLine spirometry write contract discovered
    // from the production bundle. It is deliberately separate from the read-only
    // transport: only the four clinical write/readback operations below are reachable.
    public sealed class LiveSubmissionGateway : ISubmissionGateway, ISubmissionUpdateGateway
    {
        readonly HLineClient client;
        readonly Dictionary<string,HashSet<string>> knownFiles=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
        readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=2*1024*1024};
        public LiveSubmissionGateway(HLineClient client)
        {
            if(client==null) throw new ArgumentNullException("client");
            this.client=client;
        }
        public bool ContractVerified {get {return client!=null && client.CanWrite;}}
        // Only the explicit, designated test-record scope enables this; production
        // submissions still require the patient's latest record.
        public bool AllowHistoricalRecord {get;set;}
#if SPIROLAB_TEST
        public SubmissionTiming Timing {get;set;}
#endif

        public void VerifyDestination(SubmissionDraft draft)
        {
#if SPIROLAB_TEST
            if(Timing==null)
            {
                if(AllowHistoricalRecord) client.VerifySubmissionRecord(draft);
                else client.VerifySubmissionDestination(draft);
                CaptureKnownFiles(draft.RecordId);
            }
            else
            {
                Timing.Measure("patient_record_verify",delegate
                {
                    if(AllowHistoricalRecord) client.VerifySubmissionRecord(draft);
                    else client.VerifySubmissionDestination(draft);
                });
                Timing.Measure("existing_file_discovery",delegate { CaptureKnownFiles(draft.RecordId); });
            }
#else
            if(AllowHistoricalRecord) client.VerifySubmissionRecord(draft);
            else client.VerifySubmissionDestination(draft);
            CaptureKnownFiles(draft.RecordId);
#endif
        }

        // A row of data is not success if HLine still shows the spirometry module as
        // NoData. Only a completed/saved module is accepted.
        public void VerifySaved(SubmissionDraft draft)
        {
            string status="";
            // HLine updates the module row from the same POST; allow a short, bounded
            // read-after-write window instead of rejecting a normal eventual-consistency lag.
            for(int attempt=0;attempt<3;attempt++)
            {
#if SPIROLAB_TEST
                status=Timing==null?client.SpirometryModuleStatus(draft.RecordId):Timing.Measure("module_status_poll",delegate { return client.SpirometryModuleStatus(draft.RecordId); });
#else
                status=client.SpirometryModuleStatus(draft.RecordId);
#endif
                if(string.Equals(status,"complete",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"completed",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"saved",StringComparison.OrdinalIgnoreCase)) return;
                if(attempt<2) System.Threading.Thread.Sleep(150);
            }
            if(string.IsNullOrEmpty(status)) throw new HLineException("module_status","وضعیت ماژول اسپیرومتری در سامانه خوانده نشد؛ موفقیت تأیید نمی‌شود.");
            if(string.Equals(status,"nodata",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"no_data",StringComparison.OrdinalIgnoreCase)) throw new HLineException("module_status","ماژول اسپیرومتری سامانه هنوز خالی است؛ ثبت تأیید نشد.");
            throw new HLineException("module_status","وضعیت ماژول اسپیرومتری پس از ثبت «"+status+"» است؛ موفقیت تأیید نشد.");
        }

        public RemoteSpirometry Read(SubmissionDraft draft)
        {
#if SPIROLAB_TEST
            Dictionary<string,object> data=Timing==null?client.FindSpirometry(draft.RecordId):Timing.Measure("server_result_read",delegate { return client.FindSpirometry(draft.RecordId); });
#else
            Dictionary<string,object> data=client.FindSpirometry(draft.RecordId);
#endif
            if(data==null || data.Count==0) return null;
            Dictionary<string,double?> fields=new Dictionary<string,double?>();
            foreach(string key in new string[] {"fvc_amount","fev1_amount","pef_amount","fef2575_amount","vext","fvc_percent","fev1_percent","pef_percent","fef2575_percent"})
                fields[key]=Number(data,key);
            double? fvc=fields["fvc_amount"], fev1=fields["fev1_amount"];
            fields["fev1_fvc_percent"]=fvc.HasValue && fev1.HasValue && fvc.Value>0?(double?)Math.Round(fev1.Value/fvc.Value*100.0,2,MidpointRounding.AwayFromZero):null;
            string fileId=HLineClient.Text(data,"file_id");
            byte[] jpeg=null;
            if(!string.IsNullOrEmpty(fileId) && fileId!="0")
            {
                int status=0;
                byte[] bytes;
#if SPIROLAB_TEST
                if(Timing==null) bytes=client.FileBytes(draft.RecordId,fileId,out status);
                else bytes=Timing.Measure("jpg_download",delegate { int code=0; byte[] payload=client.FileBytes(draft.RecordId,fileId,out code); status=code; return payload; });
#else
                bytes=client.FileBytes(draft.RecordId,fileId,out status);
#endif
                if(status==200) jpeg=bytes;
            }
            DateTime date; HLineClient.TryDate(HLineClient.Text(data,"test_date"),out date);
            return new RemoteSpirometry {PersonId=draft.PersonId,NationalId=draft.NationalId,RecordId=draft.RecordId,RecordDate=draft.RecordDate,RecordStatus=draft.RecordStatus,FileId=fileId,ExaminationDate=date,Fields=fields,Jpeg=jpeg};
        }

        public string UploadJpeg(SubmissionDraft draft,string localReference)
        {
            RequireProvenance(draft);
            HashSet<string> before;
            if(!knownFiles.TryGetValue(draft.RecordId,out before)) {before=new HashSet<string>(StringComparer.Ordinal);CaptureKnownFiles(draft.RecordId);knownFiles.TryGetValue(draft.RecordId,out before);}
            try
            {
#if SPIROLAB_TEST
                HLineResponse response=Timing==null?client.WriteFile(draft.RecordId,draft.JpegBytes,draft.JpegFilename,"SpiroLab report"):Timing.Measure("jpg_upload_request",delegate { return client.WriteFile(draft.RecordId,draft.JpegBytes,draft.JpegFilename,"SpiroLab report"); });
#else
                HLineResponse response=client.WriteFile(draft.RecordId,draft.JpegBytes,draft.JpegFilename,"SpiroLab report");
#endif
                if(response.Status!=201 && response.Status!=200)
                {
                    if(response.Status>=500 || response.Status==408) throw new HLineException("uncertain_upload","پاسخ بارگذاری فایل نامطمئن است.");
                    throw Failure(response,"بارگذاری فایل");
                }
                string fileId=UploadedId(response.Body);
                if(string.IsNullOrEmpty(fileId)) throw new HLineException("uncertain_upload","شناسهٔ فایل در پاسخ سامانه پیدا نشد.");
                before.Add(fileId);
                return fileId;
            }
            catch(HLineException ex)
            {
                if(ex.Code!="network" && ex.Code!="uncertain_upload" && ex.Code!="timeout") throw;
#if SPIROLAB_TEST
                string reconciled=Timing==null?ReconcileUpload(draft,before):Timing.Measure("upload_reconcile",delegate { return ReconcileUpload(draft,before); });
#else
                string reconciled=ReconcileUpload(draft,before);
#endif
                if(!string.IsNullOrEmpty(reconciled)) return reconciled;
                throw new HLineException("uncertain_upload","نتیجهٔ بارگذاری نامطمئن است؛ فایل تکراری ارسال نشد.");
            }
        }

        public void Create(SubmissionDraft draft,string fileId,string localReference)
        {
            RequireProvenance(draft);
            Dictionary<string,object> payload=BuildPayload(draft,fileId);
#if SPIROLAB_TEST
            HLineResponse response=Timing==null?client.WriteJson("POST","/medical-records/"+Uri.EscapeDataString(draft.RecordId)+"/spirometry",payload):Timing.Measure("create_post_request",delegate { return client.WriteJson("POST","/medical-records/"+Uri.EscapeDataString(draft.RecordId)+"/spirometry",payload); });
#else
            HLineResponse response=client.WriteJson("POST","/medical-records/"+Uri.EscapeDataString(draft.RecordId)+"/spirometry",payload);
#endif
            if(response.Status!=201 && response.Status!=200) throw Failure(response,"ثبت اطلاعات اسپیرومتری");
        }

        // Explicit update path for a future, separately authorized correction flow.
        // The normal one-click button never calls this while a different result exists.
        public void Update(SubmissionDraft draft,string fileId,string localReference)
        {
            RequireProvenance(draft);
            Dictionary<string,object> payload=BuildPayload(draft,fileId);
            HLineResponse response=client.WriteJson("PATCH","/medical-records/"+Uri.EscapeDataString(draft.RecordId)+"/spirometry",payload);
            if(response.Status!=200 && response.Status!=204) throw Failure(response,"به‌روزرسانی اطلاعات اسپیرومتری");
        }

        static void RequireProvenance(SubmissionDraft draft)
        {
            if(draft==null) throw new ArgumentNullException("draft");
            DeviceProvenanceResult result=DeviceProvenanceValidator.Validate(draft.Snapshot);
            if(!result.Allowed) throw new DeviceProvenanceException(result);
        }

        internal Dictionary<string,object> BuildPayload(SubmissionDraft draft,string fileId)
        {
            string performedBy=client.CurrentUserId;
            if(string.IsNullOrEmpty(performedBy)) throw new HLineException("identity","شناسهٔ کاربر سامانه در دسترس نیست؛ دوباره وارد شوید.");
            Dictionary<string,object> p=new Dictionary<string,object>();
            p["successful"]=true;
            p["failure_notes"]=null;
            p["failure_reason"]=null;
            // Workflow 4 records measurements only. HLine's optional interpretation
            // fields are not created at all (absent, never null/empty text), so the
            // physician interprets the record in HLine. Historical workflow 3 and
            // older keep the exact mapping they were written with.
            if(draft.Snapshot.Data.SelectionWorkflowVersion<4)
            {
                string interpretation=ReadInterpretation(draft.Snapshot);
                string result=null,other=null;
                MapInterpretation(interpretation,out result,out other);
                p["result"]=result;
                p["result_severity"]=null;
                p["result_other"]=other;
            }
            p["test_date"]=draft.ExaminationDate.ToString("yyyy/MM/dd",CultureInfo.InvariantCulture);
            p["performed_by"]=performedBy;
            p["fev1_amount"]=Value(draft,"fev1_amount");
            p["fev1_percent"]=Value(draft,"fev1_percent");
            p["fvc_amount"]=Value(draft,"fvc_amount");
            p["fvc_percent"]=Value(draft,"fvc_percent");
            p["pef_amount"]=Value(draft,"pef_amount");
            p["pef_percent"]=Value(draft,"pef_percent");
            p["fef2575_amount"]=Value(draft,"fef2575_amount");
            p["fef2575_percent"]=Value(draft,"fef2575_percent");
            p["vext"]=Value(draft,"vext");
            p["file_id"]=fileId;
            foreach(string key in new string[] {"bronchodilator_type","bronchodilator_dose","bronchodilator_test_date","bronchodilator_performed_by","bronchodilator_fev1_amount","bronchodilator_fev1_percent","bronchodilator_fvc_amount","bronchodilator_fvc_percent","bronchodilator_pef_amount","bronchodilator_pef_percent","bronchodilator_fef2575_amount","bronchodilator_fef2575_percent","bronchodilator_vext","bronchodilator_file_id"}) p[key]=null;
            p["srp"]=null;
            p["notes"]=null;
            return p;
        }

        static object Value(SubmissionDraft draft,string key)
        {
            double? value; if(!draft.Fields.TryGetValue(key,out value) || !value.HasValue) return null;
            if(double.IsNaN(value.Value) || double.IsInfinity(value.Value)) throw new InvalidDataException("مقدار عددی نامعتبر است.");
            return Math.Round(value.Value,2,MidpointRounding.AwayFromZero);
        }

#if SPIROLAB_TEST
        // Test seam: proves the workflow-4 payload never evaluates an
        // interpretation. Production builds call SpiroReport directly.
        internal static Func<ExaminationSnapshot,string> InterpretationProvider=SpiroReport.Interpretation;
        static string ReadInterpretation(ExaminationSnapshot snapshot) {return InterpretationProvider(snapshot);}
#else
        static string ReadInterpretation(ExaminationSnapshot snapshot) {return SpiroReport.Interpretation(snapshot);}
#endif

        static void MapInterpretation(string text,out string result,out string other)
        {
            result=null;other=null;
            if(string.IsNullOrWhiteSpace(text)) return;
            string value=text.Trim();
            // A previously recorded "Not recorded" interpretation must stay unset;
            // do not turn it into Normal or Other.
            if(string.Equals(value,"Not recorded",StringComparison.OrdinalIgnoreCase) || string.Equals(value,"Not Recorded",StringComparison.OrdinalIgnoreCase)) return;
            string[] known=new string[] {"Normal","Probably Normal","Obstructive Pattern","Restrictive Pattern","Mixed","Small Airway Obstruction","Not Acceptable/Reproducible","Other"};
            foreach(string item in known) if(string.Equals(value,item,StringComparison.OrdinalIgnoreCase)) {result=item;return;}
            if(string.Equals(value,"Normal Spirometry",StringComparison.OrdinalIgnoreCase)) {result="Normal";return;}
            if(string.Equals(value,"Obstructive pattern",StringComparison.OrdinalIgnoreCase)) {result="Obstructive Pattern";return;}
            if(string.Equals(value,"Restrictive pattern",StringComparison.OrdinalIgnoreCase)) {result="Restrictive Pattern";return;}
            // Do not invent a category for mixed/outside-range or a clinician's free text.
            result="Other";other=value;
        }

        void CaptureKnownFiles(string recordId)
        {
            HashSet<string> set=new HashSet<string>(StringComparer.Ordinal);
            foreach(Dictionary<string,object> row in client.Files(recordId)) {string id=HLineClient.Text(row,"id");if(!string.IsNullOrEmpty(id)) set.Add(id);}
            knownFiles[recordId]=set;
        }

        string ReconcileUpload(SubmissionDraft draft,HashSet<string> before)
        {
            string match=null;
            foreach(Dictionary<string,object> row in client.Files(draft.RecordId))
            {
                string id=HLineClient.Text(row,"id");
                if(string.IsNullOrEmpty(id) || before.Contains(id)) continue;
                int status=0;byte[] bytes=null;
                try {bytes=client.FileBytes(draft.RecordId,id,out status);} catch(HLineException) {continue;}
                if(status!=200 || bytes==null || bytes.Length!=draft.JpegBytes.Length) continue;
                if(!SameHash(bytes,draft.JpegSha256)) continue;
                if(match!=null) throw new HLineException("uncertain_upload","چند فایل همسان پیدا شد؛ بازخوانی دستی لازم است.");
                match=id;
            }
            if(match!=null) knownFiles[draft.RecordId].Add(match);
            return match;
        }

        static bool SameHash(byte[] data,string expected)
        {
            using(SHA256 hash=SHA256.Create()) return string.Equals(BitConverter.ToString(hash.ComputeHash(data)).Replace("-",""),expected,StringComparison.OrdinalIgnoreCase);
        }

        static string UploadedId(string body)
        {
            try
            {
                var root=new JavaScriptSerializer().DeserializeObject(body) as Dictionary<string,object>;
                if(root==null) return "";
                object data;if(!root.TryGetValue("data",out data)) return "";
                var first=data as Dictionary<string,object>;if(first==null) return "";
                object inner;if(first.TryGetValue("data",out inner)) {var second=inner as Dictionary<string,object>;if(second!=null) first=second;}
                object id;return first.TryGetValue("id",out id) && id!=null?Convert.ToString(id,CultureInfo.InvariantCulture):"";
            }
            catch {return "";}
        }

        HLineException Failure(HLineResponse response,string operation)
        {
            string detail="";
            try
            {
                var root=json.DeserializeObject(response.Body) as Dictionary<string,object>;
                object errors;if(root!=null && root.TryGetValue("errors",out errors) && errors is object[])
                {
                    List<string> parts=new List<string>();
                    foreach(object item in (object[])errors) {string value=Convert.ToString(item,CultureInfo.InvariantCulture);if(!string.IsNullOrEmpty(value)) parts.Add(value);}
                    detail=string.Join("، ",parts.ToArray());
                }
            }
            catch { }
            if(detail=="duplicate_record" || detail.IndexOf("duplicate_record",StringComparison.OrdinalIgnoreCase)>=0) return new HLineException("duplicate_record","اسپیرومتری این پرونده قبلاً ثبت شده است.");
            if(response.Status==404) return new HLineException("not_found","پرونده در سامانه پیدا نشد.");
            if(response.Status==403) return new HLineException("forbidden","مجوز انجام این عملیات وجود ندارد.");
            if(response.Status==422) return new HLineException("validation",detail.Length>0?"سامانه داده را نپذیرفت: "+detail:"سامانه داده را نپذیرفت؛ مقادیر را بررسی کنید.");
            return new HLineException("server",operation+" انجام نشد (کد "+response.Status.ToString(CultureInfo.InvariantCulture)+").");
        }

        static double? Number(Dictionary<string,object> data,string key)
        {
            string text=HLineClient.Text(data,key);
            if(text.Length==0 || text=="null") return null;
            double value;return double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value) && !double.IsNaN(value) && !double.IsInfinity(value)?(double?)value:null;
        }
    }
}

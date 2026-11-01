using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SpiroLab.HLine
{
    public enum SubmissionState { Draft, ValidationPassed, AwaitingOperatorConfirmation, Uploading, Verifying, Confirmed, FailedOrUncertain }
    public sealed class SubmissionJournal
    {
        public string Reference=Guid.NewGuid().ToString("N"),FileId="",Problem="";
        public SubmissionState State;
        public bool WriteAttempted;
    }
    public sealed class SubmissionDraft
    {
        public const string JpegMimeType="image/jpeg";
        public readonly ExaminationSnapshot Snapshot;
        public readonly string ExaminationId,PersonId,NationalId,RecordId,RecordDate,RecordStatus,ManeuverId,JpegFilename,JpegSha256,Preview;
        public readonly DateTime ExaminationDate;
        readonly Dictionary<string,double?> fields=new Dictionary<string,double?>();
        readonly byte[] jpeg;
        public IDictionary<string,double?> Fields {get {return new Dictionary<string,double?>(fields);} }
        public byte[] JpegBytes {get {return (byte[])jpeg.Clone();} }
        public readonly bool SimulatedOrReplay,ParameterReviewRequired;
        public SubmissionDraft(ExaminationSnapshot snapshot,ResultPackage package)
        {
            Snapshot=snapshot; Exam e=snapshot.Data; e.ValidateSelection(true); e.ValidateReportSelection(true);
            Trial t=e.OperatorSelectedManeuver; AnalysisResult r=t.Result;
            ExaminationId=e.ExaminationId;PersonId=e.Patient.HLinePersonId;NationalId=e.Patient.NationalId;RecordId=e.Patient.HLineRecordId;RecordDate=e.Patient.HLineRecordDate;RecordStatus=e.Patient.HLineRecordStatus;ManeuverId=t.ManeuverId;ExaminationDate=snapshot.TestedAt.Date;
            SimulatedOrReplay=e.Trials.Exists(delegate(Trial trial) {return trial.Simulated || trial.Imported;});
            ParameterReviewRequired=!ManeuverAssessment.Assess(t).Fev1Usable || !e.Patient.AnthropometryConfirmed || e.Patient.SexUnconfirmed;
            fields["fvc_amount"]=r.Fvc;fields["fev1_amount"]=r.Fev1;fields["fev1_fvc_percent"]=r.Fev1Fvc;fields["fef2575_amount"]=r.Fef2575;fields["pef_amount"]=r.Pef;
            fields["vext"]=t.VextManuallyReviewed && !string.IsNullOrWhiteSpace(t.VextReviewer) && !string.IsNullOrWhiteSpace(t.ReviewedVextSource)?t.ReviewedVextMl:null;
            fields["fvc_percent"]=r.Ref!=null && r.Ref.Fvc>0?(double?)r.PctFvc:null;
            fields["fev1_percent"]=r.Ref!=null && r.Ref.Fev1>0?(double?)r.PctFev1:null;
            fields["pef_percent"]=r.Ref!=null && r.Ref.Pef>0?(double?)r.PctPef:null;
            fields["fef2575_percent"]=r.Ref!=null && r.Ref.Fef2575>0?(double?)r.PctFef2575:null;
            JpegFilename=Path.GetFileName(package.ReportPath);
            if(package.SourceSnapshotId!=snapshot.SnapshotId || package.ReportBytes==null) throw new InvalidDataException("تصویر متعلق به snapshot همین معاینه نیست.");
            jpeg=(byte[])package.ReportBytes.Clone();
            if(jpeg.Length>=5*1024*1024) throw new InvalidDataException("JPEG به سقف ۵ MiB فرم سایت رسیده است؛ فایل محلی حفظ شد.");
            if(jpeg.Length<4 || jpeg[0]!=255 || jpeg[1]!=216 || jpeg[jpeg.Length-2]!=255 || jpeg[jpeg.Length-1]!=217) throw new InvalidDataException("فایل JPEG واقعی نیست.");
            using(MemoryStream stream=new MemoryStream(jpeg)) using(Image image=Image.FromStream(stream)) if(image.RawFormat.Guid!=ImageFormat.Jpeg.Guid) throw new InvalidDataException("فایل JPEG واقعی نیست.");
            using(SHA256 hash=SHA256.Create()) JpegSha256=BitConverter.ToString(hash.ComputeHash(jpeg)).Replace("-","").ToLowerInvariant();
            StringBuilder b=new StringBuilder();b.AppendLine("مراجع: "+e.Patient.FullName);b.AppendLine("کد ملی: "+Mask(NationalId));b.AppendLine("پروندهٔ مقصد: "+RecordId);b.AppendLine("تاریخ پرونده: "+RecordDate+" | وضعیت: "+RecordStatus);
            b.AppendLine("تاریخ معاینه: "+ExaminationDates.DisplayJalali(ExaminationDate));b.AppendLine("مانور سامانه: "+t.AcquisitionSequence);
            string[] names={"FVC (L)","FEV1 (L)","FEV1/FVC (%)","FEF25–75 (L/s)","PEF (L/s)","VEXT (mL؛ واحد سایت در انتظار تأیید)"};string[] keys={"fvc_amount","fev1_amount","fev1_fvc_percent","fef2575_amount","pef_amount","vext"};
            for(int i=0;i<keys.Length;i++) b.AppendLine(names[i]+": "+(fields[keys[i]].HasValue?Fmt.N(fields[keys[i]].Value,i==2 || i==5?1:2):"موجود یا تأییدشده نیست"));
            b.AppendLine("تصویر: "+JpegFilename);var reports=new List<string>();foreach(Trial trial in e.Trials) if(e.ReportManeuverIds.Contains(trial.ManeuverId)) reports.Add(trial.AcquisitionSequence.ToString());b.AppendLine("مانورهای JPG: "+string.Join("، ",reports.ToArray()));
            b.AppendLine("BEST VALUES گزارش از مجموعهٔ انتخاب‌شده است؛ اعداد سایت فقط از مانور رادیویی‌اند.");
            if(SimulatedOrReplay) b.AppendLine("دادهٔ ساختگی یا بازپخش؛ ارسال ممنوع است."); Preview=b.ToString();
        }
        public void ValidateForSubmission()
        {
            if(SimulatedOrReplay) throw new InvalidOperationException("ارسال دادهٔ ساختگی یا بازپخش به HLine مجاز نیست.");
            if(string.IsNullOrWhiteSpace(PersonId) || string.IsNullOrWhiteSpace(RecordId) || NationalId.Length!=10 || string.IsNullOrWhiteSpace(RecordDate)) throw new InvalidOperationException("هویت شخص و پروندهٔ مقصد را از سایت دریافت و تأیید کنید.");
            if(ParameterReviewRequired) throw new InvalidOperationException("اعتبار FEV1 و مشخصات این معاینه باید بررسی شود.");
            foreach(var field in fields) if(field.Value.HasValue && (double.IsNaN(field.Value.Value) || double.IsInfinity(field.Value.Value) || field.Value<0)) throw new InvalidOperationException("مقدار عددی نامعتبر است.");
            foreach(string key in new string[] {"fvc_amount","fev1_amount","pef_amount","fef2575_amount","fev1_fvc_percent"}) if(!fields[key].HasValue || fields[key]<=0) throw new InvalidOperationException("مقدار الزامی موجود یا معتبر نیست: "+key);
            if(fields["fev1_amount"]>fields["fvc_amount"] || fields["fev1_fvc_percent"]>100 || Math.Abs(fields["fev1_fvc_percent"].Value-fields["fev1_amount"].Value/fields["fvc_amount"].Value*100)>1e-8) throw new InvalidOperationException("نسبت FEV1/FVC با مقادیر همان مانور تطبیق ندارد.");
            if(!fields["vext"].HasValue) throw new InvalidOperationException("VEXT مانور انتخاب‌شده موجود یا بازبینی‌شده نیست.");
        }
        public static string Mask(string nid) {return nid!=null && nid.Length>=4?new string('•',nid.Length-4)+nid.Substring(nid.Length-4):"—";}
    }
    public sealed class RemoteSpirometry
    {
        public string PersonId,NationalId,RecordId,RecordDate,RecordStatus,FileId;
        public DateTime ExaminationDate;
        public IDictionary<string,double?> Fields;
        public byte[] Jpeg;
    }
    // Deliberately no live HTTP writer until network contract + named test record + specific authorization exist.
    public interface ISubmissionGateway
    {
        bool ContractVerified {get;}
        void VerifyDestination(SubmissionDraft draft);
        RemoteSpirometry Read(SubmissionDraft draft);
        string UploadJpeg(SubmissionDraft draft,string localReference);
        void Create(SubmissionDraft draft,string fileId,string localReference);
    }
    public sealed class SubmissionCoordinator
    {
        readonly ISubmissionGateway gateway;
        readonly SubmissionJournal journal;
        readonly Action persist;
        int active;
        SubmissionDraft prepared;
        public SubmissionState State {get {return journal.State;} }
        public SubmissionCoordinator(ISubmissionGateway gateway,SubmissionJournal journal,Action persist) {this.gateway=gateway;this.journal=journal;this.persist=persist;}
        void Move(SubmissionState state) {journal.State=state;persist();}
        public void Prepare(SubmissionDraft draft)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            try
            {
            if(journal.WriteAttempted) throw new InvalidOperationException("ارسال قبلی باید از سرور بازخوانی شود؛ ارسال دوباره مجاز نیست.");
            draft.ValidateForSubmission();
            if(!gateway.ContractVerified) throw new InvalidOperationException("قرارداد نوشتن و واحدها هنوز با درخواست واقعی تأیید نشده است.");
            gateway.VerifyDestination(draft);
            if(gateway.Read(draft)!=null) throw new InvalidOperationException("این پرونده نتیجهٔ اسپیرومتری دارد؛ جایگزینی خودکار ممنوع است.");
            prepared=draft;Move(SubmissionState.ValidationPassed); Move(SubmissionState.AwaitingOperatorConfirmation);
            }
            finally {Interlocked.Exchange(ref active,0);}
        }
        public void Submit(SubmissionDraft draft,bool operatorConfirmed)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            try
            {
                if(!operatorConfirmed || !object.ReferenceEquals(prepared,draft) || State!=SubmissionState.AwaitingOperatorConfirmation || journal.WriteAttempted) throw new InvalidOperationException("تأیید ارسال معتبر نیست یا قبلاً تلاش شده است.");
                draft.ValidateForSubmission(); if(!gateway.ContractVerified) throw new InvalidOperationException("قرارداد ارسال تأیید نشده است.");
                gateway.VerifyDestination(draft);if(gateway.Read(draft)!=null) throw new InvalidOperationException("نتیجهٔ قبلی یافت شد؛ ارسال متوقف شد.");
                journal.WriteAttempted=true;Move(SubmissionState.Uploading); // Durable before the first non-idempotent request.
                journal.FileId=gateway.UploadJpeg(draft,journal.Reference);persist();
                if(string.IsNullOrWhiteSpace(journal.FileId)) throw new InvalidOperationException("شناسهٔ فایل برگشت داده نشد.");
                gateway.Create(draft,journal.FileId,journal.Reference);
                Move(SubmissionState.Verifying);
                if(!Matches(draft,gateway.Read(draft))) throw new InvalidOperationException("تطبیق اعداد، پرونده یا تصویر در سرور تأیید نشد.");
                Move(SubmissionState.Confirmed);
            }
            catch
            {
                if(journal.WriteAttempted)
                {
                    journal.Problem="ارسال نامطمئن یا ناقص است؛ بدون ارسال مجدد، نتیجه و فایل از سرور بررسی شود.";
                    Move(SubmissionState.FailedOrUncertain);
                }
                throw;
            }
            finally {Interlocked.Exchange(ref active,0);}
        }
        public bool Recheck(SubmissionDraft draft)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            try {gateway.VerifyDestination(draft);if(!journal.WriteAttempted || !Matches(draft,gateway.Read(draft))) return false;Move(SubmissionState.Confirmed);return true;}
            finally {Interlocked.Exchange(ref active,0);}
        }
        bool Matches(SubmissionDraft d,RemoteSpirometry remote)
        {
            if(remote==null || remote.PersonId!=d.PersonId || remote.NationalId!=d.NationalId || remote.RecordId!=d.RecordId || remote.RecordDate!=d.RecordDate || remote.RecordStatus!=d.RecordStatus || remote.ExaminationDate!=d.ExaminationDate || string.IsNullOrEmpty(journal.FileId) || remote.FileId!=journal.FileId || remote.Fields==null || remote.Jpeg==null) return false;
            foreach(var field in d.Fields) {double? v;if(!remote.Fields.TryGetValue(field.Key,out v) || v!=field.Value) return false;}
            byte[] expected=d.JpegBytes;if(remote.Jpeg.Length!=expected.Length) return false;
            for(int i=0;i<expected.Length;i++) if(remote.Jpeg[i]!=expected[i]) return false;
            return true;
        }
    }
}

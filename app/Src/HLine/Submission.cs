using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace SpiroLab.HLine
{
    // AwaitingOperatorConfirmation is retained only to read historical XML journals.
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
        public readonly string ExaminationId,PersonId,NationalId,RecordId,RecordDate,RecordStatus,ManeuverId,JpegFilename,JpegSha256;
        public readonly DateTime ExaminationDate;
        readonly Dictionary<string,double?> fields=new Dictionary<string,double?>();
        readonly byte[] jpeg;
        public IDictionary<string,double?> Fields {get {return new Dictionary<string,double?>(fields);} }
        public byte[] JpegBytes {get {return (byte[])jpeg.Clone();} }
        public readonly bool SimulatedOrReplay,ParameterReviewRequired;
        public readonly VextSelection Vext;
        public SubmissionDraft(ExaminationSnapshot snapshot,ResultPackage package)
        {
            Snapshot=snapshot; Exam e=snapshot.Data; e.ValidateSelection(e.SelectionWorkflowVersion<3); e.ValidateReportSelection(true);
            SessionBestValues best=snapshot.Summary.BestValues;
            if(best==null) throw new InvalidDataException("مقادیر BEST VALUES در دسترس نیست.");
            ExaminationId=e.ExaminationId;PersonId=e.Patient.HLinePersonId;NationalId=e.Patient.NationalId;RecordId=e.Patient.HLineRecordId;RecordDate=e.Patient.HLineRecordDate;RecordStatus=e.Patient.HLineRecordStatus;ManeuverId=best.Fvc.SourceManeuverId;ExaminationDate=snapshot.TestedAt.Date;
            SimulatedOrReplay=e.Trials.Exists(delegate(Trial trial) {return trial.Simulated || trial.Imported;});
            ParameterReviewRequired=!best.Fvc.Available || !best.Fev1.Available || !best.Fev1Fvc.Available || e.Patient.SexUnconfirmed;
            fields["fvc_amount"]=best.Fvc.Value;fields["fev1_amount"]=best.Fev1.Value;fields["fev1_fvc_percent"]=best.Fev1Fvc.Value;fields["fef2575_amount"]=best.Fef2575.Value;fields["pef_amount"]=best.Pef.Value;
            Vext=ClinicVext.Select(e.Trials);
            fields["vext"]=Vext.Available?(double?)Vext.ValueMl:null;
            fields["fvc_percent"]=best.Fvc.PercentPredicted;
            fields["fev1_percent"]=best.Fev1.PercentPredicted;
            fields["pef_percent"]=best.Pef.PercentPredicted;
            fields["fef2575_percent"]=best.Fef2575.PercentPredicted;
            JpegFilename=Path.GetFileName(package.ReportPath);
            if(package.SourceSnapshotId!=snapshot.SnapshotId || package.ReportBytes==null) throw new InvalidDataException("تصویر متعلق به snapshot همین معاینه نیست.");
            jpeg=(byte[])package.ReportBytes.Clone();
            if(jpeg.Length>=5*1024*1024) throw new InvalidDataException("JPEG به سقف ۵ MiB فرم سایت رسیده است؛ فایل محلی حفظ شد.");
            if(jpeg.Length<4 || jpeg[0]!=255 || jpeg[1]!=216 || jpeg[jpeg.Length-2]!=255 || jpeg[jpeg.Length-1]!=217) throw new InvalidDataException("فایل JPEG واقعی نیست.");
            using(MemoryStream stream=new MemoryStream(jpeg)) using(Image image=Image.FromStream(stream)) if(image.RawFormat.Guid!=ImageFormat.Jpeg.Guid) throw new InvalidDataException("فایل JPEG واقعی نیست.");
            using(SHA256 hash=SHA256.Create()) JpegSha256=BitConverter.ToString(hash.ComputeHash(jpeg)).Replace("-","").ToLowerInvariant();
        }
        public void ValidateForSubmission()
        {
            if(SimulatedOrReplay) throw new InvalidOperationException("ارسال دادهٔ ساختگی یا بازپخش به HLine مجاز نیست.");
            if(string.IsNullOrWhiteSpace(PersonId) || string.IsNullOrWhiteSpace(RecordId) || NationalId.Length!=10 || string.IsNullOrWhiteSpace(RecordDate)) throw new InvalidOperationException("مشخصات شخص و آخرین پرونده را از سایت دریافت کنید.");
            if(ParameterReviewRequired) throw new InvalidOperationException("اعتبار FEV1 و مشخصات این معاینه باید بررسی شود.");
            foreach(var field in fields) if(field.Value.HasValue && (double.IsNaN(field.Value.Value) || double.IsInfinity(field.Value.Value) || field.Value<0)) throw new InvalidOperationException("مقدار عددی نامعتبر است.");
            foreach(string key in new string[] {"fvc_amount","fev1_amount","pef_amount","fef2575_amount","fev1_fvc_percent"}) if(!fields[key].HasValue || fields[key]<=0) throw new InvalidOperationException("مقدار الزامی موجود یا معتبر نیست: "+key);
            if(fields["fev1_amount"]>fields["fvc_amount"] || fields["fev1_fvc_percent"]>100 || Math.Abs(fields["fev1_fvc_percent"].Value-fields["fev1_amount"].Value/fields["fvc_amount"].Value*100)>1e-8) throw new InvalidOperationException("نسبت FEV1/FVC با مقادیر همان مانور تطبیق ندارد.");
            if(!Vext.Available) throw new InvalidOperationException(Vext.Problem);
        }
    }
    public sealed class RemoteSpirometry
    {
        public string PersonId,NationalId,RecordId,RecordDate,RecordStatus,FileId;
        public DateTime ExaminationDate;
        public IDictionary<string,double?> Fields;
        public byte[] Jpeg;
    }
    // Live HTTP writer contract. Normal one-click submissions use the current patient's
    // re-verified latest record; a designated test record is only an optional test harness.
    public interface ISubmissionGateway
    {
        bool ContractVerified {get;}
        void VerifyDestination(SubmissionDraft draft);
        RemoteSpirometry Read(SubmissionDraft draft);
        string UploadJpeg(SubmissionDraft draft,string localReference);
        void Create(SubmissionDraft draft,string fileId,string localReference);
        void VerifySaved(SubmissionDraft draft);
    }
    // Explicit correction path; never used by the one-click submission button.
    public interface ISubmissionUpdateGateway
    {
        void Update(SubmissionDraft draft,string fileId,string localReference);
    }
    public sealed class SubmissionCoordinator
    {
        readonly ISubmissionGateway gateway;
        readonly SubmissionJournal journal;
        readonly Action persist;
        readonly MedicalRecord testRecord;
#if SPIROLAB_TEST
        readonly SubmissionTiming timing;
#endif
        int active;
        public SubmissionState State {get {return journal.State;} }
        public bool AlreadyPresent {get;private set;}
#if SPIROLAB_TEST
        public SubmissionCoordinator(ISubmissionGateway gateway,SubmissionJournal journal,Action persist,MedicalRecord designatedTestRecord) : this(gateway,journal,persist,designatedTestRecord,null) { }
        public SubmissionCoordinator(ISubmissionGateway gateway,SubmissionJournal journal,Action persist,MedicalRecord designatedTestRecord,SubmissionTiming timing)
        {
            this.gateway=gateway;this.journal=journal;this.persist=persist;this.timing=timing;
            if(designatedTestRecord!=null) testRecord=new MedicalRecord {Id=designatedTestRecord.Id,PersonId=designatedTestRecord.PersonId,NationalId=designatedTestRecord.NationalId,DateText=designatedTestRecord.DateText,Status=designatedTestRecord.Status};
        }
#else
        public SubmissionCoordinator(ISubmissionGateway gateway,SubmissionJournal journal,Action persist,MedicalRecord designatedTestRecord)
        {
            this.gateway=gateway;this.journal=journal;this.persist=persist;
            if(designatedTestRecord!=null) testRecord=new MedicalRecord {Id=designatedTestRecord.Id,PersonId=designatedTestRecord.PersonId,NationalId=designatedTestRecord.NationalId,DateText=designatedTestRecord.DateText,Status=designatedTestRecord.Status};
        }
#endif
        void Move(SubmissionState state) {journal.State=state;persist();}
        void MoveTimed(SubmissionState state) { Timed("journal_persist",delegate { Move(state); }); }
        void Timed(string stage,Action work)
        {
#if SPIROLAB_TEST
            if(timing==null) work(); else timing.Measure(stage,work);
#else
            work();
#endif
        }
        T Timed<T>(string stage,Func<T> work)
        {
#if SPIROLAB_TEST
            return timing==null?work():timing.Measure(stage,work);
#else
            return work();
#endif
        }
        void VerifyTestDestination(SubmissionDraft draft)
        {
            if(testRecord==null) throw new InvalidOperationException("ارسال فعال نیست؛ پروندهٔ تست اختصاصی هنوز معرفی نشده است.");
            if(draft.PersonId!=testRecord.PersonId || draft.NationalId!=testRecord.NationalId || draft.RecordId!=testRecord.Id || draft.RecordDate!=testRecord.DateText || draft.RecordStatus!=testRecord.Status) throw new InvalidOperationException("در مرحلهٔ تست، ارسال فقط به پروندهٔ تست تعیین‌شده مجاز است.");
        }
        public void Submit(SubmissionDraft draft)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            bool attempted=false;
            try
            {
                AlreadyPresent=false;
                Timed("draft_validate",delegate { draft.ValidateForSubmission(); });
                // A designated test record is optional now: normal production submission
                // is bound to the destination that the live gateway has just re-verified.
                if(testRecord!=null) Timed("designated_record_verify",delegate { VerifyTestDestination(draft); });
                if(gateway==null || !gateway.ContractVerified) throw new InvalidOperationException("قرارداد ارسال هنوز تأیید نشده است.");
                if(journal.WriteAttempted)
                {
                    // A previous non-idempotent attempt may have committed. Reconcile
                    // read-only; never send the write again automatically.
                    Timed("destination_verify",delegate { gateway.VerifyDestination(draft); });
                    RemoteSpirometry pending=Timed("existing_result_read",delegate { return gateway.Read(draft); });
                    if(pending!=null && SameResult(draft,pending))
                    {
                        if(!string.IsNullOrEmpty(journal.FileId) && pending.FileId!=journal.FileId) throw new InvalidOperationException("فایل مرجع در سرور با فایل ارسالی هم‌خوان نیست؛ بازخوانی دستی لازم است.");
                        Timed("module_verify",delegate { gateway.VerifySaved(draft); });
                        journal.FileId=pending.FileId;journal.Problem="";journal.State=SubmissionState.Confirmed;AlreadyPresent=true;Timed("journal_persist",delegate { persist(); });return;
                    }
                    throw new InvalidOperationException("ارسال قبلی باید از سرور بازخوانی شود؛ ارسال دوباره مجاز نیست.");
                }
                Timed("destination_verify",delegate { gateway.VerifyDestination(draft); });
                RemoteSpirometry existing=Timed("existing_result_read",delegate { return gateway.Read(draft); });
                if(existing!=null)
                {
                    if(!SameResult(draft,existing)) throw new InvalidOperationException("نتیجهٔ متفاوتی در این پرونده ثبت شده است؛ ارسال متوقف شد و چیزی بازنویسی نشد.");
                    Timed("module_verify",delegate { gateway.VerifySaved(draft); });
                    journal.State=SubmissionState.Confirmed;AlreadyPresent=true;Timed("journal_persist",delegate { persist(); });return;
                }
                MoveTimed(SubmissionState.ValidationPassed);
                attempted=journal.WriteAttempted=true;MoveTimed(SubmissionState.Uploading); // Durable before the first non-idempotent request.
                journal.FileId=Timed("jpg_upload",delegate { return gateway.UploadJpeg(draft,journal.Reference); });Timed("journal_persist",delegate { persist(); });
                if(string.IsNullOrWhiteSpace(journal.FileId)) throw new InvalidOperationException("شناسهٔ فایل برگشت داده نشد.");
                Timed("create_post",delegate { gateway.Create(draft,journal.FileId,journal.Reference); });
                MoveTimed(SubmissionState.Verifying);
                bool matches=Timed("server_readback",delegate { return Matches(draft,gateway.Read(draft)); });
                if(!matches) throw new InvalidOperationException("تطبیق اعداد، پرونده یا تصویر در سرور تأیید نشد.");
                Timed("module_verify",delegate { gateway.VerifySaved(draft); });
                MoveTimed(SubmissionState.Confirmed);
            }
            catch(HLineException ex)
            {
                if(attempted && ex.Code=="duplicate_record")
                {
                    RemoteSpirometry remote=gateway.Read(draft);
                    if(remote!=null && SameResult(draft,remote))
                    {
                        gateway.VerifySaved(draft);
                        journal.FileId=remote.FileId;journal.Problem="";Move(SubmissionState.Confirmed);return;
                    }
                    journal.Problem="سامانه اعلام کرد نتیجه قبلاً ثبت شده، اما تطبیق آن تأیید نشد.";
                    Move(SubmissionState.FailedOrUncertain);throw;
                }
                if(attempted)
                {
                    journal.Problem="ارسال نامطمئن یا ناقص است؛ بدون ارسال مجدد، نتیجه و فایل از سرور بررسی شود.";
                    Move(SubmissionState.FailedOrUncertain);
                }
                throw;
            }
            catch
            {
                if(attempted)
                {
                    journal.Problem="ارسال نامطمئن یا ناقص است؛ بدون ارسال مجدد، نتیجه و فایل از سرور بررسی شود.";
                    Move(SubmissionState.FailedOrUncertain);
                }
                throw;
            }
            finally {Interlocked.Exchange(ref active,0);}
        }
        // Never called by the normal one-click button. It exists for a separately
        // authorized correction and still requires an explicit caller.
        public void SubmitUpdate(SubmissionDraft draft)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            bool attempted=false;
            try
            {
                AlreadyPresent=false;
                draft.ValidateForSubmission();VerifyTestDestination(draft);
                ISubmissionUpdateGateway update=gateway as ISubmissionUpdateGateway;
                if(gateway==null || !gateway.ContractVerified || update==null) throw new InvalidOperationException("قرارداد به‌روزرسانی تأیید نشده است.");
                gateway.VerifyDestination(draft);
                RemoteSpirometry existing=gateway.Read(draft);
                if(existing==null) throw new InvalidOperationException("نتیجهٔ قبلی برای به‌روزرسانی پیدا نشد.");
                if(SameResult(draft,existing)) {journal.State=SubmissionState.Confirmed;AlreadyPresent=true;persist();return;}
                Move(SubmissionState.ValidationPassed);
                attempted=journal.WriteAttempted=true;Move(SubmissionState.Uploading);
                journal.FileId=gateway.UploadJpeg(draft,journal.Reference);persist();
                if(string.IsNullOrWhiteSpace(journal.FileId)) throw new InvalidOperationException("شناسهٔ فایل برگشت داده نشد.");
                update.Update(draft,journal.FileId,journal.Reference);
                Move(SubmissionState.Verifying);
                if(!Matches(draft,gateway.Read(draft))) throw new InvalidOperationException("تطبیق اعداد، پرونده یا تصویر در سرور تأیید نشد.");
                gateway.VerifySaved(draft);
                Move(SubmissionState.Confirmed);
            }
            catch
            {
                if(attempted) {journal.Problem="به‌روزرسانی نامطمئن یا ناقص است؛ بدون ارسال مجدد، نتیجه از سرور بررسی شود.";Move(SubmissionState.FailedOrUncertain);}
                throw;
            }
            finally {Interlocked.Exchange(ref active,0);}
        }
        public bool Recheck(SubmissionDraft draft)
        {
            if(Interlocked.CompareExchange(ref active,1,0)!=0) throw new InvalidOperationException("ارسال در حال انجام است.");
            try
            {
                // Production recheck is read-only and uses the same live destination
                // verification as Submit. A designated test record is optional.
                if(testRecord!=null) VerifyTestDestination(draft);
                gateway.VerifyDestination(draft);
                if(!journal.WriteAttempted) return false;
                RemoteSpirometry remote=gateway.Read(draft);
                if(remote==null || !SameResult(draft,remote)) return false;
                if(!string.IsNullOrEmpty(journal.FileId) && remote.FileId!=journal.FileId) return false;
                if(string.IsNullOrEmpty(journal.FileId)) journal.FileId=remote.FileId;
                gateway.VerifySaved(draft);
                Move(SubmissionState.Confirmed);return true;
            }
            finally {Interlocked.Exchange(ref active,0);}
        }
        bool SameResult(SubmissionDraft d,RemoteSpirometry remote)
        {
            if(remote==null || remote.PersonId!=d.PersonId || remote.NationalId!=d.NationalId || remote.RecordId!=d.RecordId || remote.RecordDate!=d.RecordDate || remote.RecordStatus!=d.RecordStatus || remote.ExaminationDate!=d.ExaminationDate || remote.Fields==null || remote.Jpeg==null) return false;
            foreach(var field in d.Fields) {double? v;if(!remote.Fields.TryGetValue(field.Key,out v) || !SameValue(field.Key,field.Value,v)) return false;}
            byte[] expected=d.JpegBytes;if(remote.Jpeg.Length!=expected.Length) return false;
            for(int i=0;i<expected.Length;i++) if(remote.Jpeg[i]!=expected[i]) return false;
            return true;
        }
        static bool SameValue(string key,double? expected,double? actual)
        {
            if(!expected.HasValue || !actual.HasValue) return expected.HasValue==actual.HasValue;
            // HLine stores amounts at its own display precision and derives FEV1/FVC.
            if(key=="fev1_fvc_percent") return Math.Abs(expected.Value-actual.Value)<=0.5;
            return Math.Round(expected.Value,2,MidpointRounding.AwayFromZero)==Math.Round(actual.Value,2,MidpointRounding.AwayFromZero);
        }
        bool Matches(SubmissionDraft d,RemoteSpirometry remote)
        {
            if(remote==null || remote.PersonId!=d.PersonId || remote.NationalId!=d.NationalId || remote.RecordId!=d.RecordId || remote.RecordDate!=d.RecordDate || remote.RecordStatus!=d.RecordStatus || remote.ExaminationDate!=d.ExaminationDate || string.IsNullOrEmpty(journal.FileId) || remote.FileId!=journal.FileId || remote.Fields==null || remote.Jpeg==null) return false;
            foreach(var field in d.Fields) {double? v;if(!remote.Fields.TryGetValue(field.Key,out v) || !SameValue(field.Key,field.Value,v)) return false;}
            byte[] expected=d.JpegBytes;if(remote.Jpeg.Length!=expected.Length) return false;
            for(int i=0;i<expected.Length;i++) if(remote.Jpeg[i]!=expected[i]) return false;
            return true;
        }
    }
}

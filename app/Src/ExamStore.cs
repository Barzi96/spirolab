using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SpiroLab
{
    public sealed class ExportReference
    { public string SnapshotId="",ReportPath="",DataPath=""; public DateTime CreatedAt; }
    public sealed class Exam
    {
        public int Version = 2;
        public string SessionType="PRE"; // PRE/POST are separate sessions; never mixed.
        public string ExaminationId = Guid.NewGuid().ToString("N");
        public string CenterName = "";
        public string OperatorName = "";
        public string ReviewedInterpretation = "";
        public string ReviewedQualityGrade = "";
        public string ReviewedQualityComment = "";
        public bool LegacyRecord;
        public DateTime? ExaminationDate;
        public string ExaminationDateSource="legacy_capture_date";
        public DateTime? FinalizedAt;
        public string ReportSnapshotId="";
        public double? ReviewedVextMl;
        public string ReviewedVextSource="";
        public bool VextManuallyReviewed;
        public string OperatorSelectedManeuverId="",SelectionMethod="",SelectionOperatorName="",SelectionReason="";
        public DateTime? SelectionTimestamp;
        public bool PrimarySelectionRequired;
        public int SelectionWorkflowVersion;
        public List<string> ReportManeuverIds=new List<string>();
        public bool ReportSelectionManual;
        public DateTime? ReportSelectionTimestamp;
        public string ReportSelectionOperator="";
        [XmlIgnore] public string SiteSelectedManeuverId {get {return OperatorSelectedManeuverId;} }
        [XmlIgnore] public IList<string> ReportSelectedManeuverIds {get {return ReportManeuverIds.AsReadOnly();} }
        public List<ExportReference> ExportReferences=new List<ExportReference>();
        public HLine.SubmissionJournal Submission=new HLine.SubmissionJournal();
        [XmlIgnore] public Trial OperatorSelectedManeuver { get { return Trials.Find(delegate(Trial t) { return t.ManeuverId==OperatorSelectedManeuverId; }); } }
        public void EnsureManeuverIdentities()
        {
            HashSet<string> ids=new HashSet<string>(); int last=0;
            foreach(Trial item in Trials)
            {
                if(item==null || string.IsNullOrEmpty(item.ManeuverId) || !ids.Add(item.ManeuverId)) throw new InvalidDataException("شناسهٔ مانورها تکراری یا نامعتبر است.");
                if(item.AcquisitionSequence==0) item.AcquisitionSequence=last+1;
                if(item.AcquisitionSequence<=last) throw new InvalidDataException("شمارهٔ اصلی مانورها نامعتبر است.");
                last=item.AcquisitionSequence;
            }
            HashSet<int> sequences=new HashSet<int>();
            foreach(Trial item in Trials) sequences.Add(item.AcquisitionSequence);
            if(DiscardedTrials!=null) foreach(Trial item in DiscardedTrials)
            {
                if(item==null || string.IsNullOrEmpty(item.ManeuverId) || !ids.Add(item.ManeuverId)) throw new InvalidDataException("شناسهٔ مانورها تکراری یا نامعتبر است.");
                if(item.AcquisitionSequence<=0 || !sequences.Add(item.AcquisitionSequence)) throw new InvalidDataException("شمارهٔ مانورهای فعال و بایگانی باید یکتا باشد.");
                if(!item.DiscardedAt.HasValue) item.DiscardedAt=DateTime.Now;
            }
            if(FailedCaptures!=null) foreach(Trial item in FailedCaptures)
            {
                if(item==null || string.IsNullOrEmpty(item.ManeuverId)) throw new InvalidDataException("شناسهٔ تلاش ناموفق نامعتبر است.");
            }
        }
        int NextAcquisitionSequence()
        {
            int highest=0;
            foreach(Trial item in Trials) if(item!=null && item.AcquisitionSequence>highest) highest=item.AcquisitionSequence;
            if(DiscardedTrials!=null) foreach(Trial item in DiscardedTrials) if(item!=null && item.AcquisitionSequence>highest) highest=item.AcquisitionSequence;
            return checked(highest+1);
        }
        public void AddManeuver(Trial trial)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            EnsureManeuverIdentities();
            if(trial==null || string.IsNullOrEmpty(trial.ManeuverId) || Trials.Exists(delegate(Trial t) { return t.ManeuverId==trial.ManeuverId; })) throw new ArgumentException("شناسهٔ مانور باید یکتا باشد.");
            if(SelectionWorkflowVersion>=4 && !trial.Simulated && !trial.Imported && trial.DeviceCaptureSequence>0) trial.RequiresDeviceCore=true;
            // Acquisition numbers are monotonic across active and archived attempts.
            // Reusing the last active number would collide with a discarded maneuver
            // later restored by the operator.
            trial.AcquisitionSequence=NextAcquisitionSequence();
            if(trial.Result==null) trial.Result=SpiroAnalysis.Analyze(trial,Patient);
            Trials.Add(trial);
            MaintainSelections();
        }
        public void RecordFailedCapture(Trial trial)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            if(trial==null) return;
            if(trial.Result==null) trial.Result=SpiroAnalysis.Analyze(trial,Patient);
            if(FailedCaptures==null) FailedCaptures=new List<Trial>();
            FailedCaptures.Add(trial);
        }
        public Trial DiscardManeuver(string id)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            int index=Trials.FindIndex(delegate(Trial item) { return item!=null && item.ManeuverId==id; });
            if(index<0) throw new ArgumentException("مانور در این معاینه وجود ندارد.");
            Trial t=Trials[index];
            Trials.RemoveAt(index);
            t.DiscardedAt=DateTime.Now;
            t.SelectedWhenDiscarded=ReportManeuverIds.Contains(id);
            ReportManeuverIds.Remove(id);
            if(OperatorSelectedManeuverId==id)
            {
                OperatorSelectedManeuverId=""; SelectionMethod=""; SelectionTimestamp=null; SelectionOperatorName=""; SelectionReason="";
            }
            if(DiscardedTrials==null) DiscardedTrials=new List<Trial>();
            DiscardedTrials.Add(t);
            MaintainSelections();
            return t;
        }
        public Trial RestoreLastDiscarded()
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            if(DiscardedTrials==null || DiscardedTrials.Count==0) return null;
            Trial t=DiscardedTrials[DiscardedTrials.Count-1];
            if(t==null || string.IsNullOrEmpty(t.ManeuverId) || t.AcquisitionSequence<=0) throw new InvalidDataException("مانور بازگردانده‌شده معتبر نیست.");
            // Validate the complete restoration before changing either list. A failed
            // validation must leave the active set, archive and selection untouched.
            int last=0;
            foreach(Trial item in Trials)
            {
                if(item==null || string.IsNullOrEmpty(item.ManeuverId)) throw new InvalidDataException("فهرست مانورهای فعال معتبر نیست.");
                if(item.ManeuverId==t.ManeuverId) throw new InvalidDataException("شناسهٔ مانور بازگردانده‌شده با مانور فعال یکسان است.");
                if(item.AcquisitionSequence<=last) throw new InvalidDataException("ترتیب مانورهای فعال معتبر نیست.");
                if(item.AcquisitionSequence==t.AcquisitionSequence) throw new InvalidDataException("شمارهٔ مانور بازگردانده‌شده با مانور فعال یکسان است.");
                last=item.AcquisitionSequence;
            }
            if(DiscardedTrials.Exists(delegate(Trial item) { return item!=null && item!=t && item.ManeuverId==t.ManeuverId; })) throw new InvalidDataException("شناسهٔ مانور بازگردانده‌شده در بایگانی تکراری است.");
            foreach(string id in ReportManeuverIds) if(!Trials.Exists(delegate(Trial item) { return item!=null && item.ManeuverId==id; })) throw new InvalidDataException("انتخاب گزارش با مانورهای فعال تطبیق ندارد.");

            List<Trial> activeBefore=new List<Trial>(Trials);
            List<Trial> discardedBefore=new List<Trial>(DiscardedTrials);
            List<string> selectedBefore=new List<string>(ReportManeuverIds);
            bool manualBefore=ReportSelectionManual;
            DateTime? selectedAtBefore=ReportSelectionTimestamp;
            string selectedByBefore=ReportSelectionOperator;
            DateTime? discardedAtBefore=t.DiscardedAt;
            bool selectedWhenDiscarded=t.SelectedWhenDiscarded;
            try
            {
                DiscardedTrials.RemoveAt(DiscardedTrials.Count-1);
                t.DiscardedAt=null;
                Trials.Add(t);
                Trials.Sort(delegate(Trial a,Trial b) { return a.AcquisitionSequence.CompareTo(b.AcquisitionSequence); });
                if(t.SelectedWhenDiscarded)
                {
                    // Returning a formerly selected maneuver is an explicit operator
                    // decision, not a new automatic ranking. Preserve it even when the
                    // three-report set is already full.
                    ReportSelectionManual=true;
                    if(!ReportManeuverIds.Contains(t.ManeuverId))
                    {
                        while(ReportManeuverIds.Count>=3 && ReportManeuverIds.Count>0) ReportManeuverIds.RemoveAt(ReportManeuverIds.Count-1);
                        ReportManeuverIds.Add(t.ManeuverId);
                    }
                    ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName;
                }
                t.SelectedWhenDiscarded=false;
                EnsureManeuverIdentities();
                MaintainSelections();
                return t;
            }
            catch
            {
                Trials.Clear(); Trials.AddRange(activeBefore);
                DiscardedTrials.Clear(); DiscardedTrials.AddRange(discardedBefore);
                ReportManeuverIds.Clear(); ReportManeuverIds.AddRange(selectedBefore);
                ReportSelectionManual=manualBefore; ReportSelectionTimestamp=selectedAtBefore; ReportSelectionOperator=selectedByBefore;
                t.DiscardedAt=discardedAtBefore; t.SelectedWhenDiscarded=selectedWhenDiscarded;
                throw;
            }
        }
        public void ResetReportSelectionAutomatic()
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            ReportSelectionManual=false; ReportSelectionTimestamp=null; ReportSelectionOperator="";
            MaintainSelections();
        }
        public void MaintainSelections()
        {
            if(FinalizedAt.HasValue) return;
            ExamSummary summary=SelectionWorkflowVersion>=5?ExamSummary.BuildClinical(Trials):ExamSummary.Build(Trials);
            if(SelectionWorkflowVersion>=3)
            {
                // New workflow: there is no primary maneuver. The best aggregate is
                // independent of both the report display set and the old fields.
                if(!ReportSelectionManual)
                {
                    ReportManeuverIds.Clear();
                    foreach(Trial t in summary.Trials) ReportManeuverIds.Add(t.ManeuverId);
                    ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName;
                }
                return;
            }
            if(SelectionWorkflowVersion<2) return;
            if(summary.BestTrial!=null && (string.IsNullOrEmpty(OperatorSelectedManeuverId) || SelectionMethod=="automatic"))
            {
                if(OperatorSelectedManeuverId!=summary.BestTrial.ManeuverId)
                { OperatorSelectedManeuverId=summary.BestTrial.ManeuverId; SelectionMethod="automatic"; SelectionTimestamp=DateTime.Now; SelectionOperatorName=OperatorName; SelectionReason=""; }
            }
            else if(summary.BestTrial==null && SelectionMethod=="automatic") OperatorSelectedManeuverId="";
            if(!ReportSelectionManual)
            {
                ReportManeuverIds.Clear();
                foreach(Trial t in summary.Trials) ReportManeuverIds.Add(t.ManeuverId);
                ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName;
            }
        }
        public void SelectSiteManeuver(string id)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            Trial t=Trials.Find(delegate(Trial item) { return item.ManeuverId==id; });
            if(t==null) throw new ArgumentException("مانور در این معاینه وجود ندارد.");
            OperatorSelectedManeuverId=id; SelectionMethod="manual"; SelectionOperatorName=OperatorName; SelectionTimestamp=DateTime.Now; SelectionReason="";
        }
        public void SetReportManeuver(string id,bool included)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            Trial t=Trials.Find(delegate(Trial item) { return item.ManeuverId==id; });
            if(t==null) throw new ArgumentException("مانور در این معاینه وجود ندارد.");
            if(included && !ReportManeuverIds.Contains(id))
            {
                if(ReportManeuverIds.Count>=3) throw new InvalidOperationException("برای گزارش حداکثر سه مانور قابل انتخاب است.");
                ReportManeuverIds.Add(id);
            }
            else if(!included) ReportManeuverIds.Remove(id);
            ReportSelectionManual=true; ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName;
        }
        public void ValidateReportSelection(bool required)
        {
            if(SelectionWorkflowVersion<2) return;
            if(ReportManeuverIds==null || ReportManeuverIds.Count>3 || new HashSet<string>(ReportManeuverIds).Count!=ReportManeuverIds.Count) throw new InvalidDataException("انتخاب‌های گزارش معتبر نیستند.");
            foreach(string id in ReportManeuverIds)
            {
                Trial t=Trials.Find(delegate(Trial item) { return item.ManeuverId==id; });
                if(t==null) throw new InvalidDataException("شناسهٔ مانور گزارش در معاینه وجود ندارد.");
                if(required && !ExamSummary.CanReport(t,SelectionWorkflowVersion>=5)) throw new InvalidOperationException("مانور "+t.AcquisitionSequence+": "+ManeuverAssessment.Assess(t,SelectionWorkflowVersion>=5).Problem);
            }
            if(required && ReportManeuverIds.Count==0) throw new InvalidOperationException("حداقل یک مانور برای گزارش انتخاب کنید.");
        }
        public void ValidateSelection(bool required)
        {
            // Workflow 3 removed the single primary maneuver. Old fields are retained
            // only for backward-compatible reading of historical records.
            if(SelectionWorkflowVersion>=3) return;
            if(string.IsNullOrEmpty(OperatorSelectedManeuverId)) { if(required) throw new InvalidOperationException("ابتدا مانور اصلی را صریحاً انتخاب کنید."); return; }
            if(OperatorSelectedManeuver==null || (SelectionMethod!="manual" && SelectionMethod!="automatic_suggestion" && SelectionMethod!="automatic") || !SelectionTimestamp.HasValue || (SelectionWorkflowVersion<2 && string.IsNullOrWhiteSpace(SelectionOperatorName))) throw new InvalidDataException("انتخاب مانور اصلی معتبر نیست.");
            if(required && !ExamSummary.CanReport(OperatorSelectedManeuver)) throw new InvalidOperationException(ManeuverAssessment.Assess(OperatorSelectedManeuver).Problem);
        }
        public void SelectPrimary(string id,string method,string operatorName,string reason)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("انتخاب معاینهٔ نهایی قابل تغییر نیست.");
            Trial trial=Trials.Find(delegate(Trial t) { return t.ManeuverId==id; });
            if(!ExamSummary.CanReport(trial)) throw new ArgumentException("مانور اصلی باید کامل، دارای دادهٔ کافی و ردنشده باشد.");
            if(string.IsNullOrWhiteSpace(operatorName) || operatorName.Trim().Length>100) throw new ArgumentException("نام کارشناس انتخاب‌کننده را وارد کنید.");
            if(method!="manual" && method!="automatic_suggestion") throw new ArgumentException("روش انتخاب معتبر نیست.");
            if(method=="automatic_suggestion" && ExamSummary.Build(Trials).BestTrial!=trial) throw new ArgumentException("این مانور پیشنهاد فعلی برنامه نیست.");
            if((reason??"").Length>500) throw new ArgumentException("علت انتخاب طولانی است.");
            OperatorSelectedManeuverId=id; SelectionMethod=method; SelectionOperatorName=operatorName.Trim(); SelectionReason=(reason??"").Trim(); SelectionTimestamp=DateTime.Now;
        }
        public void SetRejected(Trial trial,bool rejected,string operatorName,string reason)
        {
            if(FinalizedAt.HasValue || !Trials.Contains(trial)) throw new InvalidOperationException("تغییر وضعیت مانور مجاز نیست.");
            if(rejected && SelectionWorkflowVersion<3 && trial.ManeuverId==OperatorSelectedManeuverId) throw new InvalidOperationException("پیش از رد مانور اصلی، یک مانور اصلی دیگر انتخاب کنید.");
            if(string.IsNullOrWhiteSpace(operatorName) || operatorName.Trim().Length>100 || (reason??"").Length>500) throw new ArgumentException("نام کارشناس و علت معتبر وارد کنید.");
            trial.OperatorRejected=rejected; trial.RejectionOperator=operatorName.Trim(); trial.RejectionReason=(reason??"").Trim(); trial.RejectionTimestamp=DateTime.Now;
            if(rejected && SelectionWorkflowVersion>=3) ReportManeuverIds.Remove(trial.ManeuverId);
            MaintainSelections();
        }
        public static Exam CreateNew(AppSettings settings)
        { return new Exam { CenterName=settings.CenterName,OperatorName=settings.OperatorName,ExaminationDate=DateTime.Today,ExaminationDateSource="windows_local_date",PrimarySelectionRequired=false,SelectionWorkflowVersion=5,SessionType="PRE" }; }
        public void FinalizeExamination()
        {
            if(FinalizedAt.HasValue) return;
            EnsureManeuverIdentities(); MaintainSelections(); ValidateSelection(SelectionWorkflowVersion<3); ValidateReportSelection(true);
            ExaminationSnapshot.Capture(this); // validate before freezing
            foreach(Trial t in Trials) { if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,Patient); t.FinalizedResult=t.Result.Clone(); }
            ReportSnapshotId=Guid.NewGuid().ToString("N");
            FinalizedAt=DateTime.Now;
        }
        public Patient Patient = new Patient();
        public List<Trial> Trials = new List<Trial>();
        // User-discarded and failed-capture evidence stays local for recovery and
        // diagnostics, but is never part of the active clinical collection.
        public List<Trial> DiscardedTrials = new List<Trial>();
        public List<Trial> FailedCaptures = new List<Trial>();
        [XmlIgnore] public IEnumerable<Trial> AllStoredTrials
        {
            get
            {
                foreach(Trial t in Trials) yield return t;
                if(DiscardedTrials!=null) foreach(Trial t in DiscardedTrials) yield return t;
                if(FailedCaptures!=null) foreach(Trial t in FailedCaptures) yield return t;
            }
        }
    }
    public static class ExamStore
    {
        // x86 XML deserialization needs substantially more working memory than the file size.
        public const long MaxFileBytes=40L*1024*1024;
        public const string StorageLimitMessage="سقف فنی هر فایل معاینه ۴۰ MiB است (حفاظت حافظهٔ نسخهٔ ۳۲بیتی)، نه محدودیت تعداد مانور. پروندهٔ قبلی حفظ شد؛ دادهٔ ذخیره‌نشده در برنامه باقی است.";
        public static string DataDirectory
        {
            get { string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpiroLab", "exams"); Directory.CreateDirectory(d); return d; }
        }
        public static string NewPath()
        { return Path.Combine(DataDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".spiro"); }
        public static void Save(string path, Exam exam)
        {
            string temp = path + ".tmp";
            try
            {
                exam.EnsureManeuverIdentities(); exam.MaintainSelections(); exam.ValidateSelection(false); exam.ValidateReportSelection(false);
                foreach(Trial t in exam.AllStoredTrials)
                {
                    if(t.Result==null && t.Flow!=null) t.Result=SpiroAnalysis.Analyze(t,exam.Patient);
                    if(t.Result!=null && (!exam.FinalizedAt.HasValue || t.FinalizedResult==null)) t.FinalizedResult=t.Result.Clone();
                }
                using (FileStream f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    new XmlSerializer(typeof(Exam)).Serialize(f, exam);
                if(new FileInfo(temp).Length>MaxFileBytes) throw new InvalidDataException(StorageLimitMessage);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static Exam Load(string path)
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException(StorageLimitMessage);
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 40 * 1024 * 1024 };
            Exam e;
            using (XmlReader reader = XmlReader.Create(path, settings)) e = (Exam)new XmlSerializer(typeof(Exam)).Deserialize(reader);
            string problem;
            if (e == null || (e.Version != 1 && e.Version != 2) || e.Patient == null || !e.Patient.IsUsable(out problem) || e.Trials == null)
                throw new InvalidDataException("پروندهٔ معتبر SpiroLab نیست.");
            if(e.Version==1) { e.LegacyRecord=true; e.Version=2; }
            foreach (Trial t in e.Trials) ValidateStoredTrial(t, true, e.Patient);
            if(e.DiscardedTrials!=null) foreach(Trial t in e.DiscardedTrials) ValidateStoredTrial(t, false, e.Patient);
            if(e.FailedCaptures!=null) foreach(Trial t in e.FailedCaptures) ValidateStoredTrial(t, false, e.Patient);
            e.EnsureManeuverIdentities(); e.ValidateSelection(false); e.ValidateReportSelection(false);
            if(!e.ExaminationDate.HasValue && e.Trials.Count>0) { e.ExaminationDate=e.Trials[0].When.Date; e.ExaminationDateSource="legacy_capture_date"; }
            return e;
        }
        private static void ValidateStoredTrial(Trial t,bool requireCurve,Patient patient)
        {
            if(t==null) throw new InvalidDataException("داده‌های تست معتبر نیست.");
            if(t.Flow==null)
            {
                if(requireCurve) throw new InvalidDataException("داده‌های تست معتبر نیست.");
                return;
            }
            if(t.Time==null || t.Volume==null || t.Packets==null || t.Flow.Count > 120000 || t.Packets.Count > 150000 || t.Dt <= 0 || double.IsNaN(t.Dt) || double.IsInfinity(t.Dt))
                throw new InvalidDataException("داده‌های تست معتبر نیست.");
            if((t.Time.Count != 0 && t.Time.Count != t.Flow.Count) || (t.Volume.Count != 0 && t.Volume.Count != t.Flow.Count))
                throw new InvalidDataException("تعداد نمونه‌های حجم و زمان با جریان برابر نیست.");
            for(int i=0;i<t.Flow.Count;i++)
            {
                if(!Finite(t.Flow[i]) || Math.Abs(t.Flow[i]) > 25 || (t.Time.Count > 0 && (!Finite(t.Time[i]) || t.Time[i] < 0 || t.Time[i] > 3600 || (i > 0 && t.Time[i] < t.Time[i-1]))) || (t.Volume.Count > 0 && (!Finite(t.Volume[i]) || Math.Abs(t.Volume[i]) > 100)))
                    throw new InvalidDataException("نمونهٔ نامعتبر در فایل وجود دارد.");
            }
            t.Result = t.FinalizedResult!=null ? t.FinalizedResult.Clone() : SpiroAnalysis.Analyze(t, patient);
        }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }

        public static string Source(Trial t)
        { return t.Simulated ? "نمایشی / ساختگی" : t.Imported ? "بازپخش فایل دستگاه" : "دستگاه USB"; }

        public static Trial ImportCapture(string path, double volumeStep)
        {
            Trial t = new Trial { Imported = true, StepType = 0x30, Step = volumeStep, Completed = false };
            PacketDecoderImport(path, t);
            return t;
        }
        private static void PacketDecoderImport(string path, Trial trial)
        {
            if (new FileInfo(path).Length > 30 * 1024 * 1024) throw new InvalidDataException("فایل ضبط بیش از حد بزرگ است.");
            Device.PacketDecoder decoder = new Device.PacketDecoder();
            using (StreamReader reader = new StreamReader(path))
            {
                string header = reader.ReadLine();
                if (header == null || !header.StartsWith("ms,type,raw,")) throw new InvalidDataException("قالب فایل ضبط MIR معتبر نیست.");
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] c = line.Split(',');
                    if (c.Length < 3 || c[1] == "FF") continue;
                    double ms; int raw; byte type;
                    if (!double.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out ms) || !byte.TryParse(c[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out type) || !int.TryParse(c[2], out raw) || ms < 0 || !Finite(ms) || raw < 0 || raw > 65535)
                        throw new InvalidDataException("سطر نامعتبر در فایل ضبط.");
                    decoder.Add(trial, new Device.DevicePacket { Seconds = ms / 1000.0, Type = type, Raw = raw });
                    if (type == 0x04) { trial.Completed = true; break; }
                }
            }
            if (trial.Flow.Count == 0) throw new InvalidDataException("فایل هیچ بستهٔ جریان ندارد.");
            if (!trial.Completed) trial.EndReason = "بستهٔ پایان تست در فایل وجود ندارد";
        }

        private static string CsvText(string s)
        {
            s = s ?? "";
            if (s.Length > 0 && "=+-@".IndexOf(s[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        public static void ExportCsv(string path, Exam e)
        {
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                w.WriteLine("Patient,NationalId,Age,Sex,Origin,Height_cm,Weight_kg,Trial,Source,Section,Time_s,Volume_L,Flow_Lps,Metric,Value,Unit");
                ExamSummary best = e.SelectionWorkflowVersion>=5?ExamSummary.BuildForExam(e):e.SelectionWorkflowVersion>=4?ExamSummary.BuildSelected(e.Trials,e.ReportManeuverIds):ExamSummary.Build(e.Trials,false);
                if (best.Best != null)
                {
                    string prefix = CsvText(e.Patient.FullName) + "," + CsvText(e.Patient.NationalId) + "," + e.Patient.Age.ToString(CultureInfo.InvariantCulture) + "," + e.Patient.Sex + "," + e.Patient.Origin + "," + e.Patient.HeightCm.ToString("R",CultureInfo.InvariantCulture) + "," + e.Patient.WeightKg.ToString("R",CultureInfo.InvariantCulture) + ",BEST," + CsvText(Source(best.BestTrial)) + ",";
                    string[] names = { "FVC", "FEV1", "FEV1/FVC", "PEF", "FEF25-75" };
                    double[] values = { best.Fvc, best.Fev1, best.Ratio, best.Pef, best.Fef2575 };
                    string[] units = { "L", "L", "%", "L/s", "L/s" };
                    for (int i=0;i<names.Length;i++) w.WriteLine(prefix + "Best,,,," + names[i] + "," + values[i].ToString("R",CultureInfo.InvariantCulture) + "," + units[i]);
                }
                for (int k = 0; k < e.Trials.Count; k++)
                {
                    Trial t = e.Trials[k];
                    string prefix = CsvText(e.Patient.FullName) + "," + CsvText(e.Patient.NationalId) + "," + e.Patient.Age.ToString(CultureInfo.InvariantCulture) + "," + e.Patient.Sex + "," + e.Patient.Origin + "," + e.Patient.HeightCm.ToString("R",CultureInfo.InvariantCulture) + "," + e.Patient.WeightKg.ToString("R",CultureInfo.InvariantCulture) + "," + (k + 1) + "," + CsvText(Source(t)) + ",";
                    AnalysisResult r = t.Result;
                    if (r != null && r.Valid)
                    {
                        string[] keys = { "FVC", "FEV1", "FEV1/FVC", "PEF", "FEF25", "FEF50", "FEF75", "FEF25-75", "FEV6", "FET", "BEV" };
                        double[] vals = { r.Fvc, r.Fev1, r.Fev1Fvc, r.Pef, r.Fef25, r.Fef50, r.Fef75, r.Fef2575, r.Fev6, r.Fet, r.Bev*1000 };
                        string[] units = { "L", "L", "%", "L/s", "L/s", "L/s", "L/s", "L/s", "L", "s", "mL" };
                        for (int i = 0; i < keys.Length; i++) w.WriteLine(prefix + "Result,,,," + keys[i] + "," + (keys[i]=="BEV" && !r.BevAvailable?"":vals[i].ToString("R", CultureInfo.InvariantCulture)) + "," + units[i]);
                    }
                    double volume = 0;
                    for (int i = 0; i < t.Flow.Count; i++)
                    {
                        double time = t.Time.Count == t.Flow.Count ? t.Time[i] : i * t.Dt;
                        volume = t.Volume.Count == t.Flow.Count ? t.Volume[i] : volume + t.Flow[i] * t.Dt;
                        w.WriteLine(prefix + "Sample," + time.ToString("R", CultureInfo.InvariantCulture) + "," + volume.ToString("R", CultureInfo.InvariantCulture) + "," + t.Flow[i].ToString("R", CultureInfo.InvariantCulture) + ",,,");
                    }
                    foreach (Device.DevicePacket packet in t.Packets)
                        w.WriteLine(prefix + "Packet," + packet.Seconds.ToString("R", CultureInfo.InvariantCulture) + ",,," + packet.Type.ToString("X2") + "," + packet.Raw.ToString(CultureInfo.InvariantCulture) + ",raw");
                }
            }
        }
    }
}

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
        public List<ExportReference> ExportReferences=new List<ExportReference>();
        [XmlIgnore] public Trial OperatorSelectedManeuver { get { return Trials.Find(delegate(Trial t) { return t.ManeuverId==OperatorSelectedManeuverId; }); } }
        public void EnsureManeuverIdentities()
        {
            HashSet<string> ids=new HashSet<string>(); int last=0;
            foreach(Trial t in Trials)
            {
                if(t==null || string.IsNullOrEmpty(t.ManeuverId) || !ids.Add(t.ManeuverId)) throw new InvalidDataException("شناسهٔ مانورها تکراری یا نامعتبر است.");
                if(t.AcquisitionSequence==0) t.AcquisitionSequence=last+1;
                if(t.AcquisitionSequence<=last) throw new InvalidDataException("شمارهٔ اصلی مانورها نامعتبر است.");
                last=t.AcquisitionSequence;
            }
        }
        public void AddManeuver(Trial trial)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            EnsureManeuverIdentities();
            if(trial==null || string.IsNullOrEmpty(trial.ManeuverId) || Trials.Exists(delegate(Trial t) { return t.ManeuverId==trial.ManeuverId; })) throw new ArgumentException("شناسهٔ مانور باید یکتا باشد.");
            trial.AcquisitionSequence=Trials.Count==0?1:checked(Trials[Trials.Count-1].AcquisitionSequence+1);
            if(trial.Result==null) trial.Result=SpiroAnalysis.Analyze(trial,Patient);
            Trials.Add(trial);
            MaintainSelections();
        }
        public void MaintainSelections()
        {
            if(FinalizedAt.HasValue || SelectionWorkflowVersion<2) return;
            ExamSummary summary=ExamSummary.Build(Trials);
            if(summary.BestTrial!=null && (string.IsNullOrEmpty(OperatorSelectedManeuverId) || SelectionMethod=="automatic"))
            {
                if(OperatorSelectedManeuverId!=summary.BestTrial.ManeuverId)
                { OperatorSelectedManeuverId=summary.BestTrial.ManeuverId; SelectionMethod="automatic"; SelectionTimestamp=DateTime.Now; SelectionOperatorName=OperatorName; SelectionReason=""; }
            }
            if(!ReportSelectionManual) foreach(Trial t in Trials)
                if(ReportManeuverIds.Count<3 && ExamSummary.CanReport(t) && !ReportManeuverIds.Contains(t.ManeuverId)) { ReportManeuverIds.Add(t.ManeuverId); ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName; }
        }
        public void SelectSiteManeuver(string id)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            Trial t=Trials.Find(delegate(Trial item) { return item.ManeuverId==id; });
            if(!ExamSummary.CanReport(t)) throw new ArgumentException("این مانور قابل انتخاب نیست.");
            OperatorSelectedManeuverId=id; SelectionMethod="manual"; SelectionOperatorName=OperatorName; SelectionTimestamp=DateTime.Now; SelectionReason="";
        }
        public void SetReportManeuver(string id,bool included)
        {
            if(FinalizedAt.HasValue) throw new InvalidOperationException("معاینه نهایی شده است.");
            Trial t=Trials.Find(delegate(Trial item) { return item.ManeuverId==id; });
            if(!ExamSummary.CanReport(t)) throw new ArgumentException("این مانور قابل انتخاب نیست.");
            if(included && !ReportManeuverIds.Contains(id))
            {
                if(ReportManeuverIds.Count>=3) throw new InvalidOperationException("برای گزارش حداکثر ۳ مانور قابل انتخاب است.");
                ReportManeuverIds.Add(id);
            }
            else if(!included) ReportManeuverIds.Remove(id);
            ReportSelectionManual=true; ReportSelectionTimestamp=DateTime.Now; ReportSelectionOperator=OperatorName;
        }
        public void ValidateReportSelection(bool required)
        {
            if(SelectionWorkflowVersion<2) return;
            if(ReportManeuverIds==null || ReportManeuverIds.Count>3 || new HashSet<string>(ReportManeuverIds).Count!=ReportManeuverIds.Count) throw new InvalidDataException("انتخاب‌های گزارش معتبر نیستند.");
            foreach(string id in ReportManeuverIds) if(!ExamSummary.CanReport(Trials.Find(delegate(Trial t) { return t.ManeuverId==id; }))) throw new InvalidDataException("مانور انتخاب‌شدهٔ گزارش قابل استفاده نیست.");
            int eligible=Trials.FindAll(ExamSummary.CanReport).Count;
            if(required && (eligible==0 || ReportManeuverIds.Count!=Math.Min(3,eligible))) throw new InvalidOperationException(eligible>=3?"برای گزارش ۳ مانور انتخاب کنید.":"مانورهای موجود را برای گزارش انتخاب کنید.");
        }
        public void ValidateSelection(bool required)
        {
            if(string.IsNullOrEmpty(OperatorSelectedManeuverId)) { if(required) throw new InvalidOperationException("ابتدا مانور اصلی را صریحاً انتخاب کنید."); return; }
            if(!ExamSummary.CanReport(OperatorSelectedManeuver) || (SelectionMethod!="manual" && SelectionMethod!="automatic_suggestion" && SelectionMethod!="automatic") || !SelectionTimestamp.HasValue || (SelectionWorkflowVersion<2 && string.IsNullOrWhiteSpace(SelectionOperatorName))) throw new InvalidDataException("انتخاب مانور اصلی معتبر نیست.");
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
            if(rejected && trial.ManeuverId==OperatorSelectedManeuverId) throw new InvalidOperationException("پیش از رد مانور اصلی، یک مانور اصلی دیگر انتخاب کنید.");
            if(string.IsNullOrWhiteSpace(operatorName) || operatorName.Trim().Length>100 || (reason??"").Length>500) throw new ArgumentException("نام کارشناس و علت معتبر وارد کنید.");
            trial.OperatorRejected=rejected; trial.RejectionOperator=operatorName.Trim(); trial.RejectionReason=(reason??"").Trim(); trial.RejectionTimestamp=DateTime.Now;
        }
        public static Exam CreateNew(AppSettings settings)
        { return new Exam { CenterName=settings.CenterName,OperatorName=settings.OperatorName,ExaminationDate=DateTime.Today,ExaminationDateSource="windows_local_date",PrimarySelectionRequired=true,SelectionWorkflowVersion=2 }; }
        public void FinalizeExamination()
        {
            if(FinalizedAt.HasValue) return;
            EnsureManeuverIdentities(); MaintainSelections(); ValidateSelection(true); ValidateReportSelection(true);
            ExaminationSnapshot.Capture(this); // validate before freezing
            foreach(Trial t in Trials) { if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,Patient); t.FinalizedResult=t.Result.Clone(); }
            ReportSnapshotId=Guid.NewGuid().ToString("N");
            FinalizedAt=DateTime.Now;
        }
        public Patient Patient = new Patient();
        public List<Trial> Trials = new List<Trial>();
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
                foreach(Trial t in exam.Trials)
                {
                    if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,exam.Patient);
                    if(!exam.FinalizedAt.HasValue || t.FinalizedResult==null) t.FinalizedResult=t.Result.Clone();
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
            foreach (Trial t in e.Trials)
            {
                if (t == null || t.Flow == null || t.Time == null || t.Volume == null || t.Packets == null || t.Flow.Count > 120000 || t.Packets.Count > 150000 || t.Dt <= 0 || double.IsNaN(t.Dt) || double.IsInfinity(t.Dt))
                    throw new InvalidDataException("داده‌های تست معتبر نیست.");
                if ((t.Time.Count != 0 && t.Time.Count != t.Flow.Count) || (t.Volume.Count != 0 && t.Volume.Count != t.Flow.Count))
                    throw new InvalidDataException("تعداد نمونه‌های حجم و زمان با جریان برابر نیست.");
                for (int i = 0; i < t.Flow.Count; i++)
                {
                    if (!Finite(t.Flow[i]) || Math.Abs(t.Flow[i]) > 25 || (t.Time.Count > 0 && (!Finite(t.Time[i]) || t.Time[i] < 0 || t.Time[i] > 3600 || (i > 0 && t.Time[i] < t.Time[i - 1]))) || (t.Volume.Count > 0 && (!Finite(t.Volume[i]) || Math.Abs(t.Volume[i]) > 100)))
                        throw new InvalidDataException("نمونهٔ نامعتبر در فایل وجود دارد.");
                }
                t.Result = t.FinalizedResult!=null ? t.FinalizedResult.Clone() : SpiroAnalysis.Analyze(t, e.Patient);
            }
            e.EnsureManeuverIdentities(); e.ValidateSelection(false); e.ValidateReportSelection(false);
            if(!e.ExaminationDate.HasValue && e.Trials.Count>0) { e.ExaminationDate=e.Trials[0].When.Date; e.ExaminationDateSource="legacy_capture_date"; }
            return e;
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
                ExamSummary best = ExamSummary.Build(e.Trials);
                if (best.Best != null)
                {
                    string prefix = CsvText(e.Patient.FullName) + "," + CsvText(e.Patient.NationalId) + "," + e.Patient.Age.ToString(CultureInfo.InvariantCulture) + "," + e.Patient.Sex + "," + e.Patient.Origin + "," + e.Patient.HeightCm.ToString("R",CultureInfo.InvariantCulture) + "," + e.Patient.WeightKg.ToString("R",CultureInfo.InvariantCulture) + ",BEST," + CsvText(Source(best.BestTrial)) + ",";
                    string[] names = { "FVC", "FEV1", "FEV1/FVC", "PEF", "FEF25-75" };
                    double[] values = { best.Fvc, best.Fev1, best.Ratio, best.Pef, best.Best.Fef2575 };
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

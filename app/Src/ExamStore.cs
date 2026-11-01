using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SpiroLab
{
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
        public static Exam CreateNew(AppSettings settings)
        { return new Exam { CenterName=settings.CenterName,OperatorName=settings.OperatorName,ExaminationDate=DateTime.Today,ExaminationDateSource="windows_local_date" }; }
        public void FinalizeExamination()
        {
            if(FinalizedAt.HasValue) return;
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
                using (FileStream f = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    new XmlSerializer(typeof(Exam)).Serialize(f, exam);
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static Exam Load(string path)
        {
            if (new FileInfo(path).Length > 40 * 1024 * 1024) throw new InvalidDataException("حجم پرونده بیش از حد مجاز است.");
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 40 * 1024 * 1024 };
            Exam e;
            using (XmlReader reader = XmlReader.Create(path, settings)) e = (Exam)new XmlSerializer(typeof(Exam)).Deserialize(reader);
            string problem;
            if (e == null || (e.Version != 1 && e.Version != 2) || e.Patient == null || !e.Patient.IsUsable(out problem) || e.Trials == null || e.Trials.Count > 100)
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
                t.Result = e.FinalizedAt.HasValue && t.FinalizedResult!=null ? t.FinalizedResult.Clone() : SpiroAnalysis.Analyze(t, e.Patient);
            }
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

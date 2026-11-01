using System;
using System.Collections.Generic;
using System.Text;
using System.Xml.Serialization;

namespace SpiroLab
{
    public enum Sex { Male = 0, Female = 1 }
    public enum Origin { Caucasian = 0, Unspecified = 1, Other = 2 }

    // ------------------------------------------------------------------
    // patient
    // ------------------------------------------------------------------
    public sealed class Patient
    {
        public string FirstName = "";
        public string LastName = "";
        public string NationalId = "";
        public string EmployeeCode = "";
        public string Company = "";
        public string JobTitle = "";
        public int Age = 30;
        public Sex Sex = Sex.Male;
        public Origin Origin = Origin.Unspecified;
        public double HeightCm = 175.0;
        public double WeightKg = 75.0;
        public string Note = "";

        public string FullName
        {
            get
            {
                string s = (FirstName + " " + LastName).Trim();
                return s.Length == 0 ? "(بدون نام)" : s;
            }
        }

        public double HeightM { get { return HeightCm / 100.0; } }

        public double Bmi
        {
            get
            {
                double m = HeightM;
                if (m <= 0.5) return 0;
                return WeightKg / (m * m);
            }
        }

        public Patient Clone()
        {
            return (Patient)MemberwiseClone();
        }

        public bool IsUsable(out string problem)
        {
            problem = null;
            if (double.IsNaN(HeightCm) || double.IsInfinity(HeightCm) || HeightCm < 100 || HeightCm > 230) { problem = "قد باید بین ۱۰۰ تا ۲۳۰ سانتی‌متر باشد."; return false; }
            if (Age < 5 || Age > 110) { problem = "سن باید بین ۵ تا ۱۱۰ سال باشد."; return false; }
            if (double.IsNaN(WeightKg) || double.IsInfinity(WeightKg) || WeightKg < 10 || WeightKg > 300) { problem = "وزن باید بین ۱۰ تا ۳۰۰ کیلوگرم باشد."; return false; }
            return true;
        }
    }

    // ------------------------------------------------------------------
    // one blow
    // ------------------------------------------------------------------
    public sealed class Trial
    {
        public List<double> Flow = new List<double>();
        public List<double> Time = new List<double>();
        public List<double> Volume = new List<double>();
        public List<Device.DevicePacket> Packets = new List<Device.DevicePacket>();
        public Device.DeviceResults DeviceValues;
        public string ManeuverId = Guid.NewGuid().ToString("N");
        public int AcquisitionSequence;
        public bool OperatorRejected;
        public string RejectionReason="",RejectionOperator="";
        public DateTime? RejectionTimestamp;
        public bool Completed = true;
        public bool Imported;
        public bool OperatorAccepted;
        public string EndReason = "";
        public string DeviceSerial = "";
        public byte StepType;
        public double Step;
        public double Dt = 0.01;                 // seconds per sample
        public DateTime When = DateTime.Now;
        public bool Simulated;
        [XmlIgnore] public AnalysisResult Result;
        public AnalysisResult FinalizedResult; // Saved attempt analysis; XML name retained for historical compatibility.

        public void Add(double flow)
        {
            Flow.Add(flow);
        }

        public double Duration { get { return Time.Count == Flow.Count && Time.Count > 0 ? Time[Time.Count - 1] : Flow.Count * Dt; } }

        public Trial Clone()
        {
            Trial t = new Trial();
            t.ManeuverId = ManeuverId;
            t.AcquisitionSequence=AcquisitionSequence; t.OperatorRejected=OperatorRejected;
            t.RejectionReason=RejectionReason; t.RejectionOperator=RejectionOperator; t.RejectionTimestamp=RejectionTimestamp;
            t.Flow.AddRange(Flow);
            t.Time.AddRange(Time);
            t.Volume.AddRange(Volume);
            t.Completed = Completed;
            t.Imported = Imported;
            t.OperatorAccepted = OperatorAccepted;
            t.StepType = StepType;
            t.Step = Step;
            t.DeviceSerial = DeviceSerial;
            foreach(Device.DevicePacket packet in Packets) t.Packets.Add(new Device.DevicePacket { Seconds=packet.Seconds,Type=packet.Type,Raw=packet.Raw });
            t.EndReason = EndReason;
            t.DeviceValues = DeviceValues; // raw vendor block is retained locally, never a validated export source
            t.Dt = Dt;
            t.When = When;
            t.Simulated = Simulated;
            t.Result = Result == null ? null : Result.Clone();
            t.FinalizedResult = FinalizedResult == null ? null : FinalizedResult.Clone();
            return t;
        }
    }

    // ------------------------------------------------------------------
    // reference (predicted) values
    // ------------------------------------------------------------------
    public sealed class Reference
    {
        public double Fvc, Fev1, Fev1Fvc, Fev6, Pef, Fef2575, Fef25, Fef50, Fef75;
        public double RatioLln, FvcLln, Fev1Lln;
        public bool Applicable;
        public Reference Clone() { return (Reference)MemberwiseClone(); }
    }

    // ------------------------------------------------------------------
    // measured indices + interpretation
    // ------------------------------------------------------------------
    public enum Pattern { Normal, Obstructive, Restrictive, Mixed, Invalid }

    public sealed class AnalysisResult
    {
        public bool Valid;
        public string InvalidReason = "";

        public double Fvc, Fev1, Fev6, Fev1Fvc, Fev1Fev6;
        public double Pef, PefTime, Fef25, Fef50, Fef75, Fef2575;
        public double Fet, Fif, Bev, Fev3;
        public double TimeZero;
        public bool BevAvailable;
        public string BevSourceAdequacy = "not_assessed";
        public double VolumeMax;
        public int SampleCount;

        public Reference Ref;
        public Pattern Pattern = Pattern.Invalid;
        public string Interpretation = "";

        // quality
        public bool FetOk;
        public bool PeftOk;
        public bool BevOk;
        public bool PlateauOk;
        public bool Cough;
        public List<string> Warnings = new List<string>();

        public double PctFvc { get { return Ref != null && Ref.Fvc > 0 ? Fvc / Ref.Fvc * 100.0 : 0; } }
        public double PctFev1 { get { return Ref != null && Ref.Fev1 > 0 ? Fev1 / Ref.Fev1 * 100.0 : 0; } }
        public double PctPef { get { return Ref != null && Ref.Pef > 0 ? Pef / Ref.Pef * 100.0 : 0; } }
        public double PctFef2575 { get { return Ref != null && Ref.Fef2575 > 0 ? Fef2575 / Ref.Fef2575 * 100.0 : 0; } }

        public AnalysisResult Clone()
        {
            AnalysisResult result=(AnalysisResult)MemberwiseClone();
            result.Warnings=new List<string>(Warnings);
            if(Ref!=null) result.Ref=Ref.Clone();
            return result;
        }

        public bool IsAcceptable
        {
            get { return Valid && FetOk && BevOk && !Cough; }
        }
    }

    // ------------------------------------------------------------------
    // jalali date (for the printed report)
    // ------------------------------------------------------------------
    public static class PersianDate
    {
        private static readonly int[] GDaysInMonth = { 0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        private static readonly int[] JDaysInMonth = { 0, 31, 31, 31, 31, 31, 31, 30, 30, 30, 30, 30, 29 };

        public static string Format(DateTime dt)
        {
            int gy = dt.Year, gm = dt.Month, gd = dt.Day;
            int[] gdm = (int[])GDaysInMonth.Clone();
            if (DateTime.IsLeapYear(gy)) gdm[2] = 29;

            int gy2 = gy - 1600, gm2 = gm - 1, gd2 = gd - 1;
            int gDayNo = 365 * gy2 + (gy2 + 3) / 4 - (gy2 + 99) / 100 + (gy2 + 399) / 400;
            for (int i = 0; i < gm2; i++) gDayNo += gdm[i + 1];
            gDayNo += gd2;

            int jDayNo = gDayNo - 79;
            int jNp = jDayNo / 12053;
            jDayNo = jDayNo % 12053;

            int jy = 979 + 33 * jNp + 4 * (jDayNo / 1461);
            jDayNo %= 1461;
            if (jDayNo >= 366)
            {
                jy += (jDayNo - 1) / 365;
                jDayNo = (jDayNo - 1) % 365;
            }

            int jm = 0;
            for (int i = 1; i <= 12; i++)
            {
                int len = JDaysInMonth[i];
                if (jDayNo < len) { jm = i; break; }
                jDayNo -= len;
            }
            if (jm == 0) jm = 12;

            return string.Format("{0:0000}/{1:00}/{2:00}", jy, jm, jDayNo + 1);
        }

        public static string Now { get { return Format(DateTime.Now); } }
    }

    public static class Fmt
    {
        public static string N(double v, int digits)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
            return v.ToString("F" + digits, System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Pct(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v) || v <= 0) return "-";
            return v.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        public static double Parse(string s, double fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            s = NormalizeNumber(s);
            double v;
            if (double.TryParse(s, System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out v))
                return double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
            return fallback;
        }

        public static string NormalizeNumber(string s)
        {
            StringBuilder b = new StringBuilder();
            foreach (char c in (s ?? "").Trim())
            {
                if (c >= '۰' && c <= '۹') b.Append((char)('0' + c - '۰'));
                else if (c >= '٠' && c <= '٩') b.Append((char)('0' + c - '٠'));
                else b.Append(c == '٫' || c == ',' ? '.' : c);
            }
            return b.ToString();
        }
    }
}

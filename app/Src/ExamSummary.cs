using System;
using System.Collections.Generic;

namespace SpiroLab
{
    // Session-level facade. The scientific aggregate is SessionBestValues over
    // ALL eligible maneuvers; Trials is only the displayed/report subset.
    public sealed class ExamSummary
    {
        public readonly List<Trial> Trials = new List<Trial>();
        public readonly List<Trial> Candidates = new List<Trial>();
        public SessionBestValues BestValues;
        public AnalysisResult Best;
        public Trial BestTrial;
        public bool OperatorReviewed;
        public int Excluded;
        public double Fvc { get { return BestValues!=null && BestValues.Fvc.Available?BestValues.Fvc.Value.Value:double.NaN; } }
        public double Fev1 { get { return BestValues!=null && BestValues.Fev1.Available?BestValues.Fev1.Value.Value:double.NaN; } }
        public double Pef { get { return BestValues!=null && BestValues.Pef.Available?BestValues.Pef.Value.Value:double.NaN; } }
        public double Fef2575 { get { return BestValues!=null && BestValues.Fef2575.Available?BestValues.Fef2575.Value.Value:double.NaN; } }
        public double Ratio { get { return BestValues!=null && BestValues.Fev1Fvc.Available?BestValues.Fev1Fvc.Value.Value:double.NaN; } }
        public bool FvcAvailable { get { return BestValues!=null && BestValues.Fvc.Available; } }
        public bool Fev1Available { get { return BestValues!=null && BestValues.Fev1.Available; } }
        public bool PefAvailable { get { return BestValues!=null && BestValues.Pef.Available; } }
        public bool Fef2575Available { get { return BestValues!=null && BestValues.Fef2575.Available; } }
        public string FvcSourceManeuverId { get { return BestValues==null?null:BestValues.Fvc.SourceManeuverId; } }
        public string Fev1SourceManeuverId { get { return BestValues==null?null:BestValues.Fev1.SourceManeuverId; } }
        public string PefSourceManeuverId { get { return BestValues==null?null:BestValues.Pef.SourceManeuverId; } }
        public string Fef2575SourceManeuverId { get { return BestValues==null?null:BestValues.Fef2575.SourceManeuverId; } }

        public static bool CanReport(Trial t)
        {
            if(t==null || t.OperatorRejected) return false;
            ManeuverAssessment a=ManeuverAssessment.Assess(t);
            return a.TechnicalValid && a.FvcUsable;
        }
        public static ExamSummary Build(IList<Trial> trials) { return Build(trials,true); }
        internal static ExamSummary Build(IList<Trial> trials,bool qualityRanking)
        {
            ExamSummary s=new ExamSummary();
            List<Trial> source=new List<Trial>();
            if(trials!=null) foreach(Trial t in trials) if(t!=null)
            {
                source.Add(t);
                ManeuverAssessment a=ManeuverAssessment.Assess(t);
                if(!t.OperatorRejected && a.TechnicalValid && a.FvcUsable) {s.Candidates.Add(t); if(t.OperatorAccepted) s.OperatorReviewed=true;}
            }
            Dictionary<Trial,int> originalOrder=new Dictionary<Trial,int>();
            for(int i=0;i<s.Candidates.Count;i++) if(!originalOrder.ContainsKey(s.Candidates[i])) originalOrder.Add(s.Candidates[i],i);
            s.Candidates.Sort(delegate(Trial a,Trial b)
            {
                int score=qualityRanking?ManeuverAssessment.Compare(a,b):CompareSum(a,b);
                return score!=0?score:originalOrder[a].CompareTo(originalOrder[b]);
            });
            s.BestValues=SessionBestValues.Build(source);
            s.BestTrial=s.BestValues.ReportTrial;
            s.Best=s.BestTrial==null?null:s.BestTrial.Result;
            s.Excluded=source.Count-s.Candidates.Count;
            foreach(Trial t in s.Candidates)
            {
                if(s.Trials.Count==3) break;
                s.Trials.Add(t);
            }
            s.Trials.Sort(delegate(Trial a,Trial b) { return a.AcquisitionSequence.CompareTo(b.AcquisitionSequence); });
            return s;
        }

        static int CompareSum(Trial a,Trial b)
        {
            double x=Value(a,Metric.Fvc)+Value(a,Metric.Fev1),y=Value(b,Metric.Fvc)+Value(b,Metric.Fev1);
            return y.CompareTo(x);
        }
        static double Value(Trial t,string metric) { MeasuredValue v=t.Measurements.Get(metric); return v.Available?v.Value.Value:0; }

        // Display selection is not a clinical/eligibility operation and never
        // re-runs the aggregate. It only changes which real curves are plotted and
        // which per-maneuver columns appear.
        public void SetDisplay(IEnumerable<Trial> display)
        {
            List<Trial> chosen=new List<Trial>();
            if(display!=null) foreach(Trial t in display)
            {
                if(t==null || chosen.Contains(t) || chosen.Count>=3) continue;
                if(!Candidates.Contains(t)) continue;
                chosen.Add(t);
            }
            if(chosen.Count==0 && BestValues!=null && BestValues.ReportTrial!=null) chosen.Add(BestValues.ReportTrial);
            chosen.Sort(delegate(Trial a,Trial b) { return a.AcquisitionSequence.CompareTo(b.AcquisitionSequence); });
            Trials.Clear(); Trials.AddRange(chosen);
        }

        public string Interpretation
        {
            get
            {
                if(Best==null || BestValues==null || !FvcAvailable) return "مانور کامل و معتبر برای گزارش ثبت نشده است.";
                Reference reference=BestValues.Reference;
                if(reference==null || !reference.Applicable) return "مشخصات خارج از دامنهٔ مرجع ERS است؛ تفسیر مرجع ارائه نمی‌شود.";
                bool ratioLow=Ratio<reference.RatioLln, fvcLow=Fvc<reference.FvcLln;
                if(ratioLow && fvcLow) return "احتمال انسداد همراه با کاهش FVC.";
                if(ratioLow) return "احتمال الگوی انسدادی.";
                if(fvcLow) return "احتمال الگوی محدودیتی.";
                return "در محدودهٔ مرجع ERS.";
            }
        }
        public string Repeatability
        {
            get
            {
                List<double> fvc=new List<double>(),fev=new List<double>();
                foreach(Trial t in BestValues==null?Candidates:BestValues.Eligible)
                {
                    MeasuredValue a=t.Measurements.Fvc,b=t.Measurements.Fev1;
                    if(a.Available) fvc.Add(a.Value.Value);
                    if(b.Available) fev.Add(b.Value.Value);
                }
                if(fvc.Count<2 && fev.Count<2) return "";
                string fvcText=fvc.Count>=2?Fmt.N((Max(fvc)-Second(fvc))*1000,0)+" mL":"—";
                string fevText=fev.Count>=2?Fmt.N((Max(fev)-Second(fev))*1000,0)+" mL":"—";
                return "ΔFVC: "+fvcText+"    |    ΔFEV1: "+fevText;
            }
        }
        static double Max(List<double> values) { double best=double.MinValue; foreach(double v in values) if(v>best) best=v; return best; }
        static double Second(List<double> values) { double best=double.MinValue,second=double.MinValue; foreach(double v in values) { if(v>best) {second=best;best=v;} else if(v>second) second=v; } return second; }
    }
}

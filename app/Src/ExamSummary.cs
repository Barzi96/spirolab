using System;
using System.Collections.Generic;

namespace SpiroLab
{
    public sealed class ExamSummary
    {
        public readonly List<Trial> Trials = new List<Trial>();
        public readonly List<Trial> Candidates = new List<Trial>();
        public AnalysisResult Best;
        public Trial BestTrial;
        public double Fvc, Fev1, Pef;
        public double Ratio { get { return Fvc > 0 ? Fev1 / Fvc * 100 : 0; } }
        public bool OperatorReviewed;
        public int Excluded;
        public static bool CanReport(Trial t) { return t!=null && t.Completed && !t.OperatorRejected && t.Result!=null && t.Result.Valid; }
        public static ExamSummary Build(IList<Trial> trials)
        {
            ExamSummary s = new ExamSummary();
            foreach (Trial t in trials)
                if (CanReport(t))
                { s.Candidates.Add(t); if (t.OperatorAccepted) s.OperatorReviewed = true; }

            Dictionary<Trial,int> originalOrder=new Dictionary<Trial,int>();
            for(int i=0;i<s.Candidates.Count;i++) if(!originalOrder.ContainsKey(s.Candidates[i])) originalOrder.Add(s.Candidates[i],i);
            s.Candidates.Sort(delegate(Trial a, Trial b) { int score=(b.Result.Fvc + b.Result.Fev1).CompareTo(a.Result.Fvc + a.Result.Fev1); return score!=0?score:originalOrder[a].CompareTo(originalOrder[b]); });
            foreach (Trial t in s.Candidates)
            { s.Fvc = Math.Max(s.Fvc,t.Result.Fvc); s.Fev1 = Math.Max(s.Fev1,t.Result.Fev1); s.Pef = Math.Max(s.Pef,t.Result.Pef); }
            if (s.Candidates.Count > 0)
            {
                s.BestTrial = s.Candidates[0]; s.Best = s.BestTrial.Result;
                s.Trials.AddRange(s.Candidates.GetRange(0,Math.Min(3,s.Candidates.Count)));
                s.Trials.Sort(delegate(Trial a,Trial b) { return OriginalIndex(trials,a).CompareTo(OriginalIndex(trials,b)); });
            }
            s.Excluded = trials.Count - s.Candidates.Count;
            return s;
        }
        private static int OriginalIndex(IList<Trial> list,Trial t) { for(int i=0;i<list.Count;i++) if(object.ReferenceEquals(list[i],t)) return i; return -1; }
        public string Interpretation
        {
            get
            {
                if (Best == null) return "مانور کامل و معتبر برای گزارش ثبت نشده است.";
                if (!Best.Ref.Applicable) return "مشخصات خارج از دامنهٔ مرجع ERS است؛ تفسیر مرجع ارائه نمی‌شود.";
                bool ratioLow=Ratio<Best.Ref.RatioLln, fvcLow=Fvc<Best.Ref.FvcLln;
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
                if(Candidates.Count<2) return "";
                List<double> fvc=new List<double>(),fev=new List<double>();
                foreach(Trial t in Candidates) { fvc.Add(t.Result.Fvc); fev.Add(t.Result.Fev1); }
                fvc.Sort(); fev.Sort(); int n=fvc.Count;
                return "ΔFVC: "+Fmt.N((fvc[n-1]-fvc[n-2])*1000,0)+" mL    |    ΔFEV1: "+Fmt.N((fev[n-1]-fev[n-2])*1000,0)+" mL";
            }
        }
    }

}

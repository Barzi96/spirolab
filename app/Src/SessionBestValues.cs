using System;
using System.Collections.Generic;

namespace SpiroLab
{
    // ONE authoritative session-level aggregate. Every consumer (UI, JPG, JSON,
    // HLine and read-back reconciliation) must use this same object.
    public sealed class SessionBestValues
    {
        public MeasuredValue Fvc {get;private set;}
        public MeasuredValue Fev1 {get;private set;}
        public MeasuredValue Fev1Fvc {get;private set;}
        public MeasuredValue Pef {get;private set;}
        public MeasuredValue Fef2575 {get;private set;}
        public MeasuredValue Fet {get;private set;}
        public Reference Reference {get;private set;}
        public Trial ReportTrial {get;private set;}
        public IList<Trial> Eligible {get;private set;}
        public bool QualityLimited {get;private set;}
        public IList<string> QualityLimitations {get;private set;}

        private SessionBestValues()
        {
            Fvc=Unavailable(Metric.Fvc,"session_not_built");
            Fev1=Unavailable(Metric.Fev1,"session_not_built");
            Fev1Fvc=Unavailable(Metric.Fev1Fvc,"session_not_built");
            Pef=Unavailable(Metric.Pef,"session_not_built");
            Fef2575=Unavailable(Metric.Fef2575,"session_not_built");
            Fet=Unavailable(Metric.Fet,"session_not_built");
            Eligible=new List<Trial>();
            QualityLimitations=new List<string>();
        }

        public bool HasCoreValues { get { return Fvc.Available && Fev1.Available && Fev1Fvc.Available && Pef.Available; } }

        public MeasuredValue Get(string name)
        {
            switch(name)
            {
                case Metric.Fvc:return Fvc;
                case Metric.Fev1:return Fev1;
                case Metric.Fev1Fvc:return Fev1Fvc;
                case Metric.Pef:return Pef;
                case Metric.Fef2575:return Fef2575;
                case Metric.Fet:return Fet;
                default:return Unavailable(name,"unknown_metric");
            }
        }

        public IEnumerable<string> SourceManeuverIds
        {
            get
            {
                List<string> ids=new List<string>();
                foreach(MeasuredValue m in new MeasuredValue[] {Fvc,Fev1,Pef,Fef2575,Fet})
                    if(m.Available && !string.IsNullOrEmpty(m.SourceManeuverId) && !ids.Contains(m.SourceManeuverId)) ids.Add(m.SourceManeuverId);
                return ids;
            }
        }

        public static SessionBestValues Build(IList<Trial> trials)
        {
            SessionBestValues b=new SessionBestValues();
            List<Candidate> all=new List<Candidate>();
            if(trials!=null) foreach(Trial t in trials)
            {
                if(t==null) continue;
                ManeuverAssessment a=ManeuverAssessment.Assess(t);
                if(!a.TechnicalValid || t.OperatorRejected) continue;
                Candidate c=new Candidate {Trial=t,Assessment=a,Measurements=t.Measurements};
                if(c.Measurements.Fvc.Available || c.Measurements.Fev1.Available || c.Measurements.Pef.Available) all.Add(c);
            }
            b.Eligible=new List<Trial>();
            foreach(Candidate c in all) b.Eligible.Add(c.Trial);

            b.Reference=ReferenceFor(all);
            b.Fvc=PickMax(b,all,Metric.Fvc,delegate(Candidate c){return c.Assessment.FvcUsable;},delegate(Candidate c){return c.Assessment.FvcAcceptable;});
            b.Fev1=PickMax(b,all,Metric.Fev1,delegate(Candidate c){return c.Assessment.Fev1Usable;},delegate(Candidate c){return c.Assessment.Fev1Acceptable;});
            b.Pef=PickMax(b,all,Metric.Pef,delegate(Candidate c){return (c.Assessment.FvcUsable||c.Assessment.Fev1Usable) && c.Measurements.Pef.Available;},delegate(Candidate c){return c.Assessment.QualityScreenPassed;});
            b.Fet=PickMax(b,all,Metric.Fet,delegate(Candidate c){return c.Assessment.TechnicalValid && c.Measurements.Fet.Available;},delegate(Candidate c){return c.Assessment.EndScreenPassed;});
            b.Fef2575=PickFef2575(b,all);

            if(b.Fvc.Available && b.Fev1.Available && b.Fvc.Value.Value>0 && b.Fev1.Value.Value<=b.Fvc.Value.Value+.05)
            {
                b.Fev1Fvc=new MeasuredValue {Name=Metric.Fev1Fvc,Unit="%",Value=b.Fev1.Value.Value/b.Fvc.Value.Value*100.0,Source=MeasurementSource.Derived,ValidationStatus="derived_unverified",Method="best_fev1_div_best_fvc_independent_maneuvers",Resolution=0.01,MissingReason=""};
                if(b.Reference!=null && b.Reference.Fev1Fvc>0) {b.Fev1Fvc.Predicted=b.Reference.Fev1Fvc;b.Fev1Fvc.PercentPredicted=b.Fev1Fvc.Value.Value/b.Reference.Fev1Fvc*100.0;}
                if(b.Fev1.SourceManeuverId==b.Fvc.SourceManeuverId) b.Fev1Fvc.SourceManeuverId=b.Fev1.SourceManeuverId;
                else b.Fev1Fvc.SourceManeuverId=b.Fev1.SourceManeuverId+","+b.Fvc.SourceManeuverId;
            }
            else b.Fev1Fvc=Unavailable(Metric.Fev1Fvc,"best_fvc_or_fev1_unavailable");

            b.ReportTrial=PickReportTrial(all);
            b.QualityLimited=b.QualityLimitations.Count>0;
            return b;
        }

        static Reference ReferenceFor(List<Candidate> all)
        {
            foreach(Candidate c in all) if(c.Trial.Result!=null && c.Trial.Result.Ref!=null) return c.Trial.Result.Ref;
            return null;
        }

        static MeasuredValue PickMax(SessionBestValues session,List<Candidate> all,string metric,Func<Candidate,bool> usable,Func<Candidate,bool> acceptable)
        {
            List<Candidate> candidates=new List<Candidate>();
            foreach(Candidate c in all) if(usable(c) && c.Measurements.Get(metric).Available) candidates.Add(c);
            if(candidates.Count==0) {session.QualityLimitations.Add(metric+":no_eligible_maneuver");return Unavailable(metric,"no_eligible_maneuver");}
            List<Candidate> acceptableCandidates=new List<Candidate>();
            foreach(Candidate c in candidates) if(acceptable(c)) acceptableCandidates.Add(c);
            List<Candidate> pool=acceptableCandidates.Count>0?acceptableCandidates:candidates;
            if(acceptableCandidates.Count==0 && candidates.Count>0) session.QualityLimitations.Add(metric+":reported_from_usable_not_quality_accepted_maneuver");
            Candidate selected=pool[0];
            foreach(Candidate c in pool)
            {
                double value=c.Measurements.Get(metric).Value.Value;
                double best=selected.Measurements.Get(metric).Value.Value;
                if(value>best+1e-12 || (Math.Abs(value-best)<=1e-12 && c.Trial.AcquisitionSequence<selected.Trial.AcquisitionSequence)) selected=c;
            }
            MeasuredValue result=selected.Measurements.Get(metric).Clone();
            if(session.Reference!=null) ApplyPrediction(result,session.Reference);
            return result;
        }

        static MeasuredValue PickFef2575(SessionBestValues session,List<Candidate> all)
        {
            List<Candidate> candidates=new List<Candidate>();
            foreach(Candidate c in all)
            {
                MeasuredValue fvc=c.Measurements.Fvc,fev1=c.Measurements.Fev1,fef=c.Measurements.Fef2575;
                if(c.Assessment.FvcUsable && c.Assessment.Fev1Usable && fvc.Available && fev1.Available && fef.Available) candidates.Add(c);
            }
            if(candidates.Count==0) {session.QualityLimitations.Add(Metric.Fef2575+":no_eligible_maneuver_with_adequate_curve");return Unavailable(Metric.Fef2575,"no_eligible_maneuver_with_adequate_curve");}
            List<Candidate> acceptable=new List<Candidate>();
            foreach(Candidate c in candidates) if(c.Assessment.FvcAcceptable && c.Assessment.Fev1Acceptable) acceptable.Add(c);
            List<Candidate> pool=acceptable.Count>0?acceptable:candidates;
            if(acceptable.Count==0) session.QualityLimitations.Add(Metric.Fef2575+":reported_from_usable_not_quality_accepted_maneuver");
            Candidate selected=pool[0];
            foreach(Candidate c in pool)
            {
                double value=Sum(c),best=Sum(selected);
                if(value>best+1e-12 || (Math.Abs(value-best)<=1e-12 && c.Trial.AcquisitionSequence<selected.Trial.AcquisitionSequence)) selected=c;
            }
            MeasuredValue result=selected.Measurements.Fef2575.Clone();
            result.Method="from_maneuver_with_largest_fvc_plus_fev1";
            if(session.Reference!=null) ApplyPrediction(result,session.Reference);
            return result;
        }

        static double Sum(Candidate c)
        {
            return c.Measurements.Fvc.Value.Value+c.Measurements.Fev1.Value.Value;
        }

        static Trial PickReportTrial(List<Candidate> all)
        {
            if(all.Count==0) return null;
            Candidate selected=all[0];
            foreach(Candidate c in all)
            {
                int score=ManeuverAssessment.Compare(c.Trial,selected.Trial);
                if(score<0 || (score==0 && c.Trial.AcquisitionSequence<selected.Trial.AcquisitionSequence)) selected=c;
            }
            return selected.Trial;
        }

        static void ApplyPrediction(MeasuredValue value,Reference reference)
        {
            double predicted=0;
            switch(value.Name)
            {
                case Metric.Fvc:predicted=reference.Fvc;break;
                case Metric.Fev1:predicted=reference.Fev1;break;
                case Metric.Pef:predicted=reference.Pef;break;
                case Metric.Fef2575:predicted=reference.Fef2575;break;
            }
            if(predicted>0) {value.Predicted=predicted;value.PercentPredicted=value.Value.Value/predicted*100.0;}
        }

        static MeasuredValue Unavailable(string name,string reason)
        {
            return new MeasuredValue {Name=name??"",Unit=Metric.Unit(name),Value=null,Source=MeasurementSource.Unavailable,ValidationStatus="unavailable",MissingReason=reason??"unavailable",Method=""};
        }

        sealed class Candidate
        {
            public Trial Trial;
            public ManeuverAssessment Assessment;
            public ManeuverMeasurementSet Measurements;
        }
    }
}

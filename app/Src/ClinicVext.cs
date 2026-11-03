using System;
using System.Collections.Generic;
namespace SpiroLab
{
    // Clinic-specific data-entry convention for the HLine `vext` field: the highest
    // EVol strictly below 200 mL among the real maneuvers of this examination.
    // This is not an ATS/ERS quality threshold and never gates or changes the
    // scientific maneuver assessment or the operator-selected site maneuver.
    public sealed class VextSelection
    {
        public const double UpperLimitMl=200.0;
        public const string NoEligibleProblem="هیچ مانور واقعی با EVol کمتر از ۲۰۰ میلی‌لیتر در این معاینه محاسبه نشده است؛ VEXT ساخته نمی‌شود.";
        public const string NoEligibleSelectedProblem="هیچ مانور واقعیِ انتخاب‌شده‌ای با EVol مثبت و کمتر از ۲۰۰ میلی‌لیتر در مجموعهٔ نهایی این معاینه وجود ندارد؛ VEXT ساخته نمی‌شود.";
        public const string InvalidSelectedSetProblem="مجموعهٔ نهایی انتخاب‌شده برای محاسبهٔ VEXT معتبر نیست؛ VEXT ساخته نمی‌شود.";
        public double ValueMl;
        public string ManeuverId="";
        public int AcquisitionSequence;
        public string Method="max_evol_strictly_below_200ml";
        public string Problem="";
        public bool Available { get { return !string.IsNullOrEmpty(ManeuverId); } }
    }
    public static class ClinicVext
    {
        // Pure rule over EVol values in mL; null/NaN/infinite and 0<=v>=200 are excluded.
        public static double? SelectValue(IEnumerable<double?> evolMl)
        {
            double? best=null;
            if(evolMl==null) return null;
            foreach(double? raw in evolMl)
            {
                if(!raw.HasValue) continue;
                double v=raw.Value;
                if(double.IsNaN(v) || double.IsInfinity(v) || v<=0 || v>=VextSelection.UpperLimitMl) continue;
                if(!best.HasValue || v>best.Value) best=v;
            }
            return best;
        }
        // Only real (USB-live) maneuvers; simulation and file replay are never sent.
        // Historical workflow records keep this all-active-maneuver scope.
        public static VextSelection Select(IEnumerable<Trial> trials)
        {
            VextSelection result=new VextSelection();
            if(trials!=null) foreach(Trial t in trials)
            {
                if(t==null || t.Simulated || t.Imported || t.Result==null || !t.Result.BevAvailable) continue;
                double? value=SelectValue(new double?[] { t.Result.Bev*1000.0 });
                if(!value.HasValue) continue;
                if(!result.Available || value.Value>result.ValueMl)
                {
                    result.ValueMl=value.Value; result.ManeuverId=t.ManeuverId; result.AcquisitionSequence=t.AcquisitionSequence;
                }
            }
            if(!result.Available) result.Problem=VextSelection.NoEligibleProblem;
            return result;
        }
        // Single source for every workflow-4 final VEXT consumer. The selected
        // ReportManeuverIds are authoritative; no other active maneuver, discarded
        // maneuver, rejection, simulation or replay can contribute. Workflow <4
        // deliberately falls back to the historical all-active rule.
        public static VextSelection SelectForExam(Exam exam)
        {
            if(exam==null)
            {
                VextSelection missing=new VextSelection();
                missing.Problem=VextSelection.InvalidSelectedSetProblem;
                return missing;
            }
            if(exam.SelectionWorkflowVersion<4) return Select(exam.Trials);
            return SelectSelected(exam.Trials,exam.ReportManeuverIds);
        }
        public static VextSelection SelectSelected(IList<Trial> trials,IList<string> selectedIds)
        {
            VextSelection result=new VextSelection();
            result.Method="max_evol_strictly_below_200ml_selected_max_three";
            if(selectedIds==null || selectedIds.Count==0)
            {
                result.Problem=VextSelection.NoEligibleSelectedProblem;
                return result;
            }
            if(selectedIds.Count>3)
            {
                result.Problem=VextSelection.InvalidSelectedSetProblem;
                return result;
            }
            Dictionary<string,Trial> active=new Dictionary<string,Trial>(StringComparer.Ordinal);
            if(trials!=null) foreach(Trial t in trials)
            {
                if(t==null || string.IsNullOrEmpty(t.ManeuverId) || active.ContainsKey(t.ManeuverId)) continue;
                active.Add(t.ManeuverId,t);
            }
            HashSet<string> seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(string id in selectedIds)
            {
                if(string.IsNullOrEmpty(id) || !seen.Add(id) || !active.ContainsKey(id))
                {
                    result.Problem=VextSelection.InvalidSelectedSetProblem;
                    return result;
                }
            }
            foreach(string id in selectedIds)
            {
                Trial t=active[id];
                if(t.Simulated || t.Imported || t.OperatorRejected) continue;
                if(t.Result==null || !t.Result.BevAvailable) continue;
                double? value=SelectValue(new double?[] { t.Result.Bev*1000.0 });
                if(!value.HasValue) continue;
                if(!result.Available || value.Value>result.ValueMl)
                {
                    result.ValueMl=value.Value; result.ManeuverId=t.ManeuverId; result.AcquisitionSequence=t.AcquisitionSequence;
                }
            }
            if(!result.Available) result.Problem=VextSelection.NoEligibleSelectedProblem;
            return result;
        }
    }
}

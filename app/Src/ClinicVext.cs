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
    }
}

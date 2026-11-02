using System;
namespace SpiroLab
{
    public sealed class ManeuverAssessment
    {
        public bool TechnicalValid, Fev1Usable, FvcUsable, StartScreenPassed, EndScreenPassed, QualityScreenPassed;
        public bool FvcAcceptable,Fev1Acceptable;
        public string Problem="",Status="";
        public static ManeuverAssessment Assess(Trial t)
        {
            ManeuverAssessment a=new ManeuverAssessment();
            if(t==null) { a.Problem="مانوری انتخاب نشده است."; return a; }
            AnalysisResult r=t.Result;
            if(!string.IsNullOrEmpty(t.CaptureError) || (!t.Completed && !t.OperatorStopped)) a.Problem="پایان فنی ثبت تأیید نشده است: "+t.EndReason+" "+t.CaptureError;
            else if(t.Flow.Count<5 || (t.Time.Count!=0 && t.Time.Count!=t.Flow.Count) || (t.Volume.Count!=0 && t.Volume.Count!=t.Flow.Count) || !Positive(t.Dt)) a.Problem="نمونه‌ها یا زمان‌بندی ثبت معتبر نیست.";
            else
            {
                a.TechnicalValid=true;
                for(int i=0;i<t.Flow.Count;i++) if(!Finite(t.Flow[i]) || Math.Abs(t.Flow[i])>25 || (t.Time.Count>0 && (!Finite(t.Time[i]) || t.Time[i]<0 || (i>0 && t.Time[i]<=t.Time[i-1]))) || (t.Volume.Count>0 && !Finite(t.Volume[i]))) { a.TechnicalValid=false; a.Problem="نمونهٔ خراب یا زمان‌بندی نامعتبر ثبت شده است."; break; }
            }
            if(a.TechnicalValid)
            {
                ManeuverMeasurementSet m=t.Measurements;
                double fet=m.Fet.Available?m.Fet.Value.Value:(r==null?0:r.Fet);
                bool legacyNumeric=r!=null && !r.AvailabilityMetadata && r.Valid && !r.Fev1Available && !r.FetAvailable && r.Fev1>0;
                a.FvcUsable=m.Fvc.Available && m.Fvc.Value.Value>0;
                a.StartScreenPassed=r!=null && r.BevAvailable && r.BevOk;
                a.EndScreenPassed=r!=null && (r.PlateauOk || r.Fet>=15);
                a.Fev1Usable=m.Fev1.Available && m.Fev1.Value.Value>0 && (!m.Fvc.Available || m.Fev1.Value.Value<=m.Fvc.Value.Value+.05) && (fet>=1 || legacyNumeric) && (r==null || !r.Cough);
                // Separate parameter acceptance: an inadequate FEV1 must not erase a
                // usable FVC, and an inadequate FVC must not erase a usable FEV1.
                a.FvcAcceptable=a.FvcUsable && a.StartScreenPassed && a.EndScreenPassed;
                a.Fev1Acceptable=a.Fev1Usable && a.StartScreenPassed && a.EndScreenPassed;
                // Leaks/glottic closure and device timing remain unverified; the
                // screen is not presented as a MIR firmware grade.
                a.QualityScreenPassed=a.FvcAcceptable && a.Fev1Acceptable;
                a.Status=!a.FvcUsable&&!a.Fev1Usable?"داده ناکافی":a.QualityScreenPassed?"نیازمند تأیید کیفیت":"محدودیت کیفیت";
                if(!a.FvcUsable && !a.Fev1Usable) a.Problem="FVC و FEV1 معتبر در دسترس نیست.";
                else if(!a.FvcUsable) a.Problem="FVC معتبر در دسترس نیست؛ FEV1 جداگانه ارزیابی می‌شود.";
                else if(!a.Fev1Usable) a.Problem="FEV1 قابل اتکا نیست؛ مدت بازدم یا سرفهٔ ثانیهٔ اول را بررسی کنید.";
            }
            else a.Status="خطای ثبت";
            if(t.OperatorRejected) { a.Status="ردشده"; a.Problem="مانور توسط کارشناس رد شده است."; a.FvcAcceptable=a.Fev1Acceptable=false; }
            return a;
        }
        public static int Compare(Trial a,Trial b)
        {
            ManeuverAssessment x=Assess(a),y=Assess(b);
            int rank=(y.QualityScreenPassed?2:y.Fev1Usable?1:0).CompareTo(x.QualityScreenPassed?2:x.Fev1Usable?1:0);
            return rank!=0?rank:(Sum(b)).CompareTo(Sum(a));
        }
        static double Sum(Trial t)
        {
            ManeuverMeasurementSet m=t.Measurements;
            return (m.Fvc.Available?m.Fvc.Value.Value:0)+(m.Fev1.Available?m.Fev1.Value.Value:0);
        }
        static bool Positive(double v) { return Finite(v) && v>0; }
        static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
    }
}

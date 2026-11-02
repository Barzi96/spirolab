using System;

namespace SpiroLab
{
    public static class SpiroAnalysis
    {
        public static AnalysisResult Analyze(Trial trial, Patient p)
        {
            AnalysisResult r = new AnalysisResult();
            r.Ref = Ers93.Compute(p);
            r.SampleCount = trial.Flow.Count;
            Expiration s = Expiration.From(trial);
            if (s.Count < 5 || s.Volume[s.Count - 1] < 0.3)
            { r.InvalidReason = "بازدم کافی ثبت نشد؛ اتصال دهانی و روش انجام تست را بررسی کنید."; return r; }
            r.Fvc = r.VolumeMax = s.Volume[s.Count - 1];
            r.Fet = s.Time[s.Count - 1];
            r.Fev1 = s.VolumeAt(1); r.Fev3 = s.VolumeAt(3); r.Fev6 = s.VolumeAt(6);
            r.Fev1Fvc = r.Fev1 / r.Fvc * 100;
            r.Fev1Fev6 = r.Fev6 > 0 ? r.Fev1 / r.Fev6 * 100 : 0;
            for (int i = 0; i < s.Count; i++)
            {
                if (s.Flow[i] > r.Pef) { r.Pef = s.Flow[i]; r.PefTime = s.Time[i]; }
                // A signal flag only: the operator must inspect cough and leaks.
                if (i > 1 && s.Time[i] <= 1 && s.Flow[i - 1] - s.Flow[i] > Math.Max(1.5, r.Pef * 0.4)) r.Cough = true;
            }
            r.FvcAvailable = Finite(r.Fvc) && r.Fvc > 0;
            r.FetAvailable = Finite(r.Fet) && r.Fet > 0;
            r.Fev1Available = r.FvcAvailable && Finite(r.Fev1) && r.Fev1 > 0 && r.Fev1 <= r.Fvc + .001 && r.Fet >= 1 - 1e-9;
            r.Fev3Available = r.Fev1Available && r.Fet >= 3 - 1e-9;
            r.Fev6Available = r.Fev1Available && r.Fet >= 6 - 1e-9;
            r.Fev1FvcAvailable = r.FvcAvailable && r.Fev1Available;
            r.PefAvailable = Finite(r.Pef) && r.Pef > 0;
            r.CurveUsable = UsableCurve(s, out r.CurveProblem);
            if (r.CurveUsable)
            {
                double span = s.TimeAtVolume(r.Fvc * .75) - s.TimeAtVolume(r.Fvc * .25);
                r.Fef25 = s.FlowAtVolume(r.Fvc * .25); r.Fef50 = s.FlowAtVolume(r.Fvc * .5); r.Fef75 = s.FlowAtVolume(r.Fvc * .75);
                r.Fef25Available = r.Fef50Available = r.Fef75Available = r.Fvc > 0;
                r.Fef2575 = span > .05 ? r.Fvc * .5 / span : 0;
                r.Fef2575Available = r.Fef2575 > 0 && Finite(r.Fef2575);
            }
            else
            {
                r.Fef25 = r.Fef50 = r.Fef75 = r.Fef2575 = 0;
                r.Fef25Available = r.Fef50Available = r.Fef75Available = r.Fef2575Available = false;
            }
            r.AvailabilityMetadata = true;
            // ATS/ERS: the peak tangent locates Time Zero; BEV is V(Time Zero),
            // not PEF * Time Zero. Acquisition timing is still unvalidated.
            r.BevAvailable = TryBackExtrapolation(s, r.PefTime, r.Pef, out r.TimeZero, out r.Bev);
            r.BevSourceAdequacy = r.BevAvailable ? "computed_from_reconstructed_signal_timing_unverified" : "time_zero_not_bracketed_or_invalid_signal";
            r.BevOk = r.BevAvailable && r.Bev <= Math.Max(.100, .05 * r.Fvc);
            r.PlateauOk = r.Fet >= 1 && r.Fvc - s.VolumeAt(r.Fet - 1) <= .025;
            r.FetOk = r.PlateauOk || r.Fet >= 15;
            r.PeftOk = r.PefTime <= .150;
            r.Valid = true;
            if (!trial.Completed) r.Warnings.Add(trial.OperatorStopped?"ثبت با توقف اپراتور پایان یافته است؛ اعتبار هر پارامتر جداگانه بررسی شود.":"پایان فنی ثبت تأیید نشده است؛ برای پیشنهاد خودکار استفاده نمی‌شود. " + trial.EndReason);
            if (!r.FetOk) r.Warnings.Add("فلات پایانی ۲۵ میلی‌لیتر در یک ثانیه مشاهده نشد؛ پایان بازدم را بررسی کنید.");
            if (!r.BevAvailable) r.Warnings.Add("زمان صفر از دادهٔ موجود قابل محاسبه نیست.");
            else if (!r.BevOk) r.Warnings.Add("برآورد حجم پس‌برون‌یابی بالاست؛ شروع مانور را بررسی کنید.");
            if (!r.PeftOk) r.Warnings.Add("اوج جریان دیررس است؛ شروع بازدم را بررسی کنید.");
            if (r.Cough) r.Warnings.Add("افت ناگهانی جریان در ثانیهٔ اول؛ بررسی سرفه لازم است.");
            if (!r.CurveUsable) r.Warnings.Add("FEF25–75 ناموجود است: "+r.CurveProblem);
            else if (!r.Fef2575Available) r.Warnings.Add("بازهٔ زمان-حجم برای FEF25–75 کافی نیست؛ مقدار ساخته نمی‌شود.");
            if (r.Fet < 1) r.Warnings.Add("بازدم کمتر از یک ثانیه است؛ FEV1 قابل اتکا نیست.");
            if (!trial.Simulated) r.Warnings.Add("شاخص‌ها از بسته‌های جریان بازسازی شده‌اند؛ زمان‌بندی رایانه و گام حجمی بر دقت اثر دارند.");
            if (trial.Imported) r.Warnings.Add("این تست از فایل ضبط‌شده بازپخش شده است؛ اندازه‌گیری جدید نیست.");
            r.Warnings.Add("کیفیت خودکار تقریبی است؛ سرفه، نشت، کالیبراسیون و پذیرش مانور باید توسط اپراتور بررسی شود.");
            Interpret(r, p);
            if (!trial.Completed) r.Interpretation = "مانور ناقص؛ تفسیر قابل اتکا نیست. " + r.Interpretation;
            return r;
        }

        public static bool TryBackExtrapolation(Expiration s,double peakTime,double peakFlow,out double timeZero,out double bev)
        {
            timeZero=bev=0;
            if(s==null || s.Time==null || s.Volume==null || s.Count<2 || s.Volume.Length!=s.Count || !Finite(peakTime) || !Finite(peakFlow) || peakFlow<=0) return false;
            for(int i=0;i<s.Count;i++)
                if(!Finite(s.Time[i]) || !Finite(s.Volume[i]) || s.Volume[i]<0 || (i>0 && (s.Time[i]<=s.Time[i-1] || s.Volume[i]<s.Volume[i-1]))) return false;
            if(peakTime<s.Time[0] || peakTime>s.Time[s.Count-1]) return false;
            timeZero=peakTime-s.VolumeAt(peakTime)/peakFlow;
            if(timeZero<s.Time[0] || timeZero>peakTime) return false;
            bev=s.VolumeAt(timeZero);
            return Finite(bev);
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        // FEF25-75 needs a real time-volume curve with a consistent origin. A
        // numerically finite end volume alone is not enough evidence.
        private static bool UsableCurve(Expiration s,out string problem)
        {
            problem="";
            if(s==null || s.Count<5 || s.Time==null || s.Volume==null || s.Flow==null || s.Time.Length!=s.Count || s.Volume.Length!=s.Count || s.Flow.Length!=s.Count)
            { problem="منحنی زمان-حجم برای محاسبهٔ FEF25–75 کامل نیست."; return false; }
            for(int i=0;i<s.Count;i++)
            {
                if(!Finite(s.Time[i]) || !Finite(s.Volume[i]) || !Finite(s.Flow[i]) || s.Time[i]<0 || s.Volume[i]<0)
                { problem="نمونهٔ غیرمعتبر در منحنی زمان-حجم وجود دارد."; return false; }
                if(i>0 && (s.Time[i]<=s.Time[i-1] || s.Volume[i]<s.Volume[i-1]))
                { problem="ترتیب زمان یا حجم در منحنی پیوسته نیست."; return false; }
            }
            if(s.Volume[s.Count-1]<=0 || s.Time[s.Count-1]<=0)
            { problem="مبنای زمان یا حجم منحنی معتبر نیست."; return false; }
            return true;
        }

        private static void Interpret(AnalysisResult r, Patient p)
        {
            if (!r.Ref.Applicable)
            {
                r.Pattern = Pattern.Invalid;
                r.Interpretation = p.Origin == Origin.Other ? "برای گروه انتخاب‌شده مرجع تأییدشده‌ای تعریف نشده است؛ فقط مقادیر اندازه‌گیری‌شده گزارش می‌شوند." : "مشخصات خارج از دامنهٔ مرجع ERS93 است؛ فقط مقادیر اندازه‌گیری‌شده گزارش می‌شوند.";
                r.Warnings.Add(p.Origin == Origin.Other ? "برای گروه سایر، ضریب نژادی یا مرجع تأییدشده‌ای اعمال نشده است." : "دامنهٔ مرجع: سن ۱۸ تا ۷۰ سال؛ قد مردان ۱۵۵ تا ۱۹۵ و زنان ۱۴۵ تا ۱۸۰ سانتی‌متر.");
                return;
            }
            bool lowRatio = r.Fev1Fvc < r.Ref.RatioLln, lowFvc = r.Fvc < r.Ref.FvcLln;
            r.Pattern = lowRatio ? (lowFvc ? Pattern.Mixed : Pattern.Obstructive) : (lowFvc ? Pattern.Restrictive : Pattern.Normal);
            string text;
            if (lowRatio && lowFvc) text = "کاهش نسبت FEV1/FVC و FVC نسبت به LLN؛ احتمال انسداد همراه با حبس هوا یا محدودیت. برای تأیید محدودیت یا الگوی مختلط، TLC لازم است.";
            else if (lowRatio) text = "نسبت FEV1/FVC کمتر از LLN مرجع است؛ الگوی سازگار با انسداد جریان هوا.";
            else if (lowFvc) text = "FVC کمتر از LLN با نسبت حفظ‌شده؛ احتمال محدودیت یا مانور ناکامل. تأیید محدودیت نیازمند TLC است.";
            else text = "FVC و نسبت FEV1/FVC در محدودهٔ مرجع ERS93 قرار دارند.";
            if (!r.IsAcceptable) text = "کیفیت نیازمند بررسی است. " + text;
            r.Interpretation = text + " تفسیر مقدماتی؛ تأیید پزشک لازم است.";
        }
    }
}

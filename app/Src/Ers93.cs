using System;

namespace SpiroLab
{
    // ERS/ECCS 1993, adults. LLN = predicted - 1.645 * residual SD.
    // Reference population and applicability are documented in docs/analysis.md.
    public static class Ers93
    {
        public static Reference Compute(Patient p)
        {
            double h = p.HeightM, a = Math.Max(25, p.ReferenceAgeYears??p.Age);
            Reference r = new Reference();
            r.Applicable = p.Origin != Origin.Other && p.Age >= 18 && p.Age <= 70 &&
                (p.Sex == Sex.Male ? p.HeightCm >= 155 && p.HeightCm <= 195 : p.HeightCm >= 145 && p.HeightCm <= 180);
            if (!r.Applicable) return r;
            if (p.Sex == Sex.Male)
            {
                r.Fvc = 5.76 * h - .026 * a - 4.34;
                r.Fev1 = 4.30 * h - .029 * a - 2.49;
                r.Fev1Fvc = 87.21 - .18 * a;
                r.Pef = 6.14 * h - .043 * a + .15;
                r.Fef2575 = 1.94 * h - .043 * a + 2.70;
                r.Fef25 = 5.46 * h - .029 * a - .47;
                r.Fef50 = 3.79 * h - .031 * a - .35;
                r.Fef75 = 2.61 * h - .026 * a - 1.34;
                r.FvcLln = r.Fvc - 1.645 * .61;
                r.Fev1Lln = r.Fev1 - 1.645 * .51;
                r.RatioLln = r.Fev1Fvc - 1.645 * 7.17;
            }
            else
            {
                r.Fvc = 4.43 * h - .026 * a - 2.89;
                r.Fev1 = 3.95 * h - .025 * a - 2.60;
                r.Fev1Fvc = 89.10 - .19 * a;
                r.Pef = 5.50 * h - .030 * a - 1.11;
                r.Fef2575 = 1.25 * h - .034 * a + 2.92;
                r.Fef25 = 3.22 * h - .025 * a + 1.60;
                r.Fef50 = 2.45 * h - .025 * a + 1.16;
                r.Fef75 = 1.05 * h - .025 * a + 1.11;
                r.FvcLln = r.Fvc - 1.645 * .43;
                r.Fev1Lln = r.Fev1 - 1.645 * .38;
                r.RatioLln = r.Fev1Fvc - 1.645 * 6.51;
            }
            // ERS93 does not supply FEV6. Do not fabricate a reference.
            return r;
        }
        public static string PatternName(Pattern p)
        {
            switch (p)
            {
                case Pattern.Normal: return "در محدوده مرجع";
                case Pattern.Obstructive: return "احتمال انسداد";
                case Pattern.Restrictive: return "FVC پایین، نسبت حفظ‌شده";
                case Pattern.Mixed: return "نسبت و FVC پایین";
                default: return "بدون تفسیر مرجع";
            }
        }
    }
}

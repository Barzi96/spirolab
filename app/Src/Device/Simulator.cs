#if SPIROLAB_TEST
using System;
using System.Collections.Generic;

namespace SpiroLab.Device
{
    /// <summary>
    /// Synthesises a realistic forced expiration so the UI can be used and
    /// tested before the real packet decoder is finished.
    /// </summary>
    public static class Simulator
    {
        public static Trial Build(Patient p, int seed)
        {
            Random rnd = new Random(seed);
            Reference pred = Ers93.Compute(p);

            double fvcTarget = (pred.Applicable ? pred.Fvc : Math.Max(1.0, p.HeightM * 2.5)) * (0.82 + rnd.NextDouble() * 0.28);
            double duration = 6.0 + rnd.NextDouble() * 1.8;
            double tp = 0.085 + rnd.NextDouble() * 0.035;
            double tail = 0.38 + rnd.NextDouble() * 0.18;
            double tau = 0.75 + rnd.NextDouble() * 0.20;
            double dt = 0.01;

            int nLead = 35;                                    // quiet second before the blow
            int n = nLead + (int)(duration / dt) + 20;

            double[] raw = new double[n];
            for (int i = nLead; i < n; i++)
            {
                double t = (i - nLead) * dt;
                double v = 0;
                if (t > 0.004)
                {
                    double x = t / tp;
                    if (x < 40) v += x * Math.Exp(1 - x);
                    v += tail * Math.Exp(-t / tau);
                }
                raw[i] = v;
            }

            // scale so that the integral equals the target FVC
            double area = 0;
            for (int i = 1; i < n; i++) area += raw[i] * dt;
            double scale = area > 1e-6 ? fvcTarget / area : 1;

            Trial trial = new Trial();
            trial.Dt = dt;
            trial.Simulated = true;
            trial.When = DateTime.Now;

            for (int i = 0; i < n; i++)
            {
                double f = raw[i] * scale;
                if (f > 1e-4) f += (rnd.NextDouble() - 0.5) * 0.05;      // sensor noise
                if (f < 0) f = 0;
                trial.Flow.Add(f);
            }

            // small inspiration just before the blow (looks like a real trace)
            for (int i = nLead - 12; i < nLead - 1; i++)
                trial.Flow[i] = -1.2 * Math.Sin((i - (nLead - 12)) / 11.0 * Math.PI);

            // occasional cough: a dip in the middle
            if (rnd.NextDouble() < 0.25)
            {
                int at = nLead + (int)(0.9 / dt);
                for (int i = at; i < at + 8 && i < n; i++) trial.Flow[i] *= 0.25;
            }

            return trial;
        }
    }
}
#endif

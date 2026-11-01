using System;
using System.Collections.Generic;

namespace SpiroLab
{
    // A single expiratory segment shared by analysis and both chart axes.
    public sealed class Expiration
    {
        public double[] Time, Volume, Flow;
        public int Count { get { return Time.Length; } }

        public static Expiration From(Trial trial)
        {
            int n = trial.Flow.Count;
            bool timed = trial.Time.Count == n;
            bool volumetric = trial.Volume.Count == n;
            int bestStart = -1, bestEnd = -1, start = -1;
            double best = 0, amount = 0;
            for (int i = 0; i <= n; i++)
            {
                double f = i < n ? trial.Flow[i] : -1;
                if (i < n && f >= 0)
                {
                    if (start < 0 && f > 0.08) { start = i; amount = 0; }
                    if (start >= 0)
                    {
                        double dt = timed && i > 0 ? trial.Time[i] - trial.Time[i - 1] : trial.Dt;
                        amount += volumetric ? Math.Max(0, trial.Volume[i] - (i > 0 ? trial.Volume[i - 1] : 0)) : Math.Max(0, f * dt);
                    }
                }
                else if (start >= 0)
                {
                    if (amount > best) { best = amount; bestStart = start; bestEnd = i - 1; }
                    start = -1;
                }
            }
            if (bestStart < 0) return new Expiration { Time = new double[0], Volume = new double[0], Flow = new double[0] };
            // Remove idle tail only; keep the low-flow end of the expiration.
            while (bestEnd > bestStart && trial.Flow[bestEnd] <= 0) bestEnd--;
            int count = bestEnd - bestStart + 2;
            Expiration s = new Expiration { Time = new double[count], Volume = new double[count], Flow = new double[count] };
            double origin = timed ? trial.Time[bestStart] : bestStart * trial.Dt;
            double interval = trial.Dt;
            if (volumetric && trial.Flow[bestStart] > 0)
                interval = Math.Min(0.2, Math.Max(0.001, (trial.Volume[bestStart] - (bestStart > 0 ? trial.Volume[bestStart - 1] : 0)) / trial.Flow[bestStart]));
            origin -= interval;
            double baseline = volumetric && bestStart > 0 ? trial.Volume[bestStart - 1] : 0;
            for (int j = 1; j < count; j++)
            {
                int i = bestStart + j - 1;
                s.Time[j] = (timed ? trial.Time[i] : i * trial.Dt) - origin;
                s.Flow[j] = trial.Flow[i];
                s.Volume[j] = volumetric ? Math.Max(s.Volume[j - 1], trial.Volume[i] - baseline)
                    : s.Volume[j - 1] + Math.Max(0, s.Flow[j]) * (s.Time[j] - s.Time[j - 1]);
            }
            return s;
        }

        public double VolumeAt(double time)
        {
            if (Count == 0) return 0;
            for (int i = 1; i < Count; i++)
                if (Time[i] >= time) return Interpolate(Time[i - 1], Time[i], Volume[i - 1], Volume[i], time);
            return Volume[Count - 1];
        }
        public double TimeAtVolume(double volume)
        {
            for (int i = 1; i < Count; i++)
                if (Volume[i] >= volume) return Interpolate(Volume[i - 1], Volume[i], Time[i - 1], Time[i], volume);
            return Count == 0 ? 0 : Time[Count - 1];
        }
        public double FlowAtVolume(double volume)
        {
            for (int i = 1; i < Count; i++)
                if (Volume[i] >= volume) return Interpolate(Volume[i - 1], Volume[i], Flow[i - 1], Flow[i], volume);
            return 0;
        }
        private static double Interpolate(double x0, double x1, double y0, double y1, double x)
        { return x1 <= x0 ? y1 : y0 + (y1 - y0) * Math.Max(0, Math.Min(1, (x - x0) / (x1 - x0))); }
    }
}

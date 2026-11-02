using System;
using System.Collections.Generic;

namespace MirParityLab.Core
{
    // Independent, deliberately conservative reconstruction from raw live packets.
    // It is NOT a reimplementation of MIR firmware and is never treated as SpiroLab
    // or MIR output. It exists only to expose a separately-labelled software layer.
    public static class CurveCalculator
    {
        public static CalculatedSet Calculate(ManeuverRecord maneuver)
        {
            CalculatedSet set = new CalculatedSet();
            set.Method = "independent_positive_flow_integration_v1";
            set.Assumptions.Add("Packet type 0x10 is treated as flow*100 using the exact arithmetic observed in PacketDecoder.cs.");
            set.Assumptions.Add("Packet type 0x20 is treated as (raw-5000)/100 with the declared step; semantics remain unverified.");
            set.Assumptions.Add("Only a positive-flow expiratory segment above 0.08 is integrated.");
            set.Assumptions.Add("This is not MIR firmware, not SpiroLab, and not a validated clinical algorithm.");
            if (maneuver == null || maneuver.Packets == null || maneuver.Packets.Count == 0)
            {
                set.Warnings.Add("No packets were supplied.");
                return set;
            }

            List<double> times = new List<double>();
            List<double> flows = new List<double>();
            List<double> volumes = new List<double>();
            double previousMeasuredVolume = 0;
            bool hasPreviousVolume = false;
            bool timed = maneuver.StepType.HasValue && maneuver.StepType.Value == 0x40;
            double step = maneuver.Step.HasValue ? maneuver.Step.Value : 0;
            double runningVolume = 0;

            for (int i = 0; i < maneuver.Packets.Count; i++)
            {
                PacketRecord p = maneuver.Packets[i];
                if (!p.Type.HasValue || !p.Raw.HasValue) continue;
                if (p.Type.Value != 0x10 && p.Type.Value != 0x20) continue;
                double time;
                double flow;
                double volume;
                if (p.Type.Value == 0x10)
                {
                    if (p.Raw.Value < 0 || p.Raw.Value > 65535)
                    {
                        set.Warnings.Add("Packet " + p.Index + " has a raw value outside u16.");
                        continue;
                    }
                    flow = (p.Raw.Value < 0x8000 ? p.Raw.Value : p.Raw.Value - 65535) / 100.0;
                    if (timed)
                    {
                        if (!(step > 0))
                        {
                            set.Warnings.Add("Time-step packet stream has no valid step.");
                            return set;
                        }
                        time = (times.Count + 1) * step;
                    }
                    else
                    {
                        if (!p.Seconds.HasValue || p.Seconds.Value < 0)
                        {
                            set.Warnings.Add("Volume-step packet stream lacks host receipt times; FEV1 cannot be reconstructed.");
                            continue;
                        }
                        time = p.Seconds.Value;
                    }
                    if (timed) runningVolume += flow * step;
                    else runningVolume += Math.Sign(flow) * step;
                    volume = runningVolume;
                }
                else
                {
                    if (!(step > 0) || !maneuver.StepType.HasValue)
                    {
                        set.Warnings.Add("Type 0x20 packets require an explicit step and step type.");
                        return set;
                    }
                    double measured = (p.Raw.Value - 5000) / 100.0;
                    flow = hasPreviousVolume ? (measured - previousMeasuredVolume) / step : 0;
                    previousMeasuredVolume = measured;
                    hasPreviousVolume = true;
                    time = (times.Count + 1) * step;
                    volume = measured;
                }
                if (!Finite(time) || !Finite(flow) || !Finite(volume) || time < 0) continue;
                if (times.Count > 0 && time <= times[times.Count - 1]) continue;
                times.Add(time);
                flows.Add(flow);
                volumes.Add(volume);
            }

            set.SampleCount = times.Count;
            if (times.Count < 5)
            {
                set.Warnings.Add("Fewer than five usable time samples.");
                return set;
            }

            int bestStart = -1;
            int bestEnd = -1;
            double bestVolume = -1;
            int start = -1;
            double accumulated = 0;
            for (int i = 0; i <= times.Count; i++)
            {
                bool active = i < times.Count && flows[i] >= 0.08;
                if (active)
                {
                    if (start < 0) { start = i; accumulated = 0; }
                    if (i > start)
                    {
                        double dt = times[i] - times[i - 1];
                        if (dt > 0) accumulated += 0.5 * (Math.Max(0, flows[i]) + Math.Max(0, flows[i - 1])) * dt;
                    }
                }
                else if (start >= 0)
                {
                    if (accumulated > bestVolume)
                    {
                        bestVolume = accumulated;
                        bestStart = start;
                        bestEnd = i - 1;
                    }
                    start = -1;
                }
            }
            if (bestStart < 0 || bestEnd <= bestStart)
            {
                set.Warnings.Add("No expiratory segment above the 0.08 threshold.");
                return set;
            }

            List<double> segTime = new List<double>();
            List<double> segFlow = new List<double>();
            List<double> segVolume = new List<double>();
            double origin = times[bestStart];
            segTime.Add(0);
            segFlow.Add(flows[bestStart]);
            segVolume.Add(0);
            for (int i = bestStart + 1; i <= bestEnd; i++)
            {
                double dt = times[i] - times[i - 1];
                double dv = 0.5 * (Math.Max(0, flows[i]) + Math.Max(0, flows[i - 1])) * dt;
                segTime.Add(times[i] - origin);
                segFlow.Add(flows[i]);
                segVolume.Add(segVolume[segVolume.Count - 1] + Math.Max(0, dv));
            }
            if (segVolume[segVolume.Count - 1] < 0.3) return set;

            double pef = 0;
            double pefTime = 0;
            for (int i = 0; i < segFlow.Count; i++)
            {
                if (segFlow[i] > pef) { pef = segFlow[i]; pefTime = segTime[i]; }
            }
            double fvc = segVolume[segVolume.Count - 1];
            double fev1 = VolumeAt(segTime, segVolume, 1.0);
            set.Values[Parameters.Fvc] = fvc;
            set.Values[Parameters.Fev1] = fev1;
            set.Values[Parameters.Pef] = pef;
            set.Values[Parameters.PefTime] = pefTime;
            set.Values[Parameters.Fet] = segTime[segTime.Count - 1];
            if (fvc > 0) set.Values[Parameters.Fev1Fvc] = fev1 / fvc * 100.0;
            set.Available = true;
            set.Warnings.Add("Software reconstruction is unverified and must not be labelled Device-reported or used as a clinical result.");
            return set;
        }

        private static double VolumeAt(List<double> times, List<double> volumes, double target)
        {
            if (times.Count == 0) return 0;
            for (int i = 1; i < times.Count; i++)
            {
                if (times[i] >= target)
                {
                    double span = times[i] - times[i - 1];
                    if (span <= 0) return volumes[i];
                    double fraction = (target - times[i - 1]) / span;
                    if (fraction < 0) fraction = 0;
                    if (fraction > 1) fraction = 1;
                    return volumes[i - 1] + (volumes[i] - volumes[i - 1]) * fraction;
                }
            }
            return volumes[volumes.Count - 1];
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}

#if SPIROLAB_TEST
using System;
using System.Collections.Generic;

namespace SpiroLab.Device
{
    /// <summary>
    /// Synthesises a realistic forced expiration so the UI can be used and
    /// tested before the real packet decoder is finished.
    /// </summary>
    // Test-only explicit synthetic evidence. It produces the same observed
    // cod_LAST byte layout that DeviceSession decodes, so mock submissions can
    // exercise the real provenance gate without ever presenting the fixture as a
    // live MIR capture.
    public static class SyntheticDeviceEvidence
    {
        public const string FixtureSerial = "J22889";

        public static void Attach(Trial trial,double fvc,double fev1,double pef,double fet,string serial,int captureSequence)
        {
            if(trial==null) throw new ArgumentNullException("trial");
            if(!Finite(fvc) || fvc<0.05 || fvc>12.0) throw new ArgumentOutOfRangeException("fvc");
            if(!Finite(fev1) || fev1<0.05 || fev1>fvc+0.05) throw new ArgumentOutOfRangeException("fev1");
            if(!Finite(pef) || pef<=0 || pef>20.0) throw new ArgumentOutOfRangeException("pef");
            if(!Finite(fet) || fet<=0 || fet>30.0) throw new ArgumentOutOfRangeException("fet");
            if(captureSequence<=0) throw new ArgumentOutOfRangeException("captureSequence");
            // The observed block stores hundredths; the fixture must contain the
            // exact values that its own bytes decode to.
            fvc=Math.Round(fvc*100.0)/100.0; fev1=Math.Round(fev1*100.0)/100.0;
            pef=Math.Round(pef*100.0)/100.0; fet=Math.Round(fet*100.0)/100.0;
            string id=string.IsNullOrEmpty(serial)?FixtureSerial:serial;
            trial.Simulated=false; trial.Imported=false;
            trial.Completed=true; trial.OperatorStopped=false; trial.CaptureError="";
            trial.DeviceSerial=id; trial.DeviceCaptureSequence=captureSequence; trial.RequiresDeviceCore=true;
            trial.When=DateTime.Now.AddSeconds(-2);
            trial.DeviceValues=new DeviceResults {
                Present=true,BlockValid=true,Associated=true,Association="synthetic_test_evidence_not_live_device",
                CaptureSequence=captureSequence,SerialNumber=id,ReadAtUtc=DateTime.UtcNow,ReadDelaySeconds=0.2,
                Fvc=fvc,Fev1=fev1,Pef=pef,Fet=fet,
                FvcValid=fvc>=0.05 && fvc<=12.0,
                Fev1Valid=fev1>=0.05 && fev1<=fvc+0.05,
                PefValid=pef>0 && pef<=20.0,
                FetValid=fet>0 && fet<=30.0,
                Raw=Block(fvc,fev1,pef,fet)
            };
            if(!HasPacket(trial,0x10) && !HasPacket(trial,0x20)) trial.Packets.Add(new DevicePacket {Seconds=0.01,Type=0x10,Raw=200});
            if(!HasPacket(trial,0x04)) trial.Packets.Add(new DevicePacket {Seconds=Math.Max(0.02,trial.Duration),Type=0x04,Raw=0});
        }

        public static byte[] Block(double fvc,double fev1,double pef,double fet)
        {
            byte[] bytes=new byte[160];
            WriteU16(bytes,0x10,(int)Math.Round(fvc*100.0));
            WriteU16(bytes,0x12,(int)Math.Round(fev1*100.0));
            WriteU16(bytes,0x14,(int)Math.Round(pef*100.0));
            WriteU16(bytes,0x1A,(int)Math.Round(fet*100.0));
            WriteU16(bytes,0x50,187);
            return bytes;
        }

        static bool HasPacket(Trial trial,byte type)
        {
            foreach(DevicePacket packet in trial.Packets) if(packet!=null && packet.Type==type) return true;
            return false;
        }

        static void WriteU16(byte[] bytes,int offset,int value)
        {
            bytes[offset]=(byte)((value>>8)&255); bytes[offset+1]=(byte)(value&255);
        }

        static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }

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

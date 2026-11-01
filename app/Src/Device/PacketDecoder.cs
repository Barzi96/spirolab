using System;
using System.Diagnostics;
using System.Threading;

namespace SpiroLab.Device
{
    public sealed class DevicePacket
    {
        public double Seconds;
        public byte Type;
        public int Raw;
    }

    // Decodes the observed 0x10 signed flow packets. Volume-step mode is
    // irregularly sampled: never treat USB polling intervals as a fixed 100 Hz.
    public sealed class PacketDecoder
    {
        private double _volume;
        private byte _format;
        public void Add(Trial trial, DevicePacket p)
        {
            trial.Packets.Add(p);
            if (p.Type != 0x10 && !(p.Type == 0x20 && trial.StepType == 0x40)) return;
            if (_format != 0 && _format != p.Type) throw new InvalidOperationException("قالب نمونه‌ها در حین مانور تغییر کرد؛ دادهٔ خام ذخیره شد.");
            _format = p.Type;
            double measuredVolume = (p.Raw - 5000) / 100.0;
            double flow = p.Type == 0x20 ? (measuredVolume - _volume) / trial.Step : (p.Raw < 0x8000 ? p.Raw : p.Raw - 65535) / 100.0;
            if (Math.Abs(flow) > 25) throw new InvalidOperationException("جریان خارج از دامنهٔ قابل پشتیبانی دریافت شد.");
            if (trial.Time.Count > 0 && p.Seconds < trial.Time[trial.Time.Count - 1])
                throw new InvalidOperationException("ترتیب زمانی بسته‌ها معتبر نیست.");
            if (p.Type == 0x20) _volume = measuredVolume;
            else if (trial.StepType == 0x30)
                _volume += Math.Sign(flow) * trial.Step;
            else if (trial.StepType == 0x40)
                _volume += flow * trial.Step;
            else throw new InvalidOperationException("گام نمونه‌برداری ناشناخته است.");
            double time = trial.StepType == 0x40 ? (trial.Flow.Count + 1) * trial.Step : p.Seconds;
            trial.Flow.Add(flow); trial.Time.Add(time); trial.Volume.Add(_volume);
        }
    }

    public sealed class CaptureOutcome
    {
        public bool Completed;
        public string Reason;
        public DeviceResults Results;
    }
}

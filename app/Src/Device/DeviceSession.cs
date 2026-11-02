using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace SpiroLab.Device
{
    /// <summary>Results the spirolab computes itself at the end of a maneuver.</summary>
    public sealed class DeviceResults
    {
        public bool Present;
        public bool BlockValid;
        public bool Associated;
        public string Association="not_established";
        public string InvalidReason="";
        public int CaptureSequence;
        public string SerialNumber="";
        public DateTime ReadAtUtc;
        public double ReadDelaySeconds;
        public double Fvc;         // L
        public double Fev1;        // L
        public double Pef;         // L/s
        public double Fet;         // s
        public double PefTime;     // s
        public int Quality;        // Observed field; never interpreted as an ATS grade.
        public bool FvcValid, Fev1Valid, PefValid, FetValid, PefTimeValid;
        public byte[] Raw;

        public double Fev1Fvc { get { return Fvc > 0 ? Fev1 / Fvc * 100.0 : 0; } }

        public override string ToString()
        {
            if (!Present) return "(no verified device result"+(InvalidReason.Length>0?": "+InvalidReason:"")+")";
            return string.Format("FVC {0:F2} L  FEV1 {1:F2} L  PEF {2:F2} L/s  FET {3:F2} s",
                                 Fvc, Fev1, Pef, Fet);
        }
    }

    /// <summary>
    /// Spirolab session on top of <see cref="MirUsb"/>: connect / identify,
    /// start an FVC maneuver, pump the real time packets, collect the result.
    /// Every operation is written to a text transcript.
    /// </summary>
    public sealed class DeviceSession : IDisposable
    {
        private readonly object _lock = new object();
        private readonly IMirTransport _usb;
        private StreamWriter _log;

        public event Action<string> Line;

        public bool Connected { get { return _usb.IsOpen; } }
        public bool HandleOpened { get; private set; }
        public bool ControlResponded { get; private set; }
        public bool StartCommandSucceeded { get; private set; }
        public int LivePacketCount { get; private set; }
        public string SafeTransportError { get { MirUsb native=_usb as MirUsb; return native==null?"none":native.CleanupPending?"native_cancel_acknowledgement_pending":native.LastError==0?"none":"Win32 error "+native.LastError; } }
        public string SerialNumber { get; private set; }
        public string ModelName { get; private set; }
        public string LogPath { get; private set; }
        public double VolumeStepL { get; private set; }
        public double TimeStepS { get; private set; }
        public int CaptureSequence { get; private set; }
        private int _activeCaptureSequence;
        private int _lastResultSequence;
        private DateTime _captureCompletedUtc;
        private byte[] _lastResultRaw;
        private string _lastResultSerial="";

        public DeviceSession() : this(new MirUsb()) { }

        public DeviceSession(IMirTransport usb)
        {
            if (usb == null) throw new ArgumentNullException("usb");
            _usb = usb;
            SerialNumber = "";
            ModelName = "";
            VolumeStepL = 0.03;
        }

        // ------------------------------------------------------------------
        public bool Connect(out string error)
        {
            error = null;
            lock (_lock)
            {
                try
                {
                    OpenLog();
                    SerialNumber = "";
                    ModelName = "";
                    HandleOpened=ControlResponded=StartCommandSucceeded=false; LivePacketCount=0;
                    CaptureSequence=0; _activeCaptureSequence=0; _lastResultSequence=0; _lastResultRaw=null; _lastResultSerial="";
                    Log("=== connect ===");

                    if (!_usb.Open(out error))
                    {
                        Log("open failed: " + error);
                        return false;
                    }

                    Log("device " + _usb.DevicePath);
                    HandleOpened=true;
                    byte state = _usb.State();
                    Log("state = 0x" + state.ToString("X2"));
                    if (state == 0xFF) { error = "دستگاه پاسخ نمی‌دهد؛ کابل، درایور و بسته بودن نرم‌افزار MIR را بررسی کنید."; _usb.Close(); return false; }
                    ControlResponded=true;

                    byte[] sda = _usb.ReadSda(92);
                    if (sda != null && sda.Length > 0)
                    {
                        Log("SDA " + sda.Length + " bytes: " + MirUsb.Hex(sda));
                        Log("SDA ascii: " + MirUsb.Ascii(sda));
                        ParseSda(sda);
                    }
                    else Log("SDA read failed");
                    if (_usb.State() == 0xFF) { error = "ارتباط با دستگاه پس از شناسایی برقرار نیست."; _usb.Close(); return false; }

                    return true;
                }
                catch (Exception ex)
                {
                    error = "خطای اتصال: " + ex.Message;
                    _usb.Close();
                    Log("EXCEPTION " + ex);
                    return false;
                }
            }
        }

        private void ParseSda(byte[] sda)
        {
            // the SDA block is ASCII text with a length/footer around it
            StringBuilder sb = new StringBuilder();
            foreach (byte b in sda) sb.Append(b >= 32 && b < 127 ? (char)b : ' ');
            string text = sb.ToString();
            Log("SDA text: " + text);

            // serial number: a letter followed by digits, e.g. "J22889"
            string best = "";
            for (int i = 0; i + 4 < text.Length; i++)
            {
                if (!char.IsLetter(text[i])) continue;
                int j = i + 1, digits = 0;
                while (j < text.Length && char.IsDigit(text[j])) { j++; digits++; }
                if (digits >= 4 && digits > best.Length - 1) best = text.Substring(i, j - i);
            }
            if (best.Length > 0) { SerialNumber = best; Log("serial = " + best); }

            if (text.IndexOf("SPIROLAB", StringComparison.OrdinalIgnoreCase) >= 0) ModelName = "spirolab";
            else if (text.IndexOf("SPIROBANK", StringComparison.OrdinalIgnoreCase) >= 0) ModelName = "spirobank";
            else if (text.IndexOf("MINISPIR", StringComparison.OrdinalIgnoreCase) >= 0) ModelName = "minispir";
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                try
                {
                    if (_usb.IsOpen) { Log("=== disconnect ==="); _usb.Close(); }
                }
                catch (Exception ex) { Log("EXCEPTION on close: " + ex.Message); }
                finally
                {
                    if (_log != null) { try { _log.Flush(); _log.Dispose(); } catch { } _log = null; }
                }
            }
        }

        // ------------------------------------------------------------------
        /// <summary>Start a forced vital capacity maneuver (cod_FVC).</summary>
        public bool StartFvc(out string error)
        {
            error = null;
            lock (_lock)
            {
                StartCommandSucceeded=false; LivePacketCount=0;
                _activeCaptureSequence=++CaptureSequence;
                _captureCompletedUtc=DateTime.MinValue;
                if (!_usb.IsOpen) { error = "دستگاه متصل نیست."; return false; }

                if (!_usb.WaitReady(5000))
                {
                    error = "دستگاه آماده نیست (state != 0). یک بار کابل را جدا و وصل کنید.";
                    Log("not ready before FVC");
                    return false;
                }

                byte[] step = _usb.Get(0x08, 3);
                if (step == null || step.Length < 3)
                {
                    error = "دستگاه فرمان شروع تست را نپذیرفت.";
                    _usb.Send(0x1B);
                    Log("cod_FVC failed");
                    return false;
                }

                Log("start FVC -> " + MirUsb.Hex(step));
                int value = (step[1] << 8) | step[2];
                if (value <= 0) { error = "گام نمونه‌برداری دستگاه صفر است."; _usb.Send(0x1B); return false; }
                if (step[0] == 0x30) { VolumeStepL = value / 10000.0; TimeStepS = 0; }
                else if (step[0] == 0x40) { TimeStepS = value / 1000.0; VolumeStepL = 0; }
                else
                {
                    error = "پاسخ نامعلوم دستگاه به فرمان شروع تست.";
                    _usb.Send(0x1B);
                    Log("unexpected start packet 0x" + step[0].ToString("X2"));
                    return false;
                }

                if (VolumeStepL > 0.5 || TimeStepS > 1.0)
                { error = "گام نمونه‌برداری خارج از دامنهٔ پشتیبانی‌شده است."; _usb.Send(0x1B); return false; }

                Log(string.Format("step 0x{0:X2} value={1} -> volume step {2:F3} L / time step {3:F3} s",
                                  step[0], value, VolumeStepL, TimeStepS));
                StartCommandSucceeded=true;
                return true;
            }
        }

        /// <summary>
        /// One real time packet. The caller loops on this; the device returns
        /// 0xFF while it has no new sample.
        /// </summary>
        public int ReadPacket(out byte type, out int raw)
        {
            lock (_lock)
            {
            type = 0xFF;
            raw = 0;
            byte[] p = _usb.Get(0x01, 3);
            if (p == null || p.Length < 3) return 0;
            type = p[0];
            raw = (p[1] << 8) | p[2];
            return 1;
            }
        }

        // Called exclusively by the acquisition worker. ESC is an abort,
        // never an end-of-test command: it discards the device result buffer.
        public CaptureOutcome Capture(Trial trial, Func<bool> stop, Action<DevicePacket> packet)
        {
            CaptureOutcome outcome = new CaptureOutcome();
            bool started = false;
            bool communicationFailed = false;
            try
            {
                string error;
                if (!StartFvc(out error)) { outcome.Reason = error; return outcome; }
                started = true;
                trial.StepType = TimeStepS > 0 ? (byte)0x40 : (byte)0x30;
                trial.Step = TimeStepS > 0 ? TimeStepS : VolumeStepL;
                trial.DeviceSerial = SerialNumber;
                trial.DeviceCaptureSequence = _activeCaptureSequence;
                int captureSequence = _activeCaptureSequence;
                Stopwatch watch = Stopwatch.StartNew();
                int failures = 0;
                while (!stop() && watch.Elapsed.TotalSeconds < 45)
                {
                    byte type; int raw;
                    if (ReadPacket(out type, out raw) == 0)
                    {
                        if (++failures >= 30) throw new IOException("ارتباط با دستگاه قطع شد یا دستگاه پاسخ نمی‌دهد.");
                        Thread.Sleep(20); continue;
                    }
                    failures = 0;
                    if (type == 0xFF) { Thread.Sleep(1); continue; }
                    LivePacketCount++;
                    DevicePacket p = new DevicePacket { Seconds = watch.Elapsed.TotalSeconds, Type = type, Raw = raw };
                    Log(string.Format(System.Globalization.CultureInfo.InvariantCulture, "packet {0:F4}s {1:X2} {2}", p.Seconds, type, raw));
                    packet(p);
                    if (type == 0x04) { outcome.Completed = true; _captureCompletedUtc=DateTime.UtcNow; break; }
                    if (type != 0x10 && type != 0x20 && type != 0x01)
                        Log("unhandled packet type " + type.ToString("X2"));
                }
                if (!outcome.Completed)
                    outcome.Reason = stop() ? "توقف توسط کاربر" : "پایان مهلت ۴۵ ثانیه‌ای";
                else
                {
                    Thread.Sleep(300);
                    outcome.Results = ReadResults(captureSequence);
                }
            }
            catch (Exception ex) { outcome.Completed = false; outcome.Reason = ex.Message; communicationFailed = ex is IOException; Log("capture error: " + ex); }
            finally
            {
                if (started && !outcome.Completed) { try { AbortTest(); } catch { } }
                if (communicationFailed) Disconnect();
            }
            return outcome;
        }

        /// <summary>Abort a running maneuver (Cod_ESC). Results are discarded.</summary>
        public void AbortTest()
        {
            lock (_lock)
            {
                Log("abort test (ESC)");
                _usb.Send(0x1B);
            }
        }

        /// <summary>
        /// Collect the device-computed result for the capture that has just
        /// completed. The USB protocol has no documented maneuver identifier, so
        /// association is proved only by this session's capture token, successful
        /// natural completion, an immediate single read, and duplicate-block
        /// rejection. Anything weaker is reported as unverified/unavailable.
        /// </summary>
        public DeviceResults ReadResults() { return ReadResults(_activeCaptureSequence); }

        public DeviceResults ReadResults(int captureSequence)
        {
            DeviceResults r = new DeviceResults();
            lock (_lock)
            {
                r.CaptureSequence=captureSequence;
                r.SerialNumber=SerialNumber;
                r.ReadAtUtc=DateTime.UtcNow;
                double delay=_captureCompletedUtc==DateTime.MinValue?double.NaN:(r.ReadAtUtc-_captureCompletedUtc).TotalSeconds;
                r.ReadDelaySeconds=delay;
                if(captureSequence<=0 || captureSequence!=_activeCaptureSequence || _captureCompletedUtc==DateTime.MinValue)
                {
                    r.InvalidReason="result_not_bound_to_a_completed_capture";
                    Log("cod_LAST skipped: "+r.InvalidReason);
                    return r;
                }
                if(_lastResultSequence==captureSequence)
                {
                    r.InvalidReason="result_already_read_for_this_capture";
                    Log("cod_LAST skipped: "+r.InvalidReason);
                    return r;
                }
                if(double.IsNaN(delay) || delay<0 || delay>10)
                {
                    r.InvalidReason="result_read_window_expired";
                    Log("cod_LAST skipped: "+r.InvalidReason);
                    return r;
                }

                byte[] last = _usb.ReadBlock(0xA2, 21, 100);
                r.ReadAtUtc=DateTime.UtcNow;
                r.ReadDelaySeconds=(r.ReadAtUtc-_captureCompletedUtc).TotalSeconds;
                if(double.IsNaN(r.ReadDelaySeconds) || r.ReadDelaySeconds<0 || r.ReadDelaySeconds>10)
                {
                    r.InvalidReason="result_read_window_expired_after_transfer";
                    Log("cod_LAST arrived too late -> unverified");
                    return r;
                }
                if (last == null)
                {
                    r.InvalidReason="cod_LAST_not_available";
                    Log("cod_LAST not available");
                    return r;
                }
                r.Raw = last;
                Log("cod_LAST " + last.Length + " bytes: " + MirUsb.Hex(last));
                if (last.Length < 0x1C)
                {
                    r.InvalidReason="truncated_cod_LAST_block";
                    Log("cod_LAST truncated -> ignored");
                    return r;
                }

                // A repeated byte-for-byte block after a new completed capture is
                // treated as stale LAST data, not as a second verified result.
                if(_lastResultRaw!=null && last.Length==_lastResultRaw.Length && _lastResultSerial==SerialNumber)
                {
                    bool same=true;
                    for(int i=0;i<last.Length;i++) if(last[i]!=_lastResultRaw[i]) { same=false; break; }
                    if(same)
                    {
                        r.InvalidReason="stale_duplicate_cod_LAST_block";
                        Log("cod_LAST duplicated a previous maneuver -> unverified");
                        return r;
                    }
                }

                // Observed layout only. Unknown/reserved bytes are not interpreted.
                //   0x10 FVC [0.01 L]   0x12 FEV1 [0.01 L]   0x14 PEF [0.01 L/s]
                //   0x1A FET [0.01 s]   0x50 time to PEF [0.01 s]
                r.Fvc = U16(last, 0x10) / 100.0;
                r.Fev1 = U16(last, 0x12) / 100.0;
                r.Pef = U16(last, 0x14) / 100.0;
                r.Fet = U16(last, 0x1A) / 100.0;
                if (last.Length >= 0x52) r.PefTime = U16(last, 0x50) / 100.0;
                if (last.Length >= 0x0C) r.Quality = U16(last, 0x0A); // unverified; diagnostic only

                r.FvcValid=Finite(r.Fvc) && r.Fvc>=.05 && r.Fvc<=12;
                r.Fev1Valid=Finite(r.Fev1) && r.Fev1>=.05 && r.Fev1<=r.Fvc+.05;
                r.PefValid=Finite(r.Pef) && r.Pef>0 && r.Pef<=20;
                r.FetValid=Finite(r.Fet) && r.Fet>0 && r.Fet<=30;
                r.PefTimeValid=last.Length>=0x52 && Finite(r.PefTime) && r.PefTime>0 && r.PefTime<=r.Fet+.05;
                if(!r.FvcValid)
                {
                    r.InvalidReason="cod_LAST_values_outside_supported_ranges";
                    Log("cod_LAST values look invalid -> ignored");
                    return r;
                }

                r.BlockValid=true;
                r.Associated=true;
                r.Association="capture_bound_immediate_single_read";
                r.Present=true;
                _lastResultSequence=captureSequence;
                _lastResultRaw=(byte[])last.Clone();
                _lastResultSerial=SerialNumber;
                Log("device result: " + r);
            }
            return r;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        /// <summary>Device volume-time curve (cod_CURVE), raw bytes.</summary>
        public byte[] ReadCurve()
        {
            lock (_lock)
            {
                byte[] c = _usb.ReadBlock(0xA3, 3, 100);
                if (c != null) Log("cod_CURVE " + c.Length + " bytes");
                return c;
            }
        }

        private static int U16(byte[] b, int off)
        {
            if (b == null || off + 1 >= b.Length) return 0;
            return (b[off] << 8) | b[off + 1];
        }

        // ------------------------------------------------------------------
        private void OpenLog()
        {
            if (_log != null) return;
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SpiroLab");
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, "device-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".log");
            _log = new StreamWriter(LogPath, false, Encoding.UTF8);
            _log.AutoFlush = true;
            Log("SpiroLab  " + Environment.MachineName + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        public void Log(string text)
        {
            if (_log != null)
            {
                try { _log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + text); }
                catch { }
            }
            Action<string> h = Line;
            if (h != null) h(text);
        }

        public string CapturePath { get { return LogPath; } }

        public void Dispose() { Disconnect(); }
    }
}

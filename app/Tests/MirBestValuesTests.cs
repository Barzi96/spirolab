using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Web.Script.Serialization;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;
using SpiroLab.Ui;

namespace MirBestValuesTests
{
    sealed class FixtureTransport : IMirTransport
    {
        public bool IsOpen {get;private set;}
        public string DevicePath {get{return "fixture";}}
        public byte[] Block;
        public int PacketReads, Aborts, ResultReads;
        public Action Completing;
        public bool Idle, FailReads, RequireFinishEsc, FailEsc;
        public bool Ready=true;
        public int Starts, EmptyPollsAfterEsc;
        public List<byte> Commands=new List<byte>();
        int packet, escAtStart;
        public bool Open(out string error) { error=null; IsOpen=true; return true; }
        public void Close() { IsOpen=false; }
        public byte State() { return 0; }
        public byte[] ReadSda(int length)
        {
            byte[] b=new byte[Math.Max(length,24)];
            byte[] text=System.Text.Encoding.ASCII.GetBytes("SPIROLAB J22889");
            Array.Copy(text,b,text.Length); return b;
        }
        public bool WaitReady(int timeoutMs) { if(!Ready) return false; packet=0; PacketReads=0; escAtStart=Aborts; return true; }
        public byte[] Get(byte command,int length)
        {
            Commands.Add(command);
            if(command==0x08) {Starts++; return new byte[] {0x40,0,0x14};}
            if(command==0x01)
            {
                PacketReads++;
                if(FailReads) return null;
                if(packet++==0) return new byte[] {0x10,0,200};
                if(packet==2) return new byte[] {0x10,0,210};
                if(Idle || (RequireFinishEsc && Aborts==escAtStart)) return new byte[] {0xFF,0,0};
                if(Aborts>escAtStart && EmptyPollsAfterEsc-->0) return new byte[] {0xFF,0,0};
                if(Completing!=null) Completing();
                return new byte[] {0x04,0,0};
            }
            return null;
        }
        public bool Send(byte command) { Commands.Add(command); if(command==0x1B) {Aborts++;return !FailEsc;} return true; }
        public byte[] ReadBlock(byte command,int attempts,int delayMs) { Commands.Add(command); ResultReads++; return Block; }
    }

    static class Program
    {
        static int checks;
        static string root;
        static bool requirePendingEnd;
        static void Check(bool ok,string name) { checks++; if(!ok) throw new Exception(name); Console.WriteLine("PASS "+name); }
        static void Near(double? actual,double expected,string name) { Check(actual.HasValue && Math.Abs(actual.Value-expected)<0.05,name+" ("+(actual.HasValue?actual.Value.ToString("R"):"null")+")"); }
        static Dictionary<string,object> Obj(object o) { return (Dictionary<string,object>)o; }
        static double N(object o) { return Convert.ToDouble(o,System.Globalization.CultureInfo.InvariantCulture); }
        static Trial Numeric(Patient p,int seed,double fvc,double? fev1,double? pef,double? fef,double fet,bool acceptable)
        {
            Trial t=Simulator.Build(p,seed);
            if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,p);
            AnalysisResult r=t.Result;
            r.Ref=Ers93.Compute(p);
            r.Valid=true; r.Fvc=fvc; r.FvcAvailable=true;
            r.Fev1=fev1.HasValue?fev1.Value:0; r.Fev1Available=fev1.HasValue;
            r.Pef=pef.HasValue?pef.Value:0; r.PefAvailable=pef.HasValue;
            r.Fef2575=fef.HasValue?fef.Value:0; r.Fef2575Available=fef.HasValue;
            r.Fet=fet; r.FetAvailable=true;
            r.BevAvailable=acceptable; r.BevOk=acceptable; r.PlateauOk=acceptable; r.Cough=false;
            r.CurveUsable=true; r.CurveProblem="";
            return t;
        }
        static Trial FromFixture(Patient p,object row,int seed)
        {
            var o=Obj(row);
            double? fev=Num(o,"FEV1"),pef=Num(o,"PEF"),fef=Num(o,"FEF25_75");
            double fet=o.ContainsKey("FET")?N(o["FET"]):6.0;
            return Numeric(p,seed,N(o["FVC"]),fev,pef,fef,fet,true);
        }
        static double? Num(Dictionary<string,object> o,string key) { object v; return o.TryGetValue(key,out v) && v!=null?N(v):(double?)null; }
        static byte[] DeviceBlock(double fvc,double fev1,double pef,double fet)
        {
            byte[] b=new byte[160];
            WriteU16(b,0x10,(int)Math.Round(fvc*100));
            WriteU16(b,0x12,(int)Math.Round(fev1*100));
            WriteU16(b,0x14,(int)Math.Round(pef*100));
            WriteU16(b,0x1A,(int)Math.Round(fet*100));
            WriteU16(b,0x50,187);
            return b;
        }
        static void WriteU16(byte[] b,int off,int value) { b[off]=(byte)((value>>8)&255); b[off+1]=(byte)(value&255); }
        static Trial Vext(double ml) { return new Trial { ManeuverId=Guid.NewGuid().ToString("N"),AcquisitionSequence=1,Completed=true,Result=new AnalysisResult {Valid=true,BevAvailable=true,Bev=ml/1000.0} }; }

        static void Main(string[] args)
        {
            try
            {
                root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
                requirePendingEnd=args.Length>1 && args[1]=="--require-pending-end";
                Patient p=new Patient { Age=41,HeightCm=182,WeightKg=78,Sex=Sex.Male,Origin=Origin.Caucasian,NationalId="0000000001",FirstName="synthetic",LastName="fixture",HLinePersonId="P-TEST",HLineRecordId="R-TEST",HLineRecordDate="2026/11/01",HLineRecordStatus="complete" };
                FixtureTests(p);
                IndependentBestTests(p);
                DeviceProvenanceTests(p);
                DeviceSessionTests();
                VextTests();
                SnapshotAndHLineTests(p);
                Console.WriteLine("ALL "+checks+" MIR / BEST VALUES CHECKS PASSED"); return;
            }
            catch(Exception ex) { Console.WriteLine(ex); Environment.ExitCode=1; }
        }

        static void FixtureTests(Patient p)
        {
            foreach(string letter in new string[] {"a","b","d"})
            {
                var fixture=Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText("Tests/Fixtures/mir-reference-"+letter+".json")));
                var input=Obj(fixture["input"]);
                string sex=Convert.ToString(input["sex"]),origin=Convert.ToString(input["origin"]);
                Patient fp=new Patient { Age=Convert.ToInt32(input["age"]),HeightCm=N(input["heightCm"]),WeightKg=N(input["weightKg"]),Sex=string.Equals(sex,"Female",StringComparison.OrdinalIgnoreCase)?Sex.Female:Sex.Male,Origin=string.Equals(origin,"Caucasian",StringComparison.OrdinalIgnoreCase)?Origin.Caucasian:Origin.Unspecified };
                List<Trial> trials=new List<Trial>(); int seed=100;
                foreach(object row in (object[])fixture["maneuvers"]) trials.Add(FromFixture(fp,row,seed++));
                ExamSummary s=ExamSummary.Build(trials);
                object[] best=(object[])fixture["best"],pct=(object[])fixture["percent"];
                Near(s.Fvc,N(best[0]),"fixture "+letter+" best FVC");
                Near(s.Fev1,N(best[1]),"fixture "+letter+" best FEV1");
                Near(s.Ratio,N(best[2]),"fixture "+letter+" derived ratio");
                Near(s.Pef,N(best[3]),"fixture "+letter+" best PEF");
                Near(s.Fef2575,N(best[4]),"fixture "+letter+" FEF25-75 from largest FVC plus FEV1");
                Reference reference=s.BestValues.Reference;
                double[] pred={reference.Fvc,reference.Fev1,reference.Fev1Fvc,reference.Pef,reference.Fef2575};
                double[] got={s.Fvc,s.Fev1,s.Ratio,s.Pef,s.Fef2575};
                for(int i=0;i<5;i++) Check(Fmt.N(got[i]/pred[i]*100,0)==Fmt.N(N(pct[i]),0),"fixture "+letter+" percent predicted "+i);
            }
            var c=Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText("Tests/Fixtures/mir-reference-c.json")));
            Patient cp=new Patient { Age=62,HeightCm=158,WeightKg=61,Sex=Sex.Female,Origin=Origin.Unspecified };
            List<Trial> ct=new List<Trial>(); int cs=300; foreach(object row in (object[])c["maneuvers"]) ct.Add(FromFixture(cp,row,cs++));
            ExamSummary csSummary=ExamSummary.Build(ct);
            Near(csSummary.Fvc,3.20,"short-expiration best FVC remains available");
            Check(!csSummary.Fev1Available && !csSummary.Fef2575Available && !csSummary.BestValues.Fev1Fvc.Available,"short expiration leaves FEV1 and FEF25-75 unavailable, never zero");
            Check(!csSummary.Fev1Available && csSummary.Fvc>0 && ExamSummary.CanReport(ct[0]),"FVC-only eligibility does not require a usable FEV1");
        }

        static void IndependentBestTests(Patient p)
        {
            Trial a=Numeric(p,1,5.10,3.80,7.90,4.40,6,true);
            Trial b=Numeric(p,2,4.95,4.15,8.60,5.00,6,true);
            Trial c=Numeric(p,3,5.05,3.60,9.56,4.30,6,true);
            ExamSummary s=ExamSummary.Build(new Trial[] {a,b,c});
            Near(s.Fvc,5.10,"independent best FVC"); Near(s.Fev1,4.15,"independent best FEV1"); Near(s.Pef,9.56,"independent best PEF"); Near(s.Ratio,4.15/5.10*100,"ratio from independent maxima");
            Check(s.BestValues.Fvc.SourceManeuverId==a.ManeuverId && s.BestValues.Fev1.SourceManeuverId==b.ManeuverId && s.BestValues.Pef.SourceManeuverId==c.ManeuverId,"each best value records its own source");
            Near(s.Fef2575,5.00,"FEF25-75 comes from largest FVC plus FEV1");
            Check(s.BestValues.Fef2575.SourceManeuverId==b.ManeuverId,"FEF source is the largest-sum maneuver");
            Trial rejected=Numeric(p,4,8.00,7.00,12.00,8.00,6,true); rejected.OperatorRejected=true;
            Trial incomplete=Numeric(p,5,9.00,8.00,13.00,8.00,6,true); incomplete.Completed=false; incomplete.EndReason="synthetic stop";
            ExamSummary filtered=ExamSummary.Build(new Trial[] {a,rejected,incomplete});
            Near(filtered.Fvc,5.10,"rejected and incomplete trials cannot raise the aggregate");
            Check(filtered.Candidates.Count==1 && filtered.Excluded==2,"excluded trials remain visible in the session count but not the aggregate");
            ExamSummary displayed=ExamSummary.Build(new Trial[] {a,b,c}); double fvc=displayed.Fvc;
            displayed.SetDisplay(new Trial[] {c});
            Check(displayed.Trials.Count==1 && displayed.Trials[0]==c && Math.Abs(displayed.Fvc-fvc)<1e-12,"display selection never changes the aggregate");
            displayed.SetDisplay(new Trial[] {});
            Check(displayed.Trials.Count==1 && Math.Abs(displayed.Fvc-fvc)<1e-12,"empty manual display falls back to a real report representative without changing best values");
            Trial shortTrial=Numeric(p,6,3.00,1.50,4.00,2.00,.75,true);
            ExamSummary shortOnly=ExamSummary.Build(new Trial[] {shortTrial});
            Near(shortOnly.Fvc,3.00,"FVC remains available when FEV1 is shorter than one second");
            Check(!shortOnly.Fev1Available && !shortOnly.BestValues.Fev1Fvc.Available,"sub-one-second FEV1 is unavailable rather than accepted");
            Check(shortOnly.BestValues.Fvc.SourceManeuverId==shortTrial.ManeuverId,"FVC source remains explicit in a partial maneuver");
            Trial noFev1=Numeric(p,7,4.00,null,5.00,null,6,true);
            ExamSummary noFev=ExamSummary.Build(new Trial[] {noFev1});
            Check(noFev.FvcAvailable && !noFev.Fev1Available && !noFev.Fef2575Available,"missing FEV1 never becomes a zero-valued best");
            Trial metadata=Numeric(p,8,3.10,1.25,5.00,2.00,.70,true);
            metadata.Result.AvailabilityMetadata=true; metadata.Result.Fev1Available=false;
            Check(metadata.Result.Fev1>0,"test setup retains a positive legacy-like FEV1 numeric field");
            Check(!metadata.Measurements.Fev1.Available,"modern availability metadata overrides a positive but unavailable FEV1 field");
        }

        static void DeviceProvenanceTests(Patient p)
        {
            Trial t=Numeric(p,20,4.74,1.67,1.92,1.10,7.5,true);
            t.Simulated=false; t.Imported=false; t.DeviceSerial="J22889"; t.DeviceCaptureSequence=7; t.RequiresDeviceCore=true;
            t.DeviceValues=new DeviceResults { Present=true,BlockValid=true,Associated=true,Association="fixture",CaptureSequence=7,SerialNumber="J22889",Fvc=4.78,Fev1=1.81,Pef=1.92,Fet=7.20,FvcValid=true,Fev1Valid=true,PefValid=true,FetValid=true,Raw=DeviceBlock(4.78,1.81,1.92,7.20) };
            ManeuverMeasurementSet m=t.Measurements;
            Near(m.Fvc.Value,4.78,"verified device FVC is primary");
            Check(m.Fvc.Source==MeasurementSource.DeviceReported && m.Fvc.ValidationStatus=="verified_capture_association","device source is explicit and capture bound");
            Near(m.Reconstructed.Fvc.Value,4.74,"reconstructed FVC is preserved separately for parity diagnostics");
            Check(m.Reconstructed.Fvc.Source==MeasurementSource.SoftwareCalculated,"reconstruction is never relabelled as device");
            Near(m.Fev1.Value,1.81,"verified device FEV1 is primary");
            Near(m.Pef.Value,1.92,"verified device PEF is primary");
            Near(m.Fet.Value,7.20,"verified device FET is primary");
            ExamSummary s=ExamSummary.Build(new Trial[] {t});
            Near(s.Fvc,4.78,"session best uses the verified device value");
            Check(s.BestValues.Fvc.Source==MeasurementSource.DeviceReported,"session provenance preserves device source");
            t.DeviceCaptureSequence=8;
            ManeuverMeasurementSet mismatch=t.Measurements;
            Check(!mismatch.Fvc.Available && mismatch.Fvc.Source==MeasurementSource.Unavailable && mismatch.Reconstructed.Fvc.Source==MeasurementSource.SoftwareCalculated,"capture-token mismatch cannot become a clinical device value; reconstructed diagnostics remain separate");
            t.DeviceCaptureSequence=7; t.DeviceSerial="OTHER";
            Check(!t.Measurements.Fvc.Available && t.Measurements.Fvc.Source==MeasurementSource.Unavailable && t.Measurements.Reconstructed.Fvc.Available,"serial mismatch cannot become a clinical device value");
            t.DeviceSerial="J22889"; t.DeviceValues.Associated=false; t.DeviceValues.CaptureSequence=0; t.DeviceCaptureSequence=0;
            ManeuverMeasurementSet legacy=t.Measurements;
            Near(legacy.Fvc.Value,4.78,"persisted legacy device block remains available for review");
            Check(legacy.Fvc.Source==MeasurementSource.LegacyUnverified && legacy.Fvc.ValidationStatus.Contains("unverified"),"persisted block is never called device verified");
            t.DeviceValues.Associated=true; t.DeviceCaptureSequence=9;
            Check(!t.Measurements.Fvc.Available && t.Measurements.Fvc.Source==MeasurementSource.Unavailable,"associated block with wrong capture is unavailable as device or legacy");
            t.DeviceValues.Raw=null;
            Check(!t.Measurements.Fvc.Available && t.Measurements.Fvc.Source==MeasurementSource.Unavailable,"missing raw block cannot support device provenance");
        }

        static void DeviceSessionTests()
        {
            FixtureTransport pending=new FixtureTransport {RequireFinishEsc=true,Block=DeviceBlock(4.78,1.81,1.92,7.20)};
            using(DeviceSession session=new DeviceSession(pending))
            {
                string error; Check(session.Connect(out error),"pending-end fixture connects");
                Trial trial=new Trial();
                CaptureOutcome stopped=session.Capture(trial,delegate {return pending.PacketReads>=2;},delegate {return false;},trial.Packets.Add);
                Check(stopped.Completed && !stopped.Cancelled && stopped.Results!=null && stopped.Results.Present && pending.Aborts==1 && pending.ResultReads==1 && pending.PacketReads==3,
                    "manual finish sends one ESC, keeps polling for real 0x04 and reads capture-bound LAST");
                Near(stopped.Results.Fvc,4.78,"pending-end FVC comes from LAST"); Near(stopped.Results.Fev1,1.81,"pending-end FEV1 comes from LAST");
                Near(stopped.Results.Pef,1.92,"pending-end PEF comes from LAST"); Near(stopped.Results.Fet,7.20,"pending-end FET comes from LAST");
                Check(!session.ReadResults().Present && pending.ResultReads==1,
                    "pending-end LAST remains a single read for this capture");
                Check(pending.Commands.Count==6 && pending.Commands[3]==0x1B && pending.Commands[4]==0x01 && pending.Commands[5]==0xA2,
                    "manual finish command order is ESC, packet read, then LAST after completion");
                if(requirePendingEnd)
                    Check(stopped.Completed && stopped.Results!=null && stopped.Results.Present && pending.Aborts==1,
                        "acceptance: finish ESC followed by real 0x04 preserves the completed maneuver");
                CaptureOutcome duplicate=session.Capture(new Trial(),delegate {return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(duplicate.Completed && !duplicate.Results.Present && duplicate.Results.InvalidReason=="stale_duplicate_cod_LAST_block" && pending.Aborts==2,
                    "manual finish rejects the previous maneuver duplicate LAST without another finish ESC");
                pending.Block=DeviceBlock(4.95,2.15,2.60,8.10);
                CaptureOutcome next=session.Capture(new Trial(),delegate {return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(next.Completed && next.Results.Present && next.Results.CaptureSequence==3 && pending.Aborts==3 && pending.Starts==3,
                    "next ready maneuver gets its own fresh LAST after manual finish");
            }
            FixtureTransport repeated=new FixtureTransport {RequireFinishEsc=true,EmptyPollsAfterEsc=4,Block=DeviceBlock(4.78,1.81,1.92,7.20)};
            using(DeviceSession session=new DeviceSession(repeated))
            {
                string error; session.Connect(out error); int finishRequests=0;
                CaptureOutcome result=session.Capture(new Trial(),delegate {finishRequests++;return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(result.Completed && result.Results.Present && finishRequests>4 && repeated.Aborts==1 && repeated.ResultReads==1,
                    "repeated finish requests across delayed polls send exactly one ESC");
                Check(repeated.Commands.TrueForAll(delegate(byte c) {return c==0x08 || c==0x01 || c==0x1B || c==0xA2;}),"manual finish adds no CAN or unknown USB command");
            }
            bool stop=false;
            FixtureTransport completing=new FixtureTransport {Block=DeviceBlock(4.78,1.81,1.92,7.20),Completing=delegate {stop=true;}};
            using(DeviceSession session=new DeviceSession(completing))
            {
                string error; Check(session.Connect(out error),"in-flight-end fixture connects");
                Trial trial=new Trial();
                CaptureOutcome completed=session.Capture(trial,delegate {return stop;},delegate {return false;},trial.Packets.Add);
                Check(stop && completed.Completed && completed.Results.Present && completed.Results.CaptureSequence==trial.DeviceCaptureSequence && completing.Aborts==0 && completing.ResultReads==1,
                    "stop during the read returning 0x04 preserves the capture-bound four-field result");
                Check(trial.Packets.Exists(delegate(DevicePacket p) {return p.Type==0x04;}),"in-flight completion packet is retained in raw evidence");
            }
            FixtureTransport idle=new FixtureTransport {Idle=true,Block=DeviceBlock(4.78,1.81,1.92,7.20)};
            using(DeviceSession session=new DeviceSession(idle))
            {
                string error; session.Connect(out error); Trial trial=new Trial(); Stopwatch watch=Stopwatch.StartNew();
                CaptureOutcome timedOut=session.Capture(trial,delegate {return idle.PacketReads>=2;},delegate {return false;},trial.Packets.Add);
                Check(!timedOut.Completed && timedOut.Cancelled && timedOut.Results==null && idle.Aborts==1 && idle.ResultReads==0 && trial.Packets.Count==2,
                    "no 0x04 expires finish wait, aborts and preserves raw samples without fabricated LAST");
                Check(watch.Elapsed.TotalSeconds>=9.8 && watch.Elapsed.TotalSeconds<14 && !idle.IsOpen,"finish wait is bounded to ten seconds after ESC and closes an unconfirmed session");
                Check(!session.ReadResults().Present && idle.ResultReads==0,"timeout cannot associate stale LAST with cancelled maneuver");
                idle.Ready=false; session.Connect(out error);
                CaptureOutcome notReady=session.Capture(new Trial(),delegate {return false;},delegate(DevicePacket p) {});
                Check(!notReady.Completed && idle.Starts==1 && idle.ResultReads==0,"after failed finish a device that is not ready cannot start the next FVC");
                idle.Ready=true; idle.Idle=false; idle.RequireFinishEsc=true; idle.Block=DeviceBlock(4.95,2.15,2.60,8.10);
                CaptureOutcome recovered=session.Capture(new Trial(),delegate {return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(recovered.Completed && recovered.Results.Present && recovered.Results.CaptureSequence==2 && idle.Starts==2,
                    "after reconnect and ready confirmation a new maneuver can complete with fresh LAST");
            }
            FixtureTransport cancelled=new FixtureTransport {Idle=true};
            using(DeviceSession session=new DeviceSession(cancelled))
            {
                string error; session.Connect(out error);
                CaptureOutcome aborted=session.Capture(new Trial(),delegate {return true;},delegate {return cancelled.PacketReads>=4;},delegate(DevicePacket p) {});
                Check(aborted.Cancelled && !aborted.Completed && aborted.Results==null && cancelled.Aborts==2 && cancelled.ResultReads==0 && cancelled.PacketReads==4,
                    "immediate cancel remains available during finish wait without waiting for its deadline");
            }
            FixtureTransport sendFailure=new FixtureTransport {FailEsc=true};
            using(DeviceSession session=new DeviceSession(sendFailure))
            {
                string error; session.Connect(out error);
                CaptureOutcome failed=session.Capture(new Trial(),delegate {return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(!failed.Completed && failed.Results==null && failed.Reason.Contains("ارسال فرمان پایان دستی") && sendFailure.Aborts==1 && sendFailure.ResultReads==0 && !sendFailure.IsOpen,
                    "failed manual ESC send reports the cause and disconnects without retry or fabricated result");
            }
            FixtureTransport disconnected=new FixtureTransport {FailReads=true};
            using(DeviceSession session=new DeviceSession(disconnected))
            {
                string error; session.Connect(out error);
                CaptureOutcome failed=session.Capture(new Trial(),delegate {return true;},delegate {return false;},delegate(DevicePacket p) {});
                Check(!failed.Completed && failed.Results==null && !disconnected.IsOpen && disconnected.Aborts==1 && disconnected.ResultReads==0,
                    "USB loss during finish wait aborts and disconnects without a clinical result");
            }
            bool cancelOnEnd=false;
            FixtureTransport emergency=new FixtureTransport {Block=DeviceBlock(4.78,1.81,1.92,7.20),Completing=delegate {cancelOnEnd=true;}};
            using(DeviceSession session=new DeviceSession(emergency))
            {
                string error; session.Connect(out error);
                CaptureOutcome cancelledEnd=session.Capture(new Trial(),delegate {return false;},delegate {return cancelOnEnd;},delegate(DevicePacket p) {});
                Check(cancelledEnd.Cancelled && !cancelledEnd.Completed && cancelledEnd.Results==null && emergency.Aborts==1 && emergency.ResultReads==0,
                    "emergency cancellation at completion does not register a clinical result");
                Check(!session.ReadResults().Present && emergency.ResultReads==0,"emergency cancel invalidates completed-capture association");
            }
            FixtureTransport usb=new FixtureTransport();
            using(DeviceSession session=new DeviceSession(usb))
            {
                string error; Check(session.Connect(out error),"fixture session connects");
                Check(session.SerialNumber=="J22889","SDA serial parsed for association");
                usb.Block=DeviceBlock(4.78,1.81,1.92,7.20);
                Trial first=new Trial(); CaptureOutcome one=session.Capture(first,delegate{return false;},delegate(DevicePacket packet) { });
                first.DeviceValues=one.Results;
                Check(one.Completed && one.Results.Present && one.Results.Associated && one.Results.CaptureSequence==1,"natural completion binds one result to its capture");
                Near(one.Results.Fvc,4.78,"captured LAST block decodes FVC"); Near(one.Results.Fev1,1.81,"captured LAST block decodes FEV1");
                Near(one.Results.Pef,1.92,"captured LAST block decodes PEF"); Near(one.Results.Fet,7.20,"captured LAST block decodes FET");
                Check(one.Results.FvcValid && one.Results.Fev1Valid && one.Results.PefValid && one.Results.FetValid,"captured fields pass observed range checks");
                usb.Block=DeviceBlock(4.78,1.81,1.92,7.20);
                Trial duplicate=new Trial(); CaptureOutcome two=session.Capture(duplicate,delegate{return false;},delegate(DevicePacket packet) { });
                duplicate.DeviceValues=two.Results;
                Check(two.Completed && !two.Results.Present && two.Results.InvalidReason=="stale_duplicate_cod_LAST_block","byte-identical LAST block for a new capture is rejected as stale");
                usb.Block=DeviceBlock(4.79,1.82,1.93,7.21);
                Trial changed=new Trial(); CaptureOutcome three=session.Capture(changed,delegate{return false;},delegate(DevicePacket packet) { });
                changed.DeviceValues=three.Results;
                Check(three.Completed && three.Results.Present && three.Results.CaptureSequence==3,"a genuinely changed LAST block is accepted for the new capture");
                usb.Block=DeviceBlock(4.70,0,1.90,7.10);
                Trial partial=new Trial(); CaptureOutcome partialOutcome=session.Capture(partial,delegate{return false;},delegate(DevicePacket packet) { });
                partial.DeviceValues=partialOutcome.Results;
                Check(partialOutcome.Completed && !partialOutcome.Results.Present && !partialOutcome.Results.Fev1Valid && partialOutcome.Results.InvalidReason.Contains("required_core"),"all four required device core fields are required for a clinical result");
                usb.Block=new byte[8];
                Trial truncated=new Trial(); CaptureOutcome four=session.Capture(truncated,delegate{return false;},delegate(DevicePacket packet) { });
                truncated.DeviceValues=four.Results;
                Check(four.Completed && !four.Results.Present && four.Results.InvalidReason=="truncated_cod_LAST_block","truncated LAST block is rejected, not partially decoded");
            }
        }

        static void VextTests()
        {
            VextSelection a=ClinicVext.Select(new Trial[] {Vext(110),Vext(132),Vext(105)});
            Check(a.Available && a.ValueMl==132,"VEXT example 110/132/105 selects 132");
            VextSelection b=ClinicVext.Select(new Trial[] {Vext(230),Vext(185),Vext(160)});
            Check(b.Available && b.ValueMl==185,"VEXT example 230/185/160 excludes 230 and selects 185");
            VextSelection c=ClinicVext.Select(new Trial[] {Vext(200),Vext(201),Vext(0)});
            Check(!c.Available && c.ValueMl==0,"VEXT example 200/201/0 is unavailable, never zero-filled");
            Trial simulated=Vext(150); simulated.Simulated=true;
            Check(!ClinicVext.Select(new Trial[] {simulated}).Available,"simulated EVol is not a clinic VEXT source");
            Trial replayed=Vext(150); replayed.Imported=true;
            Check(!ClinicVext.Select(new Trial[] {replayed}).Available,"replayed EVol is not a clinic VEXT source");
        }

        static void SnapshotAndHLineTests(Patient p)
        {
            Trial a=Numeric(p,30,5.10,3.80,7.90,4.40,6,true);
            Trial b=Numeric(p,31,4.95,4.15,8.60,5.00,6,true);
            Trial c=Numeric(p,32,5.05,3.60,9.56,4.30,6,true);
            foreach(Trial t in new Trial[] {a,b,c}) t.Simulated=false;
            Exam exam=Exam.CreateNew(new AppSettings {OperatorName="synthetic operator"}); exam.Patient=p;
            exam.AddManeuver(a); exam.AddManeuver(b); exam.AddManeuver(c);
            ExamSummary expected=ExamSummary.Build(exam.Trials);
            double bestFvc=expected.Fvc,bestFev1=expected.Fev1,bestPef=expected.Pef,bestFef=expected.Fef2575;
            ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(exam);
            var doc=Obj(new JavaScriptSerializer().DeserializeObject(snapshot.ToJson()));
            var best=Obj(doc["bestValues"]);
            Near(N(Obj(best["fvc"])["value"]),bestFvc,"JSON best FVC equals the authoritative aggregate");
            Near(N(Obj(best["fev1"])["value"]),bestFev1,"JSON best FEV1 equals the authoritative aggregate");
            Near(N(Obj(best["fev1Fvc"])["value"]),bestFev1/bestFvc*100,"JSON derived ratio equals best FEV1 / best FVC");
            Near(N(Obj(best["pef"])["value"]),bestPef,"JSON best PEF equals the authoritative aggregate");
            Near(N(Obj(best["fef2575"])["value"]),bestFef,"JSON FEF25-75 equals the authoritative aggregate");
            var spi=Obj(doc["spirometry"]);
            Near(N(Obj(spi["FVC"])["value"]),bestFvc,"schema2 spirometry FVC mirrors best values");
            var display=(object[])Obj(doc["report"])["displayedManeuvers"];
            Check(display.Length==3,"automatic report displays three real curves");
            ResultPackage package=ResultExport.SaveToDirectory(root,snapshot);
            SubmissionDraft draft=new SubmissionDraft(snapshot,package);
            Near(draft.Fields["fvc_amount"].Value,bestFvc,"HLine FVC uses the same best values");
            Near(draft.Fields["fev1_amount"].Value,bestFev1,"HLine FEV1 uses the same best values");
            Near(draft.Fields["fev1_fvc_percent"].Value,bestFev1/bestFvc*100,"HLine ratio uses the same best values");
            Near(draft.Fields["pef_amount"].Value,bestPef,"HLine PEF uses the same best values");
            Near(draft.Fields["fef2575_amount"].Value,bestFef,"HLine FEF25-75 uses the same best values");
            Check(draft.JpegBytes.Length==new FileInfo(package.ReportPath).Length,"HLine retains the exact JPG frozen with the JSON snapshot");
            using(Bitmap image=SpiroReport.Render(snapshot)) Check(image.Width>0 && image.Height>0,"report image renders from the same snapshot");
            string[] ids=exam.ReportManeuverIds.ToArray();
            exam.SetReportManeuver(ids[0],false);
            ExaminationSnapshot manual=ExaminationSnapshot.Capture(exam);
            var manualDoc=Obj(new JavaScriptSerializer().DeserializeObject(manual.ToJson()));
            var manualBest=Obj(manualDoc["bestValues"]);
            Near(N(Obj(manualBest["fvc"])["value"]),bestFvc,"manual report selection never changes JSON best FVC");
            Near(N(Obj(manualBest["fev1"])["value"]),bestFev1,"manual report selection never changes JSON best FEV1");
            var manualDisplay=(object[])Obj(manualDoc["report"])["displayedManeuvers"];
            Check(manualDisplay.Length==2,"manual report display contains only the selected real curves");
            Check(Obj(manualDoc["reportSelection"])["method"].ToString()=="manual","manual report provenance is explicit");
            exam.SetReportManeuver(ids[0],true);
            examination(exam,root);
        }

        static void examination(Exam exam,string root)
        {
            exam.FinalizeExamination();
            string path=Path.Combine(root,"mir-best-values.spiro");
            ExamStore.Save(path,exam);
            Exam reopened=ExamStore.Load(path);
            Check(reopened.FinalizedAt.HasValue && reopened.ReportManeuverIds.Count==3,"MIR examination finalizes and reopens with three report curves");
        }
    }
}

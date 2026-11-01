using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;

internal static class RegressionTests
{
    private static int checks;
    private static string output;
    private static void Check(bool ok,string name) { if(!ok) throw new Exception(name); checks++; Console.WriteLine("PASS "+name); }
    private static void Near(double actual,double expected,double tolerance,string name) { Check(Math.Abs(actual-expected)<=tolerance,name+" ("+actual.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+")"); }
    private static object Field(object obj,string name) { return obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj); }
    private static void Set(object obj,string name,object value) { obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value); }
    private static void Call(object obj,string name,params object[] values) { obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,values); }
    private sealed class FakeTransport : IMirTransport
    {
        public bool IsOpen { get; private set; }
        public string DevicePath { get { return "fake"; } }
        public int Aborts, ResultReads;
        public bool FailReads;
        public byte[] Step = { 0x30, 1, 44 };
        public bool Open(out string error) { error=null; IsOpen=true; return true; }
        public FakeTransport() { IsOpen=true; }
        public void Close() { IsOpen=false; }
        public byte State() { return 0; }
        public byte[] ReadSda(int n) { return null; }
        public bool WaitReady(int timeout) { return true; }
        public byte[] Get(byte command,int n)
        { if(command==8) return Step; return FailReads?null:new byte[] {4,0,0}; }
        public bool Send(byte command) { if(command==0x1B) Aborts++; return true; }
        public byte[] ReadBlock(byte command,int attempts,int delay) { ResultReads++; return null; }
    }
    private static bool HasTab(Control parent) { foreach(Control child in parent.Controls) if(child is TabControl || HasTab(child)) return true; return false; }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            output=Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            Near(Fmt.Parse("۱۷۸٫۵",0),178.5,.001,"Persian numbers"); Near(Fmt.Parse("١٧٨",0),178,.001,"Arabic numbers");
            Check(double.IsNaN(Fmt.Parse("bad",double.NaN)),"invalid numeric input rejected"); Check(double.IsNaN(Fmt.Parse("Infinity",double.NaN)),"nonfinite rejected");
            Patient p=new Patient { FirstName="آزمایش",LastName="نرم‌افزار",Age=34,HeightCm=178,WeightKg=82,Note="تست یادداشت" };
            Near(Ers93.Compute(p).Fvc,5.0288,.0001,"male ERS93 FVC"); Near(Ers93.Compute(p).FvcLln,4.02535,.0001,"male ERS93 LLN");
            Check(!Ers93.Compute(new Patient { Age=12 }).Applicable,"no pediatric extrapolation");
            Check(!Ers93.Compute(new Patient { HeightCm=210 }).Applicable,"no height extrapolation");
            Near(Ers93.Compute(new Patient { Age=18 }).Fvc,Ers93.Compute(new Patient { Age=25 }).Fvc,.0001,"adult age 18-25 rule");
            Check(PersianDate.Format(new DateTime(2026,10,31))=="1405/08/09","Persian date");
            Patient printed=new Patient { FirstName="مراجع",LastName="نمونه",NationalId="0012345678",Age=41,HeightCm=182,WeightKg=60,Sex=Sex.Male,Origin=Origin.Caucasian };
            Reference printedRef=Ers93.Compute(printed);
            string[] expected={"5.08","4.15","79.8","9.56","8.28","5.28","2.34","4.47"};
            for(int i=0;i<expected.Length;i++) Check(Fmt.N(SpiroReport.Predicted(printedRef,i),i==2?1:2)==expected[i],"ERS reference matches device photo: "+SpiroReport.MetricName(i));
            Patient changed=printed.Clone(); changed.WeightKg=90; Near(Ers93.Compute(changed).Fvc,printedRef.Fvc,.0001,"ERS predictions do not use weight");
            changed.Origin=Origin.Other; Check(!Ers93.Compute(changed).Applicable,"no invented origin correction");
            Check(new Patient().Origin==Origin.Unspecified,"legacy patient origin remains unspecified");
            List<Trial> comparison=new List<Trial>();
            for(int i=1;i<=3;i++) { Trial blow=Simulator.Build(printed,i); blow.Result=SpiroAnalysis.Analyze(blow,printed); comparison.Add(blow); }
            using(Bitmap report=SpiroReport.Render(printed,comparison)) { Check(report.Width==2400 && report.Height==3394,"one-page report image dimensions"); report.Save(Path.Combine(output,"sample-report.png")); }
            using(Bitmap curves=SpiroReport.RenderCurves(comparison)) curves.Save(Path.Combine(output,"sample-curves.png"));
            ExamSummary aggregate=ExamSummary.Build(comparison); Check(aggregate.Trials.Count==3,"report overlays three maneuvers");
            double maxFvc=0,maxFev1=0,maxPef=0; foreach(Trial blow in comparison) { maxFvc=Math.Max(maxFvc,blow.Result.Fvc); maxFev1=Math.Max(maxFev1,blow.Result.Fev1); maxPef=Math.Max(maxPef,blow.Result.Pef); }
            Near(aggregate.Fvc,maxFvc,.0001,"best FVC across maneuvers"); Near(aggregate.Fev1,maxFev1,.0001,"best FEV1 across maneuvers"); Near(aggregate.Pef,maxPef,.0001,"best PEF across maneuvers");
            Near(aggregate.Ratio,maxFev1/maxFvc*100,.0001,"best ratio uses independent maximum volumes");
            Trial oversized=Simulator.Build(printed,23); oversized.Result=SpiroAnalysis.Analyze(oversized,printed); oversized.Result.Fvc=20; oversized.Completed=false; comparison.Add(oversized);
            Check(ExamSummary.Build(comparison).Fvc<20,"partial maneuver excluded from final report");
            comparison[0].OperatorAccepted=true; aggregate=ExamSummary.Build(comparison); Check(aggregate.Candidates.Count==3,"all completed maneuvers participate in automatic selection");
            changed=printed.Clone(); changed.Origin=Origin.Unspecified; Near(Ers93.Compute(changed).Fev1,printedRef.Fev1,.0001,"No group selected matches unchanged ERS in photo");
            ResultPackage package=ResultExport.Save(Path.Combine(output,"website-ready.png"),printed,comparison);
            Check(File.Exists(package.ReportPath) && File.Exists(package.CurvesPath) && File.Exists(package.DataPath),"website package contains report, curves and structured data");
            var json=new System.Web.Script.Serialization.JavaScriptSerializer().DeserializeObject(File.ReadAllText(package.DataPath)) as System.Collections.Generic.Dictionary<string,object>;
            Check((int)json["schemaVersion"]==2 && ((Dictionary<string,object>)json["provenance"])["source"].ToString()=="simulation","website JSON schema and provenance");
            Check(File.ReadAllText(package.DataPath).Contains("bestManeuverIndex") && !File.ReadAllText(package.DataPath).Contains("Warnings"),"site adapter payload has values without display diagnostics");
            FakeTransport natural=new FakeTransport();
            using(DeviceSession device=new DeviceSession(natural))
            {
                CaptureOutcome outcome=device.Capture(new Trial(),delegate { return false; },delegate(DevicePacket packet) { });
                Check(outcome.Completed && natural.Aborts==0 && natural.ResultReads==1,"natural completion reads results without ESC");
            }
            FakeTransport aborted=new FakeTransport();
            using(DeviceSession device=new DeviceSession(aborted))
            {
                CaptureOutcome outcome=device.Capture(new Trial(),delegate { return true; },delegate(DevicePacket packet) { });
                Check(!outcome.Completed && aborted.Aborts==1 && aborted.ResultReads==0,"manual abort sends ESC and never reads stale results");
            }
            FakeTransport failure=new FakeTransport { FailReads=true };
            using(DeviceSession device=new DeviceSession(failure))
            {
                CaptureOutcome outcome=device.Capture(new Trial(),delegate { return false; },delegate(DevicePacket packet) { });
                Check(!outcome.Completed && failure.Aborts==1 && failure.ResultReads==0 && !failure.IsOpen,"read failure aborts, disconnects and preserves incomplete state");
            }
            FakeTransport badStep=new FakeTransport { Step=new byte[] {0x30,0,0} };
            using(DeviceSession device=new DeviceSession(badStep))
            {
                CaptureOutcome outcome=device.Capture(new Trial(),delegate { return false; },delegate(DevicePacket packet) { });
                Check(!outcome.Completed && badStep.Aborts==1,"invalid step cancels started device maneuver");
            }
            Trial constant=new Trial(); for(int i=0;i<201;i++) constant.Flow.Add(2);
            AnalysisResult cr=SpiroAnalysis.Analyze(constant,p); Near(cr.Fev1,2,.025,"fixed-rate timed volume"); Near(cr.Fvc,4.02,.025,"fixed-rate FVC");
            Trial t=ExamStore.ImportCapture(args[1],.030); t.Result=SpiroAnalysis.Analyze(t,p);
            Check(t.Completed && t.Imported && !t.Simulated,"development fixture provenance and natural end"); Check(t.Flow.Count==276,"all 276 device flow packets decoded");
            Near(t.Flow[0],-.34,.0001,"signed MIR flow"); Near(t.Result.Fvc,4.74,.001,"inspiration excluded from FVC");
            Near(t.Result.Fev1,1.6676,.001,"unverified development replay interpolated FEV1"); Near(t.Result.Pef,1.92,.001,"unverified development replay peak flow");
            Check(t.Result.Fet>7 && t.Result.Fet<9,"irregular sample timestamps used");
            Expiration signal=Expiration.From(t); Near(signal.Volume[signal.Count-1],t.Result.Fvc,.0001,"chart and analysis share volume");
            Near(signal.VolumeAt(1),t.Result.Fev1,.0001,"chart and analysis share time origin");
            Trial timeTrial=new Trial { StepType=0x40,Step=.02 }; PacketDecoder decoder=new PacketDecoder();
            for(int i=0;i<100;i++) decoder.Add(timeTrial,new DevicePacket { Seconds=i*.3,Type=0x10,Raw=200 });
            Near(timeTrial.Time[99],2,.0001,"time-step mode uses device clock"); Near(timeTrial.Volume[99],4,.0001,"time-step integration");
            Trial volumeTrial=new Trial { StepType=0x40,Step=.02 }; decoder=new PacketDecoder();
            for(int i=0;i<100;i++) decoder.Add(volumeTrial,new DevicePacket { Seconds=i*.3,Type=0x20,Raw=5004+4*i });
            Near(volumeTrial.Volume[99],4,.0001,"volume packets in time-step mode"); Near(volumeTrial.Flow[99],2,.0001,"flow derived from timed volume");
            Exam e=new Exam { Patient=p }; e.Trials.Add(t); string saved=Path.Combine(output,"roundtrip.spiro"); ExamStore.Save(saved,e); ExamStore.Save(saved,e);
            Exam loaded=ExamStore.Load(saved); Check(loaded.Trials[0].Packets.Count==t.Packets.Count,"raw packets survive round trip");
            Check(loaded.Trials[0].Imported && loaded.Patient.Note==p.Note && loaded.Patient.Origin==p.Origin,"patient, origin and provenance survive round trip"); Near(loaded.Trials[0].Result.Fvc,t.Result.Fvc,.0001,"results recomputed on load"); Check(File.Exists(saved+".bak"),"atomic save backup");
            ExamStore.ExportCsv(Path.Combine(output,"data.csv"),e); string csv=File.ReadAllText(Path.Combine(output,"data.csv")); Check(csv.Contains("Packet") && csv.Contains("Sample") && csv.Contains("FVC") && csv.Contains("BEST") && csv.Contains("Origin"),"CSV contains best values, origin, results and raw packets");
            string bad=Path.Combine(output,"bad.spiro");
            File.WriteAllText(bad,"<!DOCTYPE Exam [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Exam><Patient><FirstName>&x;</FirstName></Patient></Exam>");
            bool rejected=false; try { ExamStore.Load(bad); } catch { rejected=true; } Check(rejected,"XML external entities rejected");
            Trial malformed=t.Clone(); malformed.Time[2]=-1; Exam invalid=new Exam { Patient=p }; invalid.Trials.Add(malformed); ExamStore.Save(bad,invalid);
            rejected=false; try { ExamStore.Load(bad); } catch { rejected=true; } Check(rejected,"nonmonotonic sample timestamps rejected");
            p.FirstName="<script>alert(1)</script>";
            using(Bitmap bitmap=ReportForm.RenderCurves(t))
            {
                bitmap.Save(Path.Combine(output,"capture-curves.png")); string html=ReportForm.HtmlReport(p,t,1,"test",bitmap);
                Check(!html.Contains("<script>") && html.Contains("&lt;script&gt;"),"HTML escapes patient input"); Check(html.Contains("data:image/png;base64,") && html.Contains("بازپخش"),"standalone report embeds charts and source");
                File.WriteAllText(Path.Combine(output,"report.html"),html);
            }
            p.FirstName="آزمایش";
            using(ReportForm report=new ReportForm(p,t,1,"تست تکرارپذیری"))
            {

                for(int page=0;page<1;page++) using(Bitmap bitmap=new Bitmap(827,1169)) using(Graphics g=Graphics.FromImage(bitmap))
                { g.Clear(Color.White); PrintPageEventArgs pe=new PrintPageEventArgs(g,new Rectangle(45,45,737,1079),new Rectangle(0,0,827,1169),new PageSettings()); Call(report,"PrintPage",report,pe); bitmap.Save(Path.Combine(output,"report-page-"+page+".png")); }
            }
            using(MainForm form=new MainForm())
            {
                form.Show(); Application.DoEvents(); Set(form,"_path",Path.Combine(output,"ui-demo.spiro")); form.RunDemoForTest();
                for(int i=0;i<20 && (bool)Field(form,"_busy");i++) Call(form,"Tick",null,EventArgs.Empty);
                Exam demo=(Exam)Field(form,"_exam"); Check(demo.Trials.Count==1 && demo.Trials[0].Simulated && demo.Trials[0].Completed,"GUI simulation completes and is labelled");
                Check(form.Controls.Find("FirstName",true).Length==0 && demo.Patient.FirstName=="مراجع","patient snapshot preserved outside main screen");
                Check(((DataGridView)Field(form,"gridTrials")).Columns.Count==8,"GUI renders core metrics and two independent choices");
                Check(demo.Patient.Origin==Origin.Caucasian,"GUI defaults to Caucasian");
                Check(Ers93.Compute(demo.Patient).Applicable,"reference remains available without a main-screen technical field");
                Check(File.Exists(Path.Combine(output,"ui-demo.spiro")),"GUI autosaves completed trial");
                Check(!((Button)Field(form,"btnStop")).Enabled,"stop disabled after completion");

                Call(form,"StartSimulation"); Call(form,"Tick",null,EventArgs.Empty); Call(form,"StopTest");
                Check(demo.Trials.Count==2 && !demo.Trials[1].Completed,"manual stop remains incomplete"); Check(demo.Trials[1].Flow.Count<300,"stopped demo truncates unseen samples");
                Call(form,"SelectBest"); Check(object.ReferenceEquals(Field(form,"_selected"),demo.Trials[0]),"best selection excludes incomplete trial");
                form.SelectTab(0); Application.DoEvents();
                using(Bitmap bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(Path.Combine(output,"gui-test.png")); }

                using(ReportForm reportForm=new ReportForm(demo.Patient,demo.Trials))
                {
                    reportForm.Show(); Application.DoEvents();
                    using(Bitmap screenshot=new Bitmap(reportForm.Width,reportForm.Height)) { reportForm.DrawToBitmap(screenshot,reportForm.ClientRectangle); screenshot.Save(Path.Combine(output,"report-window.png")); }
                }
                Check(!HasTab(form),"single screen has no tabs");
                for(int extra=0;extra<4;extra++) { Call(form,"StartSimulation"); for(int tick=0;tick<30 && (bool)Field(form,"_busy");tick++) Call(form,"Tick",null,EventArgs.Empty); }
                Check(ExamSummary.Build(demo.Trials).Candidates.Count==5,"one to five complete maneuvers without mandatory minimum");
                Check(object.ReferenceEquals(Field(form,"_selected"),ExamSummary.Build(demo.Trials).BestTrial),"new maneuvers automatically update selected best");
                Call(form,"BeginExam",new Patient { FirstName="مراجع",LastName="جدید",NationalId="0000000001",Age=30,HeightCm=175,WeightKg=70,Origin=Origin.Caucasian },false); Check(((Exam)Field(form,"_exam")).Trials.Count==0 && (bool)Field(form,"_hasPatient"),"new patient creates a separate examination session");
            }
            Console.WriteLine("ALL "+checks+" CHECKS PASSED"); return 0;
        }
        catch(Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

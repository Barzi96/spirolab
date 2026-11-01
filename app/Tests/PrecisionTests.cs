using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;
internal static class PrecisionTests
{
    static int count;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; Console.WriteLine("PASS "+name); }
    static void Near(double actual,double expected,string name) { Check(Math.Abs(actual-expected)<1e-9,name); }
    static Dictionary<string,object> Obj(object value) { return (Dictionary<string,object>)value; }
    static double N(object value) { return Convert.ToDouble(value); }
    static object Field(object o,string name) { return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static object Call(object o,string name) { return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,null); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            string settingsPath=Path.Combine(root,"settings.xml");
            AppSettings defaults=AppSettings.Load(Path.Combine(root,"missing.xml"));
            Check(defaults.CenterName=="شرکت سنگ آهن مرکزی ایران - بافق" && defaults.OperatorName=="","exact center and empty operator defaults");
            AppSettings settings=new AppSettings { CenterName="مرکز تست",OperatorName="کارشناس تست" }; settings.Save(settingsPath);
            AppSettings restored=AppSettings.Load(settingsPath);
            Check(restored.CenterName==settings.CenterName && restored.OperatorName==settings.OperatorName,"settings persist across reload");
            Exam exam=Exam.CreateNew(restored); DateTime today=DateTime.Today;
            Check(exam.ExaminationDate==today && exam.ExaminationDateSource=="windows_local_date","new examination defaults to Windows local date");
            restored.CenterName="مرکز دیگر"; restored.OperatorName="کارشناس دیگر"; restored.Save(settingsPath);
            Check(exam.CenterName=="مرکز تست" && exam.OperatorName=="کارشناس تست","examination copies defaults without sharing mutable settings");
            Check(File.Exists(settingsPath+".bak"),"settings replacement preserves previous version");
            File.WriteAllText(settingsPath,"broken XML");
            Check(AppSettings.Load(settingsPath).LoadWarning.Length>0 && File.ReadAllText(settingsPath)=="broken XML","corrupt settings preserved with visible recovery status");
            DateTime date;
            Check(ExaminationDates.TryParse("۱۴۰۵/۰۸/۱۰",out date) && date==new DateTime(2026,11,1),"Persian digits and Jalali normalize to Gregorian");
            Check(ExaminationDates.TryParse("2024-02-29",out date) && date.Day==29,"Gregorian leap day accepted");
            Check(!ExaminationDates.TryParse("2025-02-29",out date) && !ExaminationDates.TryParse("1404/12/30",out date),"invalid leap days rejected in both calendars");
            Check(!ExaminationDates.TryParse("1405/13/01",out date) && !ExaminationDates.TryParse("bad",out date),"invalid date formats and months rejected");
            // Analytic acceleration: Q(t)=10t, V(t)=5t^2 at 0,.1,.2 s.
            // Peak tangent at .2 has slope2, intercept .1; V(.1)=.05L (not .2L).
            Expiration signal=new Expiration { Time=new double[] {0,.1,.2},Volume=new double[] {0,.05,.2},Flow=new double[] {0,1,2} };
            double t0,bev;
            Check(SpiroAnalysis.TryBackExtrapolation(signal,.2,2,out t0,out bev),"analytic peak tangent inputs accepted");
            Near(t0,.1,"analytic Time Zero = 0.1 seconds"); Near(bev,.05,"analytic BEV = V(Time Zero) = 50mL");
            // Independent irregular piecewise-linear volume samples. Tangent gives .15,
            // V(.15)=.03+(.05/.12)*.12=.08 L.
            signal=new Expiration { Time=new double[] {0,.1,.22,.3},Volume=new double[] {0,.03,.15,.3} };
            Check(SpiroAnalysis.TryBackExtrapolation(signal,.3,2,out t0,out bev),"irregular sampled curve accepted");
            Near(t0,.15,"irregular Time Zero"); Near(bev,.08,"irregular BEV independently interpolated");
            Check(!SpiroAnalysis.TryBackExtrapolation(signal,.3,.5,out t0,out bev),"unbracketed Time Zero unavailable rather than silently zero");
            Check(!SpiroAnalysis.TryBackExtrapolation(signal,.3,0,out t0,out bev),"zero tangent slope rejected");
            signal.Time[2]=.1;
            Check(!SpiroAnalysis.TryBackExtrapolation(signal,.3,2,out t0,out bev),"duplicate sample times rejected for BEV");
            signal.Time[2]=double.NaN;
            Check(!SpiroAnalysis.TryBackExtrapolation(signal,.3,2,out t0,out bev),"nonfinite sample times rejected for BEV");
            Trial clock=new Trial { StepType=0x40,Step=.02 }; PacketDecoder decoder=new PacketDecoder();
            for(int i=0;i<100;i++) decoder.Add(clock,new DevicePacket { Seconds=i*.001,Type=0x10,Raw=200 });
            Near(clock.Time[99],2,"device-clock decoding independent of faster host polling"); Near(clock.Volume[99],4,"device-clock integration retains declared step");
            Type setup=typeof(MirUsb).GetNestedType("WDM_SETUP_PACKET",BindingFlags.NonPublic);
            Check(Marshal.SizeOf(setup)==12 && Marshal.OffsetOf(setup,"ioBuffer").ToInt32()==8,"vendor IOCTL remains 12 bytes with 32-bit ioBuffer field");
            Check(DeviceDiagnostics.CommunicationState(true,false,false,0,0,"none")=="handle_opened_no_control_response","opened handle distinguished from control response");
            Check(DeviceDiagnostics.CommunicationState(true,true,true,0,0,"none")=="start_command_succeeded_no_live_packets","command distinguished from live packets");
            Check(DeviceDiagnostics.CommunicationState(true,true,true,2,0,"none")=="packets_received_no_decoded_samples","packets distinguished from actual decoded samples");
            Check(DeviceDiagnostics.CommunicationState(true,true,true,3,2,"none")=="samples_received_unverified","samples do not imply clinical validation");
            var fixture=Obj(new JavaScriptSerializer().DeserializeObject(File.ReadAllText("Tests/Fixtures/mir-reference-b.json")));
            Patient p=new Patient { Age=44,HeightCm=164,WeightKg=74,Sex=Sex.Male,Origin=Origin.Unspecified };
            Reference reference=Ers93.Compute(p); double[] predictions={reference.Fvc,reference.Fev1,reference.Fev1Fvc,reference.Pef,reference.Fef2575};
            object[] expectedPred=(object[])fixture["predicted"];
            for(int i=0;i<5;i++) Check(Fmt.N(predictions[i],i==2?1:2)==Fmt.N(N(expectedPred[i]),i==2?1:2),"printed B predicted rounding "+i);
            Near(reference.Fvc,3.9624,"B FVC full precision retained"); Near(reference.Fev1,3.286,"B FEV1 full precision retained");
            var cases=new List<Trial>();
            foreach(object value in (object[])fixture["maneuvers"])
            {
                var row=Obj(value); cases.Add(new Trial { Simulated=true,Result=new AnalysisResult { Valid=true,Ref=reference,Fvc=N(row["FVC"]),Fev1=N(row["FEV1"]),Fev1Fvc=N(row["ratio"]),Pef=N(row["PEF"]),Fef2575=N(row["FEF25_75"]) } });
            }
            ExamSummary summary=ExamSummary.Build(cases);
            double[] selected={summary.Fvc,summary.Fev1,summary.Ratio,summary.Pef,summary.Best.Fef2575}; object[] best=(object[])fixture["best"],pct=(object[])fixture["percent"];
            for(int i=0;i<5;i++) { Check(Fmt.N(selected[i],i==2?1:2)==Fmt.N(N(best[i]),i==2?1:2),"B selection from printed outputs ONLY "+i); Check(Fmt.N(selected[i]/predictions[i]*100,0)==Fmt.N(N(pct[i]),0),"B percent rounding "+i); }
            Near(summary.Best.Fef2575,5.16,"FEF25-75 from highest sum maneuver, not independent maximum 5.17");
            Check(Fmt.N(summary.Ratio,2)=="88.63" && Fmt.N(summary.Ratio,1)=="88.6","ratio one and two decimal display using unrounded quotient");
            cases[2].Result.Pef=10; Near(ExamSummary.Build(cases).Pef,10,"PEF independent of highest sum maneuver");
            Reference a=Ers93.Compute(new Patient { Age=41,HeightCm=182,Sex=Sex.Male,Origin=Origin.Caucasian });
            double[] ap={a.Fvc,a.Fev1,a.Fev1Fvc,a.Pef,a.Fef25,a.Fef50,a.Fef75,a.Fef2575},ae={5.08,4.15,79.8,9.56,8.28,5.28,2.34,4.47};
            for(int i=0;i<8;i++) Check(Fmt.N(ap[i],i==2?1:2)==Fmt.N(ae[i],i==2?1:2),"printed A predicted rounding "+i);
            exam.Patient=p; exam.ExaminationDate=new DateTime(2026,10,19); exam.ExaminationDateSource="operator_selected_date";
            Trial trial=Simulator.Build(p,123); trial.When=new DateTime(2026,11,1,10,30,0,DateTimeKind.Local); trial.Result=SpiroAnalysis.Analyze(trial,p); exam.Trials.Add(trial);
            exam.ReviewedVextMl=132; exam.ReviewedVextSource="reviewed external report, development test only"; exam.VextManuallyReviewed=true;
            exam.SelectPrimary(trial.ManeuverId,"manual",exam.OperatorName,"precision test");
            exam.FinalizeExamination(); ExaminationSnapshot frozen=ExaminationSnapshot.Capture(exam);
            string saved=Path.Combine(root,"finalized.spiro"); ExamStore.Save(saved,exam);
            Exam loaded=ExamStore.Load(saved); ExaminationSnapshot reopened=ExaminationSnapshot.Capture(loaded);
            Check(loaded.ExaminationDate==new DateTime(2026,10,19) && loaded.Trials[0].When==trial.When,"selected historical date and actual capture timestamp round trip separately");
            Check(reopened.ToJson()==frozen.ToJson(),"finalized results and snapshot identity stable after XML restart");
            Near(loaded.Trials[0].Result.Bev,trial.Result.Bev,"finalized BEV persisted without recalculation");
            var doc=Obj(new JavaScriptSerializer().DeserializeObject(reopened.ToJson()));
            Check(Obj(doc["examination"])["examinationDate"].ToString()=="2026-10-19" && Obj(doc["examination"])["captureStartedAt"].ToString().Contains("2026-11-01T10:30"),"JSON authoritative date separate from acquisition");
            var manual=Obj(Obj(doc["spirometry"])["VEXT"]); Near(N(manual["value"]),132,"manual VEXT retained in mL");
            Check(manual["verificationStatus"].ToString()=="manually_reviewed_not_device_verified" && manual["valueSource"].ToString().Contains(exam.ReviewedVextSource),"manual VEXT review provenance explicit");
            exam.VextManuallyReviewed=false; bool refused=false; try { ExaminationSnapshot.Capture(exam); } catch(ArgumentException) { refused=true; }
            Check(refused,"unreviewed VEXT cannot export"); exam.VextManuallyReviewed=true;
            exam.OperatorName=""; refused=false; try { ExaminationSnapshot.Capture(exam); } catch(ArgumentException) { refused=true; }
            Check(refused,"VEXT requires actual reviewer name");
            using(MainForm form=new MainForm())
            {
                form.Show(); Application.DoEvents();
                typeof(MainForm).GetField("_exam",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,loaded);
                Call(form,"PopulateMetadata"); Call(form,"UpdateControls");
                Check(!((TextBox)Field(form,"txtExamDate")).Enabled && !((Button)Field(form,"btnStart")).Enabled,"finalized GUI locks metadata and further captures");
                ((TextBox)Field(form,"txtCenter")).Text="changed control"; Call(form,"SyncMetadata");
                Check(loaded.CenterName=="مرکز تست","finalized saved center immune to UI resynchronization");
                using(Bitmap bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size)); bitmap.Save(Path.Combine(root,"gui-5.1.png")); }
                typeof(MainForm).GetField("_dirty",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,false);
            }
            using(SettingsForm form=new SettingsForm(defaults)) { form.Show(); Application.DoEvents(); using(Bitmap image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(root,"settings-5.1.png")); } }
            ResultPackage output=ResultExport.SaveToDirectory(root,reopened);
            Check(File.ReadAllText(output.DataPath)==reopened.ToJson(),"JPG/JSON pair exported from same finalized snapshot");
            Console.WriteLine("ALL "+count+" PRECISION / PORTABILITY SOFTWARE CHECKS PASSED"); return 0;
        }
        catch(Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

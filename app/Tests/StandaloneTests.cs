using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;
internal static class StandaloneTests
{
    private static int n;
    private static void Check(bool value,string name) { if(!value) throw new Exception(name); n++; Console.WriteLine("PASS "+name); }
    private static Dictionary<string,object> Obj(object x) { return (Dictionary<string,object>)x; }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            Exam e=new Exam { CenterName="مرکز تست",OperatorName="کارشناس تست",Patient=new Patient { FirstName="نمونه",LastName="آزمایشی",NationalId="0000000001",Age=35,HeightCm=178,WeightKg=70 } };
            for(int i=0;i<5;i++) { Trial t=Simulator.Build(e.Patient,80+i); t.When=new DateTime(2026,11,1,9,i,0,DateTimeKind.Local); t.Result=SpiroAnalysis.Analyze(t,e.Patient); e.Trials.Add(t); }
            ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(e); string original=snapshot.ToJson();
            var d=Obj(new JavaScriptSerializer().DeserializeObject(original));
            Check((int)d["schemaVersion"]==2,"schema v2");
            Check(Obj(d["patient"])["nationalId"].ToString()=="0000000001","patient identifier preserves leading zeros");
            Check(Obj(d["examination"])["operatorName"].ToString()==e.OperatorName,"operator from manual input");
            Check(Obj(d["examination"])["testedAt"].ToString()==e.Trials[0].When.ToString("o"),"examination timestamp from first acquisition");
            Check(Obj(d["provenance"])["status"].ToString()=="NON-CLINICAL TEST DATA","test-data classification mandatory");
            Check(Obj(d["spirometry"]).Keys.OrderBy(x=>x).SequenceEqual(new string[] {"FEF25_75","FEV1","FEV1_FVC","FVC","PEF","VEXT"}.OrderBy(x=>x)),"occupational core fields exactly mapped");
            foreach(string key in new string[] {"FVC","FEV1","FEV1_FVC","PEF","FEF25_75"})
            {
                var metric=Obj(Obj(d["spirometry"])[key]);
                Check(metric["value"]!=null && metric.ContainsKey("unit") && metric.ContainsKey("valueSource") && metric["verificationStatus"].ToString().Contains("unverified"),"field value, unit and provenance: "+key);
            }
            Check(Obj(Obj(d["spirometry"])["FEV1_FVC"])["unit"].ToString()=="%","ratio in percentage");
            var vext=Obj(Obj(d["spirometry"])["VEXT"]);
            Check(vext["value"]==null && vext["unit"].ToString()=="mL" && vext["missingDataStatus"].ToString()=="unavailable","report aggregate VEXT stays unavailable rather than guessed from BEV");
            var maneuvers=(object[])d["maneuvers"];
            Check(maneuvers.Length==5 && Obj(maneuvers[3])["id"].ToString()==e.Trials[3].ManeuverId && (int)Obj(maneuvers[3])["index"]==4,"maneuver identity and chronology retained");
            Check(Obj(Obj(maneuvers[0])["estimatedBEV"])["verificationStatus"].ToString()=="not_equivalent_to_validated_VEXT","estimated BEV separated");
            var displayed=(object[])Obj(d["report"])["displayedManeuvers"];
            int last=0; foreach(object item in displayed) { int index=(int)Obj(item)["index"]; Check(index>last && Obj(item)["id"].ToString()==e.Trials[index-1].ManeuverId,"reported maneuver uses original ID"); last=index; }
            e.Patient.FirstName="changed"; e.Trials[0].Flow[0]=99; e.Trials[0].Result.Fvc=99;
            Check(snapshot.ToJson()==original,"snapshot immutable against source edits");
            ResultPackage export=ResultExport.SaveToDirectory(root,snapshot);
            Check(Directory.GetFiles(Path.GetDirectoryName(export.ReportPath)).Length==2,"exactly JPG and JSON primary outputs");
            byte[] jpg=File.ReadAllBytes(export.ReportPath);
            Check(jpg[0]==0xff && jpg[1]==0xd8 && jpg[jpg.Length-2]==0xff && jpg[jpg.Length-1]==0xd9,"real JPEG encoder signature");
            using(Image image=Image.FromFile(export.ReportPath)) Check(image.RawFormat.Guid==ImageFormat.Jpeg.Guid && image.Width==1800 && image.Height==2546 && image.Width==export.ReportWidth && image.Height==export.ReportHeight && image.VerticalResolution>=299,"portrait 1800x2546 300 DPI JPG");
            Check(ResultExport.JpegQuality==70 && export.JpegQuality==70,"owner JPG policy is exactly quality 70");
            Check(jpg.Length==export.ReportBytes.Length && File.ReadAllBytes(export.ReportPath).SequenceEqual(export.ReportBytes),"JPG on disk is byte-identical to ReportBytes");
            Check(jpg.Length<=ResultExport.JpegCeilingBytes && jpg.Length>=150*1024,"JPEG stays inside the guaranteed readability ceiling");
            // A quality change must not erase the table text or the three curves:
            // sample the decoded sheet and require real dark ink without a black fill.
            using(Bitmap decoded=new Bitmap(export.ReportPath))
            {
                int dark=0,sampled=0;
                for(int y=0;y<decoded.Height;y+=8) for(int x=0;x<decoded.Width;x+=8)
                {
                    Color c=decoded.GetPixel(x,y);sampled++;
                    if(c.R<96 && c.G<96 && c.B<96) dark++;
                }
                Check(sampled>10000 && dark>sampled/100 && dark<sampled/2,"quality-70 sheet still carries text and curve ink");
            }
            File.Copy(export.ReportPath,Path.Combine(root,"jpeg-optimized.jpg"),true);
            string baselinePath=Path.Combine(root,"jpeg-baseline-97.jpg");
            using(Bitmap baselineImage=SpiroReport.Render(snapshot)) ResultExport.EncodeJpeg(baselineImage,baselinePath,97);
            File.WriteAllText(Path.Combine(root,"jpeg-optimization.txt"),"baseline=2400x3394 q97 "+new FileInfo(baselinePath).Length+" bytes\r\noptimized="+export.ReportWidth+"x"+export.ReportHeight+" q"+export.JpegQuality+" "+jpg.Length+" bytes\r\ntarget="+ResultExport.JpegTargetBytes+" bytes met="+(jpg.Length<=ResultExport.JpegTargetBytes)+"\r\nceiling="+ResultExport.JpegCeilingBytes+" bytes met="+(jpg.Length<=ResultExport.JpegCeilingBytes)+"\r\n");
            Check(File.ReadAllText(export.DataPath)==original,"export JSON is same frozen snapshot");
            bool refused=false; try { ResultExport.SaveToDirectory(root,snapshot); } catch(IOException) { refused=true; }
            Check(refused && File.ReadAllBytes(export.ReportPath).SequenceEqual(jpg),"existing export never replaced");
            var hook=typeof(ResultExport).GetField("ExportCheckpoint",BindingFlags.Static|BindingFlags.NonPublic);
            hook.SetValue(null,(Action<string>)delegate { throw new IOException("injected disk failure"); });
            Exam fresh=new Exam { Patient=new Patient() }; Trial flow=Simulator.Build(fresh.Patient,5); flow.Result=SpiroAnalysis.Analyze(flow,fresh.Patient); fresh.Trials.Add(flow);
            bool failed=false; try { ResultExport.SaveToDirectory(root,ExaminationSnapshot.Capture(fresh)); } catch(IOException) { failed=true; } finally { hook.SetValue(null,null); }
            Check(failed && Directory.GetDirectories(root,".spiro-tmp-*").Length==0,"failed export rolls back unpublished files");
            Check(File.ReadAllBytes(export.ReportPath).SequenceEqual(jpg),"failed export leaves old report untouched");
            fresh.Trials[0].Completed=false; bool incomplete=false; try { ExaminationSnapshot.Capture(fresh); } catch(InvalidOperationException) { incomplete=true; }
            Check(incomplete,"incomplete examination cannot produce final-looking report");
            Check(DeviceDiagnostics.Classify(false,"",0)=="missing","diagnostics absent PnP");
            Check(DeviceDiagnostics.Classify(true,"",28)=="incompatible","diagnostics unbound driver");
            Check(DeviceDiagnostics.Classify(true,"WinUSB",0)=="incompatible","WinUSB not treated as MIRUSB");
            Check(DeviceDiagnostics.Classify(true,"MIRUSB",10)=="not_ready","driver error distinct from readiness");
            var state=new DeviceDiagnostics { Detected=true,DriverState="ready",Communication="control_response_only" };
            Check(state.PersianSummary.Contains("هنوز") && state.SafeText().Contains("HARDWARE VALIDATION PENDING"),"control handshake not proof of live spirometry");
            Check(!state.SafeText().Contains("0000000001") && !state.SafeText().Contains(Environment.UserName),"diagnostics log excludes patient and user");
            foreach(AssemblyName dependency in typeof(ExaminationSnapshot).Assembly.GetReferencedAssemblies()) Check(!dependency.Name.ToLowerInvariant().Contains("mir"),"no MIR assembly dependency: "+dependency.Name);
            foreach(Type t in typeof(MirUsb).Assembly.GetTypes()) foreach(MethodInfo m in t.GetMethods(BindingFlags.NonPublic|BindingFlags.Static))
            {
                var import=(System.Runtime.InteropServices.DllImportAttribute)Attribute.GetCustomAttribute(m,typeof(System.Runtime.InteropServices.DllImportAttribute));
                if(import!=null) Check(new string[] {"kernel32.dll","setupapi.dll","winsqlite3.dll"}.Contains(import.Value.ToLowerInvariant()),"only in-box Windows native DLL: "+m.Name);
            }
            Check(DeviceDiagnostics.DriverBinaryPath.Contains(Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess?"Sysnative":"System32"),"native driver path accounts for WOW64 redirection");
            string sources=Path.GetFullPath("Src");
            string main=File.ReadAllText(Path.Combine(sources,"Ui","MainForm.cs"));
            string liveStart=main.Substring(main.IndexOf("private void StartTest()"),main.IndexOf("private void StartSimulation()")-main.IndexOf("private void StartTest()"));
            Check(!liveStart.Contains("StartSimulation("),"no silent simulation fallback on USB failure");
            string allSource=string.Join("\n",Directory.GetFiles(sources,"*.cs",SearchOption.AllDirectories).Select(File.ReadAllText).ToArray());
            Check(!allSource.Contains("ServerCertificateValidationCallback") && !allSource.Contains("class HLineHttpWriter"),"TLS validation preserved and no live clinical HTTP writer enabled");
            string usb=File.ReadAllText(Path.Combine(sources,"MirUsb.cs"));
            Check(usb.Contains("CancelIoEx") && usb.Contains("Quarantine") && !usb.Contains("out count, true)"),"bounded native waits and deferred cancellation cleanup present (hardware path pending)");
            string legacy=Path.Combine(root,"legacy.spiro"); Exam old=new Exam { Version=1,Patient=new Patient() }; Trial bt=Simulator.Build(old.Patient,7); old.Trials.Add(bt); ExamStore.Save(legacy,old);
            Exam migrated=ExamStore.Load(legacy); Check(migrated.Version==2 && migrated.LegacyRecord,"v1 local records explicitly migrated");
            Check(migrated.Trials[0].ManeuverId==bt.ManeuverId,"restart preserves maneuver identity");
            Exam reviewed=new Exam { Patient=new Patient(),ReviewedInterpretation="نظر کارشناس" }; Trial reviewedTrial=Simulator.Build(reviewed.Patient,9); reviewedTrial.Result=SpiroAnalysis.Analyze(reviewedTrial,reviewed.Patient); reviewed.Trials.Add(reviewedTrial);
            var reviewedJson=Obj(new JavaScriptSerializer().DeserializeObject(ExaminationSnapshot.Capture(reviewed).ToJson()));
            Check(Obj(reviewedJson["interpretation"])["valueSource"].ToString()=="clinician_entered","manual interpretation distinguished");
            File.WriteAllText(Path.Combine(root,"test-results.txt"),"PASS "+n+" software checks. Clean Windows A: PENDING. Hardware B: HARDWARE VALIDATION PENDING.");
            Console.WriteLine("ALL "+n+" STANDALONE SOFTWARE CHECKS PASSED"); return 0;
        }
        catch(Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

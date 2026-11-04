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
    static Exam ArchiveExam()
    {return new Exam {CreatedAt=new DateTime(2026,11,4,9,15,30),ExaminationDate=new DateTime(2026,11,4),SelectionWorkflowVersion=4,Patient=new Patient {FirstName="نمونهٔ",LastName="آزمایشی‌مرکز",NationalId="0000000001",Age=35,HeightCm=178,WeightKg=70}};}
    static void ArchiveTests(string root)
    {
        string records=Path.Combine(root,"archive"),legacy=Path.Combine(root,"legacy-copy");Directory.CreateDirectory(legacy);
        Exam e=ArchiveExam();string first=ExamStore.NewPath(e,records);ExamStore.Save(first,e);
        Check(Directory.Exists(records) && Path.GetFileName(Path.GetDirectoryName(first))=="1405-08-13","records creates the Persian examination-day folder");
        Check(Path.GetFileName(first)==e.Patient.FullName+" - 09-15-30.spiro","Persian name, spaces and ZWNJ are preserved with stable start time");
        DateTime? started=e.CreatedAt;e.OperatorName="changed";e.ExaminationDate=e.ExaminationDate.Value.AddDays(1);ExamStore.Save(first,e);
        Check(File.Exists(first) && ExamStore.Load(first).CreatedAt==started && ExamStore.Load(first).ExaminationDate==e.ExaminationDate,"autosave and date correction preserve the original path and creation time");
        Exam other=ArchiveExam();string second=ExamStore.NewPath(other,records);ExamStore.Save(second,other);
        Check(first!=second && Path.GetFileName(second).EndsWith("-2.spiro") && ExamStore.Load(first).ExaminationId==e.ExaminationId,"same-name same-second independent examination gets readable collision suffix");
        bool refused=false;try {ExamStore.Save(first,other);}catch(IOException){refused=true;}
        Check(refused && ExamStore.Load(first).ExaminationId==e.ExaminationId,"a different examination cannot overwrite an existing record");
        other.ExaminationDate=other.ExaminationDate.Value.AddDays(1);
        Check(Path.GetFileName(Path.GetDirectoryName(ExamStore.NewPath(other,records)))=="1405-08-14","another examination day receives a separate Persian folder");
        Check(ExamStore.SafePatientName("../CON:*?\"<>").IndexOfAny(Path.GetInvalidFileNameChars())<0 && ExamStore.SafePatientName("CON")=="_CON" && ExamStore.SafePatientName("... ").Length>0,"invalid Windows names, traversal and reserved device names are sanitized");
        var calendar=new System.Globalization.PersianCalendar();DateTime last=calendar.ToDateTime(1404,12,calendar.GetDaysInMonth(1404,12),0,0,0,0);
        Check(ExamStore.ArchiveDay(last).StartsWith("1404-12-") && ExamStore.ArchiveDay(last.AddDays(1))=="1405-01-01","Persian year rollover is calendar-based");
        string blocked=Path.Combine(root,"blocked");File.WriteAllText(blocked,"fixture");
        refused=false;try {ExamStore.NewPath(e,blocked);}catch(IOException ex){refused=ex.Message==ExamStore.RecordsAccessMessage;}
        Check(refused && e.CreatedAt==started,"non-writable archive path gives a clear error and preserves memory");
        string denied=Path.Combine(root,"acl-denied");Directory.CreateDirectory(denied);
        var security=Directory.GetAccessControl(denied);var restricted=Directory.GetAccessControl(denied);
        restricted.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User,System.Security.AccessControl.FileSystemRights.Write,System.Security.AccessControl.InheritanceFlags.ContainerInherit|System.Security.AccessControl.InheritanceFlags.ObjectInherit,System.Security.AccessControl.PropagationFlags.None,System.Security.AccessControl.AccessControlType.Deny));
        try
        {
            Directory.SetAccessControl(denied,restricted);refused=false;
            try {ExamStore.NewPath(e,denied);}catch(IOException ex){refused=ex.Message==ExamStore.RecordsAccessMessage;}
            Check(refused,"Windows ACL write denial is reported without an AppData fallback");
        }
        finally {Directory.SetAccessControl(denied,security);}
        e.OperatorName="backup-A";ExamStore.Save(first,e);e.OperatorName="backup-B";ExamStore.Save(first,e);
        Check(ExamStore.Load(first+".bak").OperatorName=="backup-A","atomic replacement retains a readable previous backup");
        string interrupted=Path.Combine(root,"interrupted.spiro");ExamStore.Save(interrupted,e);byte[] bytes=File.ReadAllBytes(interrupted);Directory.CreateDirectory(interrupted+".bak");
        refused=false;try {e.OperatorName="not committed";ExamStore.Save(interrupted,e);}catch(IOException){refused=true;}
        Check(refused && File.ReadAllBytes(interrupted).SequenceEqual(bytes) && e.OperatorName=="not committed","failed atomic replacement preserves the complete file and unsaved memory");
        Exam old=ArchiveExam();old.CreatedAt=null;old.FinalizedAt=new DateTime(2026,11,4);old.Submission.WriteAttempted=true;old.Submission.FileId="synthetic-file";
        old.ExportReferences.Add(new ExportReference {ReportPath="C:\\synthetic-original\\report.jpg",DataPath="C:\\synthetic-original\\data.json"});
        string source=Path.Combine(legacy,"old.spiro");ExamStore.Save(source,old);byte[] original=File.ReadAllBytes(source);
        RecordImportResult imported=ExamStore.ImportLegacy(legacy,records);
        Check(imported.Imported==1 && imported.Failed==0 && File.ReadAllBytes(source).SequenceEqual(original),"legacy import is copy-first and leaves source bytes untouched");
        string copy=Directory.GetFiles(records,"*.spiro",SearchOption.AllDirectories).First(p=>ExamStore.Load(p).ExaminationId==old.ExaminationId);
        Exam reopened=ExamStore.Load(copy);
        Check(File.ReadAllBytes(copy).SequenceEqual(original) && reopened.SelectionWorkflowVersion==4 && reopened.Submission.WriteAttempted && reopened.Submission.FileId=="synthetic-file" && reopened.ExportReferences[0].ReportPath==old.ExportReferences[0].ReportPath,"legacy copy preserves workflow, submission journal, identity and export paths");
        imported=ExamStore.ImportLegacy(legacy,records);Check(imported.Skipped==1 && imported.Imported==0,"identical internal identity is skipped on repeated import");
        old.OperatorName="different saved revision";ExamStore.Save(source,old);imported=ExamStore.ImportLegacy(legacy,records);
        Check(imported.Conflicts==1 && imported.Failed==1 && File.ReadAllBytes(copy).SequenceEqual(original),"same identity with different content is reported without replacing destination");
        File.WriteAllText(Path.Combine(legacy,"broken.spiro"),"not XML");imported=ExamStore.ImportLegacy(legacy,records);
        Check(imported.Failed==2,"migration reports malformed source separately from identity conflict");
        Check(Path.GetFullPath(ExamStore.DataDirectory)==Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"records"),"records root follows executable base rather than working directory");
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root=Path.Combine(Path.GetFullPath(args[0]),"run-"+Guid.NewGuid().ToString("N").Substring(0,8)); Directory.CreateDirectory(root);
            ArchiveTests(root);
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

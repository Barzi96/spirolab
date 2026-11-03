using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.HLine;
using SpiroLab.Ui;

internal static class RefinementTests
{
    static int checks;
    static string root;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); checks++; Console.WriteLine("PASS "+name); }
    static void Refuses(Action work,string name) { bool refused=false; try { work(); } catch(ArgumentException) { refused=true; } catch(InvalidOperationException) { refused=true; } Check(refused,name); }
    static void Rejects(Action work,string name) { bool refused=false; try { work(); } catch(InvalidDataException) { refused=true; } Check(refused,name); }
    static object Field(object o,string name) { return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static object Call(object o,string name,params object[] args) { return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args); }
    static void Set(object o,string name,object value) { o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value); }
    static Dictionary<string,object> Obj(object value) { return (Dictionary<string,object>)value; }
    static Dictionary<string,object> Json(ExaminationSnapshot snapshot) { return Obj(new JavaScriptSerializer {MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters}.DeserializeObject(snapshot.ToJson())); }
    static Patient Patient() { return new Patient {FirstName=" refine",LastName="synthetic",NationalId="0000000001",Age=35,HeightCm=175,WeightKg=74,Sex=Sex.Male,Origin=Origin.Caucasian,HLinePersonId="TEST-P",HLineRecordId="2",HLineRecordDate="2026/11/01",HLineRecordStatus="complete"}; }
    static Trial Blow(Patient p,int seed,double fvc,double fev1)
    {
        Trial t=Simulator.Build(p,seed);
        if(t.Result==null) t.Result=SpiroAnalysis.Analyze(t,p);
        t.Simulated=true; t.Completed=true; t.CaptureError="";
        t.Result.Valid=true; t.Result.AvailabilityMetadata=true;
        t.Result.Fvc=fvc; t.Result.Fev1=fev1;
        t.Result.FvcAvailable=true; t.Result.Fev1Available=true; t.Result.FetAvailable=true;
        t.Result.Fet=6; t.Result.Cough=false; t.Result.PlateauOk=true;
        return t;
    }
    static void Select(Exam e,params int[] indexes)
    {
        foreach(string id in e.ReportManeuverIds.ToArray()) e.SetReportManeuver(id,false);
        foreach(int index in indexes) e.SetReportManeuver(e.Trials[index].ManeuverId,true);
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            Patient p=Patient();
            Exam e=Exam.CreateNew(new AppSettings {OperatorName="refine operator"}); e.Patient=p;
            Trial t0=Blow(p,100,3.0,2.0),t1=Blow(p,101,4.0,2.5),t2=Blow(p,102,3.5,3.0),t3=Blow(p,103,4.5,3.2),t4=Blow(p,104,2.8,2.2);
            foreach(Trial t in new Trial[] {t0,t1,t2,t3,t4}) e.AddManeuver(t);
            Check(e.SelectionWorkflowVersion==4 && e.ReportManeuverIds.Count==3,"new examination uses workflow4 and at most three automatic selections");
            Select(e,0,1,2);
            Check(e.ReportSelectionManual && e.ReportManeuverIds.Count==3,"manual final selection is persisted in the examination");
            ExamSummary selected=ExamSummary.BuildSelected(e.Trials,e.ReportManeuverIds);
            Check(Math.Abs(selected.Fvc-4.0)<1e-9 && Math.Abs(selected.Fev1-3.0)<1e-9,"selected aggregate takes largest FVC and independent largest FEV1");
            Check(Math.Abs(selected.Ratio-75.0)<1e-6 && selected.BestValues.Fev1Fvc.Method.Contains("best_fev1_div_best_fvc"),"selected aggregate derives ratio from independent maxima");
            Check(selected.BestValues.Fvc.SourceManeuverId==t1.ManeuverId && selected.BestValues.Fev1.SourceManeuverId==t2.ManeuverId,"selected aggregate records exact source IDs");
            Check(selected.BestValues.Fef2575.SourceManeuverId==t1.ManeuverId,"FEF25-75 uses the selected maneuver with the largest FVC+FEV1, not the largest FEF");
            Check(selected.BestValues.Pef.Available && selected.BestValues.Pef.SourceManeuverId!=null,"selected aggregate selects an eligible PEF independently");
            ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(e);
            Check(snapshot.SelectionWarning.Contains("FVC") && snapshot.SelectionWarning.Contains("FEV1"),"unselected higher values produce a concise selection warning");
            var doc=Json(snapshot); var best=Obj(doc["bestValues"]);
            Check(best["scope"].ToString()=="operator_selected_max_three_only_not_all_eligible_maneuvers","snapshot and JSON declare selected-three scope");
            object[] selectionIds=(object[])Obj(doc["reportSelection"])["maneuverIds"];
            Check(selectionIds.Length==3 && selectionIds.All(x=>e.ReportManeuverIds.Contains(Convert.ToString(x))),"JSON retains the exact selected maneuver identifiers");
            Check(Math.Abs(Convert.ToDouble(Obj(best["fvc"])["value"])-selected.Fvc)<1e-9 && Math.Abs(Convert.ToDouble(Obj(best["fev1"])["value"])-selected.Fev1)<1e-9,"JSON, JPG snapshot and UI summary share the selected aggregate");
            ResultPackage package=ResultExport.SaveToDirectory(Path.Combine(root,"selected-export"),snapshot);
            SubmissionDraft draft=new SubmissionDraft(snapshot,package);
            Check(draft.Snapshot==snapshot && Math.Abs(draft.Fields["fvc_amount"].Value-selected.Fvc)<1e-9 && Math.Abs(draft.Fields["fev1_amount"].Value-selected.Fev1)<1e-9,"HLine draft uses the same exact selected snapshot and numeric values");
            Select(e,2,3,4);
            ExamSummary changed=ExamSummary.BuildSelected(e.Trials,e.ReportManeuverIds);
            Check(Math.Abs(changed.Fvc-4.5)<1e-9 && Math.Abs(changed.Fev1-3.2)<1e-9,"changing selected three recomputes every aggregate value");
            Check(ExaminationSnapshot.Capture(e).SelectionWarning.Length==0,"warning clears when the selected set contains the highest selected values");
            Trial added=Blow(p,105,5.0,3.4); e.AddManeuver(added);
            Check(e.ReportSelectionManual && e.ReportManeuverIds.SequenceEqual(new string[] {e.Trials[2].ManeuverId,e.Trials[3].ManeuverId,e.Trials[4].ManeuverId}),"manual final selection remains the same real set after new acquisition");
            Check(!e.ReportManeuverIds.Contains(added.ManeuverId),"new acquisition does not silently replace a manual final selection");
            Check(ExamSummary.BuildSelected(e.Trials,e.ReportManeuverIds).Fvc<5.0,"new unselected acquisition cannot change selected aggregate");
            e.ResetReportSelectionAutomatic();
            Check(!e.ReportSelectionManual && e.ReportManeuverIds.SequenceEqual(ExamSummary.Build(e.Trials).Trials.Select(x=>x.ManeuverId)),"automatic reset restores the quality-ranked top three");
            Select(e,0,1,2);
            string removed=t1.ManeuverId;
            e.DiscardManeuver(removed);
            Check(e.Trials.Count==5 && e.DiscardedTrials.Count==1 && !e.ReportManeuverIds.Contains(removed),"delete removes the maneuver from active collection and final selection");
            Check(e.Trials.All(x=>x.ManeuverId!=removed) && ExamSummary.BuildSelected(e.Trials,e.ReportManeuverIds).Fvc!=4.0,"deleted maneuver cannot contribute to aggregate");
            Check(ClinicVext.SelectForExam(e).Available==false,"deleted maneuver cannot provide clinic VEXT");
            string archived=Path.Combine(root,"refine-archive.spiro"); ExamStore.Save(archived,e);
            Exam reopened=ExamStore.Load(archived);
            Check(reopened.DiscardedTrials.Count==1 && reopened.DiscardedTrials[0].ManeuverId==removed && reopened.DiscardedTrials[0].DiscardedAt.HasValue,"discarded evidence survives restart outside the active collection");
            Check(!reopened.Trials.Any(x=>x.ManeuverId==removed) && reopened.ReportManeuverIds.Count==2,"restart keeps deleted maneuver outside active selection");
            reopened.RestoreLastDiscarded();
            Check(reopened.Trials.Any(x=>x.ManeuverId==removed) && reopened.ReportManeuverIds.Contains(removed) && reopened.DiscardedTrials.Count==0,"undo restores the stable maneuver ID and its prior selection");
            Trial real=Blow(p,99,4.0,2.5); real.Simulated=false; real.Imported=false; real.DeviceCaptureSequence=9; real.DeviceSerial="J22889"; real.RequiresDeviceCore=true; real.DeviceValues=null;
            Check(!real.Measurements.Fvc.Available && real.Measurements.Fvc.Source==MeasurementSource.Unavailable,"new real USB maneuver without a verified device result has no software clinical FVC");
            Check(real.Measurements.Reconstructed.Fvc.Available && real.Measurements.Reconstructed.Fvc.Source==MeasurementSource.SoftwareCalculated,"reconstructed values remain separate diagnostics");
            Check(!ExamSummary.CanReport(real) && ExamSummary.Build(new Trial[] {real}).Candidates.Count==0,"unverified real maneuver is not eligible for selected aggregate or report");
            Exam flaggedExam=Exam.CreateNew(new AppSettings {OperatorName="refine operator"}); flaggedExam.Patient=p;
            Trial flaggedReal=Blow(p,98,4.1,2.6); flaggedReal.Simulated=false; flaggedReal.DeviceCaptureSequence=11; flaggedReal.DeviceSerial="J22889"; flaggedReal.DeviceValues=null;
            flaggedExam.AddManeuver(flaggedReal);
            Check(flaggedReal.RequiresDeviceCore && !flaggedReal.Measurements.Fvc.Available,"workflow4 AddManeuver marks a real capture device-core-required and withholds software FVC");
            string flaggedPath=Path.Combine(root,"refine-flagged.spiro"); ExamStore.Save(flaggedPath,flaggedExam);
            Check(ExamStore.Load(flaggedPath).Trials[0].RequiresDeviceCore,"device-core requirement survives reload for a new workflow4 record");
            e.RecordFailedCapture(real);
            Check(e.Trials.All(x=>x.ManeuverId!=real.ManeuverId) && e.FailedCaptures.Count==1,"failed device capture is kept separately and never enters the active table");
            string failedPath=Path.Combine(root,"refine-failed.spiro"); ExamStore.Save(failedPath,e);
            Check(ExamStore.Load(failedPath).FailedCaptures.Count==1,"failed capture diagnostics survive restart");
            Trial noCurve=new Trial {ManeuverId=Guid.NewGuid().ToString("N"),Completed=false,Simulated=false,DeviceCaptureSequence=10,Flow=null,Time=null,Volume=null,Packets=null,Result=new AnalysisResult {Valid=false,InvalidReason="no_curve"}};
            e.RecordFailedCapture(noCurve); string noCurvePath=Path.Combine(root,"refine-failed-no-curve.spiro"); ExamStore.Save(noCurvePath,e);
            Check(ExamStore.Load(noCurvePath).FailedCaptures.Count==2,"a failed attempt without a curve remains separate local evidence and does not make the examination unreadable");
            Check(ExaminationSnapshot.Capture(ExamStore.Load(failedPath)).ToJson().IndexOf(real.ManeuverId,StringComparison.Ordinal)<0,"failed capture does not appear in the clinical snapshot");
            Exam vexam=Exam.CreateNew(new AppSettings {OperatorName="refine operator"}); vexam.Patient=p;
            Trial v1=Blow(p,300,3.0,2.0),v2=Blow(p,301,3.2,2.1),v3=Blow(p,302,2.9,1.9);
            foreach(Trial t in new Trial[] {v1,v2,v3}) { t.Simulated=false; t.Imported=false; t.Result.BevAvailable=true; t.Result.Bev=(t==v1?0.110:(t==v2?0.132:0.105)); vexam.AddManeuver(t); }
            VextSelection beforeDelete=ClinicVext.SelectForExam(vexam);
            Check(beforeDelete.Available && beforeDelete.ValueMl==132,"selected real maneuvers provide clinic VEXT 132");
            vexam.DiscardManeuver(v2.ManeuverId);
            Check(ClinicVext.SelectForExam(vexam).ValueMl==110,"deleted VEXT source is excluded without refusing the remaining real maneuvers");
            vexam.RestoreLastDiscarded();
            Check(ClinicVext.SelectForExam(vexam).ValueMl==132,"undo restores the deleted VEXT source");

            Exam seqExam=Exam.CreateNew(new AppSettings {OperatorName="sequence operator"}); seqExam.Patient=p;
            Trial s1=Blow(p,400,3.0,2.0),s2=Blow(p,401,3.4,2.3),s3=Blow(p,402,3.8,2.6);
            foreach(Trial item in new Trial[] {s1,s2,s3}) seqExam.AddManeuver(item);
            seqExam.DiscardManeuver(s3.ManeuverId);
            Trial s4=Blow(p,403,4.0,2.8); seqExam.AddManeuver(s4);
            Check(seqExam.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,4}) && seqExam.DiscardedTrials[0].AcquisitionSequence==3,"new acquisition never reuses a discarded maneuver number");
            seqExam.RestoreLastDiscarded();
            Check(seqExam.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,3,4}) && seqExam.Trials.Select(x=>x.ManeuverId).Distinct().Count()==4,"restored maneuver is inserted by original number without collision");
            Check(seqExam.ReportManeuverIds.Contains(s3.ManeuverId) && seqExam.ReportManeuverIds.Count==3,"restored formerly selected maneuver does not silently become unselected");
            string seqPath=Path.Combine(root,"sequence.spiro"); ExamStore.Save(seqPath,seqExam);
            Exam seqLoaded=ExamStore.Load(seqPath);
            Check(seqLoaded.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,3,4}) && seqLoaded.Trials.Select(x=>x.ManeuverId).Distinct().Count()==4 && seqLoaded.ReportManeuverIds.Count==3,"unique numbers and selected three survive save and reload");

            string middle=seqLoaded.Trials[1].ManeuverId; seqLoaded.DiscardManeuver(middle);
            Trial s5=Blow(p,404,4.2,3.0); seqLoaded.AddManeuver(s5);
            Check(seqLoaded.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,3,4,5}) && seqLoaded.DiscardedTrials.Last().AcquisitionSequence==2,"middle deletion followed by acquisition keeps a monotonic number");
            seqLoaded.RestoreLastDiscarded();
            Check(seqLoaded.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,3,4,5}) && seqLoaded.ReportManeuverIds.Contains(middle) && seqLoaded.ReportManeuverIds.Count==3,"middle restore is ordered, selected and still capped at three");
            ExamStore.Save(seqPath,seqLoaded);
            Check(ExamStore.Load(seqPath).Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,3,4,5}),"middle delete/acquire/restore stays unique after restart");

            Exam atomic=Exam.CreateNew(new AppSettings {OperatorName="sequence operator"}); atomic.Patient=p;
            Trial a1=Blow(p,410,3.0,2.0),a2=Blow(p,411,3.5,2.4),a3=Blow(p,412,4.0,2.7);
            foreach(Trial item in new Trial[] {a1,a2,a3}) atomic.AddManeuver(item);
            atomic.DiscardManeuver(a3.ManeuverId); Trial a4=Blow(p,413,4.1,2.9); atomic.AddManeuver(a4);
            atomic.DiscardedTrials[0].AcquisitionSequence=4;
            string[] activeBefore=atomic.Trials.Select(x=>x.ManeuverId).ToArray(); string[] selectedBefore=atomic.ReportManeuverIds.ToArray();
            Rejects(delegate { atomic.RestoreLastDiscarded(); },"duplicate restoration number is refused");
            Check(atomic.Trials.Select(x=>x.ManeuverId).SequenceEqual(activeBefore) && atomic.DiscardedTrials.Count==1 && atomic.ReportManeuverIds.SequenceEqual(selectedBefore),"refused restore leaves active set, archive and selection untouched");
            atomic.DiscardedTrials[0].AcquisitionSequence=3; atomic.RestoreLastDiscarded();
            Check(atomic.Trials.Select(x=>x.AcquisitionSequence).SequenceEqual(new int[] {1,2,3,4}),"corrected archive restores atomically after refusal");

            using(MainForm form=new MainForm())
            {
                form.Size=new Size(1280,800); form.Show(); Application.DoEvents(); Call(form,"BeginExam",p,true);
                Exam ui=(Exam)Field(form,"_exam");
                for(int i=0;i<5;i++) ui.AddManeuver(Blow(p,200+i,3.0+i*.2,2.0+i*.1));
                Call(form,"RenderTrials");
                DataGridView grid=(DataGridView)Field(form,"gridTrials");
                Check(grid.Columns.Count==7 && grid.Columns[0] is ManeuverColumn && grid.Columns["ReportSelection"] is ReportColumn,"compact history uses the existing first column for delete and the final-selection control");
                Check(grid.Columns["ReportSelection"].HeaderText=="انتخاب نهایی" && grid.Columns["ReportSelection"].ToolTipText.Contains("حداکثر سه"),"final-selection control has the required Persian label and tooltip");
                Check(grid.Rows[0].Cells[0].Value.ToString()==ui.Trials[0].AcquisitionSequence.ToString() && !grid.Rows[0].Cells[0].Value.ToString().Contains("·"),"first column keeps only the maneuver number and no long quality suffix");
                Check(((ManeuverColumn)grid.Columns[0]).HitDelete(grid.Columns[0].Width,grid.Columns[0].Width-5) && !((ManeuverColumn)grid.Columns[0]).HitDelete(grid.Columns[0].Width,5),"compact delete action is hit-tested inside the existing first column");
                Button stop=(Button)Field(form,"btnStop"); ToolTip tips=(ToolTip)Field(form,"_tips");
                Check(stop.Text=="لغو مانور" && tips.GetToolTip(stop).Contains("ESC") && tips.GetToolTip(stop).Contains("لغو"),"stop button states the real ESC cancellation meaning and explains the retained incomplete trace");
                ChartPanel plot=(ChartPanel)Field(form,"chart");
                int active=ui.Trials.Count; Trial bad=Blow(p,999,4.0,2.5); bad.Simulated=false; bad.DeviceCaptureSequence=1; bad.DeviceSerial="J22889"; bad.DeviceValues=null;
                Set(form,"_active",bad); Call(form,"FinishTrial");
                Check(ui.Trials.Count==active && ui.FailedCaptures.Count==1 && ((Label)Field(form,"lblStatus")).Text=="نتیجه معتبر دستگاه دریافت نشد. نمودار ناقص فقط برای مشاهده حفظ شده و این مانور قابل ارسال نیست؛ لطفاً مانور را تکرار کنید.","failed real finish shows the exact Persian retry message and does not append a row");
                Check(plot.Trial==bad && plot.Result==null && plot.VisibleSamples==-1 && bad.Flow.Count>=2,"failed real finish keeps the real incomplete curve visible without device markers");
                Trial stopped=Blow(p,998,4.1,2.6); stopped.Simulated=false; stopped.Completed=false; stopped.OperatorStopped=true; stopped.DeviceCaptureSequence=1; stopped.DeviceSerial="J22889"; stopped.DeviceValues=null;
                Set(form,"_active",stopped); Call(form,"FinishTrial");
                Check(ui.FailedCaptures.Count==2 && ((Label)Field(form,"lblStatus")).Text=="مانور متوقف شد. نمودار ناقص فقط برای مشاهده حفظ شده و نتیجه معتبر دستگاه دریافت نشده است." && plot.Trial==stopped && plot.Result==null,"manual stop keeps the curve and reports the required stop message");
                Check(!ui.Trials.Contains(stopped) && !ui.ReportManeuverIds.Contains(stopped.ManeuverId),"stopped incomplete maneuver stays outside the clinical table and final selection");
                Trial emptyCurve=new Trial {Simulated=false,Completed=false,OperatorStopped=true,DeviceCaptureSequence=1,DeviceSerial="J22889",Flow=new List<double>{0},Time=new List<double>{0},Volume=new List<double>{0}};
                Set(form,"_active",emptyCurve); Call(form,"FinishTrial");
                Check(ui.FailedCaptures.Count==3 && plot.Trial==null && ((Label)Field(form,"lblStatus")).Text=="مانور متوقف شد؛ نمونهٔ کافی برای رسم منحنی دریافت نشد و نتیجه معتبر دستگاه هم ثبت نشد.","stop without enough samples uses a separate message and an empty chart");
                Check(ui.Trials.Count==active && ui.Trials.All(x=>x.ManeuverId!=bad.ManeuverId && x.ManeuverId!=stopped.ManeuverId && x.ManeuverId!=emptyCurve.ManeuverId),"no failed or stopped attempt enters the active clinical table");
                Call(form,"SelectTrial",0); Check(plot.Trial==ui.Trials[0] && plot.Result==ui.Trials[0].Result,"selecting a recorded maneuver restores normal chart behavior after a stop");
            }
            string productPath=Path.GetFullPath(Path.Combine("release","SpiroLab.exe"));
            Assembly product=Assembly.LoadFile(productPath);
            Type mainType=product.GetType("SpiroLab.Ui.MainForm");
            Check(mainType!=null && product.GetType("SpiroLab.Device.Simulator")==null && product.GetType("SpiroLab.DeploymentSelfTest")==null && product.GetType("SpiroLab.HLine.SubmissionTiming")==null,"production executable has no simulator, self-test or timing subsystem");
            Check(mainType.GetMethod("RunDemoForTest",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)==null && mainType.GetMethod("StartSimulation",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)==null,"production executable has no simulated acquisition entry point");
            Check(product.GetType("SpiroLab.Ui.SettingsForm").GetProperty("RunDevelopmentDemo")==null,"production settings has no development switch");
            Check(FileVersionInfo.GetVersionInfo(productPath).FileVersion.StartsWith("5.4.5",StringComparison.Ordinal),"production executable carries the release version 5.4.5");
            Console.WriteLine("ALL "+checks+" REFINEMENT CHECKS PASSED"); return 0;
        }
        catch(Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

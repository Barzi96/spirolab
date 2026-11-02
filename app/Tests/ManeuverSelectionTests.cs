using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;
internal static class ManeuverSelectionTests
{
    static int count;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); count++; Console.WriteLine("PASS "+name); }
    static void Refuses(Action action,string name) { bool refused=false; try { action(); } catch(ArgumentException) { refused=true; } catch(InvalidOperationException) { refused=true; } Check(refused,name); }
    static Dictionary<string,object> Obj(object value) { return (Dictionary<string,object>)value; }
    static Dictionary<string,object> Json(ExaminationSnapshot s) { return Obj(new JavaScriptSerializer { MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters }.DeserializeObject(s.ToJson())); }
    static object Field(object o,string name) { return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static object Call(object o,string name,params object[] args) { return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args); }
    static Trial Blow(Patient p,int seed)
    {
        Trial t=Simulator.Build(p,seed); t.When=new DateTime(2026,11,1,8,0,0,DateTimeKind.Local).AddSeconds(seed);
        t.Packets.Add(new DevicePacket { Seconds=.01,Type=0x10,Raw=200 });
        t.Packets.Add(new DevicePacket { Seconds=8,Type=0x04,Raw=0 });
        return t;
    }
    static double[] Aggregates(Exam e) { ExamSummary s=ExamSummary.Build(e.Trials); return new double[] {s.Fvc,s.Fev1,s.Ratio,s.Pef,s.Best.Fef2575}; }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            Patient p=new Patient { FirstName="داده",LastName="آزمایشی",NationalId="0000000001",Age=35,HeightCm=175,WeightKg=75,Origin=Origin.Caucasian };
            Exam exam=Exam.CreateNew(new AppSettings { OperatorName="کارشناس تست" }); exam.Patient=p; exam.SelectionWorkflowVersion=1; // Retain regression coverage for the 5.1.1 explicit-selection records.
            Trial first=Blow(p,100); exam.AddManeuver(first);
            string firstId=first.ManeuverId; double[] firstFlow=first.Flow.ToArray(); DateTime firstWhen=first.When;
            Check(exam.Trials.Count==1 && first.AcquisitionSequence==1 && ExamSummary.Build(exam.Trials).BestTrial==first,"one maneuver works without a minimum of three");
            Refuses(delegate { exam.FinalizeExamination(); },"finalization requires an explicit operator decision");
            Check(!exam.FinalizedAt.HasValue && string.IsNullOrEmpty(exam.OperatorSelectedManeuverId),"recommendation does not silently become a saved selection");
            ExaminationSnapshot unselected=ExaminationSnapshot.Capture(exam);
            Refuses(delegate { ResultExport.SaveToDirectory(root,unselected); },"new-workflow export cannot bypass explicit primary selection");
            Refuses(delegate { using(ReportForm reportForm=new ReportForm(unselected)) { } },"new-workflow report preview cannot bypass primary selection");
            for(int i=1;i<5;i++) exam.AddManeuver(Blow(p,100+i));
            Check(exam.Trials.Count==5 && !exam.FinalizedAt.HasValue,"five maneuvers do not finalize examination");
            string saved=Path.Combine(root,"all-attempts.spiro"); ExamStore.Save(saved,exam);
            Check(ExamStore.Load(saved).Trials.Count==5,"five attempts survive persistence");
            for(int i=5;i<125;i++) exam.AddManeuver(Blow(p,100+i));
            Check(exam.Trials.Count==125 && exam.Trials[124].AcquisitionSequence==125,"125 sequential maneuvers accepted without a count limit");
            Check(exam.Trials.Select(t=>t.ManeuverId).Distinct().Count()==125,"all 125 maneuver identities unique");
            Check(exam.Trials.Select(t=>t.AcquisitionSequence).SequenceEqual(Enumerable.Range(1,125)),"original acquisition sequence never renumbered");
            Check(exam.Trials[0].ManeuverId==firstId && exam.Trials[0].When==firstWhen && exam.Trials[0].Flow.SequenceEqual(firstFlow),"new attempts do not overwrite original identity, timestamp or samples");
            ExamSummary automatic=ExamSummary.Build(exam.Trials);
            Trial chosen=exam.Trials.Last(t=>!automatic.Trials.Contains(t));
            Check(chosen.AcquisitionSequence>100 && chosen!=automatic.BestTrial,"manual test target lies beyond100 and outside top three");
            double[] aggregateBefore=Aggregates(exam);
            exam.SelectPrimary(chosen.ManeuverId,"manual","بازبین واقعی تست","منحنی این مانور بازبینی شد");
            DateTime decision=exam.SelectionTimestamp.Value;
            Check(exam.OperatorSelectedManeuverId==chosen.ManeuverId && exam.SelectionMethod=="manual" && exam.SelectionOperatorName=="بازبین واقعی تست" && exam.SelectionReason.Length>0,"explicit nonrecommended selection records method, reviewer, time and reason");
            Check(aggregateBefore.SequenceEqual(Aggregates(exam)),"manual primary selection does not replace aggregate FVC FEV1 ratio PEF FEF25-75");
            Trial incomplete=Blow(p,400); incomplete.Completed=false; incomplete.EndReason="operator stopped development test"; exam.AddManeuver(incomplete);
            Trial invalid=new Trial { Simulated=true,Completed=true }; exam.AddManeuver(invalid);
            Trial rejected=Blow(p,401); exam.AddManeuver(rejected); exam.SetRejected(rejected,true,exam.OperatorName,"rejected development attempt");
            Check(exam.OperatorSelectedManeuverId==chosen.ManeuverId && exam.SelectionTimestamp==decision,"additional completed/incomplete/invalid attempts cannot reset saved selection");
            Check(exam.Trials.Count==128 && ExamSummary.Build(exam.Trials).Candidates.Count==125,"rejected incomplete invalid attempts retained and excluded from aggregate candidates");
            Refuses(delegate { exam.SelectPrimary(incomplete.ManeuverId,"manual",exam.OperatorName,""); },"incomplete attempt cannot become primary");
            Refuses(delegate { exam.SelectPrimary(invalid.ManeuverId,"manual",exam.OperatorName,""); },"invalid attempt cannot become primary");
            Refuses(delegate { exam.SelectPrimary(rejected.ManeuverId,"manual",exam.OperatorName,""); },"rejected attempt cannot become primary");
            Refuses(delegate { exam.SetRejected(chosen,true,exam.OperatorName,""); },"rejecting current primary requires an explicit replacement first");
            Refuses(delegate { exam.SelectPrimary(firstId,"manual","",""); },"primary selection requires reviewer identity");
            Refuses(delegate { exam.SelectPrimary(chosen.ManeuverId,"automatic_suggestion",exam.OperatorName,""); },"nonrecommended choice cannot be mislabeled as automatic suggestion");
            ExamStore.Save(saved,exam); Exam reopened=ExamStore.Load(saved);
            Check(new FileInfo(saved).Length<ExamStore.MaxFileBytes && reopened.Trials.Count==128,"more than100 attempts save and reopen within the explicit byte budget");
            Check(reopened.Trials.Select(t=>t.ManeuverId).SequenceEqual(exam.Trials.Select(t=>t.ManeuverId)),"all IDs and acquisition order persist without overwriting");
            Check(reopened.Trials[0].Flow.SequenceEqual(firstFlow) && reopened.Trials[0].Packets.Count==2 && reopened.Trials[0].Packets[0].Raw==200 && reopened.Trials[0].When==firstWhen,"raw samples packets and original timestamps survive restart");
            Check(reopened.OperatorSelectedManeuverId==chosen.ManeuverId && reopened.SelectionMethod=="manual" && reopened.SelectionTimestamp==decision && reopened.SelectionOperatorName==exam.SelectionOperatorName && reopened.SelectionReason==exam.SelectionReason,"manual decision fully survives reopening");
            Check(!reopened.Trials[125].Completed && !reopened.Trials[126].Result.Valid && reopened.Trials[127].OperatorRejected && reopened.Trials[127].RejectionOperator==exam.OperatorName,"incomplete invalid and rejected states persist distinctly");
            Check(reopened.Trials[0].FinalizedResult!=null && reopened.Trials[0].Result.Fvc==exam.Trials[0].Result.Fvc,"draft attempt calculated results are persisted before finalization");
            ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(reopened); var doc=Json(snapshot);
            var selection=Obj(doc["primaryManeuverSelection"]); var report=Obj(doc["report"]);
            Check(selection["operatorSelectedManeuverId"].ToString()==chosen.ManeuverId && Convert.ToInt32(selection["originalSequence"])==chosen.AcquisitionSequence,"JSON includes saved primary ID and original sequence");
            Check(selection["automaticRecommendationId"].ToString()!=chosen.ManeuverId && report["operatorSelectedManeuverId"].ToString()==chosen.ManeuverId,"JSON distinguishes explicit primary from automatic recommendation");
            var displayed=(object[])report["displayedManeuvers"];
            Check(displayed.Length==3 && displayed.Any(x=>Obj(x)["id"].ToString()==chosen.ManeuverId),"report includes manual primary even outside automatic top three");
            Check(report["primaryManeuverLabel"].ToString()==snapshot.PrimaryLabel && snapshot.PrimaryLabel=="PRE #"+chosen.AcquisitionSequence,"report label keeps original PRE number beyond100");
            var all=(object[])doc["maneuvers"];
            Check(all.Length==128 && Obj(all[125])["status"].ToString()=="incomplete" && Obj(all[126])["status"].ToString()=="invalid" && Obj(all[127])["status"].ToString()=="operator_rejected","JSON retains all attempts and their real status");
            Check(!Convert.ToBoolean(Obj(all[125])["includedInBestReportedValues"]) && !Convert.ToBoolean(Obj(all[126])["includedInBestReportedValues"]) && !Convert.ToBoolean(Obj(all[127])["includedInBestReportedValues"]),"JSON never silently treats excluded attempts as valid best-value sources");
            Check(Obj(all[0])["source"].ToString()=="simulation" && Obj(Obj(all[125])["quality"])["warnings"]!=null,"each attempt retains provenance and quality warnings");
            Check(new JavaScriptSerializer().Serialize(doc["spirometry"])==new JavaScriptSerializer().Serialize(doc["bestReportedValues"]),"schema2 spirometry remains separately named best reported aggregate values");
            // A chosen earlier automatic suggestion stays chosen if a better maneuver arrives.
            var ordered=exam.Trials.Where(ExamSummary.CanReport).OrderBy(t=>t.Result.Fvc+t.Result.Fev1).ToList();
            Exam suggestion=Exam.CreateNew(new AppSettings { OperatorName="test operator" }); suggestion.Patient=p;
            Trial low=ordered[0].Clone(); low.ManeuverId=Guid.NewGuid().ToString("N"); suggestion.AddManeuver(low);
            suggestion.SelectPrimary(low.ManeuverId,"automatic_suggestion",suggestion.OperatorName,"");
            Trial high=ordered[ordered.Count-1].Clone(); high.ManeuverId=Guid.NewGuid().ToString("N"); suggestion.AddManeuver(high);
            Check(ExamSummary.Build(suggestion.Trials).BestTrial==high && suggestion.OperatorSelectedManeuverId==low.ManeuverId && suggestion.SelectionMethod=="automatic_suggestion","accepted automatic suggestion is not overwritten by a later recommendation");
            // Old finalized records have no retroactive inferred selection.
            Exam legacy=new Exam { Patient=p,FinalizedAt=new DateTime(2026,10,1),ReportSnapshotId=Guid.NewGuid().ToString("N") }; legacy.Trials.Add(Blow(p,3));
            string oldPath=Path.Combine(root,"legacy.spiro"); ExamStore.Save(oldPath,legacy); legacy=ExamStore.Load(oldPath);
            Check(string.IsNullOrEmpty(legacy.OperatorSelectedManeuverId) && legacy.OperatorName=="" && legacy.Trials[0].AcquisitionSequence==1,"historical finalized record gains sequence without an invented operator decision");
            Check(Json(ExaminationSnapshot.Capture(legacy))["primaryManeuverSelection"]!=null && ExaminationSnapshot.Capture(legacy).OperatorSelectedIndex==0,"legacy report explicitly has no recorded primary");
            using(MainForm form=new MainForm())
            {
                form.Show(); Application.DoEvents(); Call(form,"LoadExam",saved);
                Exam ui=(Exam)Field(form,"_exam");
                ExamSummary uiSummary=ExamSummary.Build(ui.Trials);
                DataGridView grid=(DataGridView)Field(form,"gridTrials");
                Check(grid.Rows.Count==128 && ((Trial)Field(form,"_selected")).ManeuverId==uiSummary.BestTrial.ManeuverId,"actual GUI reopen restores all history and previews the automatic aggregate representative");
                Check(((ChartPanel)Field(form,"chart")).Trial.ManeuverId==uiSummary.BestTrial.ManeuverId && grid.Rows[uiSummary.BestTrial.AcquisitionSequence-1].Cells[5].Value.ToString()==Fmt.N(uiSummary.BestTrial.Measurements.Fef2575.Value.Value,2),"automatic preview uses its curves and exposes all five requested core metrics");
                Check(grid.Columns.Count==7 && grid.Columns["SiteSelection"]==null && grid.Columns["ReportSelection"]!=null,"new GUI has no primary radio column and retains report visibility");
                Check((bool)Call(form,"CheckSource",true,false),"GUI sequential acquisition gate remains open beyond100");
                Call(form,"SelectTrial",0);
                Check(ui.SelectionWorkflowVersion==3 && !ui.PrimarySelectionRequired,"clicking a history row changes preview only and workflow3 requires no primary decision");
                double[] aggregateBeforeUi=Aggregates(ui);
                int reportColumn=grid.Columns["ReportSelection"].Index;
                bool wasReported=ui.ReportManeuverIds.Contains(ui.Trials[0].ManeuverId);
                Call(form,"ChangeChoice",0,reportColumn);
                Check(ui.ReportManeuverIds.Contains(ui.Trials[0].ManeuverId)!=wasReported && ui.SelectionWorkflowVersion==3,"report checkbox persists independently of any primary selection");
                Check(Aggregates(ui).SequenceEqual(aggregateBeforeUi),"manual report toggle never changes best-values aggregate");
                Check(!ui.FinalizedAt.HasValue && grid.Enabled,"report selection leaves examination open for further maneuvers");
                // Use the actual finish/save path for another completed acquisition.
                Trial next=Blow(p,450); typeof(MainForm).GetField("_active",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,next);
                Call(form,"FinishTrial");
                Check(ui.Trials.Count==129 && ui.SelectionWorkflowVersion==3 && !ui.PrimarySelectionRequired,"new acquisition through FinishTrial still requires no primary");
                Check(ui.ReportSelectionManual && ui.ReportManeuverIds.Contains(ui.Trials[0].ManeuverId)==!wasReported,"manual report override survives a new maneuver");
                Check(((Trial)Field(form,"_selected")).ManeuverId==ExamSummary.Build(ui.Trials).BestTrial.ManeuverId,"new acquisition restores automatic best preview");
                Call(form,"SelectTrial",125);
                Check((bool)grid.Rows[125].Cells[reportColumn].Tag && grid.Rows[125].Cells[0].ToolTipText.Contains(incomplete.EndReason),"incomplete attempt remains selectable for review with its precise warning");
                Call(form,"SelectTrial",127); Check(grid.Rows[127].Cells[0].ToolTipText.Contains("ردشده") && grid.Rows[127].Cells[0].Value.ToString()==rejected.AcquisitionSequence.ToString(),"rejected state remains visible in the compact maneuver cell tooltip");
                Call(form,"SelectTrial",0);
                using(Bitmap image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(Path.Combine(root,"operator-selection-gui.png")); }
                reopened=ui;
            }
            reopened.FinalizeExamination(); string finalPath=Path.Combine(root,"finalized.spiro"); ExamStore.Save(finalPath,reopened);
            snapshot=ExaminationSnapshot.Capture(reopened); string frozenJson=snapshot.ToJson();
            Check(ExaminationSnapshot.Capture(ExamStore.Load(finalPath)).ToJson()==frozenJson,"finalized selection aggregate values and snapshot ID stable after restart");
            Refuses(delegate { reopened.SelectPrimary(firstId,"manual","operator",""); },"finalized primary selection cannot change");
            Refuses(delegate { reopened.AddManeuver(Blow(p,999)); },"finalized examination cannot append attempts");
            ResultPackage export=ResultExport.SaveToDirectory(root,snapshot);
            Check(File.ReadAllText(export.DataPath)==frozenJson,"JSON export is exactly the saved frozen selection snapshot");
            ResultPackage repeat=ResultExport.SaveToDirectory(Path.Combine(root,"repeat"),snapshot);
            Check(export.ReportBytes.SequenceEqual(File.ReadAllBytes(export.ReportPath)) && export.ReportBytes.SequenceEqual(repeat.ReportBytes),"automatic JPG bytes are read back exactly and deterministic for the same frozen snapshot");
            // The JPG is rasterised once at 1800x2546; the guaranteed bound is the
            // readability ceiling, not the 250 KiB preferred sharing target.
            using(Image image=Image.FromFile(export.ReportPath)) Check(image.Width==1800 && image.Height==2546 && image.Width==export.ReportWidth && image.Height==export.ReportHeight && export.JpegQuality==70 && export.ReportBytes.Length<=ResultExport.JpegCeilingBytes,"report JPG is 1800x2546 at quality 70 inside the readability ceiling without a quality control");
            // The retired primary field cannot change a workflow-3 report header.
            Exam alternate=ExamStore.Load(saved); alternate.SelectPrimary(firstId,"manual",alternate.OperatorName,"");
            using(Bitmap selectedImage=SpiroReport.Render(snapshot)) using(Bitmap alternateImage=SpiroReport.Render(ExaminationSnapshot.Capture(alternate)))
            {
                bool different=false; for(int y=140;y<190 && !different;y++) for(int x=900;x<1500;x++) if(selectedImage.GetPixel(x,y)!=alternateImage.GetPixel(x,y)) { different=true; break; }
                Check(!different && snapshot.PrimaryLabel=="BEST (aggregate)","workflow3 JPG header is best-values aggregate, not a primary maneuver");
            }
            // Saved attempt analysis is not silently re-derived at reopen.
            Exam cached=ExamStore.Load(saved); double stored=cached.Trials[0].Result.Fvc;
            cached.Trials[0].Flow[0]=0; string cachedPath=Path.Combine(root,"cached.spiro"); ExamStore.Save(cachedPath,cached);
            Check(ExamStore.Load(cachedPath).Trials[0].Result.Fvc==stored,"draft save retains per-attempt calculated result");
            string budgetPath=Path.Combine(root,"byte-limit.spiro"); Exam budget=Exam.CreateNew(new AppSettings()); budget.Patient=p.Clone(); budget.AddManeuver(Blow(p,10)); ExamStore.Save(budgetPath,budget);
            byte[] previous=File.ReadAllBytes(budgetPath); budget.Patient.Note=new string('x',(int)ExamStore.MaxFileBytes);
            bool tooLarge=false; try { ExamStore.Save(budgetPath,budget); } catch(InvalidDataException ex) { tooLarge=ex.Message==ExamStore.StorageLimitMessage; }
            Check(tooLarge && File.ReadAllBytes(budgetPath).SequenceEqual(previous) && !File.Exists(budgetPath+".tmp"),"transparent40MiB technical guard refuses before replacing prior readable examination");
            Check(ExamStore.Load(budgetPath).Trials.Count==1 && budget.Trials.Count==1,"failed oversized save preserves both prior file and in-memory attempt");
            Console.WriteLine("ALL "+count+" MANEUVER SELECTION CHECKS PASSED"); return 0;
        }
        catch(Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

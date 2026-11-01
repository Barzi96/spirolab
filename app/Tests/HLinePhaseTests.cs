using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Linq;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;
class HLinePhaseTests
{
    static int count;
    static void Check(bool value,string name) { if(!value) throw new Exception(name); Console.WriteLine("PASS "+name); count++; }
    static object Get(object o,string name) { return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static object Call(object o,string name,params object[] args) { return o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args); }
    static void Mouse(DataGridView grid,int row,int column)
    {
        grid.FirstDisplayedScrollingRowIndex=row; Application.DoEvents();
        Rectangle cell=grid.GetCellDisplayRectangle(column,row,true);
        MouseEventArgs args=new MouseEventArgs(MouseButtons.Left,1,cell.Left+cell.Width/2,cell.Top+cell.Height/2,0);
        Call(grid,"OnMouseDown",args); Call(grid,"OnMouseClick",args); Call(grid,"OnMouseUp",args); Application.DoEvents();
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            using(MainForm form=new MainForm())
            {
                form.Size=new Size(1280,1000);form.Show(); Application.DoEvents();
                Patient p=new Patient {FirstName="آزمون",LastName="ساختگی",NationalId="0000000001",Origin=Origin.Caucasian};
                Call(form,"BeginExam",p,false); Exam exam=(Exam)Get(form,"_exam");
                for(int i=0;i<5;i++) exam.AddManeuver(Simulator.Build(p,100+i));
                Call(form,"RenderTrials"); Call(form,"SelectTrial",0);
                typeof(MainForm).GetField("_path",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(form,Path.Combine(root,"mouse.spiro"));
                DataGridView grid=(DataGridView)Get(form,"gridTrials");
                Mouse(grid,3,6);
                Check(exam.OperatorSelectedManeuverId==exam.Trials[3].ManeuverId && exam.SelectionMethod=="manual","real mouse radio commits before finalization on another row");
                bool selected=exam.ReportManeuverIds.Contains(exam.Trials[0].ManeuverId);
                Mouse(grid,0,7);
                Check(exam.ReportManeuverIds.Contains(exam.Trials[0].ManeuverId)!=selected,"real mouse checkbox commits immediately on another row");
                grid.Rows[0].Cells[7].AccessibilityObject.DoDefaultAction();
                Check(exam.ReportManeuverIds.Contains(exam.Trials[0].ManeuverId)==selected,"screen-reader checkbox default action commits through same selection handler");
                foreach(string id in exam.ReportManeuverIds.ToArray()) exam.SetReportManeuver(id,false);
                foreach(int index in new int[] {0,2,4}) exam.SetReportManeuver(exam.Trials[index].ManeuverId,true);
                Call(form,"RenderTrials");Call(form,"SelectTrial",0);
                grid.CurrentCell=grid.Rows[2].Cells[6];Call(grid,"OnKeyDown",new KeyEventArgs(Keys.Space));
                Check(exam.OperatorSelectedManeuverId==exam.Trials[2].ManeuverId && exam.ReportManeuverIds.Count==3,"keyboard selection commits once without coupling report choices");
                using(Bitmap shot=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(shot,new Rectangle(Point.Empty,form.Size));shot.Save(Path.Combine(root,"01-five-maneuvers.png"));}
                Trial broken=Simulator.Build(p,55);broken.Completed=false;broken.CaptureError="synthetic malformed packet";broken.EndReason="synthetic capture failure";exam.AddManeuver(broken);Call(form,"RenderTrials");Mouse(grid,5,6);
                Check(exam.OperatorSelectedManeuverId==broken.ManeuverId && (bool)grid.Rows[5].Cells[6].Tag,"technical-failure row remains selectable for review");
                bool refused=false;try {exam.ValidateSelection(true);} catch(InvalidOperationException) {refused=true;}Check(refused,"technical failure cannot silently become valid submitted result");
                Exam reopened=ExamStore.Load(Path.Combine(root,"mouse.spiro"));Check(reopened.OperatorSelectedManeuverId==broken.ManeuverId,"review selection persists without requiring clinical validity");
                Trial usable=Simulator.Build(p,66);usable.Completed=false;usable.OperatorStopped=true;exam.AddManeuver(usable);usable.Result.PlateauOk=false;usable.Result.Fet=6;
                Check(ManeuverAssessment.Assess(usable).TechnicalValid && !ManeuverAssessment.Assess(usable).QualityScreenPassed && ExamSummary.CanReport(usable),"operator-ended usable maneuver distinguished from technical failure");
                usable.Time.Add(1);Check(!ManeuverAssessment.Assess(usable).TechnicalValid,"inadequate sample timing blocks export");usable.Time.Clear();
                exam.SelectSiteManeuver(exam.Trials[2].ManeuverId);Call(form,"RenderTrials");
                Check(((Button)Get(form,"btnReport")).Enabled && ((Button)Get(form,"btnReport")).Text=="ارسال به سایت" && !exam.FinalizedAt.HasValue,"main final action enabled before finalization");
                using(var timer=new System.Windows.Forms.Timer {Interval=25})
                {
                    bool preview=false;timer.Tick+=delegate {var dialog=Application.OpenForms.OfType<SubmissionForm>().FirstOrDefault();if(dialog!=null) {preview=true;dialog.Close();}};timer.Start();
                    var task=(System.Threading.Tasks.Task)Call(form,"SendSite");while(!task.IsCompleted) {Application.DoEvents();System.Threading.Thread.Sleep(10);}task.GetAwaiter().GetResult();timer.Stop();
                    Check(preview && !exam.FinalizedAt.HasValue && exam.ExportReferences.Count==1,"send opens review, retains local JPG JSON and leaves examination editable");
                }
            }
            Exam automatic=SpiroLab.Exam.CreateNew(new AppSettings());automatic.Patient=new Patient {Origin=Origin.Caucasian};
            for(int i=0;i<12;i++)
            {
                Trial t=Simulator.Build(automatic.Patient,200+i);automatic.AddManeuver(t);
                Check(automatic.ReportManeuverIds.SequenceEqual(ExamSummary.Build(automatic.Trials).Trials.Select(x=>x.ManeuverId)) && automatic.OperatorSelectedManeuverId==ExamSummary.Build(automatic.Trials).BestTrial.ManeuverId,"automatic ranked best-one and top-three count "+(i+1));
            }
            automatic.SelectSiteManeuver(automatic.Trials[0].ManeuverId);automatic.SetReportManeuver(automatic.ReportManeuverIds[0],false);string[] manual=automatic.ReportManeuverIds.ToArray();
            automatic.AddManeuver(Simulator.Build(automatic.Patient,444));string path=Path.Combine(root,"manual.spiro");ExamStore.Save(path,automatic);automatic=ExamStore.Load(path);automatic.AddManeuver(Simulator.Build(automatic.Patient,445));
            Check(automatic.OperatorSelectedManeuverId==automatic.Trials[0].ManeuverId && automatic.ReportManeuverIds.SequenceEqual(manual),"manual overrides survive new maneuvers and restart independently");
            Trial good=automatic.Trials[0].Clone(),poor=automatic.Trials[1].Clone();
            good.Result.Fvc=2;good.Result.Fev1=1.8;good.Result.BevAvailable=good.Result.BevOk=good.Result.PlateauOk=true;good.Result.Cough=false;good.Result.Fet=6;
            poor.Result.Fvc=8;poor.Result.Fev1=7;poor.Result.BevAvailable=false;poor.Result.PlateauOk=false;poor.Result.Cough=false;poor.Result.Fet=6;
            Check(ExamSummary.Build(new Trial[] {poor,good}).BestTrial==good,"quality-screened recommendation outranks a larger unqualified FVC plus FEV1 sum");
            good.Result=poor.Result.Clone();Check(ExamSummary.Build(new Trial[] {poor,good}).BestTrial==poor,"equal quality and sum retain stable acquisition order");
            Console.WriteLine("ALL "+count+" PHASE CHECKS PASSED"); return 0;
        }
        catch(Exception e) { Console.WriteLine(e); return 1; }
    }
}

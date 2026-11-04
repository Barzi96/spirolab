using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SpiroLab;
using SpiroLab.Device;
using SpiroLab.Ui;
internal static class ClinicalUiTests
{
    static int n;
    static string root;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); n++; Console.WriteLine("PASS "+name); }
    static object Field(object o,string key) { return o.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o); }
    static void Set(object o,string key,object value) { o.GetType().GetField(key,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(o,value); }
    static object Call(object o,string method,params object[] args) { return o.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(o,args); }
    static Control Named(Control c,string name) { return c.Controls.Find(name,true).Single(); }
    static Dictionary<string,object> Obj(object o) { return (Dictionary<string,object>)o; }
    static Dictionary<string,object> Json(ExaminationSnapshot s) { return Obj(new JavaScriptSerializer { MaxJsonLength=ExaminationSnapshot.MaxJsonCharacters }.DeserializeObject(s.ToJson())); }
    static Patient Patient() { return new Patient { NationalId="0000000001",FirstName="مراجع",LastName="آزمایشی",Age=44,HeightCm=164,WeightKg=74,Origin=Origin.Caucasian }; }
    static Exam ExamWith(int blows)
    {
        Exam e=Exam.CreateNew(new AppSettings { OperatorName="کارشناس آزمایشی" }); e.SelectionWorkflowVersion=4; e.Patient=Patient(); // Historical selected-clinical-set regression.
        for(int i=0;i<blows;i++) e.AddManeuver(Simulator.Build(e.Patient,100+i)); return e;
    }
    static void Shot(Form form,string name) { Application.DoEvents(); using(Bitmap b=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(b,new Rectangle(Point.Empty,form.Size)); b.Save(Path.Combine(root,name+".png")); } }
    static void Refuses(Action work,string name) { bool refused=false; try { work(); } catch(InvalidOperationException) { refused=true; } Check(refused,name); }
    static void ApplyScale(Form form,float factor)
    {
        var fonts=Descendants(form).Concat(new Control[] {form}).Select(c=>new KeyValuePair<Control,Font>(c,c.Font)).ToArray();
        form.Scale(new SizeF(factor,factor));
        foreach(var pair in fonts) pair.Key.Font=new Font(pair.Value.FontFamily,pair.Value.Size*factor,pair.Value.Style);
        foreach(DataGridView grid in Descendants(form).OfType<DataGridView>()) { grid.ColumnHeadersHeight=(int)Math.Ceiling(52*factor); grid.RowTemplate.Height=(int)Math.Ceiling(31*factor); }
    }
    static void Space(DataGridView grid,int row,int column)
    { grid.CurrentCell=grid.Rows[row].Cells[column]; typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(grid,new object[] {new KeyEventArgs(Keys.Space)}); }
    static bool InsideParents(Control c,Form form) { Rectangle visible=c.RectangleToScreen(c.ClientRectangle); for(Control p=c.Parent;p!=null;p=p.Parent) { if(!p.RectangleToScreen(p.ClientRectangle).Contains(visible)) return false; if(p==form) break; } return true; }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            root=Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            using(PatientForm f=new PatientForm())
            {
                f.Show(); Application.DoEvents();
                Check(f.Text=="ثبت مراجع جدید" && f.Visible,"patient registration is an actual separate window");
                Check(((ComboBox)Named(f,"Origin")).SelectedIndex==0,"new patient retains Caucasian default");
                ((Button)Named(f,"SavePatient")).PerformClick();
                Check(f.Value==null && Named(f,"ValidationMessage").Text.Length>0,"required patient inputs fail inline validation");
                ((TextBox)Named(f,"NationalId")).Text="۰۰۰۰۰۰۰۰۰۱"; ((TextBox)Named(f,"FirstName")).Text="مراجع"; ((TextBox)Named(f,"LastName")).Text="آزمایشی";
                ((TextBox)Named(f,"Age")).Text="۴۴"; ((TextBox)Named(f,"Height")).Text="۱۶۴"; ((TextBox)Named(f,"Weight")).Text="۷۴";
                Shot(f,"02-patient-dialog");
                ((Button)Named(f,"SavePatient")).PerformClick();
                Check(f.DialogResult==DialogResult.OK && f.Value.Age==44 && f.Value.NationalId=="0000000001" && f.Value.HeightCm==164,"Save and Continue normalizes and returns correct patient");
            }
            using(PatientForm f=new PatientForm()) { f.Show(); ((Button)Named(f,"CancelPatient")).PerformClick(); Check(f.Value==null && f.DialogResult==DialogResult.Cancel,"patient cancel has no record or partial patient"); }
            using(PatientForm f=new PatientForm(new Patient { EmployeeCode="kept",Company="kept company",JobTitle="kept job",Note="kept note",NationalId="0000000001",FirstName="x",LastName="y" }))
            { Patient p; string error; Check(f.TryRead(out p,out error) && p.Note=="kept note" && p.EmployeeCode=="kept" && p.Company=="kept company" && p.JobTitle=="kept job","patient dialog preserves existing optional fields"); }
            for(int count=1;count<=5;count++)
            {
                Exam e=ExamWith(count);
                Check(e.SelectionWorkflowVersion==4 && !e.PrimarySelectionRequired && string.IsNullOrEmpty(e.OperatorSelectedManeuverId),"no primary maneuver in workflow4 "+count);
                Check(e.ReportManeuverIds.Count==Math.Min(count,3),"automatic report count "+count);
                e.ValidateReportSelection(true);
            }
            Exam auto=ExamWith(1); auto.Trials[0].Result.Fvc=.8; auto.Trials[0].Result.Fev1=.5; auto.AddManeuver(Simulator.Build(auto.Patient,500));
            Check(ExamSummary.BuildSelected(auto.Trials,auto.ReportManeuverIds).Fvc>=.8 && string.IsNullOrEmpty(auto.OperatorSelectedManeuverId),"new better attempt updates selected aggregate without creating a primary");
            Exam choices=ExamWith(5); string[] original=choices.ReportManeuverIds.ToArray();
            string fourth=choices.Trials.First(t=>!choices.ReportManeuverIds.Contains(t.ManeuverId)).ManeuverId;
            Refuses(delegate { choices.SetReportManeuver(fourth,true); },"fourth report checkbox refused");
            Check(choices.ReportManeuverIds.SequenceEqual(original),"refused fourth does not deselect an existing choice");
            choices.SetReportManeuver(original[1],false); choices.AddManeuver(Simulator.Build(choices.Patient,777));
            Check(choices.ReportSelectionManual && choices.ReportManeuverIds.Count==2,"manual report selection remains unchanged after another maneuver");
            choices.ValidateReportSelection(true); Check(choices.ReportManeuverIds.Count==2,"operator may report fewer than three curves");
            foreach(string id in choices.ReportManeuverIds.ToArray()) choices.SetReportManeuver(id,false);
            foreach(int i in new int[] {0,2,4}) choices.SetReportManeuver(choices.Trials[i].ManeuverId,true);
            choices.SelectSiteManeuver(choices.Trials[3].ManeuverId); // legacy field remains writable but is inert in workflow3
            choices.ValidateReportSelection(true);
            Check(choices.ReportSelectionManual && !choices.ReportManeuverIds.Contains(choices.OperatorSelectedManeuverId),"report visibility and legacy primary field are independent");
            string saved=Path.Combine(root,"selection-roundtrip.spiro"); ExamStore.Save(saved,choices); Exam reopened=ExamStore.Load(saved);
            Check(reopened.ReportManeuverIds.SequenceEqual(choices.ReportManeuverIds) && reopened.ReportSelectionManual && reopened.SelectionWorkflowVersion==4,"report choices and workflow survive restart without a required primary");
            var doc=Json(ExaminationSnapshot.Capture(reopened)); var site=Obj(doc["siteExport"]);
            Check(site["status"].ToString()=="retired_single_site_maneuver_use_bestValues" && Obj(site["measurements"]).Count==0,"workflow3 JSON retires the single-maneuver site export");
            var display=(object[])Obj(doc["report"])["displayedManeuvers"];
            Check(display.Select(x=>Obj(x)["id"].ToString()).SequenceEqual(new string[] {reopened.Trials[0].ManeuverId,reopened.Trials[2].ManeuverId,reopened.Trials[4].ManeuverId}),"JPG displays exactly the manual 1,3,5 report set");
            ExamSummary all=ExamSummary.BuildSelected(reopened.Trials,reopened.ReportManeuverIds);
            Check(Convert.ToDouble(Obj(Obj(doc["bestReportedValues"])["FVC"])["value"])==all.Fvc,"JSON best values use exactly the selected three maneuvers, not the report display subset");
            Check(Convert.ToDouble(Obj(Obj(doc["bestReportedValues"])["FEV1"])["value"])==all.Fev1 && Obj(Obj(doc["bestReportedValues"])["FVC"])["valueSource"].ToString()!="unavailable","best FEV1 is independent and sourced explicitly");
            Check(Obj(doc["vextSelection"])["valueMl"]==null,"simulated-only examination yields no clinic VEXT and never zero");
            Trial selected=reopened.Trials[3];
            using(ExaminationDetailsForm f=new ExaminationDetailsForm(reopened,selected))
            {
                ((TextBox)Named(f,"ManeuverVext")).Text="132"; ((TextBox)Named(f,"ManeuverVextSource")).Text="reviewed development fixture";
                string problem; Check(!f.Apply(out problem),"manual VEXT requires explicit review");
                foreach(Control c in Descendants(f)) if(c is CheckBox) ((CheckBox)c).Checked=true;
                Check(f.Apply(out problem),"reviewed maneuver VEXT saved in examination details");
            }
            doc=Json(ExaminationSnapshot.Capture(reopened));
            Check(!Obj(Obj(doc["siteExport"])["measurements"]).ContainsKey("VEXT"),"manual maneuver review never replaces the clinic VEXT rule");
            Check(Convert.ToDouble(Obj(Obj(((object[])doc["maneuvers"])[3])["vextReview"])["valueMl"])==132,"manual review stays recorded locally for the operator");
            reopened.SelectSiteManeuver(reopened.Trials[0].ManeuverId);
            Check(!Obj(Obj(Json(ExaminationSnapshot.Capture(reopened))["siteExport"])["measurements"]).ContainsKey("VEXT"),"changing the legacy site maneuver never fabricates a clinic VEXT");
            reopened.SelectSiteManeuver(selected.ManeuverId);
            DateTime date;
            Check(ExaminationDates.TryParse("۱۳۹۹/۱۲/۳۰",out date) && ExaminationDates.Jalali(date)=="1399/12/30","Jalali leap day round trip uses PersianCalendar");
            Check(!ExaminationDates.TryParse("۱۴۰۰/۱۲/۳۰",out date),"nonleap Esfand30 rejected");
            using(JalaliDateForm calendar=new JalaliDateForm(new DateTime(2021,3,20)))
            { calendar.Show(); Check(Descendants(calendar).OfType<Button>().Any(b=>b.Tag is DateTime && (DateTime)b.Tag==new DateTime(2021,3,20)),"real calendar picker contains leap day"); Call(calendar,"MoveMonth",1); Shot(calendar,"jalali-calendar"); }
            string uiPath=Path.Combine(root,"ui-session.spiro");
            using(MainForm form=new MainForm())
            {
                Set(form,"_settings",new AppSettings { OperatorName="کارشناس آزمایشی" });
                form.Size=new Size(1280,800); form.Show(); Application.DoEvents(); Shot(form,"01-main-empty");
                Check(!((Button)Field(form,"btnStart")).Enabled && form.Controls.Find("FirstName",true).Length==0 && form.Controls.Find("ManeuverVext",true).Length==0,"empty clinical UI has no sidebar or advanced inputs and cannot start");
                Console.WriteLine("LAYOUT connection "+((Control)Field(form,"lblConnection")).Bounds+" parent "+((Control)Field(form,"lblConnection")).Parent.Bounds);
                Exam before=(Exam)Field(form,"_exam"); using(System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=25 })
                {
                    timer.Tick+=delegate { PatientForm dialog=Application.OpenForms.OfType<PatientForm>().FirstOrDefault(); if(dialog!=null) ((Button)Named(dialog,"CancelPatient")).PerformClick(); };
                    timer.Start(); Call(form,"NewExam"); timer.Stop();
                }
                Check(object.ReferenceEquals(before,Field(form,"_exam")) && !(bool)Field(form,"_hasPatient"),"canceling actual main New Patient modal leaves active session untouched");
                Set(form,"_path",uiPath); Call(form,"BeginExam",Patient(),true);
                Exam ui=(Exam)Field(form,"_exam"); uiPath=(string)Field(form,"_path");
                Check(ui.ExaminationDate==DateTime.Today && File.Exists(uiPath) && ui.Patient.NationalId=="0000000001","registration creates a new saved examination associated with patient");
                Check(((TextBox)Field(form,"txtExamDate")).Text==ExaminationDates.DisplayJalali(DateTime.Today),"top date displays Windows local date in Persian Jalali digits");
                Shot(form,"03-main-patient");
                for(int i=0;i<5;i++) { Set(form,"_active",Simulator.Build(ui.Patient,100+i)); Call(form,"FinishTrial"); }
                DataGridView grid=(DataGridView)Field(form,"gridTrials");
                Check(grid.Rows.Count==5 && grid.Columns.Count==8 && grid.Columns["SiteSelection"]==null && grid.Columns["ReportSelection"] is ReportColumn,"compact history includes FET and one report checkbox column");
                Check(grid.Rows.Cast<DataGridViewRow>().Count(r=>Convert.ToBoolean(r.Cells["ReportSelection"].Value))==3,"automatic report selection checks up to three real curves");
                int reportColumn=grid.Columns["ReportSelection"].Index;
                foreach(string id in ui.ReportManeuverIds.ToArray()) Space(grid,ui.Trials.FindIndex(t=>t.ManeuverId==id),reportColumn);
                Space(grid,0,reportColumn); Space(grid,2,reportColumn); Space(grid,4,reportColumn);
                Check(ui.SelectionWorkflowVersion==5 && ui.ReportManeuverIds.SequenceEqual(new string[] {ui.Trials[0].ManeuverId,ui.Trials[2].ManeuverId,ui.Trials[4].ManeuverId}),"keyboard display selection directly saves1,3,5 without a primary");
                Space(grid,3,reportColumn); Check(((Label)Field(form,"lblStatus")).Text=="برای گزارش حداکثر سه مانور قابل انتخاب است." && ui.ReportManeuverIds.Count==3,"fourth checkbox gives short Persian inline message");
                Call(form,"SelectTrial",0); Check(ui.OperatorSelectedManeuverId!=ui.Trials[0].ManeuverId && ui.ReportManeuverIds.Count==3,"row preview does not alter report choices or create a primary");
                Call(form,"SelectTrial",3); Shot(form,"04-main-five");
                string stableId=ui.ExaminationId; Call(form,"LoadExam",uiPath); ui=(Exam)Field(form,"_exam");
                Check(ui.ExaminationId==stableId && ui.ReportManeuverIds.Count==3 && ui.SelectionWorkflowVersion==5,"GUI reopens saved display selections without a primary");
                ((TextBox)Field(form,"txtExamDate")).Text="۱۴۰۵/۰۷/۲۷"; Check((bool)Call(form,"SyncMetadata") && (bool)Call(form,"SaveExam",false),"manual Jalali date edit validates and saves");
                Call(form,"LoadExam",uiPath); ui=(Exam)Field(form,"_exam");
                Check(ui.ExaminationDate==new DateTime(2026,10,19),"historical chosen date does not become today on reopen");
                ui.FinalizeExamination(); ExamStore.Save(uiPath,ui); ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(ui);
                ResultPackage output=ResultExport.SaveToDirectory(root,snapshot);
                using(ReportForm report=new ReportForm(snapshot))
                {
                    report.Show(); Application.DoEvents(); Shot(report,"06-report-preview");
                    Check(report.Text.StartsWith("پیش‌نمایش گزارش اسپیرومتری") && !Descendants(report).OfType<NumericUpDown>().Any() && !Descendants(report).OfType<Label>().Any(c=>c.Text.Contains("کیفیت JPEG")),"report preview is an operator screen with no JPEG quality control");
                }
                Check(snapshot.TestedAt.Date==ui.ExaminationDate && Obj(Json(snapshot)["examination"])["examinationDate"].ToString()=="2026-10-19","JPG snapshot and JSON share chosen historical date");
                Call(form,"RecordExport",output); Check(ExamStore.Load(uiPath).ExportReferences.Count==1 && ExamStore.Load(uiPath).ExportReferences[0].DataPath==output.DataPath,"generated output references are stored without altering frozen clinical data");
                string previous=ui.ExaminationId; Call(form,"BeginExam",Patient(),true); Check(((Exam)Field(form,"_exam")).ExaminationId!=previous && ExamStore.Load(uiPath).ExaminationId==previous,"same patient can start another session without overwriting previous exam");
                Set(form,"_developerMode",true); Call(form,"BeginExam",Patient(),false);
                Check(!(bool)Field(form,"_developerMode") && !((Button)Field(form,"btnStart")).Enabled,"new clinical examination never inherits simulation mode");
            }
            using(SettingsForm settings=new SettingsForm(new AppSettings(),delegate { return DeviceDiagnostics.Read(); }))
            {
                settings.Show(); Shot(settings,"05-settings");
                Check(Named(settings,"DeviceDiagnostics")!=null && Named(settings,"DefaultCenter").Text==AppSettings.DefaultCenter,"settings exposes global defaults and retained device diagnostics");
                Check(!Descendants(settings).OfType<LinkLabel>().Any(c=>c.Text.Contains("توسعه") || c.Text.Contains("آزمون")) && !Descendants(settings).OfType<ToolStripItem>().Any(),"settings has no development or simulated-maneuver control");
            }
            // Physical OS scaling is unchanged: exercise equivalent 100/125/150% control/font layout stress.
            foreach(Size size in new Size[] {new Size(1280,800),new Size(1366,768),new Size(1600,900),new Size(1920,1080)})
                foreach(float scale in new float[] {1,1.25f,1.5f})
                    using(MainForm form=new MainForm())
                    {
                        Set(form,"_settings",new AppSettings { OperatorName="کارشناس آزمایشی" });
                        form.AutoScaleMode=AutoScaleMode.None; if(scale!=1) ApplyScale(form,scale);
                        form.Show(); form.Size=size; Call(form,"BeginExam",Patient(),false);
                        Exam e=(Exam)Field(form,"_exam"); for(int i=0;i<5;i++) { e.AddManeuver(Simulator.Build(e.Patient,100+i)); }
                        Call(form,"RenderTrials"); Call(form,"RestorePrimaryPreview"); Application.DoEvents();
                        DataGridView table=(DataGridView)Field(form,"gridTrials"); ChartPanel plot=(ChartPanel)Field(form,"chart");
                        Check(table.Columns.Cast<DataGridViewColumn>().Sum(c=>c.Width)<=table.ClientSize.Width && table.HorizontalScrollingOffset==0,"no horizontal column overflow "+size+" scale"+scale);
                        Check(plot.Height>120 && plot.Width>700 && ((Button)Field(form,"btnReport")).Bounds.Right<=((Button)Field(form,"btnReport")).Parent.ClientSize.Width,"usable plot and contained actions "+size+" scale"+scale);
                        Rectangle[] buttons=new string[] {"btnStart","btnFinish","btnStop","btnReport"}.Select(k=>((Control)Field(form,k)).Bounds).ToArray();
                        Check(!buttons[0].IntersectsWith(buttons[1]) && !buttons[1].IntersectsWith(buttons[2]),"no action button overlap "+size+" scale"+scale);
                        Check(new string[] {"btnStart","btnFinish","btnStop","btnReport","btnNew","btnOpen","btnSettings","btnLogin"}.All(k=> { Button b=(Button)Field(form,k); Size text=TextRenderer.MeasureText(b.Text,b.Font); return text.Width+b.Padding.Horizontal<=b.ClientSize.Width+3 && text.Height+b.Padding.Vertical<=b.ClientSize.Height+3; }),"button captions fit at scaled text size "+size+" scale"+scale);
                        Check(form.ClientRectangle.Contains(form.RectangleToClient(table.RectangleToScreen(table.ClientRectangle))) && new string[] {"btnStart","btnFinish","btnStop","btnReport","btnNew","btnOpen","btnSettings","btnLogin"}.All(k=> { Control c=(Control)Field(form,k); return form.ClientRectangle.Contains(form.RectangleToClient(c.RectangleToScreen(c.ClientRectangle))); }),"table and toolbar stay inside actual window "+size+" scale"+scale);
                        Check(table.Columns.Cast<DataGridViewColumn>().All(c=>TextRenderer.MeasureText(c.HeaderText,table.ColumnHeadersDefaultCellStyle.Font??table.Font,new Size(c.Width-8,1000),TextFormatFlags.WordBreak|TextFormatFlags.NoPadding).Height+4<=table.ColumnHeadersHeight),"wrapped table headers fit "+size+" scale"+scale);
                        Check(table.ClientSize.Height-table.ColumnHeadersHeight>=table.RowTemplate.Height,"at least one complete data row is readable "+size+" scale"+scale);
                        Check(new string[] {"lblPatient","txtExamDate","btnNew","btnStart","btnFinish","btnStop","btnReport","btnLogin"}.All(k=>InsideParents((Control)Field(form,k),form)) && ((Control)Field(form,"lblPatient")).Height>=((Control)Field(form,"lblPatient")).Font.Height,"patient date and actions are not clipped by parent panels "+size+" scale"+scale);
                        Check((plot.SideBySide?plot.Height:plot.Height/2)>100*scale,"responsive plot orientation preserves readable axis height "+size+" scale"+scale);
                        Check(plot.SideBySide,"desktop charts stay in a horizontal row at "+size+" scale"+scale);
                        Trial plotted=plot.Trial; double[] flow=plotted.Flow.ToArray();
                        plot.VisibleSamples=plotted.Flow.Count/2;Shot(form,"live-"+size.Width+"x"+size.Height+"-"+(int)(scale*100));
                        plot.VisibleSamples=-1;
                        Check(plotted.Flow.SequenceEqual(flow) && plot.SideBySide,"live redraw preserves samples and horizontal layout "+size+" scale"+scale);
                        plot.Trial=null;Shot(form,"empty-"+size.Width+"x"+size.Height+"-"+(int)(scale*100));plot.Trial=plotted;
                        Shot(form,"layout-"+size.Width+"x"+size.Height+"-"+(int)(scale*100));
                    }
            File.WriteAllText(Path.Combine(root,"visual-acceptance.txt"),"Native WinForms windows launched/rendered. Actual OS scaling unchanged. 125/150% are equivalent control/font scaling stress, not a claim of changing Windows display settings. Review PNGs before delivery. Real USB/second-PC pending.");
            Console.WriteLine("ALL "+n+" CLINICAL UI CHECKS PASSED"); return 0;
        }
        catch(Exception e) { Console.WriteLine(e); return 1; }
    }
    static IEnumerable<Control> Descendants(Control parent) { foreach(Control child in parent.Controls) { yield return child; foreach(Control c in Descendants(child)) yield return c; } }
}


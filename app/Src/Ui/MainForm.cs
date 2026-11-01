using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SpiroLab.Device;
using SpiroLab.HLine;
using System.Threading.Tasks;
namespace SpiroLab.Ui
{
    public sealed class MainForm : Form
    {
        readonly DeviceSession _device=new DeviceSession();
        readonly HLineClient _hline;
        readonly ISubmissionGateway _submissionGateway;
        readonly MedicalRecord _testRecord;
        AppSettings _settings;
        Exam _exam;
        string _path;
        bool _busy,_dirty,_fast,_rendering,_simulating,_hasPatient,_developerMode,_sizingLayout;
        volatile bool _stop;
        Trial _active,_selected;
        PacketDecoder _decoder;
        readonly Queue<DevicePacket> _pending=new Queue<DevicePacket>();
        readonly object _queueLock=new object();
        readonly ToolTip _tips=new ToolTip();
        System.Windows.Forms.Timer _timer;
        int _simPos,_decodedLiveSamples;
        bool _receivedLiveSamples;
        string _safeError="none";
        ChartPanel chart;
        DataGridView gridTrials;
        TextBox txtExamDate;
        Label lblConnection,lblSiteConnection,lblStatus,lblLive,lblSummary;
        LinkLabel lblPatient;
        Button btnStart,btnStop,btnReport,btnSettings,btnNew,btnOpen,btnCalendar;
        List<string> _legacyReportIds=new List<string>();
        public MainForm():this(new HLineClient(),null,null) { }
        public MainForm(HLineClient hline,ISubmissionGateway submissionGateway,MedicalRecord designatedTestRecord)
        {
            _hline=hline;_submissionGateway=submissionGateway;_testRecord=designatedTestRecord;
            _settings=AppSettings.Load(); _exam=Exam.CreateNew(_settings);
            ClinicalStyle.Form(this,"اسپیرومتری طب کار — SpiroLab 5.3.1",new Size(1280,820));
            StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(780,500);
            BuildUi(); PopulateMetadata(); _device.Line+=AppendLog;
            _timer=new System.Windows.Forms.Timer { Interval=50 }; _timer.Tick+=Tick;
            FormClosing+=ClosingForm; UpdateControls();
            if(!string.IsNullOrEmpty(_settings.LoadWarning)) lblStatus.Text=_settings.LoadWarning;
        }
        public void SelectTab(int index) { }
        public void ConnectOnLaunch() { ConnectDevice(false); }
        public void RunDemoForTest()
        {
            _fast=Array.IndexOf(Environment.GetCommandLineArgs(),"--slow")<0;
            if(!_hasPatient)
            {
                Patient p=new Patient { NationalId="0012345678",FirstName="مراجع",LastName="نمایشی",Age=34,HeightCm=178,WeightKg=82,Origin=Origin.Caucasian };
                BeginExam(p,false);
            }
            StartSimulation();
        }
        Button MakeButton(string text,EventHandler click,bool primary) { return ClinicalStyle.Button(text,click,primary); }
        void BuildUi()
        {
            TableLayoutPanel root=new TableLayoutPanel { Name="ClinicalLayout",Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(18,8,18,8),RightToLeft=RightToLeft.No };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent,66)); root.RowStyles.Add(new RowStyle(SizeType.Percent,34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,54)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,25)); Controls.Add(root);
            TableLayoutPanel toolbar=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,RightToLeft=RightToLeft.Yes };
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,72)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));
            FlowLayoutPanel buttons=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,WrapContents=false };
            btnNew=MakeButton("مراجع جدید",delegate { NewExam(); },true); btnOpen=MakeButton("پرونده قبلی",delegate { OpenExam(); },false); btnSettings=MakeButton("تنظیمات",delegate { OpenSettings(); },false);
            buttons.Controls.AddRange(new Control[] {btnNew,btnOpen,btnSettings}); toolbar.Controls.Add(buttons,0,0);
            lblConnection=ClinicalStyle.Label("● متصل نیست"); lblConnection.ForeColor=Color.FromArgb(113,126,138); lblConnection.Cursor=Cursors.Hand;
            lblConnection.Click+=delegate { if(!_busy) OpenSettings(); }; _tips.SetToolTip(lblConnection,"اتصال و جزئیات دستگاه در تنظیمات");
            TableLayoutPanel connections=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2}; connections.RowStyles.Add(new RowStyle(SizeType.Percent,50));connections.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            lblSiteConnection=ClinicalStyle.Label("سایت: متصل نیست");lblSiteConnection.Cursor=Cursors.Hand;lblSiteConnection.Click+=delegate {if(!_busy) LoginSite();};
            connections.Controls.Add(lblSiteConnection,0,0);connections.Controls.Add(lblConnection,0,1);toolbar.Controls.Add(connections,1,0); root.Controls.Add(toolbar,0,0);
            TableLayoutPanel strip=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,RightToLeft=RightToLeft.Yes,Padding=new Padding(4,4,4,8) };
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,275));
            strip.RowStyles.Add(new RowStyle(SizeType.Percent,52)); strip.RowStyles.Add(new RowStyle(SizeType.Percent,48));
            lblPatient=new LinkLabel { Dock=DockStyle.Fill,Font=new Font("Tahoma",11,FontStyle.Bold),LinkColor=ClinicalStyle.Ink,ActiveLinkColor=ClinicalStyle.Accent,LinkBehavior=LinkBehavior.HoverUnderline,TextAlign=ContentAlignment.MiddleLeft,RightToLeft=RightToLeft.Yes,AutoEllipsis=true };
            lblPatient.LinkClicked+=delegate { ShowDetails(); }; _tips.SetToolTip(lblPatient,"مشاهده مشخصات و جزئیات معاینه"); strip.Controls.Add(lblPatient,0,0);
            lblLive=ClinicalStyle.Label("برای شروع، مراجع جدید را ثبت کنید."); lblLive.ForeColor=Color.FromArgb(104,121,137); strip.Controls.Add(lblLive,0,1);
            TableLayoutPanel date=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,RightToLeft=RightToLeft.Yes,Padding=new Padding(4,9,0,9) };
            date.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40)); date.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45)); date.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,15));
            date.Controls.Add(ClinicalStyle.Label("تاریخ معاینه"),0,0);
            txtExamDate=new TextBox { Name="ExaminationDate",Dock=DockStyle.Fill,RightToLeft=RightToLeft.No,TextAlign=HorizontalAlignment.Center,MaxLength=10,Margin=new Padding(2,3,2,3) };
            txtExamDate.Validated+=delegate { if(!_rendering && _hasPatient && SyncMetadata()) { SaveExam(false); PopulateMetadata(); } };
            btnCalendar=new Button { Text="▦",Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(2,2,2,2),AccessibleName="انتخاب تاریخ شمسی" }; btnCalendar.FlatAppearance.BorderColor=ClinicalStyle.Border;
            btnCalendar.Click+=delegate { DateTime selected; if(!ExaminationDates.TryParse(txtExamDate.Text,out selected)) selected=DateTime.Today; using(JalaliDateForm f=new JalaliDateForm(selected)) if(f.ShowDialog(this)==DialogResult.OK) { txtExamDate.Text=ExaminationDates.Jalali(f.Value); if(SyncMetadata()) SaveExam(false); } };
            date.Controls.Add(txtExamDate,1,0); date.Controls.Add(btnCalendar,2,0); strip.Controls.Add(date,1,0); strip.SetRowSpan(date,2); root.Controls.Add(strip,0,1);
            chart=new ChartPanel { Name="LiveSpirometryChart",Dock=DockStyle.Fill,RightToLeft=RightToLeft.No,Margin=new Padding(0,0,0,10),Font=new Font("Tahoma",9) }; root.Controls.Add(chart,0,2);
            Panel history=new Panel { Dock=DockStyle.Fill,Margin=new Padding(0) };
            lblSummary=ClinicalStyle.Label("مانورها"); lblSummary.Dock=DockStyle.Top; lblSummary.Height=27; lblSummary.Font=new Font("Tahoma",9,FontStyle.Bold);
            gridTrials=new DataGridView { Name="ManeuverHistory",Dock=DockStyle.Fill,ReadOnly=false,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,MultiSelect=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,ScrollBars=ScrollBars.Vertical,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,RightToLeft=RightToLeft.Yes,EnableHeadersVisualStyles=false,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,ColumnHeadersHeight=52,AutoGenerateColumns=false };
            string[] captions={"مانور","حجم\nFVC (L)","حجم ثانیه اول\nFEV1 (L)","نسبت\nFEV1/FVC (%)","جریان بیشینه\nPEF (L/s)","جریان میانی\nFEF25–75 (L/s)"};
            float[] weights={9,13,14,14,13,15,11,11};
            for(int i=0;i<captions.Length;i++) gridTrials.Columns.Add(new DataGridViewTextBoxColumn { Name="Metric"+i,HeaderText=captions[i],ReadOnly=true,FillWeight=weights[i],MinimumWidth=25,SortMode=DataGridViewColumnSortMode.NotSortable });
            RadioColumn radio=new RadioColumn { Name="SiteSelection",HeaderText="ارسال به سامانه",FillWeight=weights[6],MinimumWidth=25 }; radio.SelectRow=delegate(int row) { ChangeChoice(row,6); }; gridTrials.Columns.Add(radio);
            gridTrials.Columns.Add(new ReportColumn { Name="ReportSelection",HeaderText="نمایش در گزارش",ReadOnly=true,FillWeight=weights[7],MinimumWidth=25,SortMode=DataGridViewColumnSortMode.NotSortable,ToggleRow=delegate(int row) {ChangeChoice(row,7);} });
            gridTrials.RowTemplate.Height=31;
            gridTrials.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(236,242,246); gridTrials.ColumnHeadersDefaultCellStyle.ForeColor=ClinicalStyle.Ink; gridTrials.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; gridTrials.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;
            gridTrials.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; gridTrials.DefaultCellStyle.SelectionBackColor=Color.FromArgb(223,239,242); gridTrials.DefaultCellStyle.SelectionForeColor=ClinicalStyle.Ink; gridTrials.GridColor=Color.FromArgb(233,238,242); gridTrials.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
            gridTrials.SelectionChanged+=delegate { if(!_rendering && gridTrials.CurrentRow!=null) SelectTrial(gridTrials.CurrentRow.Index); };
            gridTrials.CellClick+=delegate(object sender,DataGridViewCellEventArgs e) { ChangeChoice(e.RowIndex,e.ColumnIndex); };
            gridTrials.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.Space && gridTrials.CurrentCell!=null && gridTrials.CurrentCell.ColumnIndex>=6) { ChangeChoice(gridTrials.CurrentCell.RowIndex,gridTrials.CurrentCell.ColumnIndex); e.Handled=e.SuppressKeyPress=true; } };
            history.Controls.Add(gridTrials); history.Controls.Add(lblSummary); root.Controls.Add(history,0,3);
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,WrapContents=false,Padding=new Padding(0,6,0,0) };
            btnStart=MakeButton("شروع تست",delegate { if(_developerMode) StartSimulation(); else StartTest(); },true); btnStop=MakeButton("توقف مانور",delegate { StopTest(); },false); btnReport=MakeButton("ارسال به سایت",async delegate { await SendSite(); },false);
            ContextMenuStrip localMenu=new ContextMenuStrip();localMenu.Items.Add("ذخیره JPG + JSON",null,delegate {ShowReport();});btnReport.ContextMenuStrip=localMenu; _tips.SetToolTip(btnReport,"بازبینی ارسال؛ کلیک راست برای خروجی محلی JPG + JSON");
            actions.Controls.AddRange(new Control[] {btnStart,btnStop,btnReport}); root.Controls.Add(actions,0,4);
            lblStatus=ClinicalStyle.Label("آماده"); lblStatus.Font=new Font("Tahoma",8.5f); lblStatus.ForeColor=Color.FromArgb(104,121,137); root.Controls.Add(lblStatus,0,5);
            root.Layout+=delegate
            {
                if(_sizingLayout) return; _sizingLayout=true;
                try
                {
                    root.RowStyles[0].Height=btnNew.GetPreferredSize(Size.Empty).Height+btnNew.Margin.Vertical+buttons.Margin.Vertical+toolbar.Margin.Vertical+2;
                    root.RowStyles[1].Height=Math.Max(lblPatient.Font.Height+lblLive.Font.Height+lblPatient.Margin.Vertical+lblLive.Margin.Vertical+strip.Padding.Vertical+strip.Margin.Vertical+4,txtExamDate.PreferredSize.Height+txtExamDate.Margin.Vertical+date.Padding.Vertical+date.Margin.Vertical+strip.Padding.Vertical+strip.Margin.Vertical+4);
                    root.RowStyles[4].Height=Math.Max(btnStart.GetPreferredSize(Size.Empty).Height,btnReport.GetPreferredSize(Size.Empty).Height)+btnStart.Margin.Vertical+actions.Padding.Vertical+actions.Margin.Vertical+2;
                    root.RowStyles[5].Height=lblStatus.Font.Height+lblStatus.Margin.Vertical+2;
                    float available=root.ClientSize.Height-root.Padding.Vertical-root.RowStyles[0].Height-root.RowStyles[1].Height-root.RowStyles[4].Height-root.RowStyles[5].Height;
                    root.RowStyles[3].SizeType=SizeType.Absolute;
                    root.RowStyles[3].Height=Math.Max(gridTrials.ColumnHeadersHeight+gridTrials.RowTemplate.Height+lblSummary.Height+4,available*.34f);
                }
                finally { _sizingLayout=false; }
            };
        }
        void Post(Action action)
        {
            if(IsDisposed || Disposing) return;
            try { if(InvokeRequired) BeginInvoke((MethodInvoker)delegate { if(!IsDisposed) action(); }); else action(); } catch(InvalidOperationException) { }
        }
        void AppendLog(string line) { System.Diagnostics.Debug.WriteLine(line); }
        void Error(string message) { lblStatus.Text=message; _tips.SetToolTip(lblStatus,message); }
        Patient ReadPatient() { if(!_hasPatient) { Error("ابتدا مراجع جدید را ثبت کنید."); return null; } return _exam.Patient.Clone(); }
        void UpdateControls()
        {
            bool editable=!_busy && !_exam.FinalizedAt.HasValue;
            btnNew.Enabled=btnOpen.Enabled=btnSettings.Enabled=!_busy;
            btnStart.Enabled=editable && _hasPatient && (_developerMode || _device.Connected);
            btnStart.Text=_developerMode?"مانور نمایشی":_exam.Trials.Count>0?"تست بعدی":"شروع تست";
            btnStop.Enabled=_busy && _active!=null; gridTrials.Enabled=true;
            txtExamDate.Enabled=btnCalendar.Enabled=editable && _hasPatient;
            btnReport.Enabled=!_busy && _hasPatient && _exam.Trials.Count>0;
            _tips.SetToolTip(btnStart,!_hasPatient?"ابتدا مراجع جدید را ثبت کنید.":!_device.Connected && !_developerMode?"اتصال دستگاه را در تنظیمات بررسی کنید.":"ثبت یک مانور تازه");
            _tips.SetToolTip(btnReport,btnReport.Enabled?"بازبینی ارسال به سایت؛ کلیک راست برای JPG + JSON محلی":"ابتدا یک مانور ثبت کنید.");
            lblSiteConnection.Text="سایت: "+_hline.StatusText;
            lblConnection.Text=_busy && _active!=null?"● در حال دریافت":_device.Connected?"● متصل":"● متصل نیست";
            lblConnection.ForeColor=_device.Connected?ClinicalStyle.Accent:Color.FromArgb(113,126,138);
            lblPatient.Enabled=_hasPatient && !_busy;
            foreach(DataGridViewRow row in gridTrials.Rows) { row.Cells[6].Tag=row.Cells[7].Tag=editable; row.Cells[7].Style.ForeColor=editable?ClinicalStyle.Ink:Color.LightGray; }
        }
        bool CanReport()
        {
            if(ExamSummary.Build(_exam.Trials).Best==null) return false;
            try { _exam.ValidateSelection(_exam.PrimarySelectionRequired); _exam.ValidateReportSelection(true); return true; } catch { return false; }
        }

        private void ConnectDevice(bool startAfter,Action completed=null)
        {
            if(_busy) return; if(startAfter && ReadPatient()==null) return;
            _busy=true; UpdateControls(); lblStatus.Text="در حال اتصال به دستگاه…";
            Thread worker=new Thread(delegate()
            {
                string error; bool ok=_device.Connect(out error);
                Post(delegate
                {
                    _busy=false; UpdateControls(); lblStatus.Text=ok?"دستگاه آماده است":"اتصال برقرار نشد";
                    if(!ok) { _safeError="device_open_or_control_failed"; Error("اتصال برقرار نشد؛ تنظیمات دستگاه را بررسی کنید."); } else if(startAfter) StartTest(); if(completed!=null) completed();
                });
            }) { IsBackground=true }; worker.Start();
        }
        private void DisconnectDevice() { if(_busy) return; _device.Disconnect(); UpdateControls(); lblStatus.Text="اتصال دستگاه قطع شد."; }
        private bool CheckSource(bool simulated,bool imported)
        {
            if(_exam.FinalizedAt.HasValue) { Error("این معاینه نهایی شده است؛ مراجع جدید را انتخاب کنید."); return false; }
            if(_dirty && !SaveExam(false)) return false;
            if(_exam.Trials.Count==0) return true;
            Trial first=_exam.Trials[0]; if(first.Simulated==simulated && first.Imported==imported) return true;
            Error("برای تغییر منبع بین دستگاه، نمایش و بازپخش، ابتدا «مراجع جدید» را بزنید. پروندهٔ فعلی ذخیره می‌شود."); return false;
        }
        private void StartTest()
        {
            if(_busy || !CheckSource(false,false)) return;
            Patient p=ReadPatient(); if(p==null) return; if(!_device.Connected) { ConnectDevice(true); return; }
            if(!SyncMetadata()) return;
            _receivedLiveSamples=false; _decodedLiveSamples=0;
            _exam.Patient=p; _stop=false; _decoder=new PacketDecoder(); _active=new Trial { Completed=false }; _selected=null; _busy=true;
            lock(_queueLock) _pending.Clear(); chart.Trial=_active; chart.Result=null; lblLive.Text="آماده‌سازی دستگاه…";
             UpdateControls(); _timer.Start(); Trial capturing=_active;
            Thread worker=new Thread(delegate()
            {
                CaptureOutcome outcome=_device.Capture(capturing,delegate { return _stop; },delegate(DevicePacket packet) { lock(_queueLock) _pending.Enqueue(packet); });
                Post(delegate { DrainPackets(); _decodedLiveSamples=capturing.Flow.Count; _receivedLiveSamples=_decodedLiveSamples>0; _safeError=_receivedLiveSamples?"none":"no_valid_live_samples"; capturing.Completed=outcome.Completed && capturing.CaptureError.Length==0; capturing.OperatorStopped=!outcome.Completed && outcome.Reason=="توقف توسط کاربر"; if(!outcome.Completed && !capturing.OperatorStopped) capturing.CaptureError=outcome.Reason??"capture_failed"; capturing.EndReason=outcome.Reason??capturing.EndReason; capturing.DeviceValues=outcome.Results; FinishTrial(); });
            }) { IsBackground=true }; worker.Start();
        }
        private void StartSimulation()
        {
            if(_busy || !CheckSource(true,false)) return; Patient p=ReadPatient(); if(p==null) return;
            if(!SyncMetadata()) return;
            _exam.Patient=p; _active=Simulator.Build(p,Environment.TickCount); _active.Completed=false;
            _developerMode=true; _simPos=0; _simulating=true; _busy=true; _selected=null; chart.Trial=_active; chart.Result=null; chart.VisibleSamples=0;
            lblLive.Text="نمایشی — دادهٔ ساختگی";  UpdateControls(); _timer.Start();
        }
        private void DrainPackets()
        {
            if(_active==null) return;
            try { lock(_queueLock) while(_pending.Count>0) _decoder.Add(_active,_pending.Dequeue()); }
            catch(Exception ex) { _stop=true; _active.CaptureError=_active.EndReason=ex.Message; AppendLog(ex.Message); }
        }
        private void Tick(object sender,EventArgs args)
        {
            if(_active==null) return;
            if(_simulating)
            {
                _simPos=Math.Min(_active.Flow.Count,_simPos+(_fast?80:5)); chart.VisibleSamples=_simPos;
                lblLive.Text="نمایشی / ساختگی — "+Fmt.N(_simPos*_active.Dt,1)+" ثانیه";
                if(_simPos==_active.Flow.Count) { _active.Completed=true; FinishTrial(); } return;
            }
            DrainPackets(); chart.Invalidate();
            if(_active.Flow.Count>0)
            {
                Expiration s=Expiration.From(_active);
                lblLive.Text="در حال ثبت | جریان "+Fmt.N(_active.Flow[_active.Flow.Count-1],2)+" L/s | حجم بازدم "+Fmt.N(s.Count>0?s.Volume[s.Count-1]:0,2)+" L";
            }
            else lblLive.Text="منتظر فوت؛ دم کامل و سپس بازدم سریع و پیوسته انجام دهید.";
        }
        private void StopTest()
        {
            if(!_busy || _active==null) return;
            if(_simulating)
            {
                if(_simPos<_active.Flow.Count) _active.Flow.RemoveRange(_simPos,_active.Flow.Count-_simPos);
                _active.EndReason="توقف نمایش توسط کاربر"; FinishTrial(); return;
            }
            _stop=true; btnStop.Enabled=false; lblLive.Text="در حال توقف و ذخیرهٔ داده‌های ناقص…";
        }
        private void FinishTrial()
        {
            _timer.Stop(); _busy=false; _simulating=false;
            Trial t=_active; _active=null; if(t==null) { UpdateControls(); return; }
            t.Result=SpiroAnalysis.Analyze(t,_exam.Patient); _exam.AddManeuver(t); _dirty=true; _selected=t;
            RenderTrials(); RestorePrimaryPreview(); SaveExam(false); UpdateControls();
        }
        void RenderTrials()
        {
            _rendering=true; gridTrials.Rows.Clear();
            for(int i=0;i<_exam.Trials.Count;i++)
            {
                Trial t=_exam.Trials[i]; AnalysisResult r=t.Result; string state=ManeuverAssessment.Assess(t).Status;
                gridTrials.Rows.Add(t.AcquisitionSequence+(state.Length>0?" · "+state:""),Metric(r,0),Metric(r,1),Metric(r,2),Metric(r,3),Metric(r,7),t.ManeuverId==_exam.OperatorSelectedManeuverId,ReportIds().Contains(t.ManeuverId));
                string info=ExamStore.Source(t)+" | "+t.When.ToString("o")+(t.EndReason.Length>0?" | "+t.EndReason:"");
                if(r!=null) info+=" | "+r.InvalidReason;
                info+=" | "+ManeuverAssessment.Assess(t).Status+" | "+ManeuverAssessment.Assess(t).Problem;
                foreach(DataGridViewCell cell in gridTrials.Rows[i].Cells) cell.ToolTipText=info;
                if(!ExamSummary.CanReport(t)) gridTrials.Rows[i].DefaultCellStyle.ForeColor=Color.FromArgb(144,113,113);
            }
            _rendering=false; UpdateSummary(); UpdateComparisons(); UpdateControls();
        }
        string Metric(AnalysisResult r,int index) { return r!=null && r.Valid?Fmt.N(SpiroReport.Value(r,index),index==2?1:2):"—"; }
        List<string> ReportIds() { return _exam.SelectionWorkflowVersion>=2?_exam.ReportManeuverIds:_legacyReportIds; }
        void ChangeChoice(int row,int column)
        {
            if(_rendering || _busy || _exam.FinalizedAt.HasValue || row<0 || row>=_exam.Trials.Count || (column!=6 && column!=7)) return;
            Trial t=_exam.Trials[row];
            try
            {
                if(column==6) _exam.SelectSiteManeuver(t.ManeuverId);
                else _exam.SetReportManeuver(t.ManeuverId,!_exam.ReportManeuverIds.Contains(t.ManeuverId));
                _dirty=true; UpdateChoiceCells(); SaveExam(false);
            }
            catch(Exception ex) { Error(ex.Message); }
        }
        void UpdateChoiceCells()
        {
            _rendering=true;
            for(int i=0;i<_exam.Trials.Count;i++) { gridTrials.Rows[i].Cells[6].Value=_exam.Trials[i].ManeuverId==_exam.OperatorSelectedManeuverId; gridTrials.Rows[i].Cells[7].Value=ReportIds().Contains(_exam.Trials[i].ManeuverId); }
            _rendering=false; UpdateSummary(); UpdateComparisons(); UpdateControls();
        }
        void UpdateComparisons() { chart.ComparisonTrials=_exam.Trials.FindAll(delegate(Trial t) { return ReportIds().Contains(t.ManeuverId); }); }
        void SelectTrial(int index)
        {
            if(index<0 || index>=_exam.Trials.Count) return;
            _selected=_exam.Trials[index]; chart.Trial=_selected; chart.Result=_selected.Result;
            _rendering=true; if(gridTrials.CurrentRow==null || gridTrials.CurrentRow.Index!=index) gridTrials.CurrentCell=gridTrials.Rows[index].Cells[0]; _rendering=false;
            lblLive.Text=(_selected.Simulated?"دادهٔ آزمایشی | ":"")+"نمایش مانور "+_selected.AcquisitionSequence+(!_selected.Completed?" · ناقص":_selected.OperatorRejected?" · ردشده":_selected.Result==null || !_selected.Result.Valid?" · داده ناکافی":"");
            UpdateControls();
        }
        void SelectBest() { Trial t=ExamSummary.Build(_exam.Trials).BestTrial; if(t!=null) SelectTrial(_exam.Trials.IndexOf(t)); }
        void RestorePrimaryPreview()
        {
            if(_exam.OperatorSelectedManeuver!=null) SelectTrial(_exam.Trials.IndexOf(_exam.OperatorSelectedManeuver));
            else if(ExamSummary.Build(_exam.Trials).BestTrial!=null) SelectBest();
            else if(_exam.Trials.Count>0) SelectTrial(_exam.Trials.Count-1);
        }
        void UpdateSummary()
        {
            string site=_exam.OperatorSelectedManeuver==null?"—":_exam.OperatorSelectedManeuver.AcquisitionSequence.ToString();
            List<string> numbers=new List<string>(); foreach(Trial t in _exam.Trials) if(ReportIds().Contains(t.ManeuverId)) numbers.Add(t.AcquisitionSequence.ToString());
            lblSummary.Text="مانورها: "+_exam.Trials.Count+"     |     سامانه: "+site+"     |     گزارش: "+(numbers.Count==0?"—":string.Join("، ",numbers.ToArray()));
        }
        private bool SaveExam(bool explicitSave)
        {
            if(!_hasPatient) return true; if(!SyncMetadata()) return false;
            try { if(_path==null) _path=ExamStore.NewPath(); ExamStore.Save(_path,_exam); _dirty=false; lblStatus.Text="ذخیره شد"; return true; }
            catch(Exception ex) { Error("ذخیره انجام نشد؛ داده‌ها در برنامه باقی‌اند. "+ex.Message); return false; }
        }
        bool CanLeave()
        {
            if(!_dirty) return true;
            DialogResult decision=MessageBox.Show(this,"تغییرات معاینهٔ فعلی ذخیره شود؟ با انتخاب خیر، تغییرات ذخیره‌نشده کنار گذاشته می‌شوند.","معاینهٔ فعلی",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
            return decision==DialogResult.No || (decision==DialogResult.Yes && SaveExam(false));
        }
        private void NewExam()
        {
            if(_busy) return;
            using(PatientForm form=new PatientForm(null,false,_hline)) if(form.ShowDialog(this)==DialogResult.OK && CanLeave())
                try { BeginExam(form.Value,true); } catch(Exception ex) { Error("ثبت معاینه انجام نشد؛ پروندهٔ قبلی حفظ شد. "+ex.Message); }
        }
        private void BeginExam(Patient patient,bool persist)
        {
            string problem; if(!PatientForm.ValidatePatient(patient,out problem)) throw new ArgumentException(problem);
            Exam fresh=Exam.CreateNew(_settings); fresh.Patient=patient.Clone();fresh.Patient.SetAgeAt(fresh.ExaminationDate.Value);
            string path=persist?ExamStore.NewPath():_path;
            if(persist) ExamStore.Save(path,fresh);
            _exam=fresh; _path=path; _hasPatient=true; _selected=null; _dirty=false; _developerMode=false; _receivedLiveSamples=false; _safeError="none"; _legacyReportIds.Clear();
            chart.Trial=null; chart.Result=null; PopulateMetadata(); RenderTrials(); lblLive.Text="آمادهٔ تست"; lblStatus.Text="مشخصات ذخیره شد";
        }
        private void OpenExam()
        {
            if(_busy) return;
            using(OpenFileDialog dialog=new OpenFileDialog { Filter="پرونده SpiroLab (*.spiro)|*.spiro",InitialDirectory=ExamStore.DataDirectory })
                if(dialog.ShowDialog(this)==DialogResult.OK && CanLeave()) try { LoadExam(dialog.FileName); } catch(Exception ex) { Error("پرونده باز نشد: "+ex.Message); }
        }
        private void LoadExam(string path)
        {
            Exam loaded=ExamStore.Load(path); _exam=loaded; _path=path; _hasPatient=true; _dirty=false; _selected=null; _developerMode=false; _legacyReportIds.Clear();
            if(loaded.SelectionWorkflowVersion<2)
            {
                if(ExamSummary.Build(loaded.Trials).Best!=null) foreach(Trial t in ExaminationSnapshot.Capture(loaded).Summary.Trials) _legacyReportIds.Add(t.ManeuverId);
                if(!loaded.FinalizedAt.HasValue) { loaded.SelectionWorkflowVersion=2; loaded.ReportManeuverIds=new List<string>(_legacyReportIds); loaded.MaintainSelections(); _dirty=true; }
            }
            PopulateMetadata(); RenderTrials(); RestorePrimaryPreview(); lblStatus.Text="پرونده باز شد";
        }
        private void PopulateMetadata()
        {
            _rendering=true; txtExamDate.Text=_exam.ExaminationDate.HasValue?ExaminationDates.DisplayJalali(_exam.ExaminationDate.Value):"";
            if(_exam.Trials.Count>0) _hasPatient=true;
            lblPatient.Text=_hasPatient?"مراجع: "+_exam.Patient.FullName+"    |    سن: "+_exam.Patient.Age+" سال":"مراجعی انتخاب نشده است";
            _rendering=false;
        }
        private bool SyncMetadata()
        {
            if(_exam.FinalizedAt.HasValue || !_hasPatient) return true;
            DateTime date;
            if(!ExaminationDates.TryParse(txtExamDate.Text,out date)) { Error("تاریخ شمسی معتبر وارد کنید؛ مثال: ۱۴۰۵/۰۸/۱۰"); return false; }
            if(!_exam.ExaminationDate.HasValue || _exam.ExaminationDate.Value.Date!=date)
            {
                Patient patient=_exam.Patient.Clone();try {patient.SetAgeAt(date);string problem;if(!patient.IsUsable(out problem)) {Error(problem);return false;}}catch(Exception ex) {Error(ex.Message);return false;}
                _exam.Patient=patient;_exam.ExaminationDate=date; _exam.ExaminationDateSource="operator_selected_date";
                foreach(Trial t in _exam.Trials) t.Result=SpiroAnalysis.Analyze(t,patient);
                _exam.MaintainSelections();_dirty=true;
            }
            return true;
        }
        void ShowDetails()
        {
            if(!_hasPatient || _busy) return;
            using(ExaminationDetailsForm form=new ExaminationDetailsForm(_exam,_selected))
                if(form.ShowDialog(this)==DialogResult.OK) { _dirty=true; SaveExam(false); PopulateMetadata(); UpdateControls(); }
        }
        DeviceDiagnostics ReadDiagnostics() { return DeviceDiagnostics.Read(_device.ControlResponded,_receivedLiveSamples,_device.SafeTransportError!="none"?_device.SafeTransportError:_safeError,_device.HandleOpened,_device.StartCommandSucceeded,_device.LivePacketCount,_decodedLiveSamples); }
        void OpenSettings()
        {
            if(_busy) return;
            using(SettingsForm form=new SettingsForm(_settings,ReadDiagnostics,delegate(Action done) { ConnectDevice(false,done); }))
            {
                DialogResult result=form.ShowDialog(this);
                if(result==DialogResult.OK) _settings=form.Value;
                if(form.RunDevelopmentDemo && _hasPatient && !_exam.FinalizedAt.HasValue) StartSimulation();
            }
        }
        private void ShowReport()
        {
            if(!CanReport() || !SaveExam(false)) return;
            try
            {
                ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(_exam); UpdateControls();
                using(ReportForm report=new ReportForm(snapshot)) { report.Exported+=RecordExport; report.ShowDialog(this); }
            }
            catch(Exception ex) { Error("گزارش آماده نشد: "+ex.Message); }
        }
        void LoginSite()
        { using(HLineLoginForm form=new HLineLoginForm(_hline)) form.ShowDialog(this); UpdateControls(); }
        async Task SendSite()
        {
            if(_busy) return;
            if(!_hasPatient || _exam.Trials.Count==0) {Error("ابتدا معاینه و مانور را ثبت کنید.");return;}
            try {_exam.ValidateSelection(true);_exam.ValidateReportSelection(true);} catch(Exception ex) {Error(ex.Message);return;}
            if(!SaveExam(false)) return;
            _busy=true;UpdateControls();
            try
            {
                ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(_exam);
                ResultPackage package=ResultExport.SaveToDirectory(Path.Combine(Path.GetDirectoryName(_path),"exports"),snapshot);RecordExport(package);
                SubmissionDraft draft=new SubmissionDraft(snapshot,package);
                draft.ValidateForSubmission();
                await Task.Factory.StartNew(delegate
                {
                    _hline.VerifyLatestRecord(_exam.Patient);
                    MedicalRecord record=new MedicalRecord {Id=draft.RecordId};
                    if(_hline.HasSpirometry(record)) throw new HLineException("existing_result","این پرونده نتیجهٔ اسپیرومتری دارد؛ ارسال دوباره انجام نشد.");
                    new SubmissionCoordinator(_submissionGateway,_exam.Submission,delegate {ExamStore.Save(_path,_exam);},_testRecord).Submit(draft);
                });
                lblStatus.Text="نتیجه و تصویر در سایت ثبت و بررسی شد.";
            }
            catch(HLineException ex) {Error(ex.Message);}
            catch(InvalidOperationException ex) {Error(ex.Message);}
            catch {Error(_exam.Submission.WriteAttempted?"نتیجهٔ ارسال نامطمئن است؛ وضعیت سایت باید بازخوانی شود.":"ارسال انجام نشد؛ پروندهٔ محلی محفوظ است.");}
            finally {_busy=false;UpdateControls();}
        }
        void RecordExport(ResultPackage package)
        {
            _exam.ExportReferences.Add(new ExportReference { SnapshotId=package.SourceSnapshotId,ReportPath=package.ReportPath,DataPath=package.DataPath,CreatedAt=DateTime.Now });
            try { ExamStore.Save(_path,_exam); } catch(Exception ex) { _dirty=true; Error("فایل‌ها صادر شدند؛ ثبت مسیر خروجی در سابقه انجام نشد: "+ex.Message); }
        }
        void ClosingForm(object sender,FormClosingEventArgs args)
        {
            if(_busy) { args.Cancel=true; if(_active!=null) StopTest(); Error("ابتدا پایان ثبت یا توقف را منتظر بمانید."); return; }
            if(_dirty && !SaveExam(false)) { args.Cancel=true; return; } _timer.Dispose(); _device.Dispose(); _hline.Dispose(); _tips.Dispose();
        }
    }
}

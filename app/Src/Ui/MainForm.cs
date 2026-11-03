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
        readonly string _testNationalId,_testRecordId;
        readonly bool _allowProductionWrites;
        AppSettings _settings;
        Exam _exam;
        string _path;
        bool _busy,_dirty,_rendering,_hasPatient,_sizingLayout;
#if SPIROLAB_TEST
        bool _fast,_simulating,_developerMode;
#endif
        volatile bool _stop, _finishRequested;
        Trial _active,_selected;
        PacketDecoder _decoder;
        readonly Queue<DevicePacket> _pending=new Queue<DevicePacket>();
        readonly object _queueLock=new object();
        ToolTip _tips=new ToolTip();
        readonly object _uiSync=new object();
        volatile bool _closing;
        System.Windows.Forms.Timer _timer;
        int _decodedLiveSamples;
#if SPIROLAB_TEST
        int _simPos;
#endif
        bool _receivedLiveSamples;
        string _safeError="none";
        ChartPanel chart;
        DataGridView gridTrials;
        TextBox txtExamDate;
        Label lblConnection,lblSiteConnection,lblStatus,lblLive,lblSummary;
        LinkLabel lblPatient;
        Button btnStart,btnFinish,btnStop,btnReport,btnSettings,btnNew,btnOpen,btnLogin,btnCalendar;
        List<string> _legacyReportIds=new List<string>();
        public MainForm():this(new HLineClient(),null,null,true) { }
        public MainForm(HLineClient hline,string testNationalId,string testRecordId,bool allowProductionWrites):this(hline,null,null)
        {
            if(allowProductionWrites && (!string.IsNullOrEmpty(testNationalId) || !string.IsNullOrEmpty(testRecordId))) throw new ArgumentException("حالت تست و ارسال تولیدی را نمی‌توان هم‌زمان فعال کرد.");
            _testNationalId=testNationalId==null?"":testNationalId.Trim();
            _testRecordId=testRecordId==null?"":testRecordId.Trim();
            _allowProductionWrites=allowProductionWrites;
        }
        public MainForm(HLineClient hline,ISubmissionGateway submissionGateway,MedicalRecord designatedTestRecord)
        {
            _hline=hline;_submissionGateway=submissionGateway??(hline.CanWrite?new LiveSubmissionGateway(hline):null);_testRecord=designatedTestRecord;
            _allowProductionWrites=designatedTestRecord==null;
            _settings=AppSettings.Load(); _exam=Exam.CreateNew(_settings); _hline.PreferredFirefoxProfile=_settings.FirefoxProfile;
            ClinicalStyle.Form(this,"اسپیرومتری طب کار — SpiroLab "+SpiroReport.ProductVersion,new Size(1280,820));
            StartPosition=FormStartPosition.CenterScreen; MinimumSize=new Size(780,500);
            BuildUi(); PopulateMetadata(); _device.Line+=AppendLog;
            _timer=new System.Windows.Forms.Timer { Interval=50 }; _timer.Tick+=Tick;
            FormClosing+=ClosingForm; UpdateControls();
            if(!string.IsNullOrEmpty(_settings.LoadWarning)) lblStatus.Text=_settings.LoadWarning;
        }
        public void SelectTab(int index) { }
        // Interactive start only (Program calls this; tests never do): reuse the
        // operator's already authenticated Firefox session on launch.
        public void ProbeFirefoxSession()
        {
            if(_firefoxProbeStarted) return;
            _firefoxProbeStarted=true;
            Shown+=delegate
            {
                string preferred=_settings==null?null:_settings.FirefoxProfile;
                Task.Factory.StartNew(delegate { string problem; _hline.TryAdoptFirefoxSession(preferred,out problem); })
                    .ContinueWith(delegate(Task task)
                    {
                        if(IsDisposed || !IsHandleCreated) return;
                        try { BeginInvoke((Action)delegate { UpdateControls(); }); }
                        catch(ObjectDisposedException) { }
                        catch(InvalidOperationException) { }
                    });
            };
        }
        bool _firefoxProbeStarted;
        public void ConnectOnLaunch() { ConnectDevice(false); }
#if SPIROLAB_TEST
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
#endif
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
            btnLogin=MakeButton("ورود به سامانه",delegate { SignInSite(); },false); btnLogin.Name="LoginToSite";
            buttons.Controls.AddRange(new Control[] {btnNew,btnOpen,btnSettings,btnLogin}); toolbar.Controls.Add(buttons,0,0);
            lblConnection=ClinicalStyle.Label("● متصل نیست"); lblConnection.ForeColor=Color.FromArgb(113,126,138); lblConnection.Cursor=Cursors.Hand;
            lblConnection.Click+=delegate { if(!_busy) OpenSettings(); }; _tips.SetToolTip(lblConnection,"اتصال و جزئیات دستگاه در تنظیمات");
            TableLayoutPanel connections=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2}; connections.RowStyles.Add(new RowStyle(SizeType.Percent,50));connections.RowStyles.Add(new RowStyle(SizeType.Percent,50));
            lblSiteConnection=ClinicalStyle.Label("سایت: متصل نیست");lblSiteConnection.Cursor=Cursors.Hand;lblSiteConnection.Click+=delegate {if(!_busy) SignInSite();};
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
            string[] captions={"مانور","حجم\nFVC (L)","حجم ثانیه اول\nFEV1 (L)","نسبت\nFEV1/FVC (%)","جریان بیشینه\nPEF (L/s)","جریان میانی\nFEF25–75 (L/s)","زمان بازدم\nFET (s)"};
            float[] weights={9,13,14,14,13,15,10,11};
            for(int i=0;i<captions.Length;i++)
            {
                if(i==0) gridTrials.Columns.Add(new ManeuverColumn { Name="Metric0",HeaderText=captions[0],ReadOnly=true,FillWeight=weights[0],MinimumWidth=44,DeleteRow=delegate(int row) {DeleteManeuver(row);} });
                else gridTrials.Columns.Add(new DataGridViewTextBoxColumn { Name="Metric"+i,HeaderText=captions[i],ReadOnly=true,FillWeight=weights[i],MinimumWidth=25,SortMode=DataGridViewColumnSortMode.NotSortable });
            }
            ReportColumn reportColumn=new ReportColumn { Name="ReportSelection",HeaderText="انتخاب نهایی",ReadOnly=true,FillWeight=weights[7],MinimumWidth=40,SortMode=DataGridViewColumnSortMode.NotSortable,ToggleRow=delegate(int row) {ChangeChoice(row,ReportColumnIndex);} };
            reportColumn.ToolTipText="حداکثر سه مانور؛ بهترین مقادیر گزارش و سایت از همین مانورها انتخاب می‌شوند.";
            gridTrials.Columns.Add(reportColumn);
            gridTrials.RowTemplate.Height=31;
            gridTrials.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(236,242,246); gridTrials.ColumnHeadersDefaultCellStyle.ForeColor=ClinicalStyle.Ink; gridTrials.ColumnHeadersDefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; gridTrials.ColumnHeadersDefaultCellStyle.WrapMode=DataGridViewTriState.True;
            gridTrials.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleCenter; gridTrials.DefaultCellStyle.SelectionBackColor=Color.FromArgb(223,239,242); gridTrials.DefaultCellStyle.SelectionForeColor=ClinicalStyle.Ink; gridTrials.GridColor=Color.FromArgb(233,238,242); gridTrials.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;
            gridTrials.SelectionChanged+=delegate { if(!_rendering && gridTrials.CurrentRow!=null) SelectTrial(gridTrials.CurrentRow.Index); };
            gridTrials.CellClick+=delegate(object sender,DataGridViewCellEventArgs e) { ChangeChoice(e.RowIndex,e.ColumnIndex); };
            gridTrials.CellMouseClick+=delegate(object sender,DataGridViewCellMouseEventArgs e)
            {
                if(e.RowIndex<0 || e.ColumnIndex!=0) return;
                ManeuverColumn column=gridTrials.Columns[0] as ManeuverColumn;
                if(column!=null && column.HitDelete(gridTrials.Columns[0].Width,e.Location.X)) DeleteManeuver(e.RowIndex);
            };
            gridTrials.KeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.KeyCode==Keys.Space && gridTrials.CurrentCell!=null && gridTrials.CurrentCell.OwningColumn.Name=="ReportSelection") { ChangeChoice(gridTrials.CurrentCell.RowIndex,ReportColumnIndex); e.Handled=e.SuppressKeyPress=true; }
                else if(e.KeyCode==Keys.Delete && gridTrials.CurrentRow!=null) { DeleteManeuver(gridTrials.CurrentRow.Index); e.Handled=e.SuppressKeyPress=true; }
                else if(e.Control && e.KeyCode==Keys.Z) { UndoLastDiscard(); e.Handled=e.SuppressKeyPress=true; }
            };
            ContextMenuStrip historyMenu=new ContextMenuStrip();
            historyMenu.Items.Add("نمایش حذف‌شده‌ها",null,delegate { ShowDiscarded(); });
            historyMenu.Items.Add("بازگرداندن آخرین حذف",null,delegate { UndoLastDiscard(); });
            historyMenu.Items.Add("بازگشت به انتخاب خودکار",null,delegate { RestoreAutomaticSelection(); });
            gridTrials.ContextMenuStrip=historyMenu;
            history.Controls.Add(gridTrials); history.Controls.Add(lblSummary); root.Controls.Add(history,0,3);
            FlowLayoutPanel actions=new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,RightToLeft=RightToLeft.No,WrapContents=false,Padding=new Padding(0,6,0,0) };
#if SPIROLAB_TEST
            btnStart=MakeButton("شروع تست",delegate { if(_developerMode) StartSimulation(); else StartTest(); },true);
#else
            btnStart=MakeButton("شروع تست",delegate { StartTest(); },true);
#endif
            btnFinish=MakeButton("پایان بازدم",delegate { RequestFinish(); },false);
            btnStop=MakeButton("لغو",delegate { StopTest(); },false); btnReport=MakeButton("ارسال به سایت",async delegate { await SendSite(); },false);
            ContextMenuStrip localMenu=new ContextMenuStrip();localMenu.Items.Add("ذخیره JPG + JSON",null,delegate {ShowReport();});btnReport.ContextMenuStrip=localMenu; _tips.SetToolTip(btnReport,"بازبینی ارسال؛ کلیک راست برای خروجی محلی JPG + JSON");
            _tips.SetToolTip(btnStop,"دریافت مانور را با فرمان ESC لغو می‌کند؛ پایان معتبر و ثبت نتیجه نیست. دادهٔ خام در سابقهٔ تشخیصی نگه داشته می‌شود.");
            _tips.SetToolTip(btnFinish,"فرمان ESC یک بار ارسال می‌شود؛ تا ۱۰ ثانیه منتظر پیام پایان دستگاه می‌ماند. لغو فعال است.");
            actions.Controls.AddRange(new Control[] {btnStart,btnFinish,btnStop,btnReport}); root.Controls.Add(actions,0,4);
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
        void Error(string message)
        {
            lock(_uiSync)
            {
                if(_closing || IsDisposed || Disposing || _tips==null) return;
                lblStatus.Text=message; _tips.SetToolTip(lblStatus,message);
            }
        }
        Patient ReadPatient() { if(!_hasPatient) { Error("ابتدا مراجع جدید را ثبت کنید."); return null; } return _exam.Patient.Clone(); }
        void UpdateControls()
        {
            lock(_uiSync)
            {
            if(_closing || IsDisposed || Disposing || _tips==null) return;
            bool editable=!_busy && !_exam.FinalizedAt.HasValue;
            btnNew.Enabled=btnOpen.Enabled=btnSettings.Enabled=btnLogin.Enabled=!_busy;
#if SPIROLAB_TEST
            btnStart.Enabled=editable && _hasPatient && (_developerMode || _device.Connected);
            btnStart.Text=_developerMode?"مانور نمایشی":_exam.Trials.Count>0?"تست بعدی":"شروع تست";
#else
            btnStart.Enabled=editable && _hasPatient && _device.Connected;
            btnStart.Text=_exam.Trials.Count>0?"تست بعدی":"شروع تست";
#endif
            btnStop.Enabled=_busy && _active!=null && !_stop; btnFinish.Enabled=btnStop.Enabled && !_finishRequested; gridTrials.Enabled=true;
            txtExamDate.Enabled=btnCalendar.Enabled=editable && _hasPatient;
            btnReport.Enabled=!_busy && _hasPatient && _exam.Trials.Count>0;
#if SPIROLAB_TEST
            _tips.SetToolTip(btnStart,!_hasPatient?"ابتدا مراجع جدید را ثبت کنید.":!_device.Connected && !_developerMode?"اتصال دستگاه را در تنظیمات بررسی کنید.":"ثبت یک مانور تازه");
#else
            _tips.SetToolTip(btnStart,!_hasPatient?"ابتدا مراجع جدید را ثبت کنید.":!_device.Connected?"اتصال دستگاه را در تنظیمات بررسی کنید.":"ثبت یک مانور تازه");
#endif
            _tips.SetToolTip(btnReport,btnReport.Enabled?"بازبینی ارسال به سایت؛ کلیک راست برای JPG + JSON محلی":"ابتدا یک مانور ثبت کنید.");
            lblSiteConnection.Text=_hline.StatusText;
            _tips.SetToolTip(lblSiteConnection,_hline.Status==SessionStatus.Connected && !string.IsNullOrEmpty(_hline.FirefoxProfileName)
                ?"نشست فایرفاکس ("+_hline.FirefoxProfileName+") به‌کار رفته است."
                :"برای اتصال، «ورود به سامانه» را بزنید؛ اگر در فایرفاکس وارد شده باشید، همان نشست به‌کار می‌رود.");
            lblConnection.Text=_busy && _active!=null?"● در حال دریافت":_device.Connected?"● متصل":"● متصل نیست";
            lblConnection.ForeColor=_device.Connected?ClinicalStyle.Accent:Color.FromArgb(113,126,138);
            lblPatient.Enabled=_hasPatient && !_busy;
            foreach(DataGridViewRow row in gridTrials.Rows) { row.Cells[0].Tag=editable; row.Cells[ReportColumnIndex].Tag=editable; row.Cells[ReportColumnIndex].Style.ForeColor=editable?ClinicalStyle.Ink:Color.LightGray; }
            }
        }
        int ReportColumnIndex { get { return gridTrials.Columns["ReportSelection"].Index; } }
        bool CanReport()
        {
            if(CurrentSummary().Best==null) return false;
            try { _exam.ValidateSelection(_exam.SelectionWorkflowVersion<3); _exam.ValidateReportSelection(true); return true; } catch { return false; }
        }
        ExamSummary CurrentSummary()
        {
            return _exam.SelectionWorkflowVersion>=4?ExamSummary.BuildSelected(_exam.Trials,ReportIds()):ExamSummary.Build(_exam.Trials);
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
            _exam.Patient=p; _stop=false; _finishRequested=false; _decoder=new PacketDecoder(); _active=new Trial { Completed=false }; _selected=null; _busy=true;
            lock(_queueLock) _pending.Clear(); chart.Trial=_active; chart.Result=null; lblLive.Text="آماده‌سازی دستگاه…";
             UpdateControls(); _timer.Start(); Trial capturing=_active;
            Thread worker=new Thread(delegate()
            {
                CaptureOutcome outcome=_device.Capture(capturing,delegate { return _finishRequested; },delegate { return _stop; },delegate(DevicePacket packet) { lock(_queueLock) _pending.Enqueue(packet); });
                Post(delegate { DrainPackets(); _decodedLiveSamples=capturing.Flow.Count; _receivedLiveSamples=_decodedLiveSamples>0; _safeError=_receivedLiveSamples?"none":"no_valid_live_samples"; capturing.Completed=outcome.Completed && capturing.CaptureError.Length==0 && !_stop; capturing.OperatorStopped=!capturing.Completed && (outcome.Cancelled || _stop); if(!outcome.Completed && !capturing.OperatorStopped) capturing.CaptureError=outcome.Reason??"capture_failed"; capturing.EndReason=_stop?"توقف توسط کاربر":outcome.Reason??capturing.EndReason; capturing.DeviceValues=_stop?null:outcome.Results; FinishTrial(); });
            }) { IsBackground=true }; worker.Start();
        }
#if SPIROLAB_TEST
        private void StartSimulation()
        {
            if(_busy || !CheckSource(true,false)) return; Patient p=ReadPatient(); if(p==null) return;
            if(!SyncMetadata()) return;
            _exam.Patient=p; _stop=false; _finishRequested=false; _active=Simulator.Build(p,Environment.TickCount); _active.Completed=false;
            _developerMode=true; _simPos=0; _simulating=true; _busy=true; _selected=null; chart.Trial=_active; chart.Result=null; chart.VisibleSamples=0;
            lblLive.Text="نمایشی — دادهٔ ساختگی";  UpdateControls(); _timer.Start();
        }
#endif
        private void DrainPackets()
        {
            if(_active==null) return;
            try { lock(_queueLock) while(_pending.Count>0) _decoder.Add(_active,_pending.Dequeue()); }
            catch(Exception ex) { _stop=true; _active.CaptureError=_active.EndReason=ex.Message; AppendLog(ex.Message); }
        }
        private void Tick(object sender,EventArgs args)
        {
            if(_active==null) return;
#if SPIROLAB_TEST
            if(_simulating)
            {
                _simPos=Math.Min(_active.Flow.Count,_simPos+(_fast?80:5)); chart.VisibleSamples=_simPos;
                lblLive.Text="نمایشی / ساختگی — "+Fmt.N(_simPos*_active.Dt,1)+" ثانیه";
                if(_simPos==_active.Flow.Count) { _active.Completed=true; FinishTrial(); } return;
            }
#endif
            DrainPackets(); chart.Invalidate();
            if(_finishRequested && !_stop) { lblLive.Text="در انتظار اعلام پایان مانور از دستگاه…"; return; }
            if(_stop) return;
            if(_active.Flow.Count>0)
            {
                Expiration s=Expiration.From(_active);
                lblLive.Text="در حال ثبت | جریان "+Fmt.N(_active.Flow[_active.Flow.Count-1],2)+" L/s | حجم بازدم "+Fmt.N(s.Count>0?s.Volume[s.Count-1]:0,2)+" L";
            }
            else lblLive.Text="منتظر فوت؛ دم کامل و سپس بازدم سریع و پیوسته انجام دهید.";
        }
        private void RequestFinish()
        {
            if(!_busy || _active==null || _stop) return;
            _finishRequested=true; btnFinish.Enabled=false;
            lblLive.Text="در انتظار اعلام پایان مانور از دستگاه…";
        }
        private void StopTest()
        {
            if(!_busy || _active==null) return;
#if SPIROLAB_TEST
            if(_simulating)
            {
                if(_simPos<_active.Flow.Count) _active.Flow.RemoveRange(_simPos,_active.Flow.Count-_simPos);
                _active.EndReason="توقف نمایش توسط کاربر"; FinishTrial(); return;
            }
#endif
            _stop=true; btnFinish.Enabled=btnStop.Enabled=false; lblLive.Text="در حال لغو و ذخیرهٔ داده‌های ناقص…";
        }
        private void FinishTrial()
        {
            _timer.Stop(); _busy=false;
#if SPIROLAB_TEST
            _simulating=false;
#endif
            Trial t=_active; _active=null; if(t==null) { UpdateControls(); return; }
            t.Result=SpiroAnalysis.Analyze(t,_exam.Patient);
            if(!t.Simulated && !t.Imported && !DeviceCoreAccepted(t))
            {
                // A failed device result never enters the active clinical table.
                // Keep the raw attempt only as separate local diagnostic evidence.
                _exam.RecordFailedCapture(t);
                _dirty=true;
                chart.Trial=null; chart.Result=null; chart.VisibleSamples=0;
                RenderTrials(); RestorePrimaryPreview(); SaveExam(false); UpdateControls();
                if(_finishRequested && !_stop)
                {
                    string reason=!string.IsNullOrEmpty(t.CaptureError)?t.CaptureError:t.EndReason;
                    if(string.IsNullOrEmpty(reason))
                    {
                        string code=t.DeviceValues==null?"cod_LAST_not_available":t.DeviceValues.InvalidReason;
                        if(code=="stale_duplicate_cod_LAST_block") reason="بلوک نتیجه با مانور قبلی یکسان است و به این مانور نسبت داده نشد.";
                        else if(code=="truncated_cod_LAST_block") reason="بلوک نتیجه دستگاه ناقص دریافت شد.";
                        else if(code=="cod_LAST_not_available") reason="پس از پیام پایان، بلوک نتیجه از دستگاه دریافت نشد.";
                        else if(code=="cod_LAST_required_core_field_missing_or_outside_supported_ranges") reason="حداقل یکی از چهار مقدار اصلی دستگاه موجود یا در دامنهٔ معتبر نیست.";
                        else if(code!=null && code.StartsWith("result_read_window_expired",StringComparison.Ordinal)) reason="بلوک نتیجه در مهلت معتبر خواندن دریافت نشد.";
                        else { reason=ManeuverAssessment.Assess(t).Problem; if(string.IsNullOrEmpty(reason)) reason="انتساب نتیجه یا سلامت دادهٔ این مانور تأیید نشد."; }
                    }
                    Error("پایان دستی ناموفق: "+reason+" این تلاش فقط برای عیب‌یابی نگه داشته شد و قابل ارسال نیست.");
                }
                else if(t.OperatorStopped)
                    Error("مانور لغو شد؛ نتیجه معتبر دستگاه ثبت نشد. دادهٔ خام فقط در سابقهٔ تشخیصی نگهداری شد و این تلاش قابل ارسال نیست.");
                else
                    Error("نتیجه مانور از دستگاه دریافت نشد؛ لطفاً مانور را تکرار کنید.");
                return;
            }
            _exam.AddManeuver(t); _dirty=true; _selected=t;
            RenderTrials(); RestorePrimaryPreview(); SaveExam(false); UpdateControls();
        }
        static bool DeviceCoreAccepted(Trial t)
        {
            if(t==null || t.Simulated || t.Imported || !t.Completed || !string.IsNullOrEmpty(t.CaptureError)) return false;
            if(t.DeviceCaptureSequence<=0 || t.DeviceValues==null) return false;
            DeviceResults d=t.DeviceValues;
            if(!d.Present || !d.BlockValid || !d.Associated || d.Raw==null) return false;
            if(d.CaptureSequence!=t.DeviceCaptureSequence) return false;
            if(!d.FvcValid || !d.Fev1Valid || !d.PefValid || !d.FetValid) return false;
            // Same rule as DeviceProvenanceValidator: a serial must match on both
            // sides; the documented both-empty (SDA did not report one) case is the
            // only accepted absence.
            if(d.SerialNumber!=t.DeviceSerial && !(d.SerialNumber.Length==0 && t.DeviceSerial.Length==0)) return false;
            ManeuverAssessment a=ManeuverAssessment.Assess(t);
            return a.TechnicalValid && t.Flow.Count>=5;
        }
        void RenderTrials()
        {
            _rendering=true; gridTrials.Rows.Clear();
            for(int i=0;i<_exam.Trials.Count;i++)
            {
                Trial t=_exam.Trials[i]; AnalysisResult r=t.Result; ManeuverAssessment assessment=ManeuverAssessment.Assess(t); string state=assessment.Status;
                gridTrials.Rows.Add(t.AcquisitionSequence.ToString(),MetricText(t,0),MetricText(t,1),MetricText(t,2),MetricText(t,3),MetricText(t,7),MetricText(t,9),ReportIds().Contains(t.ManeuverId));
                string info=ExamStore.Source(t)+" | "+t.When.ToString("o")+(t.EndReason.Length>0?" | "+t.EndReason:"");
                if(r!=null) info+=" | "+r.InvalidReason;
                info+=" | "+assessment.Status+" | "+assessment.Problem;
                MeasuredValue fvc=t.Measurements.Fvc,fev1=t.Measurements.Fev1,pef=t.Measurements.Pef;
                info+=" | FVC: "+SourceFa(fvc)+" | FEV1: "+SourceFa(fev1)+" | PEF: "+SourceFa(pef);
                foreach(DataGridViewCell cell in gridTrials.Rows[i].Cells) cell.ToolTipText=info;
                gridTrials.Rows[i].Cells[0].ToolTipText="حذف مانور "+t.AcquisitionSequence+" | "+info;
                if(!ExamSummary.CanReport(t)) gridTrials.Rows[i].DefaultCellStyle.ForeColor=Color.FromArgb(144,113,113);
            }
            _rendering=false; UpdateSummary(); UpdateComparisons(); UpdateControls();
        }
        string SourceFa(MeasuredValue value) { return value.Available?(value.DeviceReported?"دستگاه":"محاسبه‌شده"):"ناموجود"; }
        string MetricText(Trial t,int index)
        {
            MeasuredValue value=t.Measurements.Get(MetricNameForIndex(index));
            return value.Available?Fmt.N(value.Value.Value,index==2?1:2):"—";
        }
        internal static string MetricNameForIndex(int index)
        {
            switch(index)
            {
                case 0:return Metric.Fvc;case 1:return Metric.Fev1;case 2:return Metric.Fev1Fvc;case 3:return Metric.Pef;
                case 4:return Metric.Fef25;case 5:return Metric.Fef50;case 6:return Metric.Fef75;case 7:return Metric.Fef2575;case 9:return Metric.Fet;default:return Metric.Bev;
            }
        }
        List<string> ReportIds() { return _exam.SelectionWorkflowVersion>=2?_exam.ReportManeuverIds:_legacyReportIds; }
        void ChangeChoice(int row,int column)
        {
            if(_rendering || _busy || _exam.FinalizedAt.HasValue || row<0 || row>=_exam.Trials.Count || column!=ReportColumnIndex) return;
            Trial t=_exam.Trials[row];
            try
            {
                if(!_exam.ReportManeuverIds.Contains(t.ManeuverId) && !ExamSummary.CanReport(t))
                {
                    Error("این مانور برای انتخاب نهایی واجد شرایط نیست؛ وضعیت کیفیت و هشدارهای آن را ببینید.");
                    return;
                }
                _exam.SetReportManeuver(t.ManeuverId,!_exam.ReportManeuverIds.Contains(t.ManeuverId));
                _dirty=true; UpdateChoiceCells(); SaveExam(false);
            }
            catch(Exception ex) { Error(ex.Message); }
        }
        void UpdateChoiceCells()
        {
            _rendering=true;
            for(int i=0;i<_exam.Trials.Count;i++) gridTrials.Rows[i].Cells[ReportColumnIndex].Value=ReportIds().Contains(_exam.Trials[i].ManeuverId);
            _rendering=false; UpdateSummary(); UpdateComparisons(); UpdateControls();
        }
        void DeleteManeuver(int row)
        {
            if(_rendering || _busy || _exam.FinalizedAt.HasValue || row<0 || row>=_exam.Trials.Count) return;
            Trial t=_exam.Trials[row]; if(t==null) return;
            if(MessageBox.Show(this,"مانور شماره "+t.AcquisitionSequence+" از معاینهٔ فعال حذف شود؟ این کار با «بازگرداندن آخرین حذف» قابل بازگشت است.","حذف مانور",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
            try
            {
                _exam.DiscardManeuver(t.ManeuverId);
                _dirty=true; _selected=null; chart.Trial=null; chart.Result=null; chart.VisibleSamples=0;
                RenderTrials(); RestorePrimaryPreview(); SaveExam(false);
            }
            catch(Exception ex) { Error(ex.Message); }
        }
        void UndoLastDiscard()
        {
            if(_busy || _exam.FinalizedAt.HasValue) return;
            try
            {
                Trial t=_exam.RestoreLastDiscarded();
                if(t==null) { Error("مانور حذف‌شده‌ای برای بازگرداندن وجود ندارد."); return; }
                _dirty=true; _selected=t; RenderTrials(); SelectTrial(_exam.Trials.IndexOf(t)); SaveExam(false);
            }
            catch(Exception ex) { Error(ex.Message); }
        }
        void ShowDiscarded()
        {
            if(_exam.DiscardedTrials==null || _exam.DiscardedTrials.Count==0) { Error("مانور حذف‌شده‌ای در این معاینه وجود ندارد."); return; }
            List<string> rows=new List<string>();
            foreach(Trial t in _exam.DiscardedTrials) rows.Add("مانور "+t.AcquisitionSequence+(t.DiscardedAt.HasValue?" | "+t.DiscardedAt.Value.ToString("yyyy-MM-dd HH:mm"):"")+(t.SelectedWhenDiscarded?" | قبلاً در انتخاب نهایی":""));
            MessageBox.Show(this,string.Join(Environment.NewLine,rows.ToArray()),"مانورهای حذف‌شده (بازیابی از منوی راست‌کلیک)",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        void RestoreAutomaticSelection()
        {
            if(_busy || _exam.FinalizedAt.HasValue) return;
            try { _exam.ResetReportSelectionAutomatic(); _dirty=true; RenderTrials(); SaveExam(false); }
            catch(Exception ex) { Error(ex.Message); }
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
        void SelectBest() { Trial t=CurrentSummary().BestTrial; if(t!=null) SelectTrial(_exam.Trials.IndexOf(t)); }
        void RestorePrimaryPreview()
        {
            // Row preview never changes the aggregate. New workflow starts on the
            // quality-ranked representative maneuver, not an operator primary.
            if(CurrentSummary().BestTrial!=null) SelectBest();
            else if(_exam.Trials.Count>0) SelectTrial(_exam.Trials.Count-1);
            else { chart.Trial=null; chart.Result=null; chart.VisibleSamples=0; }
        }
        void UpdateSummary()
        {
            ExamSummary s=CurrentSummary();
            List<string> numbers=new List<string>(); foreach(Trial t in _exam.Trials) if(ReportIds().Contains(t.ManeuverId)) numbers.Add(t.AcquisitionSequence.ToString());
            string best=s.FvcAvailable?"FVC "+Fmt.N(s.Fvc,2)+" L | FEV1 "+Fmt.N(s.Fev1,2)+" L | FEV1/FVC "+Fmt.N(s.Ratio,1)+"% | PEF "+Fmt.N(s.Pef,2)+" L/s":"مقادیر BEST VALUES کامل نیست";
            string warning=SelectionWarning(s);
            lblSummary.Text="مانورها: "+_exam.Trials.Count+"     |     "+best+"     |     گزارش: "+(numbers.Count==0?"—":string.Join("، ",numbers.ToArray()))+(warning.Length>0?"     |     توجه: "+warning:"");
        }
        string SelectionWarning(ExamSummary selected)
        {
            if(_exam.SelectionWorkflowVersion<4 || selected==null || selected.BestValues==null) return "";
            ExamSummary all=ExamSummary.Build(_exam.Trials);
            if(all.BestValues==null) return "";
            bool fvc=all.BestValues.Fvc.Available && selected.BestValues.Fvc.Available && all.BestValues.Fvc.Value.Value>selected.BestValues.Fvc.Value.Value+1e-9;
            bool fev1=all.BestValues.Fev1.Available && selected.BestValues.Fev1.Available && all.BestValues.Fev1.Value.Value>selected.BestValues.Fev1.Value.Value+1e-9;
            if(!fvc && !fev1) return "";
            return "یک مانور فعال خارج از انتخاب نهایی مقدار "+(fvc?"FVC":"")+(fvc&&fev1?" و ":"")+(fev1?"FEV1":"")+" بالاتری دارد؛ BEST VALUES فقط از انتخاب نهایی است.";
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
            UpdateControls(); // reflect a session opened from inside the patient form
        }
        private void BeginExam(Patient patient,bool persist)
        {
            string problem; if(!PatientForm.ValidatePatient(patient,out problem)) throw new ArgumentException(problem);
            Exam fresh=Exam.CreateNew(_settings); fresh.Patient=patient.Clone();fresh.Patient.SetAgeAt(fresh.ExaminationDate.Value);
            string path=persist?ExamStore.NewPath():_path;
            if(persist) ExamStore.Save(path,fresh);
            _exam=fresh; _path=path; _hasPatient=true; _selected=null; _dirty=false; _receivedLiveSamples=false; _safeError="none"; _legacyReportIds.Clear();
#if SPIROLAB_TEST
            _developerMode=false;
#endif
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
            Exam loaded=ExamStore.Load(path); _exam=loaded; _path=path; _hasPatient=true; _dirty=false; _selected=null; _legacyReportIds.Clear();
#if SPIROLAB_TEST
            _developerMode=false;
#endif
            if(loaded.SelectionWorkflowVersion<2)
            {
                if(ExamSummary.Build(loaded.Trials).Best!=null) foreach(Trial t in ExaminationSnapshot.Capture(loaded).Summary.Trials) _legacyReportIds.Add(t.ManeuverId);
            }
            if(loaded.SelectionWorkflowVersion<3 && !loaded.FinalizedAt.HasValue)
            {
                // Versioned, in-place draft migration. Historical fields are kept for
                // reading; the normal workflow no longer needs a primary maneuver.
                if(loaded.ReportManeuverIds.Count==0) loaded.ReportManeuverIds=new List<string>(_legacyReportIds);
                loaded.SelectionWorkflowVersion=3; loaded.PrimarySelectionRequired=false;
                loaded.MaintainSelections(); _dirty=true;
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
                if(result==DialogResult.OK) { _settings=form.Value; _hline.PreferredFirefoxProfile=_settings.FirefoxProfile; }
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
        void SignInSite()
        {
            if(_busy) return;
            if(HLineLoginForm.SignIn(_hline,this,_settings==null?null:_settings.FirefoxProfile)) lblStatus.Text="متصل به سامانه";
            else lblStatus.Text=_hline.StatusText;
            UpdateControls();
        }
        async Task SendSite()
        {
            if(_busy) return;
            if(!_hasPatient || _exam.Trials.Count==0) {Error("ابتدا معاینه و مانور را ثبت کنید.");return;}
            try {_exam.ValidateSelection(_exam.SelectionWorkflowVersion<3);_exam.ValidateReportSelection(true);} catch(Exception ex) {Error(ex.Message);return;}
            if(!SaveExam(false)) return;
            _busy=true;UpdateControls();
            Exam submitting=_exam;string submittingPath=_path;
            try
            {
                ExaminationSnapshot snapshot=ExaminationSnapshot.Capture(submitting);
                // The JPG+JSON are produced off the UI thread so the window keeps
                // painting during the one-click send. The snapshot is an immutable deep
                // copy, so the bytes are identical to a synchronous export; no network
                // work or submission logic moves here.
                string exportRoot=Path.Combine(Path.GetDirectoryName(submittingPath),"exports");
                ResultPackage package=await Task.Factory.StartNew(delegate { return ResultExport.SaveToDirectory(exportRoot,snapshot); },CancellationToken.None,TaskCreationOptions.DenyChildAttach,TaskScheduler.Default);
                StoreExport(package,submitting,submittingPath);
                SubmissionDraft draft=new SubmissionDraft(snapshot,package);
                // An already attempted write is reconciled read-only by the
                // coordinator, so do not let a provenance failure hide it.
                if(!submitting.Submission.WriteAttempted) draft.ValidateForSubmission();
                bool already=await Task.Factory.StartNew(delegate
                {
                    if(_submissionGateway==null) throw new InvalidOperationException("ارسال به سایت روی این نسخه فعال نیست.");
                    bool designatedTest=_testRecord!=null || (!_allowProductionWrites && !string.IsNullOrEmpty(_testNationalId));
                    LiveSubmissionGateway live=_submissionGateway as LiveSubmissionGateway;
                    if(live!=null) live.AllowHistoricalRecord=designatedTest;
                    if(live==null || !designatedTest) _hline.VerifyLatestRecord(submitting.Patient);
                    MedicalRecord allowed=SubmissionDestination(draft);
                    SubmissionCoordinator coordinator=new SubmissionCoordinator(_submissionGateway,submitting.Submission,delegate {ExamStore.Save(submittingPath,submitting);},allowed);
                    coordinator.Submit(draft);
                    return coordinator.AlreadyPresent;
                });
                lock(_uiSync) { if(!_closing && !IsDisposed && !Disposing && _tips!=null) lblStatus.Text=already?"این نتیجه قبلاً در پرونده ثبت شده بود؛ ارسال دوباره انجام نشد.":"نتیجه و تصویر در سایت ثبت و بررسی شد."; }
            }
            catch(HLineException ex) {Error(ex.Message);}
            catch(DeviceProvenanceException ex) {AppendLog(ex.Result.TechnicalSummary);Error(ex.Message);}
            catch(InvalidOperationException ex) {Error(ex.Message);}
            catch {Error(submitting.Submission.WriteAttempted?"نتیجهٔ ارسال نامطمئن است؛ وضعیت سایت باید بازخوانی شود.":"ارسال انجام نشد؛ پروندهٔ محلی محفوظ است.");}
            finally {_busy=false;UpdateControls();}
        }
        MedicalRecord SubmissionDestination(SubmissionDraft draft)
        {
            if(_testRecord!=null) return _testRecord;
            if(!_allowProductionWrites)
            {
                if(string.IsNullOrEmpty(_testNationalId) || string.IsNullOrEmpty(_testRecordId)) throw new InvalidOperationException("ارسال بالینی هنوز فعال نشده است؛ فقط پروندهٔ تست تعیین‌شده مجاز است.");
                if(draft.NationalId!=_testNationalId || draft.RecordId!=_testRecordId) throw new InvalidOperationException("در مرحلهٔ تست، ارسال فقط به پروندهٔ تست تعیین‌شده مجاز است.");
            }
            return new MedicalRecord {Id=draft.RecordId,PersonId=draft.PersonId,NationalId=draft.NationalId,DateText=draft.RecordDate,Status=draft.RecordStatus};
        }
        void RecordExport(ResultPackage package) { StoreExport(package,_exam,_path); }
        void StoreExport(ResultPackage package,Exam exam,string path)
        {
            exam.ExportReferences.Add(new ExportReference { SnapshotId=package.SourceSnapshotId,ReportPath=package.ReportPath,DataPath=package.DataPath,CreatedAt=DateTime.Now });
            try { ExamStore.Save(path,exam); } catch(Exception ex) { _dirty=true; Error("فایل‌ها صادر شدند؛ ثبت مسیر خروجی در سابقه انجام نشد: "+ex.Message); }
        }
        void ClosingForm(object sender,FormClosingEventArgs args)
        {
            _closing=true;
            if(_busy) { _closing=false; args.Cancel=true; if(_active!=null) StopTest(); Error("ابتدا پایان ثبت یا توقف را منتظر بمانید."); return; }
            if(_dirty && !SaveExam(false)) { _closing=false; args.Cancel=true; return; }
            lock(_uiSync) { _timer.Dispose(); _device.Dispose(); _hline.Dispose(); ToolTip tips=_tips; _tips=null; if(tips!=null) tips.Dispose(); }
        }
    }
}

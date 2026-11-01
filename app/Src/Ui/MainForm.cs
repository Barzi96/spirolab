using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using SpiroLab.Device;

namespace SpiroLab.Ui
{
    public sealed class MainForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(17,103,137), Ink = Color.FromArgb(31,49,66), Bg = Color.FromArgb(240,245,249);
        private readonly DeviceSession _device = new DeviceSession();
        private Exam _exam = new Exam();
        private string _path;
        private bool _busy, _dirty, _fast, _rendering, _simulating;
        private volatile bool _stop;
        private Trial _active, _selected;
        private PacketDecoder _decoder;
        private readonly Queue<DevicePacket> _pending = new Queue<DevicePacket>();
        private readonly object _queueLock = new object();
        private System.Windows.Forms.Timer _timer;
        private int _simPos;
        
        private Panel _patientPanel;
        private TextBox txtOperator,txtCenter,txtReviewed;
        private bool _receivedLiveSamples;
        private string _safeError="none";
        private TextBox txtFirst,txtLast,txtNid,txtEmp,txtCompany,txtJob,txtAge,txtHeight,txtWeight,txtNote,txtLog,txtWarnings,lblInterpretation;
        private ComboBox cmbSex,cmbOrigin;
        private Label lblConnection,lblStatus,lblLive,lblSummary,lblSource,lblPrediction;
        private Button btnStart,btnStop,btnDemo,btnConnect,btnDisconnect,btnNew,btnSave,btnOpen,btnExport,btnReport,btnImport,btnBest,btnImage;
        private DataGridView gridResults,gridTrials;
        private CheckBox chkAccepted;
        private ChartPanel chart,resultChart;
        public MainForm()
        {
            Text="SpiroLab 5 — اسپیرومتری طب کار"; Font=new Font("Tahoma",9.5f); BackColor=Bg; ForeColor=Ink;
            RightToLeft=RightToLeft.Yes; StartPosition=FormStartPosition.CenterScreen;
            MinimumSize=new Size(1080,700); Size=new Size(1280,860);
            BuildUi(); _device.Line+=AppendLog;
            _timer=new System.Windows.Forms.Timer { Interval=50 }; _timer.Tick+=Tick;
            FormClosing+=ClosingForm; UpdateControls();
        }
        public void SelectTab(int index) { }
        public void ConnectOnLaunch() { lblConnection.Text="آماده برای اتصال"; }
        public void RunDemoForTest()
        {
            _fast=Array.IndexOf(Environment.GetCommandLineArgs(),"--slow")<0;
            txtNid.Text="0012345678"; txtFirst.Text="مراجع"; txtLast.Text="نمایشی"; txtAge.Text="34"; txtHeight.Text="178"; txtWeight.Text="82"; txtCompany.Text="شرکت نمونه";
            StartSimulation();
        }
        private Button MakeButton(string text,EventHandler action,bool primary)
        {
            Button b=new Button { Text=text, AutoSize=true, Height=36, MinimumSize=new Size(85,36), FlatStyle=FlatStyle.Flat,
                BackColor=primary?Accent:Color.White, ForeColor=primary?Color.White:Ink, Margin=new Padding(4), Padding=new Padding(8,0,8,0), Cursor=Cursors.Hand };
            b.FlatAppearance.BorderColor=Color.FromArgb(214,224,232); b.Click+=action; return b;
        }
        private Label MakeLabel(string text,float size,bool bold)
        { return new Label { Text=text,Dock=DockStyle.Fill,RightToLeft=RightToLeft.Yes,TextAlign=ContentAlignment.MiddleLeft,Font=new Font("Tahoma",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink }; }
        private FlowLayoutPanel Actions()
        { return new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,AutoScroll=true,Padding=new Padding(4) }; }
        private void BuildUi()
        {
            TableLayoutPanel root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(12),RightToLeft=RightToLeft.No };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,26)); Controls.Add(root);
            TableLayoutPanel top=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=4 };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145)); top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));
            Button diagnostics=MakeButton("وضعیت دستگاه",delegate { using(DiagnosticsForm f=new DiagnosticsForm(DeviceDiagnostics.Read(_device.Connected,_receivedLiveSamples,_device.SafeTransportError!="none"?_device.SafeTransportError:_safeError))) f.ShowDialog(this); },false); top.Controls.Add(diagnostics,3,0);
            lblConnection=MakeLabel("SpiroLab",12,true); top.Controls.Add(lblConnection,0,0);
            btnNew=MakeButton("مراجع جدید",delegate { NewExam(); },false); btnOpen=MakeButton("پرونده قبلی",delegate { OpenExam(); },false);
            top.Controls.Add(btnOpen,1,0); top.Controls.Add(btnNew,2,0); root.Controls.Add(top,0,0);
            // Auxiliary controls are kept outside the visible workflow.
            btnSave=new Button(); btnExport=new Button(); btnImage=new Button(); btnBest=new Button(); btnDisconnect=new Button(); btnImport=new Button();
            txtLog=new TextBox(); txtWarnings=new TextBox(); lblInterpretation=new TextBox(); chkAccepted=new CheckBox(); lblSource=new Label();
            resultChart=new ChartPanel();
            gridResults=Grid(); foreach(string c in new string[] {"شاخص","مقدار","مرجع","درصد","LLN"}) gridResults.Columns.Add(c,c);
            gridTrials=Grid(); foreach(string c in new string[] {"مانور","منبع","FVC","FEV1","وضعیت"}) gridTrials.Columns.Add(c,c);
            foreach(DataGridViewColumn c in gridTrials.Columns) c.SortMode=DataGridViewColumnSortMode.NotSortable;
            gridTrials.SelectionChanged+=delegate { if(!_rendering && gridTrials.CurrentRow!=null) SelectTrial(gridTrials.CurrentRow.Index); };
            Panel main=new Panel { Dock=DockStyle.Fill }; root.Controls.Add(main,0,1); BuildTestTab(main);
            lblStatus=MakeLabel("آماده",9,false); root.Controls.Add(lblStatus,0,2);
        }
        private void BuildTestTab(Control page)
        {
            TableLayoutPanel layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,RightToLeft=RightToLeft.No };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,330));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); page.Controls.Add(layout);
            lblSummary=MakeLabel("مانوری ثبت نشده است",10,false); Label guide=lblSummary;
            guide.BackColor=Color.FromArgb(224,239,245); guide.Padding=new Padding(12); layout.Controls.Add(guide,0,0);
            _patientPanel=new Panel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.White,Padding=new Padding(12),RightToLeft=RightToLeft.Yes,Margin=new Padding(8,0,0,0) };
            layout.Controls.Add(_patientPanel,1,0); layout.SetRowSpan(_patientPanel,3);
            TableLayoutPanel form=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,ColumnCount=2,RightToLeft=RightToLeft.Yes };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,105)); form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); _patientPanel.Controls.Add(form);
            Label pt=MakeLabel("مشخصات مراجع",12,true); pt.ForeColor=Accent; form.Controls.Add(pt,0,0); form.SetColumnSpan(pt,2); form.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            txtNid=Field(form,"کد ملی *",1); txtNid.MaxLength=10;
            txtFirst=Field(form,"نام *",2); txtLast=Field(form,"نام خانوادگی *",3); txtAge=Field(form,"سن (سال) *",4);
            txtEmp=new TextBox(); txtCompany=new TextBox(); txtJob=new TextBox();
            cmbSex=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,Margin=new Padding(2,6,2,3) }; cmbSex.Items.AddRange(new object[] { "مرد","زن" }); cmbSex.SelectedIndex=0;
            form.Controls.Add(MakeLabel("جنسیت",9,false),0,5); form.Controls.Add(cmbSex,1,5); form.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            cmbOrigin=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,Margin=new Padding(2,6,2,3) };
            cmbOrigin.Items.AddRange(new object[] { "Caucasian","انتخاب نشده","سایر" }); cmbOrigin.SelectedIndex=0;
            form.Controls.Add(MakeLabel("نژاد / گروه",9,false),0,6); form.Controls.Add(cmbOrigin,1,6); form.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            txtHeight=Field(form,"قد (cm) *",7); txtWeight=Field(form,"وزن (kg) *",8);
            txtNote=new TextBox();
            lblPrediction=MakeLabel("مرجع: ERS",9,false);
            lblPrediction.BackColor=Color.FromArgb(231,242,247);
            txtCenter=Field(form,"نام مرکز",9); txtOperator=Field(form,"کارشناس",10);
            txtReviewed=Field(form,"تفسیر کارشناس",11); txtReviewed.Multiline=true; txtReviewed.MaxLength=250; form.RowStyles[11].Height=65;
            form.Controls.Add(lblPrediction,0,12); form.SetColumnSpan(lblPrediction,2); form.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
            foreach(TextBox metadata in new TextBox[] {txtCenter,txtOperator,txtReviewed}) metadata.TextChanged+=delegate { if(_exam.Trials.Count>0) _dirty=true; };
            txtAge.TextChanged+=delegate { RefreshPrediction(); }; txtHeight.TextChanged+=delegate { RefreshPrediction(); };
            cmbSex.SelectedIndexChanged+=delegate { RefreshPrediction(); }; cmbOrigin.SelectedIndexChanged+=delegate { RefreshPrediction(); };
            Panel card=new Panel { Dock=DockStyle.Fill,BackColor=Color.White,Margin=new Padding(0,6,0,0) };
            chart=new ChartPanel { Dock=DockStyle.Fill,RightToLeft=RightToLeft.No };
            lblLive=MakeLabel("منتظر شروع تست",10,true); lblLive.Dock=DockStyle.Bottom; lblLive.Height=34; lblLive.Padding=new Padding(8);
            Panel tables=new Panel { Dock=DockStyle.Bottom,Height=158,Padding=new Padding(4) };
            gridResults.Dock=DockStyle.Left; gridResults.Width=310; gridResults.Columns[4].Visible=false; gridResults.Columns[0].FillWeight=170; gridResults.Columns[1].FillWeight=85; gridResults.Columns[2].FillWeight=85; gridResults.Columns[3].FillWeight=70;
            gridTrials.Dock=DockStyle.Fill; gridTrials.Columns[1].Visible=false;
            tables.Controls.Add(gridTrials); tables.Controls.Add(gridResults);
            card.Controls.Add(chart); card.Controls.Add(tables); card.Controls.Add(lblLive); layout.Controls.Add(card,0,1);
            FlowLayoutPanel actions=Actions(); layout.Controls.Add(actions,0,2);
            btnStart=MakeButton("شروع تست",delegate { StartTest(); },true); btnStop=MakeButton("توقف مانور",delegate { StopTest(); },false);
            btnDemo=MakeButton("تست نمایشی",delegate { StartSimulation(); },false); btnConnect=MakeButton("اتصال USB",delegate { ConnectDevice(false); },false);
            btnReport=MakeButton("پایان و گزارش",delegate { ShowReport(); },true);
            Button experimental=MakeButton("حالت آزمایشی",delegate { StartSimulation(); },false); experimental.Enabled=true;
            actions.Controls.AddRange(new Control[] { btnStart,btnStop,btnReport,experimental });
        }
        private TextBox Field(TableLayoutPanel form,string caption,int row)
        {
            form.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); form.Controls.Add(MakeLabel(caption,9,false),0,row);
            TextBox t=new TextBox { Dock=DockStyle.Fill,Margin=new Padding(2,6,2,3),MaxLength=500 }; form.Controls.Add(t,1,row); return t;
        }
        private void RefreshPrediction()
        {
            double age=Fmt.Parse(txtAge.Text,double.NaN), height=Fmt.Parse(txtHeight.Text,double.NaN);
            if(double.IsNaN(age) || age<5 || age>110 || age!=Math.Floor(age) || double.IsNaN(height))
            { lblPrediction.Text="مرجع: ERS"; return; }
            Reference r=Ers93.Compute(new Patient { Age=(int)age,HeightCm=height,Sex=cmbSex.SelectedIndex==1?Sex.Female:Sex.Male,Origin=(Origin)cmbOrigin.SelectedIndex });
            lblPrediction.Text=r.Applicable?"مقادیر مرجع ERS:\nFVC: "+Fmt.N(r.Fvc,2)+" L   |   FEV1: "+Fmt.N(r.Fev1,2)+" L\nPEF: "+Fmt.N(r.Pef,2)+" L/s":"مرجع: —";
        }
        private DataGridView Grid()
        {
            DataGridView g=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,
                MultiSelect=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor=Color.White,BorderStyle=BorderStyle.None,RightToLeft=RightToLeft.Yes,EnableHeadersVisualStyles=false,ColumnHeadersHeight=34 };
            g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,237,243); g.DefaultCellStyle.SelectionBackColor=Color.FromArgb(210,232,243);
            g.DefaultCellStyle.SelectionForeColor=Ink; g.RowTemplate.Height=28; return g;
        }
        private void Post(Action action)
        {
            if(IsDisposed || Disposing) return;
            try { if(InvokeRequired) BeginInvoke((MethodInvoker)delegate { if(!IsDisposed) action(); }); else action(); } catch(InvalidOperationException) { }
        }
        private void AppendLog(string line) { System.Diagnostics.Debug.WriteLine(line); }
        private void Error(string text) { MessageBox.Show(this,text,"SpiroLab",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        private Patient ReadPatient()
        {
            if(_exam.Trials.Count>0) return _exam.Patient.Clone();
            Patient p=new Patient { FirstName=txtFirst.Text.Trim(),LastName=txtLast.Text.Trim(),NationalId=Fmt.NormalizeNumber(txtNid.Text),EmployeeCode=txtEmp.Text.Trim(),
                Company=txtCompany.Text.Trim(),JobTitle=txtJob.Text.Trim(),Note=txtNote.Text.Trim(),Sex=cmbSex.SelectedIndex==1?Sex.Female:Sex.Male,Origin=(Origin)cmbOrigin.SelectedIndex };
            if(p.NationalId.Length!=10 || !System.Text.RegularExpressions.Regex.IsMatch(p.NationalId,@"^[0-9]{10}$")) { Error("کد ملی ده‌رقمی را وارد کنید."); return null; }
            double age=Fmt.Parse(txtAge.Text,double.NaN);
            if(double.IsNaN(age) || age!=Math.Floor(age) || age<5 || age>110) { Error("سن را به صورت عدد صحیح بین ۵ تا ۱۱۰ سال وارد کنید."); return null; }
            p.Age=(int)age; p.HeightCm=Fmt.Parse(txtHeight.Text,double.NaN); p.WeightKg=Fmt.Parse(txtWeight.Text,double.NaN);
            string problem; if(!p.IsUsable(out problem)) { Error(problem); return null; }
            if(p.FirstName.Length==0 || p.LastName.Length==0) { Error("نام و نام خانوادگی مراجع را وارد کنید."); return null; } return p;
        }
        private void Populate(Patient p)
        {
            txtFirst.Text=p.FirstName; txtLast.Text=p.LastName; txtNid.Text=p.NationalId; txtEmp.Text=p.EmployeeCode; txtCompany.Text=p.Company; txtJob.Text=p.JobTitle;
            txtNote.Text=p.Note; txtAge.Text=p.Age.ToString(); txtHeight.Text=Fmt.N(p.HeightCm,1); txtWeight.Text=Fmt.N(p.WeightKg,1); cmbSex.SelectedIndex=(int)p.Sex; cmbOrigin.SelectedIndex=(int)p.Origin;
        }
        private void UpdateControls()
        {
            btnStart.Enabled=btnDemo.Enabled=!_busy; btnConnect.Enabled=!_busy && !_device.Connected;
            btnStop.Enabled=_busy && _active!=null; btnDisconnect.Enabled=!_busy && _device.Connected;
            btnNew.Enabled=btnOpen.Enabled=btnImport.Enabled=!_busy;
            btnSave.Enabled=btnExport.Enabled=!_busy && _exam.Trials.Count>0;
            btnReport.Enabled=btnImage.Enabled=!_busy && ExamSummary.Build(_exam.Trials).Best!=null;
            btnBest.Enabled=!_busy && _exam.Trials.Count>0;
            chkAccepted.Enabled=!_busy && _selected!=null && _selected.Completed && _selected.Result!=null && _selected.Result.Valid;
            gridTrials.Enabled=!_busy; _patientPanel.Enabled=!_busy;
            foreach(Control field in new Control[] {txtFirst,txtLast,txtNid,txtAge,txtHeight,txtWeight,cmbSex,cmbOrigin}) field.Enabled=!_busy && _exam.Trials.Count==0;
            lblConnection.Text=_device.Connected?(_receivedLiveSamples?"نمونه دریافت شد؛ اعتبارسنجی نشده":"پاسخ کنترل USB؛ تست تأیید نشده"):"دستگاه متصل نیست";
        }
        private void ConnectDevice(bool startAfter)
        {
            if(_busy) return; if(startAfter && ReadPatient()==null) return;
            _busy=true; UpdateControls(); lblStatus.Text="در حال اتصال به دستگاه…";
            Thread worker=new Thread(delegate()
            {
                string error; bool ok=_device.Connect(out error);
                Post(delegate
                {
                    _busy=false; UpdateControls(); lblStatus.Text=ok?"دستگاه آماده است":"اتصال برقرار نشد";
                    if(!ok) { _safeError="device_open_or_control_failed"; Error(error); } else if(startAfter) StartTest();
                });
            }) { IsBackground=true }; worker.Start();
        }
        private void DisconnectDevice() { if(_busy) return; _device.Disconnect(); UpdateControls(); lblStatus.Text="اتصال دستگاه قطع شد."; }
        private bool CheckSource(bool simulated,bool imported)
        {
            if(_exam.Trials.Count>=100) { Error("این پرونده به سقف ۱۰۰ مانور رسیده است؛ پروندهٔ جدید باز کنید."); return false; }
            if(_exam.Trials.Count==0) return true;
            Trial first=_exam.Trials[0]; if(first.Simulated==simulated && first.Imported==imported) return true;
            Error("برای تغییر منبع بین دستگاه، نمایش و بازپخش، ابتدا «مراجع جدید» را بزنید. پروندهٔ فعلی ذخیره می‌شود."); return false;
        }
        private void StartTest()
        {
            if(_busy || !CheckSource(false,false)) return;
            Patient p=ReadPatient(); if(p==null) return; if(!_device.Connected) { ConnectDevice(true); return; }
            _exam.Patient=p; _exam.CenterName=txtCenter.Text.Trim(); _exam.OperatorName=txtOperator.Text.Trim(); _exam.ReviewedInterpretation=txtReviewed.Text.Trim(); _stop=false; _decoder=new PacketDecoder(); _active=new Trial { Completed=false }; _selected=null; _busy=true;
            lock(_queueLock) _pending.Clear(); chart.Trial=_active; chart.Result=null; lblLive.Text="آماده‌سازی دستگاه…";
             UpdateControls(); _timer.Start(); Trial capturing=_active;
            Thread worker=new Thread(delegate()
            {
                CaptureOutcome outcome=_device.Capture(capturing,delegate { return _stop; },delegate(DevicePacket packet) { lock(_queueLock) _pending.Enqueue(packet); });
                Post(delegate { DrainPackets(); _receivedLiveSamples=capturing.Flow.Count>0; _safeError=_receivedLiveSamples?"none":"no_valid_live_samples"; capturing.Completed=outcome.Completed && capturing.EndReason.Length==0; capturing.EndReason=outcome.Reason??capturing.EndReason; capturing.DeviceValues=outcome.Results; FinishTrial(); });
            }) { IsBackground=true }; worker.Start();
        }
        private void StartSimulation()
        {
            if(_busy || !CheckSource(true,false)) return; Patient p=ReadPatient(); if(p==null) return;
            _exam.Patient=p; _exam.CenterName=txtCenter.Text.Trim(); _exam.OperatorName=txtOperator.Text.Trim(); _exam.ReviewedInterpretation=txtReviewed.Text.Trim(); _active=Simulator.Build(p,Environment.TickCount); _active.Completed=false;
            _simPos=0; _simulating=true; _busy=true; _selected=null; chart.Trial=_active; chart.Result=null; chart.VisibleSamples=0;
            lblLive.Text="نمایشی — دادهٔ ساختگی";  UpdateControls(); _timer.Start();
        }
        private void DrainPackets()
        {
            if(_active==null) return;
            try { lock(_queueLock) while(_pending.Count>0) _decoder.Add(_active,_pending.Dequeue()); }
            catch(Exception ex) { _stop=true; _active.EndReason=ex.Message; AppendLog(ex.Message); }
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
            t.Result=SpiroAnalysis.Analyze(t,_exam.Patient); _exam.Trials.Add(t); _dirty=true; _selected=t;
            chart.Trial=t; chart.Result=t.Result;
            lblLive.Text=t.Completed?"مانور پایان یافت — "+ExamStore.Source(t):"مانور ناقص — "+t.EndReason;
            RenderTrials(); if(ExamSummary.Build(_exam.Trials).Best!=null) SelectBest(); else SelectTrial(_exam.Trials.Count-1); SaveExam(false); UpdateControls(); 
        }
        private void RenderTrials()
        {
            _rendering=true; gridTrials.Rows.Clear();
            for(int i=0;i<_exam.Trials.Count;i++)
            {
                Trial t=_exam.Trials[i]; AnalysisResult r=t.Result;
                gridTrials.Rows.Add(i+1,ExamStore.Source(t),r!=null && r.Valid?Fmt.N(r.Fvc,2):"—",r!=null && r.Valid?Fmt.N(r.Fev1,2):"—",
                    !t.Completed?"ناقص":r==null || !r.Valid?"نامعتبر":object.ReferenceEquals(ExamSummary.Build(_exam.Trials).BestTrial,t)?"بهترین":"ثبت شد");
            }
            int selected=_exam.Trials.IndexOf(_selected); if(selected>=0) gridTrials.CurrentCell=gridTrials.Rows[selected].Cells[0];
            _rendering=false; UpdateSummary();
        }
        private void SelectTrial(int index)
        {
            if(index<0 || index>=_exam.Trials.Count) return;
            _selected=_exam.Trials[index]; AnalysisResult r=_selected.Result;
            chart.Trial=resultChart.Trial=_selected; chart.Result=resultChart.Result=r;
            lblSource.Text=_exam.Patient.FullName+" | مانور "+(index+1)+" | "+ExamStore.Source(_selected)+(_selected.Completed?" | کامل":" | ناقص"); gridResults.Rows.Clear();
            if(r!=null && r.Valid)
            {
                AddMetric("FVC (L)",r.Fvc,r.Ref.Fvc,r.Ref.FvcLln); AddMetric("FEV1 (L)",r.Fev1,r.Ref.Fev1,r.Ref.Fev1Lln);
                AddMetric("FEV1/FVC (%)",r.Fev1Fvc,r.Ref.Fev1Fvc,r.Ref.RatioLln); AddMetric("PEF (L/s)",r.Pef,r.Ref.Pef,0);
                AddMetric("FEF25 (L/s)",r.Fef25,r.Ref.Fef25,0); AddMetric("FEF50 (L/s)",r.Fef50,r.Ref.Fef50,0); AddMetric("FEF75 (L/s)",r.Fef75,r.Ref.Fef75,0); AddMetric("FEF25-75 (L/s)",r.Fef2575,r.Ref.Fef2575,0); AddMetric("FEV6 (L)",r.Fev6,0,0); AddMetric("FET (s)",r.Fet,0,0); AddMetric("BEV تخمینی (mL)",r.Bev*1000,0,0);
            }
            lblLive.Text="مانور "+(index+1)+(_selected.Completed?"":" — ناقص")+(_selected.Simulated?" | نمایشی":"");
            _rendering=true; chkAccepted.Checked=_selected.OperatorAccepted; _rendering=false; UpdateControls();
        }
        private void AddMetric(string name,double value,double predicted,double lln)
        { gridResults.Rows.Add(name,Fmt.N(value,2),predicted>0?Fmt.N(predicted,2):"—",predicted>0?Fmt.Pct(value/predicted*100):"—",lln>0?Fmt.N(lln,2):"—"); }
        private void SelectBest()
        {
            int best=-1; double score=-1;

            for(int i=0;i<_exam.Trials.Count;i++)
            {
                Trial t=_exam.Trials[i]; if(!t.Completed || t.Result==null || !t.Result.Valid) continue;
                double s=t.Result.Fvc+t.Result.Fev1; if(s>score) { score=s; best=i; }
            }
            if(best<0) { Error("مانور کامل و معتبر برای انتخاب وجود ندارد."); return; }
            _rendering=true; gridTrials.CurrentCell=gridTrials.Rows[best].Cells[0]; _rendering=false; SelectTrial(best); 
        }
        private void UpdateSummary()
        {
            ExamSummary best=ExamSummary.Build(_exam.Trials);
            lblSummary.Text="تعداد مانورها: "+_exam.Trials.Count+(best.BestTrial==null?"":"   |   بهترین مانور: "+(_exam.Trials.IndexOf(best.BestTrial)+1));
            btnStart.Text=_exam.Trials.Count==0?"شروع تست":"تست بعدی";
        }
        private bool SaveExam(bool explicitSave)
        {
            if(_exam.Trials.Count==0) return true;
            _exam.CenterName=txtCenter.Text.Trim(); _exam.OperatorName=txtOperator.Text.Trim(); _exam.ReviewedInterpretation=txtReviewed.Text.Trim();
            try
            {
                if(_path==null) _path=ExamStore.NewPath(); ExamStore.Save(_path,_exam); _dirty=false; lblStatus.Text="ذخیره شد";
                if(explicitSave) MessageBox.Show(this,"پرونده ذخیره شد:\n"+_path,"ذخیره",MessageBoxButtons.OK,MessageBoxIcon.Information); return true;
            }
            catch(Exception ex) { lblStatus.Text="ذخیره انجام نشد: "+ex.Message; Error("ذخیرهٔ پرونده انجام نشد. داده‌ها هنوز در برنامه هستند.\n"+ex.Message); return false; }
        }
        private void NewExam()
        {
            if(_busy || (_dirty && !SaveExam(false))) return;
            _exam=new Exam(); txtOperator.Clear(); txtReviewed.Clear(); _receivedLiveSamples=false; _safeError="none"; _selected=null; _path=null; cmbOrigin.SelectedIndex=0;
            foreach(TextBox t in new TextBox[] { txtFirst,txtLast,txtNid,txtEmp,txtCompany,txtJob,txtAge,txtHeight,txtWeight,txtNote }) t.Clear();
            chart.Trial=resultChart.Trial=null; chart.Result=resultChart.Result=null; gridResults.Rows.Clear(); RenderTrials(); txtWarnings.Clear();
             lblSource.Text="هنوز تستی ثبت نشده است."; lblLive.Text="آماده"; lblStatus.Text="پروندهٔ جدید — مشخصات مراجع را وارد کنید.";
            _rendering=true; chkAccepted.Checked=false; _rendering=false; UpdateControls();  txtFirst.Focus();
        }
        private void OpenExam()
        {
            if(_dirty && !SaveExam(false)) return;
            using(OpenFileDialog dialog=new OpenFileDialog { Filter="پرونده SpiroLab (*.spiro)|*.spiro",InitialDirectory=ExamStore.DataDirectory })
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try
                {
                    Exam loaded=ExamStore.Load(dialog.FileName); _exam=loaded; _path=dialog.FileName; _dirty=false; _selected=null;
                    Populate(_exam.Patient); txtOperator.Text=_exam.OperatorName; txtCenter.Text=_exam.CenterName; txtReviewed.Text=_exam.ReviewedInterpretation; RenderTrials(); if(_exam.Trials.Count>0) SelectTrial(_exam.Trials.Count-1);
                    else { gridResults.Rows.Clear(); chart.Trial=resultChart.Trial=null; txtWarnings.Clear(); lblInterpretation.Text="این پرونده تست ندارد."; }
                    UpdateControls();  lblStatus.Text="پرونده باز شد";
                }
                catch(Exception ex) { Error("پرونده باز نشد: "+ex.Message); }
            }
        }
        private void ExportCsv()
        {
            using(SaveFileDialog dialog=new SaveFileDialog { Filter="CSV UTF-8 (*.csv)|*.csv",FileName="SpiroLab-results.csv" })
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try { ExamStore.ExportCsv(dialog.FileName,_exam); lblStatus.Text="خروجی داده و شاخص‌ها: "+dialog.FileName; } catch(Exception ex) { Error(ex.Message); }
            }
        }
        private void ImportCapture()
        {
            if(!CheckSource(false,true)) return; Patient p=ReadPatient(); if(p==null) return;
            using(OpenFileDialog dialog=new OpenFileDialog { Filter="ضبط MIR با گام حجمی 0.030 L (*.csv)|*.csv" })
            {
                if(dialog.ShowDialog(this)!=DialogResult.OK) return;
                try { Trial t=ExamStore.ImportCapture(dialog.FileName,.030); _exam.Patient=p; _active=t; FinishTrial(); }
                catch(Exception ex) { Error("بازپخش انجام نشد: "+ex.Message); }
            }
        }
        private void ExportImage()
        {
            using(SaveFileDialog dialog=new SaveFileDialog { Filter="گزارش کامل PNG|*.png|فقط نمودارها PNG|*.png",FileName="SpiroLab-report.png" })
                if(dialog.ShowDialog(this)==DialogResult.OK)
                    try
                    {
                        if(dialog.FilterIndex==2) using(Bitmap bitmap=SpiroReport.RenderCurves(_exam.Trials)) bitmap.Save(dialog.FileName,System.Drawing.Imaging.ImageFormat.Png);
                        else ResultExport.Save(dialog.FileName,_exam.Patient,_exam.Trials);
                        lblStatus.Text="تصویر ذخیره شد: "+dialog.FileName;
                    }
                    catch(Exception ex) { Error("تصویر ذخیره نشد: "+ex.Message); }
        }
        private void ShowReport()
        {
            if(ExamSummary.Build(_exam.Trials).Best==null) return;
            _exam.CenterName=txtCenter.Text.Trim(); _exam.OperatorName=txtOperator.Text.Trim(); _exam.ReviewedInterpretation=txtReviewed.Text.Trim(); SaveExam(false);
            try { using(ReportForm report=new ReportForm(ExaminationSnapshot.Capture(_exam))) report.ShowDialog(this); }
            catch(Exception ex) { Error("گزارش آماده نشد: "+ex.Message); }
        }
        private void ClosingForm(object sender,FormClosingEventArgs args)
        {
            if(_busy) { args.Cancel=true; if(_active!=null) StopTest(); lblStatus.Text="ابتدا پایان توقف یا اتصال را منتظر بمانید، سپس برنامه را ببندید."; return; }
            if(_dirty && !SaveExam(false)) { args.Cancel=true; return; } _timer.Dispose(); _device.Dispose();
        }
    }
}

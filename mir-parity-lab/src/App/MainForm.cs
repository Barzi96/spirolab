using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using MirParityLab.Core;

namespace MirParityLab.App
{
    internal sealed class MainForm : Form
    {
        private CaptureDocument _capture;
        private List<ExternalMeasurement> _official = new List<ExternalMeasurement>();
        private ComparisonReport _report;
        private string _capturePath = "";
        private string _officialPath = "";

        private readonly TextBox _summary = new TextBox();
        private readonly ComboBox _maneuverCombo = new ComboBox();
        private readonly DataGridView _packetGrid = Grid();
        private readonly TextBox _packetDetail = new TextBox();
        private readonly DataGridView _fieldGrid = Grid();
        private readonly DataGridView _unknownGrid = Grid();
        private readonly DataGridView _officialGrid = Grid();
        private readonly DataGridView _compareGrid = Grid();
        private readonly TextBox _reportText = new TextBox();
        private readonly ToolStripStatusLabel _status = new ToolStripStatusLabel();
        private readonly Label _headerBadge = new Label();
        private Panel _header;
        private readonly Button _exportCsv = Button("Export CSV...");
        private readonly Button _exportJson = Button("Export JSON...");
        private readonly Button _exportReport = Button("Export report...");
        private readonly Button _recalculate = Button("Recalculate comparison");

        public MainForm()
        {
            Text = "MIR Parity Lab";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1180, 760);
            Size = new Size(1460, 940);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(241, 245, 249);

            BuildHeader();
            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 6);
            tabs.TabPages.Add(BuildCaptureTab());
            tabs.TabPages.Add(BuildFieldsTab());
            tabs.TabPages.Add(BuildOfficialTab());
            tabs.TabPages.Add(BuildCompareTab());
            tabs.TabPages.Add(BuildMethodTab());
            Controls.Add(tabs);

            StatusStrip strip = new StatusStrip();
            strip.BackColor = Color.FromArgb(15, 23, 42);
            _status.ForeColor = Color.White;
            _status.Text = "Offline. No USB transport. Load a capture to begin.";
            strip.Items.Add(_status);
            Controls.Add(strip);
            tabs.BringToFront();

            Shown += delegate { SetStatus("Offline. No USB transport. Load a capture to begin."); };
        }

        private void BuildHeader()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 94;
            header.BackColor = Color.FromArgb(15, 23, 42);
            _header = header;
            Controls.Add(header);

            Label title = new Label();
            title.Text = "MIR PARITY LAB";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI Semibold", 21F, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(22, 14);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "آزمایشگاه مستقل تطبیق داده‌های MIR — فاز ۱، آفلاین و فقط‌خواندنی";
            subtitle.ForeColor = Color.FromArgb(203, 213, 225);
            subtitle.Font = new Font("Segoe UI", 10F);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(25, 55);
            header.Controls.Add(subtitle);

            _headerBadge.Text = "NO USB I/O  •  NO DEVICE WRITES  •  NO PARITY CLAIM";
            _headerBadge.ForeColor = Color.FromArgb(251, 146, 60);
            _headerBadge.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            _headerBadge.AutoSize = true;
            _headerBadge.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _headerBadge.Location = new Point(header.Width - 470, 24);
            header.Resize += delegate { _headerBadge.Left = header.Width - _headerBadge.Width - 24; };
            header.Controls.Add(_headerBadge);

            Label phase = new Label();
            phase.Text = "Phase 1 • import / decode / compare / export";
            phase.ForeColor = Color.FromArgb(148, 163, 184);
            phase.Font = new Font("Segoe UI", 9F);
            phase.AutoSize = true;
            phase.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            phase.Location = new Point(header.Width - 330, 55);
            header.Resize += delegate { phase.Left = header.Width - phase.Width - 24; };
            header.Controls.Add(phase);
        }

        private TabPage BuildCaptureTab()
        {
            TabPage page = new TabPage("1  Capture & packets");
            page.BackColor = Color.FromArgb(241, 245, 249);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.RowCount = 3;
            layout.ColumnCount = 1;
            layout.Padding = new Padding(10);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = false;
            Button load = Button("Load capture...");
            load.Click += delegate { LoadCaptureDialog(); };
            Button demo = Button("Load synthetic demo");
            demo.Click += delegate { LoadSyntheticDemo(); };
            Button clear = Button("Clear / new");
            clear.Click += delegate { ClearAll(); };
            bar.Controls.Add(load);
            bar.Controls.Add(demo);
            bar.Controls.Add(clear);
            Label label = new Label();
            label.Text = "Maneuver:";
            label.AutoSize = true;
            label.Margin = new Padding(18, 11, 4, 0);
            bar.Controls.Add(label);
            _maneuverCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _maneuverCombo.Width = 260;
            _maneuverCombo.Margin = new Padding(0, 6, 0, 0);
            _maneuverCombo.SelectedIndexChanged += delegate { RefreshManeuverViews(); };
            bar.Controls.Add(_maneuverCombo);
            layout.Controls.Add(bar, 0, 0);

            _summary.Dock = DockStyle.Fill;
            _summary.Multiline = true;
            _summary.ReadOnly = true;
            _summary.ScrollBars = ScrollBars.Vertical;
            _summary.BackColor = Color.White;
            _summary.Font = new Font("Consolas", 8.5F);
            _summary.BorderStyle = BorderStyle.FixedSingle;
            layout.Controls.Add(_summary, 0, 1);

            TableLayoutPanel bottom = new TableLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.ColumnCount = 2;
            bottom.RowCount = 1;
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            layout.Controls.Add(bottom, 0, 2);

            AddGridColumns(_packetGrid, new string[] { "Index", "Time (s)", "Type", "Raw", "Length", "Raw bytes", "Decode", "Source line" },
                new string[] { "Index", "TimeSeconds", "Type", "Raw", "Length", "RawBytes", "Decode", "SourceLine" },
                new int[] { 55, 75, 55, 60, 55, 120, 360, 100 });
            bottom.Controls.Add(_packetGrid, 0, 0);
            _packetGrid.SelectionChanged += delegate { ShowPacketDetail(); };

            Panel detailPanel = new Panel();
            detailPanel.Dock = DockStyle.Fill;
            detailPanel.Padding = new Padding(8, 0, 0, 0);
            Label detailLabel = new Label();
            detailLabel.Text = "Raw block / selected packet evidence";
            detailLabel.Dock = DockStyle.Top;
            detailLabel.Height = 22;
            detailLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
            _packetDetail.Dock = DockStyle.Fill;
            _packetDetail.Multiline = true;
            _packetDetail.ReadOnly = true;
            _packetDetail.ScrollBars = ScrollBars.Both;
            _packetDetail.WordWrap = false;
            _packetDetail.Font = new Font("Consolas", 8.5F);
            _packetDetail.BackColor = Color.White;
            detailPanel.Controls.Add(_packetDetail);
            detailPanel.Controls.Add(detailLabel);
            bottom.Controls.Add(detailPanel, 1, 0);
            return page;
        }

        private TabPage BuildFieldsTab()
        {
            TabPage page = new TabPage("2  Decoded fields");
            page.BackColor = Color.FromArgb(241, 245, 249);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.RowCount = 2;
            layout.ColumnCount = 1;
            layout.Padding = new Padding(10);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            Label note = new Label();
            note.Text = "Known offsets are source-level observations. Unknown bytes below are preserved byte-for-byte and never assigned meaning.";
            note.Dock = DockStyle.Fill;
            note.ForeColor = Color.FromArgb(51, 65, 85);
            layout.Controls.Add(note, 0, 0);

            TableLayoutPanel pair = new TableLayoutPanel();
            pair.Dock = DockStyle.Fill;
            pair.ColumnCount = 2;
            pair.RowCount = 1;
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            layout.Controls.Add(pair, 0, 1);

            AddGridColumns(_fieldGrid,
                new string[] { "Field", "Offset", "Size", "Raw", "Raw u16", "Value", "Unit", "Conversion", "Provenance", "Evidence / note" },
                new string[] { "Field", "Offset", "Size", "Raw", "RawU16", "Value", "Unit", "Conversion", "Provenance", "EvidenceNote" },
                new int[] { 90, 55, 45, 85, 65, 70, 45, 145, 115, 360 });
            pair.Controls.Add(_fieldGrid, 0, 0);

            AddGridColumns(_unknownGrid, new string[] { "Offset", "Size", "Raw bytes", "Provenance" },
                new string[] { "Offset", "Size", "RawBytes", "Provenance" },
                new int[] { 60, 50, 260, 85 });
            pair.Controls.Add(_unknownGrid, 1, 0);
            return page;
        }

        private TabPage BuildOfficialTab()
        {
            TabPage page = new TabPage("3  Official MIR evidence");
            page.BackColor = Color.FromArgb(241, 245, 249);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.RowCount = 3;
            layout.ColumnCount = 1;
            layout.Padding = new Padding(10);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            page.Controls.Add(layout);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = false;
            Button import = Button("Import official CSV...");
            import.Click += delegate { ImportOfficialDialog(); };
            Button add = Button("Add manual value...");
            add.Click += delegate { AddManual(); };
            Button remove = Button("Remove selected");
            remove.Click += delegate { RemoveOfficial(); };
            Button template = Button("Save CSV template");
            template.Click += delegate { SaveTemplate(); };
            bar.Controls.Add(import);
            bar.Controls.Add(add);
            bar.Controls.Add(remove);
            bar.Controls.Add(template);
            layout.Controls.Add(bar, 0, 0);

            AddGridColumns(_officialGrid,
                new string[] { "Test ID", "Maneuver key", "Seq", "PRE/POST", "Parameter", "Value", "Unit", "Source", "Precision", "Recorded at", "Same-maneuver attestation", "Evidence" },
                new string[] { "TestId", "ManeuverKey", "Sequence", "PrePost", "Parameter", "Value", "Unit", "Source", "Precision", "RecordedAt", "SameManeuverAttested", "Evidence" },
                new int[] { 130, 160, 45, 70, 85, 70, 50, 145, 65, 135, 150, 330 });
            layout.Controls.Add(_officialGrid, 0, 1);

            Label note = new Label();
            note.Dock = DockStyle.Fill;
            note.Text = "Rows are evidence, not verified MIR parity. Source and precision must come from the actual display, printout or official export; missing precision blocks a defensible tolerance.";
            note.ForeColor = Color.FromArgb(120, 53, 15);
            layout.Controls.Add(note, 0, 2);
            return page;
        }

        private TabPage BuildCompareTab()
        {
            TabPage page = new TabPage("4  Compare & export");
            page.BackColor = Color.FromArgb(241, 245, 249);
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.RowCount = 3;
            layout.ColumnCount = 1;
            layout.Padding = new Padding(10);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
            page.Controls.Add(layout);

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.WrapContents = false;
            _recalculate.Click += delegate { Recalculate(true); };
            _exportCsv.Click += delegate { ExportCsvDialog(); };
            _exportJson.Click += delegate { ExportJsonDialog(); };
            _exportReport.Click += delegate { ExportReportDialog(); };
            _exportCsv.Enabled = _exportJson.Enabled = _exportReport.Enabled = false;
            bar.Controls.Add(_recalculate);
            bar.Controls.Add(_exportCsv);
            bar.Controls.Add(_exportJson);
            bar.Controls.Add(_exportReport);
            layout.Controls.Add(bar, 0, 0);

            AddGridColumns(_compareGrid,
                new string[] { "Maneuver", "Parameter", "Official", "Unit", "Official source", "Decoded", "Decoded provenance", "Raw / offset", "Software", "Software vs official", "Software status", "SpiroLab", "Abs diff", "Rel diff %", "Tolerance", "Status", "Same maneuver", "Reason" },
                new string[] { "Maneuver", "Parameter", "Official", "Unit", "OfficialSource", "Decoded", "DecodedProvenance", "RawOffset", "Software", "SoftwareAbs", "SoftwareStatus", "Spirolab", "AbsDiff", "RelDiffPct", "Tolerance", "Status", "SameManeuver", "Reason" },
                new int[] { 130, 80, 70, 45, 120, 70, 120, 120, 70, 90, 155, 65, 65, 65, 70, 145, 80, 430 });
            _compareGrid.CellFormatting += CompareCellFormatting;
            layout.Controls.Add(_compareGrid, 0, 1);

            _reportText.Dock = DockStyle.Fill;
            _reportText.Multiline = true;
            _reportText.ReadOnly = true;
            _reportText.ScrollBars = ScrollBars.Both;
            _reportText.WordWrap = false;
            _reportText.Font = new Font("Consolas", 8.5F);
            _reportText.BackColor = Color.White;
            _reportText.Text = "The generated Persian research report will appear here after comparison.";
            layout.Controls.Add(_reportText, 0, 2);
            return page;
        }

        private TabPage BuildMethodTab()
        {
            TabPage page = new TabPage("5  Method & limits");
            RichTextBox text = new RichTextBox();
            text.Dock = DockStyle.Fill;
            text.ReadOnly = true;
            text.BackColor = Color.White;
            text.Font = new Font("Segoe UI", 9.5F);
            text.Text = MethodText();
            page.Controls.Add(text);
            return page;
        }

        private static string MethodText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MIR PARITY LAB — PHASE 1 METHOD");
            sb.AppendLine();
            sb.AppendLine("Purpose");
            sb.AppendLine("  Import raw/recorded USB evidence, preserve unknown bytes, decode only observed fields,");
            sb.AppendLine("  accept separately-sourced MIR values, and calculate traceable differences.");
            sb.AppendLine();
            sb.AppendLine("Hard safety boundary");
            sb.AppendLine("  This executable contains no USB transport, no driver call and no device command.");
            sb.AppendLine("  It cannot write firmware, calibration, settings, memory, records, serial, time or date.");
            sb.AppendLine();
            sb.AppendLine("Known cod_LAST offsets (observed in SpiroLab source, not MIR-certified)");
            sb.AppendLine("  0x10 u16_be /100  FVC       L");
            sb.AppendLine("  0x12 u16_be /100  FEV1      L");
            sb.AppendLine("  0x14 u16_be /100  PEF       L/s");
            sb.AppendLine("  0x1A u16_be /100  FET       s");
            sb.AppendLine("  0x50 u16_be /100  PEF time  s   (only when length >= 0x52)");
            sb.AppendLine("  0x0A u16_be       quality/reserved raw only (Unknown; never used clinically)");
            sb.AppendLine();
            sb.AppendLine("Packet / block framing");
            sb.AppendLine("  cod_LAST and cod_CURVE: 4-byte big-endian length, then bulk payload.");
            sb.AppendLine("  Live packet: 3 bytes: type, u16_be raw. 0x10 and 0x20 observations are source-level only.");
            sb.AppendLine("  0x04 is treated by the reviewed source as natural completion; 0xFF means no new sample.");
            sb.AppendLine();
            sb.AppendLine("Association rule");
            sb.AppendLine("  A test_id alone is NOT proof of the same maneuver. The comparison engine only declares");
            sb.AppendLine("  'same maneuver' for an exact maneuver key plus compatible time/PRE-POST evidence, or an");
            sb.AppendLine("  explicit operator attestation. Otherwise it reports Identity not established.");
            sb.AppendLine();
            sb.AppendLine("Tolerance rule");
            sb.AppendLine("  tolerance = half of official output precision + half of observed device resolution.");
            sb.AppendLine("  This is a rounding envelope only. ATS/ERS between-maneuver repeatability is deliberately");
            sb.AppendLine("  NOT used as an allowed error between two software products for the same maneuver.");
            sb.AppendLine();
            sb.AppendLine("Unverified areas");
            sb.AppendLine("  - Firmware semantics, signed flow encoding and packet timing.");
            sb.AppendLine("  - Device-level maneuver identity; only a capture token/heuristic exists.");
            sb.AppendLine("  - Stored cod_CURVE decoding: not implemented; software values are independent reconstructions.");
            sb.AppendLine("  - BTPS, calibration, leak/cough/acceptability, clinical validity.");
            sb.AppendLine("  - Proprietary .mir archive contents: imported opaque, never treated as raw USB.");
            sb.AppendLine();
            sb.AppendLine("Official MIR evidence");
            sb.AppendLine("  The public MIR site describes Spirolab memory, USB/PC operation, MIR Spiro data import and");
            sb.AppendLine("  a Windows SDK for Spirolab. The SDK brochure requires a license agreement and minimum order.");
            sb.AppendLine("  Manuals are in MyMIR; no public USB protocol specification or .mir format specification was found.");
            sb.AppendLine("  See mir-parity-lab/docs/official-access.md for citations and status.");
            return sb.ToString();
        }

        private static Button Button(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.Height = 30;
            b.Margin = new Padding(0, 4, 7, 0);
            b.Padding = new Padding(8, 0, 8, 0);
            return b;
        }

        private static DataGridView Grid()
        {
            DataGridView g = new DataGridView();
            g.Dock = DockStyle.Fill;
            g.AllowUserToAddRows = false;
            g.AllowUserToDeleteRows = false;
            g.AllowUserToResizeRows = false;
            g.AutoGenerateColumns = false;
            g.BackgroundColor = Color.White;
            g.BorderStyle = BorderStyle.FixedSingle;
            g.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            g.ColumnHeadersHeight = 30;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(226, 232, 240);
            g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold);
            g.RowHeadersVisible = false;
            g.ReadOnly = true;
            g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            g.MultiSelect = false;
            g.RowTemplate.Height = 24;
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
            return g;
        }

        private static void AddGridColumns(DataGridView grid, string[] names, string[] properties, int[] widths)
        {
            for (int i = 0; i < names.Length; i++)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
                column.HeaderText = names[i];
                column.DataPropertyName = properties[i];
                column.Width = widths[i];
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                grid.Columns.Add(column);
            }
        }

        private void LoadCaptureDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Load raw / recorded MIR capture";
                dialog.Filter = "Capture files (*.json;*.log;*.txt;*.hex;*.pkts;*.bin;*.mir)|*.json;*.log;*.txt;*.hex;*.pkts;*.bin;*.mir|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadCaptureFile(dialog.FileName);
            }
        }

        private void LoadCaptureFile(string path)
        {
            try
            {
                CaptureDocument doc = CaptureLoader.Load(path);
                _capture = doc;
                _capturePath = path;
                _official = new List<ExternalMeasurement>();
                _officialPath = "";
                _report = null;
                BindOfficial();
                BindCapture();
                SetStatus("Loaded capture: " + Path.GetFileName(path) + "  •  " + doc.Maneuvers.Count + " maneuver(s)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Capture import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadSyntheticDemo()
        {
            string capture = FindFixture("fixtures\\synthetic-capture.json");
            string official = FindFixture("fixtures\\synthetic-official.csv");
            if (capture == null || official == null)
            {
                MessageBox.Show(this, "Synthetic fixtures were not found. Run the application from the repository or keep the fixtures folder beside the executable.", "Demo not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                _capture = CaptureLoader.Load(capture);
                _capturePath = capture;
                _official = OfficialLoader.LoadCsv(official).Measurements;
                _officialPath = official;
                BindOfficial();
                BindCapture();
                Recalculate(false);
                SetStatus("Synthetic demo loaded. These are NOT MIR measurements and cannot establish parity.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Synthetic demo failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FindFixture(string relative)
        {
            List<string> candidates = new List<string>();
            candidates.Add(Path.Combine(Environment.CurrentDirectory, relative));
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "mir-parity-lab", relative));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative));
            candidates.Add(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", relative)));
            candidates.Add(Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", relative)));
            for (int i = 0; i < candidates.Count; i++) if (File.Exists(candidates[i])) return candidates[i];
            return null;
        }

        private void BindCapture()
        {
            _maneuverCombo.Items.Clear();
            _packetGrid.DataSource = null;
            _fieldGrid.DataSource = null;
            _unknownGrid.DataSource = null;
            _packetDetail.Text = "";
            if (_capture == null)
            {
                _summary.Text = "No capture loaded.";
                _maneuverCombo.Enabled = false;
                return;
            }
            _maneuverCombo.Enabled = true;
            for (int i = 0; i < _capture.Maneuvers.Count; i++)
                _maneuverCombo.Items.Add(new ManeuverItem(_capture.Maneuvers[i]));
            if (_maneuverCombo.Items.Count > 0) _maneuverCombo.SelectedIndex = 0;
            _summary.Text = BuildCaptureSummary();
        }

        private string BuildCaptureSummary()
        {
            if (_capture == null) return "No capture loaded.";
            CaptureMeta m = _capture.Meta;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("MIR PARITY LAB — PHASE 1 (OFFLINE, NO USB I/O)");
            sb.AppendLine("No claim of numerical parity with MIR is made by this screen.");
            sb.AppendLine("test_id          : " + m.TestId);
            sb.AppendLine("data_origin      : " + m.DataOrigin + (m.DataOrigin == "synthetic" ? "   <-- SYNTHETIC, NOT MIR DATA" : ""));
            sb.AppendLine("recorded_at      : " + HexUtil.Iso(m.RecordedAt));
            sb.AppendLine("device_model     : " + m.DeviceModel);
            sb.AppendLine("device_serial    : " + (m.DeviceSerial.Length == 0 ? "(not supplied)" : m.DeviceSerial));
            sb.AppendLine("source           : " + Path.GetFileName(_capturePath));
            if (m.SourceFiles.Count > 0) sb.AppendLine("sha256           : " + m.SourceFiles[0].Sha256);
            sb.AppendLine("maneuvers        : " + _capture.Maneuvers.Count);
            sb.AppendLine("official rows    : " + _official.Count);
            if (_capture.OpaqueArchive) sb.AppendLine("archive          : OPAQUE .mir — not parsed, not treated as raw USB");
            if (_capture.UnframedBlob.Length > 0) sb.AppendLine("opaque blob      : " + _capture.UnframedBlob.Length + " bytes");
            sb.AppendLine();
            if (m.Notes.Length > 0) sb.AppendLine("notes            : " + m.Notes);
            for (int i = 0; i < _capture.Warnings.Count; i++) sb.AppendLine("warning          : " + _capture.Warnings[i]);
            return sb.ToString();
        }

        private void RefreshManeuverViews()
        {
            if (_capture == null) return;
            ManeuverItem item = _maneuverCombo.SelectedItem as ManeuverItem;
            if (item == null) return;
            ManeuverRecord maneuver = item.Maneuver;
            List<PacketView> packets = new List<PacketView>();
            for (int i = 0; i < maneuver.Packets.Count; i++)
            {
                PacketRecord p = maneuver.Packets[i];
                PacketView v = new PacketView();
                v.Index = p.Index;
                v.TimeSeconds = p.Seconds.HasValue ? p.Seconds.Value.ToString("0.0000", CultureInfo.InvariantCulture) : "";
                v.Type = p.Type.HasValue ? "0x" + p.Type.Value.ToString("X2") : "";
                v.Raw = p.Raw.HasValue ? p.Raw.Value.ToString(CultureInfo.InvariantCulture) : "";
                v.Length = p.Length.ToString(CultureInfo.InvariantCulture);
                v.RawBytes = HexUtil.ToSpacedHex(p.RawBytes);
                v.Decode = ProtocolDecoder.DecodePacket(p);
                v.SourceLine = p.SourceLine;
                packets.Add(v);
            }
            _packetGrid.DataSource = packets;

            DecodeResult decoded = ProtocolDecoder.DecodeLast(maneuver.LastBlock, maneuver);
            List<FieldView> fields = new List<FieldView>();
            for (int i = 0; i < decoded.Fields.Count; i++)
            {
                DecodedField f = decoded.Fields[i];
                FieldView v = new FieldView();
                v.Field = f.Key;
                v.Offset = "0x" + f.Offset.ToString("X2");
                v.Size = f.Size.ToString(CultureInfo.InvariantCulture);
                v.Raw = HexUtil.ToSpacedHex(f.RawBytes);
                v.RawU16 = f.RawUnsigned.HasValue ? f.RawUnsigned.Value.ToString(CultureInfo.InvariantCulture) : "";
                v.Value = HexUtil.Invariant(f.Value);
                v.Unit = f.Unit;
                v.Conversion = f.Conversion;
                v.Provenance = ProvenanceText.Label(f.Provenance);
                v.EvidenceNote = (f.Note + " " + f.Evidence).Trim();
                fields.Add(v);
            }
            _fieldGrid.DataSource = fields;

            List<UnknownView> unknown = new List<UnknownView>();
            for (int i = 0; i < decoded.UnknownRanges.Count; i++)
            {
                UnknownRange u = decoded.UnknownRanges[i];
                UnknownView v = new UnknownView();
                v.Offset = "0x" + u.Offset.ToString("X2");
                v.Size = u.Size.ToString(CultureInfo.InvariantCulture);
                v.RawBytes = HexUtil.ToSpacedHex(u.RawBytes);
                v.Provenance = "Unknown";
                unknown.Add(v);
            }
            _unknownGrid.DataSource = unknown;

            StringBuilder detail = new StringBuilder();
            detail.AppendLine("Maneuver: " + maneuver.ManeuverKey);
            detail.AppendLine("Association: " + maneuver.Association);
            detail.AppendLine("Evidence: " + maneuver.AssociationEvidence);
            detail.AppendLine("cod_LAST: " + maneuver.LastBlock.Length + " bytes");
            detail.AppendLine("cod_CURVE: " + maneuver.CurveBlock.Length + " bytes (opaque; not decoded)");
            detail.AppendLine("Packets: " + maneuver.Packets.Count);
            detail.AppendLine();
            detail.AppendLine("cod_LAST raw hex:");
            detail.AppendLine(maneuver.LastBlock.Length == 0 ? "(none)" : HexUtil.ToSpacedHex(maneuver.LastBlock));
            detail.AppendLine();
            for (int i = 0; i < decoded.Warnings.Count; i++) detail.AppendLine("warning: " + decoded.Warnings[i]);
            _packetDetail.Text = detail.ToString();
        }

        private void ShowPacketDetail()
        {
            if (_packetGrid.CurrentRow == null) return;
            PacketView v = _packetGrid.CurrentRow.DataBoundItem as PacketView;
            if (v == null) return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Packet #" + v.Index);
            sb.AppendLine("Time: " + v.TimeSeconds + " s");
            sb.AppendLine("Type: " + v.Type);
            sb.AppendLine("Raw: " + v.Raw);
            sb.AppendLine("Length: " + v.Length + " bytes");
            sb.AppendLine("Hex: " + v.RawBytes);
            sb.AppendLine("Decode: " + v.Decode);
            sb.AppendLine("Source: " + v.SourceLine);
            sb.AppendLine();
            sb.AppendLine("No unknown packet field is interpreted here.");
            _packetDetail.Text = sb.ToString();
        }

        private void ImportOfficialDialog()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Import official MIR values or SpiroLab values";
                dialog.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadOfficialFile(dialog.FileName);
            }
        }

        private void LoadOfficialFile(string path)
        {
            try
            {
                OfficialLoadResult result = OfficialLoader.LoadCsv(path);
                if (_official.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this, "Replace the existing " + _official.Count + " official row(s)?", "Replace measurements", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer != DialogResult.Yes) return;
                }
                _official = result.Measurements;
                _officialPath = path;
                _report = null;
                BindOfficial();
                InvalidateReport();
                SetStatus("Imported " + _official.Count + " measurement row(s) from " + Path.GetFileName(path) + ". Not yet compared.");
                if (_capture != null) Recalculate(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Official CSV import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddManual()
        {
            using (ManualMeasurementForm dialog = new ManualMeasurementForm(_capture))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;
                _official.Add(dialog.Result);
                BindOfficial();
                InvalidateReport();
                if (_capture != null) Recalculate(false);
                SetStatus("Added one measurement row. Evidence remains operator-supplied, not independently certified.");
            }
        }

        private void RemoveOfficial()
        {
            if (_officialGrid.CurrentRow == null) return;
            OfficialView selected = _officialGrid.CurrentRow.DataBoundItem as OfficialView;
            if (selected == null || selected.Row == null) return;
            _official.Remove(selected.Row);
            BindOfficial();
            InvalidateReport();
            if (_capture != null) Recalculate(false);
        }

        private void BindOfficial()
        {
            _officialGrid.DataSource = null;
            List<OfficialView> rows = new List<OfficialView>();
            for (int i = 0; i < _official.Count; i++)
            {
                ExternalMeasurement m = _official[i];
                OfficialView v = new OfficialView();
                v.Row = m;
                v.TestId = m.TestId;
                v.ManeuverKey = m.ManeuverKey;
                v.Sequence = m.Sequence.HasValue ? m.Sequence.Value.ToString(CultureInfo.InvariantCulture) : "";
                v.PrePost = m.PrePost;
                v.Parameter = Parameters.DisplayName(m.Parameter);
                v.Value = HexUtil.Invariant(m.Value);
                v.Unit = m.Unit;
                v.Source = MeasurementSourceText.Label(m.Source);
                v.Precision = m.Precision.HasValue ? HexUtil.Invariant(m.Precision) : "";
                v.RecordedAt = HexUtil.Iso(m.RecordedAt);
                v.SameManeuverAttested = m.SameManeuverAttested ? "yes" : "no";
                v.Evidence = m.Evidence;
                rows.Add(v);
            }
            _officialGrid.DataSource = rows;
        }

        private void OfficialCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_officialGrid.Columns[e.ColumnIndex].DataPropertyName == "Source")
            {
                OfficialView v = _officialGrid.Rows[e.RowIndex].DataBoundItem as OfficialView;
                if (v != null) { e.Value = v.Source; e.FormattingApplied = true; }
            }
        }

        private void Recalculate(bool announceErrors)
        {
            if (_capture == null)
            {
                if (announceErrors) MessageBox.Show(this, "Load a capture first.", "No capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                _report = ComparisonEngine.Build(_capture, _official);
                BindComparison();
                _exportCsv.Enabled = _exportJson.Enabled = _exportReport.Enabled = true;
                SetStatus("Comparison rebuilt: " + _report.RowCount + " row(s). No MIR parity claim.");
            }
            catch (Exception ex)
            {
                if (announceErrors) MessageBox.Show(this, ex.Message, "Comparison failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InvalidateReport()
        {
            _report = null;
            _compareGrid.DataSource = null;
            _reportText.Text = "Comparison is out of date. Press Recalculate comparison.";
            _exportCsv.Enabled = _exportJson.Enabled = _exportReport.Enabled = false;
        }

        private void BindComparison()
        {
            if (_report == null) { InvalidateReport(); return; }
            List<CompareView> rows = new List<CompareView>();
            for (int mi = 0; mi < _report.Maneuvers.Count; mi++)
            {
                ManeuverComparison mc = _report.Maneuvers[mi];
                for (int ri = 0; ri < mc.Rows.Count; ri++)
                {
                    ComparisonRow r = mc.Rows[ri];
                    CompareView v = new CompareView();
                    v.Maneuver = r.ManeuverKey;
                    v.Parameter = Parameters.DisplayName(r.Parameter);
                    v.Official = HexUtil.Invariant(r.OfficialValue);
                    v.Unit = r.Unit;
                    v.OfficialSource = MeasurementSourceText.Code(r.OfficialSource);
                    v.Decoded = HexUtil.Invariant(r.DecodedValue);
                    v.DecodedProvenance = ProvenanceText.Label(r.DecodedProvenance);
                    v.RawOffset = (r.DecodedRawHex + (r.DecodedOffset.Length > 0 ? "  " + r.DecodedOffset : "")).Trim();
                    v.Software = HexUtil.Invariant(r.SoftwareValue);
                    v.SoftwareAbs = HexUtil.Invariant(r.OfficialVsSoftwareAbs);
                    v.SoftwareStatus = ComparisonText.Code(r.SoftwareStatus);
                    v.Spirolab = HexUtil.Invariant(r.SpirolabValue);
                    v.AbsDiff = HexUtil.Invariant(r.OfficialVsDecodedAbs);
                    v.RelDiffPct = HexUtil.Invariant(r.OfficialVsDecodedRelPct);
                    v.Tolerance = HexUtil.Invariant(r.ToleranceAbs);
                    v.Status = ComparisonText.Code(r.Status);
                    v.SameManeuver = r.SameManeuver ? "yes" : "NO";
                    v.Reason = r.Reason + (r.ProbableCause.Length > 0 ? " | " + r.ProbableCause : "");
                    rows.Add(v);
                }
            }
            _compareGrid.DataSource = rows;
            _reportText.Text = ReportExporter.BuildMarkdown(_report);
            _reportText.SelectionStart = 0;
            _reportText.SelectionLength = 0;
        }

        private void CompareCellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0) return;
            CompareView v = _compareGrid.Rows[e.RowIndex].DataBoundItem as CompareView;
            if (v == null) return;
            if (_compareGrid.Columns[e.ColumnIndex].DataPropertyName == "Status")
            {
                if (v.Status == "within_rounding_tolerance") e.CellStyle.BackColor = Color.FromArgb(220, 252, 231);
                else if (v.Status == "outside_rounding_tolerance") e.CellStyle.BackColor = Color.FromArgb(254, 226, 226);
                else if (v.Status == "identity_not_established") e.CellStyle.BackColor = Color.FromArgb(254, 243, 199);
                else e.CellStyle.BackColor = Color.FromArgb(241, 245, 249);
            }
            if (_compareGrid.Columns[e.ColumnIndex].DataPropertyName == "SoftwareStatus")
            {
                if (v.SoftwareStatus == "within_rounding_tolerance") e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
                else if (v.SoftwareStatus == "outside_rounding_tolerance") e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
                else e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
            if (_compareGrid.Columns[e.ColumnIndex].DataPropertyName == "DecodedProvenance")
            {
                if (v.DecodedProvenance == "Device-reported") e.CellStyle.ForeColor = Color.FromArgb(21, 128, 61);
                else if (v.DecodedProvenance == "Unverified") e.CellStyle.ForeColor = Color.FromArgb(194, 65, 12);
                else e.CellStyle.ForeColor = Color.FromArgb(71, 85, 105);
            }
        }

        private void SaveTemplate()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Save official-measurement CSV template";
                dialog.Filter = "CSV files (*.csv)|*.csv";
                dialog.FileName = "mir-official-template.csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dialog.FileName, OfficialLoader.TemplateCsv(), new UTF8Encoding(true));
                SetStatus("CSV template saved. Replace the synthetic example with real evidence.");
            }
        }

        private ComparisonReport EnsureReport()
        {
            if (_report == null) Recalculate(true);
            return _report;
        }

        private string BaseName()
        {
            return _capture == null ? "mir-parity" : _capture.Meta.TestId + "-parity";
        }

        private void ExportCsvDialog()
        {
            ComparisonReport report = EnsureReport();
            if (report == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export comparison CSV";
                dialog.Filter = "CSV files (*.csv)|*.csv";
                dialog.FileName = BaseName() + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReportExporter.ExportCsv(report, dialog.FileName);
                    SetStatus("CSV exported: " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "CSV export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExportJsonDialog()
        {
            ComparisonReport report = EnsureReport();
            if (report == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export comparison JSON";
                dialog.Filter = "JSON files (*.json)|*.json";
                dialog.FileName = BaseName() + ".json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, ReportExporter.BuildJson(report), new UTF8Encoding(false));
                    SetStatus("JSON exported: " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "JSON export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ExportReportDialog()
        {
            ComparisonReport report = EnsureReport();
            if (report == null) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export Persian research report";
                dialog.Filter = "Markdown report (*.md)|*.md|Text file (*.txt)|*.txt";
                dialog.FileName = BaseName() + "-report.md";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReportExporter.ExportMarkdown(report, dialog.FileName);
                    SetStatus("Research report exported: " + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Report export failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        private void ClearAll()
        {
            _capture = null;
            _capturePath = "";
            _official = new List<ExternalMeasurement>();
            _officialPath = "";
            _report = null;
            _maneuverCombo.Items.Clear();
            _packetGrid.DataSource = null;
            _fieldGrid.DataSource = null;
            _unknownGrid.DataSource = null;
            _officialGrid.DataSource = null;
            _compareGrid.DataSource = null;
            _packetDetail.Text = "";
            _summary.Text = "No capture loaded.";
            _reportText.Text = "The generated Persian research report will appear here after comparison.";
            _exportCsv.Enabled = _exportJson.Enabled = _exportReport.Enabled = false;
            SetStatus("Cleared. Offline. No USB transport.");
        }

        private void SetStatus(string text)
        {
            _status.Text = text;
        }

        private sealed class ManeuverItem
        {
            public ManeuverRecord Maneuver;
            public ManeuverItem(ManeuverRecord maneuver) { Maneuver = maneuver; }
            public override string ToString()
            {
                return Maneuver.ManeuverKey + "  [" + Maneuver.PrePost + ", seq " + Maneuver.Sequence + "]";
            }
        }

        private sealed class PacketView
        {
            public int Index { get; set; }
            public string TimeSeconds { get; set; }
            public string Type { get; set; }
            public string Raw { get; set; }
            public string Length { get; set; }
            public string RawBytes { get; set; }
            public string Decode { get; set; }
            public string SourceLine { get; set; }
        }

        private sealed class FieldView
        {
            public string Field { get; set; }
            public string Offset { get; set; }
            public string Size { get; set; }
            public string Raw { get; set; }
            public string RawU16 { get; set; }
            public string Value { get; set; }
            public string Unit { get; set; }
            public string Conversion { get; set; }
            public string Provenance { get; set; }
            public string EvidenceNote { get; set; }
        }

        private sealed class UnknownView
        {
            public string Offset { get; set; }
            public string Size { get; set; }
            public string RawBytes { get; set; }
            public string Provenance { get; set; }
        }

        private sealed class OfficialView
        {
            public ExternalMeasurement Row { get; set; }
            public string TestId { get; set; }
            public string ManeuverKey { get; set; }
            public string Sequence { get; set; }
            public string PrePost { get; set; }
            public string Parameter { get; set; }
            public string Value { get; set; }
            public string Unit { get; set; }
            public string Source { get; set; }
            public string Precision { get; set; }
            public string RecordedAt { get; set; }
            public string SameManeuverAttested { get; set; }
            public string Evidence { get; set; }
        }

        private sealed class CompareView
        {
            public string Maneuver { get; set; }
            public string Parameter { get; set; }
            public string Official { get; set; }
            public string Unit { get; set; }
            public string OfficialSource { get; set; }
            public string Decoded { get; set; }
            public string DecodedProvenance { get; set; }
            public string RawOffset { get; set; }
            public string Software { get; set; }
            public string SoftwareAbs { get; set; }
            public string SoftwareStatus { get; set; }
            public string Spirolab { get; set; }
            public string AbsDiff { get; set; }
            public string RelDiffPct { get; set; }
            public string Tolerance { get; set; }
            public string Status { get; set; }
            public string SameManeuver { get; set; }
            public string Reason { get; set; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MirParityLab.Core;

namespace MirParityLab.App
{
    internal sealed class ManualMeasurementForm : Form
    {
        private readonly TextBox _testId = new TextBox();
        private readonly TextBox _maneuverKey = new TextBox();
        private readonly TextBox _sequence = new TextBox();
        private readonly ComboBox _prePost = new ComboBox();
        private readonly TextBox _recordedAt = new TextBox();
        private readonly ComboBox _parameter = new ComboBox();
        private readonly TextBox _value = new TextBox();
        private readonly ComboBox _unit = new ComboBox();
        private readonly ComboBox _source = new ComboBox();
        private readonly TextBox _precision = new TextBox();
        private readonly TextBox _evidence = new TextBox();
        private readonly CheckBox _attested = new CheckBox();
        private readonly TextBox _notes = new TextBox();

        public ExternalMeasurement Result { get; private set; }

        public ManualMeasurementForm(CaptureDocument capture)
        {
            Text = "Add official / reference measurement";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(680, 620);
            Font = new Font("Segoe UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(14);
            layout.ColumnCount = 2;
            layout.RowCount = 15;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 13; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            AddRow(layout, 0, "Test ID (non-identifying)", _testId);
            AddRow(layout, 1, "Maneuver key (optional)", _maneuverKey);
            AddRow(layout, 2, "Sequence (optional)", _sequence);
            _prePost.DropDownStyle = ComboBoxStyle.DropDownList;
            _prePost.Items.AddRange(new object[] { "UNKNOWN", "PRE", "POST" });
            _prePost.SelectedIndex = 0;
            AddRow(layout, 3, "PRE/POST", _prePost);
            AddRow(layout, 4, "Recorded at (ISO, optional)", _recordedAt);

            _parameter.DropDownStyle = ComboBoxStyle.DropDown;
            _parameter.Items.AddRange(new object[] { "FVC", "FEV1", "FEV1/FVC", "PEF", "FET", "PEF time", "FEV6", "FEF25-75" });
            _parameter.Text = "FVC";
            _parameter.SelectedIndexChanged += ParameterChanged;
            AddRow(layout, 5, "Parameter", _parameter);
            AddRow(layout, 6, "Value", _value);
            _unit.DropDownStyle = ComboBoxStyle.DropDown;
            _unit.Items.AddRange(new object[] { "L", "mL", "L/s", "mL/s", "L/min", "s", "ms", "%" });
            _unit.Text = "L";
            AddRow(layout, 7, "Unit", _unit);

            _source.DropDownStyle = ComboBoxStyle.DropDownList;
            _source.Items.Add(new SourceItem(MeasurementSourceKind.MirDeviceDisplay, "MIR device display"));
            _source.Items.Add(new SourceItem(MeasurementSourceKind.MirDevicePrint, "MIR device print"));
            _source.Items.Add(new SourceItem(MeasurementSourceKind.MirSoftware, "Official MIR software"));
            _source.Items.Add(new SourceItem(MeasurementSourceKind.SpirolabSoftware, "SpiroLab software export"));
            _source.Items.Add(new SourceItem(MeasurementSourceKind.ManualReference, "Manual / reference entry"));
            _source.Items.Add(new SourceItem(MeasurementSourceKind.ReferencePredicted, "Reference-predicted"));
            _source.SelectedIndex = 0;
            AddRow(layout, 8, "Source", _source);

            AddRow(layout, 9, "Output precision (e.g. 0.01)", _precision);
            AddRow(layout, 10, "Evidence / provenance note", _evidence);
            _attested.Text = "Operator attests this is the same maneuver (required without an exact key/time proof)";
            _attested.Width = 450;
            layout.Controls.Add(new Label(), 0, 11);
            layout.Controls.Add(_attested, 1, 11);
            AddRow(layout, 12, "Notes", _notes);

            Label privacy = new Label();
            privacy.Text = "Do not enter a patient name, national ID, MRN, birth date, address or phone number. Use only a non-identifying test ID. This tool is offline.";
            privacy.ForeColor = Color.FromArgb(153, 27, 27);
            privacy.AutoSize = false;
            privacy.Dock = DockStyle.Fill;
            layout.Controls.Add(privacy, 0, 13);
            layout.SetColumnSpan(privacy, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Width = 95;
            cancel.DialogResult = DialogResult.Cancel;
            Button ok = new Button();
            ok.Text = "Add measurement";
            ok.Width = 130;
            ok.Click += OkClicked;
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 14);
            layout.SetColumnSpan(buttons, 2);
            CancelButton = cancel;
            AcceptButton = ok;

            if (capture != null)
            {
                _testId.Text = capture.Meta.TestId;
                if (capture.FirstManeuver != null)
                {
                    _maneuverKey.Text = capture.FirstManeuver.ManeuverKey;
                    _sequence.Text = capture.FirstManeuver.Sequence.ToString(CultureInfo.InvariantCulture);
                    _prePost.SelectedItem = capture.FirstManeuver.PrePost == "PRE" || capture.FirstManeuver.PrePost == "POST"
                        ? capture.FirstManeuver.PrePost : "UNKNOWN";
                }
            }
            _testId.ReadOnly = false;
        }

        private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            Label l = new Label();
            l.Text = label;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Dock = DockStyle.Fill;
            layout.Controls.Add(l, 0, row);
            control.Dock = DockStyle.Fill;
            layout.Controls.Add(control, 1, row);
        }

        private void ParameterChanged(object sender, EventArgs e)
        {
            string unit = Parameters.Unit(_parameter.Text);
            if (unit.Length > 0) _unit.Text = unit;
        }

        private void OkClicked(object sender, EventArgs e)
        {
            try
            {
                ExternalMeasurement m = new ExternalMeasurement();
                m.TestId = _testId.Text.Trim();
                m.ManeuverKey = _maneuverKey.Text.Trim();
                if (!String.IsNullOrWhiteSpace(_sequence.Text))
                {
                    int sequence;
                    if (!Int32.TryParse(_sequence.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) || sequence < 1)
                        throw new FormatException("Sequence must be a positive integer.");
                    m.Sequence = sequence;
                }
                m.PrePost = _prePost.SelectedItem == null ? "UNKNOWN" : _prePost.SelectedItem.ToString();
                m.RecordedAt = HexUtil.TryDate(_recordedAt.Text);
                if (!String.IsNullOrWhiteSpace(_recordedAt.Text) && !m.RecordedAt.HasValue)
                    throw new FormatException("Recorded at is not a recognized date/time. Use ISO 8601, e.g. 2026-01-15T09:00:00Z.");
                m.Parameter = _parameter.Text.Trim();
                double value;
                if (!HexUtil.TryDouble(_value.Text, out value)) throw new FormatException("Value must be a finite number.");
                m.Value = value;
                m.Unit = _unit.Text.Trim();
                SourceItem source = _source.SelectedItem as SourceItem;
                if (source == null) throw new FormatException("Select a source.");
                m.Source = source.Kind;
                if (!String.IsNullOrWhiteSpace(_precision.Text))
                {
                    double precision;
                    if (!HexUtil.TryDouble(_precision.Text, out precision) || precision <= 0)
                        throw new FormatException("Precision must be a positive number.");
                    m.Precision = precision;
                }
                m.Evidence = _evidence.Text.Trim();
                m.SameManeuverAttested = _attested.Checked;
                m.Notes = _notes.Text.Trim();
                OfficialLoader.ValidateMeasurement(m);
                Result = m;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid measurement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private sealed class SourceItem
        {
            public MeasurementSourceKind Kind;
            public string Label;
            public SourceItem(MeasurementSourceKind kind, string label) { Kind = kind; Label = label; }
            public override string ToString() { return Label; }
        }
    }
}

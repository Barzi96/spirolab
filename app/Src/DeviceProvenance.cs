using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpiroLab.Device;

namespace SpiroLab
{
    // The gate is deliberately a pure read-only policy over the finalized
    // ExaminationSnapshot. It does not mutate the snapshot, the selection or the
    // BEST VALUES and it never performs network or disk I/O.
    public enum DeviceProvenanceCode
    {
        Allowed = 0,
        MissingDirectDeviceData = 1,
        RawBlockMismatch = 2,
        InvalidManeuverAssociation = 3,
        IncompleteOrInconsistentData = 4
    }

    public sealed class DeviceProvenanceIssue
    {
        public string Code = "";
        public string Metric = "";
        public string ManeuverId = "";
        public string Detail = "";

        public override string ToString()
        {
            return Code + ":" + Metric + ":" + (ManeuverId.Length == 0 ? "-" : ManeuverId) + ":" + Detail;
        }
    }

    public sealed class DeviceProvenanceResult
    {
        readonly List<DeviceProvenanceIssue> issues = new List<DeviceProvenanceIssue>();
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        public bool Allowed { get; private set; }
        public DeviceProvenanceCode Code { get; private set; }
        public string OperatorMessage { get; private set; }
        public string TechnicalSummary { get; private set; }
        public IList<DeviceProvenanceIssue> Issues { get { return issues; } }

        internal DeviceProvenanceResult()
        {
            Allowed = true;
            Code = DeviceProvenanceCode.Allowed;
            OperatorMessage = DeviceProvenanceValidator.OperatorMessage;
        }

        internal void Reject(DeviceProvenanceCode code, string metric, Trial trial, string detail)
        {
            string id = trial == null ? "" : (trial.ManeuverId ?? "");
            string key = code.ToString() + "|" + (metric ?? "") + "|" + id + "|" + (detail ?? "");
            if (!seen.Add(key)) return;
            if (issues.Count < 64)
                issues.Add(new DeviceProvenanceIssue { Code = code.ToString(), Metric = metric ?? "", ManeuverId = id, Detail = detail ?? "" });
            if (Allowed)
            {
                Allowed = false;
                Code = code;
            }
        }

        internal void Finish()
        {
            if (Allowed)
            {
                TechnicalSummary = "device_provenance_allowed";
                return;
            }
            StringBuilder text = new StringBuilder();
            text.Append("device_provenance_rejected code=").Append(Code.ToString());
            int count = 0;
            foreach (DeviceProvenanceIssue issue in issues)
            {
                if (count++ >= 6) break;
                text.Append("; ").Append(issue.ToString());
            }
            TechnicalSummary = text.ToString();
        }
    }

    public sealed class DeviceProvenanceException : InvalidOperationException
    {
        public readonly DeviceProvenanceResult Result;

        public DeviceProvenanceException(DeviceProvenanceResult result)
            : base(DeviceProvenanceValidator.OperatorMessage)
        {
            if (result == null) throw new ArgumentNullException("result");
            Result = result;
        }
    }

    public static class DeviceProvenanceValidator
    {
        public const string OperatorMessage = "منشأ مقادیر اصلی این معاینه از دستگاه تأیید نشد. نتیجه به سامانه ارسال نشد. لطفاً دریافت دستگاه یا پرونده معاینه را بررسی کنید.";

        public static DeviceProvenanceResult Validate(ExaminationSnapshot snapshot)
        {
            DeviceProvenanceResult result = new DeviceProvenanceResult();
            try
            {
                if (snapshot == null || snapshot.Data == null || snapshot.Summary == null || snapshot.Summary.BestValues == null)
                {
                    result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "snapshot", null, "snapshot_or_best_values_missing");
                    result.Finish();
                    return result;
                }

                Exam exam = snapshot.Data;
                SessionBestValues best = snapshot.Summary.BestValues;

                CheckCore(result, snapshot, Metric.Fvc, best.Fvc);
                CheckCore(result, snapshot, Metric.Fev1, best.Fev1);
                CheckCore(result, snapshot, Metric.Pef, best.Pef);

                if (best.Fet.Available)
                {
                    Trial fetTrial = FindTrial(exam, best.Fet.SourceManeuverId);
                    if (best.Fet.Source != MeasurementSource.DeviceReported)
                        result.Reject(DeviceProvenanceCode.MissingDirectDeviceData, Metric.Fet, fetTrial, "fet_not_device_reported");
                    if (CheckTrial(result, fetTrial, Metric.Fet) && CheckSelected(result, snapshot, fetTrial, Metric.Fet))
                    {
                        if (Math.Abs(fetTrial.DeviceValues.Fet - best.Fet.Value.Value) > 0.000000001)
                            result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fet, fetTrial, "best_fet_not_from_device_block");
                    }
                }

                CheckFef2575(result, snapshot, best);
                CheckRatio(result, best);
                CheckVext(result, exam);

                if (exam.SelectionWorkflowVersion >= 4)
                {
                    if (exam.ReportManeuverIds == null || exam.ReportManeuverIds.Count == 0)
                        result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "selection", null, "selected_report_set_empty");
                    if (exam.ReportManeuverIds != null && exam.ReportManeuverIds.Count > 3)
                        result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "selection", null, "selected_report_set_larger_than_three");
                    // The selected set is authoritative for the whole report, not only
                    // for the BEST VALUES it happens to supply. Reuse the same trial
                    // checks for every selected maneuver, including one that supplies
                    // no BEST VALUE and is not the VEXT source.
                    CheckSelectedManeuvers(result, exam);
                }
            }
            catch (Exception ex)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "snapshot", null, "validator_exception_" + ex.GetType().Name);
            }
            result.Finish();
            return result;
        }

        static void CheckCore(DeviceProvenanceResult result, ExaminationSnapshot snapshot, string metric, MeasuredValue value)
        {
            if (value == null || !value.Available)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, null, "core_value_unavailable");
                return;
            }
            if (value.Source != MeasurementSource.DeviceReported)
            {
                Trial source = FindTrial(snapshot.Data, value.SourceManeuverId);
                result.Reject(DeviceProvenanceCode.MissingDirectDeviceData, metric, source, "core_source_" + value.SourceCode);
                return;
            }

            Trial trial = FindTrial(snapshot.Data, value.SourceManeuverId);
            if (!CheckTrial(result, trial, metric)) return;
            if (!CheckSelected(result, snapshot, trial, metric)) return;

            if (value.AcquisitionSequence > 0 && value.AcquisitionSequence != trial.AcquisitionSequence)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "source_acquisition_sequence_mismatch");
                return;
            }
            double deviceValue = DeviceValue(trial.DeviceValues, metric);
            if (Math.Abs(deviceValue - value.Value.Value) > 0.000000001)
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "best_value_not_from_device_block");
        }

        static void CheckFef2575(DeviceProvenanceResult result, ExaminationSnapshot snapshot, SessionBestValues best)
        {
            MeasuredValue fef = best.Fef2575;
            if (fef == null || !fef.Available) return;
            Trial trial = FindTrial(snapshot.Data, fef.SourceManeuverId);
            if (fef.Source == MeasurementSource.LegacyUnverified)
            {
                result.Reject(DeviceProvenanceCode.MissingDirectDeviceData, Metric.Fef2575, trial, "fef2575_legacy_unverified");
                return;
            }
            if (fef.Source != MeasurementSource.SoftwareCalculated)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fef2575, trial, "fef2575_source_unexpected_" + fef.SourceCode);
                return;
            }
            if (!CheckTrial(result, trial, Metric.Fef2575)) return;
            if (!CheckSelected(result, snapshot, trial, Metric.Fef2575)) return;
            if (trial.Result == null || !trial.Result.CurveUsable)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fef2575, trial, "fef2575_curve_not_usable");
                return;
            }
            if (Math.Abs(trial.Result.Fef2575 - fef.Value.Value) > 0.000001)
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fef2575, trial, "fef2575_not_from_stored_curve_result");
        }

        static void CheckRatio(DeviceProvenanceResult result, SessionBestValues best)
        {
            if (best.Fev1Fvc == null || !best.Fev1Fvc.Available) return;
            if (best.Fev1Fvc.Source != MeasurementSource.Derived)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fev1Fvc, null, "ratio_not_derived");
                return;
            }
            if (!best.Fvc.Available || !best.Fev1.Available || best.Fvc.Value.Value <= 0) return;
            double expected = best.Fev1.Value.Value / best.Fvc.Value.Value * 100.0;
            if (Math.Abs(best.Fev1Fvc.Value.Value - expected) > 0.00000001)
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, Metric.Fev1Fvc, null, "ratio_not_from_best_fvc_and_fev1");
        }

        static void CheckVext(DeviceProvenanceResult result, Exam exam)
        {
            VextSelection vext = ClinicVext.SelectForExam(exam);
            if (!vext.Available)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "VEXT", null, "vext_unavailable");
                return;
            }
            Trial trial = FindTrial(exam, vext.ManeuverId);
            if (!CheckTrial(result, trial, "VEXT")) return;
            if (trial.Result == null || !trial.Result.BevAvailable)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "VEXT", trial, "vext_evol_not_available");
                return;
            }
            if (Math.Abs(trial.Result.Bev * 1000.0 - vext.ValueMl) > 0.000001)
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, "VEXT", trial, "vext_value_not_from_source_maneuver");
        }

        static bool CheckTrial(DeviceProvenanceResult result, Trial trial, string metric)
        {
            if (trial == null)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, null, "source_maneuver_missing");
                return false;
            }
            bool ok = true;
            if (trial.Simulated || trial.Imported)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "source_is_simulation_or_replay");
                ok = false;
            }
            if (trial.OperatorRejected)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "source_maneuver_rejected");
                ok = false;
            }
            if (!trial.Completed || trial.OperatorStopped)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "capture_not_completed");
                ok = false;
            }
            if (!string.IsNullOrEmpty(trial.CaptureError))
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "capture_error_present");
                ok = false;
            }
            if (trial.DeviceCaptureSequence <= 0)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_capture_sequence_not_positive");
                ok = false;
            }
            if (trial.Flow == null || trial.Flow.Count < 5)
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "decoded_curve_missing_or_too_short");
                ok = false;
            }

            DeviceResults device = trial.DeviceValues;
            if (device == null)
            {
                result.Reject(DeviceProvenanceCode.MissingDirectDeviceData, metric, trial, "device_result_absent");
                return false;
            }
            if (!device.Present)
            {
                result.Reject(DeviceProvenanceCode.MissingDirectDeviceData, metric, trial, "device_result_not_present");
                ok = false;
            }
            if (!device.BlockValid)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_block_not_valid");
                ok = false;
            }
            if (!device.Associated)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_block_not_associated");
                ok = false;
            }
            if (device.CaptureSequence != trial.DeviceCaptureSequence)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "capture_sequence_mismatch");
                ok = false;
            }
            // A session whose SDA did not yield a serial carries an empty serial on
            // both the trial and its cod_LAST block. That documented both-empty case
            // is accepted (the capture token, natural completion, immediate single
            // read and duplicate-block rejection carry the association); a serial on
            // only one side or two different serials is still rejected.
            if (!SerialConsistent(device.SerialNumber, trial.DeviceSerial))
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial,
                    (string.IsNullOrEmpty(device.SerialNumber) || string.IsNullOrEmpty(trial.DeviceSerial)) ? "serial_number_missing_on_one_side" : "serial_number_mismatch");
                ok = false;
            }
            if (!HasCompletionPacket(trial))
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "completion_packet_missing");
                ok = false;
            }
            if (!HasSamplePacket(trial))
            {
                result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "raw_sample_packet_missing");
                ok = false;
            }

            if (device.Raw == null)
            {
                result.Reject(DeviceProvenanceCode.RawBlockMismatch, metric, trial, "raw_device_block_absent");
                return false;
            }
            if (device.Raw.Length < 0x1C || device.Raw.Length > 4096)
            {
                result.Reject(DeviceProvenanceCode.RawBlockMismatch, metric, trial, "raw_device_block_length_invalid");
                ok = false;
            }
            else
            {
                double rawFvc = U16(device.Raw, 0x10) / 100.0;
                double rawFev1 = U16(device.Raw, 0x12) / 100.0;
                double rawPef = U16(device.Raw, 0x14) / 100.0;
                double rawFet = U16(device.Raw, 0x1A) / 100.0;
                if (!Finite(rawFvc) || !Finite(rawFev1) || !Finite(rawPef) || !Finite(rawFet))
                {
                    result.Reject(DeviceProvenanceCode.RawBlockMismatch, metric, trial, "raw_device_field_not_finite");
                    ok = false;
                }
                if (Math.Abs(rawFvc - device.Fvc) > 0.000000001 ||
                    Math.Abs(rawFev1 - device.Fev1) > 0.000000001 ||
                    Math.Abs(rawPef - device.Pef) > 0.000000001 ||
                    Math.Abs(rawFet - device.Fet) > 0.000000001)
                {
                    result.Reject(DeviceProvenanceCode.RawBlockMismatch, metric, trial, "decoded_values_do_not_match_raw_block");
                    ok = false;
                }

                bool fvcOk = Finite(device.Fvc) && device.Fvc >= 0.05 && device.Fvc <= 12.0;
                bool fev1Ok = Finite(device.Fev1) && device.Fev1 >= 0.05 && device.Fev1 <= device.Fvc + 0.05;
                bool pefOk = Finite(device.Pef) && device.Pef > 0.0 && device.Pef <= 20.0;
                bool fetOk = Finite(device.Fet) && device.Fet > 0.0 && device.Fet <= 30.0;
                if (!fvcOk || !fev1Ok || !pefOk || !fetOk)
                {
                    result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "device_core_field_outside_supported_range");
                    ok = false;
                }
                if (!device.FvcValid || device.FvcValid != fvcOk || !device.Fev1Valid || device.Fev1Valid != fev1Ok ||
                    !device.PefValid || device.PefValid != pefOk || !device.FetValid || device.FetValid != fetOk)
                {
                    result.Reject(DeviceProvenanceCode.IncompleteOrInconsistentData, metric, trial, "device_validity_flags_do_not_match_values");
                    ok = false;
                }
            }

            if (device.ReadAtUtc == DateTime.MinValue)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_read_time_missing");
                ok = false;
            }
            else if (!Finite(device.ReadDelaySeconds) || device.ReadDelaySeconds < 0.0 || device.ReadDelaySeconds > 10.0)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_read_delay_outside_window");
                ok = false;
            }
            else if (trial.When == DateTime.MinValue)
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "maneuver_time_missing");
                ok = false;
            }
            else
            {
                DateTime whenUtc;
                try { whenUtc = trial.When.ToUniversalTime(); }
                catch { whenUtc = DateTime.MinValue; }
                if (whenUtc == DateTime.MinValue)
                {
                    result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "maneuver_time_invalid");
                    ok = false;
                }
                else if (device.ReadAtUtc < whenUtc.AddMinutes(-1) || device.ReadAtUtc > whenUtc.AddMinutes(5))
                {
                    result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "device_read_time_outside_capture_window");
                    ok = false;
                }
            }
            return ok;
        }

        static bool HasCompletionPacket(Trial trial)
        {
            if (trial == null || trial.Packets == null) return false;
            foreach (DevicePacket packet in trial.Packets) if (packet != null && packet.Type == 0x04) return true;
            return false;
        }

        static bool HasSamplePacket(Trial trial)
        {
            if (trial == null || trial.Packets == null) return false;
            foreach (DevicePacket packet in trial.Packets) if (packet != null && (packet.Type == 0x10 || packet.Type == 0x20)) return true;
            return false;
        }

        static bool CheckSelected(DeviceProvenanceResult result, ExaminationSnapshot snapshot, Trial trial, string metric)
        {
            if (snapshot.Data.SelectionWorkflowVersion < 4) return true;
            if (snapshot.Data.ReportManeuverIds == null || !snapshot.Data.ReportManeuverIds.Contains(trial.ManeuverId))
            {
                result.Reject(DeviceProvenanceCode.InvalidManeuverAssociation, metric, trial, "source_maneuver_not_in_selected_report_set");
                return false;
            }
            return true;
        }

        static void CheckSelectedManeuvers(DeviceProvenanceResult result, Exam exam)
        {
            foreach (string id in exam.ReportManeuverIds)
                CheckTrial(result, FindTrial(exam, id), "selection");
        }

        static Trial FindTrial(Exam exam, string id)
        {
            if (exam == null || exam.Trials == null || string.IsNullOrEmpty(id)) return null;
            foreach (Trial trial in exam.Trials) if (trial != null && trial.ManeuverId == id) return trial;
            return null;
        }

        static double DeviceValue(DeviceResults device, string metric)
        {
            if (device == null) return double.NaN;
            switch (metric)
            {
                case Metric.Fvc: return device.Fvc;
                case Metric.Fev1: return device.Fev1;
                case Metric.Pef: return device.Pef;
                case Metric.Fet: return device.Fet;
                default: return double.NaN;
            }
        }

        static int U16(byte[] bytes, int offset)
        {
            return (bytes[offset] << 8) | bytes[offset + 1];
        }

        static bool SerialConsistent(string deviceSerial, string trialSerial)
        {
            return string.Equals(deviceSerial ?? "", trialSerial ?? "", StringComparison.Ordinal);
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}

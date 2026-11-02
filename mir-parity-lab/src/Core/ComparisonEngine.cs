using System;
using System.Collections.Generic;

namespace MirParityLab.Core
{
    public static class ComparisonEngine
    {
        public static ComparisonReport Build(CaptureDocument capture, IList<ExternalMeasurement> measurements)
        {
            if (capture == null) throw new ArgumentNullException("capture");
            ComparisonReport report = new ComparisonReport();
            report.Capture = capture;
            List<ExternalMeasurement> all = measurements == null ? new List<ExternalMeasurement>() : new List<ExternalMeasurement>(measurements);

            for (int i = 0; i < all.Count; i++)
            {
                if (!String.Equals(all[i].TestId, capture.Meta.TestId, StringComparison.OrdinalIgnoreCase))
                    report.Warnings.Add("Official row " + (all[i].SourceRow > 0 ? all[i].SourceRow.ToString() : "?") +
                        " belongs to test_id '" + all[i].TestId + "' and was not matched to capture '" + capture.Meta.TestId + "'.");
            }

            for (int mi = 0; mi < capture.Maneuvers.Count; mi++)
            {
                ManeuverRecord maneuver = capture.Maneuvers[mi];
                ManeuverComparison mc = new ManeuverComparison();
                mc.Maneuver = maneuver;
                mc.Decoded = ProtocolDecoder.DecodeLast(maneuver.LastBlock, maneuver);
                mc.Calculated = CurveCalculator.Calculate(maneuver);

                for (int i = 0; i < all.Count; i++)
                {
                    ExternalMeasurement official = all[i];
                    if (!String.Equals(official.TestId, capture.Meta.TestId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!SameManeuverFilter(official, maneuver, capture)) continue;
                    mc.Official.Add(official);
                    mc.Rows.Add(BuildRow(capture, maneuver, mc, official));
                }
                for (int ri = 0; ri < mc.Rows.Count; ri++)
                {
                    if (mc.Rows[ri].OfficialSource == MeasurementSourceKind.SpirolabSoftware) continue;
                    AttachSpirolabValue(mc.Rows[ri], mc, mc.Rows[ri].Unit);
                    AttachSpirolabDiff(mc.Rows[ri]);
                }
                AddDecodedOnlyRows(mc);
                report.Maneuvers.Add(mc);
            }

            if (capture.Maneuvers.Count == 0)
                report.Warnings.Add("Capture has no maneuver object; no decoded fields can be compared.");
            if (report.RowCount == 0)
                report.Warnings.Add("No comparison rows were produced. Import official MIR values and/or a capture with decoded fields.");
            report.Warnings.Add("A numerical difference is reported as a difference, not as validation of either software.");
            return report;
        }

        private static bool SameManeuverFilter(ExternalMeasurement official, ManeuverRecord maneuver, CaptureDocument capture)
        {
            if (official.ManeuverKey.Length > 0)
                return String.Equals(official.ManeuverKey, maneuver.ManeuverKey, StringComparison.OrdinalIgnoreCase);
            if (official.Sequence.HasValue)
                return official.Sequence.Value == maneuver.Sequence;
            return capture.Maneuvers.Count == 1;
        }

        private static ComparisonRow BuildRow(CaptureDocument capture, ManeuverRecord maneuver,
            ManeuverComparison mc, ExternalMeasurement official)
        {
            ComparisonRow row = new ComparisonRow();
            row.TestId = capture.Meta.TestId;
            row.ManeuverKey = maneuver.ManeuverKey;
            row.Parameter = Parameters.Normalize(official.Parameter);
            row.Unit = official.Unit;
            row.OfficialSource = official.Source;
            row.OfficialPrecision = official.Precision;
            row.SpirolabEvidence = "";

            MatchResult match = Match(capture, maneuver, official, mc.Official.Count);
            row.SameManeuver = match.Established;
            row.MatchEvidence = match.Evidence;
            row.Reason = "";

            if (official.Source == MeasurementSourceKind.ReferencePredicted)
            {
                row.OfficialValue = official.Value;
                row.Status = ComparisonState.ReferenceOnly;
                row.SoftwareStatus = ComparisonState.ReferenceOnly;
                row.Reason = "Reference-predicted value is displayed separately and is not a measured MIR device value.";
                row.ProbableCause = "Not a measurement comparison.";
                AttachDecodedAndSoftware(row, mc, official.Unit);
                return row;
            }

            if (official.Source == MeasurementSourceKind.SpirolabSoftware)
            {
                row.SpirolabValue = official.Value;
                row.SpirolabEvidence = official.Evidence;
                row.Status = ComparisonState.NoMirReference;
                row.Reason = "SpiroLab software value supplied; no MIR official value on this row.";
                row.ProbableCause = "No MIR reference measurement supplied for this parameter.";
                AttachDecodedAndSoftware(row, mc, official.Unit);
                AttachSpirolabDiff(row);
                return row;
            }

            row.OfficialValue = official.Value;
            DecodedField decoded = mc.Decoded.Parameter(row.Parameter);
            double? decodedInOfficialUnit = null;
            double? officialPrecisionInOfficialUnit = official.Precision;
            double? deviceResolutionInOfficialUnit = null;
            string unitError = "";
            if (decoded != null && decoded.FieldPresent && decoded.Value.HasValue)
            {
                double converted;
                double factor;
                string group;
                if (UnitConversion.TryConvert(decoded.Value.Value, decoded.Unit, official.Unit, out converted, out factor, out group, out unitError))
                {
                    decodedInOfficialUnit = converted;
                    if (decoded.Resolution.HasValue) deviceResolutionInOfficialUnit = decoded.Resolution.Value * factor;
                }
            }
            row.DecodedValue = decodedInOfficialUnit;
            if (decoded != null)
            {
                row.DecodedProvenance = decoded.Provenance;
                row.DecodedRawHex = HexUtil.ToSpacedHex(decoded.RawBytes);
                row.DecodedOffset = "0x" + decoded.Offset.ToString("X2") + " (" + decoded.Size + " B)";
                row.DecodedNote = decoded.Note;
            }
            else row.DecodedProvenance = Provenance.Unknown;

            double? softwareInOfficialUnit = ConvertCalc(mc, row.Parameter, official.Unit, out unitError);
            row.SoftwareValue = softwareInOfficialUnit;
            row.SoftwareMethod = mc.Calculated.Available ? mc.Calculated.Method : "";

            AttachSpirolabValue(row, mc, official.Unit);
            AttachDiffs(row);
            AttachSpirolabDiff(row);

            if (!match.Established)
            {
                row.Status = ComparisonState.IdentityNotEstablished;
                row.SoftwareStatus = ComparisonState.IdentityNotEstablished;
                row.Reason = match.Reason;
                row.ProbableCause = "Insufficient non-identifying evidence that both values describe the same maneuver.";
                return row;
            }

            if (decoded == null || !decoded.FieldPresent || !decoded.Value.HasValue)
            {
                row.Status = ComparisonState.NoCapturedValue;
                row.Reason = "No decoded cod_LAST field exists for this parameter.";
                row.ProbableCause = "The observed result block does not expose this parameter at a known offset.";
            }
            else if (decodedInOfficialUnit == null)
            {
                row.Status = ComparisonState.UnitNotComparable;
                row.Reason = unitError;
                row.ProbableCause = "Unsupported or dimensionally different units.";
            }
            else
            {
                double? tol = null;
                if (official.Precision.HasValue && official.Precision.Value > 0 &&
                    decoded.Resolution.HasValue && deviceResolutionInOfficialUnit.HasValue)
                {
                    tol = official.Precision.Value / 2.0 + deviceResolutionInOfficialUnit.Value / 2.0;
                    row.ToleranceBasis = "half official output precision + half observed device resolution (not ATS/ERS repeatability)";
                }
                row.ToleranceAbs = tol;
                if (!tol.HasValue)
                {
                    row.Status = ComparisonState.ToleranceNotEstablished;
                    row.Reason = "Official precision and/or device resolution is missing; no defensible rounding tolerance can be set.";
                    row.ProbableCause = "Missing precision metadata.";
                }
                else if (Math.Abs(row.OfficialVsDecodedAbs.Value) <= tol.Value + 1e-12)
                {
                    row.Status = ComparisonState.WithinRoundingTolerance;
                    row.Reason = "Absolute difference is within the combined half-resolution rounding tolerance.";
                    row.ProbableCause = "Compatible with rounding only; no physiological or calibration agreement is implied.";
                }
                else
                {
                    row.Status = ComparisonState.OutsideRoundingTolerance;
                    row.Reason = "Absolute difference exceeds the combined half-resolution rounding tolerance.";
                    row.ProbableCause = "Undetermined: identity, algorithm, time origin, BTPS, sampling and rounding cannot be separated from this evidence.";
                }
            }

            SetSoftwareStatus(row, official, softwareInOfficialUnit, match.Established);
            return row;
        }

        private static void AttachDecodedAndSoftware(ComparisonRow row, ManeuverComparison mc, string targetUnit)
        {
            DecodedField decoded = mc.Decoded.Parameter(row.Parameter);
            if (decoded != null && decoded.FieldPresent && decoded.Value.HasValue)
            {
                double converted;
                double factor;
                string group;
                string error;
                if (UnitConversion.TryConvert(decoded.Value.Value, decoded.Unit, targetUnit, out converted, out factor, out group, out error))
                    row.DecodedValue = converted;
                row.DecodedProvenance = decoded.Provenance;
                row.DecodedRawHex = HexUtil.ToSpacedHex(decoded.RawBytes);
                row.DecodedOffset = "0x" + decoded.Offset.ToString("X2") + " (" + decoded.Size + " B)";
                row.DecodedNote = decoded.Note;
            }
            string calcError;
            row.SoftwareValue = ConvertCalc(mc, row.Parameter, targetUnit, out calcError);
            row.SoftwareMethod = mc.Calculated.Available ? mc.Calculated.Method : "";
            if (row.OfficialValue.HasValue && row.DecodedValue.HasValue)
            {
                row.OfficialVsDecodedAbs = Math.Abs(row.OfficialValue.Value - row.DecodedValue.Value);
                row.OfficialVsDecodedRelPct = Relative(row.OfficialVsDecodedAbs.Value, row.OfficialValue.Value);
            }
            if (row.OfficialValue.HasValue && row.SoftwareValue.HasValue)
            {
                row.OfficialVsSoftwareAbs = Math.Abs(row.OfficialValue.Value - row.SoftwareValue.Value);
                row.OfficialVsSoftwareRelPct = Relative(row.OfficialVsSoftwareAbs.Value, row.OfficialValue.Value);
            }
        }

        private static void AttachSpirolabValue(ComparisonRow row, ManeuverComparison mc, string targetUnit)
        {
            for (int i = 0; i < mc.Official.Count; i++)
            {
                ExternalMeasurement candidate = mc.Official[i];
                if (candidate.Source != MeasurementSourceKind.SpirolabSoftware) continue;
                if (Parameters.Normalize(candidate.Parameter) != row.Parameter) continue;
                double converted;
                double factor;
                string group;
                string error;
                if (candidate.Value.HasValue &&
                    UnitConversion.TryConvert(candidate.Value.Value, candidate.Unit, targetUnit, out converted, out factor, out group, out error))
                {
                    row.SpirolabValue = converted;
                    row.SpirolabEvidence = candidate.Evidence;
                    return;
                }
            }
        }

        private static void AttachDiffs(ComparisonRow row)
        {
            if (row.OfficialValue.HasValue && row.DecodedValue.HasValue)
            {
                row.OfficialVsDecodedAbs = Math.Abs(row.OfficialValue.Value - row.DecodedValue.Value);
                row.OfficialVsDecodedRelPct = Relative(row.OfficialVsDecodedAbs.Value, row.OfficialValue.Value);
            }
            if (row.OfficialValue.HasValue && row.SoftwareValue.HasValue)
            {
                row.OfficialVsSoftwareAbs = Math.Abs(row.OfficialValue.Value - row.SoftwareValue.Value);
                row.OfficialVsSoftwareRelPct = Relative(row.OfficialVsSoftwareAbs.Value, row.OfficialValue.Value);
            }
        }

        private static void AttachSpirolabDiff(ComparisonRow row)
        {
            if (row.SpirolabValue.HasValue && row.DecodedValue.HasValue)
            {
                row.SpirolabVsDecodedAbs = Math.Abs(row.SpirolabValue.Value - row.DecodedValue.Value);
                row.SpirolabVsDecodedRelPct = Relative(row.SpirolabVsDecodedAbs.Value, row.SpirolabValue.Value);
            }
        }

        private static void SetSoftwareStatus(ComparisonRow row, ExternalMeasurement official, double? softwareValue, bool identityEstablished)
        {
            if (!identityEstablished) { row.SoftwareStatus = ComparisonState.IdentityNotEstablished; return; }
            if (!softwareValue.HasValue) { row.SoftwareStatus = ComparisonState.NoCapturedValue; return; }
            if (!official.Precision.HasValue || official.Precision.Value <= 0)
            {
                row.SoftwareStatus = ComparisonState.ToleranceNotEstablished;
                return;
            }
            double tolerance = official.Precision.Value / 2.0;
            row.SoftwareStatus = Math.Abs(row.OfficialVsSoftwareAbs.GetValueOrDefault()) <= tolerance + 1e-12
                ? ComparisonState.WithinRoundingTolerance
                : ComparisonState.OutsideRoundingTolerance;
        }

        private static double? ConvertCalc(ManeuverComparison mc, string parameter, string targetUnit, out string error)
        {
            error = "";
            double? value = mc.Calculated.Get(parameter);
            if (!value.HasValue) return null;
            string sourceUnit = Parameters.Unit(parameter);
            if (sourceUnit.Length == 0) return value;
            double converted;
            double factor;
            string group;
            if (!UnitConversion.TryConvert(value.Value, sourceUnit, targetUnit, out converted, out factor, out group, out error)) return null;
            return converted;
        }

        private static MatchResult Match(CaptureDocument capture, ManeuverRecord maneuver,
            ExternalMeasurement official, int officialRowCount)
        {
            MatchResult result = new MatchResult();
            result.Maneuver = maneuver;
            if (official.ManeuverKey.Length > 0)
            {
                if (!String.Equals(official.ManeuverKey, maneuver.ManeuverKey, StringComparison.OrdinalIgnoreCase))
                {
                    result.Reason = "Official maneuver_key does not equal the capture maneuver_key.";
                    return result;
                }
                if (official.SameManeuverAttested)
                {
                    result.Established = true;
                    result.Evidence = "operator_attested_exact_maneuver_key";
                    result.Reason = "Operator attested the same maneuver for an exact non-identifying key.";
                    return result;
                }
                if (official.PrePost != "UNKNOWN" && maneuver.PrePost != "UNKNOWN" && official.PrePost != maneuver.PrePost)
                {
                    result.Reason = "PRE/POST evidence conflicts.";
                    return result;
                }
                if (official.RecordedAt.HasValue && maneuver.RecordedAt.HasValue)
                {
                    double seconds = Math.Abs((official.RecordedAt.Value - maneuver.RecordedAt.Value).TotalSeconds);
                    if (seconds <= 300)
                    {
                        result.Established = true;
                        result.Evidence = "exact_maneuver_key_prepost_time_within_300s";
                        result.Reason = "Exact key plus compatible time evidence (within 300 s).";
                        return result;
                    }
                    result.Reason = "Exact key but evidence timestamps differ by more than 300 s.";
                    return result;
                }
                result.Reason = "Exact key supplied, but same-maneuver attestation or independent time evidence is missing.";
                return result;
            }

            if (official.SameManeuverAttested && capture.Maneuvers.Count == 1)
            {
                result.Established = true;
                result.Evidence = "operator_attested_single_maneuver_capture";
                result.Reason = "Operator attested the same maneuver for a single-maneuver capture.";
                return result;
            }
            if (official.SameManeuverAttested && official.Sequence.HasValue && official.Sequence.Value == maneuver.Sequence)
            {
                result.Established = true;
                result.Evidence = "operator_attested_sequence";
                result.Reason = "Operator attested the same maneuver and the sequence number matches.";
                return result;
            }
            result.Reason = "Only a test_id was supplied. A test ID alone is not proof of the same maneuver.";
            return result;
        }

        private static void AddDecodedOnlyRows(ManeuverComparison mc)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < mc.Rows.Count; i++) seen.Add(mc.Rows[i].Parameter);

            string[] order = new string[] { Parameters.Fvc, Parameters.Fev1, Parameters.Fev1Fvc, Parameters.Pef, Parameters.Fet, Parameters.PefTime };
            for (int i = 0; i < order.Length; i++)
            {
                string parameter = order[i];
                if (seen.Contains(parameter)) continue;
                DecodedField decoded = mc.Decoded.Parameter(parameter);
                double? calc = mc.Calculated.Get(parameter);
                if ((decoded == null || !decoded.FieldPresent || !decoded.Value.HasValue) && !calc.HasValue) continue;
                ComparisonRow row = new ComparisonRow();
                row.TestId = "";
                row.ManeuverKey = mc.Maneuver.ManeuverKey;
                row.Parameter = parameter;
                row.Unit = Parameters.Unit(parameter);
                row.Status = ComparisonState.NoMirReference;
                row.SoftwareStatus = ComparisonState.NoMirReference;
                row.Reason = "Captured/derived value is shown for research triage; no official MIR value was supplied.";
                row.ProbableCause = "No MIR reference measurement supplied for this parameter.";
                row.SameManeuver = false;
                if (decoded != null && decoded.FieldPresent && decoded.Value.HasValue)
                {
                    row.DecodedValue = decoded.Value;
                    row.DecodedProvenance = decoded.Provenance;
                    row.DecodedRawHex = HexUtil.ToSpacedHex(decoded.RawBytes);
                    row.DecodedOffset = "0x" + decoded.Offset.ToString("X2") + " (" + decoded.Size + " B)";
                    row.DecodedNote = decoded.Note;
                }
                row.SoftwareValue = calc;
                row.SoftwareMethod = mc.Calculated.Available ? mc.Calculated.Method : "";
                mc.Rows.Add(row);
            }
        }

        private static double? Relative(double? absolute, double? reference)
        {
            if (!absolute.HasValue || !reference.HasValue || Math.Abs(reference.Value) < 1e-15) return null;
            return absolute.Value / Math.Abs(reference.Value) * 100.0;
        }
    }
}

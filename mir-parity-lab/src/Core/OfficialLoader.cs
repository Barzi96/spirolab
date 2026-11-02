using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MirParityLab.Core
{
    public sealed class OfficialLoadResult
    {
        public List<ExternalMeasurement> Measurements = new List<ExternalMeasurement>();
        public List<string> Warnings = new List<string>();
    }

    public static class OfficialLoader
    {
        public static readonly string[] RequiredColumns = new string[] { "test_id", "parameter", "value", "unit", "source" };

        public static OfficialLoadResult LoadCsv(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new CaptureFormatException("No CSV file was selected.");
            if (!File.Exists(path)) throw new FileNotFoundException("CSV not found.", path);
            if (new FileInfo(path).Length > CaptureLoader.MaxInputBytes) throw new CaptureFormatException("CSV exceeds the 5 MiB safety limit.");
            string text = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
            List<List<string>> rows = ParseCsv(text);
            if (rows.Count < 2) throw new CaptureFormatException("CSV must contain a header and at least one data row.");
            List<string> header = rows[0];
            List<string> normalizedHeader = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                string key = NormalizeHeader(header[i]);
                if (key.Length == 0) throw new CaptureFormatException("CSV has an empty header at column " + (i + 1) + ".");
                if (!seen.Add(key)) throw new CaptureFormatException("Duplicate CSV column: " + key);
                if (Privacy.IsForbiddenKey(key))
                    throw new PrivacyException("Patient-identifying CSV column rejected: " + key);
                normalizedHeader.Add(key);
            }
            for (int i = 0; i < RequiredColumns.Length; i++)
                if (!seen.Contains(RequiredColumns[i])) throw new CaptureFormatException("CSV is missing required column: " + RequiredColumns[i]);

            OfficialLoadResult result = new OfficialLoadResult();
            for (int r = 1; r < rows.Count; r++)
            {
                List<string> row = rows[r];
                bool blank = true;
                for (int c = 0; c < row.Count; c++) if (!String.IsNullOrWhiteSpace(row[c])) { blank = false; break; }
                if (blank) continue;
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int c = 0; c < normalizedHeader.Count; c++)
                    values[normalizedHeader[c]] = c < row.Count ? row[c].Trim() : "";
                ExternalMeasurement m;
                try
                {
                    m = FromValues(values);
                    m.SourceRow = r + 1;
                    m.RawLine = JoinRaw(row);
                    ValidateMeasurement(m);
                    result.Measurements.Add(m);
                }
                catch (Exception ex)
                {
                    throw new CaptureFormatException("CSV row " + (r + 1) + ": " + ex.Message);
                }
                if (String.IsNullOrWhiteSpace(m.Evidence))
                    result.Warnings.Add("Row " + (r + 1) + " has no evidence note. Source attribution remains incomplete.");
            }
            if (result.Measurements.Count == 0) throw new CaptureFormatException("CSV contains no data rows.");
            result.Warnings.Add("CSV import preserves the supplied values and precision. It does not certify that they came from the same maneuver.");
            return result;
        }

        public static ExternalMeasurement FromValues(Dictionary<string, string> values)
        {
            ExternalMeasurement m = new ExternalMeasurement();
            m.TestId = Get(values, "test_id");
            m.ManeuverKey = Get(values, "maneuver_key");
            string seq = Get(values, "sequence");
            if (seq.Length > 0)
            {
                int sequence;
                if (!Int32.TryParse(seq, NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) || sequence < 1)
                    throw new FormatException("sequence must be a positive integer.");
                m.Sequence = sequence;
            }
            m.PrePost = NormalizePrePost(Get(values, "pre_post"));
            m.RecordedAt = HexUtil.TryDate(Get(values, "recorded_at"));
            string recorded = Get(values, "recorded_at");
            if (recorded.Length > 0 && !m.RecordedAt.HasValue) throw new FormatException("recorded_at is not a recognized date/time.");
            m.Parameter = Parameters.Normalize(Get(values, "parameter"));
            if (m.Parameter.Length == 0) throw new FormatException("parameter is required.");
            double value;
            if (!HexUtil.TryDouble(Get(values, "value"), out value)) throw new FormatException("value must be a finite number.");
            m.Value = value;
            m.Unit = Get(values, "unit");
            if (m.Unit.Length == 0) throw new FormatException("unit is required.");
            MeasurementSourceKind source;
            string sourceText = Get(values, "source");
            if (!MeasurementSourceText.TryParse(sourceText, out source))
                throw new FormatException("source is not recognized: " + sourceText);
            m.Source = source;
            string precision = Get(values, "precision");
            if (precision.Length > 0)
            {
                double p;
                if (!HexUtil.TryDouble(precision, out p) || p <= 0) throw new FormatException("precision must be a positive number.");
                m.Precision = p;
            }
            m.Evidence = Get(values, "evidence");
            m.SameManeuverAttested = ParseBool(Get(values, "same_maneuver_attested")) ||
                                     ParseBool(Get(values, "operator_attested_same_maneuver"));
            m.Notes = Get(values, "notes");
            return m;
        }

        public static void ValidateMeasurement(ExternalMeasurement m)
        {
            if (m == null) throw new ArgumentNullException("m");
            Privacy.ValidateTestId(m.TestId);
            if (m.Parameter.Length == 0) throw new FormatException("parameter is required.");
            if (!m.Value.HasValue || Double.IsNaN(m.Value.Value) || Double.IsInfinity(m.Value.Value))
                throw new FormatException("finite value is required.");
            if (m.Unit.Length == 0) throw new FormatException("unit is required.");
            if (UnitConversion.Group(m.Unit).Length == 0)
                throw new FormatException("unit is not in the supported dimension set (L, mL, L/s, mL/s, L/min, s, ms, %).");
            if (m.Precision.HasValue && m.Precision.Value <= 0) throw new FormatException("precision must be positive.");
            if (m.Sequence.HasValue && m.Sequence.Value < 1) throw new FormatException("sequence must be positive.");
            if (m.ManeuverKey.Length > 0) m.ManeuverKey = HexUtil.SafeToken(m.ManeuverKey, "");
            if (m.ManeuverKey.Length == 0) m.ManeuverKey = "";
        }

        public static string TemplateCsv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("test_id,maneuver_key,sequence,pre_post,recorded_at,parameter,value,unit,source,precision,evidence,same_maneuver_attested,notes");
            sb.AppendLine("SYN-EXAMPLE-001,SYN-EXAMPLE-001-M1,1,PRE,2026-01-15T09:00:00Z,FVC,4.32,L,mir_device_print,0.01,\"Synthetic example only; replace with your evidence\",yes,\"No patient identifiers\"");
            return sb.ToString();
        }

        private static Dictionary<string, string> GetMap(List<string> header, List<string> row)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++) values[header[i]] = i < row.Count ? row[i].Trim() : "";
            return values;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value.Trim() : "";
        }

        private static string NormalizeHeader(string text)
        {
            return (text ?? "").Trim().TrimStart('\uFEFF').ToLowerInvariant().Replace(" ", "_").Replace("-", "_");
        }

        private static string NormalizePrePost(string text)
        {
            string s = (text ?? "").Trim().ToUpperInvariant();
            return s == "PRE" || s == "POST" ? s : "UNKNOWN";
        }

        private static bool ParseBool(string text)
        {
            string s = (text ?? "").Trim().ToLowerInvariant();
            return s == "yes" || s == "true" || s == "1" || s == "y";
        }

        private static string JoinRaw(List<string> row) { return String.Join(",", row.ToArray()); }

        // RFC4180-style parser with quoted commas, doubled quotes and newlines.
        public static List<List<string>> ParseCsv(string text)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else quoted = false;
                    }
                    else field.Append(c);
                }
                else
                {
                    if (c == '"') quoted = true;
                    else if (c == ',')
                    {
                        row.Add(field.ToString());
                        field.Length = 0;
                    }
                    else if (c == '\r')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        row.Add(field.ToString());
                        field.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else if (c == '\n')
                    {
                        row.Add(field.ToString());
                        field.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            if (quoted) throw new FormatException("Unterminated quoted CSV field.");
            return rows;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using MirParityLab.Core;

namespace MirParityLab.Tests
{
    internal static class ParityLabTests
    {
        private static int _pass;
        private static int _fail;
        private static string _fixtures;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _fixtures = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "fixtures"));
            Console.WriteLine("MIR Parity Lab — test harness");
            Console.WriteLine("Fixtures: " + _fixtures);
            Console.WriteLine();

            try
            {
                TestFixtureLoad();
                TestProtocolDecode();
                TestTruncationAndStaleAssociation();
                TestPacketDecode();
                TestCurveCalculation();
                TestOfficialCsv();
                TestCsvQuoting();
                TestPrivacy();
                TestUnitConversion();
                TestComparison();
                TestIdentityGate();
                TestOutsideTolerance();
                TestUnitConversionComparison();
                TestSpirolabAttachment();
                TestExports();
                TestOpaqueArchive();
                if (args.Length == 2 && args[0] == "--sample") GenerateSample(args[1]);
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine("FATAL: " + ex);
            }

            Console.WriteLine();
            Console.WriteLine("RESULT: " + _pass + " PASS, " + _fail + " FAIL");
            return _fail == 0 ? 0 : 1;
        }

        private static void GenerateSample(string directory)
        {
            Directory.CreateDirectory(directory);
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            ComparisonReport report = ComparisonEngine.Build(doc, official.Measurements);
            string baseName = Path.Combine(directory, "SYN-STRUCT-001-sample");
            ReportExporter.ExportCsv(report, baseName + ".csv");
            File.WriteAllText(baseName + ".json", ReportExporter.BuildJson(report), new UTF8Encoding(false));
            ReportExporter.ExportMarkdown(report, baseName + "-report.md");
            Console.WriteLine("Sample outputs written to " + Path.GetFullPath(directory));
        }

        private static void TestFixtureLoad()
        {
            Section("fixture load and provenance");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            Check(doc.Meta.TestId == "SYN-STRUCT-001", "test_id loaded");
            Check(doc.Meta.DataOrigin == "synthetic", "synthetic origin is explicit");
            Check(doc.Maneuvers.Count == 1, "one maneuver");
            Check(doc.Maneuvers[0].Packets.Count == 120, "120 synthetic packets");
            Check(doc.Meta.SourceFiles.Count == 1, "source file evidence retained");
            Check(doc.Meta.SourceFiles[0].Sha256.Length == 64, "SHA-256 retained");
            Check(doc.Maneuvers[0].LastBlock.Length == 0x60, "cod_LAST length 0x60");
            Check(doc.Maneuvers[0].CurveBlock.Length == 17, "cod_CURVE raw bytes retained");
        }

        private static void TestProtocolDecode()
        {
            Section("cod_LAST offsets, conversion and unknown-byte coverage");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            DecodeResult d = ProtocolDecoder.DecodeLast(doc.Maneuvers[0].LastBlock, doc.Maneuvers[0]);
            Check(d.BlockValid, "block valid at observed minimum length and sane core");
            Check(d.MinimumLengthMet, "minimum length met");
            Check(d.CoreFieldsValid, "core fields sane");
            Check(d.AssociationCaptureBound, "capture association recognized");

            DecodedField fvc = d.Field("FVC");
            DecodedField fev1 = d.Field("FEV1");
            DecodedField pef = d.Field("PEF");
            DecodedField fet = d.Field("FET");
            DecodedField pefTime = d.Field("PEF_TIME");
            DecodedField quality = d.Field("QUALITY_RAW");
            Check(fvc != null && fvc.Offset == 0x10 && fvc.Size == 2, "FVC at 0x10, 2 bytes");
            Check(fev1 != null && fev1.Offset == 0x12 && fev1.Size == 2, "FEV1 at 0x12, 2 bytes");
            Check(pef != null && pef.Offset == 0x14 && pef.Size == 2, "PEF at 0x14, 2 bytes");
            Check(fet != null && fet.Offset == 0x1A && fet.Size == 2, "FET at 0x1A, 2 bytes");
            Check(pefTime != null && pefTime.Offset == 0x50 && pefTime.Size == 2, "PEF time at 0x50, 2 bytes");
            Check(Near(fvc.Value, 4.32, 1e-12), "FVC raw u16 /100 = 4.32 L");
            Check(Near(fev1.Value, 3.51, 1e-12), "FEV1 raw u16 /100 = 3.51 L");
            Check(Near(pef.Value, 8.76, 1e-12), "PEF raw u16 /100 = 8.76 L/s");
            Check(Near(fet.Value, 6.42, 1e-12), "FET raw u16 /100 = 6.42 s");
            Check(Near(pefTime.Value, 0.12, 1e-12), "PEF time raw u16 /100 = 0.12 s");
            Check(fvc.Provenance == Provenance.DeviceReported, "capture-bound decoded field is Device-reported");
            Check(fvc.ClinicalUseAllowed, "valid capture-bound FVC marked available for clinical core");
            Check(pefTime.Provenance == Provenance.DeviceReported, "PEF time retains direct device provenance");
            Check(!pefTime.ClinicalUseAllowed, "PEF time not promoted to clinical use");
            Check(quality != null, "quality field present");
            Check(quality.Provenance == Provenance.Unknown, "quality field is Unknown");
            Check(quality.RawUnsigned == 1, "quality raw integer preserved");
            Check(!quality.ClinicalUseAllowed, "quality field never clinical");
            Check(d.UnknownRanges.Count > 0, "unknown ranges preserved");
            Check(d.CoverageComplete, "coverage complete (known + unknown)");

            byte[] invalidPefTime = (byte[])doc.Maneuvers[0].LastBlock.Clone();
            invalidPefTime[0x50] = 0x02;
            invalidPefTime[0x51] = 0xBC;
            DecodeResult invalidTime = ProtocolDecoder.DecodeLast(invalidPefTime, doc.Maneuvers[0]);
            Check(invalidTime.BlockValid, "core block can remain valid with an invalid optional PEF-time field");
            Check(invalidTime.Field("PEF_TIME").Provenance == Provenance.Unverified, "out-of-range PEF time is not promoted to Device-reported");
            Check(!invalidTime.Field("PEF_TIME").ClinicalUseAllowed, "out-of-range PEF time never clinical");

            int covered = d.BlockLength;
            for (int i = 0; i < d.UnknownRanges.Count; i++) covered -= d.UnknownRanges[i].Size;
            Check(covered == 12, "six observed 2-byte fields cover 12 bytes");

            bool[] seen = new bool[d.BlockLength];
            for (int i = 0; i < d.UnknownRanges.Count; i++)
            {
                UnknownRange u = d.UnknownRanges[i];
                Check(u.RawBytes.Length == u.Size, "unknown range raw byte count");
                for (int j = 0; j < u.Size; j++)
                {
                    Check(!seen[u.Offset + j], "unknown ranges do not overlap at " + (u.Offset + j));
                    seen[u.Offset + j] = true;
                    Check(d.RawBlock[u.Offset + j] == u.RawBytes[j], "unknown byte preserved at " + (u.Offset + j));
                }
            }
        }

        private static void TestTruncationAndStaleAssociation()
        {
            Section("truncation and stale association fail closed");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            byte[] trunc = HexUtil.Slice(doc.Maneuvers[0].LastBlock, 0, 0x1A);
            DecodeResult d = ProtocolDecoder.DecodeLast(trunc, doc.Maneuvers[0]);
            Check(!d.MinimumLengthMet, "short block is below observed minimum");
            Check(!d.BlockValid, "short block is not valid");
            Check(d.Field("FET") == null, "FET is not decoded without its bytes");
            Check(d.Field("FVC") != null && d.Field("FVC").Provenance == Provenance.Unverified, "present short-block field is Unverified");
            Check(d.BlockLength == 0x1A, "short raw block retained");

            ManeuverRecord stale = new ManeuverRecord();
            stale.Association = "stale_duplicate_cod_LAST_block";
            stale.LastBlock = doc.Maneuvers[0].LastBlock;
            DecodeResult s = ProtocolDecoder.DecodeLast(stale.LastBlock, stale);
            Check(!s.AssociationCaptureBound, "stale association not capture-bound");
            Check(s.Field("FVC").Provenance == Provenance.Unverified, "stale block is Unverified, not Device-reported");
            Check(s.Field("QUALITY_RAW").Provenance == Provenance.Unknown, "quality stays Unknown on stale block");
        }

        private static void TestPacketDecode()
        {
            Section("packet structure and observed conversions");
            PacketRecord p = new PacketRecord();
            p.Type = 0x10;
            p.Raw = 0x1388;
            p.RawBytes = new byte[] { 0x10, 0x13, 0x88 };
            Check(ProtocolDecoder.DecodePacket(p).IndexOf("50") >= 0, "0x1388 -> 50.00 in source-level conversion");
            Check(p.Length == 3, "3-byte packet length");
            PacketRecord completion = new PacketRecord();
            completion.Type = 0x04;
            completion.RawBytes = new byte[] { 0x04, 0x00, 0x00 };
            Check(ProtocolDecoder.DecodePacket(completion).IndexOf("completion") >= 0, "0x04 completion marker is labelled observed");
            PacketRecord unknown = new PacketRecord();
            unknown.Type = 0x77;
            unknown.Raw = 1;
            unknown.RawBytes = new byte[] { 0x77, 0x00, 0x01 };
            Check(ProtocolDecoder.DecodePacket(unknown).IndexOf("Unknown") >= 0, "unknown packet type preserved as unknown");
        }

        private static void TestCurveCalculation()
        {
            Section("software reconstruction is separate and labelled");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            CalculatedSet set = CurveCalculator.Calculate(doc.Maneuvers[0]);
            Check(set.Available, "synthetic curve has enough positive flow");
            Check(set.SampleCount == 120, "all 120 packets usable");
            Check(set.Provenance == Provenance.SoftwareCalculated, "calculated provenance is Software-calculated");
            Check(Near(set.Get(Parameters.Pef), 3.0, 0.01), "PEF reconstruction ~3.00 L/s");
            Check(set.Get(Parameters.Fvc).Value > 3.0, "FVC reconstruction is populated");
            Check(set.Get(Parameters.Fev1).Value > 0, "FEV1 reconstruction is populated");
            Check(set.Get(Parameters.Fev1Fvc).HasValue, "ratio reconstruction is populated");
            bool anyDevice = false;
            foreach (KeyValuePair<string, double> pair in set.Values) if (pair.Key == "device_reported") anyDevice = true;
            Check(!anyDevice, "software values do not carry a Device-reported key");
        }

        private static void TestOfficialCsv()
        {
            Section("official CSV import");
            OfficialLoadResult result = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            Check(result.Measurements.Count == 6, "six official-style synthetic rows");
            ExternalMeasurement fvc = Find(result.Measurements, "FVC", MeasurementSourceKind.MirDevicePrint);
            Check(fvc != null, "FVC official row");
            Check(fvc.Precision.HasValue && Math.Abs(fvc.Precision.Value - 0.01) < 1e-12, "FVC precision retained");
            Check(fvc.SameManeuverAttested, "same-maneuver attestation retained");
            Check(fvc.Evidence.IndexOf("SYNTHETIC", StringComparison.OrdinalIgnoreCase) >= 0, "synthetic evidence retained");
            Check(fvc.Value.HasValue && Math.Abs(fvc.Value.Value - 4.32) < 1e-12, "FVC value retained");
        }

        private static void TestCsvQuoting()
        {
            Section("CSV parsing");
            List<List<string>> rows = OfficialLoader.ParseCsv("a,\"b,c\"\r\n\"x\"\"y\",z\r\n");
            Check(rows.Count == 2, "two rows");
            Check(rows[0][1] == "b,c", "quoted comma preserved");
            Check(rows[1][0] == "x\"y", "doubled quote preserved");
            CheckThrows<FormatException>(delegate { OfficialLoader.ParseCsv("\"unterminated"); }, "unterminated quoted field rejected");
        }

        private static void TestPrivacy()
        {
            Section("privacy and identifier guard");
            CheckThrows<PrivacyException>(delegate { Privacy.ValidateTestId("Patient Name"); }, "space/patient-like test_id rejected");
            CheckThrows<PrivacyException>(delegate { Privacy.ValidateTestId(""); }, "empty test_id rejected");
            string path = Path.Combine(Path.GetTempPath(), "mir-parity-privacy-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, "{\"schema\":\"mir-parity-capture/v1\",\"test_id\":\"T1\",\"patient_name\":\"X\"}", Encoding.UTF8);
                CheckThrows<PrivacyException>(delegate { CaptureLoader.Load(path); }, "patient_name key rejected before decode");
            }
            finally { TryDelete(path); }

            string logPath = Path.Combine(Path.GetTempPath(), "mir-parity-log-" + Guid.NewGuid().ToString("N") + ".log");
            try
            {
                File.WriteAllText(logPath, "patient_name = X\npacket 0.0200s 10 30\n", Encoding.UTF8);
                CheckThrows<PrivacyException>(delegate { CaptureLoader.Load(logPath); }, "patient text in a log is rejected before parsing");
            }
            finally { TryDelete(logPath); }

            string csvPath = Path.Combine(Path.GetTempPath(), "mir-parity-csv-privacy-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                File.WriteAllText(csvPath, "test_id,parameter,value,unit,source,address\nT1,FVC,4.32,L,mir_device_print,X\n", Encoding.UTF8);
                CheckThrows<PrivacyException>(delegate { OfficialLoader.LoadCsv(csvPath); }, "patient-identifying CSV column rejected");
            }
            finally { TryDelete(csvPath); }
        }

        private static void TestUnitConversion()
        {
            Section("unit conversion");
            double converted, factor;
            string group, error;
            Check(UnitConversion.TryConvert(4320, "mL", "L", out converted, out factor, out group, out error), "mL to L supported");
            Check(Near(converted, 4.32, 1e-12), "4320 mL = 4.32 L");
            Check(UnitConversion.TryConvert(1, "L/min", "L/s", out converted, out factor, out group, out error), "L/min to L/s supported");
            Check(Near(converted, 1.0 / 60.0, 1e-12), "1 L/min = 1/60 L/s");
            Check(!UnitConversion.TryConvert(1, "L", "s", out converted, out factor, out group, out error), "volume/time dimensions rejected");
        }

        private static void TestComparison()
        {
            Section("comparison status and tolerance");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            ComparisonReport report = ComparisonEngine.Build(doc, official.Measurements);
            Check(report.Maneuvers.Count == 1, "one maneuver comparison");
            Check(report.RowCount == 6, "six comparison rows");
            ComparisonRow fvc = FindRow(report, "FVC", MeasurementSourceKind.MirDevicePrint);
            Check(fvc != null, "FVC comparison row");
            Check(fvc.SameManeuver, "explicit attestation establishes same maneuver");
            Check(fvc.DecodedProvenance == Provenance.DeviceReported, "decoded FVC stays Device-reported");
            Check(fvc.Status == ComparisonState.WithinRoundingTolerance, "identical FVC is within rounding tolerance");
            Check(Near(fvc.ToleranceAbs, 0.01, 1e-12), "tolerance = half official + half device resolution");
            Check(Near(fvc.OfficialVsDecodedAbs, 0, 1e-12), "absolute difference zero");
            ComparisonRow pefTime = FindRow(report, "PEF_TIME", MeasurementSourceKind.MirDevicePrint);
            Check(pefTime != null && pefTime.Status == ComparisonState.WithinRoundingTolerance, "PEF time compared as a separate field");
            ComparisonRow ratio = FindRow(report, "FEV1/FVC", MeasurementSourceKind.MirDevicePrint);
            Check(ratio != null, "ratio row present");
            Check(ratio.Status == ComparisonState.NoCapturedValue, "ratio has no direct device field");
            Check(ratio.SoftwareValue.HasValue, "ratio has a software reconstruction");
            Check(ratio.DecodedProvenance != Provenance.DeviceReported, "software ratio is not Device-reported");
        }

        private static void TestIdentityGate()
        {
            Section("identity gate");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            List<ExternalMeasurement> noKey = new List<ExternalMeasurement>();
            for (int i = 0; i < official.Measurements.Count; i++)
            {
                ExternalMeasurement c = Clone(official.Measurements[i]);
                c.ManeuverKey = "";
                c.Sequence = null;
                c.RecordedAt = null;
                c.SameManeuverAttested = false;
                noKey.Add(c);
            }
            ComparisonReport report = ComparisonEngine.Build(doc, noKey);
            Check(report.RowCount == 6, "rows still shown for research triage");
            for (int i = 0; i < report.Maneuvers[0].Rows.Count; i++)
                Check(report.Maneuvers[0].Rows[i].Status == ComparisonState.IdentityNotEstablished,
                    "test_id alone does not establish same maneuver");
        }

        private static void TestOutsideTolerance()
        {
            Section("outside-tolerance arithmetic");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            for (int i = 0; i < official.Measurements.Count; i++)
                if (official.Measurements[i].Parameter == "FVC") official.Measurements[i].Value = 4.40;
            ComparisonReport report = ComparisonEngine.Build(doc, official.Measurements);
            ComparisonRow fvc = FindRow(report, "FVC", MeasurementSourceKind.MirDevicePrint);
            Check(fvc.Status == ComparisonState.OutsideRoundingTolerance, "0.08 L difference is outside 0.01 tolerance");
            Check(Near(fvc.OfficialVsDecodedAbs, 0.08, 1e-12), "absolute difference 0.08");
            Check(fvc.ProbableCause.IndexOf("Undetermined", StringComparison.OrdinalIgnoreCase) >= 0, "cause is not guessed");
        }

        private static void TestUnitConversionComparison()
        {
            Section("comparison in alternate units");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            List<ExternalMeasurement> rows = new List<ExternalMeasurement>();
            for (int i = 0; i < official.Measurements.Count; i++)
                if (official.Measurements[i].Parameter != "FVC") rows.Add(official.Measurements[i]);
            ExternalMeasurement fvc = Clone(Find(official.Measurements, "FVC", MeasurementSourceKind.MirDevicePrint));
            fvc.Value = 4320;
            fvc.Unit = "mL";
            fvc.Precision = 10;
            rows.Add(fvc);
            ComparisonReport report = ComparisonEngine.Build(doc, rows);
            ComparisonRow row = FindRow(report, "FVC", MeasurementSourceKind.MirDevicePrint);
            Check(row != null, "FVC row in mL");
            Check(Near(row.DecodedValue, 4320, 1e-9), "decoded L converted to official mL");
            Check(row.Status == ComparisonState.WithinRoundingTolerance, "unit-normalized difference within 10 mL tolerance");
            Check(Near(row.ToleranceAbs, 10, 1e-9), "tolerance converted to official unit");
        }

        private static void TestSpirolabAttachment()
        {
            Section("SpiroLab software value attachment");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            List<ExternalMeasurement> rows = new List<ExternalMeasurement>(official.Measurements);
            ExternalMeasurement spiro = Clone(Find(official.Measurements, "FVC", MeasurementSourceKind.MirDevicePrint));
            spiro.Source = MeasurementSourceKind.SpirolabSoftware;
            spiro.Value = 4310;
            spiro.Unit = "mL";
            spiro.Precision = 10;
            spiro.Evidence = "Synthetic SpiroLab-style row for plumbing only";
            rows.Add(spiro);
            ComparisonReport report = ComparisonEngine.Build(doc, rows);
            ComparisonRow fvc = FindRow(report, "FVC", MeasurementSourceKind.MirDevicePrint);
            Check(fvc.SpirolabValue.HasValue && Near(fvc.SpirolabValue, 4.31, 1e-12), "SpiroLab value converted to the official unit and attached");
            ComparisonRow own = FindRow(report, "FVC", MeasurementSourceKind.SpirolabSoftware);
            Check(own != null && own.Status == ComparisonState.NoMirReference, "SpiroLab-only row has no MIR reference");
            Check(own != null && Near(own.DecodedValue, 4320, 1e-9), "SpiroLab-only row converts decoded L to its mL unit");
            Check(own != null && Near(own.SpirolabVsDecodedAbs, 10, 1e-9), "SpiroLab-only row compares in mL");
        }

        private static void TestExports()
        {
            Section("CSV / JSON / Markdown export");
            CaptureDocument doc = CaptureLoader.Load(Path.Combine(_fixtures, "synthetic-capture.json"));
            OfficialLoadResult official = OfficialLoader.LoadCsv(Path.Combine(_fixtures, "synthetic-official.csv"));
            ComparisonReport report = ComparisonEngine.Build(doc, official.Measurements);
            string dir = Path.Combine(Path.GetTempPath(), "mir-parity-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string csvPath = Path.Combine(dir, "report.csv");
                string jsonPath = Path.Combine(dir, "report.json");
                string mdPath = Path.Combine(dir, "report.md");
                ReportExporter.ExportCsv(report, csvPath);
                File.WriteAllText(jsonPath, ReportExporter.BuildJson(report), new UTF8Encoding(false));
                ReportExporter.ExportMarkdown(report, mdPath);
                Check(File.Exists(csvPath) && new FileInfo(csvPath).Length > 100, "CSV written");
                Check(File.ReadAllText(csvPath).IndexOf("official_vs_decoded_abs", StringComparison.Ordinal) >= 0, "CSV has difference columns");
                Check(File.ReadAllText(csvPath).Split('\n').Length >= 7, "CSV has header plus six rows");

                string json = File.ReadAllText(jsonPath);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                Dictionary<string, object> root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                Check(root != null, "JSON parses");
                Check(Convert.ToString(root["claim"]).IndexOf("no claim", StringComparison.OrdinalIgnoreCase) >= 0, "JSON carries no-parity claim");
                object[] maneuvers = root["maneuvers"] as object[];
                Check(maneuvers != null && maneuvers.Length == 1, "JSON maneuver array");
                Dictionary<string, object> maneuver = maneuvers[0] as Dictionary<string, object>;
                Dictionary<string, object> decoded = maneuver["decoded"] as Dictionary<string, object>;
                Check(decoded != null, "JSON decoded object");
                object[] fields = decoded["fields"] as object[];
                Check(fields != null && fields.Length == 6, "JSON has six decoded fields");
                object[] unknown = decoded["unknown_ranges"] as object[];
                Check(unknown != null && unknown.Length > 0, "JSON has unknown ranges");
                Dictionary<string, object> calc = maneuver["calculated"] as Dictionary<string, object>;
                Check(Convert.ToString(calc["provenance"]) == "software_calculated", "JSON calculated provenance is software_calculated");
                Check(json.IndexOf("\"claim\"", StringComparison.Ordinal) >= 0, "JSON claim key present");

                string md = File.ReadAllText(mdPath);
                Check(md.IndexOf("MIR Parity Lab", StringComparison.Ordinal) >= 0, "Markdown title present");
                Check(md.IndexOf("ادعایی", StringComparison.Ordinal) >= 0, "Markdown has Persian no-claim warning");
                Check(md.IndexOf("Device-reported", StringComparison.Ordinal) >= 0, "Markdown reports provenance");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static void TestOpaqueArchive()
        {
            Section("opaque .mir is not treated as raw USB");
            string path = Path.Combine(Path.GetTempPath(), "mir-opaque-" + Guid.NewGuid().ToString("N") + ".mir");
            try
            {
                File.WriteAllBytes(path, new byte[] { 0x4D, 0x49, 0x52, 0x00, 0x01, 0x02 });
                CaptureDocument doc = CaptureLoader.Load(path);
                Check(doc.OpaqueArchive, "archive flagged opaque");
                Check(doc.Maneuvers.Count == 0, "no maneuver guessed");
                Check(doc.UnframedBlob.Length == 6, "raw bytes preserved");
                Check(doc.Warnings.Count > 0 && doc.Warnings[0].IndexOf("NOT", StringComparison.Ordinal) >= 0, "warning says it is not raw USB");
            }
            finally { TryDelete(path); }
        }

        private static ExternalMeasurement Find(List<ExternalMeasurement> list, string parameter, MeasurementSourceKind source)
        {
            for (int i = 0; i < list.Count; i++)
                if (Parameters.Normalize(list[i].Parameter) == Parameters.Normalize(parameter) && list[i].Source == source) return list[i];
            return null;
        }

        private static ComparisonRow FindRow(ComparisonReport report, string parameter, MeasurementSourceKind source)
        {
            string normalized = Parameters.Normalize(parameter);
            for (int mi = 0; mi < report.Maneuvers.Count; mi++)
                for (int ri = 0; ri < report.Maneuvers[mi].Rows.Count; ri++)
                {
                    ComparisonRow row = report.Maneuvers[mi].Rows[ri];
                    if (row.Parameter == normalized && row.OfficialSource == source) return row;
                }
            return null;
        }

        private static ExternalMeasurement Clone(ExternalMeasurement m)
        {
            ExternalMeasurement c = new ExternalMeasurement();
            c.TestId = m.TestId;
            c.ManeuverKey = m.ManeuverKey;
            c.Sequence = m.Sequence;
            c.PrePost = m.PrePost;
            c.RecordedAt = m.RecordedAt;
            c.Parameter = m.Parameter;
            c.Value = m.Value;
            c.Unit = m.Unit;
            c.Source = m.Source;
            c.Precision = m.Precision;
            c.Evidence = m.Evidence;
            c.SameManeuverAttested = m.SameManeuverAttested;
            c.Notes = m.Notes;
            c.SourceRow = m.SourceRow;
            c.RawLine = m.RawLine;
            return c;
        }

        private static bool Near(double? actual, double expected, double tolerance)
        {
            return actual.HasValue && Math.Abs(actual.Value - expected) <= tolerance;
        }

        private static void CheckThrows<T>(Action action, string name) where T : Exception
        {
            try
            {
                action();
                Check(false, name + " (no exception)");
            }
            catch (T)
            {
                Check(true, name);
            }
            catch (Exception ex)
            {
                Check(false, name + " (wrong exception " + ex.GetType().Name + ")");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void Section(string name)
        {
            Console.WriteLine("-- " + name);
        }

        private static void Check(bool condition, string name)
        {
            if (condition) { _pass++; Console.WriteLine("PASS  " + name); }
            else { _fail++; Console.WriteLine("FAIL  " + name); }
        }
    }
}

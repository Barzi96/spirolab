using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MirParityLab.Core
{
    public sealed class CaptureFormatException : Exception
    {
        public CaptureFormatException(string message) : base(message) { }
    }

    public static class CaptureLoader
    {
        public const long MaxInputBytes = 5 * 1024 * 1024;
        public const int MaxPackets = 200000;
        public const int MaxBlockBytes = 200000;

        public static CaptureDocument Load(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new CaptureFormatException("No input file was selected.");
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("Input file not found.", path);
            if (info.Length > MaxInputBytes) throw new CaptureFormatException("Input file exceeds the 5 MiB safety limit.");
            string ext = info.Extension.ToLowerInvariant();
            CaptureDocument doc;
            if (ext == ".json") doc = LoadJson(path);
            else if (ext == ".log" || ext == ".txt") doc = LoadDeviceLog(path);
            else if (ext == ".hex" || ext == ".pkts") doc = LoadHexPackets(path);
            else if (ext == ".bin" || ext == ".mir") doc = LoadOpaque(path);
            else
            {
                string head;
                using (StreamReader reader = new StreamReader(path, Encoding.UTF8, true))
                {
                    char[] buffer = new char[4096];
                    int read = reader.Read(buffer, 0, buffer.Length);
                    head = new string(buffer, 0, read).TrimStart();
                }
                if (head.StartsWith("{", StringComparison.Ordinal)) doc = LoadJson(path);
                else doc = LoadDeviceLog(path);
            }
            doc.LoadedPath = path;
            SourceFileInfo source = new SourceFileInfo();
            source.FileName = Path.GetFileName(path);
            source.ByteLength = info.Length;
            source.Sha256 = Sha256File(path);
            source.Kind = ext.TrimStart('.');
            source.Note = "Imported evidence file. Only the basename and hash are retained in exports.";
            doc.Meta.SourceFiles.Add(source);
            return doc;
        }

        private static CaptureDocument LoadJson(string path)
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = (int)MaxInputBytes;
            object parsed;
            try { parsed = serializer.DeserializeObject(text); }
            catch (Exception ex) { throw new CaptureFormatException("Invalid JSON: " + ex.Message); }
            Dictionary<string, object> root = parsed as Dictionary<string, object>;
            if (root == null) throw new CaptureFormatException("Capture JSON must be an object.");
            Privacy.EnsureNoPatientKeys(root);

            CaptureDocument doc = new CaptureDocument();
            string schema = S(root, "schema");
            if (!String.Equals(schema, "mir-parity-capture/v1", StringComparison.Ordinal))
                throw new CaptureFormatException("Unsupported schema. Expected mir-parity-capture/v1.");
            doc.Meta.TestId = RequireTestId(S(root, "test_id"));
            doc.Meta.DataOrigin = Default(S(root, "data_origin"), "unknown").ToLowerInvariant();
            if (doc.Meta.DataOrigin != "synthetic" && doc.Meta.DataOrigin != "live_deidentified" && doc.Meta.DataOrigin != "unknown")
                throw new CaptureFormatException("data_origin must be synthetic, live_deidentified or unknown.");

            Dictionary<string, object> capture = Obj(root, "capture");
            if (capture != null)
            {
                doc.Meta.RecordedAt = HexUtil.TryDate(S(capture, "recorded_at"));
                doc.Meta.DeviceModel = S(capture, "device_model");
                doc.Meta.DeviceSerial = S(capture, "device_serial");
                doc.Meta.Notes = S(capture, "notes");
            }

            object[] maneuvers = Arr(root, "maneuvers");
            int sequenceFallback = 1;
            if (maneuvers != null)
            {
                for (int i = 0; i < maneuvers.Length; i++)
                {
                    Dictionary<string, object> m = maneuvers[i] as Dictionary<string, object>;
                    if (m == null) throw new CaptureFormatException("maneuvers[" + i + "] must be an object.");
                    ManeuverRecord record = new ManeuverRecord();
                    record.Sequence = I(m, "sequence", sequenceFallback++);
                    record.ManeuverKey = Default(S(m, "maneuver_key"), doc.Meta.TestId + "-M" + record.Sequence.ToString(CultureInfo.InvariantCulture));
                    record.ManeuverKey = HexUtil.SafeToken(record.ManeuverKey, doc.Meta.TestId + "-M" + record.Sequence.ToString(CultureInfo.InvariantCulture));
                    record.PrePost = NormalizePrePost(S(m, "pre_post"));
                    record.RecordedAt = HexUtil.TryDate(S(m, "recorded_at"));
                    record.Association = Default(S(m, "association"), "not_established");
                    record.AssociationEvidence = S(m, "association_evidence");
                    record.StepType = ParseByteOrNull(S(m, "step_type"));
                    if (m.ContainsKey("step") && S(m, "step").Length > 0)
                    {
                        double stepValue;
                        if (!HexUtil.TryDouble(S(m, "step"), out stepValue) || stepValue <= 0)
                            throw new CaptureFormatException("Maneuver step must be a positive number.");
                        record.Step = stepValue;
                    }
                    record.LastBlock = HexUtil.Parse(S(m, "cod_last_hex"));
                    record.CurveBlock = HexUtil.Parse(S(m, "cod_curve_hex"));
                    if (record.LastBlock.Length > MaxBlockBytes) throw new CaptureFormatException("cod_LAST exceeds 200000 bytes.");
                    if (record.CurveBlock.Length > MaxBlockBytes) throw new CaptureFormatException("cod_CURVE exceeds 200000 bytes.");
                    object[] packets = Arr(m, "packets");
                    if (packets != null)
                    {
                        if (packets.Length > MaxPackets) throw new CaptureFormatException("Too many packets.");
                        for (int p = 0; p < packets.Length; p++)
                        {
                            Dictionary<string, object> packet = packets[p] as Dictionary<string, object>;
                            if (packet == null) throw new CaptureFormatException("Packet must be an object.");
                            record.Packets.Add(ParsePacket(packet, p));
                        }
                    }
                    if (record.LastBlock.Length == 0 && record.CurveBlock.Length == 0 && record.Packets.Count == 0)
                        doc.Warnings.Add("Maneuver " + record.ManeuverKey + " has no cod_LAST, cod_CURVE or packets.");
                    doc.Maneuvers.Add(record);
                }
            }

            doc.UnframedBlob = HexUtil.Parse(S(root, "raw_blob_hex"));
            doc.BlobKind = S(root, "raw_blob_kind");
            if (doc.UnframedBlob.Length > MaxInputBytes) throw new CaptureFormatException("raw_blob_hex exceeds the input limit.");
            if (doc.Maneuvers.Count == 0 && doc.UnframedBlob.Length == 0)
                throw new CaptureFormatException("Capture contains no maneuvers and no raw blob.");

            if (doc.Meta.DataOrigin != "synthetic")
                doc.Warnings.Add("This capture is not marked synthetic. Do not infer MIR parity without paired evidence.");
            if (doc.Maneuvers.Count > 1)
                doc.Warnings.Add("Multiple maneuvers are present. Each official row must be matched with its own maneuver key/evidence.");
            return doc;
        }

        private static PacketRecord ParsePacket(Dictionary<string, object> packet, int index)
        {
            PacketRecord record = new PacketRecord();
            record.Index = I(packet, "index", index);
            if (packet.ContainsKey("seconds") && S(packet, "seconds").Length > 0)
            {
                double seconds;
                if (!HexUtil.TryDouble(S(packet, "seconds"), out seconds) || seconds < 0)
                    throw new CaptureFormatException("Packet seconds must be non-negative.");
                record.Seconds = seconds;
            }
            record.Type = ParseByteOrNull(S(packet, "type"));
            if (packet.ContainsKey("raw") && S(packet, "raw").Length > 0)
            {
                int raw;
                if (!Int32.TryParse(S(packet, "raw"), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw) || raw < 0 || raw > 65535)
                    throw new CaptureFormatException("Packet raw must be 0..65535.");
                record.Raw = raw;
            }
            string hexText = S(packet, "hex");
            if (hexText.Length > 0)
            {
                record.RawBytes = HexUtil.Parse(hexText);
                if (record.RawBytes.Length > 4096) throw new CaptureFormatException("A single packet exceeds 4096 bytes.");
                if (record.RawBytes.Length >= 3)
                {
                    if (!record.Type.HasValue) record.Type = record.RawBytes[0];
                    if (!record.Raw.HasValue) record.Raw = (record.RawBytes[1] << 8) | record.RawBytes[2];
                }
            }
            else
            {
                if (!record.Type.HasValue || !record.Raw.HasValue)
                    throw new CaptureFormatException("Packet needs either hex or both type and raw.");
                record.RawBytes = new byte[] { record.Type.Value, (byte)(record.Raw.Value >> 8), (byte)(record.Raw.Value & 0xFF) };
            }
            record.Notes = S(packet, "notes");
            record.SourceLine = S(packet, "source_line");
            return record;
        }

        private static CaptureDocument LoadDeviceLog(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            Privacy.EnsureNoPatientText(lines);
            CaptureDocument doc = new CaptureDocument();
            doc.Meta.DataOrigin = "unknown";
            string testId = "";
            string association = "";
            List<byte[]> blocks = new List<byte[]>();
            List<PacketRecord> packets = new List<PacketRecord>();
            int curveLength = 0;
            Regex packetRx = new Regex(@"packet\s+(?<t>[0-9]+(?:[.,][0-9]+)?)s\s+(?<type>[0-9A-Fa-f]{2})\s+(?<raw>[0-9]+)", RegexOptions.CultureInvariant);
            Regex lastRx = new Regex(@"cod_LAST\s+\d+\s+bytes:\s*(?<hex>(?:[0-9A-Fa-f]{2}[ \t]*)+)", RegexOptions.CultureInvariant);
            Regex testRx = new Regex(@"(?:test_id|test-id|test id)\s*[=:]\s*(?<id>[A-Za-z0-9._-]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Regex assocRx = new Regex(@"association\s*[=:]\s*(?<a>[A-Za-z0-9_]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            Regex curveRx = new Regex(@"cod_CURVE\s+(?<n>\d+)\s+bytes", RegexOptions.CultureInvariant);
            int unrecognized = 0;
            for (int lineNo = 0; lineNo < lines.Length; lineNo++)
            {
                string line = lines[lineNo];
                Match m = packetRx.Match(line);
                if (m.Success)
                {
                    double seconds;
                    int raw;
                    if (HexUtil.TryDouble(m.Groups["t"].Value, out seconds) &&
                        Int32.TryParse(m.Groups["raw"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw) &&
                        raw >= 0 && raw <= 65535 && packets.Count < MaxPackets)
                    {
                        byte type = Byte.Parse(m.Groups["type"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        PacketRecord p = new PacketRecord();
                        p.Index = packets.Count;
                        p.Seconds = seconds;
                        p.Type = type;
                        p.Raw = raw;
                        p.RawBytes = new byte[] { type, (byte)(raw >> 8), (byte)(raw & 0xFF) };
                        p.SourceLine = "line " + (lineNo + 1);
                        packets.Add(p);
                        continue;
                    }
                }
                m = lastRx.Match(line);
                if (m.Success)
                {
                    byte[] block = HexUtil.Parse(m.Groups["hex"].Value);
                    if (block.Length > 0 && block.Length <= MaxBlockBytes) blocks.Add(block);
                    continue;
                }
                m = testRx.Match(line);
                if (m.Success) { testId = m.Groups["id"].Value; continue; }
                m = assocRx.Match(line);
                if (m.Success) { association = m.Groups["a"].Value; continue; }
                m = curveRx.Match(line);
                if (m.Success) { Int32.TryParse(m.Groups["n"].Value, out curveLength); continue; }
                if (line.Trim().Length > 0 && !line.Contains("=== connect ===") && !line.Contains("=== disconnect ==="))
                    unrecognized++;
            }

            testId = HexUtil.SafeToken(testId, HexUtil.SafeToken(Path.GetFileNameWithoutExtension(path), "CAP-" + ShortHash(path)));
            doc.Meta.TestId = RequireTestId(testId);
            doc.Meta.RecordedAt = File.GetLastWriteTimeUtc(path);
            doc.Meta.Notes = "Imported from a device text log. The log is not a formal MIR export.";
            if (packets.Count == 0 && blocks.Count == 0)
                throw new CaptureFormatException("No packet or cod_LAST records were recognized in the log.");
            if (curveLength > 0)
                doc.Warnings.Add("The log contains only a cod_CURVE byte count (" + curveLength + "), not curve bytes. Stored-device curve data is unavailable.");
            if (unrecognized > 0)
                doc.Warnings.Add(unrecognized + " non-empty log line(s) were not interpreted; the original file hash is retained.");
            if (blocks.Count > 1)
                doc.Warnings.Add("Multiple cod_LAST blocks were found. They cannot be tied to individual packet streams from this log; packets are left unassigned.");

            if (blocks.Count == 0)
            {
                ManeuverRecord only = new ManeuverRecord();
                only.ManeuverKey = doc.Meta.TestId + "-M1";
                only.Sequence = 1;
                only.PrePost = "UNKNOWN";
                only.RecordedAt = doc.Meta.RecordedAt;
                only.Association = association.Length == 0 ? "not_established" : association;
                only.AssociationEvidence = "Imported log metadata only; no independent capture token.";
                only.Packets = packets;
                doc.Maneuvers.Add(only);
            }
            else
            {
                for (int i = 0; i < blocks.Count; i++)
                {
                    ManeuverRecord record = new ManeuverRecord();
                    record.ManeuverKey = doc.Meta.TestId + "-M" + (i + 1);
                    record.Sequence = i + 1;
                    record.PrePost = "UNKNOWN";
                    record.RecordedAt = doc.Meta.RecordedAt;
                    record.Association = association.Length == 0 ? "unverified" : association;
                    record.AssociationEvidence = "Imported log block; no device-level maneuver identifier.";
                    record.LastBlock = blocks[i];
                    if (blocks.Count == 1) record.Packets = packets;
                    doc.Maneuvers.Add(record);
                }
            }
            doc.Warnings.Add("Host timestamps and log association are not certified. No parity claim is possible without pairing evidence.");
            doc.Warnings.Add("The log is marked data_origin=unknown; de-identification and live origin must be confirmed by the operator.");
            return doc;
        }

        private static CaptureDocument LoadHexPackets(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            CaptureDocument doc = new CaptureDocument();
            doc.Meta.TestId = RequireTestId(HexUtil.SafeToken(Path.GetFileNameWithoutExtension(path), "CAP-" + ShortHash(path)));
            doc.Meta.DataOrigin = "unknown";
            ManeuverRecord record = new ManeuverRecord();
            record.ManeuverKey = doc.Meta.TestId + "-M1";
            record.Sequence = 1;
            record.PrePost = "UNKNOWN";
            record.StepType = 0x40;
            record.Step = 0.01;
            record.Association = "not_established";
            record.AssociationEvidence = "Raw packet text file; no capture token.";
            Regex triples = new Regex(@"^(?:0x)?(?<t>[0-9A-Fa-f]{2})[\s,;:-]+(?:0x)?(?<r>[0-9A-Fa-f]{4})$", RegexOptions.CultureInvariant);
            Regex compact = new Regex(@"^(?:0x)?(?<h>[0-9A-Fa-f]{6})$", RegexOptions.CultureInvariant);
            int bad = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                Match m = triples.Match(line);
                if (!m.Success) m = compact.Match(line);
                if (!m.Success) { bad++; continue; }
                byte[] raw = HexUtil.Parse(line);
                if (raw.Length != 3) { bad++; continue; }
                PacketRecord p = new PacketRecord();
                p.Index = record.Packets.Count;
                p.Seconds = record.Packets.Count * 0.01;
                p.Type = raw[0];
                p.Raw = (raw[1] << 8) | raw[2];
                p.RawBytes = raw;
                p.SourceLine = "line " + (i + 1);
                record.Packets.Add(p);
            }
            if (record.Packets.Count == 0)
                throw new CaptureFormatException("No 3-byte packets were recognized. Use lines like '10 1388' or '101388'.");
            doc.Maneuvers.Add(record);
            if (bad > 0)
            {
                doc.Warnings.Add(bad + " line(s) were not recognized and were not converted into packets.");
                doc.UnframedBlob = File.ReadAllBytes(path);
                doc.BlobKind = "unparsed_hex_text";
            }
            doc.Warnings.Add("A raw packet text file has no certified step, time origin or association. Values derived from it are unverified.");
            return doc;
        }

        private static CaptureDocument LoadOpaque(string path)
        {
            FileInfo info = new FileInfo(path);
            byte[] bytes = File.ReadAllBytes(path);
            CaptureDocument doc = new CaptureDocument();
            doc.Meta.TestId = RequireTestId(HexUtil.SafeToken(Path.GetFileNameWithoutExtension(path), "CAP-" + ShortHash(path)));
            doc.Meta.DataOrigin = "unknown";
            doc.Meta.Notes = "Opaque file; only a hash, length and byte display are retained. No framing, decoding or semantics are inferred.";
            doc.UnframedBlob = bytes;
            doc.OpaqueArchive = String.Equals(info.Extension, ".mir", StringComparison.OrdinalIgnoreCase);
            doc.BlobKind = doc.OpaqueArchive ? "opaque_mir_archive_not_parsed" : "unframed_binary_blob";
            doc.Warnings.Add(doc.OpaqueArchive
                ? "The .mir file is treated as an opaque proprietary archive, NOT as raw USB data. It is not decoded."
                : "The binary file has no certified framing. It is preserved as one opaque blob; no packets are guessed.");
            return doc;
        }

        public static string Sha256File(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("X2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static string ShortHash(string path)
        {
            string hash;
            try { hash = Sha256File(path); } catch { return "HASHERR"; }
            return hash.Substring(0, 8);
        }

        private static string RequireTestId(string testId)
        {
            Privacy.ValidateTestId(testId);
            return testId.Trim();
        }

        private static string NormalizePrePost(string value)
        {
            string s = (value ?? "").Trim().ToUpperInvariant();
            if (s == "PRE" || s == "POST") return s;
            return "UNKNOWN";
        }

        private static byte? ParseByteOrNull(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            string s = text.Trim();
            int value;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!Int32.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)) throw new CaptureFormatException("Invalid byte: " + text);
            }
            else if (!Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                throw new CaptureFormatException("Invalid byte: " + text);
            if (value < 0 || value > 255) throw new CaptureFormatException("Byte outside 0..255: " + text);
            return (byte)value;
        }

        private static string Default(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim(); }

        private static string S(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key) || map[key] == null) return "";
            return Convert.ToString(map[key], CultureInfo.InvariantCulture);
        }

        private static int I(Dictionary<string, object> map, string key, int fallback)
        {
            int value;
            string text = S(map, key);
            return Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key)) return null;
            return map[key] as Dictionary<string, object>;
        }

        private static object[] Arr(Dictionary<string, object> map, string key)
        {
            if (map == null || !map.ContainsKey(key)) return null;
            return map[key] as object[];
        }
    }
}

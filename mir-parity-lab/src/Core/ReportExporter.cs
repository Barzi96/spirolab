using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace MirParityLab.Core
{
    public static class ReportExporter
    {
        public static readonly string[] Limitations = new string[]
        {
            "Phase 1 is offline: the tool contains no USB transport and sends no command to any device.",
            "cod_LAST offsets and conversions are observed source-level claims, not MIR-certified protocol documentation.",
            "No device-level maneuver identifier is known. Association depends on the supplied capture token or an explicit operator attestation.",
            "A repeated or stale cod_LAST block can only be rejected when it is byte-identical; changed stale data cannot be detected from the block alone.",
            "cod_CURVE is preserved raw but has no decoder in the reviewed source. Software values in this tool are independent reconstructions, not SpiroLab or MIR output.",
            "The .mir archive format is treated as opaque and is never equated with raw USB capture data.",
            "Official MIR manuals are distributed through MyMIR login; no public Spirolab service manual or USB protocol specification was obtained.",
            "The Windows SDK is not openly downloadable: MIR's brochure requires a license agreement and a minimum device order.",
            "The reviewed PacketDecoder.cs uses raw - 65535 for the negative-flow branch, one unit away from conventional two's complement; the tool records this as unverified source evidence.",
            "No BTPS, calibration, leak, cough, acceptability or clinical-validity claim is established by a numerical difference.",
            "ATS/ERS between-maneuver repeatability is not used as an allowed error between two software products for the same maneuver.",
            "Synthetic fixtures test plumbing and arithmetic only; they are not MIR measurements and cannot establish parity."
        };

        public static void ExportCsv(ComparisonReport report, string path)
        {
            StringBuilder sb = new StringBuilder();
            string[] headers = new string[] {
                "test_id","data_origin","maneuver_key","parameter","unit",
                "official_value","official_source","official_precision",
                "decoded_value","decoded_provenance","decoded_raw_hex","decoded_offset",
                "software_value","software_method","spirolab_value",
                "official_vs_decoded_abs","official_vs_decoded_rel_pct",
                "official_vs_software_abs","official_vs_software_rel_pct",
                "spirolab_vs_decoded_abs","spirolab_vs_decoded_rel_pct",
                "tolerance_abs","tolerance_basis","status","software_status",
                "same_maneuver","match_evidence","reason","probable_cause","claim"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Csv(headers[i]));
            }
            sb.AppendLine();
            for (int mi = 0; mi < report.Maneuvers.Count; mi++)
            {
                ManeuverComparison mc = report.Maneuvers[mi];
                for (int ri = 0; ri < mc.Rows.Count; ri++)
                {
                    ComparisonRow row = mc.Rows[ri];
                    string[] values = new string[] {
                        report.Capture.Meta.TestId, report.Capture.Meta.DataOrigin, row.ManeuverKey, row.Parameter, row.Unit,
                        Num(row.OfficialValue), MeasurementSourceText.Code(row.OfficialSource), Num(row.OfficialPrecision),
                        Num(row.DecodedValue), ProvenanceText.Code(row.DecodedProvenance), row.DecodedRawHex, row.DecodedOffset,
                        Num(row.SoftwareValue), row.SoftwareMethod, Num(row.SpirolabValue),
                        Num(row.OfficialVsDecodedAbs), Num(row.OfficialVsDecodedRelPct),
                        Num(row.OfficialVsSoftwareAbs), Num(row.OfficialVsSoftwareRelPct),
                        Num(row.SpirolabVsDecodedAbs), Num(row.SpirolabVsDecodedRelPct),
                        Num(row.ToleranceAbs), row.ToleranceBasis, ComparisonText.Code(row.Status), ComparisonText.Code(row.SoftwareStatus),
                        row.SameManeuver ? "true" : "false", row.MatchEvidence, row.Reason, row.ProbableCause, report.Claim
                    };
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(Csv(values[i]));
                    }
                    sb.AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        public static string BuildJson(ComparisonReport report)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["schema"] = "mir-parity-report/v1";
            root["generated_at_utc"] = report.GeneratedAtUtc.ToString("o", CultureInfo.InvariantCulture);
            root["claim"] = report.Claim;
            root["phase"] = "phase_1_offline";
            root["capture"] = CaptureJson(report);
            List<object> maneuvers = new List<object>();
            for (int mi = 0; mi < report.Maneuvers.Count; mi++) maneuvers.Add(ManeuverJson(report, report.Maneuvers[mi]));
            root["maneuvers"] = maneuvers;
            root["warnings"] = Strings(report.Warnings);
            root["limitations"] = Strings(Limitations);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 64 * 1024 * 1024;
            return TrimLineEnds(PrettyJson(serializer.Serialize(root)));
        }

        private static Dictionary<string, object> CaptureJson(ComparisonReport report)
        {
            CaptureMeta meta = report.Capture.Meta;
            Dictionary<string, object> capture = new Dictionary<string, object>();
            capture["test_id"] = meta.TestId;
            capture["data_origin"] = meta.DataOrigin;
            capture["recorded_at"] = HexUtil.Iso(meta.RecordedAt);
            capture["device_model"] = meta.DeviceModel;
            capture["device_serial"] = meta.DeviceSerial;
            capture["notes"] = meta.Notes;
            capture["source_file_name"] = meta.SourceFiles.Count > 0 ? meta.SourceFiles[0].FileName : "";
            capture["source_sha256"] = meta.SourceFiles.Count > 0 ? MetaHash(meta) : "";
            capture["opaque_archive"] = report.Capture.OpaqueArchive;
            capture["blob_kind"] = report.Capture.BlobKind;
            capture["blob_length"] = report.Capture.UnframedBlob == null ? 0 : report.Capture.UnframedBlob.Length;
            List<object> sources = new List<object>();
            for (int i = 0; i < meta.SourceFiles.Count; i++)
            {
                Dictionary<string, object> source = new Dictionary<string, object>();
                source["file_name"] = meta.SourceFiles[i].FileName;
                source["sha256"] = meta.SourceFiles[i].Sha256;
                source["byte_length"] = meta.SourceFiles[i].ByteLength;
                source["kind"] = meta.SourceFiles[i].Kind;
                source["note"] = meta.SourceFiles[i].Note;
                sources.Add(source);
            }
            capture["source_files"] = sources;
            capture["warnings"] = Strings(report.Capture.Warnings);
            return capture;
        }

        private static string MetaHash(CaptureMeta meta)
        {
            for (int i = 0; i < meta.SourceFiles.Count; i++)
                if (!String.IsNullOrEmpty(meta.SourceFiles[i].Sha256)) return meta.SourceFiles[i].Sha256;
            return "";
        }

        private static Dictionary<string, object> ManeuverJson(ComparisonReport report, ManeuverComparison mc)
        {
            Dictionary<string, object> m = new Dictionary<string, object>();
            m["maneuver_key"] = mc.Maneuver.ManeuverKey;
            m["sequence"] = mc.Maneuver.Sequence;
            m["pre_post"] = mc.Maneuver.PrePost;
            m["recorded_at"] = HexUtil.Iso(mc.Maneuver.RecordedAt);
            m["association"] = mc.Maneuver.Association;
            m["association_evidence"] = mc.Maneuver.AssociationEvidence;
            m["cod_last_length"] = mc.Maneuver.LastBlock == null ? 0 : mc.Maneuver.LastBlock.Length;
            m["cod_curve_length"] = mc.Maneuver.CurveBlock == null ? 0 : mc.Maneuver.CurveBlock.Length;
            m["decoded"] = DecodedJson(mc.Decoded);
            m["calculated"] = CalculatedJson(mc.Calculated);
            m["packets"] = PacketsJson(mc.Maneuver);
            m["comparisons"] = RowsJson(mc.Rows);
            return m;
        }

        private static Dictionary<string, object> DecodedJson(DecodeResult decoded)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["block_length"] = decoded.BlockLength;
            d["minimum_length_met"] = decoded.MinimumLengthMet;
            d["core_fields_valid"] = decoded.CoreFieldsValid;
            d["block_valid"] = decoded.BlockValid;
            d["association_capture_bound"] = decoded.AssociationCaptureBound;
            d["association"] = decoded.Association;
            d["coverage_complete"] = decoded.CoverageComplete;
            List<object> fields = new List<object>();
            for (int i = 0; i < decoded.Fields.Count; i++)
            {
                DecodedField f = decoded.Fields[i];
                Dictionary<string, object> field = new Dictionary<string, object>();
                field["key"] = f.Key;
                field["label"] = f.Label;
                field["offset"] = "0x" + f.Offset.ToString("X2");
                field["size"] = f.Size;
                field["raw_hex"] = HexUtil.ToSpacedHex(f.RawBytes);
                field["raw_unsigned"] = f.RawUnsigned;
                field["value"] = f.Value;
                field["unit"] = f.Unit;
                field["conversion"] = f.Conversion;
                field["resolution"] = f.Resolution;
                field["provenance"] = ProvenanceText.Code(f.Provenance);
                field["evidence"] = f.Evidence;
                field["note"] = f.Note;
                field["available"] = f.Available;
                field["field_present"] = f.FieldPresent;
                field["clinical_use_allowed"] = f.ClinicalUseAllowed;
                fields.Add(field);
            }
            d["fields"] = fields;
            List<object> unknown = new List<object>();
            for (int i = 0; i < decoded.UnknownRanges.Count; i++)
            {
                Dictionary<string, object> u = new Dictionary<string, object>();
                u["offset"] = "0x" + decoded.UnknownRanges[i].Offset.ToString("X2");
                u["size"] = decoded.UnknownRanges[i].Size;
                u["raw_hex"] = HexUtil.ToSpacedHex(decoded.UnknownRanges[i].RawBytes);
                u["provenance"] = "unknown";
                u["note"] = decoded.UnknownRanges[i].Note;
                unknown.Add(u);
            }
            d["unknown_ranges"] = unknown;
            d["warnings"] = Strings(decoded.Warnings);
            return d;
        }

        private static Dictionary<string, object> CalculatedJson(CalculatedSet set)
        {
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["available"] = set.Available;
            c["method"] = set.Method;
            c["provenance"] = ProvenanceText.Code(set.Provenance);
            c["sample_count"] = set.SampleCount;
            Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (KeyValuePair<string, double> pair in set.Values) values[pair.Key] = pair.Value;
            c["values"] = values;
            c["assumptions"] = Strings(set.Assumptions);
            c["warnings"] = Strings(set.Warnings);
            return c;
        }

        private static List<object> PacketsJson(ManeuverRecord maneuver)
        {
            List<object> list = new List<object>();
            for (int i = 0; i < maneuver.Packets.Count; i++)
            {
                PacketRecord p = maneuver.Packets[i];
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["index"] = p.Index;
                item["seconds"] = p.Seconds;
                item["type"] = p.Type.HasValue ? "0x" + p.Type.Value.ToString("X2") : "";
                item["raw"] = p.Raw;
                item["length"] = p.Length;
                item["raw_hex"] = HexUtil.ToSpacedHex(p.RawBytes);
                item["decode"] = ProtocolDecoder.DecodePacket(p);
                item["source_line"] = p.SourceLine;
                item["notes"] = p.Notes;
                list.Add(item);
            }
            return list;
        }

        private static List<object> RowsJson(List<ComparisonRow> rows)
        {
            List<object> list = new List<object>();
            for (int i = 0; i < rows.Count; i++)
            {
                ComparisonRow r = rows[i];
                Dictionary<string, object> row = new Dictionary<string, object>();
                row["parameter"] = r.Parameter;
                row["unit"] = r.Unit;
                row["official_value"] = r.OfficialValue;
                row["official_source"] = MeasurementSourceText.Code(r.OfficialSource);
                row["official_precision"] = r.OfficialPrecision;
                row["decoded_value"] = r.DecodedValue;
                row["decoded_provenance"] = ProvenanceText.Code(r.DecodedProvenance);
                row["decoded_raw_hex"] = r.DecodedRawHex;
                row["decoded_offset"] = r.DecodedOffset;
                row["decoded_note"] = r.DecodedNote;
                row["software_value"] = r.SoftwareValue;
                row["software_method"] = r.SoftwareMethod;
                row["spirolab_value"] = r.SpirolabValue;
                row["spirolab_evidence"] = r.SpirolabEvidence;
                row["official_vs_decoded_abs"] = r.OfficialVsDecodedAbs;
                row["official_vs_decoded_rel_pct"] = r.OfficialVsDecodedRelPct;
                row["official_vs_software_abs"] = r.OfficialVsSoftwareAbs;
                row["official_vs_software_rel_pct"] = r.OfficialVsSoftwareRelPct;
                row["spirolab_vs_decoded_abs"] = r.SpirolabVsDecodedAbs;
                row["spirolab_vs_decoded_rel_pct"] = r.SpirolabVsDecodedRelPct;
                row["tolerance_abs"] = r.ToleranceAbs;
                row["tolerance_basis"] = r.ToleranceBasis;
                row["status"] = ComparisonText.Code(r.Status);
                row["software_status"] = ComparisonText.Code(r.SoftwareStatus);
                row["same_maneuver"] = r.SameManeuver;
                row["match_evidence"] = r.MatchEvidence;
                row["reason"] = r.Reason;
                row["probable_cause"] = r.ProbableCause;
                list.Add(row);
            }
            return list;
        }

        public static void ExportMarkdown(ComparisonReport report, string path)
        {
            File.WriteAllText(path, BuildMarkdown(report), new UTF8Encoding(false));
        }

        public static string BuildMarkdown(ComparisonReport report)
        {
            StringBuilder sb = new StringBuilder();
            CaptureMeta meta = report.Capture.Meta;
            sb.AppendLine("# گزارش پژوهشی MIR Parity Lab — فاز ۱");
            sb.AppendLine();
            sb.AppendLine("> **هشدار روش‌شناختی:** این گزارش ادعایی درباره برابری واقعی با MIR، اعتبار بالینی، کالیبراسیون یا BTPS نمی‌کند. فاز ۱ فقط برای بررسی ساختار بسته، منشأ داده و محاسبه اختلاف است.");
            sb.AppendLine();
            sb.AppendLine("- شناسه آزمایش غیرهویتی: `" + Md(meta.TestId) + "`");
            sb.AppendLine("- منشأ داده: `" + Md(meta.DataOrigin) + "`");
            sb.AppendLine("- زمان ثبت: `" + Md(HexUtil.Iso(meta.RecordedAt)) + "`");
            sb.AppendLine("- مدل دستگاه: `" + Md(meta.DeviceModel) + "`");
            sb.AppendLine("- فایل مبدأ: `" + Md(meta.SourceFiles.Count > 0 ? meta.SourceFiles[0].FileName : "") + "`");
            sb.AppendLine("- SHA-256 فایل مبدأ: `" + Md(MetaHash(meta)) + "`");
            sb.AppendLine("- زمان تولید گزارش (UTC): `" + report.GeneratedAtUtc.ToString("o", CultureInfo.InvariantCulture) + "`");
            sb.AppendLine("- تعداد ردیف مقایسه: **" + report.RowCount.ToString(CultureInfo.InvariantCulture) + "**");
            sb.AppendLine();
            sb.AppendLine("## خلاصه اجرایی");
            sb.AppendLine();
            int within = 0, outside = 0, identity = 0, noRef = 0, other = 0;
            for (int mi = 0; mi < report.Maneuvers.Count; mi++)
                for (int ri = 0; ri < report.Maneuvers[mi].Rows.Count; ri++)
                {
                    ComparisonState s = report.Maneuvers[mi].Rows[ri].Status;
                    if (s == ComparisonState.WithinRoundingTolerance) within++;
                    else if (s == ComparisonState.OutsideRoundingTolerance) outside++;
                    else if (s == ComparisonState.IdentityNotEstablished) identity++;
                    else if (s == ComparisonState.NoMirReference) noRef++;
                    else other++;
                }
            sb.AppendLine("- داخل تلورانس گردکردن: **" + within + "**");
            sb.AppendLine("- خارج از تلورانس گردکردن: **" + outside + "**");
            sb.AppendLine("- فاقد شواهد کافی برای همان مانور: **" + identity + "**");
            sb.AppendLine("- فاقد مقدار رسمی MIR: **" + noRef + "**");
            sb.AppendLine("- سایر/غیرقابل‌مقایسه: **" + other + "**");
            sb.AppendLine();
            if (meta.DataOrigin == "synthetic")
                sb.AppendLine("> داده این اجرا **مصنوعی** است. ردیف‌های این گزارش نتیجه MIR نیستند.");
            sb.AppendLine();

            if (report.Capture.Warnings.Count > 0)
            {
                sb.AppendLine("## هشدارهای ورودی");
                sb.AppendLine();
                for (int i = 0; i < report.Capture.Warnings.Count; i++) sb.AppendLine("- " + Md(report.Capture.Warnings[i]));
                sb.AppendLine();
            }

            for (int mi = 0; mi < report.Maneuvers.Count; mi++)
            {
                ManeuverComparison mc = report.Maneuvers[mi];
                sb.AppendLine("## مانور `" + Md(mc.Maneuver.ManeuverKey) + "`");
                sb.AppendLine();
                sb.AppendLine("- ترتیب: " + mc.Maneuver.Sequence.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("- PRE/POST: `" + Md(mc.Maneuver.PrePost) + "`");
                sb.AppendLine("- association: `" + Md(mc.Maneuver.Association) + "`");
                sb.AppendLine("- شاهد انتساب: " + Md(mc.Maneuver.AssociationEvidence));
                sb.AppendLine("- طول cod_LAST: " + mc.Maneuver.LastBlock.Length + " بایت");
                sb.AppendLine("- طول cod_CURVE: " + mc.Maneuver.CurveBlock.Length + " بایت (فقط ذخیره خام)");
                sb.AppendLine("- تعداد بسته زنده: " + mc.Maneuver.Packets.Count);
                sb.AppendLine();

                sb.AppendLine("### فیلدهای رمزگشایی‌شده");
                sb.AppendLine();
                sb.AppendLine("| فیلد | آفست | اندازه | خام | مقدار | واحد | تبدیل | منشأ | وضعیت |");
                sb.AppendLine("|---|---:|---:|---|---:|---|---|---|---|");
                for (int fi = 0; fi < mc.Decoded.Fields.Count; fi++)
                {
                    DecodedField f = mc.Decoded.Fields[fi];
                    sb.AppendLine("| " + Md(f.Key) + " | 0x" + f.Offset.ToString("X2") + " | " + f.Size + " | `" +
                        Md(HexUtil.ToSpacedHex(f.RawBytes)) + "` | " + Md(Num(f.Value)) + " | " + Md(f.Unit) + " | " +
                        Md(f.Conversion) + " | " + Md(ProvenanceText.Label(f.Provenance)) + " | " + Md(f.Note) + " |");
                }
                if (mc.Decoded.Fields.Count == 0) sb.AppendLine("| — | — | — | — | — | — | — | — | فیلد شناخته‌شده‌ای موجود نیست |");
                sb.AppendLine();

                sb.AppendLine("### بایت‌های ناشناخته (بدون حدس معنا)");
                sb.AppendLine();
                if (mc.Decoded.UnknownRanges.Count == 0) sb.AppendLine("همه بایت‌ها توسط فیلدهای مشاهده‌شده پوشش داده شده‌اند.");
                else
                {
                    sb.AppendLine("| آفست | اندازه | خام | منشأ |");
                    sb.AppendLine("|---|---:|---|---|");
                    for (int ui = 0; ui < mc.Decoded.UnknownRanges.Count; ui++)
                    {
                        UnknownRange u = mc.Decoded.UnknownRanges[ui];
                        sb.AppendLine("| 0x" + u.Offset.ToString("X2") + " | " + u.Size + " | `" + Md(HexUtil.ToSpacedHex(u.RawBytes)) + "` | Unknown |");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("### مقایسه");
                sb.AppendLine();
                if (mc.Rows.Count == 0) sb.AppendLine("مقدار رسمی یا مقدار قابل‌رمزگشایی برای مقایسه وارد نشده است.");
                else
                {
                    sb.AppendLine("| پارامتر | رسمی | منبع رسمی | خوانده‌شده | منشأ خوانده‌شده | نرم‌افزاری | اختلاف رسمی-خوانده | اختلاف رسمی-نرم‌افزار | وضعیت نرم‌افزاری | تلورانس | وضعیت | دلیل |");
                    sb.AppendLine("|---|---:|---|---:|---|---:|---:|---:|---|---:|---|---|");
                    for (int ri = 0; ri < mc.Rows.Count; ri++)
                    {
                        ComparisonRow r = mc.Rows[ri];
                        sb.AppendLine("| " + Md(Parameters.DisplayName(r.Parameter)) + " | " + Md(Num(r.OfficialValue)) + " " + Md(r.Unit) +
                            " | " + Md(MeasurementSourceText.Code(r.OfficialSource)) + " | " + Md(Num(r.DecodedValue)) + " | " +
                            Md(ProvenanceText.Label(r.DecodedProvenance)) + " | " + Md(Num(r.SoftwareValue)) + " | " +
                            Md(Num(r.OfficialVsDecodedAbs)) + " | " + Md(Num(r.OfficialVsSoftwareAbs)) + " | " +
                            Md(ComparisonText.Code(r.SoftwareStatus)) + " | " + Md(Num(r.ToleranceAbs)) + " | " +
                            Md(ComparisonText.Code(r.Status)) + " | " + Md(r.Reason) + " |");
                    }
                }
                sb.AppendLine();
                sb.AppendLine("> وضعیت نرم‌افزاری فقط با نصف دقت خروجی رسمی سنجیده می‌شود؛ مقدار بازسازی‌شده دقت خروجی مستند و تأییدشده ندارد. تلورانس ستون قبل مربوط به مقدار خوانده‌شده دستگاه است.");
                sb.AppendLine();
                if (mc.Calculated.Available)
                {
                    sb.AppendLine("**روش مقدار نرم‌افزاری:** `" + Md(mc.Calculated.Method) + "` — این مقدار بازسازی مستقل Parity Lab است، نه خروجی SpiroLab یا MIR.");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("## حدود اثبات و محدودیت‌ها");
            sb.AppendLine();
            for (int i = 0; i < Limitations.Length; i++) sb.AppendLine("- " + Md(Limitations[i]));
            sb.AppendLine();
            sb.AppendLine("## نتیجه‌گیری فاز ۱");
            sb.AppendLine();
            sb.AppendLine("این گزارش فقط شواهد ساختاری و اختلاف عددی قابل‌ردیابی را ثبت می‌کند. برای ادعای تطابق با MIR باید مانور واحد با شواهد غیرهویتی کافی، خروجی رسمی دستگاه/نرم‌افزار MIR، و در صورت امکان SDK رسمی مقایسه شود.");
            return sb.ToString();
        }

        private static List<string> Strings(List<string> source)
        {
            List<string> result = new List<string>();
            if (source != null) for (int i = 0; i < source.Count; i++) result.Add(source[i]);
            return result;
        }

        private static List<string> Strings(string[] source)
        {
            List<string> result = new List<string>();
            if (source != null) for (int i = 0; i < source.Length; i++) result.Add(source[i]);
            return result;
        }

        private static string Num(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.############", CultureInfo.InvariantCulture) : "";
        }

        private static string Csv(string value)
        {
            string s = value ?? "";
            if (s.IndexOf('"') >= 0) s = s.Replace("\"", "\"\"");
            if (s.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0) s = "\"" + s + "\"";
            return s;
        }

        private static string Md(string value)
        {
            return (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string TrimLineEnds(string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(lines[i].TrimEnd(' ', '\t', '\r'));
            }
            return sb.ToString();
        }

        // Indents already-valid JSON without changing string contents.
        private static string PrettyJson(string json)
        {
            StringBuilder sb = new StringBuilder(json.Length + 1024);
            bool inString = false, escape = false;
            int indent = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; sb.Append(c); }
                else if (c == '{' || c == '[')
                {
                    sb.Append(c); sb.AppendLine(); indent++; Indent(sb, indent);
                }
                else if (c == '}' || c == ']')
                {
                    sb.AppendLine(); indent--; Indent(sb, indent); sb.Append(c);
                }
                else if (c == ',') { sb.Append(c); sb.AppendLine(); Indent(sb, indent); }
                else if (c == ':') { sb.Append(": "); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int count)
        {
            for (int i = 0; i < count; i++) sb.Append("  ");
        }
    }
}

using System;
using System.Collections.Generic;

namespace MirParityLab.Core
{
    public static class ProtocolDecoder
    {
        public static DecodeResult DecodeLast(byte[] block, ManeuverRecord maneuver)
        {
            DecodeResult result = new DecodeResult();
            result.RawBlock = block == null ? new byte[0] : (byte[])block.Clone();
            result.BlockLength = result.RawBlock.Length;
            result.Association = maneuver == null ? "not_established" : (maneuver.Association ?? "");
            result.AssociationEvidence = maneuver == null ? "" : (maneuver.AssociationEvidence ?? "");
            result.AssociationCaptureBound = String.Equals(result.Association, "capture_bound_immediate_single_read", StringComparison.OrdinalIgnoreCase);
            result.MinimumLengthMet = result.BlockLength >= ProtocolCatalog.LastMinimumLength;

            if (result.BlockLength == 0)
            {
                result.Warnings.Add("No cod_LAST block was supplied.");
                return result;
            }
            if (!result.MinimumLengthMet)
                result.Warnings.Add("cod_LAST is shorter than the observed 0x1C-byte minimum. Fields are shown only as unverified raw evidence.");

            bool[] covered = new bool[result.BlockLength];
            for (int i = 0; i < ProtocolCatalog.LastFields.Length; i++)
            {
                FieldDefinition def = ProtocolCatalog.LastFields[i];
                if (result.BlockLength >= def.MinimumBlockLength && def.Offset + def.Size <= result.BlockLength)
                {
                    DecodedField field = DecodeField(def, result.RawBlock);
                    field.ClinicalUseAllowed = false;
                    result.Fields.Add(field);
                    for (int j = 0; j < field.Size; j++) covered[field.Offset + j] = true;
                }
            }

            DecodedField fvc = result.Field("FVC");
            DecodedField fev1 = result.Field("FEV1");
            DecodedField pef = result.Field("PEF");
            DecodedField fet = result.Field("FET");
            DecodedField pefTime = result.Field("PEF_TIME");
            bool valuesSane = fvc != null && fev1 != null && pef != null && fet != null &&
                              fvc.FieldPresent && fev1.FieldPresent && pef.FieldPresent && fet.FieldPresent &&
                              fvc.Value.HasValue && fvc.Value.Value >= 0.05 && fvc.Value.Value <= 12.0 &&
                              fev1.Value.HasValue && fev1.Value.Value >= 0.05 && fev1.Value.Value <= fvc.Value.Value + 0.05 &&
                              pef.Value.HasValue && pef.Value.Value > 0 && pef.Value.Value <= 20.0 &&
                              fet.Value.HasValue && fet.Value.Value > 0 && fet.Value.Value <= 30.0;
            if (pefTime != null && pefTime.FieldPresent && pefTime.Value.HasValue && fet != null && fet.Value.HasValue)
            {
                if (!(pefTime.Value.Value > 0 && pefTime.Value.Value <= fet.Value.Value + 0.05))
                {
                    pefTime.Provenance = Provenance.Unverified;
                    pefTime.Note = "PEF time is outside the source range check against FET.";
                }
            }

            result.CoreFieldsValid = valuesSane;
            result.BlockValid = result.MinimumLengthMet && valuesSane;
            if (!valuesSane && result.MinimumLengthMet)
                result.Warnings.Add("One or more cod_LAST core fields are absent or outside the source range checks; no field is promoted to a clinical device result.");

            for (int i = 0; i < result.Fields.Count; i++)
            {
                DecodedField field = result.Fields[i];
                if (field.Key == "QUALITY_RAW")
                {
                    field.Provenance = Provenance.Unknown;
                    field.Note = "The raw integer is preserved. No clinical, ATS/ERS or device-quality meaning is assigned.";
                    field.ClinicalUseAllowed = false;
                    continue;
                }
                if (result.BlockValid && result.AssociationCaptureBound && field.Provenance != Provenance.Unverified)
                {
                    field.Provenance = Provenance.DeviceReported;
                    field.Note = "Direct cod_LAST field, capture-bound by the supplied association evidence. Device identity is still not a device-level maneuver ID.";
                }
                else
                {
                    field.Provenance = Provenance.Unverified;
                    field.Note = "Direct cod_LAST field, but block validity and/or capture association is not established.";
                }
                field.ClinicalUseAllowed = result.BlockValid && result.AssociationCaptureBound &&
                                           field.Key != "PEF_TIME" && field.Provenance == Provenance.DeviceReported;
            }

            int start = -1;
            for (int i = 0; i <= result.BlockLength; i++)
            {
                bool isCovered = i < result.BlockLength && covered[i];
                if (i < result.BlockLength && !isCovered && start < 0) start = i;
                if ((i == result.BlockLength || isCovered) && start >= 0)
                {
                    UnknownRange range = new UnknownRange();
                    range.Offset = start;
                    range.Size = i - start;
                    range.RawBytes = HexUtil.Slice(result.RawBlock, start, range.Size);
                    result.UnknownRanges.Add(range);
                    start = -1;
                }
            }
            result.CoverageComplete = true;
            if (result.UnknownRanges.Count > 0)
                result.Warnings.Add("All bytes not covered by an observed field are preserved as Unknown. No meaning or value is inferred.");

            return result;
        }

        private static DecodedField DecodeField(FieldDefinition def, byte[] block)
        {
            DecodedField field = new DecodedField();
            field.Key = def.Key;
            field.Label = def.Label;
            field.Offset = def.Offset;
            field.Size = def.Size;
            field.RawBytes = HexUtil.Slice(block, def.Offset, def.Size);
            field.RawUnsigned = HexUtil.U16Be(block, def.Offset);
            field.Unit = def.Unit;
            field.Conversion = def.Conversion;
            field.Evidence = def.Evidence;
            field.Resolution = def.Resolution;
            field.RequiredBlockLength = def.MinimumBlockLength;
            field.FieldPresent = field.RawBytes.Length == def.Size;
            field.Available = field.FieldPresent;
            field.ClinicalUseAllowed = false;
            if (field.FieldPresent && def.Key != "QUALITY_RAW")
                field.Value = field.RawUnsigned.Value / 100.0;
            else if (field.FieldPresent)
                field.Value = field.RawUnsigned.Value;
            return field;
        }

        public static string DecodePacket(PacketRecord packet)
        {
            if (packet == null) return "No packet.";
            if (!packet.Type.HasValue) return "Opaque bytes; packet type unknown.";
            PacketTypeDefinition def = ProtocolCatalog.PacketType(packet.Type.Value);
            string name = def == null ? "Unknown packet type" : def.Name;
            string raw = packet.Raw.HasValue ? packet.Raw.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            if (packet.Type.Value == 0x10 && packet.Raw.HasValue)
            {
                // This is the exact arithmetic used by the existing PacketDecoder.cs.
                // The negative branch is retained for traceability, not endorsed as correct.
                double flow = (packet.Raw.Value < 0x8000 ? packet.Raw.Value : packet.Raw.Value - 65535) / 100.0;
                return name + "; raw=" + raw + "; source conversion=" + HexUtil.Invariant(flow) + " L/s (unverified)";
            }
            if (packet.Type.Value == 0x20 && packet.Raw.HasValue)
            {
                double volume = (packet.Raw.Value - 5000) / 100.0;
                return name + "; raw=" + raw + "; (raw-5000)/100=" + HexUtil.Invariant(volume) + " (unverified)";
            }
            if (packet.Type.Value == 0x04) return name + "; capture-completion marker (observed)";
            if (packet.Type.Value == 0xFF) return name + "; no new sample (observed)";
            return name + "; raw=" + raw + "; type status=" + (def == null ? "UNKNOWN" : def.EvidenceStatus);
        }
    }
}

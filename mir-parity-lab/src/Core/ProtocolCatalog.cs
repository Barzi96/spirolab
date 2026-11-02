using System;
using System.Collections.Generic;

namespace MirParityLab.Core
{
    public sealed class FieldDefinition
    {
        public string Key;
        public string Label;
        public int Offset;
        public int Size;
        public string Unit;
        public double? Resolution;
        public string Conversion;
        public string Evidence;
        public int MinimumBlockLength;
        public bool ClinicalUseAllowed;
    }

    public sealed class PacketTypeDefinition
    {
        public byte Code;
        public string Name;
        public string ObservedMeaning;
        public string EvidenceStatus;
    }

    public static class ProtocolCatalog
    {
        public const int LastMinimumLength = 0x1C;
        public const int LastPefTimeMinimumLength = 0x52;
        public const int QualityOffset = 0x0A;
        public const int QualitySize = 2;
        public const int BlockLengthPrefixBytes = 4;
        public const int MaxBlockLength = 200000;

        public static readonly FieldDefinition[] LastFields = new FieldDefinition[]
        {
            new FieldDefinition {
                Key="FVC", Label="Forced vital capacity", Offset=0x10, Size=2,
                Unit="L", Resolution=0.01, Conversion="u16_be / 100",
                Evidence="Observed cod_LAST layout in app/Src/Device/DeviceSession.cs; not independently certified by MIR.",
                MinimumBlockLength=0x12, ClinicalUseAllowed=true
            },
            new FieldDefinition {
                Key="FEV1", Label="Forced expiratory volume in 1 s", Offset=0x12, Size=2,
                Unit="L", Resolution=0.01, Conversion="u16_be / 100",
                Evidence="Observed cod_LAST layout in app/Src/Device/DeviceSession.cs; not independently certified by MIR.",
                MinimumBlockLength=0x14, ClinicalUseAllowed=true
            },
            new FieldDefinition {
                Key="PEF", Label="Peak expiratory flow", Offset=0x14, Size=2,
                Unit="L/s", Resolution=0.01, Conversion="u16_be / 100",
                Evidence="Observed cod_LAST layout in app/Src/Device/DeviceSession.cs; not independently certified by MIR.",
                MinimumBlockLength=0x16, ClinicalUseAllowed=true
            },
            new FieldDefinition {
                Key="FET", Label="Forced expiratory time", Offset=0x1A, Size=2,
                Unit="s", Resolution=0.01, Conversion="u16_be / 100",
                Evidence="Observed cod_LAST layout in app/Src/Device/DeviceSession.cs; not independently certified by MIR.",
                MinimumBlockLength=0x1C, ClinicalUseAllowed=true
            },
            new FieldDefinition {
                Key="PEF_TIME", Label="Time to PEF", Offset=0x50, Size=2,
                Unit="s", Resolution=0.01, Conversion="u16_be / 100",
                Evidence="Decoded only when the block is at least 0x52 bytes. The source does not use this field clinically; meaning and time origin are unverified.",
                MinimumBlockLength=0x52, ClinicalUseAllowed=false
            },
            new FieldDefinition {
                Key="QUALITY_RAW", Label="Quality/reserved field (raw only)", Offset=QualityOffset, Size=QualitySize,
                Unit="", Resolution=null, Conversion="u16_be (raw integer; NO interpretation)",
                Evidence="Observed as an unknown diagnostic field in DeviceSession.cs. It is not an ATS/ERS quality grade and is never used clinically.",
                MinimumBlockLength=0x0C, ClinicalUseAllowed=false
            }
        };

        public static readonly PacketTypeDefinition[] PacketTypes = new PacketTypeDefinition[]
        {
            new PacketTypeDefinition {Code=0x10, Name="Live signal packet", ObservedMeaning="Used by PacketDecoder.cs as flow-like data. Signed/scale interpretation is not certified.", EvidenceStatus="OBSERVED_SOURCE_ONLY"},
            new PacketTypeDefinition {Code=0x20, Name="Timed-volume packet", ObservedMeaning="Used only in time-step mode as a volume-like value; semantics not certified.", EvidenceStatus="OBSERVED_SOURCE_ONLY"},
            new PacketTypeDefinition {Code=0x01, Name="Live packet type", ObservedMeaning="Accepted by the capture loop but not decoded by PacketDecoder.cs.", EvidenceStatus="UNKNOWN"},
            new PacketTypeDefinition {Code=0x04, Name="Completion marker", ObservedMeaning="Capture loop treats this as natural completion.", EvidenceStatus="OBSERVED_SOURCE_ONLY"},
            new PacketTypeDefinition {Code=0xFF, Name="No new sample", ObservedMeaning="Capture loop skips it; not a measurement.", EvidenceStatus="OBSERVED_SOURCE_ONLY"}
        };

        public static PacketTypeDefinition PacketType(byte type)
        {
            for (int i = 0; i < PacketTypes.Length; i++) if (PacketTypes[i].Code == type) return PacketTypes[i];
            return null;
        }

        public static FieldDefinition Field(string key)
        {
            for (int i = 0; i < LastFields.Length; i++)
                if (String.Equals(LastFields[i].Key, key, StringComparison.OrdinalIgnoreCase)) return LastFields[i];
            return null;
        }

        public static string CommandEvidence(byte command)
        {
            switch (command)
            {
                case 0x00: return "cod_ON — used before SDA identification read; write command, not used by this offline tool.";
                case 0x01: return "live packet read, 3 bytes; observed only.";
                case 0x08: return "cod_FVC start response, 3 bytes: first byte 0x30 (volume step) or 0x40 (time step); value u16_be.";
                case 0x1B: return "cod_ESC abort; not used by this offline tool.";
                case 0xA2: return "cod_LAST result block; 4-byte big-endian length prefix then bulk payload.";
                case 0xA3: return "cod_CURVE curve block; 4-byte big-endian length prefix then bulk payload.";
                case 0xFF: return "state read, 1 byte; 0x00 ready, 0xFF no response/unavailable in source.";
                default: return "Unknown command; not emitted or used by this offline tool.";
            }
        }
    }
}

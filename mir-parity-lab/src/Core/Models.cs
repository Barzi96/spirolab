using System;
using System.Collections.Generic;

namespace MirParityLab.Core
{
    public sealed class SourceFileInfo
    {
        public string FileName = "";
        public string Sha256 = "";
        public long ByteLength;
        public string Kind = "";
        public string Note = "";
        public override string ToString() { return FileName + " (" + ByteLength + " bytes)"; }
    }

    public sealed class CaptureMeta
    {
        public string TestId = "";
        public string DataOrigin = "unknown";
        public DateTime? RecordedAt;
        public string DeviceModel = "";
        public string DeviceSerial = "";
        public string Notes = "";
        public List<SourceFileInfo> SourceFiles = new List<SourceFileInfo>();
    }

    public sealed class PacketRecord
    {
        public int Index;
        public double? Seconds;
        public byte? Type;
        public int? Raw;
        public byte[] RawBytes = new byte[0];
        public string SourceLine = "";
        public string Notes = "";
        public int Length { get { return RawBytes == null ? 0 : RawBytes.Length; } }
    }

    public sealed class ManeuverRecord
    {
        public string ManeuverKey = "";
        public int Sequence = 1;
        public string PrePost = "UNKNOWN";
        public DateTime? RecordedAt;
        public string Association = "not_established";
        public string AssociationEvidence = "";
        public byte[] LastBlock = new byte[0];
        public byte[] CurveBlock = new byte[0];
        public byte? StepType;
        public double? Step;
        public List<PacketRecord> Packets = new List<PacketRecord>();
    }

    public sealed class CaptureDocument
    {
        public CaptureMeta Meta = new CaptureMeta();
        public List<ManeuverRecord> Maneuvers = new List<ManeuverRecord>();
        public byte[] UnframedBlob = new byte[0];
        public string BlobKind = "";
        public bool OpaqueArchive;
        public List<string> Warnings = new List<string>();
        public string LoadedPath = "";

        public ManeuverRecord FirstManeuver
        {
            get { return Maneuvers.Count == 0 ? null : Maneuvers[0]; }
        }
    }

    public sealed class DecodedField
    {
        public string Key = "";
        public string Label = "";
        public int Offset;
        public int Size;
        public byte[] RawBytes = new byte[0];
        public long? RawUnsigned;
        public double? Value;
        public string Unit = "";
        public string Conversion = "";
        public Provenance Provenance = Provenance.Unknown;
        public string Evidence = "";
        public string Note = "";
        public double? Resolution;
        public bool Available;
        public bool ClinicalUseAllowed;
        public int RequiredBlockLength;
        public bool FieldPresent;
    }

    public sealed class UnknownRange
    {
        public int Offset;
        public int Size;
        public byte[] RawBytes = new byte[0];
        public string Note = "Preserved only; no meaning is assigned.";
    }

    public sealed class DecodeResult
    {
        public byte[] RawBlock = new byte[0];
        public int BlockLength;
        public bool MinimumLengthMet;
        public bool CoreFieldsValid;
        public bool BlockValid;
        public bool AssociationCaptureBound;
        public string Association = "";
        public string AssociationEvidence = "";
        public List<DecodedField> Fields = new List<DecodedField>();
        public List<UnknownRange> UnknownRanges = new List<UnknownRange>();
        public List<string> Warnings = new List<string>();
        public bool CoverageComplete;

        public DecodedField Field(string key)
        {
            for (int i = 0; i < Fields.Count; i++)
                if (String.Equals(Fields[i].Key, key, StringComparison.OrdinalIgnoreCase)) return Fields[i];
            return null;
        }

        public DecodedField Parameter(string parameter)
        {
            string key = Parameters.LastBlockField(parameter);
            return key.Length == 0 ? null : Field(key);
        }
    }

    public sealed class ExternalMeasurement
    {
        public string TestId = "";
        public string ManeuverKey = "";
        public int? Sequence;
        public string PrePost = "UNKNOWN";
        public DateTime? RecordedAt;
        public string Parameter = "";
        public double? Value;
        public string Unit = "";
        public MeasurementSourceKind Source = MeasurementSourceKind.Unknown;
        public double? Precision;
        public string Evidence = "";
        public bool SameManeuverAttested;
        public string Notes = "";
        public int SourceRow;
        public string RawLine = "";
    }

    public sealed class CalculatedSet
    {
        public Dictionary<string, double> Values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public string Method = "none";
        public bool Available;
        public int SampleCount;
        public Provenance Provenance = Provenance.SoftwareCalculated;
        public List<string> Assumptions = new List<string>();
        public List<string> Warnings = new List<string>();

        public double? Get(string parameter)
        {
            string key = Parameters.Normalize(parameter);
            double value;
            return Values.TryGetValue(key, out value) ? (double?)value : null;
        }
    }

    public sealed class MatchResult
    {
        public ManeuverRecord Maneuver;
        public bool Established;
        public string Evidence = "";
        public string Reason = "";
    }

    public sealed class ComparisonRow
    {
        public string TestId = "";
        public string ManeuverKey = "";
        public string Parameter = "";
        public string Unit = "";

        public double? OfficialValue;
        public MeasurementSourceKind OfficialSource = MeasurementSourceKind.Unknown;
        public double? OfficialPrecision;

        public double? DecodedValue;
        public Provenance DecodedProvenance = Provenance.Unknown;
        public string DecodedRawHex = "";
        public string DecodedOffset = "";
        public string DecodedNote = "";

        public double? SoftwareValue;
        public string SoftwareMethod = "";

        public double? SpirolabValue;
        public string SpirolabEvidence = "";

        public double? OfficialVsDecodedAbs;
        public double? OfficialVsDecodedRelPct;
        public double? OfficialVsSoftwareAbs;
        public double? OfficialVsSoftwareRelPct;
        public double? SpirolabVsDecodedAbs;
        public double? SpirolabVsDecodedRelPct;

        public double? ToleranceAbs;
        public string ToleranceBasis = "not_established";
        public ComparisonState Status = ComparisonState.IdentityNotEstablished;
        public ComparisonState SoftwareStatus = ComparisonState.IdentityNotEstablished;
        public bool SameManeuver;
        public string MatchEvidence = "";
        public string Reason = "";
        public string ProbableCause = "";
    }

    public sealed class ManeuverComparison
    {
        public ManeuverRecord Maneuver;
        public DecodeResult Decoded;
        public CalculatedSet Calculated;
        public List<ExternalMeasurement> Official = new List<ExternalMeasurement>();
        public List<ComparisonRow> Rows = new List<ComparisonRow>();
    }

    public sealed class ComparisonReport
    {
        public CaptureDocument Capture;
        public List<ManeuverComparison> Maneuvers = new List<ManeuverComparison>();
        public List<string> Warnings = new List<string>();
        public DateTime GeneratedAtUtc = DateTime.UtcNow;
        public string Claim = "PHASE-1 RESEARCH TOOL: no claim of numerical parity with MIR is made.";

        public int RowCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Maneuvers.Count; i++) count += Maneuvers[i].Rows.Count;
                return count;
            }
        }
    }
}

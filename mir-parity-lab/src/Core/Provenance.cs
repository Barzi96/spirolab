using System;

namespace MirParityLab.Core
{
    // Exactly the six provenance classes required by the research protocol.
    // A software reconstruction is never converted into DeviceReported.
    public enum Provenance
    {
        DeviceReported = 0,
        DeviceDerived = 1,
        SoftwareCalculated = 2,
        ReferencePredicted = 3,
        Unknown = 4,
        Unverified = 5
    }

    public static class ProvenanceText
    {
        public static string Code(Provenance value)
        {
            switch (value)
            {
                case Provenance.DeviceReported: return "device_reported";
                case Provenance.DeviceDerived: return "device_derived";
                case Provenance.SoftwareCalculated: return "software_calculated";
                case Provenance.ReferencePredicted: return "reference_predicted";
                case Provenance.Unknown: return "unknown";
                default: return "unverified";
            }
        }

        public static string Label(Provenance value)
        {
            switch (value)
            {
                case Provenance.DeviceReported: return "Device-reported";
                case Provenance.DeviceDerived: return "Device-derived";
                case Provenance.SoftwareCalculated: return "Software-calculated";
                case Provenance.ReferencePredicted: return "Reference-predicted";
                case Provenance.Unknown: return "Unknown";
                default: return "Unverified";
            }
        }
    }

    public enum MeasurementSourceKind
    {
        Unknown = 0,
        MirDeviceDisplay = 1,
        MirDevicePrint = 2,
        MirSoftware = 3,
        SpirolabSoftware = 4,
        ManualReference = 5,
        ReferencePredicted = 6
    }

    public static class MeasurementSourceText
    {
        public static string Code(MeasurementSourceKind value)
        {
            switch (value)
            {
                case MeasurementSourceKind.MirDeviceDisplay: return "mir_device_display";
                case MeasurementSourceKind.MirDevicePrint: return "mir_device_print";
                case MeasurementSourceKind.MirSoftware: return "mir_software";
                case MeasurementSourceKind.SpirolabSoftware: return "spirolab_software";
                case MeasurementSourceKind.ManualReference: return "manual_reference";
                case MeasurementSourceKind.ReferencePredicted: return "reference_predicted";
                default: return "unknown";
            }
        }

        public static string Label(MeasurementSourceKind value)
        {
            switch (value)
            {
                case MeasurementSourceKind.MirDeviceDisplay: return "MIR device display";
                case MeasurementSourceKind.MirDevicePrint: return "MIR device print";
                case MeasurementSourceKind.MirSoftware: return "Official MIR software";
                case MeasurementSourceKind.SpirolabSoftware: return "SpiroLab software export";
                case MeasurementSourceKind.ManualReference: return "Manual / reference entry";
                case MeasurementSourceKind.ReferencePredicted: return "Reference-predicted";
                default: return "Unknown source";
            }
        }

        public static bool TryParse(string text, out MeasurementSourceKind value)
        {
            value = MeasurementSourceKind.Unknown;
            string s = (text ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            if (s.Length == 0) return false;
            switch (s)
            {
                case "mir_device_display":
                case "device_display":
                case "display":
                case "mir_display": value = MeasurementSourceKind.MirDeviceDisplay; return true;
                case "mir_device_print":
                case "device_print":
                case "print":
                case "printed": value = MeasurementSourceKind.MirDevicePrint; return true;
                case "mir_software":
                case "winspiro":
                case "winspiropro":
                case "mir_spiro":
                case "official_software": value = MeasurementSourceKind.MirSoftware; return true;
                case "spirolab_software":
                case "spirolab":
                case "spirolab_export": value = MeasurementSourceKind.SpirolabSoftware; return true;
                case "manual_reference":
                case "manual":
                case "operator_entry": value = MeasurementSourceKind.ManualReference; return true;
                case "reference_predicted":
                case "predicted":
                case "reference": value = MeasurementSourceKind.ReferencePredicted; return true;
                default: return false;
            }
        }
    }

    public enum ComparisonState
    {
        IdentityNotEstablished = 0,
        NoMirReference = 1,
        NoCapturedValue = 2,
        UnitNotComparable = 3,
        ToleranceNotEstablished = 4,
        WithinRoundingTolerance = 5,
        OutsideRoundingTolerance = 6,
        ReferenceOnly = 7
    }

    public static class ComparisonText
    {
        public static string Code(ComparisonState value)
        {
            switch (value)
            {
                case ComparisonState.NoMirReference: return "no_mir_reference";
                case ComparisonState.NoCapturedValue: return "no_captured_value";
                case ComparisonState.UnitNotComparable: return "unit_not_comparable";
                case ComparisonState.ToleranceNotEstablished: return "tolerance_not_established";
                case ComparisonState.WithinRoundingTolerance: return "within_rounding_tolerance";
                case ComparisonState.OutsideRoundingTolerance: return "outside_rounding_tolerance";
                case ComparisonState.ReferenceOnly: return "reference_only";
                default: return "identity_not_established";
            }
        }

        public static string Label(ComparisonState value)
        {
            switch (value)
            {
                case ComparisonState.NoMirReference: return "No MIR reference";
                case ComparisonState.NoCapturedValue: return "No captured value";
                case ComparisonState.UnitNotComparable: return "Unit not comparable";
                case ComparisonState.ToleranceNotEstablished: return "Tolerance not established";
                case ComparisonState.WithinRoundingTolerance: return "Within rounding tolerance";
                case ComparisonState.OutsideRoundingTolerance: return "Outside rounding tolerance";
                case ComparisonState.ReferenceOnly: return "Reference only";
                default: return "Identity not established";
            }
        }
    }

    public static class Parameters
    {
        public const string Fvc = "FVC";
        public const string Fev1 = "FEV1";
        public const string Fev1Fvc = "FEV1/FVC";
        public const string Pef = "PEF";
        public const string Fet = "FET";
        public const string PefTime = "PEF_TIME";
        public const string Quality = "QUALITY_RAW";
        public const string Fev6 = "FEV6";
        public const string Fef2575 = "FEF25-75";

        public static string Normalize(string name)
        {
            string s = (name ?? "").Trim().ToUpperInvariant();
            s = s.Replace(" ", "").Replace("_", "").Replace("-", "").Replace("−", "");
            switch (s)
            {
                case "FVC": return Fvc;
                case "FEV1": return Fev1;
                case "FEV1/FVC":
                case "FEV1FVC":
                case "RATIO":
                case "FEV1%FVC": return Fev1Fvc;
                case "PEF": return Pef;
                case "FET": return Fet;
                case "PEFTIME":
                case "TIMETOPEF":
                case "TPEF": return PefTime;
                case "QUALITY":
                case "QUALITYRAW": return Quality;
                case "FEV6": return Fev6;
                case "FEF2575": return Fef2575;
                default: return (name ?? "").Trim().ToUpperInvariant();
            }
        }

        public static string Unit(string parameter)
        {
            switch (Normalize(parameter))
            {
                case Fvc:
                case Fev1:
                case Fev6: return "L";
                case Fev1Fvc: return "%";
                case Pef:
                case Fef2575: return "L/s";
                case Fet:
                case PefTime: return "s";
                case Quality: return "";
                default: return "";
            }
        }

        public static string DisplayName(string parameter)
        {
            switch (Normalize(parameter))
            {
                case PefTime: return "PEF time";
                case Fev1Fvc: return "FEV1/FVC";
                case Quality: return "Quality field (raw only)";
                case Fef2575: return "FEF25-75";
                default: return Normalize(parameter);
            }
        }

        public static string LastBlockField(string parameter)
        {
            switch (Normalize(parameter))
            {
                case Fvc: return "FVC";
                case Fev1: return "FEV1";
                case Pef: return "PEF";
                case Fet: return "FET";
                case PefTime: return "PEF_TIME";
                default: return "";
            }
        }
    }
}

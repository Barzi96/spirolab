using System;

namespace MirParityLab.Core
{
    public static class UnitConversion
    {
        public static bool TryConvert(double value, string fromUnit, string toUnit,
            out double converted, out double factor, out string group, out string error)
        {
            converted = value;
            factor = 1;
            group = Group(fromUnit);
            error = "";
            string from = Normalize(fromUnit);
            string to = Normalize(toUnit);
            if (group == "" || group != Group(toUnit))
            {
                error = "Units are not in the same dimension: '" + fromUnit + "' vs '" + toUnit + "'.";
                return false;
            }
            double fromFactor = FactorToCanonical(from, group);
            double toFactor = FactorToCanonical(to, group);
            if (fromFactor <= 0 || toFactor <= 0)
            {
                error = "Unsupported unit: '" + fromUnit + "' or '" + toUnit + "'.";
                return false;
            }
            converted = value * fromFactor / toFactor;
            factor = fromFactor / toFactor;
            if (Double.IsNaN(converted) || Double.IsInfinity(converted))
            {
                error = "Converted value is not finite.";
                return false;
            }
            return true;
        }

        public static string Group(string unit)
        {
            string s = Normalize(unit);
            switch (s)
            {
                case "l":
                case "ml":
                case "milliliter":
                case "millilitre": return "volume";
                case "l/s":
                case "l/sec":
                case "l/min":
                case "ml/s":
                case "ml/sec": return "flow";
                case "s":
                case "sec":
                case "ms": return "time";
                case "%":
                case "percent":
                case "percentage": return "percent";
                default: return "";
            }
        }

        public static string Normalize(string unit)
        {
            if (unit == null) return "";
            return unit.Trim().ToLowerInvariant()
                .Replace("litre", "l").Replace("liter", "l")
                .Replace("litres", "l").Replace("liters", "l")
                .Replace("sec", "s").Replace("seconds", "s")
                .Replace("second", "s")
                .Replace(" ", "");
        }

        private static double FactorToCanonical(string normalized, string group)
        {
            if (group == "volume")
            {
                if (normalized == "l") return 1.0;
                if (normalized == "ml" || normalized == "milliliter" || normalized == "millilitre") return 0.001;
            }
            if (group == "flow")
            {
                if (normalized == "l/s" || normalized == "l/sec") return 1.0;
                if (normalized == "ml/s" || normalized == "ml/sec") return 0.001;
                if (normalized == "l/min") return 1.0 / 60.0;
            }
            if (group == "time")
            {
                if (normalized == "s") return 1.0;
                if (normalized == "ms") return 0.001;
            }
            if (group == "percent")
            {
                if (normalized == "%" || normalized == "percent" || normalized == "percentage") return 1.0;
            }
            return 0;
        }

        public static double? ConvertNullable(double? value, string from, string to, out double factor, out string error)
        {
            factor = 1;
            error = "";
            if (!value.HasValue) return null;
            double converted;
            string group;
            if (!TryConvert(value.Value, from, to, out converted, out factor, out group, out error)) return null;
            return converted;
        }
    }
}

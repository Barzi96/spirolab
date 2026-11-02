using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MirParityLab.Core
{
    public static class HexUtil
    {
        public static string ToHex(byte[] bytes)
        {
            if (bytes == null) return "";
            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public static string ToSpacedHex(byte[] bytes)
        {
            if (bytes == null) return "";
            StringBuilder sb = new StringBuilder(bytes.Length * 3);
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static byte[] Parse(string text)
        {
            if (text == null) return new byte[0];
            string cleaned = text.Replace("0x", "").Replace("0X", "");
            StringBuilder digits = new StringBuilder(cleaned.Length);
            for (int i = 0; i < cleaned.Length; i++)
            {
                char c = cleaned[i];
                if (Uri.IsHexDigit(c)) digits.Append(c);
                else if (Char.IsWhiteSpace(c) || c == ',' || c == ';' || c == ':' || c == '-' || c == '|') continue;
                else throw new FormatException("Invalid hex character: " + c);
            }
            if (digits.Length % 2 != 0) throw new FormatException("Hex text has an odd number of digits.");
            byte[] result = new byte[digits.Length / 2];
            for (int i = 0; i < result.Length; i++)
                result[i] = byte.Parse(digits.ToString(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return result;
        }

        public static int U16Be(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 1 >= data.Length) return 0;
            return (data[offset] << 8) | data[offset + 1];
        }

        public static long U32Be(byte[] data, int offset)
        {
            if (data == null || offset < 0 || offset + 3 >= data.Length) return 0;
            return ((long)data[offset] << 24) | ((long)data[offset + 1] << 16) |
                   ((long)data[offset + 2] << 8) | data[offset + 3];
        }

        public static byte[] Slice(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length < 0 || offset + length > data.Length) return new byte[0];
            byte[] result = new byte[length];
            Array.Copy(data, offset, result, 0, length);
            return result;
        }

        public static string Invariant(double? value)
        {
            if (!value.HasValue || Double.IsNaN(value.Value) || Double.IsInfinity(value.Value)) return "";
            return value.Value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        public static string Invariant(double value)
        {
            return value.ToString("0.############", CultureInfo.InvariantCulture);
        }

        public static bool TryDouble(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            string s = text.Trim();
            if (s.Length == 0) return false;
            if (s.IndexOf(',') >= 0 && s.IndexOf('.') < 0) s = s.Replace(',', '.');
            return Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !Double.IsNaN(value) && !Double.IsInfinity(value);
        }

        public static DateTime? TryDate(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            DateTime value;
            if (DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out value))
                return value;
            return null;
        }

        public static string Iso(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) : "";
        }

        public static string SafeToken(string text, string fallback)
        {
            if (String.IsNullOrWhiteSpace(text)) return fallback;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < text.Length && sb.Length < 64; i++)
            {
                char c = text[i];
                if (Char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
                else if (Char.IsWhiteSpace(c) && sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            string result = sb.ToString().Trim('-', '.', '_');
            return result.Length == 0 ? fallback : result;
        }
    }
}

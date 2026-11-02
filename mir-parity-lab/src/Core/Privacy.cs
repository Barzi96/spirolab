using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MirParityLab.Core
{
    public sealed class PrivacyException : Exception
    {
        public PrivacyException(string message) : base(message) { }
    }

    public static class Privacy
    {
        private static readonly Regex TestIdPattern =
            new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

        private static readonly Regex PatientTextPattern = new Regex(
            @"(?i)\b(patient[_ -]?(name|id|code)?|first[_ -]?name|last[_ -]?name|full[_ -]?name|national[_ -]?(id|code)|mrn|medical[_ -]?record[_ -]?(number|id)?|birth[_ -]?(date|day)|date[_ -]?of[_ -]?birth|dob|address|phone|mobile|email)\b\s*[:=]",
            RegexOptions.CultureInvariant);

        private static readonly HashSet<string> ForbiddenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "patient", "patient_name", "patientname", "first_name", "firstname",
            "last_name", "lastname", "full_name", "fullname", "national_id",
            "nationalid", "national_code", "mrn", "medical_record_number",
            "medicalrecordnumber", "address", "phone", "mobile", "birth_date",
            "birthdate", "date_of_birth", "email"
        };

        public static void ValidateTestId(string testId)
        {
            if (String.IsNullOrWhiteSpace(testId))
                throw new PrivacyException("A non-identifying test_id is required.");
            if (!TestIdPattern.IsMatch(testId.Trim()))
                throw new PrivacyException("test_id must be 1-64 characters: letters, digits, dot, underscore or hyphen. Do not use a patient name or national ID.");
        }

        public static void EnsureNoPatientText(IEnumerable<string> lines)
        {
            if (lines == null) return;
            foreach (string line in lines)
            {
                if (String.IsNullOrWhiteSpace(line)) continue;
                Match match = PatientTextPattern.Match(line);
                if (match.Success)
                    throw new PrivacyException("Patient-identifying field rejected in the imported text: " + match.Value.Trim() + ". De-identify the file and use only a non-identifying test_id.");
            }
        }

        public static bool IsForbiddenKey(string key)
        {
            string normalized = (key ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
            return ForbiddenKeys.Contains(normalized);
        }

        public static void EnsureNoPatientKeys(object node)
        {
            Dictionary<string, object> map = node as Dictionary<string, object>;
            if (map != null)
            {
                foreach (KeyValuePair<string, object> pair in map)
                {
                    string key = (pair.Key ?? "").Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
                    if (ForbiddenKeys.Contains(key))                        throw new PrivacyException("Patient-identifying key rejected: " + pair.Key + ". Use only a non-identifying test_id.");
                    EnsureNoPatientKeys(pair.Value);
                }
                return;
            }
            object[] list = node as object[];
            if (list != null)
            {
                for (int i = 0; i < list.Length; i++) EnsureNoPatientKeys(list[i]);
            }
        }
    }
}

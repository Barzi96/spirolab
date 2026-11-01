using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace SpiroLab.HLine
{
    // One cookie copied out of Firefox. Values live only in memory and are never
    // written to settings, logs or the examination file.
    public sealed class SessionCookie
    {
        public string Name = "";
        public string Value = "";
        public string Domain = "";
        public string Path = "/";
        public bool Secure;
        public bool HttpOnly;
        public DateTime ExpiresUtc = DateTime.MinValue; // MinValue = session cookie
        public bool Expired { get { return ExpiresUtc != DateTime.MinValue && ExpiresUtc <= DateTime.UtcNow; } }
    }

    public sealed class FirefoxProfile
    {
        public string Name = "";
        public string Directory = "";
        public bool IsDefault;
    }

    public sealed class FirefoxSession
    {
        public FirefoxProfile Profile;
        public List<SessionCookie> Cookies = new List<SessionCookie>();
        public long LastAccess;
    }

    // Reads ONLY hline.ssu.ac.ir cookies (host or its direct parent) from the
    // user's Firefox profiles. Never opens Firefox's own file for writing and
    // never touches its password store.
    public static class FirefoxSessions
    {
        public const string HLineDomain = "hline.ssu.ac.ir";

        public static string Root
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox"); }
        }

        public static List<FirefoxProfile> Profiles() { return Profiles(null); }

        public static List<FirefoxProfile> Profiles(string root)
        {
            root = string.IsNullOrEmpty(root) ? Root : root;
            List<FirefoxProfile> profiles = new List<FirefoxProfile>();
            string ini = Path.Combine(root, "profiles.ini");
            if (File.Exists(ini))
            {
                try
                {
                    string section = null, pathValue = null, name = null, relative = "1", isDefault = "0";
                    List<string> lines = new List<string>(File.ReadAllLines(ini));
                    for (int index = 0; index <= lines.Count; index++)
                    {
                        string line = index == lines.Count ? "[" : lines[index].Trim();
                        if (line.StartsWith("[") || index == lines.Count)
                        {
                            AddProfile(profiles, root, pathValue, name, relative, isDefault);
                            pathValue = name = null; relative = "1"; isDefault = "0";
                            if (index == lines.Count) break;
                            section = line.Substring(1, line.Length - (line.EndsWith("]") ? 2 : 1));
                            continue;
                        }
                        if (section == null || !section.ToLowerInvariant().StartsWith("profile")) continue;
                        int equals = line.IndexOf('=');
                        if (equals <= 0) continue;
                        string key = line.Substring(0, equals).Trim().ToLowerInvariant();
                        string value = line.Substring(equals + 1).Trim();
                        if (key == "path") pathValue = value;
                        else if (key == "name") name = value;
                        else if (key == "isrelative") relative = value;
                        else if (key == "default") isDefault = value;
                    }
                }
                catch { profiles.Clear(); }
            }
            if (profiles.Count == 0)
            {
                try
                {
                    string folder = Path.Combine(root, "Profiles");
                    if (Directory.Exists(folder))
                    {
                        List<string> candidates = new List<string>(Directory.GetDirectories(folder));
                        candidates.Sort(StringComparer.OrdinalIgnoreCase);
                        foreach (string directory in candidates)
                        {
                            if (!File.Exists(Path.Combine(directory, "cookies.sqlite"))) continue;
                            string leaf = Path.GetFileName(directory);
                            profiles.Add(new FirefoxProfile { Name = leaf, Directory = directory, IsDefault = leaf.IndexOf("release", StringComparison.OrdinalIgnoreCase) >= 0 || leaf.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0 });
                        }
                    }
                }
                catch { profiles.Clear(); }
            }
            return profiles;
        }

        static void AddProfile(List<FirefoxProfile> profiles, string root, string path, string name, string relative, string isDefault)
        {
            if (string.IsNullOrEmpty(path)) return;
            string resolved;
            try
            {
                resolved = relative == "1"
                    ? Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)))
                    : Path.GetFullPath(path);
            }
            catch { return; }
            if (!Directory.Exists(resolved) || !File.Exists(Path.Combine(resolved, "cookies.sqlite"))) return;
            foreach (FirefoxProfile existing in profiles)
                if (string.Equals(existing.Directory, resolved, StringComparison.OrdinalIgnoreCase)) return;
            profiles.Add(new FirefoxProfile
            {
                Name = string.IsNullOrEmpty(name) ? Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar)) : name,
                Directory = resolved,
                IsDefault = isDefault == "1"
            });
        }

        // Strict scope check: the cookie host itself or its direct parent.
        public static bool MatchesDomain(string cookieHost, string domain)
        {
            string host = (cookieHost ?? "").Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant();
            string expected = (domain ?? "").Trim().TrimStart('.').TrimEnd('.').ToLowerInvariant();
            if (host.Length == 0 || expected.Length == 0) return false;
            if (host == expected) return true;
            int dot = expected.IndexOf('.');
            return dot > 0 && dot + 1 < expected.Length && host == expected.Substring(dot + 1);
        }

        public static List<FirefoxSession> Candidates(string domain, string preferredDirectory, out string problem)
        { return Candidates(domain, preferredDirectory, out problem, null); }

        public static List<FirefoxSession> Candidates(string domain, string preferredDirectory, out string problem, string root)
        {
            problem = null;
            domain = string.IsNullOrEmpty(domain) ? HLineDomain : domain;
            List<FirefoxSession> sessions = new List<FirefoxSession>();
            List<FirefoxProfile> profiles = Profiles(root);
            if (profiles.Count == 0)
            {
                problem = "پروفایل فایرفاکس پیدا نشد.";
                return sessions;
            }
            foreach (FirefoxProfile profile in profiles)
            {
                long newest;
                string readProblem;
                List<SessionCookie> cookies = Read(profile, domain, out newest, out readProblem);
                if (cookies.Count > 0) sessions.Add(new FirefoxSession { Profile = profile, Cookies = cookies, LastAccess = newest });
                else if (readProblem != null && problem == null) problem = readProblem;
            }
            sessions.Sort(delegate(FirefoxSession left, FirefoxSession right)
            {
                int leftPreferred = SameDirectory(left.Profile.Directory, preferredDirectory) ? 1 : 0;
                int rightPreferred = SameDirectory(right.Profile.Directory, preferredDirectory) ? 1 : 0;
                if (leftPreferred != rightPreferred) return rightPreferred - leftPreferred;
                if (left.Profile.IsDefault != right.Profile.IsDefault) return left.Profile.IsDefault ? -1 : 1;
                int byAccess = right.LastAccess.CompareTo(left.LastAccess);
                if (byAccess != 0) return byAccess;
                return right.Cookies.Count.CompareTo(left.Cookies.Count);
            });
            if (sessions.Count == 0 && problem == null)
                problem = "نشست اچ‌لاین در فایرفاکس پیدا نشد؛ ابتدا در فایرفاکس وارد سامانه شوید و سپس «اتصال مجدد» را بزنید.";
            return sessions;
        }

        static bool SameDirectory(string left, string right)
        {
            if (string.IsNullOrEmpty(right)) return false;
            try { return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        static List<SessionCookie> Read(FirefoxProfile profile, string domain, out long newest, out string problem)
        {
            newest = 0; problem = null;
            List<SessionCookie> cookies = new List<SessionCookie>();
            string database = Path.Combine(profile.Directory, "cookies.sqlite");
            if (!File.Exists(database))
            {
                problem = "پایگاه کوکی فایرفاکس پیدا نشد.";
                return cookies;
            }
            string parent = domain.IndexOf('.') > 0 ? "%." + domain.Substring(domain.IndexOf('.') + 1) : null;
            string sql = "SELECT name,value,host,path,expiry,isSecure,isHttpOnly,lastAccessed FROM moz_cookies WHERE host LIKE ?";
            List<string> parameters = new List<string>();
            parameters.Add("%" + domain);
            if (parent != null) { sql += " OR host LIKE ?"; parameters.Add(parent); }
            List<string[]> rows = null;
            try
            {
                // Read the live database read-only; Firefox keeps it open in WAL
                // mode and a read-only connection never writes to it, so no copy
                // of the cookie store is ever created.
                for (int attempt = 0; attempt < 3 && rows == null; attempt++)
                {
                    try { rows = Sqlite.Query(database, sql, parameters.ToArray()); }
                    catch (IOException) { if (attempt == 2) throw; Thread.Sleep(150); }
                }
            }
            catch (DllNotFoundException) { problem = "SQLite داخلی ویندوز (winsqlite3) در دسترس نیست."; return cookies; }
            catch (BadImageFormatException) { problem = "SQLite داخلی ویندوز با این نسخهٔ برنامه سازگار نیست."; return cookies; }
            catch (Exception exception) { problem = "خواندن کوکی فایرفاکس ممکن نشد: " + exception.Message; return cookies; }
            foreach (string[] row in rows)
            {
                if (row.Length < 8 || !MatchesDomain(row[2], domain)) continue;
                string name = row[0];
                if (name.Length == 0) continue;
                SessionCookie cookie = new SessionCookie
                {
                    Name = name,
                    Value = row[1],
                    Domain = row[2],
                    Path = string.IsNullOrEmpty(row[3]) ? "/" : row[3],
                    Secure = row[5] == "1",
                    HttpOnly = row[6] == "1",
                    ExpiresUtc = FromUnix(row[4])
                };
                if (cookie.Expired) continue;
                cookies.Add(cookie);
                long access;
                if (long.TryParse(row[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out access) && access > newest) newest = access;
            }
            return cookies;
        }

        static DateTime FromUnix(string raw)
        {
            long value;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0) return DateTime.MinValue;
            try
            {
                DateTime result = value > 100000000000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime
                    : DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
                // A past date must stay past (and therefore be filtered as
                // expired); only a zero expiry means a session cookie.
                if(result.Year > 2100) return new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return result;
            }
            catch { return DateTime.MinValue; }
        }

    }
}

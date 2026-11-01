using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SpiroLab.HLine
{
    // Minimal read-only bridge to the in-box Windows SQLite (winsqlite3.dll).
    // Firefox stores cookies in a plain SQLite database; the caller copies that
    // file to a private temp folder and opens the copy, so Firefox itself is
    // never locked, stopped or modified.
    internal static class Sqlite
    {
        const string Library = "winsqlite3.dll";
        const int ReadOnly = 1, Row = 100, Done = 101;
        static readonly IntPtr Transient = new IntPtr(-1);

        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_close(IntPtr db);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_busy_timeout(IntPtr db, int milliseconds);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out IntPtr statement, IntPtr tail);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, int bytes, IntPtr destructor);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_column_count(IntPtr statement);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern IntPtr sqlite3_column_text(IntPtr statement, int column);
        [DllImport(Library, CallingConvention = CallingConvention.StdCall)]
        static extern int sqlite3_column_bytes(IntPtr statement, int column);

        public static bool Available
        {
            get
            {
                try
                {
                    string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "winsqlite3.dll");
                    string wow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "winsqlite3.dll");
                    return File.Exists(system) || File.Exists(wow);
                }
                catch { return false; }
            }
        }

        // Returns every row as text columns; callers parse the few numeric ones.
        public static List<string[]> Query(string path, string sql, params string[] parameters)
        {
            IntPtr db = IntPtr.Zero, statement = IntPtr.Zero;
            int result = sqlite3_open_v2(path, out db, ReadOnly, IntPtr.Zero);
            if (result != 0 || db == IntPtr.Zero) throw new IOException("پایگاه کوکی فایرفاکس خوانده نشد.");
            try
            {
                sqlite3_busy_timeout(db, 2000);
                result = sqlite3_prepare_v2(db, sql, -1, out statement, IntPtr.Zero);
                if (result != 0 || statement == IntPtr.Zero) throw new IOException("ساختار پایگاه کوکی فایرفاکس ناشناخته است.");
                for (int i = 0; i < parameters.Length; i++)
                    sqlite3_bind_text(statement, i + 1, parameters[i] ?? "", -1, Transient);
                List<string[]> rows = new List<string[]>();
                int step;
                while ((step = sqlite3_step(statement)) == Row)
                {
                    int columns = sqlite3_column_count(statement);
                    string[] row = new string[columns];
                    for (int i = 0; i < columns; i++) row[i] = Text(statement, i);
                    rows.Add(row);
                }
                if (step != Done) throw new IOException("خواندن پایگاه کوکی فایرفاکس کامل نشد.");
                return rows;
            }
            finally
            {
                if (statement != IntPtr.Zero) sqlite3_finalize(statement);
                if (db != IntPtr.Zero) sqlite3_close(db);
            }
        }

        static string Text(IntPtr statement, int column)
        {
            IntPtr pointer = sqlite3_column_text(statement, column);
            int length = sqlite3_column_bytes(statement, column);
            if (pointer == IntPtr.Zero || length <= 0) return "";
            byte[] buffer = new byte[length];
            Marshal.Copy(pointer, buffer, 0, length);
            return Encoding.UTF8.GetString(buffer);
        }
    }
}

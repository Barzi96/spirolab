#if SPIROLAB_TEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace SpiroLab.HLine
{
    // Low-risk, local-only stage timing for the submission path. It deliberately
    // accepts only generic stage names supplied by the application; it never logs
    // patient identity, record identifiers, cookies, CSRF tokens, payloads or raw data.
    public sealed class SubmissionTiming : IDisposable
    {
        static readonly object fileGate=new object();
        readonly object gate=new object();
        readonly Dictionary<string,long> totals=new Dictionary<string,long>(StringComparer.Ordinal);
        readonly Stopwatch total=Stopwatch.StartNew();
        readonly string operation;
        bool written;

        public SubmissionTiming(string operation)
        {
            this.operation=string.IsNullOrEmpty(operation)?"submission":Safe(operation);
        }

        public static string LogPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpiroLab","logs","hline-timing.log"); }
        }

        public void Measure(string stage,Action work)
        {
            if(work==null) return;
            Stopwatch watch=Stopwatch.StartNew();
            try { work(); }
            finally { Add(stage,watch.ElapsedMilliseconds); }
        }

        public T Measure<T>(string stage,Func<T> work)
        {
            if(work==null) return default(T);
            Stopwatch watch=Stopwatch.StartNew();
            try { return work(); }
            finally { Add(stage,watch.ElapsedMilliseconds); }
        }

        public void Add(string stage,long milliseconds)
        {
            if(string.IsNullOrWhiteSpace(stage)) return;
            lock(gate)
            {
                long current;
                totals.TryGetValue(stage,out current);
                totals[stage]=checked(current+Math.Max(0,milliseconds));
            }
        }

        public string Summary
        {
            get
            {
                lock(gate)
                {
                    List<string> keys=new List<string>(totals.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    StringBuilder text=new StringBuilder();
                    foreach(string key in keys) text.Append(key).Append('=').Append(totals[key].ToString(CultureInfo.InvariantCulture)).Append("ms ");
                    return text.ToString().Trim();
                }
            }
        }

        public void Complete(string outcome)
        {
            if(written) return;
            written=true;
            try
            {
                string line=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" operation="+operation+" outcome="+Safe(outcome??"unknown")+" total="+total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture)+"ms "+(Summary.Length==0?"stages=none":Summary)+Environment.NewLine;
                string path=LogPath;
                string dir=Path.GetDirectoryName(path);
                if(!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                lock(fileGate) File.AppendAllText(path,line,new UTF8Encoding(false));
            }
            catch
            {
                // Diagnostics must never change the clinical result path.
            }
        }

        public void Dispose() { Complete("completed"); }

        static string Safe(string value)
        {
            if(string.IsNullOrEmpty(value)) return "unknown";
            StringBuilder text=new StringBuilder(value.Length);
            foreach(char c in value)
            {
                if(char.IsLetterOrDigit(c) || c=='_' || c=='-' || c=='.') text.Append(c);
                else text.Append('_');
            }
            return text.Length>80?text.ToString(0,80):text.ToString();
        }
    }
}
#endif

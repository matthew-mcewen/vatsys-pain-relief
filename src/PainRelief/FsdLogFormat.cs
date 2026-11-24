using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PainRelief
{
    /// <summary>
    /// The parts of FSD logging that don't touch vatSys: the EuroScope-style entry format, the
    /// log file name, and which old logs to delete.
    ///
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal static class FsdLogFormat
    {
        // <anything>_yyyyMMdd_HHmmss.txt: only files with this shape are ever pruned.
        private static readonly Regex LogFileName = new Regex(@"^.+_(\d{8})_(\d{6})\.txt$", RegexOptions.IgnoreCase);

        /// <summary>One log entry, as EuroScope writes it: a header line, the packet, a blank line.</summary>
        public static string Entry(DateTime utc, bool sent, string callsign, string line)
        {
            // EuroScope marks lines it received with ">>>>" and lines it sent with "<<<2".
            return string.Format(CultureInfo.InvariantCulture, "[{0:HH:mm:ss} {1} {2}]\r\n{3}\r\n\r\n",
                utc, sent ? "<<<2" : ">>>>", callsign, line);
        }

        /// <summary>The non-empty FSD packets in a chunk of raw data (one packet per line).</summary>
        public static IEnumerable<string> Packets(string data)
        {
            if (string.IsNullOrEmpty(data))
                return Enumerable.Empty<string>();
            return data.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>[Position]_[UTC date]_[UTC start time].txt, e.g. WIII_APP_20261108_151640.txt.</summary>
        public static string FileName(string callsign, DateTime startUtc)
        {
            var safe = new StringBuilder();
            foreach (char c in string.IsNullOrWhiteSpace(callsign) ? "UNKNOWN" : callsign.Trim())
                safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd}_{1:HHmmss}.txt", safe, startUtc);
        }

        /// <summary>
        /// Of the given file names, the logs to delete so that at most <paramref name="keep"/>
        /// remain: the oldest by the start time in their name. Names that aren't logs are never
        /// returned.
        /// </summary>
        public static IEnumerable<string> FilesToDelete(IEnumerable<string> fileNames, int keep)
        {
            var logs = new List<KeyValuePair<string, string>>();
            foreach (string name in fileNames)
            {
                Match m = LogFileName.Match(name ?? "");
                if (m.Success)
                    logs.Add(new KeyValuePair<string, string>(m.Groups[1].Value + m.Groups[2].Value, name));
            }

            return logs
                .OrderByDescending(l => l.Key, StringComparer.Ordinal)
                .ThenByDescending(l => l.Value, StringComparer.OrdinalIgnoreCase)
                .Skip(Math.Max(0, keep))
                .Select(l => l.Value)
                .ToList();
        }
    }
}

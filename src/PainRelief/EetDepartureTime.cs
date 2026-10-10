using System;

namespace PainRelief
{
    /// <summary>
    /// Works out a realistic departure time for an aircraft that is entering our FIR.
    ///
    /// Flight plan remarks can carry an EET/ item listing the planned elapsed time from
    /// departure to each FIR boundary, e.g. "EET/WSJC0002 WMFC0003 VVHM0055" (hours and
    /// minutes since takeoff, not clock times). If we are a centre controller for one of
    /// those FIRs and the aircraft is being handed to us now, it departed roughly that
    /// long ago: ATD = now - EET.
    ///
    /// Anything missing or malformed means "don't know", and callers get the current time.
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal static class EetDepartureTime
    {
        private const string EetIndicator = "EET/";
        private const string CentreSuffix = "CTR";
        private const int FirCodeLength = 4;
        private const int TimeLength = 4;

        /// <summary>
        /// now - EET for our FIR, or now itself if either can't be determined.
        /// </summary>
        public static DateTime DepartureTime(string myCallsign, string remarks, DateTime utcNow)
        {
            string firCode = FirCodeFromCallsign(myCallsign);
            if (firCode == null)
                return utcNow;

            TimeSpan? eet = EetForFir(remarks, firCode);
            if (eet == null)
                return utcNow;

            return utcNow - eet.Value;
        }

        /// <summary>
        /// The FIR code from a centre callsign such as WAAF_BI_CTR or VTBB_CTR: the part
        /// before the first underscore, provided it is exactly four letters and the
        /// callsign ends in _CTR. Null for anything else.
        /// </summary>
        public static string FirCodeFromCallsign(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
                return null;

            string[] parts = callsign.Trim().ToUpperInvariant().Split('_');
            if (parts.Length < 2 || parts[parts.Length - 1] != CentreSuffix)
                return null;

            string code = parts[0];
            if (code.Length != FirCodeLength)
                return null;

            foreach (char c in code)
            {
                if (c < 'A' || c > 'Z')
                    return null;
            }

            return code;
        }

        /// <summary>
        /// The elapsed time filed against the given FIR in the remarks' EET/ item, or null
        /// if there isn't one.
        /// </summary>
        public static TimeSpan? EetForFir(string remarks, string firCode)
        {
            if (string.IsNullOrWhiteSpace(remarks) || string.IsNullOrEmpty(firCode))
                return null;

            string[] tokens = remarks.ToUpperInvariant().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

            int start = Array.FindIndex(tokens, t => t.StartsWith(EetIndicator, StringComparison.Ordinal));
            if (start < 0)
                return null;

            for (int i = start; i < tokens.Length; i++)
            {
                string entry = tokens[i];
                if (i == start)
                    entry = entry.Substring(EetIndicator.Length);
                else if (entry.IndexOf('/') >= 0)
                    break; // next remarks item (SEL/, CODE/, ...): the EET list is over

                // A route can enter the same FIR more than once and nothing here says
                // which crossing this handoff is for, so the first one filed wins.
                TimeSpan? eet = ParseEntry(entry, firCode);
                if (eet != null)
                    return eet;
            }

            return null;
        }

        // An entry for our FIR is the code followed by hhmm, e.g. WAAF0623. Entries for
        // other FIRs, or for lat/long points, are just not a match.
        private static TimeSpan? ParseEntry(string entry, string firCode)
        {
            if (entry.Length != firCode.Length + TimeLength || !entry.StartsWith(firCode, StringComparison.Ordinal))
                return null;

            for (int i = firCode.Length; i < entry.Length; i++)
            {
                if (entry[i] < '0' || entry[i] > '9')
                    return null;
            }

            int hours = (entry[firCode.Length] - '0') * 10 + (entry[firCode.Length + 1] - '0');
            int minutes = (entry[firCode.Length + 2] - '0') * 10 + (entry[firCode.Length + 3] - '0');
            if (minutes > 59)
                return null;

            return new TimeSpan(hours, minutes, 0);
        }
    }
}

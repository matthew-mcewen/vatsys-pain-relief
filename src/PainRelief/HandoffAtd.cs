using System;
using System.Collections.Generic;

namespace PainRelief
{
    /// <summary>
    /// Works out the ATD that makes vatSys show a handed-off aircraft where it should be: a fixed
    /// flying time BEFORE it enters our airspace - 10 minutes for a centre (_CTR) position,
    /// 5 minutes for an approach (_APP) position.
    ///
    /// vatSys already knows where an FDR's route crosses into the sectors we control and how long
    /// each leg of the route takes, and none of that depends on the ATD. So rather than doing any
    /// geometry or speed maths here, we add up vatSys's own leg times to the boundary and set the
    /// ATD from that. Ground speed is therefore whatever vatSys uses (the FDR's TAS).
    ///
    ///   time from departure to the boundary = entry time - ATD used when we asked
    ///   wanted time from departure          = that - lead
    ///   ATD                                 = now - wanted time from departure
    ///
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal static class HandoffAtd
    {
        private const string CentreSuffix = "_CTR";
        private const string ApproachSuffix = "_APP";

        private static readonly TimeSpan CentreLead = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ApproachLead = TimeSpan.FromMinutes(5);

        // Longer than any real flight; a result beyond this means bad data, not a real ATD.
        private static readonly TimeSpan MaxElapsed = TimeSpan.FromHours(24);

        /// <summary>
        /// How long before the airspace boundary a handed-off aircraft should be shown, based on
        /// our callsign: 10 min for ..._CTR, 5 min for ..._APP, null for anything else.
        /// </summary>
        public static TimeSpan? LeadForCallsign(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
                return null;

            string c = callsign.Trim().ToUpperInvariant();
            if (c.EndsWith(CentreSuffix, StringComparison.Ordinal))
                return CentreLead;
            if (c.EndsWith(ApproachSuffix, StringComparison.Ordinal))
                return ApproachLead;
            return null;
        }

        /// <summary>
        /// The flying time from departure to the point where the route enters our airspace, picked
        /// the way vatSys picks its sector entry time (MMI.GetSectorEntryTime): the first crossing
        /// into one of our sectors that is still ahead of the aircraft, or that it is inside now
        /// (passed, but the next crossing isn't reached yet).
        /// </summary>
        /// <param name="crossings">Every sector boundary crossing on the route, in route order:
        /// the flying time from departure to it, and whether it enters a sector we control.</param>
        /// <param name="flown">How long the aircraft has been flying.</param>
        /// <returns>Null if the route has no such crossing.</returns>
        public static TimeSpan? BoundaryOffset(IList<KeyValuePair<TimeSpan, bool>> crossings, TimeSpan flown)
        {
            if (crossings == null)
                return null;

            for (int i = 0; i < crossings.Count; i++)
            {
                if (!crossings[i].Value)
                    continue;

                TimeSpan at = crossings[i].Key;
                bool ahead = flown < at;
                bool inside = i + 1 < crossings.Count && flown > at && flown < crossings[i + 1].Key;
                if (ahead || inside)
                    return at;
            }
            return null;
        }

        /// <summary>
        /// The ATD that puts the aircraft <paramref name="lead"/> short of the point where it
        /// enters our airspace.
        /// </summary>
        /// <param name="utcNow">The current time.</param>
        /// <param name="activatedAtd">The ATD the FDR currently has, which
        /// <paramref name="boundaryEntry"/> was calculated from.</param>
        /// <param name="boundaryEntry">vatSys's estimate of when the aircraft enters our sectors
        /// (DateTime.MaxValue if it never does).</param>
        /// <returns>The ATD to use, or null if there is no usable entry time. If the boundary is
        /// closer to the origin than the lead allows, the result is "now" (the aircraft cannot be
        /// placed before it took off).</returns>
        public static DateTime? AtdForBoundary(DateTime utcNow, DateTime activatedAtd, DateTime boundaryEntry, TimeSpan lead)
        {
            if (boundaryEntry == DateTime.MaxValue || boundaryEntry == DateTime.MinValue)
                return null;

            TimeSpan elapsed = (boundaryEntry - activatedAtd) - lead;
            if (elapsed < TimeSpan.Zero)
                elapsed = TimeSpan.Zero;
            if (elapsed > MaxElapsed)
                return null;

            return utcNow - elapsed;
        }
    }
}

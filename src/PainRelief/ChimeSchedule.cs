using System;

namespace PainRelief
{
    internal enum ChimeAction
    {
        /// <summary>Nothing to do yet.</summary>
        Wait,
        /// <summary>Play the chime now.</summary>
        Chime,
        /// <summary>Stop reminding about this handoff.</summary>
        GiveUp
    }

    /// <summary>
    /// When to chime about a handoff that hasn't been accepted: the first chime 5 seconds after
    /// the handoff arrives, then every 2 seconds, until it is accepted or we give up.
    ///
    /// The give-up limit is a safety net. A sender can cancel a handoff without vatSys telling us,
    /// and without a limit the chime would then never stop.
    ///
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal static class ChimeSchedule
    {
        public static readonly TimeSpan FirstChimeAfter = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan RepeatEvery = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(2);

        public static ChimeAction Decide(DateTime handoffArrived, DateTime? lastChime, DateTime now)
        {
            TimeSpan age = now - handoffArrived;
            if (age >= GiveUpAfter)
                return ChimeAction.GiveUp;
            if (age < FirstChimeAfter)
                return ChimeAction.Wait;
            if (lastChime == null)
                return ChimeAction.Chime;
            return now - lastChime.Value >= RepeatEvery ? ChimeAction.Chime : ChimeAction.Wait;
        }
    }
}

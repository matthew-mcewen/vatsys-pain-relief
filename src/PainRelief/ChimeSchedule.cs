using System;

namespace PainRelief
{
    internal enum ChimeAction
    {
        /// <summary>Nothing to do yet.</summary>
        Wait,
        /// <summary>Play the nudge now.</summary>
        Nudge,
        /// <summary>Stop reminding about this handoff.</summary>
        GiveUp
    }

    /// <summary>
    /// When to nudge about a handoff that hasn't been accepted. The incoming chime plays as the
    /// handoff arrives (not decided here). If it is still waiting FirstNudgeAfter later the nudge
    /// plays, then every NudgeEvery for the following NudgeFor. With the defaults (10 s, 10 s,
    /// 90 s) that is at 10, 20, ... 100 seconds: 10 nudges in all. All three come from
    /// PainRelief.conf.
    ///
    /// Each nudge is due at a fixed time after the handoff arrived, so a late timer tick never
    /// pushes the later ones back. The limit also means a handoff the sender cancelled without
    /// vatSys noticing can't nudge forever.
    ///
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal sealed class ChimeSchedule
    {
        public static readonly TimeSpan DefaultFirstNudgeAfter = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultNudgeEvery = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultNudgeFor = TimeSpan.FromSeconds(90);

        /// <summary>The shortest repeat allowed, so a typo can't turn the nudge into a buzz.</summary>
        public static readonly TimeSpan MinNudgeEvery = TimeSpan.FromSeconds(1);

        public static readonly ChimeSchedule Default =
            new ChimeSchedule(DefaultFirstNudgeAfter, DefaultNudgeEvery, DefaultNudgeFor);

        public TimeSpan FirstNudgeAfter { get; }
        public TimeSpan NudgeEvery { get; }
        public TimeSpan NudgeFor { get; }

        /// <summary>The first nudge plus one for every whole interval in the NudgeFor after it.</summary>
        public int MaxNudges { get; }

        /// <param name="firstNudgeAfter">From the handoff arriving to the first nudge; 0 or more.</param>
        /// <param name="nudgeEvery">Between nudges; at least 1 second.</param>
        /// <param name="nudgeFor">How long after the first nudge to keep nudging; 0 means just the one.</param>
        public ChimeSchedule(TimeSpan firstNudgeAfter, TimeSpan nudgeEvery, TimeSpan nudgeFor)
        {
            FirstNudgeAfter = firstNudgeAfter < TimeSpan.Zero ? TimeSpan.Zero : firstNudgeAfter;
            NudgeEvery = nudgeEvery < MinNudgeEvery ? MinNudgeEvery : nudgeEvery;
            NudgeFor = nudgeFor < TimeSpan.Zero ? TimeSpan.Zero : nudgeFor;
            MaxNudges = (int)Math.Min(int.MaxValue, 1 + NudgeFor.Ticks / NudgeEvery.Ticks);
        }

        /// <param name="handoffArrived">When the handoff arrived (and the incoming chime played).</param>
        /// <param name="nudgesPlayed">How many nudges have played for this handoff so far.</param>
        /// <param name="now">The current time.</param>
        public ChimeAction Decide(DateTime handoffArrived, int nudgesPlayed, DateTime now)
        {
            if (nudgesPlayed >= MaxNudges)
                return ChimeAction.GiveUp;

            TimeSpan due = FirstNudgeAfter + TimeSpan.FromTicks(NudgeEvery.Ticks * nudgesPlayed);
            return now - handoffArrived >= due ? ChimeAction.Nudge : ChimeAction.Wait;
        }

        /// <summary>
        /// How many nudges should have played by now. After a nudge, the count is set to this,
        /// so a long stall (one late tick) plays one nudge rather than a burst of catch-up nudges.
        /// </summary>
        public int NudgesDue(DateTime handoffArrived, DateTime now)
        {
            TimeSpan age = now - handoffArrived;
            if (age < FirstNudgeAfter)
                return 0;
            long due = 1 + (age - FirstNudgeAfter).Ticks / NudgeEvery.Ticks;
            return (int)Math.Min(MaxNudges, due);
        }
    }
}

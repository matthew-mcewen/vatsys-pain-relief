using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Plays the incoming chime as a handoff addressed to us arrives. If it hasn't been accepted
    /// 10 seconds later, plays the nudge, then every 10 seconds for the next 90 seconds (the
    /// defaults; see ChimeSchedule). Nudging stops as soon as the handoff is accepted, refused (by us, or
    /// cancelled by the sender), the FDR is deactivated, or someone else takes the aircraft.
    ///
    /// vatSys's strip bays are just a view of FDR data, and I know of no documented "handover-in"
    /// state to read. So this tracks the same thing from the event side: HandoffAutoActivate tells
    /// us when a handoff addressed to us arrives, and a short timer then watches each one until
    /// fdr.IsTrackedByMe says we've accepted it. Nothing here changes any FDR or any track.
    ///
    /// The sounds and timings come from PainRelief.conf (HandoffIncomingSound, HandoffNudgeSound,
    /// HandoffNudgeAfter, HandoffNudgeEvery, HandoffNudgeFor).
    /// </summary>
    internal sealed class HandoffChime
    {
        private const int TickMilliseconds = 250;

        private sealed class Pending
        {
            public FDP2.FDR Fdr;
            public string Sender;
            public DateTime Arrived;
            public int Nudges;
        }

        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private readonly object gate = new object();
        private readonly Chime incoming;
        private readonly Chime nudge;
        private readonly ChimeSchedule schedule;
        private readonly System.Threading.Timer timer;
        private int ticking;
        private bool errorReported;

        /// <param name="incoming">Played as a handoff arrives.</param>
        /// <param name="nudge">Played while it waits unaccepted.</param>
        /// <param name="schedule">When to nudge.</param>
        public HandoffChime(Chime incoming, Chime nudge, ChimeSchedule schedule)
        {
            this.incoming = incoming;
            this.nudge = nudge;
            this.schedule = schedule ?? ChimeSchedule.Default;
            timer = new System.Threading.Timer(Tick, null, TickMilliseconds, TickMilliseconds);
        }

        /// <summary>A handoff addressed to us has just arrived for this FDR.</summary>
        public void HandoffReceived(FDP2.FDR fdr, string senderCallsign)
        {
            try
            {
                if (fdr == null || fdr.IsTrackedByMe)
                    return;

                lock (gate)
                {
                    pending[fdr.Callsign] = new Pending
                    {
                        Fdr = fdr,
                        Sender = senderCallsign,
                        Arrived = DateTime.UtcNow
                    };
                }
                incoming.Play();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        /// <summary>We have rejected the handoff for this callsign; stop nudging about it.</summary>
        public void HandoffRejected(string callsign)
        {
            if (callsign == null)
                return;
            lock (gate)
                pending.Remove(callsign);
        }

        private void Tick(object state)
        {
            // Never let two ticks overlap.
            if (Interlocked.Exchange(ref ticking, 1) == 1)
                return;

            try
            {
                DateTime now = DateTime.UtcNow;
                bool play = false;

                lock (gate)
                {
                    if (pending.Count == 0)
                        return;

                    List<string> finished = new List<string>();
                    foreach (KeyValuePair<string, Pending> entry in pending)
                    {
                        Pending p = entry.Value;

                        if (p.Fdr.IsTrackedByMe)
                        {
                            finished.Add(entry.Key); // accepted
                            continue;
                        }

                        ChimeAction action = schedule.Decide(p.Arrived, p.Nudges, now);
                        if (action == ChimeAction.GiveUp)
                        {
                            finished.Add(entry.Key);
                            continue;
                        }
                        if (action != ChimeAction.Nudge)
                            continue;

                        if (!StillWaitingForUs(p))
                        {
                            finished.Add(entry.Key);
                            continue;
                        }

                        p.Nudges = schedule.NudgesDue(p.Arrived, now);
                        play = true;
                    }

                    foreach (string key in finished)
                        pending.Remove(key);
                }

                // One nudge per tick however many handoffs are waiting.
                if (play)
                    nudge.Play();
            }
            catch (Exception ex)
            {
                Report(ex);
            }
            finally
            {
                Volatile.Write(ref ticking, 0);
            }
        }

        // False once there is nothing left for us to accept:
        //  - the handoff is gone: refused by us, or cancelled by the sender (both clear IsHandoff);
        //  - the FDR itself is gone;
        //  - the FDR has been deactivated (inactive, preactive, coordinated, finished, ...);
        //  - someone other than the sender has taken the aircraft.
        // Only checked from the first nudge on: when the handoff arrives our handler runs before
        // vatSys's, so IsHandoff isn't set yet and an inactive FDR may be about to be activated.
        // An unset tracker counts as still waiting, so a quirk there can only cause an extra
        // nudge, never a missing one.
        private static bool StillWaitingForUs(Pending p)
        {
            if (!p.Fdr.IsHandoff)
                return false;
            // The FDR has been deleted (the aircraft disconnected): nothing clears IsHandoff on
            // it after that, so without this it would nudge until the schedule ran out.
            if (!FDP2.GetFDRs.Contains(p.Fdr))
                return false;
            if (p.Fdr.State < FDP2.FDR.FDRStates.STATE_UNCONTROLLED)
                return false;

            var tracker = p.Fdr.ControllerTracking;
            if (tracker != null && !string.Equals(tracker.Callsign, p.Sender, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }


        private void Report(Exception ex)
        {
            if (errorReported)
                return;
            errorReported = true;
            Errors.Add(new Exception("Handoff chime is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}

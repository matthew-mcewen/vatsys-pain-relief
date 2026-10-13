using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;
using System.Threading;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Chimes while an incoming handoff sits unaccepted: first 5 seconds after it arrives, then
    /// every 2 seconds until you accept it (see ChimeSchedule).
    ///
    /// vatSys's strip bays are just a view of FDR data, and I know of no documented "handover-in"
    /// state to read. So this tracks the same thing from the event side: HandoffAutoActivate tells
    /// us when a handoff addressed to us arrives, and a short timer then watches each one until
    /// fdr.IsTrackedByMe says we've accepted it. Nothing here changes any FDR or any track.
    ///
    /// Plays chime.wav from the folder the plugin DLL is in, or a Windows system sound if that
    /// file isn't there.
    /// </summary>
    internal sealed class HandoffChime
    {
        private const int TickMilliseconds = 250;
        private const string ChimeFileName = "chime.wav";

        private sealed class Pending
        {
            public FDP2.FDR Fdr;
            public string Sender;
            public DateTime Arrived;
            public DateTime? LastChime;
        }

        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>();
        private readonly object gate = new object();
        private readonly SoundPlayer player;
        private readonly System.Threading.Timer timer;
        private int ticking;
        private bool errorReported;

        public HandoffChime()
        {
            player = LoadPlayer();
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
            }
            catch (Exception ex)
            {
                Report(ex);
            }
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

                        ChimeAction action = ChimeSchedule.Decide(p.Arrived, p.LastChime, now);
                        if (action == ChimeAction.GiveUp)
                        {
                            finished.Add(entry.Key);
                            continue;
                        }
                        if (action != ChimeAction.Chime)
                            continue;

                        if (!StillWaitingForUs(p))
                        {
                            finished.Add(entry.Key);
                            continue;
                        }

                        p.LastChime = now;
                        play = true;
                    }

                    foreach (string key in finished)
                        pending.Remove(key);
                }

                // One chime per tick however many handoffs are waiting.
                if (play)
                    Play();
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

        // False once the flight is finished or someone other than the sender has taken it, in
        // which case there is nothing left for us to accept. An unset tracker counts as still
        // waiting, so a quirk there can only cause an extra chime, never a missing one.
        private static bool StillWaitingForUs(Pending p)
        {
            if (p.Fdr.State == FDP2.FDR.FDRStates.STATE_FINISHED)
                return false;

            var tracker = p.Fdr.ControllerTracking;
            if (tracker != null && !string.Equals(tracker.Callsign, p.Sender, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private void Play()
        {
            try
            {
                if (player != null)
                    player.Play(); // plays on its own thread; doesn't block
                else
                    SystemSounds.Asterisk.Play();
            }
            catch
            {
                // A sound problem must never get in the way of anything else.
            }
        }

        private static SoundPlayer LoadPlayer()
        {
            try
            {
                string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string file = Path.Combine(folder, ChimeFileName);
                if (!File.Exists(file))
                    return null;

                SoundPlayer p = new SoundPlayer(file);
                p.Load();
                return p;
            }
            catch
            {
                return null;
            }
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

using System;
using System.Linq;
using System.Reflection;
using RossCarlson.Vatsim.Network;
using RossCarlson.Vatsim.Network.PDU;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Plays a sound when a controller refuses a handoff we made.
    ///
    /// A refusal arrives as a "handoff cancelled" from the controller we handed off to, for an
    /// FDR we still track. That is exactly how vatSys recognises one (Network.
    /// VATSIM_HandoffCancelledReceived, where it raises "Handoff refused by ..."). The same message
    /// also arrives when a sender withdraws a handoff they made to us, but then we aren't tracking
    /// the FDR, so that never plays this sound.
    ///
    /// vatSys's handler takes the FDR out of handover straight away, after which the refusal can no
    /// longer be recognised. So, like HandoffAutoActivate, this puts its own handler at the FRONT of
    /// the network session's event by reflection. It only reads; nothing is changed. If a vatSys
    /// update renames the members it needs, one error is raised and there is simply no sound.
    ///
    /// Plays the WAV file given (HandoffRefusedSound in PainRelief.conf, refused.wav by default),
    /// or a Windows system sound if it can't be loaded.
    /// </summary>
    internal sealed class HandoffRefusedChime
    {
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // Network.Instance (internal static) -> the Network singleton
        private static readonly FieldInfo NetworkInstanceField = typeof(Network).GetField("Instance", NonPublicStatic);
        // Network.VATSIM (private) -> the FSD session, recreated on every connect
        private static readonly FieldInfo SessionField = typeof(Network).GetField("VATSIM", NonPublicInstance);
        // The compiler-generated delegate field behind the FSDSession.HandoffCancelledReceived event
        private static readonly FieldInfo CancelledEventField = typeof(FSDSession).GetField("HandoffCancelledReceived", NonPublicInstance);

        private readonly EventHandler<DataReceivedEventArgs<PDUHandoffCancelled>> handler;
        private readonly Chime sound;
        private readonly object hookLock = new object();
        private FSDSession hookedSession;
        private bool hookFailureReported;
        private bool errorReported;

        /// <param name="sound">Played when a handoff we made is refused.</param>
        public HandoffRefusedChime(Chime sound)
        {
            this.sound = sound;
            handler = OnHandoffCancelled;
            Network.Connected += (sender, e) => EnsureHooked();
            EnsureHooked();
        }

        /// <summary>
        /// Makes sure our handler is first in line on the current network session.
        /// Safe to call as often as you like; does nothing if already hooked.
        /// </summary>
        public void EnsureHooked()
        {
            try
            {
                if (NetworkInstanceField == null || SessionField == null || CancelledEventField == null)
                    throw new MissingFieldException("This version of vatSys has changed internally.");

                Network network = NetworkInstanceField.GetValue(null) as Network;
                FSDSession session = network == null ? null : SessionField.GetValue(network) as FSDSession;
                if (session == null || ReferenceEquals(session, hookedSession))
                    return;

                lock (hookLock)
                {
                    if (ReferenceEquals(session, hookedSession))
                        return;

                    Delegate existing = (Delegate)CancelledEventField.GetValue(session);
                    existing = Delegate.Remove(existing, handler);
                    CancelledEventField.SetValue(session, Delegate.Combine(handler, existing));
                    hookedSession = session;
                }
            }
            catch (Exception ex)
            {
                if (hookFailureReported)
                    return;
                hookFailureReported = true;
                Errors.Add(new Exception("Handoff refused sound is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
            }
        }

        private void OnHandoffCancelled(object sender, DataReceivedEventArgs<PDUHandoffCancelled> e)
        {
            try
            {
                PDUHandoffCancelled pdu = e.PDU;
                if (!string.Equals(pdu.To, Network.Me?.Callsign, StringComparison.OrdinalIgnoreCase))
                    return;

                // Same match vatSys makes: our FDR, in a handoff to the controller who sent this.
                FDP2.FDR fdr = FDP2.GetFDRs.FirstOrDefault(f =>
                    f.Callsign == pdu.Target &&
                    string.Equals(f.HandoffController?.Callsign, pdu.From, StringComparison.OrdinalIgnoreCase));

                if (fdr != null && fdr.IsTrackedByMe)
                    sound.Play();
            }
            catch (Exception ex)
            {
                if (errorReported)
                    return;
                errorReported = true;
                Errors.Add(new Exception("Handoff refused sound is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
            }
        }

    }
}

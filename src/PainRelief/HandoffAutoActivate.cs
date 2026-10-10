using System;
using System.Linq;
using System.Reflection;
using RossCarlson.Vatsim.Network;
using RossCarlson.Vatsim.Network.PDU;
using vatsys;

namespace PainRelief
{
    /// <summary>
    /// Activates an FDR the moment a handoff for it arrives, so the handoff isn't refused.
    ///
    /// vatSys's own handler (Network.VATSIM_HandoffReceived) refuses any handoff addressed
    /// to us whose FDR state is below STATE_UNCONTROLLED - i.e. inactive, preactive or
    /// coordinated - by immediately sending a "handoff cancelled" back to the sender.
    /// EuroScope controllers have no way of knowing our FDR wasn't activated, so from their
    /// side the handoff just bounces.
    ///
    /// There is no plugin hook for handoffs, and reacting after vatSys's handler is too
    /// late (the refusal has been sent). So this puts its own handler at the FRONT of the
    /// network session's HandoffReceived event: we activate the FDR, then vatSys's handler
    /// runs unmodified, finds an active FDR and puts it into handover-in as normal.
    ///
    /// That requires reaching three non-public members by reflection. If a vatSys update
    /// renames any of them, the hook is skipped, one error is raised, and vatSys behaves
    /// exactly as it does without this plugin.
    /// </summary>
    internal sealed class HandoffAutoActivate
    {
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // Network.Instance (internal static) -> the Network singleton
        private static readonly FieldInfo NetworkInstanceField = typeof(Network).GetField("Instance", NonPublicStatic);
        // Network.VATSIM (private) -> the FSD session, recreated on every connect
        private static readonly FieldInfo SessionField = typeof(Network).GetField("VATSIM", NonPublicInstance);
        // The compiler-generated delegate field behind the FSDSession.HandoffReceived event
        private static readonly FieldInfo HandoffEventField = typeof(FSDSession).GetField("HandoffReceived", NonPublicInstance);

        private readonly EventHandler<DataReceivedEventArgs<PDUHandoff>> handler;
        private readonly object hookLock = new object();
        private FSDSession hookedSession;
        private bool hookFailureReported;

        public HandoffAutoActivate()
        {
            handler = OnHandoffReceived;
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
                if (NetworkInstanceField == null || SessionField == null || HandoffEventField == null)
                    throw new MissingFieldException("This version of vatSys has changed internally.");

                Network network = NetworkInstanceField.GetValue(null) as Network;
                FSDSession session = network == null ? null : SessionField.GetValue(network) as FSDSession;
                if (session == null || ReferenceEquals(session, hookedSession))
                    return;

                lock (hookLock)
                {
                    if (ReferenceEquals(session, hookedSession))
                        return;

                    Delegate existing = (Delegate)HandoffEventField.GetValue(session);
                    existing = Delegate.Remove(existing, handler);
                    HandoffEventField.SetValue(session, Delegate.Combine(handler, existing));
                    hookedSession = session;
                }
            }
            catch (Exception ex)
            {
                if (hookFailureReported)
                    return;
                hookFailureReported = true;
                Errors.Add(new Exception("Handoff auto-activate is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
            }
        }

        private void OnHandoffReceived(object sender, DataReceivedEventArgs<PDUHandoff> e)
        {
            try
            {
                PDUHandoff pdu = e.PDU;

                // Only handoffs addressed to us get refused; leave everything else alone.
                // (Same comparison vatSys makes.)
                if (pdu.To != Network.Me.Callsign)
                    return;

                string callsign = pdu.Target.ToUpperInvariant();
                FDP2.FDR fdr = FDP2.GetFDRs.FirstOrDefault(f => f.Callsign == callsign);
                if (fdr == null)
                    return;

                if (fdr.State != FDP2.FDR.FDRStates.STATE_INACTIVE &&
                    fdr.State != FDP2.FDR.FDRStates.STATE_PREACTIVE &&
                    fdr.State != FDP2.FDR.FDRStates.STATE_COORDINATED)
                    return;

                // Record the sender as the tracking controller before activating. Otherwise
                // vatSys may decide nobody has the aircraft and start tracking it ourselves
                // while the other controller still owns it. vatSys sets this same value in
                // its own handler straight after ours.
                string from = pdu.From.ToUpperInvariant();
                Network network = NetworkInstanceField.GetValue(null) as Network;
                NetworkATC sendingController =
                    network?.OnlineATC.FirstOrDefault(a => a.Callsign == from)
                    ?? new NetworkATC { Callsign = from };
                fdr.ControllerTracking = sendingController;

                // Departure time: if we're a centre controller and the flight plan has an
                // EET for our FIR, the aircraft left about that long ago. Otherwise "now".
                DateTime now = DateTime.UtcNow;
                DateTime atd = now;
                try
                {
                    atd = EetDepartureTime.DepartureTime(Network.Me.Callsign, fdr.Remarks, now);
                }
                catch
                {
                    // A nicety only; never let it stop the activation.
                }

                // This is what the "activate" menu item does. An ATD in the past/present
                // makes the state change happen immediately.
                FDP2.DepartFDR(fdr, atd, true);

                // Should already be CONTROLLED at this point; guarantee it so the handoff
                // that vatSys processes next can't be refused.
                if (fdr.State < FDP2.FDR.FDRStates.STATE_UNCONTROLLED)
                    fdr.State = FDP2.FDR.FDRStates.STATE_CONTROLLED;
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not auto-activate FDR for incoming handoff. " + ex.Message, ex), PainReliefPlugin.ErrorSource);
            }
        }
    }
}

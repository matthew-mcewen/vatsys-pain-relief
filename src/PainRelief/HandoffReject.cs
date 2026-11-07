using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;

namespace PainRelief
{
    /// <summary>
    /// Adds Tools → Reject Handoff, listing every handoff addressed to us that we haven't
    /// accepted. Picking one refuses it.
    ///
    /// vatSys has no way to refuse an incoming handoff: you can only accept it or leave it. But it
    /// already has both halves of a refusal, used when it refuses a handoff for an inactive plan
    /// and when a sender cancels one:
    ///  - Network.CancelHandoff sends a "handoff cancelled" back to the sender. vatSys and
    ///    EuroScope both show that to the sender as a refused handoff, and the aircraft stays theirs.
    ///  - FDP2.CancelHandoff takes the FDR back out of handover-in on our side.
    /// This calls those two, in that order, and tells the chime to stop.
    ///
    /// Network.CancelHandoff is internal, so it is reached by reflection. If a vatSys update
    /// renames it, the menu item reports one error and does nothing else; the handoff is left as
    /// it was.
    /// </summary>
    internal sealed class HandoffReject
    {
        private const BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        // Network.Instance (internal static) -> the Network singleton
        private static readonly FieldInfo NetworkInstanceField = typeof(Network).GetField("Instance", NonPublicStatic);
        // Network.CancelHandoff(FDR, NetworkATC) (internal) -> sends the "handoff cancelled" PDU
        private static readonly MethodInfo CancelHandoffMethod = typeof(Network).GetMethod(
            "CancelHandoff", NonPublicInstance, null, new[] { typeof(FDP2.FDR), typeof(NetworkATC) }, null);

        private readonly HandoffChime chime;
        private readonly ToolStripMenuItem menu;
        private bool errorReported;

        /// <param name="chime">Told about every rejected handoff; may be null.</param>
        public HandoffReject(HandoffChime chime)
        {
            this.chime = chime;

            menu = new ToolStripMenuItem("Reject Handoff");
            // A submenu only opens (and so only raises DropDownOpening) if it has an item.
            menu.DropDownItems.Add(NothingToReject());
            menu.DropDownOpening += (sender, e) => Fill();

            MMI.AddCustomMenuItem(new CustomToolStripMenuItem(
                CustomToolStripMenuItemWindowType.Main, CustomToolStripMenuItemCategory.Tools, menu));
        }

        private void Fill()
        {
            menu.DropDownItems.Clear();
            try
            {
                List<FDP2.FDR> incoming = IncomingHandoffs();
                foreach (FDP2.FDR fdr in incoming)
                {
                    FDP2.FDR target = fdr;
                    ToolStripMenuItem item = new ToolStripMenuItem(fdr.Callsign + "  from " + fdr.ControllerTracking.Callsign);
                    item.Click += (sender, e) => Reject(target);
                    menu.DropDownItems.Add(item);
                }
            }
            catch (Exception ex)
            {
                Report(ex);
            }

            if (menu.DropDownItems.Count == 0)
                menu.DropDownItems.Add(NothingToReject());
        }

        // Handoffs addressed to us (the same test vatSys makes on arrival: To == our callsign) that
        // we haven't accepted, and whose sender we know, since the refusal goes to them.
        private static List<FDP2.FDR> IncomingHandoffs()
        {
            string me = Network.Me?.Callsign;
            if (string.IsNullOrEmpty(me))
                return new List<FDP2.FDR>();

            return FDP2.GetFDRs
                .Where(f => f.IsHandoff
                    && !f.IsTrackedByMe
                    && f.ControllerTracking != null
                    && f.HandoffSector != null
                    && string.Equals(f.HandoffSector.Callsign, me, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Callsign, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void Reject(FDP2.FDR fdr)
        {
            try
            {
                // The list may be stale by the time the click lands (accepted, cancelled, ...).
                if (!fdr.IsHandoff || fdr.IsTrackedByMe || fdr.ControllerTracking == null)
                    return;

                if (NetworkInstanceField == null || CancelHandoffMethod == null)
                    throw new MissingMemberException("This version of vatSys has changed internally.");

                Network network = NetworkInstanceField.GetValue(null) as Network;
                if (network == null)
                    throw new InvalidOperationException("vatSys's network connection is not available.");

                // Tell the sender first. If this throws, nothing has changed on our side either.
                CancelHandoffMethod.Invoke(network, new object[] { fdr, fdr.ControllerTracking });

                // FDP2.CancelHandoff restores the state from before the handoff, but only if vatSys
                // recorded one. If it didn't, the FDR would stay in handover with no handoff, so put
                // it back to what an FDR tracked by another controller is: controlled.
                if (fdr.PreviousState == FDP2.FDR.FDRStates.STATE_NULL &&
                    fdr.State == FDP2.FDR.FDRStates.STATE_HANDOVER)
                    fdr.State = FDP2.FDR.FDRStates.STATE_CONTROLLED;

                FDP2.CancelHandoff(fdr);
                chime?.HandoffRejected(fdr.Callsign);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Report(ex.InnerException);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        private static ToolStripMenuItem NothingToReject()
        {
            return new ToolStripMenuItem("No incoming handoffs") { Enabled = false };
        }

        private void Report(Exception ex)
        {
            if (errorReported)
                return;
            errorReported = true;
            Errors.Add(new Exception("Reject handoff is not working: " + ex.Message, ex), PainReliefPlugin.ErrorSource);
        }
    }
}

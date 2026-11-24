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
    /// accepted. Picking one refuses it. Also adds "Reject" under "Accept" in the menu that opens
    /// on an aircraft's label, shown only for a handoff addressed to us.
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
    ///
    /// vatSys has no plugin hook for the aircraft menu, so the item is inserted into vatSys's own
    /// menu (the private MMI.aircraftMenu). vatSys hides every item and then shows the ones that
    /// apply each time it opens the menu; ours decides for itself as the menu opens. If the menu
    /// can't be found, one error is raised and Tools → Reject Handoff still works.
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
        // MMI.aircraftMenu (private static) -> the menu that opens on an aircraft's label
        private static readonly FieldInfo AircraftMenuField = typeof(MMI).GetField("aircraftMenu", NonPublicStatic);
        private const string AcceptText = "Accept";

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

            AddToAircraftMenu();
        }

        /// <summary>Puts "Reject" straight after "Accept" in vatSys's aircraft menu.</summary>
        private void AddToAircraftMenu()
        {
            try
            {
                if (!(AircraftMenuField?.GetValue(null) is ContextMenuStrip aircraftMenu))
                    throw new MissingFieldException("MMI.aircraftMenu not found");

                // The menu's first item is the aircraft's callsign; vatSys sets its text before
                // opening the menu, so that's the aircraft the menu is for. For a track with no
                // FDR vatSys only hides that item, leaving the last aircraft's callsign in it, so
                // the callsign counts only while the item is shown. (Available, not Visible:
                // Visible is false for every item until the menu itself is on screen.)
                Func<string> menuCallsign = () =>
                    aircraftMenu.Items.Count > 0 && aircraftMenu.Items[0].Available ? aircraftMenu.Items[0].Text : null;
                PropertyInfo asmgcsProperty = aircraftMenu.GetType().GetProperty("ASMGCS");

                ToolStripMenuItem reject = new ToolStripMenuItem("Reject") { Visible = false };
                reject.Click += (sender, e) =>
                {
                    FDP2.FDR fdr = IncomingHandoff(menuCallsign());
                    if (fdr != null)
                        Reject(fdr);
                };
                aircraftMenu.Opening += (sender, e) =>
                {
                    try
                    {
                        bool asmgcs = asmgcsProperty?.GetValue(aircraftMenu) is bool b && b;
                        reject.Visible = !asmgcs && IncomingHandoff(menuCallsign()) != null;
                    }
                    catch (Exception ex)
                    {
                        reject.Visible = false;
                        Report(ex);
                    }
                };

                int accept = -1;
                for (int i = 0; i < aircraftMenu.Items.Count; i++)
                    if (aircraftMenu.Items[i].Text == AcceptText)
                        accept = i;
                if (accept < 0)
                    aircraftMenu.Items.Add(reject);
                else
                    aircraftMenu.Items.Insert(accept + 1, reject);
            }
            catch (Exception ex)
            {
                Report(ex);
            }
        }

        /// <summary>The unaccepted handoff to us for this callsign, or null.</summary>
        private static FDP2.FDR IncomingHandoff(string callsign)
        {
            if (string.IsNullOrEmpty(callsign))
                return null;
            return IncomingHandoffs().FirstOrDefault(f => string.Equals(f.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
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

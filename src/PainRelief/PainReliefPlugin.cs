using System;
using System.ComponentModel.Composition;
using vatsys;
using vatsys.Plugin;

namespace PainRelief
{
    /// <summary>
    /// Plugin entry point. vatSys discovers this through the MEF [Export] attribute and
    /// creates one instance at startup. Each pain-point fix lives in its own class and is
    /// wired up here.
    /// </summary>
    [Export(typeof(IPlugin))]
    public class PainReliefPlugin : IPlugin
    {
        internal const string ErrorSource = "Pain Relief";

        private readonly HandoffAutoActivate handoffAutoActivate;
        private readonly HandoffChime handoffChime;

        public string Name => "Pain Relief";

        public PainReliefPlugin()
        {
            try
            {
                handoffChime = new HandoffChime();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start handoff chime. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                handoffAutoActivate = new HandoffAutoActivate(handoffChime);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start handoff auto-activate. " + ex.Message, ex), ErrorSource);
            }
        }

        public void OnFDRUpdate(FDP2.FDR updated)
        {
            // FDR updates arrive constantly while connected, which makes this a cheap
            // safety net for re-installing the hook if the network session was replaced.
            handoffAutoActivate?.EnsureHooked();
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
        }
    }
}

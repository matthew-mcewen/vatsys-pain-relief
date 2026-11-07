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

        /// <summary>
        /// The plugin's version (major.minor.patch), read from the built assembly and shown in
        /// the plugin's name in vatSys. The one place to change it is &lt;Version&gt; in
        /// PainRelief.csproj.
        /// </summary>
        public static readonly string Version = typeof(PainReliefPlugin).Assembly.GetName().Version.ToString(3);

        private readonly HandoffAutoActivate handoffAutoActivate;
        private readonly HandoffChime handoffChime;
        private readonly VscsCallsignLimit vscsCallsignLimit;
        private readonly AboutWindowDpiFix aboutWindowDpiFix;
        private readonly SectorWindowDpiFix sectorWindowDpiFix;

        private readonly DisplaySettingsWindowDpiFix displaySettingsWindowDpiFix;
        private readonly VscsDpiFix vscsDpiFix;
        private readonly AudioSettingsWindowDpiFix audioSettingsWindowDpiFix;

        public string Name => "Pain Relief v" + Version;

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

            try
            {
                vscsCallsignLimit = new VscsCallsignLimit();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start VSCS callsign limit removal. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                aboutWindowDpiFix = new AboutWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start About window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                sectorWindowDpiFix = new SectorWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Sector Configuration Window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                displaySettingsWindowDpiFix = new DisplaySettingsWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Display Settings window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                vscsDpiFix = new VscsDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start VSCS scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                audioSettingsWindowDpiFix = new AudioSettingsWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Audio Settings window scaling fix. " + ex.Message, ex), ErrorSource);
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
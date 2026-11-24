using System;
using System.ComponentModel.Composition;
using vatsys;
using vatsys.Plugin;

namespace PainRelief
{
    /// <summary>
    /// Plugin entry point. vatSys discovers this through the MEF [Export] attribute and
    /// creates one instance at startup. Each pain-point fix lives in its own class and is
    /// wired up here, if PainRelief.conf has it switched on (read once, at startup).
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
        private readonly HandoffReject handoffReject;
        private readonly HandoffRefusedChime handoffRefusedChime;
        private readonly VscsCallsignLimit vscsCallsignLimit;
        private readonly AboutWindowDpiFix aboutWindowDpiFix;
        private readonly SectorWindowDpiFix sectorWindowDpiFix;

        private readonly DisplaySettingsWindowDpiFix displaySettingsWindowDpiFix;
        private readonly VscsDpiFix vscsDpiFix;
        private readonly StripPanelDpiFix stripPanelDpiFix;
        private readonly AudioSettingsWindowDpiFix audioSettingsWindowDpiFix;
        private readonly FsdLogger fsdLogger;

        public string Name => "Pain Relief v" + Version;

        public PainReliefPlugin()
        {
            string folder = System.IO.Path.GetDirectoryName(typeof(PainReliefPlugin).Assembly.Location) ?? "";
            string configPath = System.IO.Path.Combine(folder, PainReliefConfig.FileName);
            CreateConfigIfMissing(configPath);
            PainReliefConfig config = PainReliefConfig.Load(configPath);

            try
            {
                if (config.HandoffChime)
                    handoffChime = new HandoffChime(
                        new Chime(config.HandoffIncomingSound(folder), System.Media.SystemSounds.Asterisk),
                        new Chime(config.HandoffNudgeSound(folder), System.Media.SystemSounds.Exclamation),
                        config.NudgeSchedule);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start handoff chime. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                // Also the source of incoming handoffs for the chime, so it's needed for either.
                if (config.HandoffAutoActivate || handoffChime != null)
                    handoffAutoActivate = new HandoffAutoActivate(handoffChime, config.HandoffAutoActivate);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start handoff auto-activate. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                handoffRefusedChime = new HandoffRefusedChime(
                    new Chime(config.HandoffRefusedSound(folder), System.Media.SystemSounds.Hand));
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start handoff refused sound. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                handoffReject = new HandoffReject(handoffChime);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start reject handoff. " + ex.Message, ex), ErrorSource);
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
                if (config.ScalingFixes)
                    aboutWindowDpiFix = new AboutWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start About window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                if (config.ScalingFixes)
                    sectorWindowDpiFix = new SectorWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Sector Configuration Window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                if (config.ScalingFixes)
                    displaySettingsWindowDpiFix = new DisplaySettingsWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Display Settings window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                if (config.ScalingFixes)
                    vscsDpiFix = new VscsDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start VSCS scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                if (config.ScalingFixes)
                    stripPanelDpiFix = new StripPanelDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start strip window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                if (config.ScalingFixes)
                    audioSettingsWindowDpiFix = new AudioSettingsWindowDpiFix();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start Audio Settings window scaling fix. " + ex.Message, ex), ErrorSource);
            }

            try
            {
                fsdLogger = new FsdLogger(folder, configPath);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not start FSD logging. " + ex.Message, ex), ErrorSource);
            }
        }

        /// <summary>Writes out the default PainRelief.conf (embedded in the DLL) if there is none.</summary>
        private static void CreateConfigIfMissing(string path)
        {
            try
            {
                if (System.IO.File.Exists(path))
                    return;
                using (System.IO.Stream source = typeof(PainReliefPlugin).Assembly.GetManifestResourceStream(PainReliefConfig.FileName))
                using (System.IO.FileStream target = System.IO.File.Create(path))
                    source?.CopyTo(target);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not create " + path + ": " + ex.Message, ex), ErrorSource);
            }
        }

        public void OnFDRUpdate(FDP2.FDR updated)
        {
            // FDR updates arrive constantly while connected, which makes this a cheap
            // safety net for re-installing the hook if the network session was replaced.
            handoffAutoActivate?.EnsureHooked();
            handoffRefusedChime?.EnsureHooked();
            fsdLogger?.EnsureHooked();
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
        }
    }
}
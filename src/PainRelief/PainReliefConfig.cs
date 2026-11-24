using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PainRelief
{
    /// <summary>
    /// The settings in PainRelief.conf: one "Key=Value" per line, keys case-insensitive, lines
    /// starting with # or ; ignored. A missing or unreadable value falls back to its default, so a
    /// broken or older file never stops the plugin.
    ///
    /// Deliberately free of vatSys types so it can be tested on its own.
    /// </summary>
    internal sealed class PainReliefConfig
    {
        public const string FileName = "PainRelief.conf";

        public const string ScalingFixesKey = "ScalingFixes";
        public const string HandoffAutoActivateKey = "HandoffAutoActivate";
        public const string HandoffChimeKey = "HandoffChime";
        public const string HandoffIncomingSoundKey = "HandoffIncomingSound";
        public const string HandoffNudgeSoundKey = "HandoffNudgeSound";
        public const string HandoffRefusedSoundKey = "HandoffRefusedSound";
        public const string HandoffNudgeAfterKey = "HandoffNudgeAfter";
        public const string HandoffNudgeEveryKey = "HandoffNudgeEvery";
        public const string HandoffNudgeForKey = "HandoffNudgeFor";
        public const string FsdLogKey = "FsdLog";
        public const string FsdLogMaxFilesKey = "FsdLogMaxFiles";

        public const string DefaultSoundValue = "default";
        public const string NoSoundValue = "none";
        /// <summary>What a sound setting of "none" resolves to: play nothing.</summary>
        public const string NoSound = "";
        public const string PackagedIncomingSound = "incoming.wav";
        public const string PackagedNudgeSound = "nudge.wav";
        public const string PackagedRefusedSound = "refused.wav";
        /// <summary>What HandoffRefusedSound=default plays: vatSys's own Tone3.</summary>
        public const string DefaultRefusedSound = @"C:\Program Files (x86)\vatSys\wav\Tone3.wav";
        public const int DefaultFsdLogMaxFiles = 10;

        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static PainReliefConfig Parse(string text)
        {
            var config = new PainReliefConfig();
            if (text == null)
                return config;

            foreach (string raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal))
                    continue;
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                string key = line.Substring(0, eq).Trim();
                // The first occurrence wins, as a reader skimming the file would expect.
                if (!config.values.ContainsKey(key))
                    config.values[key] = line.Substring(eq + 1).Trim();
            }
            return config;
        }

        /// <summary>Reads the file, or returns all defaults if it is missing or unreadable.</summary>
        public static PainReliefConfig Load(string path)
        {
            try
            {
                return Parse(File.Exists(path) ? File.ReadAllText(path) : null);
            }
            catch
            {
                return Parse(null);
            }
        }

        public bool ScalingFixes => GetBool(ScalingFixesKey, true);
        public bool HandoffAutoActivate => GetBool(HandoffAutoActivateKey, true);
        public bool HandoffChime => GetBool(HandoffChimeKey, true);
        public bool FsdLog => GetBool(FsdLogKey, true);

        /// <summary>How many FSD logs to keep; 0 (or less) means none, so no logging.</summary>
        public int FsdLogMaxFiles
        {
            get
            {
                string value = GetString(FsdLogMaxFilesKey);
                return value != null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                    ? Math.Max(0, n)
                    : DefaultFsdLogMaxFiles;
            }
        }

        /// <summary>
        /// When to nudge about an unaccepted handoff: HandoffNudgeAfter, HandoffNudgeEvery and
        /// HandoffNudgeFor, in seconds (decimals allowed). One that is missing or not a number
        /// uses its default; ChimeSchedule keeps them in range.
        /// </summary>
        public ChimeSchedule NudgeSchedule => new ChimeSchedule(
            GetSeconds(HandoffNudgeAfterKey, ChimeSchedule.DefaultFirstNudgeAfter),
            GetSeconds(HandoffNudgeEveryKey, ChimeSchedule.DefaultNudgeEvery),
            GetSeconds(HandoffNudgeForKey, ChimeSchedule.DefaultNudgeFor));

        /// <summary>
        /// The WAV file played as a handoff to us arrives, or null for the Windows system sound
        /// (HandoffIncomingSound=default).
        /// </summary>
        public string HandoffIncomingSound(string pluginFolder) =>
            ResolveSound(GetString(HandoffIncomingSoundKey), pluginFolder, PackagedIncomingSound, null);

        /// <summary>
        /// The WAV file played while a handoff to us waits unaccepted, or null for the Windows
        /// system sound (HandoffNudgeSound=default).
        /// </summary>
        public string HandoffNudgeSound(string pluginFolder) =>
            ResolveSound(GetString(HandoffNudgeSoundKey), pluginFolder, PackagedNudgeSound, null);

        /// <summary>The WAV file to play when a handoff we made is refused.</summary>
        public string HandoffRefusedSound(string pluginFolder) =>
            ResolveSound(GetString(HandoffRefusedSoundKey), pluginFolder, PackagedRefusedSound, DefaultRefusedSound);

        /// <summary>
        /// A sound setting as a full path: empty means the packaged file, "default" the given
        /// default (may be null), "none" NoSound, a relative path is taken from the plugin's folder.
        /// </summary>
        public static string ResolveSound(string value, string pluginFolder, string packagedFile, string defaultFile)
        {
            if (value != null && value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
                value = value.Substring(1, value.Length - 2).Trim();
            if (string.IsNullOrEmpty(value))
                value = packagedFile;
            if (string.Equals(value, DefaultSoundValue, StringComparison.OrdinalIgnoreCase))
                return defaultFile;
            if (string.Equals(value, NoSoundValue, StringComparison.OrdinalIgnoreCase))
                return NoSound;
            try
            {
                return Path.IsPathRooted(value) ? value : Path.Combine(pluginFolder ?? "", value);
            }
            catch (ArgumentException)
            {
                return defaultFile; // invalid characters in the path
            }
        }

        /// <summary>A number of seconds, or the fallback if it's missing, not a number, or huge.</summary>
        public TimeSpan GetSeconds(string key, TimeSpan fallback)
        {
            string value = GetString(key);
            if (value != null
                && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
                && !double.IsNaN(seconds) && Math.Abs(seconds) <= TimeSpan.FromDays(1).TotalSeconds)
                return TimeSpan.FromSeconds(seconds);
            return fallback;
        }

        public string GetString(string key) => values.TryGetValue(key, out string value) ? value : null;

        /// <summary>true/false, on/off, yes/no or 1/0; anything else gives the default.</summary>
        public bool GetBool(string key, bool fallback)
        {
            switch ((GetString(key) ?? "").ToLowerInvariant())
            {
                case "true":
                case "on":
                case "yes":
                case "1":
                    return true;
                case "false":
                case "off":
                case "no":
                case "0":
                    return false;
                default:
                    return fallback;
            }
        }
    }
}

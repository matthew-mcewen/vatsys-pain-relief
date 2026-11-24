using System.IO;
using System.Media;

namespace PainRelief
{
    /// <summary>
    /// One configurable sound: a WAV file, a Windows system sound, or nothing. Built from a sound
    /// setting resolved by PainReliefConfig: a path plays that file, null plays the system sound,
    /// and PainReliefConfig.NoSound plays nothing. A file that is missing or won't load falls back
    /// to the system sound, so a typo in the setting never leaves you without a sound.
    /// </summary>
    internal sealed class Chime
    {
        private readonly SoundPlayer player;
        private readonly SystemSound fallback;

        public Chime(string resolvedSetting, SystemSound fallback)
        {
            this.fallback = resolvedSetting == PainReliefConfig.NoSound ? null : fallback;
            player = Load(resolvedSetting);
        }

        public void Play()
        {
            try
            {
                if (player != null)
                    player.Play(); // plays on its own thread; doesn't block
                else
                    fallback?.Play();
            }
            catch
            {
                // A sound problem must never get in the way of anything else.
            }
        }

        private static SoundPlayer Load(string file)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file))
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
    }
}

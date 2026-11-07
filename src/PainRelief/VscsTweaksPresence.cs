using System;

namespace PainRelief
{
    /// <summary>
    /// Tells the VSCS fixes in this plugin whether a VSCS Tweaks plugin that has them is loaded.
    /// VSCS Tweaks has the same fixes and takes precedence, so when it is loaded Pain Relief
    /// leaves the VSCS to it.
    ///
    /// Only VSCS Tweaks 1.2.0 or later counts: that is the release that added the scaling fix
    /// and the callsign limit. An older one would leave both undone if this plugin stood aside.
    ///
    /// vatSys may load VSCS Tweaks after this plugin, so this watches assemblies as they load
    /// rather than checking once at startup. The answer is a cheap flag, safe to read on every
    /// repaint.
    /// </summary>
    internal static class VscsTweaksPresence
    {
        private const string VscsTweaksAssembly = "VscsTweaksPlugin";
        private static readonly Version FirstVersionWithFixes = new Version(1, 2, 0);

        private static volatile bool loaded;

        static VscsTweaksPresence()
        {
            // Subscribe before scanning, so an assembly loading in between is not missed.
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) => Check(args.LoadedAssembly);
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                Check(assembly);
        }

        public static bool IsLoaded => loaded;

        private static void Check(System.Reflection.Assembly assembly)
        {
            System.Reflection.AssemblyName name = assembly.GetName();
            if (string.Equals(name.Name, VscsTweaksAssembly, StringComparison.OrdinalIgnoreCase)
                && name.Version >= FirstVersionWithFixes)
                loaded = true;
        }
    }
}

using System.IO;
using BepInEx;

namespace ModSyncer
{
    /// <summary>
    /// Where downloaded-but-not-yet-installed mods wait for the next game launch.
    ///
    /// This file is compiled into BOTH the plugin (which writes here) and the patcher (which
    /// reads here), so the two always agree on the folder layout:
    ///
    ///   BepInEx/ModSyncer/staging/plugins/Author-Name/...   -> becomes BepInEx/plugins/Author-Name/
    ///   BepInEx/ModSyncer/staging/patchers/Author-Name/...  -> becomes BepInEx/patchers/Author-Name/
    ///   BepInEx/ModSyncer/staging/config/...                -> copied into BepInEx/config/ (existing files kept)
    ///
    /// <see cref="Paths"/> is BepInEx's helper that knows where its folders are, which also
    /// makes this work when a mod manager such as r2modman keeps BepInEx outside the game folder.
    /// </summary>
    internal static class StagingPaths
    {
        public static string Root => Path.Combine(Paths.BepInExRootPath, "ModSyncer", "staging");
        public static string Plugins => Path.Combine(Root, "plugins");
        public static string Patchers => Path.Combine(Root, "patchers");
        public static string Config => Path.Combine(Root, "config");
    }
}

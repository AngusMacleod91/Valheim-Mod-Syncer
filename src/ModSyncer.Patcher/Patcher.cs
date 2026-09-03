using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace ModSyncer.Patcher
{
    /// <summary>
    /// A BepInEx "preloader patcher". BepInEx runs these before it loads any plugin, which is
    /// the one moment no mod DLL is locked by the game. We use that moment to move whatever the
    /// plugin downloaded last session (see StagingPaths) into the real plugins/patchers/config
    /// folders.
    ///
    /// BepInEx's contract for a patcher class: a static TargetDLLs property listing which game
    /// DLLs it wants to rewrite, and a static Patch method that rewrites them. We rewrite
    /// nothing, so TargetDLLs is empty and Patch is never called; all the work happens in
    /// Initialize(), which BepInEx calls first.
    /// </summary>
    public static class StagingPatcher
    {
        private static readonly ManualLogSource Log = Logger.CreateLogSource("ModSyncer.Patcher");

        public static IEnumerable<string> TargetDLLs { get { yield break; } }

        public static void Patch(AssemblyDefinition assembly)
        {
            // Never called: TargetDLLs is empty. Required so BepInEx recognises this class as a patcher.
        }

        public static void Initialize()
        {
            try
            {
                ApplyStaging();
            }
            catch (Exception ex)
            {
                Log.LogError("Applying staged mods failed: " + ex);
            }
        }

        private static void ApplyStaging()
        {
            string root = StagingPaths.Root;
            if (!Directory.Exists(root)) return;

            int moved = 0;
            moved += ReplaceFolders(StagingPaths.Plugins, Paths.PluginPath);
            moved += ReplaceFolders(StagingPaths.Patchers, Paths.PatcherPluginPath);
            moved += CopyNewFiles(StagingPaths.Config, Paths.ConfigPath);

            try { Directory.Delete(root, true); }
            catch (Exception ex) { Log.LogWarning("Could not clean up staging folder: " + ex.Message); }

            if (moved > 0) Log.LogInfo($"Applied {moved} staged item(s). Mods are now up to date with the server.");
        }

        /// <summary>For each Author-Name folder in staging: delete the installed copy, move the new one in.</summary>
        private static int ReplaceFolders(string stagingDir, string destRoot)
        {
            if (!Directory.Exists(stagingDir)) return 0;
            Directory.CreateDirectory(destRoot);

            int count = 0;
            foreach (string src in Directory.GetDirectories(stagingDir))
            {
                string name = Path.GetFileName(src);
                string dest = Path.Combine(destRoot, name);
                try
                {
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    Directory.Move(src, dest);
                    Log.LogInfo($"Installed {name} -> {dest}");
                    count++;
                }
                catch (Exception ex)
                {
                    // Most likely cause: a file in the old folder is locked (this patcher updating itself, for instance).
                    // Fall back to copying file-by-file so as much as possible is updated, and report what was not.
                    Log.LogWarning($"Could not replace {name} cleanly ({ex.Message}); copying files individually.");
                    count += CopyTree(src, dest, overwrite: true) > 0 ? 1 : 0;
                }
            }
            return count;
        }

        /// <summary>Config files: only add ones the player does not already have, so their settings survive.</summary>
        private static int CopyNewFiles(string stagingDir, string destRoot)
        {
            if (!Directory.Exists(stagingDir)) return 0;
            return CopyTree(stagingDir, destRoot, overwrite: false);
        }

        private static int CopyTree(string srcDir, string destDir, bool overwrite)
        {
            int copied = 0;
            Directory.CreateDirectory(destDir);
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(srcDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dest = Path.Combine(destDir, rel);
                if (!overwrite && File.Exists(dest)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.Copy(file, dest, overwrite);
                    copied++;
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Could not write {dest}: {ex.Message}");
                }
            }
            return copied;
        }
    }
}

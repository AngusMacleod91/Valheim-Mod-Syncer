using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using BepInEx;

namespace ModSyncer
{
    /// <summary>
    /// Works out which Thunderstore mods are installed by looking at the plugins folder.
    ///
    /// Mod managers (r2modman, Thunderstore Mod Manager) and this mod's own downloader all use
    /// the same layout: one folder per mod named "Author-Name" containing the mod's files plus
    /// the manifest.json from its Thunderstore package. That manifest tells us the exact
    /// version. Loose DLLs dropped straight into the plugins folder cannot be identified and are
    /// ignored, which is why the README asks everyone to install through a mod manager.
    /// </summary>
    internal static class InstalledMods
    {
        private static readonly Regex NameRegex = new Regex("\"name\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);
        private static readonly Regex VersionRegex = new Regex("\"version_number\"\\s*:\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        /// <summary>Mods installed for this game instance (whatever BepInEx says its plugins folder is).</summary>
        public static List<ModRef> Scan() => ScanFolder(Paths.PluginPath);

        /// <summary>Scan any folder laid out like a plugins folder (also used for the staging area).</summary>
        public static List<ModRef> ScanFolder(string pluginsFolder)
        {
            var result = new List<ModRef>();
            if (string.IsNullOrEmpty(pluginsFolder) || !Directory.Exists(pluginsFolder)) return result;

            foreach (string dir in Directory.GetDirectories(pluginsFolder))
            {
                string manifestPath = Path.Combine(dir, "manifest.json");
                if (!File.Exists(manifestPath)) continue;

                string folder = Path.GetFileName(dir);
                string json;
                try { json = File.ReadAllText(manifestPath); }
                catch (Exception ex) { Plugin.Log.LogWarning($"Could not read {manifestPath}: {ex.Message}"); continue; }

                Match nameMatch = NameRegex.Match(json);
                Match versionMatch = VersionRegex.Match(json);
                if (!nameMatch.Success || !versionMatch.Success)
                {
                    Plugin.Log.LogWarning($"manifest.json in '{folder}' has no name/version_number; skipping.");
                    continue;
                }

                string name = nameMatch.Groups[1].Value;
                string version = versionMatch.Groups[1].Value;

                // The manifest has no author field, so the namespace comes from the folder name "Author-Name".
                string ns;
                if (folder.EndsWith("-" + name, StringComparison.OrdinalIgnoreCase) && folder.Length > name.Length + 1)
                {
                    ns = folder.Substring(0, folder.Length - name.Length - 1);
                }
                else
                {
                    int dash = folder.IndexOf('-');
                    ns = dash > 0 ? folder.Substring(0, dash) : "Unknown";
                    Plugin.Log.LogWarning($"Folder '{folder}' does not end with '-{name}'; guessing namespace '{ns}'.");
                }

                result.Add(new ModRef(ns, name, version));
            }
            return result;
        }

        public static string Describe(IEnumerable<ModRef> mods)
        {
            var lines = new List<string>();
            foreach (ModRef m in mods) lines.Add("  " + m.DependencyString);
            return lines.Count == 0 ? "  (none found)" : string.Join(Environment.NewLine, lines);
        }
    }
}

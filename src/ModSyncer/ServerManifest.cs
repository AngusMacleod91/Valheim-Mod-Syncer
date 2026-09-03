using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace ModSyncer
{
    /// <summary>
    /// Builds the server's rule book from two sources:
    ///   1. Every mod installed on the server (if Server.EnforceInstalledMods is on) -> side Both.
    ///   2. Lines in BepInEx/config/ModSyncer.extra-mods.txt, which can add client-only mods,
    ///      mark server-only mods, or pin a different version. These override source 1.
    /// The result is cached until the server restarts, unless Server.RescanEveryConnection is on.
    /// </summary>
    internal static class ServerManifest
    {
        public static string ExtraModsPath => Path.Combine(Paths.ConfigPath, "ModSyncer.extra-mods.txt");

        private static Manifest _cached;

        public static Manifest Get()
        {
            if (_cached != null && !Plugin.RescanEveryConnection.Value) return _cached;
            _cached = Build();
            return _cached;
        }

        public static void Invalidate() => _cached = null;

        private static Manifest Build()
        {
            var entries = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string s in (Plugin.IgnoreMods.Value ?? "").Split(','))
                if (s.Trim().Length > 0) ignored.Add(s.Trim());

            // Mod Syncer itself is not on Thunderstore, so clients could never download it. Its
            // protocol version check already guards compatibility, so never enforce it unless the
            // host explicitly turns that on (only sensible once it is published on Thunderstore).
            string self = PluginVersion.ThunderstoreNamespace + "-" + PluginVersion.ThunderstoreName;
            if (!Plugin.EnforceSyncerVersion.Value) ignored.Add(self);

            if (Plugin.EnforceInstalledMods.Value)
            {
                foreach (ModRef m in InstalledMods.Scan())
                {
                    if (ignored.Contains(m.FullName))
                    {
                        Plugin.Log.LogInfo($"Not enforcing {m.FullName} ({(m.FullName.Equals(self, StringComparison.OrdinalIgnoreCase) ? "Mod Syncer itself" : "listed in Server.IgnoreMods")}).");
                        continue;
                    }
                    entries[m.FullName] = new ManifestEntry(m, ModSide.Both);
                }
            }

            EnsureExtraModsFileExists();
            if (File.Exists(ExtraModsPath))
            {
                int lineNo = 0;
                foreach (string raw in File.ReadAllLines(ExtraModsPath))
                {
                    lineNo++;
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;

                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (!ModRef.TryParse(parts[0], out ModRef mod))
                    {
                        Plugin.Log.LogWarning($"{ExtraModsPath} line {lineNo}: cannot understand '{parts[0]}' (expected Namespace-Name-Version).");
                        continue;
                    }
                    ModSide side = ModSide.Both;
                    if (parts.Length > 1 && !ModSideExtensions.TryParseSide(parts[1], out side))
                        Plugin.Log.LogWarning($"{ExtraModsPath} line {lineNo}: unknown side '{parts[1]}' (use both, client or server). Assuming both.");

                    entries[mod.FullName] = new ManifestEntry(mod, side);
                }
            }

            var manifest = new Manifest();
            manifest.Entries.AddRange(entries.Values);
            manifest.Entries.Sort((a, b) => string.Compare(a.Mod.FullName, b.Mod.FullName, StringComparison.OrdinalIgnoreCase));

            Plugin.Log.LogInfo($"Server is enforcing {manifest.Entries.Count} mod(s):{Environment.NewLine}{manifest.Describe()}");
            return manifest;
        }

        private static void EnsureExtraModsFileExists()
        {
            if (File.Exists(ExtraModsPath)) return;
            try
            {
                string[] template =
                {
                    "# Valheim Mod Syncer - extra mods to enforce on connecting players.",
                    "#",
                    "# By default every mod installed on this server is required on every client at the same",
                    "# version. Use this file for anything that rule cannot express:",
                    "#   - mods that clients need but the server does not run (UI mods, etc.)",
                    "#   - mods that only the server runs, so clients are NOT asked for them",
                    "#   - pinning a different version than the one installed here",
                    "#",
                    "# One mod per line:   Namespace-Name-Version   [both|client|server]",
                    "# The Namespace-Name-Version text is exactly what Thunderstore shows as the dependency string.",
                    "#",
                    "# Examples (remove the leading # to activate):",
                    "#ValheimModding-Jotunn-2.20.0 both",
                    "#Azumatt-MinimalUI-2.3.9 client",
                    "#Smoothbrain-ServerCharacters-1.5.6 server",
                    "",
                };
                File.WriteAllLines(ExtraModsPath, template);
                Plugin.Log.LogInfo("Created " + ExtraModsPath);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not create " + ExtraModsPath + ": " + ex.Message);
            }
        }
    }
}

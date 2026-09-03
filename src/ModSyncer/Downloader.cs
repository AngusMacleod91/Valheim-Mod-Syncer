using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityEngine.Networking;

namespace ModSyncer
{
    /// <summary>
    /// Downloads exact mod versions from Thunderstore and unpacks them into the staging folder.
    ///
    /// Why staging and not straight into plugins? Because the game has already loaded the old
    /// versions, and Windows will not let a loaded DLL be overwritten. The patcher project moves
    /// staged files into place on the next launch, before anything is loaded.
    ///
    /// Downloads run as a Unity "coroutine": a method that can pause with `yield return` while
    /// the game keeps rendering, then resume on the next frame. UnityWebRequest is Unity's HTTP
    /// client and handles HTTPS for us.
    /// </summary>
    internal static class Downloader
    {
        public static bool Running { get; private set; }
        public static bool LastRunFailed { get; private set; }
        public static List<string> Failures { get; } = new List<string>();

        public static void Start(List<ModRef> mods)
        {
            if (Running)
            {
                Plugin.Log.LogInfo("A download is already running; not starting another.");
                return;
            }
            Plugin.Instance.StartCoroutine(Run(mods));
        }

        private static IEnumerator Run(List<ModRef> mods)
        {
            Running = true;
            LastRunFailed = false;
            Failures.Clear();

            for (int i = 0; i < mods.Count; i++)
            {
                ModRef mod = mods[i];
                Plugin.Log.LogInfo($"Downloading {mod.DependencyString} ({i + 1}/{mods.Count}) from {mod.DownloadUrl}");

                using (UnityWebRequest req = UnityWebRequest.Get(mod.DownloadUrl))
                {
                    req.timeout = 180; // seconds
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        string msg = $"{mod.DependencyString}: {req.error} (HTTP {req.responseCode})";
                        Plugin.Log.LogError("Download failed: " + msg);
                        Failures.Add(msg);
                        continue;
                    }

                    byte[] zipBytes = req.downloadHandler.data;
                    try
                    {
                        Stage(mod, zipBytes);
                        Plugin.Log.LogInfo($"Staged {mod.DependencyString} ({zipBytes.Length / 1024} KB).");
                    }
                    catch (Exception ex)
                    {
                        string msg = $"{mod.DependencyString}: could not unpack ({ex.Message})";
                        Plugin.Log.LogError(msg);
                        Failures.Add(msg);
                    }
                }
            }

            Running = false;
            LastRunFailed = Failures.Count > 0;

            if (LastRunFailed)
            {
                ClientUI.Show("Mod download failed",
                    $"{mods.Count - Failures.Count} of {mods.Count} mod(s) downloaded.\n\nFailed:\n" + string.Join("\n", Failures) +
                    "\n\nCheck your internet connection and try connecting again.");
            }
            else
            {
                ClientUI.Show("Restart required",
                    $"Downloaded {mods.Count} mod update(s) for this server.\n\nQuit Valheim completely, start it again, and reconnect.");
            }
        }

        /// <summary>
        /// Unpack a Thunderstore zip into staging using the same rules mod managers use:
        ///   files under plugins/  -> plugins/Author-Name/...
        ///   files under patchers/ -> patchers/Author-Name/...
        ///   files under config/   -> config/...
        ///   files at the zip root (the DLL, manifest.json, icon, README) -> plugins/Author-Name/
        ///   anything else         -> plugins/Author-Name/<same relative path>
        /// A leading "BepInEx/" folder inside the zip is ignored.
        /// </summary>
        private static void Stage(ModRef mod, byte[] zipBytes)
        {
            string modFolder = mod.FullName;
            string pluginDest = Path.Combine(StagingPaths.Plugins, modFolder);
            string patcherDest = Path.Combine(StagingPaths.Patchers, modFolder);

            // Start clean so files from an older staged version cannot linger.
            if (Directory.Exists(pluginDest)) Directory.Delete(pluginDest, true);
            if (Directory.Exists(patcherDest)) Directory.Delete(patcherDest, true);

            string stagingRoot = Path.GetFullPath(StagingPaths.Root);

            using (var stream = new MemoryStream(zipBytes))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // a folder entry, not a file

                    string rel = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (rel.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)) rel = rel.Substring("BepInEx/".Length);

                    string target;
                    if (rel.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
                        target = Path.Combine(pluginDest, rel.Substring("plugins/".Length));
                    else if (rel.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase))
                        target = Path.Combine(patcherDest, rel.Substring("patchers/".Length));
                    else if (rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
                        target = Path.Combine(StagingPaths.Config, rel.Substring("config/".Length));
                    else if (rel.StartsWith("core/", StringComparison.OrdinalIgnoreCase))
                    {
                        Plugin.Log.LogWarning($"{mod.DependencyString}: skipping '{rel}' (BepInEx core files are not managed).");
                        continue;
                    }
                    else
                        target = Path.Combine(pluginDest, rel);

                    // Safety: a malicious zip could contain "../" paths. Refuse anything that escapes staging.
                    string full = Path.GetFullPath(target);
                    if (!full.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"zip entry '{entry.FullName}' escapes the staging folder");

                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    entry.ExtractToFile(full, true);
                }
            }

            // The scanner identifies mods by manifest.json; every Thunderstore zip has one at the root,
            // but write a minimal one if this package somehow lacked it.
            string manifestPath = Path.Combine(pluginDest, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                Directory.CreateDirectory(pluginDest);
                File.WriteAllText(manifestPath, "{\n  \"name\": \"" + mod.Name + "\",\n  \"version_number\": \"" + mod.Version + "\"\n}\n");
            }
        }
    }
}

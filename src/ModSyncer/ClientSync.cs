using System;
using System.Collections.Generic;
using System.Text;

namespace ModSyncer
{
    /// <summary>
    /// Client-side state machine. Remembers the last server rule book we saw, what we still
    /// need, and turns those into downloads and a player-facing explanation.
    /// </summary>
    internal static class ClientSync
    {
        public static Manifest LastServerManifest { get; private set; }
        public static SyncPlan LastPlan { get; private set; }

        /// <summary>True once the server's rules arrived and we do not meet them.</summary>
        public static bool KnownOutOfSync => LastPlan != null && !LastPlan.InSync;

        /// <summary>Mods that are downloaded and waiting in staging for a restart.</summary>
        private static readonly List<ModRef> Staged = new List<ModRef>();

        public static void OnConnecting()
        {
            LastServerManifest = null;
            LastPlan = null;
            Staged.Clear();
        }

        public static void OnServerManifest(Manifest server)
        {
            LastServerManifest = server;
            Plugin.Log.LogInfo($"Server (Mod Syncer {server.SyncerVersion}) enforces {server.Entries.Count} mod(s):{Environment.NewLine}{server.Describe()}");

            List<ModRef> installed = InstalledMods.Scan();
            LastPlan = SyncPlan.Compare(server, installed);
            Plugin.Log.LogInfo("Comparison with our install: " + Environment.NewLine + LastPlan.Describe());

            if (LastPlan.InSync) return;

            // Anything already sitting in staging from an earlier attempt does not need downloading again.
            List<ModRef> alreadyStaged = InstalledMods.ScanFolder(StagingPaths.Plugins);
            var toDownload = new List<ModRef>();
            Staged.Clear();
            foreach (ModRef wanted in LastPlan.ToInstall)
            {
                bool staged = alreadyStaged.Exists(s => s.FullName.Equals(wanted.FullName, StringComparison.OrdinalIgnoreCase)
                                                     && s.Version.Equals(wanted.Version, StringComparison.OrdinalIgnoreCase));
                if (staged) Staged.Add(wanted); else toDownload.Add(wanted);
            }

            if (toDownload.Count == 0)
            {
                Plugin.Log.LogInfo("Everything the server wants is already downloaded; a restart will apply it.");
                return;
            }

            if (!Plugin.AutoDownload.Value)
            {
                Plugin.Log.LogInfo("Client.AutoDownload is off; not downloading anything.");
                return;
            }

            Downloader.Start(toDownload);
        }

        /// <summary>Called by the downloader when it has finished (successfully or not).</summary>
        public static void OnDownloadFinished(List<ModRef> downloaded)
        {
            Staged.AddRange(downloaded);
            // The player is probably looking at the "connection failed" panel by now; update it.
            ClientUI.RefreshConnectErrorText();
        }

        /// <summary>The server sent the vanilla "incompatible version" error.</summary>
        public static void OnRejectedForVersion()
        {
            if (LastServerManifest == null)
                Plugin.Log.LogInfo("Connection refused with a version error, but the server did not send a mod manifest. This is not a mod mismatch.");
            else if (!KnownOutOfSync)
                Plugin.Log.LogWarning("Server refused us for version reasons even though our mod list looked in sync. Check the server log.");
            else
                Plugin.Log.LogInfo("Server refused us: mods out of sync (expected).");
        }

        /// <summary>
        /// The text shown on the connection-failed panel, or null when the failure has nothing
        /// to do with mods and the game's own message should stay.
        /// </summary>
        public static string BuildPlayerMessage()
        {
            if (LastServerManifest == null) return null;

            if (!KnownOutOfSync)
                return "Mod Syncer: your mods match this server, but it still refused the connection.\nAsk the host to check the server log.";

            var sb = new StringBuilder();
            sb.AppendLine("This server needs different mods:");
            int shown = 0;
            foreach (ModRef m in LastPlan.Missing)
            {
                if (shown++ >= 4) break;
                sb.AppendLine("  " + m.Name + " " + m.Version + " (missing)");
            }
            foreach (var pair in LastPlan.WrongVersion)
            {
                if (shown++ >= 4) break;
                sb.AppendLine("  " + pair.Key.Name + " " + pair.Key.Version + " > " + pair.Value.Version);
            }
            int total = LastPlan.Missing.Count + LastPlan.WrongVersion.Count;
            if (total > shown) sb.AppendLine("  ...and " + (total - shown) + " more");
            sb.AppendLine();

            if (Downloader.Running)
                sb.Append(Downloader.Progress);
            else if (Downloader.LastRunFailed)
                sb.Append("Download failed: " + string.Join("; ", Downloader.Failures) + "\nCheck your internet connection and try again.");
            else if (!Plugin.AutoDownload.Value && Staged.Count < total)
                sb.Append("Automatic download is turned off in the Mod Syncer config.");
            else
                sb.Append("Downloaded. Quit Valheim completely, start it again, and rejoin.");

            return sb.ToString().TrimEnd();
        }
    }
}

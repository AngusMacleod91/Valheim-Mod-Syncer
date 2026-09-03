using System.Collections.Generic;

namespace ModSyncer
{
    /// <summary>
    /// Client-side state machine. Remembers the last server rule book we saw, what we still
    /// need, and turns those into downloads and player-facing messages.
    /// </summary>
    internal static class ClientSync
    {
        public static Manifest LastServerManifest { get; private set; }
        public static SyncPlan LastPlan { get; private set; }

        /// <summary>Mods already downloaded to staging and waiting for a restart.</summary>
        private static List<ModRef> _staged = new List<ModRef>();

        public static void OnConnecting()
        {
            LastServerManifest = null;
            LastPlan = null;
        }

        public static void OnServerManifest(Manifest server)
        {
            LastServerManifest = server;
            Plugin.Log.LogInfo($"Server (Mod Syncer {server.SyncerVersion}) enforces {server.Entries.Count} mod(s):{System.Environment.NewLine}{server.Describe()}");

            List<ModRef> installed = InstalledMods.Scan();
            LastPlan = SyncPlan.Compare(server, installed);
            Plugin.Log.LogInfo("Comparison with our install: " + System.Environment.NewLine + LastPlan.Describe());

            if (LastPlan.InSync) return;

            // Anything already sitting in staging from an earlier attempt does not need downloading again.
            _staged = InstalledMods.ScanFolder(StagingPaths.Plugins);
            var toDownload = new List<ModRef>();
            var alreadyStaged = new List<ModRef>();
            foreach (ModRef wanted in LastPlan.ToInstall)
            {
                bool staged = _staged.Exists(s => s.FullName.Equals(wanted.FullName, System.StringComparison.OrdinalIgnoreCase)
                                                && s.Version.Equals(wanted.Version, System.StringComparison.OrdinalIgnoreCase));
                if (staged) alreadyStaged.Add(wanted); else toDownload.Add(wanted);
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

        /// <summary>The server sent the vanilla "incompatible version" error. Add our explanation if we caused it.</summary>
        public static void OnRejectedForVersion()
        {
            if (LastServerManifest == null)
            {
                // Server never sent a manifest: either a vanilla version mismatch or the server has no Mod Syncer.
                Plugin.Log.LogInfo("Connection refused with a version error, but the server did not send a mod manifest. This is not a mod mismatch.");
                return;
            }

            if (LastPlan == null || LastPlan.InSync)
            {
                Plugin.Log.LogWarning("Server refused us for version reasons even though our mod list looked in sync. Check the server log.");
                ClientUI.Show("Mod Syncer",
                    "The server refused the connection, but this client's mods appear to match.\n\nAsk the host to check the server log.");
                return;
            }

            string details = LastPlan.DescribeForPlayer();
            if (Downloader.Running)
            {
                ClientUI.Show("Mods out of date",
                    "This server requires different mod versions:\n\n" + details +
                    "\n\nMod Syncer is downloading them now. You will get another message when it is done.");
            }
            else if (Downloader.LastRunFailed)
            {
                ClientUI.Show("Mod download failed",
                    "This server requires different mod versions:\n\n" + details +
                    "\n\nDownloading failed:\n" + string.Join("\n", Downloader.Failures) +
                    "\n\nCheck your internet connection and try connecting again.");
            }
            else
            {
                ClientUI.Show("Restart required",
                    "This server requires different mod versions:\n\n" + details +
                    "\n\nThey have been downloaded. Quit Valheim completely, start it again, and reconnect.");
            }
        }
    }
}

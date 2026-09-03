using System;
using System.Collections.Generic;
using HarmonyLib;

namespace ModSyncer
{
    /// <summary>
    /// Hooks into Valheim's connection handshake using Harmony, a library that lets a mod run
    /// its own code before ("Prefix") or after ("Postfix") one of the game's methods.
    ///
    /// Vanilla handshake, read from the game's own code (ZNet class):
    ///   1. Both sides: OnNewConnection(peer) registers the RPC handlers they will accept.
    ///      The client then immediately calls "ServerHandshake" on the server.
    ///   2. Server: RPC_ServerHandshake -> calls "ClientHandshake" on the client (needs password?).
    ///   3. Client: RPC_ClientHandshake -> password dialog, then SendPeerInfo (version, name, ...).
    ///   4. Server: RPC_PeerInfo checks everything and either accepts or calls "Error" on the client.
    ///
    /// Our additions:
    ///   1. Both sides also register "ModSyncer_Manifest". The client sends its installed-mod list
    ///      right after "ServerHandshake", so it always reaches the server before PeerInfo does
    ///      (messages on one connection arrive in order).
    ///   2. Before the server sends "ClientHandshake" it sends its rule book. The client therefore
    ///      knows whether it will be accepted BEFORE any password prompt.
    ///   3. If the client already knows it is out of sync it fails the connection itself, with the
    ///      same status code the game uses for a version mismatch, and never shows the password box.
    ///   4. On the server, a Prefix on RPC_PeerInfo turns a bad verdict into that same "Error", which
    ///      also covers clients whose Mod Syncer is missing or which ignored step 3.
    /// </summary>
    [HarmonyPatch]
    internal static class HandshakePatches
    {
        private const string RpcName = "ModSyncer_Manifest";

        /// <summary>ZNet.ConnectionStatus.ErrorVersion. Kept as a number so we do not depend on the enum being public.</summary>
        internal const int ErrorVersion = 3;

        /// <summary>Server side: verdict for each connection that has sent us a manifest.</summary>
        private static readonly Dictionary<ZRpc, SyncPlan> Verdicts = new Dictionary<ZRpc, SyncPlan>();

        // ------------------------------------------------------------------ step 1

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        [HarmonyPostfix]
        private static void OnNewConnection_Postfix(ZNet __instance, ZNetPeer peer)
        {
            if (peer == null || peer.m_rpc == null) return;

            peer.m_rpc.Register<ZPackage>(RpcName, OnManifestReceived);

            if (__instance.IsServer())
            {
                Verdicts.Remove(peer.m_rpc);
                return;
            }

            // Client: tell the server what we have.
            var mine = new Manifest();
            foreach (ModRef m in InstalledMods.Scan())
                mine.Entries.Add(new ManifestEntry(m, ModSide.Both));

            var pkg = new ZPackage();
            mine.WriteTo(pkg);
            peer.m_rpc.Invoke(RpcName, pkg);
            Plugin.Log.LogInfo($"Sent our mod list ({mine.Entries.Count} mods) to the server.");

            ClientSync.OnConnecting();
        }

        // ------------------------------------------------------------------ step 2 (server)

        [HarmonyPatch(typeof(ZNet), "RPC_ServerHandshake")]
        [HarmonyPrefix]
        private static void RPC_ServerHandshake_Prefix(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer() || rpc == null) return;

            // Send the rule book before the game sends ClientHandshake, so the client can decide
            // before it asks the player for a password.
            var reply = new ZPackage();
            ServerManifest.Get().WriteTo(reply);
            rpc.Invoke(RpcName, reply);
        }

        // ------------------------------------------------------------------ the new RPC

        private static void OnManifestReceived(ZRpc rpc, ZPackage pkg)
        {
            Manifest received = Manifest.ReadFrom(pkg);
            ZNet znet = ZNet.instance;
            if (znet == null) return;

            if (znet.IsServer())
            {
                string who = rpc.GetSocket()?.GetHostName() ?? "unknown";
                if (received == null)
                {
                    Plugin.Log.LogWarning($"Client {who} sent an unreadable manifest; it will be rejected.");
                    Verdicts[rpc] = null;
                    return;
                }

                var installed = new List<ModRef>();
                foreach (ManifestEntry e in received.Entries) installed.Add(e.Mod);

                SyncPlan plan = SyncPlan.Compare(ServerManifest.Get(), installed);
                Verdicts[rpc] = plan;
                Plugin.Log.LogInfo($"Client {who} (Mod Syncer {received.SyncerVersion}) reported {installed.Count} mods. Verdict: {(plan.InSync ? "in sync" : "out of sync")}{(plan.InSync ? "" : Environment.NewLine + plan.Describe())}");
            }
            else
            {
                if (received == null)
                {
                    Plugin.Log.LogWarning("Server sent an unreadable manifest.");
                    return;
                }
                ClientSync.OnServerManifest(received);
            }
        }

        // ------------------------------------------------------------------ step 3 (client)

        [HarmonyPatch(typeof(ZNet), "RPC_ClientHandshake")]
        [HarmonyPrefix]
        private static bool RPC_ClientHandshake_Prefix(ZNet __instance)
        {
            if (__instance.IsServer()) return true;
            if (!ClientSync.KnownOutOfSync) return true; // in sync, or server has no Mod Syncer: vanilla flow

            Plugin.Log.LogInfo("We already know this server will refuse us; skipping the password prompt and failing the connection now.");
            SetConnectionStatus(ErrorVersion);
            return false; // no password dialog, no PeerInfo
        }

        /// <summary>Mirrors what the game's own RPC_Error does: set the status and let the game's update loop tear down the connection.</summary>
        private static void SetConnectionStatus(int status)
        {
            var field = AccessTools.Field(typeof(ZNet), "m_connectionStatus");
            if (field == null) { Plugin.Log.LogError("ZNet.m_connectionStatus not found; game update?"); return; }
            field.SetValue(null, Enum.ToObject(field.FieldType, status));
        }

        // ------------------------------------------------------------------ step 4 (server)

        [HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
        [HarmonyPrefix]
        private static bool RPC_PeerInfo_Prefix(ZNet __instance, ZRpc rpc)
        {
            if (!__instance.IsServer()) return true; // clients run the original untouched

            Manifest rules = ServerManifest.Get();
            bool haveVerdict = Verdicts.TryGetValue(rpc, out SyncPlan plan);
            string who = rpc.GetSocket()?.GetHostName() ?? "unknown";

            if (!haveVerdict)
            {
                // No manifest arrived: this client does not have Mod Syncer at all.
                bool nothingToEnforce = rules.Entries.Count == 0;
                if (nothingToEnforce || !Plugin.RequireSyncerOnClients.Value)
                {
                    Plugin.Log.LogInfo($"Client {who} has no Mod Syncer; allowing (nothing to enforce or RequireSyncerOnClients is off).");
                    return true;
                }
                Plugin.Log.LogWarning($"Rejecting client {who}: Mod Syncer is not installed on their side.");
                return Reject(rpc);
            }

            if (plan == null || !plan.InSync)
            {
                Plugin.Log.LogWarning($"Rejecting client {who}: mods out of sync.");
                return Reject(rpc);
            }

            return true;
        }

        private static bool Reject(ZRpc rpc)
        {
            // Same message the vanilla game sends for a version mismatch.
            rpc.Invoke("Error", ErrorVersion);
            return false; // skip the original RPC_PeerInfo, so the player is never admitted
        }

        // ------------------------------------------------------------------ housekeeping

        [HarmonyPatch(typeof(ZNet), "Disconnect")]
        [HarmonyPostfix]
        private static void Disconnect_Postfix(ZNetPeer peer)
        {
            if (peer?.m_rpc != null) Verdicts.Remove(peer.m_rpc);
        }

        [HarmonyPatch(typeof(ZNet), "RPC_Error")]
        [HarmonyPostfix]
        private static void RPC_Error_Postfix(ZNet __instance, int error)
        {
            if (!__instance.IsServer() && error == ErrorVersion) ClientSync.OnRejectedForVersion();
        }

        // ------------------------------------------------------------------ the "connection failed" screen (client)

        /// <summary>
        /// The game shows a small panel with a fixed message such as "Incompatible version". When the
        /// failure was caused by mods, replace that text with what is actually going on.
        /// </summary>
        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        [HarmonyPostfix]
        private static void ShowConnectError_Postfix(FejdStartup __instance)
        {
            ClientUI.RememberConnectErrorPanel(__instance);
            ClientUI.RefreshConnectErrorText();
        }
    }
}

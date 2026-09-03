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
    ///   3. Client: RPC_ClientHandshake -> SendPeerInfo (version, name, password hash ...).
    ///   4. Server: RPC_PeerInfo checks everything and either accepts or calls "Error" on the client.
    ///
    /// Our addition: in step 1 both sides also register "ModSyncer_Manifest". The client sends
    /// its installed-mod list right after "ServerHandshake", so it always reaches the server
    /// before PeerInfo does (messages on one connection arrive in order). The server replies
    /// with its rule book so the client knows what to download, and remembers a verdict per
    /// connection. In step 4 a Prefix on RPC_PeerInfo turns a bad verdict into the same "Error"
    /// the game uses for a version mismatch, which the client already knows how to display.
    /// </summary>
    [HarmonyPatch]
    internal static class HandshakePatches
    {
        private const string RpcName = "ModSyncer_Manifest";

        /// <summary>ZNet.ConnectionStatus.ErrorVersion. Kept as a number so we do not depend on the enum being public.</summary>
        private const int ErrorVersion = 3;

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

                Manifest rules = ServerManifest.Get();
                var installed = new List<ModRef>();
                foreach (ManifestEntry e in received.Entries) installed.Add(e.Mod);

                SyncPlan plan = SyncPlan.Compare(rules, installed);
                Verdicts[rpc] = plan;
                Plugin.Log.LogInfo($"Client {who} (Mod Syncer {received.SyncerVersion}) reported {installed.Count} mods. Verdict: {(plan.InSync ? "in sync" : "out of sync")}{(plan.InSync ? "" : System.Environment.NewLine + plan.Describe())}");

                // Always answer with the rule book so the client can fix itself.
                var reply = new ZPackage();
                rules.WriteTo(reply);
                rpc.Invoke(RpcName, reply);
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

        // ------------------------------------------------------------------ step 4

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
            // Same message the vanilla game sends for a version mismatch; the client shows its
            // normal "incompatible version" screen and our client code adds the details.
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
        private static void RPC_Error_Postfix(ZNet __instance, ZRpc rpc, int error)
        {
            if (__instance.IsServer()) return;
            if (error == ErrorVersion) ClientSync.OnRejectedForVersion();
        }
    }
}

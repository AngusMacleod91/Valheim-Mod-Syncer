using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ModSyncer
{
    /// <summary>
    /// Entry point. BepInEx finds this class through the [BepInPlugin] attribute and creates it
    /// when the game starts; Awake() runs once, before the main menu appears.
    ///
    /// The same DLL runs on the dedicated server and on every player's game. It decides which
    /// role it is playing at connection time by asking the game (ZNet.IsServer()).
    /// </summary>
    [BepInPlugin(Guid, DisplayName, PluginVersion.Value)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.boogytime.valheim.modsyncer";
        public const string DisplayName = "Valheim Mod Syncer";

        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        // ----- configuration (BepInEx writes these to BepInEx/config/com.boogytime.valheim.modsyncer.cfg) -----
        internal static ConfigEntry<bool> RequireSyncerOnClients;
        internal static ConfigEntry<bool> EnforceInstalledMods;
        internal static ConfigEntry<string> IgnoreMods;
        internal static ConfigEntry<bool> RescanEveryConnection;
        internal static ConfigEntry<bool> EnforceSyncerVersion;
        internal static ConfigEntry<bool> AutoDownload;
        internal static ConfigEntry<bool> ShowPopups;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            RequireSyncerOnClients = Config.Bind("Server", "RequireSyncerOnClients", true,
                "Reject players who do not have Mod Syncer installed at all. Turn off only for a transition period.");
            EnforceInstalledMods = Config.Bind("Server", "EnforceInstalledMods", true,
                "Require every mod installed on this server (in Author-Name folders with a manifest.json) on all clients at the same version.");
            IgnoreMods = Config.Bind("Server", "IgnoreMods", "",
                "Comma-separated Namespace-Name values that are installed on the server but should NOT be enforced on clients.");
            RescanEveryConnection = Config.Bind("Server", "RescanEveryConnection", false,
                "Rebuild the enforced list on every connection instead of once at startup. Handy while setting up; slower.");
            EnforceSyncerVersion = Config.Bind("Server", "EnforceSyncerVersion", false,
                "Also require clients to have exactly this server's Mod Syncer version. Leave off until Mod Syncer is published on Thunderstore, otherwise clients cannot download it.");
            AutoDownload = Config.Bind("Client", "AutoDownload", true,
                "Automatically download missing or outdated mods from Thunderstore when a server requires them.");
            ShowPopups = Config.Bind("Client", "ShowPopups", true,
                "Show in-game popups explaining what happened. When off, everything still goes to the BepInEx log.");

            try
            {
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception ex)
            {
                Log.LogError("Failed to apply Harmony patches. Mod Syncer will do nothing. " + ex);
                return;
            }

            Log.LogInfo($"{DisplayName} {PluginVersion.Value} loaded. Installed mods detected:{Environment.NewLine}{InstalledMods.Describe(InstalledMods.Scan())}");

            // A dedicated server runs without a graphics device. Build (and log) its rule book right
            // away so the host sees it at startup, and so the extra-mods file exists before anyone connects.
            if (UnityEngine.SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Log.LogInfo("Running headless (dedicated server). Building the enforced mod list now.");
                ServerManifest.Get();
            }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace ModSyncer
{
    /// <summary>
    /// Player-facing messages. The main channel is the game's own "connection failed" panel in
    /// the main menu: after a refused join the game reloads its menu scene, which destroys any
    /// popup we might have opened earlier, but the panel is created fresh and stays until the
    /// player clicks OK. So we write our explanation into that panel instead.
    /// </summary>
    internal static class ClientUI
    {
        private static FejdStartup _menu;

        public static void RememberConnectErrorPanel(FejdStartup menu) => _menu = menu;

        /// <summary>Replace the panel's text with the mod explanation, if the failure is mod related.</summary>
        public static void RefreshConnectErrorText()
        {
            string message = ClientSync.BuildPlayerMessage();
            if (message == null) return; // not our failure; leave the game's text alone

            Plugin.Log.LogInfo("[connection failed panel] " + message.Replace("\n", " | "));
            if (!Plugin.ShowPopups.Value) return;

            try
            {
                if (_menu == null) return;
                var panel = AccessTools.Field(typeof(FejdStartup), "m_connectionFailedPanel")?.GetValue(_menu) as GameObject;
                object label = AccessTools.Field(typeof(FejdStartup), "m_connectionFailedError")?.GetValue(_menu);
                if (panel == null || label == null || !panel.activeInHierarchy) return;

                // The label is a TextMeshPro component. Setting it through reflection avoids taking a
                // compile-time dependency on the TextMeshPro assembly.
                Traverse.Create(label).Property("text").SetValue(message);
                Traverse.Create(label).Property("enableAutoSizing").SetValue(true);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not update the connection-failed panel: " + ex.Message);
            }
        }
    }
}

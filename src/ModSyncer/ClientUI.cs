namespace ModSyncer
{
    /// <summary>
    /// Shows messages to the player using Valheim's own popup system (UnifiedPopup), the same
    /// dialog style the game uses for warnings in the main menu. Falls back to the log when the
    /// UI is not available (for example on a dedicated server, which has no screen).
    /// </summary>
    internal static class ClientUI
    {
        public static void Show(string header, string text)
        {
            Plugin.Log.LogInfo($"[{header}] {text.Replace("\n", " ")}");

            if (!Plugin.ShowPopups.Value) return;

            try
            {
                if (!UnifiedPopup.IsAvailable())
                {
                    Plugin.Log.LogInfo("Popup UI not available yet; message logged only.");
                    return;
                }
                // localizeText:false because our strings are plain English, not translation keys.
                UnifiedPopup.Push(new WarningPopup(header, text, () => UnifiedPopup.Pop(), false));
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Could not show popup: " + ex.Message);
            }
        }
    }
}

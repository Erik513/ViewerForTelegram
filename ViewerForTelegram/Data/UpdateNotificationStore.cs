namespace ViewerForTelegram.Data;

/// <summary>
/// Loads and saves which release version the startup "update available"
/// popup has already been shown for (<see cref="AppPaths.UpdateNotificationFile"/>).
/// Kept separate from <see cref="JsonUiStateStore"/>/<see cref="UiState"/>
/// deliberately - SaveUiState() always reconstructs a whole new UiState from
/// scratch, so a field living there would get silently reset back to its
/// default on the next unrelated UI-state save (e.g. playing a track).
/// Losing this file is harmless - worst case, one update notice repeats.
/// </summary>
public static class UpdateNotificationStore
{
    public static string? LoadLastNotifiedVersion()
    {
        try
        {
            return File.Exists(AppPaths.UpdateNotificationFile)
                ? File.ReadAllText(AppPaths.UpdateNotificationFile).Trim()
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void SaveLastNotifiedVersion(string version)
    {
        try
        {
            File.WriteAllText(AppPaths.UpdateNotificationFile, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth surfacing - worst case, the popup repeats once.
        }
    }
}

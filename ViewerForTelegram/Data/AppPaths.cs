namespace ViewerForTelegram.Data;

/// <summary>
/// Central storage locations of the app, all under <c>%AppData%\ViewerForTelegram\</c>.
///
/// Deliberately NOT next to the .exe: a future self-update overwrites the
/// program folder, and config / session / cache would be swept away with it.
/// Under %AppData% they survive an update and are per-Windows-user.
///
/// The static constructor creates <see cref="Root"/> and <see cref="CacheDir"/>
/// on first access.
/// </summary>
public static class AppPaths
{
    /// <summary><c>%AppData%\ViewerForTelegram</c></summary>
    public static string Root { get; }

    /// <summary>
    /// User input (api_id, api_hash, phone number). Contains secrets - lives
    /// here and NOT in the repo (see .gitignore).
    /// </summary>
    public static string ConfigFile { get; }

    /// <summary>
    /// WTelegramClient session. Contains the completed login - treat it like a
    /// password, never share it.
    /// </summary>
    public static string SessionFile { get; }

    /// <summary>Remembered UI preferences (open chat, time range, volume).</summary>
    public static string UiStateFile { get; }

    /// <summary>Folder for downloaded audio files.</summary>
    public static string CacheDir { get; }

    static AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ViewerForTelegram");

        ConfigFile = Path.Combine(Root, "appsettings.local.json");
        SessionFile = Path.Combine(Root, "telegram.session");
        UiStateFile = Path.Combine(Root, "ui-state.json");
        CacheDir = Path.Combine(Root, "cache");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
    }
}

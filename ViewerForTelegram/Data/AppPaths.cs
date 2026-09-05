using System.Runtime.InteropServices;

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

    /// <summary>
    /// The latest release version the startup "update available" popup has
    /// already been shown for - lets the check notify about a given release
    /// only once instead of on every launch. See <see cref="UpdateNotificationStore"/>.
    /// </summary>
    public static string UpdateNotificationFile { get; }

    /// <summary>
    /// The last successfully loaded audio list (see <c>PersistedFeed</c>) - lets
    /// a restart show it instantly and fetch only what changed, instead of
    /// re-pulling e.g. "Newest 5000" from scratch.
    /// </summary>
    public static string FeedCacheFile { get; }

    /// <summary>Folder for the throwaway playback cache.</summary>
    public static string CacheDir { get; }

    /// <summary>
    /// Track lengths decoded from files Telegram gave no duration for. Kept
    /// beside the cache folder, not inside it, so wiping the cached songs (in
    /// the app or by hand) does not take this metadata with it.
    /// </summary>
    public static string DurationsFile { get; }

    /// <summary>
    /// The Windows "Downloads" folder of the current user - the default target
    /// for "Save a copy" until the user picks another folder in the settings.
    /// Resolved from the shell known folder (so a relocated Downloads folder is
    /// honoured); falls back to <c>%USERPROFILE%\Downloads</c>.
    /// </summary>
    public static string DownloadsFolder { get; }

    static AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ViewerForTelegram");

        ConfigFile = Path.Combine(Root, "appsettings.local.json");
        SessionFile = Path.Combine(Root, "telegram.session");
        UiStateFile = Path.Combine(Root, "ui-state.json");
        UpdateNotificationFile = Path.Combine(Root, "last-notified-update.txt");
        FeedCacheFile = Path.Combine(Root, "feed-cache.json");
        CacheDir = Path.Combine(Root, "cache");
        DurationsFile = Path.Combine(Root, "durations.json");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);

        DownloadsFolder = ResolveDownloadsFolder();
    }

    // FOLDERID_Downloads
    private static readonly Guid DownloadsKnownFolder =
        new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    private static string ResolveDownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsKnownFolder, 0, IntPtr.Zero, out IntPtr p) == 0)
            {
                try
                {
                    string? path = Marshal.PtrToStringUni(p);
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(p);
                }
            }
        }
        catch
        {
            // fall through to the profile-relative default
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}

namespace ViewerForTelegram.Data;

/// <summary>
/// Zentrale Ablageorte der App, alle unter <c>%AppData%\ViewerForTelegram\</c>.
///
/// Bewusst NICHT neben der .exe: ein späteres Self-Update überschreibt den
/// Programmordner, und Config / Session / Cache würden mitgerissen. Unter
/// %AppData% überleben sie ein Update und sind pro Windows-Benutzer getrennt.
///
/// Der statische Konstruktor legt <see cref="Root"/> und <see cref="CacheDir"/>
/// beim ersten Zugriff an.
/// </summary>
public static class AppPaths
{
    /// <summary><c>%AppData%\ViewerForTelegram</c></summary>
    public static string Root { get; }

    /// <summary>
    /// Nutzereingaben (api_id, api_hash, Telefonnummer). Enthält Geheimnisse -
    /// liegt hier und NICHT im Repo (siehe .gitignore).
    /// </summary>
    public static string ConfigFile { get; }

    /// <summary>
    /// WTelegramClient-Sitzung. Enthält den fertigen Login - wie ein Passwort
    /// behandeln, niemals weitergeben.
    /// </summary>
    public static string SessionFile { get; }

    /// <summary>Ordner für heruntergeladene Audiodateien.</summary>
    public static string CacheDir { get; }

    /// <summary>User-Data-Ordner der eingebetteten WebView2.</summary>
    public static string WebView2Dir { get; }

    static AppPaths()
    {
        Root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ViewerForTelegram");

        ConfigFile = Path.Combine(Root, "appsettings.local.json");
        SessionFile = Path.Combine(Root, "telegram.session");
        CacheDir = Path.Combine(Root, "cache");
        WebView2Dir = Path.Combine(Root, "WebView2");

        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(WebView2Dir);
    }
}

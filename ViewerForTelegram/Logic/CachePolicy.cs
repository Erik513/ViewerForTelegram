namespace ViewerForTelegram.Logic;

/// <summary>
/// Zentrale Cache-Regel. Liegt in Logic, damit alle Beteiligten (Composition
/// Root, <see cref="Services.MediaDownloader"/>, die Einstellungsanzeige)
/// denselben Wert verwenden.
/// </summary>
public static class CachePolicy
{
    /// <summary>
    /// Obergrenze des Song-Caches, wenn er nicht bei jedem Start geleert wird
    /// (glatte 3000 MB, damit die Anzeige rund aussieht).
    /// </summary>
    public const long LimitBytes = 3000L * 1024 * 1024;
}

namespace ViewerForTelegram.Logic;

/// <summary>
/// Central cache rule. Lives in Logic so that everyone involved (the
/// composition root, <see cref="Services.MediaDownloader"/>, the settings
/// display) uses the same value.
/// </summary>
public static class CachePolicy
{
    /// <summary>
    /// Upper limit of the song cache when it is not wiped on every start
    /// (a round 3000 MB, so the display reads nicely).
    /// </summary>
    public const long LimitBytes = 3000L * 1024 * 1024;
}

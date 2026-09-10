namespace ViewerForTelegram.UI;

/// <summary>
/// Small display formatters for a track's length and size, shared by the list
/// and the player so both read the same way.
/// </summary>
internal static class TrackFormat
{
    /// <summary><c>m:ss</c> (e.g. <c>3:07</c>). The caller handles a missing length.</summary>
    public static string Duration(TimeSpan d) => $"{(int)d.TotalMinutes}:{d.Seconds:00}";

    /// <summary>The file size in whole tenths of a megabyte (e.g. <c>4.2 MB</c>).</summary>
    public static string SizeMb(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";
}

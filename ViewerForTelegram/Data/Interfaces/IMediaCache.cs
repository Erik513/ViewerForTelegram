using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// Manages the local folder of already-downloaded audio files. Knows nothing
/// about Telegram - only answers "which file lives where". The actual download
/// is done by <see cref="ITelegramSource.DownloadAsync"/>; the Logic layer
/// brings the two together.
/// </summary>
public interface IMediaCache
{
    /// <summary>Is the file for this message already present locally and complete?</summary>
    bool Contains(AudioMessage message);

    /// <summary>
    /// Full path where the file lives or should live - regardless of whether it
    /// is already there. Used as the download target.
    /// </summary>
    string GetPath(AudioMessage message);

    /// <summary>Cache figures for display (file count, bytes used).</summary>
    (int Count, long TotalBytes) GetStats();

    /// <summary>Deletes all complete cache files.</summary>
    void Clear();

    /// <summary>
    /// Deletes the oldest files until the cache no longer exceeds
    /// <paramref name="maxBytes"/>. No effect if it is already below.
    /// </summary>
    void PruneToLimit(long maxBytes);
}

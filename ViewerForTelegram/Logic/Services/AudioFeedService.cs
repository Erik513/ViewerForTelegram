using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic.Services;

/// <summary>
/// One entry of the song list: the Telegram metadata plus whether the file is
/// already present and complete in the local cache.
/// </summary>
public sealed record FeedItem(AudioMessage Audio, bool Cached);

/// <summary>
/// Assembles the audio list of a chat - either a rolling time window or the
/// newest N audios. Knows neither the UI nor Telegram internals - only the
/// source and the cache.
/// </summary>
public sealed class AudioFeedService
{
    /// <summary>How many audios the "newest N" mode returns (matches the combo label).</summary>
    public const int RecentAudioCount = 100;

    private readonly ITelegramSource _telegram;
    private readonly IMediaCache _cache;

    public AudioFeedService(ITelegramSource telegram, IMediaCache cache)
    {
        _telegram = telegram;
        _cache = cache;
    }

    /// <summary>
    /// Audios from <paramref name="chatId"/>, newest first. <paramref name="days"/>
    /// &gt; 0 = the last "now minus n days" (not midnight-rounded);
    /// <paramref name="days"/> &lt;= 0 = the newest <see cref="RecentAudioCount"/>
    /// regardless of age (for chats where nothing was posted for a long time).
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> LoadAsync(
        long chatId, int days, CancellationToken ct)
    {
        bool byCount = days <= 0;
        DateTime sinceUtc = byCount ? DateTime.MinValue : DateTime.UtcNow.AddDays(-days);
        int maxAudios = byCount ? RecentAudioCount : int.MaxValue;

        IReadOnlyList<AudioMessage> audios =
            await _telegram.GetAudioMessagesSinceAsync(chatId, sinceUtc, ct, maxAudios);

        var items = new List<FeedItem>(audios.Count);
        foreach (AudioMessage a in audios)
        {
            // Telegram often gives no duration for files posted "as a file" - fill
            // it in from a length the cache decoded on an earlier playback.
            AudioMessage enriched = a.Duration is null
                ? a with { Duration = _cache.GetKnownDuration(a) }
                : a;
            items.Add(new FeedItem(enriched, _cache.Contains(enriched)));
        }
        return items;
    }
}

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
    private readonly ITelegramSource _telegram;
    private readonly IMediaCache _cache;

    public AudioFeedService(ITelegramSource telegram, IMediaCache cache)
    {
        _telegram = telegram;
        _cache = cache;
    }

    /// <summary>
    /// Audios from <paramref name="chatId"/>, newest first.
    /// <paramref name="range"/> &gt; 0 = the last "now minus n days" (not
    /// midnight-rounded); <paramref name="range"/> &lt; 0 = the newest
    /// <c>-range</c> audios regardless of age (for chats idle for a long time).
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> LoadAsync(
        long chatId, int range, CancellationToken ct)
    {
        bool byCount = range <= 0;
        DateTime sinceUtc = byCount ? DateTime.MinValue : DateTime.UtcNow.AddDays(-range);
        int maxAudios = byCount ? Math.Max(1, -range) : int.MaxValue;

        IReadOnlyList<AudioMessage> audios =
            await _telegram.GetAudioMessagesSinceAsync(chatId, sinceUtc, ct, maxAudios);

        var items = new List<FeedItem>(audios.Count);
        for (int i = 0; i < audios.Count; i++)
        {
            if ((i & 0x1FF) == 0)   // every 512 - the cache lookups hit the disk
            {
                ct.ThrowIfCancellationRequested();
            }

            AudioMessage a = audios[i];
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

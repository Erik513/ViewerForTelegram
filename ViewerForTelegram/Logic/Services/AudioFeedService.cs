using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic.Services;

/// <summary>
/// One entry of the song list: the Telegram metadata plus whether the file is
/// already present and complete in the local cache.
/// </summary>
public sealed record FeedItem(AudioMessage Audio, bool Cached);

/// <summary>
/// Assembles the audio list of a chat for a rolling time window. Knows neither
/// the UI nor the WebView - only the Telegram source and the cache.
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
    /// All audios from <paramref name="chatId"/> of the last
    /// <paramref name="days"/> days - exactly "now minus n days", not rounded to
    /// midnight. Order as delivered by the source (newest first).
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> LoadAsync(
        long chatId, int days, CancellationToken ct)
    {
        DateTime sinceUtc = DateTime.UtcNow.AddDays(-Math.Max(0, days));

        IReadOnlyList<AudioMessage> audios =
            await _telegram.GetAudioMessagesSinceAsync(chatId, sinceUtc, ct);

        var items = new List<FeedItem>(audios.Count);
        foreach (AudioMessage a in audios)
        {
            items.Add(new FeedItem(a, _cache.Contains(a)));
        }
        return items;
    }
}

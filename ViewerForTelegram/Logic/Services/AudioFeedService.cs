using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic.Services;

/// <summary>
/// Ein Eintrag der Song-Liste: die Telegram-Metadaten plus die Info, ob die
/// Datei schon vollständig im lokalen Cache liegt.
/// </summary>
public sealed record FeedItem(AudioMessage Audio, bool Cached);

/// <summary>
/// Stellt die Audioliste eines Chats für ein rollierendes Zeitfenster zusammen.
/// Kennt weder UI noch WebView - nur die Telegram-Quelle und den Cache.
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
    /// Alle Audios aus <paramref name="chatId"/> der letzten
    /// <paramref name="days"/> Tage - exakt ab "jetzt minus n Tage", nicht auf
    /// Mitternacht gerundet. Reihenfolge wie von der Quelle geliefert
    /// (neueste zuerst).
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

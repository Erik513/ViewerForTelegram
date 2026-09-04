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
    ///
    /// In count mode, pass the previous result as <paramref name="previous"/>
    /// (same chat) to reuse it: only the newly-posted messages and, if the count
    /// grew, the extra older ones are fetched.
    /// </summary>
    public async Task<IReadOnlyList<FeedItem>> LoadAsync(
        long chatId, int range, CancellationToken ct, IProgress<int>? progress = null,
        IReadOnlyList<AudioMessage>? previous = null)
    {
        bool byCount = range <= 0;
        DateTime sinceUtc = byCount ? DateTime.MinValue : DateTime.UtcNow.AddDays(-range);
        int maxAudios = byCount ? Math.Max(1, -range) : int.MaxValue;

        IReadOnlyList<AudioMessage> audios =
            byCount && previous is { Count: > 0 }
                ? await LoadIncrementalAsync(chatId, maxAudios, previous, ct, progress)
                : await _telegram.GetAudioMessagesSinceAsync(chatId, sinceUtc, ct, maxAudios, progress);

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

    private async Task<IReadOnlyList<AudioMessage>> LoadIncrementalAsync(
        long chatId, int maxAudios, IReadOnlyList<AudioMessage> previous,
        CancellationToken ct, IProgress<int>? progress)
    {
        // The count we already have carries over - the progress display must
        // start there, not at zero (the caller asked for e.g. 1000 -> 2000).
        progress?.Report(previous.Count);

        int newestKnownId = previous.Max(a => a.MessageId);
        int oldestKnownId = previous.Min(a => a.MessageId);

        // 1. anything posted since the previous load
        IReadOnlyList<AudioMessage> newer =
            await _telegram.GetAudioMessagesAfterAsync(chatId, newestKnownId, ct);

        var byId = new Dictionary<int, AudioMessage>(previous.Count + newer.Count);
        foreach (AudioMessage a in newer) byId[a.MessageId] = a;
        foreach (AudioMessage a in previous) byId.TryAdd(a.MessageId, a);
        progress?.Report(byId.Count);

        // 2. if the requested count grew, fetch the extra older ones - reporting
        //    progress on top of what we already had, not from zero.
        if (byId.Count < maxAudios)
        {
            IProgress<int>? shifted = progress is null ? null : new ShiftProgress(progress, byId.Count);
            IReadOnlyList<AudioMessage> older = await _telegram.GetAudioMessagesSinceAsync(
                chatId, DateTime.MinValue, ct, maxAudios - byId.Count, shifted,
                beforeMessageId: oldestKnownId);
            foreach (AudioMessage a in older) byId.TryAdd(a.MessageId, a);
        }

        return byId.Values
            .OrderByDescending(a => a.DateUtc)
            .ThenByDescending(a => a.MessageId)
            .Take(maxAudios)
            .ToList();
    }

    /// <summary>Forwards progress reports with a fixed offset added on.</summary>
    private sealed class ShiftProgress : IProgress<int>
    {
        private readonly IProgress<int> _inner;
        private readonly int _offset;
        public ShiftProgress(IProgress<int> inner, int offset) { _inner = inner; _offset = offset; }
        public void Report(int value) => _inner.Report(_offset + value);
    }
}

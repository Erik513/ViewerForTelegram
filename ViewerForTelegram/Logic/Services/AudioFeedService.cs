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
    /// <param name="onBatch">
    /// Optional: reports newly-fetched <see cref="FeedItem"/>s as soon as each
    /// page arrives - for a fresh load (no reusable <paramref name="previous"/>)
    /// that's every page; when growing a reused <paramref name="previous"/>
    /// list (e.g. Newest 1000 -&gt; 5000) it's the extra older pages only (the
    /// caller already shows <paramref name="previous"/>). Either way the caller
    /// can show rows for a large "newest N" pull as it grows instead of waiting
    /// for the whole thing.
    /// </param>
    public async Task<IReadOnlyList<FeedItem>> LoadAsync(
        long chatId, int range, CancellationToken ct, IProgress<int>? progress = null,
        IReadOnlyList<AudioMessage>? previous = null,
        IProgress<IReadOnlyList<FeedItem>>? onBatch = null)
    {
        bool byCount = range <= 0;
        DateTime sinceUtc = byCount ? DateTime.MinValue : DateTime.UtcNow.AddDays(-range);
        int maxAudios = byCount ? Math.Max(1, -range) : int.MaxValue;

        // Plain adapter, not "new Progress<T>(...)" - the latter captures
        // SynchronizationContext.Current *here* and would post through it,
        // double-marshalling on top of onBatch's own (it's already a real
        // Progress<T> from the caller) - and silently do nothing at all
        // where there is no context (e.g. a unit test).
        IProgress<IReadOnlyList<AudioMessage>>? forwardBatch = onBatch is null
            ? null
            : new ActionProgress<IReadOnlyList<AudioMessage>>(batch => onBatch.Report(Enrich(batch)));

        IReadOnlyList<AudioMessage> audios;
        if (byCount && previous is { Count: > 0 })
        {
            audios = await LoadIncrementalAsync(chatId, maxAudios, previous, ct, progress, forwardBatch);
        }
        else
        {
            audios = await _telegram.GetAudioMessagesSinceAsync(
                chatId, sinceUtc, ct, maxAudios, progress, onBatch: forwardBatch);
        }

        var items = new List<FeedItem>(audios.Count);
        for (int i = 0; i < audios.Count; i++)
        {
            if ((i & 0x1FF) == 0)   // every 512 - the cache lookups hit the disk
            {
                ct.ThrowIfCancellationRequested();
            }

            items.Add(Enrich(audios[i]));
        }
        return items;
    }

    /// <summary>
    /// Fills in a missing <see cref="AudioMessage.Duration"/> from a length the
    /// cache decoded on an earlier playback, and joins the cached-on-disk flag.
    /// </summary>
    private FeedItem Enrich(AudioMessage a)
    {
        AudioMessage enriched = a.Duration is null
            ? a with { Duration = _cache.GetKnownDuration(a) }
            : a;
        return new FeedItem(enriched, _cache.Contains(enriched));
    }

    private List<FeedItem> Enrich(IReadOnlyList<AudioMessage> batch)
    {
        var items = new List<FeedItem>(batch.Count);
        foreach (AudioMessage a in batch)
        {
            items.Add(Enrich(a));
        }
        return items;
    }

    private async Task<IReadOnlyList<AudioMessage>> LoadIncrementalAsync(
        long chatId, int maxAudios, IReadOnlyList<AudioMessage> previous,
        CancellationToken ct, IProgress<int>? progress,
        IProgress<IReadOnlyList<AudioMessage>>? onBatch)
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
                beforeMessageId: oldestKnownId, onBatch: onBatch);
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

    /// <summary>
    /// Plain <see cref="IProgress{T}"/> that just runs the given action - unlike
    /// "new Progress&lt;T&gt;(action)", it never marshals through a captured
    /// SynchronizationContext, so it's safe to wrap another IProgress&lt;T&gt;
    /// that already does its own marshalling.
    /// </summary>
    private sealed class ActionProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public ActionProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}

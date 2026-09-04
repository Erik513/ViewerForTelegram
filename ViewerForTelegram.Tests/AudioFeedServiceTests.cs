using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic.Services;

namespace ViewerForTelegram.Tests;

public class AudioFeedServiceTests
{
    /// <summary>Synchronous <see cref="IProgress{T}"/> - no SynchronizationContext races in tests.</summary>
    private sealed class CollectingProgress : IProgress<int>
    {
        public List<int> Values { get; } = new();
        public void Report(int value) => Values.Add(value);
    }

    private sealed class CollectingBatchProgress : IProgress<IReadOnlyList<FeedItem>>
    {
        public List<IReadOnlyList<FeedItem>> Batches { get; } = new();
        public void Report(IReadOnlyList<FeedItem> value) => Batches.Add(value);
    }

    private static AudioMessage Audio(long fileId, DateTime dateUtc, long size = 10) =>
        new(ChatId: 1, MessageId: (int)fileId, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: size, FileName: $"{fileId}.mp3", DateUtc: dateUtc);

    [Fact]
    public async Task LoadAsync_TakesOnlyAudiosInsideTheWindow()
    {
        var tg = new FakeTelegramSource();
        tg.Audios.Add(Audio(1, DateTime.UtcNow.AddDays(-2)));   // inside the window
        tg.Audios.Add(Audio(2, DateTime.UtcNow.AddDays(-10)));  // too old

        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(chatId: 1, range: 7, CancellationToken.None);

        Assert.Single(items);
        Assert.Equal(1, items[0].Audio.FileId);
    }

    [Fact]
    public async Task LoadAsync_SetsCachedFlagPerFile()
    {
        var tg = new FakeTelegramSource();
        AudioMessage cached = Audio(1, DateTime.UtcNow.AddDays(-1), size: 100);
        AudioMessage missing = Audio(2, DateTime.UtcNow.AddDays(-1), size: 100);
        tg.Audios.Add(cached);
        tg.Audios.Add(missing);

        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        File.WriteAllBytes(cache.GetPath(cached), new byte[100]);

        var svc = new AudioFeedService(tg, cache);
        IReadOnlyList<FeedItem> items = await svc.LoadAsync(1, 7, CancellationToken.None);

        Assert.True(items.Single(i => i.Audio.FileId == 1).Cached);
        Assert.False(items.Single(i => i.Audio.FileId == 2).Cached);
    }

    [Fact]
    public async Task LoadAsync_FillsMissingDurationFromTheCache()
    {
        var tg = new FakeTelegramSource();
        AudioMessage noDuration = Audio(1, DateTime.UtcNow.AddDays(-1));   // Duration == null
        tg.Audios.Add(noDuration);

        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        cache.RememberDuration(noDuration, TimeSpan.FromSeconds(200));

        var svc = new AudioFeedService(tg, cache);
        IReadOnlyList<FeedItem> items = await svc.LoadAsync(1, 7, CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(200), items.Single().Audio.Duration);
    }

    [Theory]
    [InlineData(-50, 50)]
    [InlineData(-200, 200)]
    public async Task LoadAsync_NegativeRange_ReturnsNewestNRegardlessOfAge(int range, int expected)
    {
        var tg = new FakeTelegramSource();
        // 600 audios, all more than a year old
        for (int i = 0; i < 600; i++)
        {
            tg.Audios.Add(Audio(i + 1, DateTime.UtcNow.AddDays(-400 - i)));
        }

        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(1, range, CancellationToken.None);

        Assert.Equal(expected, items.Count);
        Assert.Equal(1, items[0].Audio.FileId);   // newest first
    }

    [Fact]
    public async Task LoadAsync_Incremental_TopsUpWithNewPosts_WithoutRefetchingEverything()
    {
        var tg = new FakeTelegramSource();
        // Telegram-like: MessageId grows with time (100 = newest)
        for (int id = 1; id <= 100; id++)
        {
            tg.Audios.Add(new(ChatId: 1, MessageId: id, FileId: id, Title: "T", Performer: "P",
                Duration: null, SizeBytes: 10, FileName: $"{id}.mp3",
                DateUtc: DateTime.UtcNow.AddMinutes(-2000 + id)));
        }
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        // one track whose length was decoded on an earlier playback
        cache.RememberDuration(tg.Audios[99], TimeSpan.FromSeconds(222));   // FileId 100
        var svc = new AudioFeedService(tg, cache);

        IReadOnlyList<FeedItem> first = await svc.LoadAsync(1, -30, CancellationToken.None);
        Assert.Equal(30, first.Count);
        Assert.Equal(100, first[0].Audio.FileId);
        Assert.Equal(TimeSpan.FromSeconds(222), first[0].Audio.Duration);

        // 5 new songs posted since
        for (int id = 101; id <= 105; id++)
        {
            tg.Audios.Add(new(ChatId: 1, MessageId: id, FileId: id, Title: "T", Performer: "P",
                Duration: null, SizeBytes: 10, FileName: $"{id}.mp3",
                DateUtc: DateTime.UtcNow.AddMinutes(id)));
        }
        tg.SinceCalls = 0;
        tg.AfterCalls = 0;

        IReadOnlyList<FeedItem> again = await svc.LoadAsync(
            1, -30, CancellationToken.None, previous: first.Select(i => i.Audio).ToList());

        Assert.Equal(30, again.Count);
        Assert.Equal(105, again[0].Audio.FileId);   // newest new post floated to the top
        // the incremental merge must not drop a duration the previous list carried
        Assert.Equal(TimeSpan.FromSeconds(222), again.Single(i => i.Audio.FileId == 100).Audio.Duration);
        Assert.Equal(1, tg.AfterCalls);
        Assert.Equal(0, tg.SinceCalls);             // same count -> no older re-fetch

        // now grow the count -> the extra older ones are fetched, and the
        // progress must count on top of what we already had (not restart at 0)
        var probe = new CollectingProgress();
        IReadOnlyList<FeedItem> grown = await svc.LoadAsync(
            1, -60, CancellationToken.None, probe, previous: again.Select(i => i.Audio).ToList());
        Assert.Equal(60, grown.Count);
        Assert.True(tg.SinceCalls >= 1);
        Assert.NotEmpty(probe.Values);
        Assert.All(probe.Values, n => Assert.True(n >= 30));   // never below the 30 we kept
        Assert.Contains(probe.Values, n => n == 60);           // reaches the new goal
    }

    [Fact]
    public async Task LoadAsync_FreshLoad_ReportsEnrichedBatches()
    {
        var tg = new FakeTelegramSource();
        tg.Audios.Add(Audio(1, DateTime.UtcNow.AddDays(-1), size: 100));
        tg.Audios.Add(Audio(2, DateTime.UtcNow.AddDays(-2), size: 100));

        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        var svc = new AudioFeedService(tg, cache);
        var batches = new CollectingBatchProgress();

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(
            1, 7, CancellationToken.None, onBatch: batches);

        Assert.Equal(2, items.Count);
        // FakeTelegramSource reports its whole (capped) result as one batch -
        // real paging in TelegramSource reports one per page instead.
        Assert.NotEmpty(batches.Batches);
        Assert.Equal(
            items.Select(i => i.Audio.FileId).OrderBy(x => x),
            batches.Batches.SelectMany(b => b).Select(i => i.Audio.FileId).OrderBy(x => x));
    }

    [Fact]
    public async Task LoadAsync_IncrementalReuse_SameCount_DoesNotReportBatches()
    {
        // Nothing new to fetch when the count didn't grow - previous already
        // covers it, so there is nothing to progressively report.
        var tg = new FakeTelegramSource();
        for (int id = 1; id <= 10; id++)
        {
            tg.Audios.Add(new(1, id, id, "T", "P", null, 10, $"{id}.mp3", DateTime.UtcNow.AddMinutes(-id)));
        }
        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> first = await svc.LoadAsync(1, -5, CancellationToken.None);
        var batches = new CollectingBatchProgress();

        await svc.LoadAsync(1, -5, CancellationToken.None,
            previous: first.Select(i => i.Audio).ToList(), onBatch: batches);

        Assert.Empty(batches.Batches);
    }

    [Fact]
    public async Task LoadAsync_IncrementalReuse_Grown_ReportsTheExtraOlderBatch()
    {
        // Growing (e.g. Newest 5 -> Newest 10) fetches the extra older ones -
        // that fetch must report batches too, not just a fresh (no-previous) load.
        var tg = new FakeTelegramSource();
        // Telegram-like: MessageId grows with time (id 10 = newest).
        for (int id = 1; id <= 10; id++)
        {
            tg.Audios.Add(new(1, id, id, "T", "P", null, 10, $"{id}.mp3", DateTime.UtcNow.AddMinutes(-(11 - id))));
        }
        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> first = await svc.LoadAsync(1, -5, CancellationToken.None);
        var batches = new CollectingBatchProgress();

        IReadOnlyList<FeedItem> grown = await svc.LoadAsync(1, -10, CancellationToken.None,
            previous: first.Select(i => i.Audio).ToList(), onBatch: batches);

        Assert.Equal(10, grown.Count);
        Assert.NotEmpty(batches.Batches);
        // only the extra 5 older ones were reported, not the 5 already kept
        Assert.Equal(5, batches.Batches.SelectMany(b => b).Count());
    }

    [Fact]
    public async Task LoadAsync_WindowIsHourPrecise_NotRoundedToMidnight()
    {
        var tg = new FakeTelegramSource();
        // Right at the edge: 7 days minus/plus one hour.
        tg.Audios.Add(Audio(1, DateTime.UtcNow.AddDays(-7).AddHours(1)));   // just inside
        tg.Audios.Add(Audio(2, DateTime.UtcNow.AddDays(-7).AddHours(-1)));  // just outside

        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(1, 7, CancellationToken.None);

        Assert.Single(items);
        Assert.Equal(1, items[0].Audio.FileId);
    }
}

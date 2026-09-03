using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic.Services;

namespace ViewerForTelegram.Tests;

public class AudioFeedServiceTests
{
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

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(chatId: 1, days: 7, CancellationToken.None);

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
    public async Task LoadAsync_NegativeDays_ReturnEmptyListWithoutError()
    {
        var tg = new FakeTelegramSource();
        tg.Audios.Add(Audio(1, DateTime.UtcNow.AddDays(-1)));

        using var dir = TempPath.Dir();
        var svc = new AudioFeedService(tg, new FileMediaCache(dir.Path));

        IReadOnlyList<FeedItem> items = await svc.LoadAsync(1, days: -5, CancellationToken.None);

        Assert.Empty(items);
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

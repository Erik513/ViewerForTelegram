using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic.Services;

namespace ViewerForTelegram.Tests;

public class MediaDownloaderTests
{
    private const long NoLimit = long.MaxValue;

    private static AudioMessage Audio(long fileId, long size = 100) =>
        new(ChatId: 1, MessageId: (int)fileId, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: size, FileName: $"{fileId}.mp3", DateUtc: DateTime.UtcNow);

    [Fact]
    public async Task EnsureLocalAsync_DownloadsWhenNotCached()
    {
        var tg = new FakeTelegramSource();
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        var dl = new MediaDownloader(tg, cache, NoLimit);
        AudioMessage a = Audio(1);

        string path = await dl.EnsureLocalAsync(a, null, CancellationToken.None);

        Assert.Equal(1, tg.DownloadCalls);
        Assert.True(cache.Contains(a));
        Assert.Equal(cache.GetPath(a), path);
    }

    [Fact]
    public async Task EnsureLocalAsync_DoesNotDownloadWhenAlreadyCached()
    {
        var tg = new FakeTelegramSource();
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        AudioMessage a = Audio(1);
        File.WriteAllBytes(cache.GetPath(a), new byte[a.SizeBytes]);

        var dl = new MediaDownloader(tg, cache, NoLimit);
        string path = await dl.EnsureLocalAsync(a, null, CancellationToken.None);

        Assert.Equal(0, tg.DownloadCalls);
        Assert.Equal(cache.GetPath(a), path);
    }

    [Fact]
    public async Task EnsureLocalAsync_ParallelCallsForSameFile_DownloadOnce()
    {
        var gate = new TaskCompletionSource();
        var tg = new FakeTelegramSource
        {
            DownloadBehavior = async (m, path, _, ct) =>
            {
                await gate.Task;
                await File.WriteAllBytesAsync(path, new byte[m.SizeBytes], ct);
            }
        };
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        var dl = new MediaDownloader(tg, cache, NoLimit);
        AudioMessage a = Audio(1);

        Task<string> first = dl.EnsureLocalAsync(a, null, CancellationToken.None);
        Task<string> second = dl.EnsureLocalAsync(a, null, CancellationToken.None);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, tg.DownloadCalls);
        Assert.True(cache.Contains(a));
    }

    [Fact]
    public async Task Cancel_AbortsRunningDownload()
    {
        var started = new TaskCompletionSource();
        var tg = new FakeTelegramSource
        {
            DownloadBehavior = async (_, _, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
        };
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        var dl = new MediaDownloader(tg, cache, NoLimit);
        AudioMessage a = Audio(1);

        Task<string> task = dl.EnsureLocalAsync(a, null, CancellationToken.None);
        await started.Task;
        dl.Cancel(a.FileId);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(cache.Contains(a));
    }

    [Fact]
    public async Task EnsureLocalAsync_TrimsCacheToLimitAfterDownload()
    {
        var tg = new FakeTelegramSource();
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        AudioMessage old = Audio(99, size: 100);
        File.WriteAllBytes(cache.GetPath(old), new byte[100]);
        File.SetLastWriteTimeUtc(cache.GetPath(old), DateTime.UtcNow.AddHours(-5));

        var dl = new MediaDownloader(tg, cache, cacheLimitBytes: 120);
        AudioMessage fresh = Audio(1, size: 100);

        await dl.EnsureLocalAsync(fresh, null, CancellationToken.None);

        Assert.True(cache.Contains(fresh)); // the new one stays
        Assert.False(cache.Contains(old));  // the old one is trimmed away
    }
}

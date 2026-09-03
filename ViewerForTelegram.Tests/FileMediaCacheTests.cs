using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class FileMediaCacheTests
{
    private static AudioMessage Audio(long fileId, long size, string name = "Song.mp3") =>
        new(ChatId: 1, MessageId: 1, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: size, FileName: name, DateUtc: DateTime.UtcNow);

    [Fact]
    public void GetPath_ContainsFileIdAndLivesInCacheFolder()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        string path = cache.GetPath(Audio(999, 10, "Ka Miixer - Insomnia.mp3"));

        Assert.Equal(dir.Path, Path.GetDirectoryName(path));
        Assert.StartsWith("999__", Path.GetFileName(path));
        Assert.EndsWith(".mp3", path);
    }

    [Fact]
    public void GetPath_SanitizesInvalidCharacters()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        string name = Path.GetFileName(cache.GetPath(Audio(1, 1, "a/b:c*d?.mp3")));

        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain(':', name);
        Assert.DoesNotContain('*', name);
        Assert.DoesNotContain('?', name);
    }

    [Fact]
    public void Contains_OnlyWhenPresentAndSizeMatches()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        AudioMessage a = Audio(1, size: 100);

        Assert.False(cache.Contains(a));

        File.WriteAllBytes(cache.GetPath(a), new byte[50]); // wrong size
        Assert.False(cache.Contains(a));

        File.WriteAllBytes(cache.GetPath(a), new byte[100]); // matches
        Assert.True(cache.Contains(a));
    }

    [Fact]
    public void GetStats_CountsFilesAndBytes_IgnoresPartFiles()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        File.WriteAllBytes(Path.Combine(dir.Path, "1__a.mp3"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir.Path, "2__b.mp3"), new byte[200]);
        File.WriteAllBytes(Path.Combine(dir.Path, "3__c.mp3.part"), new byte[999]);

        (int count, long bytes) = cache.GetStats();

        Assert.Equal(2, count);
        Assert.Equal(300, bytes);
    }

    [Fact]
    public void Clear_DeletesAllCompleteFiles()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        File.WriteAllBytes(Path.Combine(dir.Path, "1__a.mp3"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir.Path, "2__b.mp3"), new byte[10]);

        cache.Clear();

        Assert.Equal((0, 0L), cache.GetStats());
    }

    [Fact]
    public void PruneToLimit_DeletesOldestUntilBelowLimit()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        string old = Path.Combine(dir.Path, "1__old.mp3");
        string mid = Path.Combine(dir.Path, "2__mid.mp3");
        string fresh = Path.Combine(dir.Path, "3__fresh.mp3");

        File.WriteAllBytes(old, new byte[100]);
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-3));
        File.WriteAllBytes(mid, new byte[100]);
        File.SetLastWriteTimeUtc(mid, DateTime.UtcNow.AddHours(-2));
        File.WriteAllBytes(fresh, new byte[100]);
        File.SetLastWriteTimeUtc(fresh, DateTime.UtcNow.AddHours(-1));

        cache.PruneToLimit(150); // must delete 2 of the 3 files

        Assert.False(File.Exists(old));
        Assert.False(File.Exists(mid));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void PruneToLimit_BelowLimit_DoesNothing()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        File.WriteAllBytes(Path.Combine(dir.Path, "1__a.mp3"), new byte[100]);

        cache.PruneToLimit(1_000_000);

        Assert.Equal(1, cache.GetStats().Count);
    }
}

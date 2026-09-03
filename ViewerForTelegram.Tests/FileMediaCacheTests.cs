using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class FileMediaCacheTests
{
    private static AudioMessage Audio(long fileId, long size, string name = "Song.mp3") =>
        new(ChatId: 1, MessageId: 1, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: size, FileName: name, DateUtc: DateTime.UtcNow);

    [Fact]
    public void GetPath_EnthaeltFileIdUndLiegtImCacheOrdner()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        string path = cache.GetPath(Audio(999, 10, "Ka Miixer - Insomnia.mp3"));

        Assert.Equal(dir.Path, Path.GetDirectoryName(path));
        Assert.StartsWith("999__", Path.GetFileName(path));
        Assert.EndsWith(".mp3", path);
    }

    [Fact]
    public void GetPath_SaeubertUnzulaessigeZeichen()
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
    public void Contains_NurWennDaUndGroessePasst()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        AudioMessage a = Audio(1, size: 100);

        Assert.False(cache.Contains(a));

        File.WriteAllBytes(cache.GetPath(a), new byte[50]); // falsche Größe
        Assert.False(cache.Contains(a));

        File.WriteAllBytes(cache.GetPath(a), new byte[100]); // passt
        Assert.True(cache.Contains(a));
    }

    [Fact]
    public void GetStats_ZaehltDateienUndBytes_IgnoriertPartDateien()
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
    public void Clear_LoeschtAlleFertigenDateien()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        File.WriteAllBytes(Path.Combine(dir.Path, "1__a.mp3"), new byte[10]);
        File.WriteAllBytes(Path.Combine(dir.Path, "2__b.mp3"), new byte[10]);

        cache.Clear();

        Assert.Equal((0, 0L), cache.GetStats());
    }

    [Fact]
    public void PruneToLimit_LoeschtDieAeltestenBisUnterGrenze()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);

        string alt = Path.Combine(dir.Path, "1__alt.mp3");
        string mittel = Path.Combine(dir.Path, "2__mittel.mp3");
        string neu = Path.Combine(dir.Path, "3__neu.mp3");

        File.WriteAllBytes(alt, new byte[100]);
        File.SetLastWriteTimeUtc(alt, DateTime.UtcNow.AddHours(-3));
        File.WriteAllBytes(mittel, new byte[100]);
        File.SetLastWriteTimeUtc(mittel, DateTime.UtcNow.AddHours(-2));
        File.WriteAllBytes(neu, new byte[100]);
        File.SetLastWriteTimeUtc(neu, DateTime.UtcNow.AddHours(-1));

        cache.PruneToLimit(150); // muss 2 der 3 Dateien löschen

        Assert.False(File.Exists(alt));
        Assert.False(File.Exists(mittel));
        Assert.True(File.Exists(neu));
    }

    [Fact]
    public void PruneToLimit_UnterGrenze_MachtNichts()
    {
        using var dir = TempPath.Dir();
        var cache = new FileMediaCache(dir.Path);
        File.WriteAllBytes(Path.Combine(dir.Path, "1__a.mp3"), new byte[100]);

        cache.PruneToLimit(1_000_000);

        Assert.Equal(1, cache.GetStats().Count);
    }
}

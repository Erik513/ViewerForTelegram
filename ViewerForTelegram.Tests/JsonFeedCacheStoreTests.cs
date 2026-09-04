using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class JsonFeedCacheStoreTests
{
    private static AudioMessage Audio(long fileId, DateTime dateUtc) =>
        new(ChatId: 1, MessageId: (int)fileId, FileId: fileId, Title: "T", Performer: "P",
            Duration: TimeSpan.FromSeconds(180), SizeBytes: 12345, FileName: $"{fileId}.mp3", DateUtc: dateUtc);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);

        var feed = new PersistedFeed(42, -1000, new List<AudioMessage>
        {
            Audio(1, DateTime.UtcNow),
            Audio(2, DateTime.UtcNow.AddDays(-1)),
        });

        store.Save(feed);
        PersistedFeed? loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(feed.ChatId, loaded!.ChatId);
        Assert.Equal(feed.Range, loaded.Range);
        Assert.Equal(feed.Audios, loaded.Audios);
    }

    [Fact]
    public void Load_FileMissing_ReturnsNull()
    {
        using var file = TempPath.File(); // not created
        Assert.Null(new JsonFeedCacheStore(file.Path).Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, "{ this is not JSON ");

        Assert.Null(new JsonFeedCacheStore(file.Path).Load());
    }

    [Fact]
    public void SaveAndLoad_HandlesAudiosWithNullDuration()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);
        var withNullDuration = new AudioMessage(1, 1, 1, "T", "", null, 100, "f.mp3", DateTime.UtcNow);

        store.Save(new PersistedFeed(1, 7, new List<AudioMessage> { withNullDuration }));
        PersistedFeed? loaded = store.Load();

        Assert.Null(loaded!.Audios.Single().Duration);
    }
}

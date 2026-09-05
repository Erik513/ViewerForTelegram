using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Tests;

public class JsonFeedCacheStoreTests
{
    private static AudioMessage Audio(long chatId, long fileId, DateTime dateUtc) =>
        new(ChatId: chatId, MessageId: (int)fileId, FileId: fileId, Title: "T", Performer: "P",
            Duration: TimeSpan.FromSeconds(180), SizeBytes: 12345, FileName: $"{fileId}.mp3", DateUtc: dateUtc);

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);

        var feed = new PersistedFeed(42, -1000, new List<AudioMessage>
        {
            Audio(42, 1, DateTime.UtcNow),
            Audio(42, 2, DateTime.UtcNow.AddDays(-1)),
        });

        store.Save(feed);
        PersistedFeed? loaded = store.Load(42);

        Assert.NotNull(loaded);
        Assert.Equal(feed.ChatId, loaded!.ChatId);
        Assert.Equal(feed.Range, loaded.Range);
        Assert.Equal(feed.Audios, loaded.Audios);
    }

    [Fact]
    public void Load_UnknownChat_ReturnsNull()
    {
        using var file = TempPath.File(); // not created
        Assert.Null(new JsonFeedCacheStore(file.Path).Load(1));
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        using var file = TempPath.File();
        File.WriteAllText(file.Path, "{ this is not JSON ");

        Assert.Null(new JsonFeedCacheStore(file.Path).Load(1));
    }

    [Fact]
    public void SaveAndLoad_HandlesAudiosWithNullDuration()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);
        var withNullDuration = new AudioMessage(1, 1, 1, "T", "", null, 100, "f.mp3", DateTime.UtcNow);

        store.Save(new PersistedFeed(1, 7, new List<AudioMessage> { withNullDuration }));
        PersistedFeed? loaded = store.Load(1);

        Assert.Null(loaded!.Audios.Single().Duration);
    }

    [Fact]
    public void EachChat_IsStoredAndLoadedIndependently()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);

        store.Save(new PersistedFeed(1, -50, new List<AudioMessage> { Audio(1, 1, DateTime.UtcNow) }));
        store.Save(new PersistedFeed(2, -200, new List<AudioMessage> { Audio(2, 2, DateTime.UtcNow) }));

        Assert.Equal(-50, store.Load(1)!.Range);
        Assert.Equal(-200, store.Load(2)!.Range);

        // Saving chat 1 again must not disturb chat 2's entry.
        store.Save(new PersistedFeed(1, -100, new List<AudioMessage> { Audio(1, 1, DateTime.UtcNow) }));
        Assert.Equal(-100, store.Load(1)!.Range);
        Assert.Equal(-200, store.Load(2)!.Range);
    }

    [Fact]
    public void Save_CapsAudiosAtMaxPerChat()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);
        var many = Enumerable.Range(0, JsonFeedCacheStore.MaxAudiosPerChat + 500)
            .Select(i => Audio(1, i + 1, DateTime.UtcNow.AddMinutes(-i)))
            .ToList();

        store.Save(new PersistedFeed(1, -(JsonFeedCacheStore.MaxAudiosPerChat + 500), many));

        Assert.Equal(JsonFeedCacheStore.MaxAudiosPerChat, store.Load(1)!.Audios.Count);
    }

    [Fact]
    public void PruneToKnownChats_RemovesChatsNotInTheList()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);
        store.Save(new PersistedFeed(1, -50, new List<AudioMessage> { Audio(1, 1, DateTime.UtcNow) }));
        store.Save(new PersistedFeed(2, -50, new List<AudioMessage> { Audio(2, 2, DateTime.UtcNow) }));
        store.Save(new PersistedFeed(3, -50, new List<AudioMessage> { Audio(3, 3, DateTime.UtcNow) }));

        store.PruneToKnownChats(new long[] { 1, 3 }); // chat 2 was left

        Assert.NotNull(store.Load(1));
        Assert.Null(store.Load(2));
        Assert.NotNull(store.Load(3));
    }

    [Fact]
    public void PruneToKnownChats_NothingStale_DoesNotRewriteFile()
    {
        using var file = TempPath.File();
        var store = new JsonFeedCacheStore(file.Path);
        store.Save(new PersistedFeed(1, -50, new List<AudioMessage> { Audio(1, 1, DateTime.UtcNow) }));
        DateTime before = File.GetLastWriteTimeUtc(file.Path);

        store.PruneToKnownChats(new long[] { 1, 2 });

        Assert.Equal(before, File.GetLastWriteTimeUtc(file.Path));
    }
}

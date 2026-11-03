using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;

namespace ViewerForTelegram.Tests;

public class PlayerTrackTests
{
    private static AudioMessage Audio(long fileId) =>
        new(ChatId: 1, MessageId: (int)fileId, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: 100, FileName: $"{fileId}.mp3", DateUtc: DateTime.UtcNow);

    [Fact]
    public void Choose_NothingLoaded_ShowsSelectedRow()
    {
        AudioMessage selected = Audio(3);

        Assert.Same(selected, PlayerTrack.Choose(null, null, selected));
    }

    [Fact]
    public void Choose_LoadedTrack_StaysWhileBrowsing()
    {
        AudioMessage loaded = Audio(1);

        Assert.Same(loaded, PlayerTrack.Choose(null, loaded, Audio(2)));
    }

    [Fact]
    public void Choose_Download_BeatsLoadedAndSelected()
    {
        AudioMessage downloading = Audio(1);

        Assert.Same(downloading, PlayerTrack.Choose(downloading, Audio(2), Audio(3)));
    }

    [Fact]
    public void Choose_NothingAtAll_IsNull()
    {
        Assert.Null(PlayerTrack.Choose(null, null, null));
    }

    [Fact]
    public void Choose_LoadedTrackWithoutSelection_StillShowsLoaded()
    {
        AudioMessage loaded = Audio(1);

        Assert.Same(loaded, PlayerTrack.Choose(null, loaded, null));
    }
}

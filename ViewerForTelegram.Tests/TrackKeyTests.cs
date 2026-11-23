using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;

namespace ViewerForTelegram.Tests;

public class TrackKeyTests
{
    private static AudioMessage Audio(long chatId, long fileId) =>
        new(ChatId: chatId, MessageId: 1, FileId: fileId, Title: "T", Performer: "P",
            Duration: null, SizeBytes: 100, FileName: $"{fileId}.mp3", DateUtc: DateTime.UtcNow);

    [Fact]
    public void Matches_SameFileInTheSameChat_IsTrue() =>
        Assert.True(TrackKey.Of(Audio(1, 42)).Matches(Audio(1, 42)));

    [Fact]
    public void Matches_SameFileInAnotherChat_IsFalse() =>
        Assert.False(TrackKey.Of(Audio(1, 42)).Matches(Audio(2, 42)));

    [Fact]
    public void Matches_OtherFileInTheSameChat_IsFalse() =>
        Assert.False(TrackKey.Of(Audio(1, 42)).Matches(Audio(1, 43)));

    [Fact]
    public void Matches_UnknownChat_MatchesTheFileInAnyChat()
    {
        var fromOlderState = new TrackKey(0, 42);

        Assert.True(fromOlderState.Matches(Audio(1, 42)));
        Assert.True(fromOlderState.Matches(Audio(2, 42)));
        Assert.False(fromOlderState.Matches(Audio(1, 43)));
    }

    [Fact]
    public void Of_TakesChatAndFileOfTheMessage()
    {
        TrackKey key = TrackKey.Of(Audio(7, 99));

        Assert.Equal(7, key.ChatId);
        Assert.Equal(99, key.FileId);
    }
}

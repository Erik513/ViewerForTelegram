using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Logic;

/// <summary>
/// Which list row a remembered track points at: the same file can sit in
/// several chats (a forward keeps its Telegram file id), and the row that is
/// playing / selected / scrolled to belongs to the chat it was started in -
/// the file id alone would also mark and scroll to its twin in another chat.
/// </summary>
/// <param name="ChatId">The chat of the track; 0 = not known (state of an older version), matches any chat.</param>
public readonly record struct TrackKey(long ChatId, long FileId)
{
    public static TrackKey Of(AudioMessage audio) => new(audio.ChatId, audio.FileId);

    public bool Matches(AudioMessage audio) =>
        audio.FileId == FileId && (ChatId == 0 || audio.ChatId == ChatId);
}

namespace ViewerForTelegram.Data.Models;

/// <summary>
/// A single audio file from a chat - metadata only.
/// The actual bytes are fetched on demand (playback / download) via
/// <see cref="Interfaces.ITelegramSource"/> and stored locally via
/// <see cref="Interfaces.IMediaCache"/>.
/// </summary>
/// <param name="ChatId">Which chat the message belongs to (<see cref="TelegramChat.Id"/>).</param>
/// <param name="MessageId">Running message number within the chat.</param>
/// <param name="FileId">
/// Telegram document ID of the audio file. A stable key for the cache: the same
/// file posted multiple times -> same FileId -> downloaded only once.
/// </param>
/// <param name="Title">Title tag of the file, otherwise the file name without extension.</param>
/// <param name="Performer">Performer tag of the file (may be empty).</param>
/// <param name="Duration">
/// Length of the track, or <c>null</c> when Telegram does not know it (the file
/// was posted "as a file" instead of "as music"). Backfilled from the file
/// contents after download.
/// </param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="FileName">Original file name incl. extension (e.g. "track.mp3").</param>
/// <param name="DateUtc">Time of the post, in UTC.</param>
public sealed record AudioMessage(
    long ChatId,
    int MessageId,
    long FileId,
    string Title,
    string Performer,
    TimeSpan? Duration,
    long SizeBytes,
    string FileName,
    DateTime DateUtc)
{
    /// <summary>
    /// "Performer - Title", or just the title when no performer is known.
    /// Pure display logic - allowed in the model because it only composes its
    /// own fields and needs no other layer.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Performer) ? Title : $"{Performer} - {Title}";

    /// <summary>
    /// Average bit rate in kbit/s (file size × 8 ÷ duration) - the "overall"
    /// rate you'd see in a tag editor. <c>null</c> while the duration is unknown.
    /// </summary>
    public int? BitrateKbps =>
        Duration is { TotalSeconds: > 0 } d
            ? (int)Math.Round(SizeBytes * 8 / d.TotalSeconds / 1000)
            : null;
}

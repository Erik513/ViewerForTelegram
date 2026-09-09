using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// The single entry point to Telegram. Only the Data implementation of this
/// interface talks to WTelegramClient - Logic and UI know nothing but this
/// contract. That keeps the source swappable (later e.g. the Bot API) and
/// replaceable by a fake in tests.
/// </summary>
public interface ITelegramSource : IAsyncDisposable
{
    /// <summary>
    /// Opens the connection and signs the account in. On the first run the
    /// implementation calls <paramref name="requestVerificationCode"/> to obtain
    /// the login code delivered by SMS/Telegram. After that the stored session
    /// file takes over, without another prompt.
    /// </summary>
    /// <param name="requestVerificationCode">
    /// Callback the UI backs with an input dialog and that returns the code.
    /// </param>
    Task ConnectAsync(
        Func<Task<string>> requestVerificationCode,
        CancellationToken ct);

    /// <summary>
    /// Everything the signed-in account can pull audio from: its groups and
    /// channels, its own "Saved Messages", and its chats with bots. Regular
    /// person-to-person DMs are deliberately excluded.
    /// </summary>
    Task<IReadOnlyList<TelegramChat>> GetChatsAsync(CancellationToken ct);

    /// <summary>
    /// Audio messages from <paramref name="chatId"/>, newest first: those posted
    /// on or after <paramref name="sinceUtc"/>, capped at <paramref name="maxAudios"/>.
    /// Pass <see cref="DateTime.MinValue"/> for "no date limit, just the newest N".
    /// </summary>
    /// <param name="onBatch">
    /// Optional: reports just the audios found on each fetched page (not the
    /// running total) as soon as that page is mapped, newest-first order
    /// preserved across calls - lets a large "newest N" pull show rows as they
    /// arrive instead of only once everything is fetched.
    /// </param>
    Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId,
        DateTime sinceUtc,
        CancellationToken ct,
        int maxAudios = int.MaxValue,
        IProgress<int>? progress = null,
        int beforeMessageId = 0,
        IProgress<IReadOnlyList<AudioMessage>>? onBatch = null);

    /// <summary>
    /// Audio messages from <paramref name="chatId"/> posted after
    /// <paramref name="afterMessageId"/>, newest first - to catch what was
    /// posted since an earlier load without re-fetching everything.
    /// </summary>
    Task<IReadOnlyList<AudioMessage>> GetAudioMessagesAfterAsync(
        long chatId,
        int afterMessageId,
        CancellationToken ct);

    /// <summary>
    /// Downloads the bytes of the audio file belonging to
    /// <paramref name="message"/> and writes them to
    /// <paramref name="targetPath"/>.
    /// </summary>
    /// <param name="progress">Progress 0..100, optional.</param>
    Task DownloadAsync(
        AudioMessage message,
        string targetPath,
        IProgress<int>? progress,
        CancellationToken ct);

    /// <summary>
    /// Which of <paramref name="messageIds"/> (from <paramref name="chatId"/>)
    /// no longer exist on Telegram - re-queried explicitly, since a deletion
    /// that happened while this app wasn't connected is never pushed as an
    /// update after the fact. A background correctness pass, not part of the
    /// normal load path: default implementation reports nothing deleted.
    /// </summary>
    Task<IReadOnlyList<int>> FindDeletedMessagesAsync(
        long chatId,
        IReadOnlyList<int> messageIds,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<int>>(Array.Empty<int>());
}

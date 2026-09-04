namespace ViewerForTelegram.Data.Models;

/// <summary>
/// The most recently displayed audio list for one chat, saved to disk so a
/// restart can show it immediately and only fetch what changed since -
/// instead of re-fetching everything (e.g. "Newest 5000" from scratch).
/// </summary>
/// <param name="ChatId">Which chat this list belongs to.</param>
/// <param name="Range">
/// The range/count it was loaded with (see <see cref="Logic.Services.AudioFeedService.LoadAsync"/>) -
/// only a count-mode (&lt;= 0) value lets the restart reuse this list
/// incrementally; a day-window value still shows it instantly but triggers a
/// normal fresh re-fetch.
/// </param>
/// <param name="Audios">The audios, newest first.</param>
public sealed record PersistedFeed(long ChatId, int Range, IReadOnlyList<AudioMessage> Audios);

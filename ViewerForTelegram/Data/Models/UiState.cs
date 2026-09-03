namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Small remembered UI preferences (which chat was open, the time range, the
/// volume). Stored next to the config as <c>ui-state.json</c>. Losing it is
/// harmless - the app just falls back to <see cref="Default"/>.
/// </summary>
public sealed record UiState(
    long LastChatId = 0,
    int RangeDays = 7,
    int VolumePercent = 10)
{
    public static UiState Default { get; } = new();
}

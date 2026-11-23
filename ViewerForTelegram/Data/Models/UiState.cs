using System.Text.Json.Serialization;
using ErikwnkCore;

namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Small remembered UI preferences (which chat was open, the time range, the
/// volume, the last track shown in the player). Stored next to the config as
/// <c>ui-state.json</c>. Losing it is harmless - the app just falls back to
/// <see cref="Default"/>.
/// </summary>
/// <param name="VolumePercent">Legacy: the volume before <paramref name="Volume"/> existed (read once, never the newer one).</param>
/// <param name="VolumeMuted">Legacy, as <paramref name="VolumePercent"/>.</param>
/// <param name="Volume">Volume and mute of the player; null in a file written by an older version.</param>
/// <param name="LastPlayedChatId">The chat the last played track was started in; 0 in a file of an older version (then the file id alone decides).</param>
public sealed record UiState(
    long LastChatId = 0,
    int RangeDays = 7,
    int VolumePercent = 10,
    long LastPlayedFileId = 0,
    string FormatFilter = "",
    bool VolumeMuted = false,
    AudioVolumeSnapshot? Volume = null,
    long LastPlayedChatId = 0)
{
    public static UiState Default { get; } = new();

    /// <summary>The volume to start with: the saved snapshot, else what an older version wrote.</summary>
    [JsonIgnore]
    public AudioVolumeSnapshot StartVolume => Volume ?? new AudioVolumeSnapshot
    {
        Volume = Math.Clamp(VolumePercent, 0, 100) / 100.0,
        IsMuted = VolumeMuted,
    };
}

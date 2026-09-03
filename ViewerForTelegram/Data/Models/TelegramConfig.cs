using System.Text.Json.Serialization;

namespace ViewerForTelegram.Data.Models;

/// <summary>
/// The credentials the user enters on first run. Stored as JSON in
/// <see cref="AppPaths.ConfigFile"/> - never in the repo.
///
/// Every user obtains api_id / api_hash themselves on my.telegram.org
/// ("API development tools"); they cannot be shipped with a public app.
/// </summary>
/// <param name="ApiId">Number from my.telegram.org.</param>
/// <param name="ApiHash">32-character hex string from my.telegram.org.</param>
/// <param name="PhoneNumber">Phone number of the Telegram account, international (e.g. +49170...).</param>
/// <param name="ClearCacheOnStart">
/// When true (default), the song cache is wiped on every startup - fitting the
/// "just checking what's new" use case. Can be turned off, then the cache is
/// kept and only trimmed to the size limit.
/// </param>
/// <param name="DownloadFolder">Default folder for "download" from the player.</param>
/// <param name="UseDownloadFolder">
/// true: downloads go to <see cref="DownloadFolder"/> without asking.
/// false: pick the folder on every download.
/// </param>
public sealed record TelegramConfig(
    int ApiId,
    string ApiHash,
    string PhoneNumber,
    bool ClearCacheOnStart = true,
    string DownloadFolder = "",
    bool UseDownloadFolder = false)
{
    /// <summary>Empty state for "nothing entered yet".</summary>
    public static TelegramConfig Empty { get; } = new(0, "", "");

    /// <summary>Are all required fields plausibly filled?</summary>
    [JsonIgnore]
    public bool IsComplete =>
        ApiId > 0
        && !string.IsNullOrWhiteSpace(ApiHash)
        && !string.IsNullOrWhiteSpace(PhoneNumber);
}

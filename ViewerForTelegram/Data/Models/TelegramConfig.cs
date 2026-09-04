using System.Text.Json.Serialization;

namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Everything the Settings dialog persists: the credentials the user enters on
/// first run plus a few app preferences. Stored as JSON in
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
/// <param name="DownloadFolder">
/// Folder for "Save a copy". Blank means "follow the Windows Downloads folder" -
/// see <see cref="EffectiveDownloadFolder"/>.
/// </param>
/// <param name="UseDownloadFolder">
/// true: downloads go to <see cref="DownloadFolder"/> without asking.
/// false: pick the folder on every download.
/// </param>
/// <param name="Language">
/// UI language for the library's built-in dialog text. English by default;
/// a change takes full effect on the next start.
/// </param>
public sealed record TelegramConfig(
    int ApiId,
    string ApiHash,
    string PhoneNumber,
    bool ClearCacheOnStart = true,
    string DownloadFolder = "",
    bool UseDownloadFolder = false,
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    DisplayLanguage Language = DisplayLanguage.English)
{
    /// <summary>Empty state for "nothing entered yet".</summary>
    public static TelegramConfig Empty { get; } = new(0, "", "");

    /// <summary>
    /// The folder "Save a copy" actually uses: the user's chosen
    /// <see cref="DownloadFolder"/>, or the Windows Downloads folder while it is
    /// left blank.
    /// </summary>
    [JsonIgnore]
    public string EffectiveDownloadFolder =>
        string.IsNullOrWhiteSpace(DownloadFolder) ? AppPaths.DownloadsFolder : DownloadFolder;

    /// <summary>Are all required fields plausibly filled?</summary>
    [JsonIgnore]
    public bool IsComplete =>
        ApiId > 0
        && !string.IsNullOrWhiteSpace(ApiHash)
        && !string.IsNullOrWhiteSpace(PhoneNumber);
}

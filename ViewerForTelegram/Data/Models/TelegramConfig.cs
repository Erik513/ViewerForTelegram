using System.Text.Json.Serialization;

namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Die Zugangsdaten, die der Nutzer beim ersten Start eingibt. Werden als
/// JSON in <see cref="AppPaths.ConfigFile"/> gespeichert - nie im Repo.
///
/// api_id / api_hash holt sich jeder Nutzer selbst auf my.telegram.org
/// ("API development tools"); sie können bei einer öffentlichen App nicht
/// mitgeliefert werden.
/// </summary>
/// <param name="ApiId">Zahl von my.telegram.org.</param>
/// <param name="ApiHash">32-stelliger Hex-String von my.telegram.org.</param>
/// <param name="PhoneNumber">Telefonnummer des Telegram-Kontos, international (z. B. +49170...).</param>
/// <param name="ClearCacheOnStart">
/// Wenn true (Standard), wird der Song-Cache bei jedem Programmstart geleert -
/// passend zum "ich höre mal rein"-Anwendungsfall. Ausschaltbar, dann bleibt
/// der Cache und wird nur auf die Obergrenze gestutzt.
/// </param>
/// <param name="DownloadFolder">Standardordner für "Herunterladen" aus dem Player.</param>
/// <param name="UseDownloadFolder">
/// true: Downloads gehen ohne Nachfrage in <see cref="DownloadFolder"/>.
/// false: bei jedem Download den Ordner wählen.
/// </param>
public sealed record TelegramConfig(
    int ApiId,
    string ApiHash,
    string PhoneNumber,
    bool ClearCacheOnStart = true,
    string DownloadFolder = "",
    bool UseDownloadFolder = false)
{
    /// <summary>Leerzustand für "noch nichts eingegeben".</summary>
    public static TelegramConfig Empty { get; } = new(0, "", "");

    /// <summary>Sind alle Pflichtfelder plausibel gefüllt?</summary>
    [JsonIgnore]
    public bool IsComplete =>
        ApiId > 0
        && !string.IsNullOrWhiteSpace(ApiHash)
        && !string.IsNullOrWhiteSpace(PhoneNumber);
}

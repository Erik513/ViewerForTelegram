namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Eine Telegram-Gruppe oder ein Kanal, den der angemeldete Account sehen kann.
/// Reiner Datencontainer: wird von der Data-Schicht erzeugt und nach oben
/// (Logic, UI) durchgereicht. Kein Verhalten, keine Telegram-Interna.
/// </summary>
/// <param name="Id">
/// Telegram-interne ID des Chats. Für die UI nur ein undurchsichtiger
/// Schlüssel; wie daraus wieder ein ansprechbarer Telegram-Peer wird, ist
/// allein Sache der Data-Implementierung.
/// </param>
/// <param name="Title">Angezeigter Name der Gruppe / des Kanals.</param>
/// <param name="Kind">Grobe Einordnung für Anzeige/Icon.</param>
public sealed record TelegramChat(
    long Id,
    string Title,
    TelegramChatKind Kind);

/// <summary>
/// Telegram kennt mehrere Untertypen (Basisgruppe, Supergruppe/Megagroup,
/// Broadcast-Kanal). Für diese App reicht die Unterscheidung
/// "kann jeder schreiben" (Gruppe) vs. "nur Betreiber posten" (Kanal).
/// </summary>
public enum TelegramChatKind
{
    Group,
    Channel
}

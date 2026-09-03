namespace ViewerForTelegram.Data.Models;

/// <summary>
/// A Telegram group or channel the signed-in account can see. Pure data
/// container: created by the Data layer and passed up (Logic, UI). No behavior,
/// no Telegram internals.
/// </summary>
/// <param name="Id">
/// Telegram-internal chat ID. To the UI just an opaque key; how it turns back
/// into an addressable Telegram peer is the Data implementation's concern alone.
/// </param>
/// <param name="Title">Display name of the group / channel.</param>
/// <param name="Kind">Rough classification for display/icon.</param>
public sealed record TelegramChat(
    long Id,
    string Title,
    TelegramChatKind Kind);

/// <summary>
/// Telegram has several subtypes (basic group, supergroup/megagroup, broadcast
/// channel). For this app the distinction "anyone can post" (group) vs. "only
/// admins post" (channel) is enough.
/// </summary>
public enum TelegramChatKind
{
    Group,
    Channel
}

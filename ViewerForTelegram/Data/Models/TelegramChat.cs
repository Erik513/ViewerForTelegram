namespace ViewerForTelegram.Data.Models;

/// <summary>
/// A conversation the signed-in account can pull audio from - a group, a
/// channel, the account's own "Saved Messages", or a chat with a bot. Pure data
/// container: created by the Data layer and passed up (Logic, UI). No behavior,
/// no Telegram internals.
/// </summary>
/// <param name="Id">
/// Telegram-internal id (chat / channel / user). To the UI just an opaque key;
/// how it turns back into an addressable Telegram peer is the Data
/// implementation's concern alone.
/// </param>
/// <param name="Title">Display name. Empty/placeholder for <see cref="TelegramChatKind.SavedMessages"/> - the UI labels that one itself.</param>
/// <param name="Kind">Rough classification for display/icon.</param>
public sealed record TelegramChat(
    long Id,
    string Title,
    TelegramChatKind Kind);

/// <summary>
/// Telegram has several subtypes (basic group, supergroup/megagroup, broadcast
/// channel). For this app the distinction "anyone can post" (group) vs. "only
/// admins post" (channel) is enough, plus the two 1:1 cases it also lists:
/// the account's own <see cref="SavedMessages"/> and a chat with a
/// <see cref="Bot"/>.
/// </summary>
public enum TelegramChatKind
{
    Group,
    Channel,
    SavedMessages,
    Bot
}

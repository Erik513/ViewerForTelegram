using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// Der einzige Zugang zu Telegram. Nur die Data-Implementierung dieses
/// Interfaces spricht mit WTelegramClient - Logic und UI kennen ausschließlich
/// diesen Vertrag. Dadurch lässt sich die Quelle austauschen (später z. B.
/// Bot-API) oder im Test durch einen Fake ersetzen.
/// </summary>
public interface ITelegramSource : IAsyncDisposable
{
    /// <summary>
    /// Baut die Verbindung auf und meldet den Account an. Beim ersten Start
    /// ruft die Implementierung <paramref name="requestVerificationCode"/> auf,
    /// um den per SMS/Telegram zugestellten Login-Code abzufragen. Danach
    /// übernimmt die gespeicherte Session-Datei, ohne erneute Abfrage.
    /// </summary>
    /// <param name="requestVerificationCode">
    /// Callback, den die UI mit einem Eingabedialog füllt und den Code
    /// zurückgibt.
    /// </param>
    Task ConnectAsync(
        Func<Task<string>> requestVerificationCode,
        CancellationToken ct);

    /// <summary>Alle Gruppen und Kanäle, die der angemeldete Account sehen kann.</summary>
    Task<IReadOnlyList<TelegramChat>> GetChatsAsync(CancellationToken ct);

    /// <summary>
    /// Alle Audio-Nachrichten aus <paramref name="chatId"/>, die am oder nach
    /// <paramref name="sinceUtc"/> gepostet wurden - neueste zuerst.
    /// </summary>
    Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId,
        DateTime sinceUtc,
        CancellationToken ct);

    /// <summary>
    /// Lädt die Bytes der zu <paramref name="message"/> gehörenden Audiodatei
    /// herunter und schreibt sie nach <paramref name="targetPath"/>.
    /// </summary>
    /// <param name="progress">Fortschritt 0..100, optional.</param>
    Task DownloadAsync(
        AudioMessage message,
        string targetPath,
        IProgress<int>? progress,
        CancellationToken ct);
}

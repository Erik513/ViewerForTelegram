namespace ViewerForTelegram.Data.Models;

/// <summary>
/// Eine einzelne Audiodatei aus einem Chat - nur die Metadaten.
/// Die eigentlichen Bytes werden erst bei Bedarf (Abspielen / Download)
/// über <see cref="Interfaces.ITelegramSource"/> geholt und über
/// <see cref="Interfaces.IMediaCache"/> lokal abgelegt.
/// </summary>
/// <param name="ChatId">Zu welchem Chat die Nachricht gehört (<see cref="TelegramChat.Id"/>).</param>
/// <param name="MessageId">Laufende Nachrichtennummer innerhalb des Chats.</param>
/// <param name="FileId">
/// Telegram-Dokument-ID der Audiodatei. Stabiler Schlüssel für den Cache:
/// dieselbe Datei mehrfach gepostet -> gleiche FileId -> nur einmal laden.
/// </param>
/// <param name="Title">Titel-Tag der Datei, sonst der Dateiname ohne Endung.</param>
/// <param name="Performer">Interpret-Tag der Datei (kann leer sein).</param>
/// <param name="Duration">
/// Länge des Stücks, oder <c>null</c> wenn Telegram sie nicht kennt (Datei
/// wurde "als Datei" statt "als Musik" gepostet). Wird nach dem Download aus
/// dem Datei-Inhalt nachgetragen.
/// </param>
/// <param name="SizeBytes">Dateigröße in Byte.</param>
/// <param name="FileName">Originaldateiname inkl. Endung (z. B. "track.mp3").</param>
/// <param name="DateUtc">Zeitpunkt des Posts, in UTC.</param>
public sealed record AudioMessage(
    long ChatId,
    int MessageId,
    long FileId,
    string Title,
    string Performer,
    TimeSpan? Duration,
    long SizeBytes,
    string FileName,
    DateTime DateUtc)
{
    /// <summary>
    /// "Interpret - Titel", oder nur der Titel, wenn kein Interpret bekannt ist.
    /// Reine Anzeigelogik - darf im Modell stehen, weil sie nur eigene Felder
    /// zusammensetzt und keine fremde Schicht braucht.
    /// </summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Performer) ? Title : $"{Performer} - {Title}";
}

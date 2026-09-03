using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// Verwaltet den lokalen Ordner mit bereits heruntergeladenen Audiodateien.
/// Weiss nichts von Telegram - beantwortet nur "welche Datei liegt wo".
/// Das eigentliche Herunterladen macht <see cref="ITelegramSource.DownloadAsync"/>;
/// die Logic-Schicht bringt beide zusammen.
/// </summary>
public interface IMediaCache
{
    /// <summary>Liegt die Datei zu dieser Nachricht bereits vollständig lokal vor?</summary>
    bool Contains(AudioMessage message);

    /// <summary>
    /// Voller Pfad, unter dem die Datei liegt bzw. liegen soll - unabhängig
    /// davon, ob sie schon da ist. Dient als Ziel für den Download.
    /// </summary>
    string GetPath(AudioMessage message);

    /// <summary>Kennzahlen des Caches für die Anzeige (Anzahl Dateien, belegte Bytes).</summary>
    (int Count, long TotalBytes) GetStats();

    /// <summary>Löscht alle vollständigen Cache-Dateien.</summary>
    void Clear();

    /// <summary>
    /// Löscht die ältesten Dateien, bis der Cache <paramref name="maxBytes"/>
    /// nicht mehr überschreitet. Kein Effekt, wenn er schon darunter liegt.
    /// </summary>
    void PruneToLimit(long maxBytes);
}

using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// Lädt und speichert die <see cref="TelegramConfig"/>. Kapselt, WO und WIE
/// gespeichert wird (aktuell: JSON-Datei unter %AppData%) - Logic und UI
/// sehen nur Load/Save.
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Liest die gespeicherte Config. Gibt <see cref="TelegramConfig.Empty"/>
    /// zurück, wenn noch nichts gespeichert wurde oder die Datei unlesbar ist.
    /// </summary>
    TelegramConfig Load();

    /// <summary>Schreibt die Config und überschreibt eine vorhandene.</summary>
    void Save(TelegramConfig config);
}

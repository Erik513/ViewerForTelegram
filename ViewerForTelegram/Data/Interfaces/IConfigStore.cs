using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data.Interfaces;

/// <summary>
/// Loads and saves the <see cref="TelegramConfig"/>. Encapsulates WHERE and HOW
/// it is stored (currently: a JSON file under %AppData%) - Logic and UI only
/// see Load/Save.
/// </summary>
public interface IConfigStore
{
    /// <summary>
    /// Reads the stored config. Returns <see cref="TelegramConfig.Empty"/> when
    /// nothing has been saved yet or the file is unreadable.
    /// </summary>
    TelegramConfig Load();

    /// <summary>Writes the config, overwriting an existing one.</summary>
    void Save(TelegramConfig config);
}

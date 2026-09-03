using System.Text.Json;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IConfigStore"/> as a single JSON file. The default path is
/// <see cref="AppPaths.ConfigFile"/>; the path overload is for tests.
/// </summary>
public sealed class JsonConfigStore : IConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonConfigStore()
        : this(AppPaths.ConfigFile)
    {
    }

    public JsonConfigStore(string path)
    {
        _path = path;
    }

    public TelegramConfig Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return TelegramConfig.Empty;
            }

            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<TelegramConfig>(json, Options)
                   ?? TelegramConfig.Empty;
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken/locked file must not block startup -
            // the app treats it like "not configured yet".
            return TelegramConfig.Empty;
        }
    }

    public void Save(TelegramConfig config)
    {
        string json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(_path, json);
    }
}

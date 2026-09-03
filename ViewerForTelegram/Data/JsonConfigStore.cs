using System.Text.Json;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="IConfigStore"/> als eine JSON-Datei. Standardpfad ist
/// <see cref="AppPaths.ConfigFile"/>; die Pfad-Überladung dient Tests.
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
            // Kaputte/gesperrte Datei soll den Start nicht verhindern -
            // die App behandelt es wie "noch nicht konfiguriert".
            return TelegramConfig.Empty;
        }
    }

    public void Save(TelegramConfig config)
    {
        string json = JsonSerializer.Serialize(config, Options);
        File.WriteAllText(_path, json);
    }
}

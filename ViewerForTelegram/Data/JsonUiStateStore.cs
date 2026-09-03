using System.Text.Json;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// Loads and saves <see cref="UiState"/> as a JSON file (default
/// <see cref="AppPaths.UiStateFile"/>). Any read/parse problem just yields
/// <see cref="UiState.Default"/> - this data is a convenience, never critical.
/// </summary>
public sealed class JsonUiStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonUiStateStore()
        : this(AppPaths.UiStateFile)
    {
    }

    public JsonUiStateStore(string path)
    {
        _path = path;
    }

    public UiState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return UiState.Default;
            }

            return JsonSerializer.Deserialize<UiState>(File.ReadAllText(_path), Options)
                   ?? UiState.Default;
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return UiState.Default;
        }
    }

    public void Save(UiState state)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, Options));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            // Not worth surfacing - it's just remembered UI state.
        }
    }
}

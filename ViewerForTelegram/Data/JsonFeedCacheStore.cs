using System.Text.Json;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// Loads and saves the last-shown <see cref="PersistedFeed"/> as JSON (default
/// <see cref="AppPaths.FeedCacheFile"/>). Any read/parse problem just yields
/// null - this is a restart-time convenience (skip re-fetching everything),
/// never critical; a normal fetch still happens either way.
/// </summary>
public sealed class JsonFeedCacheStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;

    public JsonFeedCacheStore()
        : this(AppPaths.FeedCacheFile)
    {
    }

    public JsonFeedCacheStore(string path)
    {
        _path = path;
    }

    public PersistedFeed? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PersistedFeed>(File.ReadAllText(_path), Options);
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(PersistedFeed feed)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(feed, Options));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            // Not worth surfacing - it's just a restart-time convenience.
        }
    }
}

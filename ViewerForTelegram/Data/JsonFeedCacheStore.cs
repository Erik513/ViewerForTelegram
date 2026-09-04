using System.Text.Json;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.Data;

/// <summary>
/// Loads and saves the last-shown <see cref="PersistedFeed"/> **per chat**, as
/// one JSON file (default <see cref="AppPaths.FeedCacheFile"/>) holding a
/// chat-id-keyed map. Any read/parse problem just yields null/does nothing -
/// this is a restart-time convenience (skip re-fetching everything), never
/// critical; a normal fetch still happens either way.
///
/// Each chat's audio list is capped at <see cref="MaxAudiosPerChat"/> on save,
/// so a handful of visited chats never turns this into an unbounded file.
/// </summary>
public sealed class JsonFeedCacheStore
{
    /// <summary>
    /// Matches the largest "Newest N" the UI offers - nothing beyond this is
    /// ever explicitly requested, so keeping more per chat would only grow the
    /// file without ever being useful.
    /// </summary>
    public const int MaxAudiosPerChat = 5000;

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

    /// <summary>The persisted list for <paramref name="chatId"/>, or null if none.</summary>
    public PersistedFeed? Load(long chatId) =>
        LoadAll().TryGetValue(chatId, out PersistedFeed? feed) ? feed : null;

    /// <summary>Replaces the persisted list for <see cref="PersistedFeed.ChatId"/>.</summary>
    public void Save(PersistedFeed feed)
    {
        Dictionary<long, PersistedFeed> all = LoadAll();
        all[feed.ChatId] = feed.Audios.Count > MaxAudiosPerChat
            ? feed with { Audios = feed.Audios.Take(MaxAudiosPerChat).ToList() }
            : feed;

        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(all, Options));
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            // Not worth surfacing - it's just a restart-time convenience.
        }
    }

    private Dictionary<long, PersistedFeed> LoadAll()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new();
            }

            return JsonSerializer.Deserialize<Dictionary<long, PersistedFeed>>(File.ReadAllText(_path), Options)
                   ?? new();
        }
        catch (Exception ex) when (
            ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }
}

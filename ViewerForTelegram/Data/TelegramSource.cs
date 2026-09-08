using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using TL;
// TL.Message collides with System.Windows.Forms.Message (pulled in project-wide
// via UseWindowsForms). Here we always mean the Telegram message.
using Message = TL.Message;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="ITelegramSource"/> on top of WTelegramClient (MTProto, signs in
/// as the user). The only class in the project that knows the <c>TL</c> /
/// <c>WTelegram</c> namespace at all.
/// </summary>
public sealed class TelegramSource : ITelegramSource
{
    private readonly IConfigStore _configStore;
    private readonly string _sessionPath;

    // Replaced during self-healing (phone-number correction).
    private TelegramConfig _config;

    private WTelegram.Client? _client;
    private Func<Task<string>>? _requestCode;

    // From GetChatsAsync: chatId -> addressable Telegram peer. Messages_Search
    // needs an InputPeer, not just the id.
    private Dictionary<long, InputPeer>? _peers;

    // From GetAudioMessagesSinceAsync: FileId -> the real Telegram document.
    // DownloadFileAsync needs the whole Document (access_hash, file_reference,
    // dc_id), not just our FileId.
    private readonly Dictionary<long, Document> _documents = new();

    public TelegramSource(IConfigStore configStore, string sessionPath)
    {
        _configStore = configStore;
        _config = TelegramConfig.Empty;
        _sessionPath = sessionPath;
    }

    public async Task ConnectAsync(
        Func<Task<string>> requestVerificationCode,
        CancellationToken ct)
    {
        _requestCode = requestVerificationCode;

        // Read the config fresh from the store - so a change just saved in the
        // settings form takes effect immediately, without rebuilding this class.
        _config = _configStore.Load();
        if (!_config.IsComplete)
        {
            throw new InvalidOperationException(
                "Credentials are missing - please complete the setup first.");
        }

        // Capture WTelegramClient messages from level "Info" (2) upwards - level
        // 0/1 is packet-by-packet noise.
        WTelegram.Helpers.Log = (level, message) =>
        {
            if (level >= 2)
            {
                AppLog.Line($"WTelegram/{level}", message);
            }
        };

        LogLine(File.Exists(_sessionPath)
            ? $"Session file present: {_sessionPath} ({new FileInfo(_sessionPath).Length} bytes)"
            : $"No session file at {_sessionPath} - full login required.");

        // A retry after a failed attempt calls this again - drop the old client
        // first so we don't leak it or run two on the same session.
        if (_client is not null)
        {
            try { _client.Dispose(); } catch { /* already broken */ }
            _client = null;
        }

        _client = new WTelegram.Client(ProvideConfigValue);
        User me = await _client.LoginUserIfNeeded();

        LogLine($"Signed in as {me.first_name} (id {me.id}). " +
                $"Session now: {(File.Exists(_sessionPath) ? new FileInfo(_sessionPath).Length + " bytes" : "MISSING")}");

        HealPhoneNumber(me);
    }

    /// <summary>
    /// Self-healing: Telegram reports the account's canonical number. If the
    /// stored one differs (e.g. an extra leading 0), WTelegramClient would
    /// discard the session as "foreign" on EVERY start and log in again. We
    /// write the correct number back into the config once.
    /// </summary>
    private void HealPhoneNumber(User me)
    {
        string reported = me.phone ?? "";
        if (reported.Length == 0)
        {
            return;
        }

        if (PhoneNumbers.DigitsOnly(_config.PhoneNumber) == reported)
        {
            return; // already matches
        }

        string corrected = "+" + reported;
        LogLine($"Phone number corrected: '{_config.PhoneNumber}' -> '{corrected}'");
        _config = _config with { PhoneNumber = corrected };
        _configStore.Save(_config);
    }

    /// <summary>
    /// Called by WTelegramClient field by field to assemble the login. A
    /// <c>null</c> means "no value / use the default".
    /// </summary>
    private string? ProvideConfigValue(string key) => key switch
    {
        "api_id" => _config.ApiId.ToString(),
        "api_hash" => _config.ApiHash,
        "phone_number" => _config.PhoneNumber,
        "session_pathname" => _sessionPath,

        // The code is only known at runtime. This method is synchronous, so we
        // block here waiting for the (asynchronous) UI dialog. Harmless because
        // this call runs on a worker thread.
        "verification_code" => _requestCode!().GetAwaiter().GetResult(),

        "password" => throw new TwoFactorAuthNotSupportedException(),

        _ => null
    };

    public async Task<IReadOnlyList<TelegramChat>> GetChatsAsync(CancellationToken ct)
    {
        EnsureConnected();

        Messages_Dialogs dialogs = await _client!.Messages_GetAllDialogs();

        var result = new List<TelegramChat>();
        _peers = new Dictionary<long, InputPeer>();

        foreach (ChatBase chat in dialogs.chats.Values)
        {
            if (!chat.IsActive)
            {
                continue; // skip left / banned chats
            }

            TelegramChatKind kind = chat.IsChannel
                ? TelegramChatKind.Channel
                : TelegramChatKind.Group;

            result.Add(new TelegramChat(chat.ID, chat.Title ?? "", kind));
            _peers[chat.ID] = chat.ToInputPeer();
        }

        return result;
    }

    private const int HistoryPageSize = 100;
    private const int MaxPages = 50;       // date-window mode
    private const int MaxMessages = 15000; // hard message ceiling ("newest N" mode: ~150 pages)

    public async Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId, DateTime sinceUtc, CancellationToken ct, int maxAudios = int.MaxValue,
        IProgress<int>? progress = null, int beforeMessageId = 0,
        IProgress<IReadOnlyList<AudioMessage>>? onBatch = null)
    {
        EnsureConnected();

        InputPeer peer = GetPeer(chatId);

        DateTime since = sinceUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)
            : sinceUtc.ToUniversalTime();

        int cap = maxAudios <= 0 ? 0 : Math.Min(maxAudios, MaxMessages);
        // Count mode ("newest N") keeps paging until it has N audios, history
        // runs out, or the hard ceiling is hit. Since Messages_Search already
        // filters server-side, "ceiling" here is effectively audio messages
        // scanned, not general messages - MaxMessages comfortably covers the
        // largest "Newest N" the UI offers (5000) even for an audio-sparse
        // group. The date-window mode stops much sooner (the date check bails
        // first).
        int maxPages = maxAudios == int.MaxValue
            ? MaxPages
            : MaxMessages / HistoryPageSize;

        var result = new List<AudioMessage>();
        int offsetId = beforeMessageId; // 0 = from the newest message; else start just before this id

        for (int page = 0; page < maxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            // Server-side "music" filter (the same one Telegram's own clients
            // use for a chat's shared-media "Audio" tab) instead of paging
            // through every message and discarding the non-audio ones - a
            // group that is mostly text used to need many pages of
            // Messages_GetHistory to reach even a few hundred tracks; each
            // page here is (close to) all audio, so the same page budget
            // reaches much further and needs far fewer requests overall
            // (less FLOOD_WAIT risk too).
            Messages_MessagesBase batch = await _client!.Messages_Search(
                peer, q: "", filter: new InputMessagesFilterMusic(),
                offset_id: offsetId, limit: HistoryPageSize);

            MessageBase[] messages = batch.Messages;
            if (messages.Length == 0)
            {
                break; // nothing left
            }

            int pageStart = result.Count;
            bool reachedOlder = false;
            bool reachedCap = false;
            foreach (MessageBase mb in messages)
            {
                // Messages come newest -> oldest. Once one is older than
                // 'since', all following ones are too.
                if (mb.Date < since)
                {
                    reachedOlder = true;
                    break;
                }

                if (mb is Message m && TryMapAudio(m, chatId, out AudioMessage audio))
                {
                    result.Add(audio);
                    if (result.Count >= cap)
                    {
                        reachedCap = true;
                        break;
                    }
                }
            }

            offsetId = messages[^1].ID; // oldest id of this page -> next page older
            progress?.Report(result.Count);
            if (result.Count > pageStart)
            {
                onBatch?.Report(result.GetRange(pageStart, result.Count - pageStart));
            }
            if (reachedOlder || reachedCap || messages.Length < HistoryPageSize)
            {
                break;
            }
        }

        LogLine($"GetAudioMessagesSince: chat {chatId}, since {since:u}, cap {cap}, before {beforeMessageId} -> {result.Count} audios");
        return result;
    }

    /// <summary>
    /// Audio messages posted after <paramref name="afterMessageId"/> (newest
    /// first) - used to top up an existing list with what has been posted since.
    /// </summary>
    public async Task<IReadOnlyList<AudioMessage>> GetAudioMessagesAfterAsync(
        long chatId, int afterMessageId, CancellationToken ct)
    {
        EnsureConnected();

        InputPeer peer = GetPeer(chatId);

        var result = new List<AudioMessage>();
        int offsetId = 0;

        for (int page = 0; page < 30; page++)   // new posts since the last load are normally few
        {
            ct.ThrowIfCancellationRequested();

            // Plain history, NOT Messages_Search+InputMessagesFilterMusic:
            // the "music" filter only matches posts tagged as music and misses
            // tracks sent "as a file" (audio/* mime, no audio attribute) - which
            // TryMapAudio does accept. This window (new posts since last visit)
            // is small, so scanning a few unfiltered pages costs nothing.
            Messages_MessagesBase batch = await _client!.Messages_GetHistory(
                peer, offset_id: offsetId, min_id: afterMessageId, limit: HistoryPageSize);

            MessageBase[] messages = batch.Messages;
            if (messages.Length == 0)
            {
                break;
            }

            bool reachedKnown = false;
            foreach (MessageBase mb in messages)
            {
                if (mb.ID <= afterMessageId)
                {
                    reachedKnown = true;
                    break;
                }
                if (mb is Message m && TryMapAudio(m, chatId, out AudioMessage audio))
                {
                    result.Add(audio);
                }
            }

            offsetId = messages[^1].ID;
            if (reachedKnown || messages.Length < HistoryPageSize)
            {
                break;
            }
        }

        LogLine($"GetAudioMessagesAfter: chat {chatId}, after {afterMessageId} -> {result.Count} new audios");
        return result;
    }

    private const int GetMessagesBatchSize = 100;   // Telegram's own per-call ceiling for this method

    /// <summary>
    /// Re-queries the given message ids directly (in batches) and reports the
    /// ones that no longer come back - deleted while this app was not
    /// connected, so no update event was ever received for them.
    /// </summary>
    public async Task<IReadOnlyList<int>> FindDeletedMessagesAsync(
        long chatId, IReadOnlyList<int> messageIds, CancellationToken ct)
    {
        EnsureConnected();

        InputPeer peer = GetPeer(chatId);

        var deleted = new List<int>();

        for (int offset = 0; offset < messageIds.Count; offset += GetMessagesBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            int[] batchIds = messageIds.Skip(offset).Take(GetMessagesBatchSize).ToArray();
            InputMessage[] inputs = Array.ConvertAll(batchIds, id => (InputMessage)new InputMessageID { id = id });

            Messages_MessagesBase result = await _client!.GetMessages(peer, inputs);

            var found = new HashSet<int>();
            foreach (MessageBase mb in result.Messages)
            {
                if (mb is not MessageEmpty)
                {
                    found.Add(mb.ID);
                }
            }

            foreach (int id in batchIds)
            {
                if (!found.Contains(id))
                {
                    deleted.Add(id);
                }
            }
        }

        if (deleted.Count > 0)
        {
            LogLine($"FindDeletedMessages: chat {chatId}, checked {messageIds.Count} -> {deleted.Count} gone");
        }
        return deleted;
    }

    /// <summary>
    /// Turns a message into an <see cref="AudioMessage"/> if it contains an
    /// audio file at all (as "music" with an audio attribute, or as a "file"
    /// with an audio/* MIME type). Voice messages are deliberately included -
    /// filtering happens later in the app settings.
    /// </summary>
    private bool TryMapAudio(Message message, long chatId, out AudioMessage audio)
    {
        audio = default!;

        if (message.media is not MessageMediaDocument { document: Document doc })
        {
            return false;
        }

        var audioAttr = doc.attributes.OfType<DocumentAttributeAudio>().FirstOrDefault();
        bool looksLikeAudioFile =
            doc.mime_type?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true;

        if (audioAttr is null && !looksLikeAudioFile)
        {
            return false;
        }

        string? fileName = doc.attributes
            .OfType<DocumentAttributeFilename>()
            .FirstOrDefault()?.file_name;

        string title =
            !string.IsNullOrWhiteSpace(audioAttr?.title) ? audioAttr!.title
            : !string.IsNullOrWhiteSpace(fileName) ? Path.GetFileNameWithoutExtension(fileName)
            : "(untitled)";

        audio = new AudioMessage(
            ChatId: chatId,
            MessageId: message.id,
            FileId: doc.id,
            Title: title,
            Performer: audioAttr?.performer ?? "",
            Duration: audioAttr is not null
                ? TimeSpan.FromSeconds(audioAttr.duration)
                : null,
            SizeBytes: doc.size,
            FileName: fileName ?? $"{doc.id}{MimeToExtension(doc.mime_type)}",
            DateUtc: message.date);

        _documents[doc.id] = doc; // keep for the later download
        return true;
    }

    /// <summary>
    /// Fetches a single message by id and runs it through <see cref="TryMapAudio"/>
    /// so its <c>Document</c> lands in <see cref="_documents"/> for a download.
    /// Best-effort - a failure just leaves the caller to report "not known".
    /// </summary>
    private async Task TryFetchDocumentAsync(long chatId, int messageId, CancellationToken ct)
    {
        if (_peers is null || !_peers.TryGetValue(chatId, out InputPeer? peer))
        {
            return;
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            Messages_MessagesBase result = await _client!.GetMessages(
                peer, new InputMessage[] { new InputMessageID { id = messageId } });

            foreach (MessageBase mb in result.Messages)
            {
                if (mb is Message m)
                {
                    TryMapAudio(m, chatId, out _);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLine($"TryFetchDocument: message {messageId} in chat {chatId} failed: {ex.Message}");
        }
    }

    // Only used when the document has no filename of its own - give the file a
    // real extension so the player (and Media Foundation) can route it.
    private static string MimeToExtension(string? mime) => mime?.ToLowerInvariant() switch
    {
        "audio/mpeg" or "audio/mp3" or "audio/x-mp3" => ".mp3",
        "audio/mp4" or "audio/x-m4a" or "audio/m4a"
            or "audio/aac" or "audio/x-aac" or "audio/aacp" => ".m4a",
        "audio/opus" or "audio/x-opus+ogg" => ".opus",
        "audio/ogg" or "application/ogg" or "audio/vorbis" or "audio/x-vorbis+ogg" => ".ogg",
        "audio/flac" or "audio/x-flac" => ".flac",
        "audio/wav" or "audio/x-wav" or "audio/wave" or "audio/vnd.wave" => ".wav",
        "audio/aiff" or "audio/x-aiff" => ".aiff",
        "audio/x-ms-wma" or "audio/wma" => ".wma",
        _ => ".bin"
    };

    public async Task DownloadAsync(
        AudioMessage message, string targetPath, IProgress<int>? progress, CancellationToken ct)
    {
        EnsureConnected();
        ct.ThrowIfCancellationRequested();

        if (!_documents.TryGetValue(message.FileId, out Document? doc))
        {
            // The list can come straight from the on-disk cache (a previous
            // session), so a track's Document was never re-fetched this run.
            // Pull just this one message to get it.
            await TryFetchDocumentAsync(message.ChatId, message.MessageId, ct);

            if (!_documents.TryGetValue(message.FileId, out doc))
            {
                throw new InvalidOperationException(
                    "File not known - load the list first.");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string partPath = targetPath + ".part";

        // Some documents report total = 0 in the callback - then take the known
        // size from the message, otherwise no progress would ever arrive.
        // NOTE: the callback runs on WTelegramClient's own worker threads, so it
        // must NOT throw (an OperationCanceledException there escapes the awaited
        // task and crashes). Cancellation is done purely by disposing the
        // FileStream below.
        long knownTotal = message.SizeBytes;
        WTelegram.Client.ProgressCallback? cb = (transmitted, total) =>
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }
            long t = total > 0 ? total : knownTotal;
            if (t > 0)
            {
                progress?.Report((int)Math.Min(100, transmitted * 100 / t));
            }
        };

        try
        {
            await using FileStream fs = new(
                partPath, FileMode.Create, FileAccess.Write, FileShare.None);
            // Cancel: close the FileStream -> the next write in DownloadFileAsync
            // throws -> the download ends.
            await using CancellationTokenRegistration reg =
                ct.Register(() => { try { fs.Dispose(); } catch { } });

            await _client!.DownloadFileAsync(doc, fs, (PhotoSizeBase?)null, cb);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            IoUtil.TryDelete(partPath);
            throw new OperationCanceledException(ct);
        }
        catch
        {
            IoUtil.TryDelete(partPath);
            throw;
        }

        ct.ThrowIfCancellationRequested();   // safe here - we are back on our own await path

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        File.Move(partPath, targetPath);

        LogLine($"Download complete: {Path.GetFileName(targetPath)} " +
                $"({new FileInfo(targetPath).Length} bytes)");
    }


    public async ValueTask DisposeAsync()
    {
        WTelegram.Client? client = _client;
        _client = null;
        if (client is null)
        {
            return;
        }

        // WTelegramClient.Dispose() is synchronous and can hang while tearing
        // down the connection - offload it to a background thread and give up
        // after 3 s.
        try
        {
            await Task.Run(client.Dispose).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // hangs or throws - does not matter on shutdown
        }
    }

    private static void LogLine(string message) => AppLog.Line("Telegram", message);

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} must run successfully first.");
        }
    }

    private InputPeer GetPeer(long chatId)
    {
        if (_peers is null || !_peers.TryGetValue(chatId, out InputPeer? peer))
        {
            throw new InvalidOperationException(
                "Chat not known - GetChatsAsync must have run first.");
        }
        return peer;
    }
}

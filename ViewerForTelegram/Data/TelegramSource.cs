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

    // From GetChatsAsync: chatId -> addressable Telegram peer. Messages_GetHistory
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

        "password" => throw new NotSupportedException(
            "This account uses a cloud password (2FA). Support for that will be added later."),

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
    private const int MaxPages = 50;
    private const int MaxMessages = 5000;

    public async Task<IReadOnlyList<AudioMessage>> GetAudioMessagesSinceAsync(
        long chatId, DateTime sinceUtc, CancellationToken ct)
    {
        EnsureConnected();

        if (_peers is null || !_peers.TryGetValue(chatId, out InputPeer? peer))
        {
            throw new InvalidOperationException(
                "Chat not known - GetChatsAsync must have run first.");
        }

        DateTime since = sinceUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)
            : sinceUtc.ToUniversalTime();

        var result = new List<AudioMessage>();
        int offsetId = 0; // 0 = from the newest message

        for (int page = 0; page < MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            Messages_MessagesBase batch = await _client!.Messages_GetHistory(
                peer, offset_id: offsetId, limit: HistoryPageSize);

            MessageBase[] messages = batch.Messages;
            if (messages.Length == 0)
            {
                break; // nothing left
            }

            bool reachedOlder = false;
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
                }
            }

            offsetId = messages[^1].ID; // oldest id of this page -> next page older
            if (reachedOlder || result.Count >= MaxMessages || messages.Length < HistoryPageSize)
            {
                break;
            }
        }

        LogLine($"GetAudioMessagesSince: chat {chatId}, since {since:u} -> {result.Count} audios");
        return result;
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

    private static string MimeToExtension(string? mime) => mime?.ToLowerInvariant() switch
    {
        "audio/mpeg" => ".mp3",
        "audio/mp4" or "audio/x-m4a" => ".m4a",
        "audio/ogg" => ".ogg",
        "audio/flac" or "audio/x-flac" => ".flac",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => ".bin"
    };

    public async Task DownloadAsync(
        AudioMessage message, string targetPath, IProgress<int>? progress, CancellationToken ct)
    {
        EnsureConnected();
        ct.ThrowIfCancellationRequested();

        if (!_documents.TryGetValue(message.FileId, out Document? doc))
        {
            throw new InvalidOperationException(
                "File not known - load the list first.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string partPath = targetPath + ".part";

        // Some documents report total = 0 in the callback - then take the known
        // size from the message, otherwise no progress would ever arrive.
        long knownTotal = message.SizeBytes;
        WTelegram.Client.ProgressCallback? cb = (transmitted, total) =>
        {
            ct.ThrowIfCancellationRequested();
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
            TryDelete(partPath);
            throw new OperationCanceledException(ct);
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }
        File.Move(partPath, targetPath);

        LogLine($"Download complete: {Path.GetFileName(targetPath)} " +
                $"({new FileInfo(targetPath).Length} bytes)");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // never mind
        }
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
}

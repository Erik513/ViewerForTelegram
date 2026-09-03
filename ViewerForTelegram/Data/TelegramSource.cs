using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using TL;
// TL.Message kollidiert mit System.Windows.Forms.Message (kommt projektweit
// über UseWindowsForms rein). Hier meinen wir immer die Telegram-Nachricht.
using Message = TL.Message;

namespace ViewerForTelegram.Data;

/// <summary>
/// <see cref="ITelegramSource"/> auf Basis von WTelegramClient (MTProto,
/// meldet sich als der Benutzer an). Die einzige Klasse im Projekt, die
/// den Namespace <c>TL</c> / <c>WTelegram</c> überhaupt kennt.
/// </summary>
public sealed class TelegramSource : ITelegramSource
{
    private readonly IConfigStore _configStore;
    private readonly string _sessionPath;

    // Wird bei der Selbstheilung (Telefonnummer-Korrektur) ersetzt.
    private TelegramConfig _config;

    private WTelegram.Client? _client;
    private Func<Task<string>>? _requestCode;

    // Aus GetChatsAsync: chatId -> ansprechbarer Telegram-Peer. Messages_GetHistory
    // braucht einen InputPeer, nicht nur die Id.
    private Dictionary<long, InputPeer>? _peers;

    // Aus GetAudioMessagesSinceAsync: FileId -> das echte Telegram-Dokument.
    // DownloadFileAsync braucht das ganze Document (access_hash, file_reference,
    // dc_id), nicht nur unsere FileId.
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

        // Config frisch aus dem Store lesen - so wirkt eine gerade im
        // SetupForm gespeicherte Änderung sofort, ohne die Klasse neu zu bauen.
        _config = _configStore.Load();
        if (!_config.IsComplete)
        {
            throw new InvalidOperationException(
                "Zugangsdaten fehlen - bitte erst die Einrichtung ausfüllen.");
        }

        // WTelegramClient-Meldungen ab Level "Info" (2) mitschreiben - Level 0/1
        // ist Paket-für-Paket-Rauschen.
        WTelegram.Helpers.Log = (level, message) =>
        {
            if (level >= 2)
            {
                AppLog.Line($"WTelegram/{level}", message);
            }
        };

        LogLine(File.Exists(_sessionPath)
            ? $"Session-Datei vorhanden: {_sessionPath} ({new FileInfo(_sessionPath).Length} Bytes)"
            : $"Keine Session-Datei unter {_sessionPath} - voller Login nötig.");

        _client = new WTelegram.Client(ProvideConfigValue);
        User me = await _client.LoginUserIfNeeded();

        LogLine($"Angemeldet als {me.first_name} (id {me.id}). " +
                $"Session jetzt: {(File.Exists(_sessionPath) ? new FileInfo(_sessionPath).Length + " Bytes" : "FEHLT")}");

        HealPhoneNumber(me);
    }

    /// <summary>
    /// Selbstheilung: Telegram meldet die kanonische Nummer des Kontos. Weicht
    /// die gespeicherte davon ab (z. B. eine 0 zu viel), würde WTelegramClient
    /// die Session bei JEDEM Start als "fremd" verwerfen und neu einloggen.
    /// Wir schreiben die korrekte Nummer einmalig zurück in die Config.
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
            return; // passt bereits
        }

        string corrected = "+" + reported;
        LogLine($"Telefonnummer korrigiert: '{_config.PhoneNumber}' -> '{corrected}'");
        _config = _config with { PhoneNumber = corrected };
        _configStore.Save(_config);
    }

    /// <summary>
    /// Wird von WTelegramClient Feld für Feld aufgerufen, um den Login
    /// zusammenzusetzen. Ein <c>null</c> bedeutet "kein Wert / nimm den
    /// Standard".
    /// </summary>
    private string? ProvideConfigValue(string key) => key switch
    {
        "api_id" => _config.ApiId.ToString(),
        "api_hash" => _config.ApiHash,
        "phone_number" => _config.PhoneNumber,
        "session_pathname" => _sessionPath,

        // Der Code ist erst zur Laufzeit bekannt. Diese Methode ist synchron,
        // also warten wir hier blockierend auf den (asynchronen) UI-Dialog.
        // Unkritisch, weil dieser Aufruf auf einem Worker-Thread läuft.
        "verification_code" => _requestCode!().GetAwaiter().GetResult(),

        "password" => throw new NotSupportedException(
            "Dieses Konto nutzt ein Cloud-Passwort (2FA). Das wird später ergänzt."),

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
                continue; // verlassene / gesperrte Chats überspringen
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
                "Chat nicht bekannt - GetChatsAsync muss zuerst gelaufen sein.");
        }

        DateTime since = sinceUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(sinceUtc, DateTimeKind.Utc)
            : sinceUtc.ToUniversalTime();

        var result = new List<AudioMessage>();
        int offsetId = 0; // 0 = ab der neuesten Nachricht

        for (int page = 0; page < MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();

            Messages_MessagesBase batch = await _client!.Messages_GetHistory(
                peer, offset_id: offsetId, limit: HistoryPageSize);

            MessageBase[] messages = batch.Messages;
            if (messages.Length == 0)
            {
                break; // nichts mehr da
            }

            bool reachedOlder = false;
            foreach (MessageBase mb in messages)
            {
                // Nachrichten kommen neueste -> älteste. Sobald eine älter als
                // 'since' ist, sind alle folgenden es auch.
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

            offsetId = messages[^1].ID; // älteste Id dieser Seite -> nächste Seite älter
            if (reachedOlder || result.Count >= MaxMessages || messages.Length < HistoryPageSize)
            {
                break;
            }
        }

        LogLine($"GetAudioMessagesSince: chat {chatId}, ab {since:u} -> {result.Count} Audios");
        return result;
    }

    /// <summary>
    /// Wandelt eine Nachricht in ein <see cref="AudioMessage"/> um, sofern sie
    /// überhaupt eine Audiodatei enthält (als "Musik" mit Audio-Attribut oder
    /// als "Datei" mit audio/*-MIME-Typ). Sprachnachrichten sind bewusst dabei -
    /// gefiltert wird später in den App-Einstellungen.
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
            : "(ohne Titel)";

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

        _documents[doc.id] = doc; // für den späteren Download aufheben
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
                "Datei nicht bekannt - erst die Liste laden.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string partPath = targetPath + ".part";

        // Manche Dokumente melden im Callback total = 0 - dann die bekannte
        // Größe aus der Nachricht nehmen, sonst käme nie ein Fortschritt.
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
            // Abbruch: FileStream schließen -> nächster Schreibversuch in
            // DownloadFileAsync wirft -> der Download endet.
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

        LogLine($"Download fertig: {Path.GetFileName(targetPath)} " +
                $"({new FileInfo(targetPath).Length} Bytes)");
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
            // egal
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

        // WTelegramClient.Dispose() ist synchron und kann beim Verbindungsabbau
        // hängen - auf einen Hintergrund-Thread auslagern und nach 3 s aufgeben.
        try
        {
            await Task.Run(client.Dispose).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // hängt oder wirft - beim Runterfahren egal
        }
    }

    private static void LogLine(string message) => AppLog.Line("Telegram", message);

    private void EnsureConnected()
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                $"{nameof(ConnectAsync)} muss zuerst erfolgreich durchlaufen.");
        }
    }
}

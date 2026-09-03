using System.Diagnostics;
using System.Text.Json;
using ErikwnkWFUI.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic.Services;
using StyledMessageBox = ErikwnkWFUI.Forms.MessageBox;
using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Hauptfenster: besteht nur aus der eingebetteten WebView2. Die komplette
/// Bedienung (Gruppenwahl, Zeitraum, Liste, Player) liegt in der HTML-Seite
/// unter web\. C# liefert Daten und die Audiodateien. Anmeldung und Optionen
/// laufen über die <see cref="SettingsForm"/>.
/// </summary>
public sealed class MainForm : StyledForm
{
    private const string WebHost = "app.viewerfortelegram";
    private const string CacheHost = "cache.viewerfortelegram";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITelegramSource _telegram;
    private readonly IConfigStore _configStore;
    private readonly IMediaCache _cache;
    private readonly AudioFeedService _feed;
    private readonly MediaDownloader _downloader;
    private readonly WebView2 _web;

    private readonly TaskCompletionSource _webReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private List<TelegramChat> _chats = new();
    private readonly Dictionary<long, AudioMessage> _loadedAudios = new();
    private bool _started;
    private bool _connecting;
    private bool _connected;

    public MainForm(
        ITelegramSource telegram,
        IConfigStore configStore,
        IMediaCache cache,
        AudioFeedService feed,
        MediaDownloader downloader)
        : base(StyledFormOptions.CreateStandard("Viewer for Telegram"))
    {
        _telegram = telegram;
        _configStore = configStore;
        _cache = cache;
        _feed = feed;
        _downloader = downloader;

        MinimumSize = new Size(720, 480);
        Size = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;

        _web = new WebView2 { Dock = DockStyle.Fill };
        ContentPanel.Controls.Add(_web);

        Shown += async (_, _) =>
        {
            if (_started)
            {
                return;
            }
            _started = true;
            await StartAsync();
        };
    }

    private async Task StartAsync()
    {
        if (!WebView2Available())
        {
            DialogResult r = StyledMessageBox.Show(
                "Die WebView2-Runtime von Microsoft ist nicht installiert.\r\n" +
                "Viewer for Telegram braucht sie für die Oberfläche.\r\n\r\n" +
                "Jetzt die Download-Seite öffnen?",
                "WebView2 fehlt", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this);

            if (r == DialogResult.Yes)
            {
                OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
            }
            Close();
            return;
        }

        try
        {
            await InitWebViewAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return;
        }

        _ = PushCacheInfoAsync();

        // Nicht angemeldet (keine Zugangsdaten oder keine gespeicherte Sitzung)
        // -> Einstellungen direkt öffnen. Sonst still über die Sitzung anmelden.
        bool hasSession = File.Exists(AppPaths.SessionFile);
        if (!_configStore.Load().IsComplete || !hasSession)
        {
            await OpenSettingsAsync(isStartup: true);
        }
        else
        {
            await ConnectAsync();
        }
    }

    /// <summary>Anmelden + Chatliste holen.</summary>
    private async Task ConnectAsync()
    {
        if (_connecting)
        {
            return;
        }
        _connecting = true;
        try
        {
            await ConnectAndListChatsAsync();
            _connected = true;
            await RunScriptAsync("window.tv.setConnected(true)");
        }
        catch (Exception ex)
        {
            _connected = false;
            await RunScriptAsync("window.tv.setConnected(false)");
            await SetStatusAsync("Anmeldung fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _connecting = false;
        }
    }

    // ---------- WebView2 ----------
    private static bool WebView2Available()
    {
        try
        {
            return !string.IsNullOrEmpty(
                CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch
        {
            return false;
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // egal - der Nutzer kann die Adresse auch abtippen
        }
    }

    private async Task InitWebViewAsync()
    {
        CoreWebView2Environment env =
            await CoreWebView2Environment.CreateAsync(null, AppPaths.WebView2Dir);
        await _web.EnsureCoreWebView2Async(env);

        _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            WebHost,
            Path.Combine(AppContext.BaseDirectory, "web"),
            CoreWebView2HostResourceAccessKind.Allow);

        _web.CoreWebView2.AddWebResourceRequestedFilter(
            $"https://{CacheHost}/*", CoreWebView2WebResourceContext.All);
        _web.CoreWebView2.WebResourceRequested += OnCacheResourceRequested;

        _web.CoreWebView2.Settings.AreDevToolsEnabled = AppLog.Verbose;
        _web.CoreWebView2.WebMessageReceived += OnWebMessage;
        _web.CoreWebView2.DownloadStarting += OnDownloadStarting;

        _web.CoreWebView2.NavigationCompleted += (_, _) => _webReady.TrySetResult();
        _web.CoreWebView2.Navigate($"https://{WebHost}/index.html");
    }

    private async Task RunScriptAsync(string js)
    {
        try
        {
            await _webReady.Task;
            if (_web.CoreWebView2 is not null)
            {
                await _web.CoreWebView2.ExecuteScriptAsync(js);
            }
        }
        catch
        {
            // Skript-Aufrufe dürfen nie den Ablauf abbrechen (z. B. beim Schließen).
        }
    }

    private Task SetStatusAsync(string text) =>
        RunScriptAsync($"window.tv.setStatus({JsStr(text)})");

    // ---------- Anmeldung ----------
    private async Task ConnectAndListChatsAsync()
    {
        if (!_configStore.Load().IsComplete)
        {
            throw new InvalidOperationException("Zugangsdaten fehlen.");
        }

        await SetStatusAsync("Verbinde …");
        await _telegram.ConnectAsync(AskForCodeAsync, CancellationToken.None);

        _chats = (await _telegram.GetChatsAsync(CancellationToken.None))
            .OrderBy(c => c.Title)
            .ToList();

        var dtos = _chats.Select(c => new ChatDto(c.Id.ToString(), c.Title, c.Kind.ToString()));
        await RunScriptAsync($"window.tv.setChats({JsonSerializer.Serialize(dtos, JsonOpts)})");
        await SetStatusAsync($"Angemeldet – {_chats.Count} Gruppen/Kanäle.");
    }

    private Task<string> AskForCodeAsync()
    {
        try
        {
            string code = Invoke(() =>
            {
                using var form = new CodeInputForm();
                if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(form.Code))
                {
                    return form.Code!;
                }

                throw new OperationCanceledException("Kein Login-Code eingegeben.");
            });

            return Task.FromResult(code);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Fenster wurde geschlossen, bevor der Code eingegeben wurde.
            throw new OperationCanceledException("Anmeldung abgebrochen.");
        }
    }

    // ---------- JS -> C# ----------
    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string messageJson = e.WebMessageAsJson;

        // Raus aus dem WebView2-Callback-Stack, BEVOR wir modale Dialoge öffnen
        // (ShowDialog aus einem WebView2-Handler heraus kann WebView2 abschießen).
        await Task.Yield();

        long chatId;
        int days;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(messageJson);
            JsonElement root = doc.RootElement;
            string type = root.GetProperty("type").GetString() ?? "";

            if (type == "settings")
            {
                await OpenSettingsAsync(isStartup: false);
                return;
            }

            if (type == "cancel")
            {
                if (long.TryParse(root.GetProperty("fileId").GetString(), out long cid))
                {
                    _downloader.Cancel(cid);
                }
                return;
            }

            if (type != "load"
                || !long.TryParse(root.GetProperty("chatId").GetString(), out chatId))
            {
                return;
            }

            days = root.GetProperty("days").GetInt32();
        }
        catch
        {
            return;
        }

        if (_chats.All(c => c.Id != chatId))
        {
            return;
        }

        try
        {
            IReadOnlyList<FeedItem> items =
                await _feed.LoadAsync(chatId, days, CancellationToken.None);

            _loadedAudios.Clear();
            foreach (FeedItem item in items)
            {
                _loadedAudios[item.Audio.FileId] = item.Audio;
            }

            await PushSongsAsync(items);
        }
        catch (Exception ex)
        {
            await SetStatusAsync("Laden fehlgeschlagen: " + ex.Message);
        }
    }

    private Task PushSongsAsync(IReadOnlyList<FeedItem> items)
    {
        var dtos = items.Select(i => i.Audio).Select(a => new SongDto(
            a.FileId.ToString(),
            a.DateUtc.ToString("yyyy-MM-dd"),
            a.DateUtc.ToLocalTime().ToString("dd.MM.yyyy"),
            a.Performer,
            a.Title,
            a.FileName,
            a.Duration?.TotalSeconds,
            $"{a.SizeBytes / 1024d / 1024d:0.0} MB"));

        string json = JsonSerializer.Serialize(dtos, JsonOpts);
        return RunScriptAsync($"window.tv.setSongs({json})");
    }

    // ---------- Audio ausliefern ----------
    private const int MaxChunk = 16 * 1024 * 1024; // je Antwort höchstens 16 MB in den Speicher

    private async void OnCacheResourceRequested(
        object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        CoreWebView2Deferral? deferral = null;
        try
        {
            deferral = e.GetDeferral();
            try
            {
                e.Response = await BuildAudioResponseAsync(e.Request);
            }
            catch (OperationCanceledException)
            {
                e.Response = SafeErrorResponse(499, "Cancelled");
            }
            catch (Exception ex)
            {
                Trace("FEHLER beim Ausliefern: " + ex);
                e.Response = SafeErrorResponse(500, "Error");
                _ = SetStatusAsync("Wiedergabe fehlgeschlagen: " + ex.Message);
            }
        }
        catch
        {
            // nichts darf hier hochblubbern - sonst Absturz
        }
        finally
        {
            try { deferral?.Complete(); } catch { }
        }
    }

    private CoreWebView2WebResourceResponse? SafeErrorResponse(int code, string reason)
    {
        try
        {
            return _web.CoreWebView2?.Environment
                .CreateWebResourceResponse(null, code, reason, "");
        }
        catch
        {
            return null;
        }
    }

    private async Task<CoreWebView2WebResourceResponse> BuildAudioResponseAsync(
        CoreWebView2WebResourceRequest request)
    {
        string idText = new Uri(request.Uri).AbsolutePath.Trim('/');
        string rangeHdr = request.Headers.Contains("Range")
            ? request.Headers.GetHeader("Range") : "-";

        if (!long.TryParse(idText, out long id)
            || !_loadedAudios.TryGetValue(id, out AudioMessage? audio))
        {
            Trace($"Anfrage {idText}: unbekannt (404). geladen={_loadedAudios.Count}");
            return _web.CoreWebView2.Environment
                .CreateWebResourceResponse(null, 404, "Not Found", "");
        }

        Trace($"Anfrage {id} \"{audio.FileName}\" Range={rangeHdr} " +
              $"cached={_cache.Contains(audio)}");

        string path = await EnsureDownloadedAsync(audio);

        long total = new FileInfo(path).Length;
        long start = 0;
        long end = total - 1;
        bool ranged = false;

        string? range = request.Headers.Contains("Range")
            ? request.Headers.GetHeader("Range")
            : null;
        if (range?.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) == true)
        {
            string[] parts = range["bytes=".Length..].Split('-');
            if (long.TryParse(parts[0], out long s))
            {
                start = Math.Clamp(s, 0, Math.Max(0, total - 1));
            }
            if (parts.Length > 1 && long.TryParse(parts[1], out long en))
            {
                end = Math.Min(en, total - 1);
            }
            ranged = true;
        }

        // Nie mehr als MaxChunk am Stück ausliefern - der Browser holt den
        // Rest per Folge-Range. Hält den Speicher pro Anfrage begrenzt und
        // vermeidet hängende FileStreams.
        if (end - start + 1 > MaxChunk)
        {
            end = start + MaxChunk - 1;
            ranged = true;
        }

        int length = (int)(end - start + 1);
        byte[] buffer = new byte[length];
        await using (FileStream fs = new(
            path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            fs.Seek(start, SeekOrigin.Begin);
            await fs.ReadExactlyAsync(buffer);
        }

        string headers =
            $"Content-Type: {MimeFor(audio.FileName)}\r\n" +
            $"Content-Length: {length}\r\n" +
            "Accept-Ranges: bytes\r\n" +
            $"Content-Disposition: inline; filename*=UTF-8''{Uri.EscapeDataString(audio.FileName)}\r\n" +
            (ranged ? $"Content-Range: bytes {start}-{end}/{total}\r\n" : "");

        return _web.CoreWebView2.Environment.CreateWebResourceResponse(
            new MemoryStream(buffer, writable: false),
            ranged ? 206 : 200,
            ranged ? "Partial Content" : "OK",
            headers);
    }

    /// <summary>
    /// "Herunterladen" aus dem Player-Menue: Dateiname auf den Originalnamen
    /// setzen, und je nach Einstellung direkt in den Standardordner speichern
    /// oder den Speichern-Dialog zeigen.
    /// </summary>
    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        try
        {
            string idText = new Uri(e.DownloadOperation.Uri).AbsolutePath.Trim('/');
            string name = long.TryParse(idText, out long id)
                          && _loadedAudios.TryGetValue(id, out AudioMessage? a)
                ? a.FileName
                : "audio";
            name = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));

            TelegramConfig cfg = _configStore.Load();

            if (cfg.UseDownloadFolder
                && !string.IsNullOrWhiteSpace(cfg.DownloadFolder)
                && Directory.Exists(cfg.DownloadFolder))
            {
                e.ResultFilePath = Path.Combine(cfg.DownloadFolder, name);
                e.Handled = true; // ohne den Standard-Download-Balken

                CoreWebView2DownloadOperation op = e.DownloadOperation;
                op.StateChanged += (_, _) =>
                {
                    if (op.State == CoreWebView2DownloadState.Completed)
                    {
                        _ = SetStatusAsync($"Gespeichert: {Path.GetFileName(op.ResultFilePath)}");
                    }
                    else if (op.State == CoreWebView2DownloadState.Interrupted)
                    {
                        _ = SetStatusAsync($"Download fehlgeschlagen ({op.InterruptReason}).");
                    }
                };
            }
            else
            {
                // Speichern-Dialog von WebView2 - aber mit gutem Dateinamen.
                string start = string.IsNullOrWhiteSpace(cfg.DownloadFolder)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : cfg.DownloadFolder;
                e.ResultFilePath = Path.Combine(start, name);
            }
        }
        catch
        {
            // im Zweifel den Standard von WebView2 lassen
        }
    }

    private static string MimeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg",
            ".m4a" or ".mp4" or ".aac" => "audio/mp4",
            ".ogg" or ".opus" => "audio/ogg",
            ".flac" => "audio/flac",
            ".wav" => "audio/wav",
            _ => "application/octet-stream"
        };

    /// <summary>
    /// Sorgt dafür, dass die Datei im Cache liegt; gibt den Pfad zurück. Hier
    /// nur die WebView-Hülle (Statuszeile, Fortschrittsbalken, Cache-Anzeige) -
    /// das eigentliche Laden macht <see cref="MediaDownloader"/>.
    /// </summary>
    private async Task<string> EnsureDownloadedAsync(AudioMessage audio)
    {
        if (_cache.Contains(audio))
        {
            return _cache.GetPath(audio);
        }

        string fid = audio.FileId.ToString();
        var progress = new Progress<int>(p =>
            _ = RunScriptAsync($"window.tv.setProgress({JsStr(fid)}, {p})"));
        try
        {
            await SetStatusAsync($"Lade \"{audio.DisplayName}\" ...");
            Trace($"Download START {audio.FileId} ({audio.SizeBytes} Bytes)");
            string path = await _downloader.EnsureLocalAsync(audio, progress, CancellationToken.None);
            Trace($"Download OK {audio.FileId}");
            return path;
        }
        finally
        {
            _ = RunScriptAsync($"window.tv.clearProgress({JsStr(fid)})");
            _ = SetStatusAsync($"{_loadedAudios.Count} Audios");
            _ = PushCacheInfoAsync();
        }
    }

    private Task PushCacheInfoAsync()
    {
        (int count, long bytes) = _cache.GetStats();
        string size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
            : $"{bytes / 1024d / 1024d:0} MB";
        return RunScriptAsync($"window.tv.setCacheInfo({JsStr($"Cache: {size} ({count})")})");
    }

    /// <summary>
    /// Öffnet die <see cref="SettingsForm"/> und reagiert auf das, was der
    /// Nutzer darin ausgelöst hat (Anmelden / Abmelden / Zurücksetzen / nur
    /// speichern). Beim Programmstart wird bei vollständigen Daten automatisch
    /// angemeldet.
    /// </summary>
    private async Task OpenSettingsAsync(bool isStartup)
    {
        while (true)
        {
            TelegramConfig before = _configStore.Load();

            using var dlg = new SettingsForm(before, _cache, _connected);
            dlg.ShowDialog(this);
            await PushCacheInfoAsync();

            if (dlg.Action == SettingsAction.Wipe)
            {
                await LogoutAsync(wipeConfig: true);
                return;
            }

            TelegramConfig after = dlg.Result;
            _configStore.Save(after);

            if (dlg.Action == SettingsAction.Logout)
            {
                await LogoutAsync(wipeConfig: false);
                return;
            }

            bool credsChanged =
                after.ApiId != before.ApiId
                || after.ApiHash != before.ApiHash
                || after.PhoneNumber != before.PhoneNumber;

            bool wantsConnect =
                dlg.Action == SettingsAction.Connect
                || (isStartup && dlg.Action == SettingsAction.None);

            if (wantsConnect && !after.IsComplete)
            {
                DialogResult r = StyledMessageBox.Show(
                    "api_id, api_hash und Telefonnummer müssen vollständig ausgefüllt sein.\r\n" +
                    "Erneut eingeben?",
                    "Zugangsdaten unvollständig",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this);

                if (r == DialogResult.Yes)
                {
                    continue; // Dialog erneut öffnen
                }

                await SetStatusAsync("Nicht angemeldet – Einstellungen öffnen.");
                return;
            }

            if (wantsConnect)
            {
                if (_connected)
                {
                    await LogoutAsync(wipeConfig: false);
                }
                await ConnectAsync();
            }
            else if (_connected && credsChanged && after.IsComplete)
            {
                await LogoutAsync(wipeConfig: false);
                await ConnectAsync();
            }
            else if (!_connected)
            {
                await SetStatusAsync("Nicht angemeldet – Einstellungen öffnen.");
            }

            return;
        }
    }

    private async Task LogoutAsync(bool wipeConfig)
    {
        _downloader.CancelAll();

        await _telegram.DisposeAsync();
        TryDelete(AppPaths.SessionFile);
        if (wipeConfig)
        {
            TryDelete(AppPaths.ConfigFile);
        }

        _connected = false;
        _chats.Clear();
        _loadedAudios.Clear();
        await RunScriptAsync("window.tv.setChats([])");
        await RunScriptAsync("window.tv.setSongs([])");
        await RunScriptAsync("window.tv.setConnected(false)");
        await SetStatusAsync(wipeConfig ? "Zugangsdaten gelöscht." : "Abgemeldet.");
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

    private static void Trace(string message) => AppLog.Line("UI", message);

    private void ShowError(Exception ex) => StyledMessageBox.Show(
        ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error, this);

    private static string JsStr(string value) => JsonSerializer.Serialize(value);

    private sealed record ChatDto(string Id, string Title, string Kind);

    /// <summary>Was die HTML-Seite pro Song erwartet (camelCase im JSON).</summary>
    private sealed record SongDto(
        string FileId,
        string DateIso,
        string DateDisplay,
        string Performer,
        string Title,
        string FileName,
        double? DurationSec,
        string SizeDisplay);
}

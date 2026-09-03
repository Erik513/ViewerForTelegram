using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic.Services;
using ViewerForTelegram.UI.Controls;
using StyledGrid = ErikwnkWFUI.Controls.DataGridView;
using StyledMessageBox = ErikwnkWFUI.Forms.MessageBox;
using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Main window: a top bar (settings, chat, time range, search), the list of
/// audio messages, and a player strip at the bottom. Double-click a row to
/// download (if needed) and play it.
/// </summary>
public sealed class MainForm : StyledForm
{
    private static readonly int[] RangeDayOptions = { 3, 7, 14, 30 };

    private readonly ITelegramSource _telegram;
    private readonly IConfigStore _configStore;
    private readonly IMediaCache _cache;
    private readonly AudioFeedService _feed;
    private readonly MediaDownloader _downloader;
    private readonly IAudioPlayer _audio;
    private readonly JsonUiStateStore _uiStateStore;

    private readonly ComboBox _groupCombo;
    private readonly ComboBox _rangeCombo;
    private readonly TextBox _searchBox;
    private readonly Label _cacheLabel;
    private readonly Label _statusLabel;
    private readonly StyledGrid _list;
    private readonly PlayerPanel _player;
    private readonly System.Windows.Forms.Timer _positionTimer;

    private List<TelegramChat> _chats = new();
    private IReadOnlyList<FeedItem> _items = Array.Empty<FeedItem>();
    private readonly Dictionary<long, AudioMessage> _byFileId = new();
    private long? _currentFileId;
    private long _pendingFileId;   // a track PlayAsync is currently loading
    private int _playSeq;          // bumped per PlayAsync so a superseded one bails out
    private CancellationTokenSource? _playCts;
    private bool _suppressListEvents;

    private bool _started;
    private bool _connecting;
    private bool _connected;
    private bool _suppressComboEvents;

    public MainForm(
        ITelegramSource telegram,
        IConfigStore configStore,
        IMediaCache cache,
        AudioFeedService feed,
        MediaDownloader downloader,
        IAudioPlayer audio,
        JsonUiStateStore uiStateStore)
        : base(StyledFormOptions.CreateStandard("Viewer for Telegram"))
    {
        _telegram = telegram;
        _configStore = configStore;
        _cache = cache;
        _feed = feed;
        _downloader = downloader;
        _audio = audio;
        _uiStateStore = uiStateStore;

        MinimumSize = new Size(820, 520);
        Size = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- top bar ----
        var settingsButton = UIStyles.Buttons.CreateStandard("⚙", "Settings", new Size(34, 28));
        settingsButton.Anchor = AnchorStyles.Left;
        settingsButton.Click += async (_, _) => await OpenSettingsAsync(isStartup: false);

        _groupCombo = UIStyles.ComboBoxes.CreateStandard();
        _groupCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _groupCombo.SelectedIndexChanged += (_, _) => OnFilterChanged();

        _rangeCombo = UIStyles.ComboBoxes.CreateStandard();
        _rangeCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _rangeCombo.Items.AddRange(new object[]
        {
            "Last 3 days", "Last 7 days", "Last 14 days", "Last 30 days"
        });
        _rangeCombo.SelectedIndexChanged += (_, _) => OnFilterChanged();

        // No factory placeholder - that variant writes the placeholder string
        // into .Text, which would then be read as a filter. Use the native one.
        _searchBox = UIStyles.TextBoxes.CreateStandard();
        _searchBox.PlaceholderText = "Filter …";
        _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _searchBox.TextChanged += (_, _) => RenderList();

        _cacheLabel = UIStyles.Labels.CreateMuted("Cache: –");
        _cacheLabel.Anchor = AnchorStyles.Right;
        _cacheLabel.TextAlign = ContentAlignment.MiddleRight;
        _cacheLabel.AutoSize = false;

        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(10, 6, 10, 4),
            BackColor = UIStyles.Colors.BackgroundDarkElevated
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        topRow.Controls.Add(settingsButton, 0, 0);
        topRow.Controls.Add(_groupCombo, 1, 0);
        topRow.Controls.Add(_rangeCombo, 2, 0);
        topRow.Controls.Add(_searchBox, 3, 0);
        topRow.Controls.Add(_cacheLabel, 4, 0);

        _statusLabel = UIStyles.Labels.CreateMuted("");
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Padding = new Padding(12, 0, 12, 0);
        _statusLabel.AutoSize = false;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.BackColor = UIStyles.Colors.BackgroundDarkElevated;

        // ---- list ----
        _list = new StyledGrid
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            MultiSelect = false,
            AllowUserToResizeColumns = false,
            AllowUserToOrderColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ScrollBars = ScrollBars.Vertical,   // no horizontal scrollbar, ever
        };
        _list.RowTemplate.Height = 26;
        AddColumn("Date", DataGridViewAutoSizeColumnMode.AllCells);
        AddColumn("Title", DataGridViewAutoSizeColumnMode.Fill, fillWeight: 62);
        AddColumn("Performer", DataGridViewAutoSizeColumnMode.Fill, fillWeight: 38);
        AddColumn("Length", DataGridViewAutoSizeColumnMode.AllCells);
        AddColumn("Size", DataGridViewAutoSizeColumnMode.AllCells);
        // Selecting a row loads and plays it; double-click / Enter on the
        // already-playing row toggles play/pause; Left/Right seek +-10s.
        _list.SelectionChanged += (_, _) => LoadSelected();
        _list.CellMouseDoubleClick += (_, _) => PlaySelected();
        _list.KeyDown += OnListKeyDown;

        // ---- player ----
        _player = new PlayerPanel();
        _player.PlayPause += OnPlayPause;
        _player.Seek += seconds => _audio.Position = TimeSpan.FromSeconds(seconds);
        _player.VolumeChanged += OnVolumeChanged;
        _player.Save += SaveCurrent;
        _player.CancelDownload += () => _playCts?.Cancel();

        _positionTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _positionTimer.Tick += (_, _) => _player.SetPosition(_audio.Position);

        _audio.PlaybackEnded += (_, _) =>
        {
            _positionTimer.Stop();
            _player.SetPlaying(false);
            _player.SetPosition(_audio.Duration);
        };
        _audio.PlaybackFailed += (_, ex) =>
        {
            _positionTimer.Stop();
            _player.SetIdle();
            _currentFileId = null;
            Status("Playback failed: " + ex.Message);
        };

        // A 4-row grid so nothing can overlap regardless of window size.
        _player.Dock = DockStyle.Fill;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = UIStyles.Colors.BackgroundMedium
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, PlayerPanel.PanelHeight));
        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(_statusLabel, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(_player, 0, 3);

        ContentPanel.Controls.Add(root);

        Shown += async (_, _) =>
        {
            if (_started)
            {
                return;
            }
            _started = true;
            await StartAsync();
        };
        FormClosing += (_, _) =>
        {
            _playCts?.Cancel();
            _positionTimer.Stop();
            _audio.Stop();
            SaveUiState();
        };
    }

    // ---------- startup / connect ----------
    private async Task StartAsync()
    {
        UiState state = _uiStateStore.Load();
        _suppressComboEvents = true;
        _rangeCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RangeDayOptions, state.RangeDays));
        if (_rangeCombo.SelectedIndex < 0)
        {
            _rangeCombo.SelectedIndex = 1; // 7 days
        }
        _suppressComboEvents = false;
        _player.Volume = Math.Clamp(state.VolumePercent, 0, 100) / 100f;
        _audio.Volume = _player.Volume;

        PushCacheInfo();

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
        }
        catch (Exception ex)
        {
            _connected = false;
            Status("Sign-in failed: " + ex.Message);
        }
        finally
        {
            _connecting = false;
        }
    }

    private async Task ConnectAndListChatsAsync()
    {
        if (!_configStore.Load().IsComplete)
        {
            throw new InvalidOperationException("Credentials are missing.");
        }

        Status("Connecting …");
        await _telegram.ConnectAsync(AskForCodeAsync, CancellationToken.None);

        _chats = (await _telegram.GetChatsAsync(CancellationToken.None))
            .OrderBy(c => c.Title)
            .ToList();

        _suppressComboEvents = true;
        _groupCombo.Items.Clear();
        foreach (TelegramChat chat in _chats)
        {
            _groupCombo.Items.Add(new ChatChoice(chat));
        }

        long lastId = _uiStateStore.Load().LastChatId;
        int idx = _chats.FindIndex(c => c.Id == lastId);
        _groupCombo.SelectedIndex = idx >= 0 ? idx : (_chats.Count > 0 ? 0 : -1);
        _suppressComboEvents = false;

        Status($"Signed in – {_chats.Count} groups/channels.");

        if (_groupCombo.SelectedIndex >= 0)
        {
            await LoadFeedAsync();
        }
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
                throw new OperationCanceledException("No login code entered.");
            });
            return Task.FromResult(code);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new OperationCanceledException("Sign-in cancelled.");
        }
    }

    // ---------- feed ----------
    private void OnFilterChanged()
    {
        if (_suppressComboEvents)
        {
            return;
        }
        SaveUiState();
        _ = LoadFeedAsync();
    }

    private int SelectedDays =>
        _rangeCombo.SelectedIndex >= 0 ? RangeDayOptions[_rangeCombo.SelectedIndex] : 7;

    private TelegramChat? SelectedChat =>
        (_groupCombo.SelectedItem as ChatChoice)?.Chat;

    private async Task LoadFeedAsync()
    {
        if (SelectedChat is not { } chat)
        {
            return;
        }

        Status("Loading …");
        try
        {
            _items = await _feed.LoadAsync(chat.Id, SelectedDays, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Status("Loading failed: " + ex.Message);
            return;
        }

        _byFileId.Clear();
        foreach (FeedItem item in _items)
        {
            _byFileId[item.Audio.FileId] = item.Audio;
        }

        RenderList();
    }

    private void AddColumn(string header, DataGridViewAutoSizeColumnMode mode, int fillWeight = 100)
    {
        _list.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            AutoSizeMode = mode,
            FillWeight = fillWeight,
            MinimumWidth = 46,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False
        });
    }

    private void RenderList()
    {
        string query = _searchBox.Text.Trim();
        var filtered = _items
            .Where(i => query.Length == 0 || Matches(i.Audio, query))
            .ToList();

        _suppressListEvents = true;
        _list.SuspendLayout();
        _list.Rows.Clear();
        foreach (FeedItem item in filtered)
        {
            AudioMessage a = item.Audio;
            int i = _list.Rows.Add(
                a.DateUtc.ToLocalTime().ToString("yyyy-MM-dd"),
                a.Title,
                a.Performer,
                a.Duration is { } d ? $"{(int)d.TotalMinutes}:{d.Seconds:00}" : "–",
                $"{a.SizeBytes / 1024d / 1024d:0.0} MB");
            _list.Rows[i].Tag = a.FileId;
        }
        _list.ClearSelection();
        try { _list.CurrentCell = null; } catch { }   // no auto-selected row 0
        _list.ResumeLayout();
        _suppressListEvents = false;

        if (_currentFileId is long fid)
        {
            SelectRow(fid);
        }

        if (_items.Count == 0)
        {
            Status(_connected ? "No audio in this time range." : "Not signed in – open Settings.");
        }
        else if (query.Length == 0)
        {
            Status($"{_items.Count} audios");
        }
        else
        {
            Status($"{filtered.Count} of {_items.Count} audios");
        }
    }

    private static bool Matches(AudioMessage a, string query) =>
        $"{a.Performer} {a.Title} {a.FileName}".Contains(query, StringComparison.OrdinalIgnoreCase);

    // ---------- playback ----------
    private AudioMessage? SelectedAudio =>
        _list.SelectedRows.Count > 0
        && _list.SelectedRows[0].Tag is long fid
        && _byFileId.TryGetValue(fid, out AudioMessage? a)
            ? a
            : null;

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                PlaySelected();
                e.Handled = true;
                break;
            case Keys.Right:
                SeekBy(TimeSpan.FromSeconds(10));
                e.Handled = e.SuppressKeyPress = true;
                break;
            case Keys.Left:
                SeekBy(TimeSpan.FromSeconds(-10));
                e.Handled = e.SuppressKeyPress = true;
                break;
            // Up/Down stay native so they move the selection (which plays the row).
        }
    }

    private void SeekBy(TimeSpan delta)
    {
        if (_currentFileId is null || _audio.Duration <= TimeSpan.Zero)
        {
            return;
        }
        _audio.Position += delta;
        _player.SetPosition(_audio.Position);
    }

    /// <summary>Row selected: load + play it, unless it is already the current / loading track.</summary>
    private void LoadSelected()
    {
        if (_suppressListEvents)
        {
            return;
        }
        if (SelectedAudio is { } audio
            && audio.FileId != _currentFileId
            && audio.FileId != _pendingFileId)
        {
            _ = PlayAsync(audio);
        }
    }

    /// <summary>Double-click / Enter: like select, but toggles play/pause on the current track.</summary>
    private void PlaySelected()
    {
        if (SelectedAudio is not { } audio)
        {
            return;
        }

        if (_currentFileId == audio.FileId && _audio.State != PlaybackState.Stopped)
        {
            OnPlayPause();
            return;
        }

        if (audio.FileId != _pendingFileId)
        {
            _ = PlayAsync(audio);
        }
    }

    private async Task PlayAsync(AudioMessage audio)
    {
        // Cancel whatever the previous PlayAsync was doing, hard.
        _playCts?.Cancel();
        _playCts?.Dispose();
        var cts = _playCts = new CancellationTokenSource();
        CancellationToken token = cts.Token;

        int seq = ++_playSeq;
        _pendingFileId = audio.FileId;

        StopCurrent();
        _currentFileId = audio.FileId;
        SelectRow(audio.FileId);

        string title = audio.DisplayName;
        _player.SetDownloading(audio, 0);
        Status($"Downloading: {title}");
        var progress = new Progress<int>(p =>
        {
            if (seq == _playSeq && _pendingFileId == audio.FileId)
            {
                _player.SetDownloading(audio, p);
            }
        });

        string path;
        try
        {
            path = await _downloader.EnsureLocalAsync(audio, progress, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer PlayAsync, or the user pressed the cancel
            // button. Only the latter (still the current request) resets the UI.
            if (seq == _playSeq)
            {
                _player.SetIdle();
                _currentFileId = null;
                _pendingFileId = 0;
                Status("Cancelled.");
            }
            return;
        }
        catch (Exception ex)
        {
            if (seq == _playSeq)
            {
                _player.SetIdle();
                _currentFileId = null;
                _pendingFileId = 0;
                Status("Download failed: " + ex.Message);
            }
            return;
        }

        if (seq != _playSeq || token.IsCancellationRequested)
        {
            return;
        }

        PushCacheInfo();

        try
        {
            _audio.Load(path);
        }
        catch (Exception ex)
        {
            _player.SetIdle();
            _currentFileId = null;
            _pendingFileId = 0;
            Status($"Cannot play {Path.GetExtension(audio.FileName)} – use the download button and open it elsewhere. ({ex.Message})");
            return;
        }

        _pendingFileId = 0;
        _player.SetLoaded(audio, _audio.Duration);
        _audio.Play();
        _player.SetPlaying(true);
        _positionTimer.Start();
        SelectRow(audio.FileId);
        Status($"Playing: {title}");
    }

    /// <summary>Select (and keep the keyboard focus on) the row for this file.</summary>
    private void SelectRow(long fileId)
    {
        foreach (DataGridViewRow row in _list.Rows)
        {
            if (row.Tag is long fid && fid == fileId)
            {
                if (!row.Selected || _list.CurrentCell?.RowIndex != row.Index)
                {
                    _suppressListEvents = true;
                    _list.CurrentCell = row.Cells[0]; // scrolls it into view
                    row.Selected = true;
                    _suppressListEvents = false;
                }
                break;
            }
        }
    }

    private void OnPlayPause()
    {
        switch (_audio.State)
        {
            case PlaybackState.Playing:
                _audio.Pause();
                _player.SetPlaying(false);
                _positionTimer.Stop();
                break;
            case PlaybackState.Paused:
            case PlaybackState.Stopped:
                _audio.Play();
                _player.SetPlaying(true);
                _positionTimer.Start();
                break;
        }
    }

    private void OnVolumeChanged(float v)
    {
        _audio.Volume = v;
        SaveUiState();
    }

    private void StopCurrent()
    {
        _positionTimer.Stop();
        _audio.Stop();
        _player.SetIdle();
        _currentFileId = null;
    }

    private void SaveCurrent()
    {
        if (_currentFileId is not long fid || !_byFileId.TryGetValue(fid, out AudioMessage? audio))
        {
            return;
        }
        if (!_cache.Contains(audio))
        {
            Status("Not downloaded yet.");
            return;
        }

        string source = _cache.GetPath(audio);
        string name = string.Join("_", audio.FileName.Split(Path.GetInvalidFileNameChars()));
        TelegramConfig cfg = _configStore.Load();

        try
        {
            if (cfg.UseDownloadFolder
                && !string.IsNullOrWhiteSpace(cfg.DownloadFolder)
                && Directory.Exists(cfg.DownloadFolder))
            {
                string dest = Path.Combine(cfg.DownloadFolder, name);
                File.Copy(source, dest, overwrite: true);
                Status($"Saved: {name}");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                FileName = name,
                InitialDirectory = Directory.Exists(cfg.DownloadFolder)
                    ? cfg.DownloadFolder
                    : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Filter = "Audio file|*" + Path.GetExtension(name) + "|All files|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                File.Copy(source, dlg.FileName, overwrite: true);
                Status($"Saved: {Path.GetFileName(dlg.FileName)}");
            }
        }
        catch (Exception ex)
        {
            Status("Save failed: " + ex.Message);
        }
    }

    // ---------- settings ----------
    private async Task OpenSettingsAsync(bool isStartup)
    {
        while (true)
        {
            TelegramConfig before = _configStore.Load();

            using var dlg = new SettingsForm(before, _cache, _connected);
            dlg.ShowDialog(this);
            PushCacheInfo();

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
                    "api_id, api_hash and phone number must all be filled in.\r\n" +
                    "Enter them again?",
                    "Credentials incomplete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this);

                if (r == DialogResult.Yes)
                {
                    continue;
                }
                Status("Not signed in – open Settings.");
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
                Status("Not signed in – open Settings.");
            }
            return;
        }
    }

    private async Task LogoutAsync(bool wipeConfig)
    {
        _playCts?.Cancel();
        _downloader.CancelAll();
        _playSeq++;              // abandon any in-flight PlayAsync
        _pendingFileId = 0;
        StopCurrent();

        await _telegram.DisposeAsync();
        TryDelete(AppPaths.SessionFile);
        if (wipeConfig)
        {
            TryDelete(AppPaths.ConfigFile);
        }

        _connected = false;
        _chats.Clear();
        _items = Array.Empty<FeedItem>();
        _byFileId.Clear();
        _suppressComboEvents = true;
        _groupCombo.Items.Clear();
        _suppressComboEvents = false;
        _suppressListEvents = true;
        _list.Rows.Clear();
        _suppressListEvents = false;
        Status(wipeConfig ? "Credentials deleted." : "Signed out.");
    }

    // ---------- helpers ----------
    private void PushCacheInfo()
    {
        (int count, long bytes) = _cache.GetStats();
        string size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
            : $"{bytes / 1024d / 1024d:0} MB";
        _cacheLabel.Text = $"Cache: {size} ({count})";
    }

    private void SaveUiState()
    {
        _uiStateStore.Save(new UiState(
            LastChatId: SelectedChat?.Id ?? 0,
            RangeDays: SelectedDays,
            VolumePercent: (int)Math.Round(_player.Volume * 100)));
    }

    private void Status(string text) => _statusLabel.Text = text;

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

    private sealed record ChatChoice(TelegramChat Chat)
    {
        public override string ToString() =>
            $"[{(Chat.Kind == TelegramChatKind.Channel ? "Channel" : "Group")}] {Chat.Title}";
    }
}

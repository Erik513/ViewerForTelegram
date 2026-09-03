using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
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
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 400 };
    private readonly System.Windows.Forms.Timer _positionTimer;

    private List<TelegramChat> _chats = new();
    private List<FeedItem> _items = new();
    private readonly Dictionary<long, AudioMessage> _byFileId = new();
    private long? _currentFileId;   // loaded in the audio player (playing / paused)
    private long? _selectedFileId;  // the row the player panel is showing
    private long _pendingFileId;    // a track being downloaded right now
    private int _playSeq;           // bumped per download so a superseded one bails out
    private int _lastProgress;
    private CancellationTokenSource? _playCts;
    private bool _suppressListEvents;
    private readonly ToolStripItem _saveMenuItem;

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
        var settingsButton = UIStyles.Buttons.CreatePrimary("", "Settings", new Size(30, 30));
        settingsButton.Anchor = AnchorStyles.None;   // square, centred in its cell, no clipping
        settingsButton.Paint += (s, e) => GlyphIcons.DrawGear(
            e.Graphics, ((Control)s!).ClientRectangle, ((Control)s).ForeColor);
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
        _cacheLabel.Dock = DockStyle.Fill;
        _cacheLabel.TextAlign = ContentAlignment.MiddleRight;
        _cacheLabel.AutoSize = false;
        _cacheLabel.AutoEllipsis = false;
        _cacheLabel.Margin = new Padding(6, 0, 0, 0);

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
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
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
        AddColumn("Date", width: 84);
        AddColumn("Title", fill: 62);
        AddColumn("Artist", fill: 38);
        AddColumn("Length", width: 64);
        AddColumn("Size", width: 90);
        // The list only shows info. The player's one button does the work:
        // download / cancel / play / pause on the selected row.
        _list.SelectionChanged += (_, _) => ShowSelected();
        _list.CellMouseDoubleClick += (_, _) => OnMainButton();
        _list.KeyDown += OnListKeyDown;

        // context menu: save a copy to disk
        var menu = new ContextMenuStrip();
        _saveMenuItem = menu.Items.Add("Save a copy…", null, (_, _) => SaveSelected());
        menu.Opening += (_, e) => { _saveMenuItem.Enabled = SelectedAudio is not null; };
        _list.ContextMenuStrip = menu;

        // ---- player ----
        _player = new PlayerPanel();
        _player.MainButton += OnMainButton;
        _player.Save += SaveSelected;
        _player.BrowseFolder += OpenDownloadFolder;
        _player.Seek += seconds => Seek(TimeSpan.FromSeconds(seconds));
        _player.VolumeChanged += OnVolumeChanged;

        _positionTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _positionTimer.Tick += (_, _) =>
        {
            if (_selectedFileId == _currentFileId)
            {
                _player.SetPosition(_audio.Position);
            }
        };

        _audio.PlaybackEnded += (_, _) =>
        {
            _positionTimer.Stop();
            if (_selectedFileId == _currentFileId)
            {
                _player.SetButton(PlayerButton.Play);
                _player.SetPosition(_audio.Duration);
            }
        };
        _audio.PlaybackFailed += (_, ex) =>
        {
            _positionTimer.Stop();
            _currentFileId = null;
            ShowSelected();
            Status("Playback failed: " + ex.Message);
        };

        // A 4-row grid so nothing can overlap regardless of window size.
        _player.Dock = DockStyle.Fill;
        _player.Margin = new Padding(0);   // the default 3px margin ate into the panel

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
            _items = (await _feed.LoadAsync(chat.Id, SelectedDays, CancellationToken.None)).ToList();
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

    /// <summary>Add a column: pass <paramref name="fill"/> for a stretchy column, or <paramref name="width"/> for a fixed one.</summary>
    private void AddColumn(string header, int fill = 0, int width = 0)
    {
        bool isFill = fill > 0;
        _list.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = header,
            AutoSizeMode = isFill
                ? DataGridViewAutoSizeColumnMode.Fill
                : DataGridViewAutoSizeColumnMode.None,
            FillWeight = isFill ? fill : 100,
            Width = isFill ? 100 : width,
            MinimumWidth = isFill ? 80 : width,
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

        // Keep the previously-selected (or playing) row selected across a re-render.
        long? keep = _selectedFileId ?? _currentFileId;
        if (keep is long fid)
        {
            foreach (DataGridViewRow row in _list.Rows)
            {
                if (row.Tag is long rf && rf == fid)
                {
                    _list.CurrentCell = row.Cells[0];
                    row.Selected = true;
                    break;
                }
            }
        }
        _list.ResumeLayout();
        _suppressListEvents = false;
        ShowSelected();

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
                OnMainButton();
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
            // Up/Down stay native so they move the selection.
        }
    }

    private void Seek(TimeSpan position)
    {
        if (_currentFileId is not null && _selectedFileId == _currentFileId)
        {
            _audio.Position = position;
            _player.SetPosition(_audio.Position);
        }
    }

    private void SeekBy(TimeSpan delta)
    {
        if (_currentFileId is not null
            && _selectedFileId == _currentFileId
            && _audio.Duration > TimeSpan.Zero)
        {
            _audio.Position += delta;
            _player.SetPosition(_audio.Position);
        }
    }

    /// <summary>A row got selected - show it in the player (no download, no playback).</summary>
    private void ShowSelected()
    {
        if (_suppressListEvents)
        {
            return;
        }

        if (SelectedAudio is not { } audio)
        {
            _selectedFileId = null;
            _player.SetIdle();
            return;
        }

        _selectedFileId = audio.FileId;
        bool cached = _cache.Contains(audio);

        if (_pendingFileId == audio.FileId)
        {
            _player.ShowTrack(audio, PlayerButton.Cancel, cached: false);
            _player.SetDownloadProgress(_lastProgress);
        }
        else if (_currentFileId == audio.FileId)
        {
            bool playing = _audio.State == PlaybackState.Playing;
            _player.ShowTrack(audio, playing ? PlayerButton.Pause : PlayerButton.Play, cached);
            _player.SetLoaded(_audio.Duration);
            _player.SetPosition(_audio.Position);
        }
        else
        {
            _player.ShowTrack(audio, PlayerButton.Play, cached);
        }
    }

    /// <summary>The one player button: download / cancel / play / pause the selected track.</summary>
    private void OnMainButton()
    {
        if (SelectedAudio is not { } audio)
        {
            return;
        }

        // Downloading this one -> cancel.
        if (_pendingFileId == audio.FileId)
        {
            _playCts?.Cancel();
            return;
        }

        // Loaded in the audio player -> toggle play/pause.
        if (_currentFileId == audio.FileId)
        {
            if (_audio.State == PlaybackState.Playing)
            {
                _audio.Pause();
                _positionTimer.Stop();
                _player.SetButton(PlayerButton.Play);
            }
            else
            {
                _audio.Play();
                _positionTimer.Start();
                _player.SetButton(PlayerButton.Pause);
            }
            return;
        }

        // Not loaded: play from cache if it's there, otherwise download first.
        if (_cache.Contains(audio))
        {
            PlayFromCache(audio);
        }
        else
        {
            _ = DownloadAndPlayAsync(audio);
        }
    }

    private void PlayFromCache(AudioMessage audio)
    {
        StopCurrent();
        try
        {
            _audio.Load(_cache.GetPath(audio));
        }
        catch (Exception ex)
        {
            Status($"Cannot play {Path.GetExtension(audio.FileName)}: {ex.Message}");
            return;
        }

        _currentFileId = audio.FileId;
        BackfillDuration(audio.FileId, _audio.Duration);
        _player.SetLoaded(_audio.Duration);
        _audio.Play();
        _player.SetButton(PlayerButton.Pause);
        _positionTimer.Start();
        Status($"Playing: {audio.DisplayName}");
    }

    /// <summary>
    /// Telegram sometimes doesn't report a track's length (posted "as a file").
    /// Once the audio engine has decoded it we know the real duration - write it
    /// back into the model and the list so the Length column stops showing "–".
    /// </summary>
    private void BackfillDuration(long fileId, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero
            || !_byFileId.TryGetValue(fileId, out AudioMessage? a)
            || (a.Duration is { } known && known > TimeSpan.Zero))
        {
            return;
        }

        AudioMessage updated = a with { Duration = duration };
        _byFileId[fileId] = updated;

        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Audio.FileId == fileId)
            {
                _items[i] = _items[i] with { Audio = updated };
                break;
            }
        }

        foreach (DataGridViewRow row in _list.Rows)
        {
            if (row.Tag is long rf && rf == fileId)
            {
                row.Cells[3].Value = $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";
                break;
            }
        }
    }

    private async Task DownloadAndPlayAsync(AudioMessage audio)
    {
        _playCts?.Cancel();
        _playCts?.Dispose();
        var cts = _playCts = new CancellationTokenSource();
        CancellationToken token = cts.Token;

        int seq = ++_playSeq;
        _pendingFileId = audio.FileId;
        _lastProgress = 0;

        if (_selectedFileId == audio.FileId)
        {
            _player.SetDownloadProgress(0);
        }
        Status($"Downloading: {audio.DisplayName}");

        var progress = new Progress<int>(p =>
        {
            if (seq != _playSeq || _pendingFileId != audio.FileId)
            {
                return;
            }
            _lastProgress = p;
            if (_selectedFileId == audio.FileId)
            {
                _player.SetDownloadProgress(p);
            }
        });

        string path;
        try
        {
            path = await _downloader.EnsureLocalAsync(audio, progress, token);
        }
        catch (OperationCanceledException)
        {
            if (seq == _playSeq)
            {
                _pendingFileId = 0;
                Status("Cancelled.");
                ShowSelected();
            }
            return;
        }
        catch (Exception ex)
        {
            if (seq == _playSeq)
            {
                _pendingFileId = 0;
                Status("Download failed: " + ex.Message);
                ShowSelected();
            }
            return;
        }

        if (seq != _playSeq || token.IsCancellationRequested)
        {
            return;
        }

        _pendingFileId = 0;
        PushCacheInfo();

        StopCurrent();
        try
        {
            _audio.Load(path);
        }
        catch (Exception ex)
        {
            Status($"Cannot play {Path.GetExtension(audio.FileName)} – open it elsewhere via \"Save a copy\". ({ex.Message})");
            ShowSelected();
            return;
        }

        _currentFileId = audio.FileId;
        BackfillDuration(audio.FileId, _audio.Duration);
        _audio.Play();
        _positionTimer.Start();
        if (_selectedFileId == audio.FileId)
        {
            _player.SetLoaded(_audio.Duration);
            _player.SetButton(PlayerButton.Pause);
        }
        Status($"Playing: {audio.DisplayName}");
    }

    private void OnVolumeChanged(float v)
    {
        _audio.Volume = v;
        SaveUiState();
    }

    private void OpenDownloadFolder()
    {
        TelegramConfig cfg = _configStore.Load();
        string folder = Directory.Exists(cfg.EffectiveDownloadFolder)
            ? cfg.EffectiveDownloadFolder
            : AppPaths.CacheDir;
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Status("Cannot open folder: " + ex.Message);
        }
    }

    private void StopCurrent()
    {
        _positionTimer.Stop();
        _audio.Stop();
        _currentFileId = null;
    }

    private void SaveSelected()
    {
        if (SelectedAudio is not { } audio)
        {
            return;
        }
        if (!_cache.Contains(audio))
        {
            Status("Not downloaded yet - play it first.");
            return;
        }

        string source = _cache.GetPath(audio);
        string name = string.Join("_", audio.FileName.Split(Path.GetInvalidFileNameChars()));
        TelegramConfig cfg = _configStore.Load();

        try
        {
            if (cfg.UseDownloadFolder && Directory.Exists(cfg.EffectiveDownloadFolder))
            {
                string dest = Path.Combine(cfg.EffectiveDownloadFolder, name);
                File.Copy(source, dest, overwrite: true);
                Status($"Saved: {name}");
                return;
            }

            using var dlg = new SaveFileDialog
            {
                FileName = name,
                InitialDirectory = Directory.Exists(cfg.EffectiveDownloadFolder)
                    ? cfg.EffectiveDownloadFolder
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
        _items = new();
        _byFileId.Clear();
        _suppressComboEvents = true;
        _groupCombo.Items.Clear();
        _suppressComboEvents = false;
        _suppressListEvents = true;
        _list.Rows.Clear();
        _suppressListEvents = false;
        _selectedFileId = null;
        _player.SetIdle();
        Status(wipeConfig ? "Credentials deleted." : "Signed out.");
    }

    // ---------- helpers ----------
    private void PushCacheInfo()
    {
        (int count, long bytes) = _cache.GetStats();
        string size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
            : $"{bytes / 1024d / 1024d:0} MB";
        long limitMb = CachePolicy.LimitBytes / 1024 / 1024;
        _cacheLabel.Text = $"Cache {size} · {count}";
        _toolTip.SetToolTip(_cacheLabel,
            $"Downloaded songs kept locally: {size} / {limitMb} MB ({count} files).\r\n" +
            "The oldest are removed once the limit is reached.");
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

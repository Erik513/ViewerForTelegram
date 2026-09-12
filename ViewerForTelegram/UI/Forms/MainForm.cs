using System.Net.Http;
using ErikwnkCore;
using ErikwnkCore.Updater;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Helpers;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
using ViewerForTelegram.Logic.Services;
using ViewerForTelegram.UI.Controls;
using ViewerForTelegram.UI.Localization;
using ViewerForTelegram.UI;
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
    // > 0 = days; < 0 = "the newest |n| audios" (no date limit) - see AudioFeedService.
    private static readonly int[] RangeDayOptions =
        { 3, 7, 14, 30, 60, -50, -100, -200, -500, -1000, -2000, -3000, -4000, -5000 };

    // Format-filter combo entries below "all". Key is persisted in ui-state;
    // Ext is matched against the file name's extension (lower-case).
    private static readonly (string Key, string Label, string[] Ext)[] FormatOptions =
    {
        ("mp3",  "MP3",       new[] { ".mp3" }),
        ("m4a",  "M4A / AAC", new[] { ".m4a", ".aac", ".mp4" }),
        ("wav",  "WAV",       new[] { ".wav" }),
        ("flac", "FLAC",      new[] { ".flac" }),
        ("aiff", "AIFF",      new[] { ".aiff", ".aif" }),
        ("ogg",  "OGG",       new[] { ".ogg" }),
        ("opus", "Opus",      new[] { ".opus" }),
        ("wma",  "WMA",       new[] { ".wma" }),
    };

    private const string UpdateRepoOwner = "Erik513";
    private const string UpdateRepoName = "ViewerForTelegram";

    // Kept alive for the app's whole lifetime (not per-check, like before) so
    // the same AppUpdater/HttpClients can still drive a download later, if
    // the user clicks the Settings update button instead of reacting to the
    // startup notice.
    private readonly HttpClient _updateCheckClient = new();
    private readonly HttpClient _updateDownloadClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AppUpdater _updateAppUpdater;

    // The most recent update check's result, or null - remembered so
    // OpenSettingsAsync can add an update button without checking again.
    private UpdateCheckResult? _pendingUpdateResult;

    private readonly ITelegramSource _telegram;
    private readonly IConfigStore _configStore;
    private readonly IMediaCache _cache;
    private readonly AudioFeedService _feed;
    private readonly MediaDownloader _downloader;
    private readonly IAudioPlayer _audio;
    private readonly JsonUiStateStore _uiStateStore;
    private readonly JsonFeedCacheStore _feedCacheStore;

    private readonly ComboBox _groupCombo;
    private readonly ComboBox _rangeCombo;
    private readonly ComboBox _formatCombo;
    private readonly TextBox _searchBox;
    private readonly Label _cacheLabel;
    private readonly Button _settingsButton;
    private readonly Button _refreshButton;
    private readonly StyledGrid _list;
    private readonly PlayerPanel _player;
    private readonly ToolTip _toolTip = new() { AutoPopDelay = 12000, InitialDelay = 400 };
    private readonly System.Windows.Forms.Timer _positionTimer;
    private readonly System.Windows.Forms.Timer _feedDebounce;    // coalesce rapid chat/range changes
    private readonly System.Windows.Forms.Timer _filterDebounce;  // coalesce search-box typing

    private List<TelegramChat> _chats = new();
    private List<FeedItem> _items = new();

    // The rows actually on screen: _items after the format / text filter and the
    // column sort, index-aligned with the grid. The grid runs in VirtualMode and
    // pulls cell values from here via CellValueNeeded, so only the ~30 visible
    // rows are ever realised - filtering a 5000-track list stays instant.
    private List<FeedItem> _view = new();
    private IReadOnlySet<long> _cachedIds = new HashSet<long>();

    private readonly Dictionary<long, AudioMessage> _byFileId = new();
    private long? _currentFileId;   // loaded in the audio player (playing / paused)
    private long? _selectedFileId;  // the row the player panel is showing
    private long _pendingFileId;    // a track being downloaded right now
    private long _lastTrackId;    // last track put in the player (persisted; drives the row tint before playback)
    private int _playSeq;           // bumped per download so a superseded one bails out
    private int _feedSeq;           // bumped per feed load so a superseded one bails out
    private int _feedLoadingSeq;    // != 0 while a feed load owns the status line (matches its _feedSeq)
    private string? _deferredStatus; // a playback status held back until the feed load above finishes
    // The biggest count-mode ("Newest N") list built up this session for one
    // chat - never shrinks (a smaller N just displays fewer of it), so a later
    // grow (or the next app start) doesn't re-fetch what was already known.
    // Basis for incremental reuse and what gets persisted to disk.
    private const int MaxLargestAudios = ViewerForTelegram.Data.JsonFeedCacheStore.MaxAudiosPerChat;

    // Below this many known audios a count-mode reload re-fetches the chat in
    // full instead of reusing the cached list - cheap at this size, and unlike
    // the incremental path it drops tracks deleted from inside the known range.
    private const int IncrementalReuseFloor = 250;

    private long _largestChatId;
    private int _largestRange;      // informational: -_largestItems.Count
    private List<FeedItem> _largestItems = new();
    private int _lastProgress;
    private CancellationTokenSource? _playCts;
    private CancellationTokenSource? _feedCts;
    private CancellationTokenSource? _prefetchCts;   // background document warm-up for the selected row

    private int _sortColumn = -1;   // -1 = feed order (newest first); else a column index
    private bool _sortAscending;
    // Where the next RenderList should leave the viewport. KeepOffset is the
    // norm (a live filter keystroke); Top follows a re-sort; KeptRow follows a
    // chat switch (land on the last-played row, or the top if it dropped out).
    private enum NextScroll { KeepOffset, Top, KeptRow }
    private NextScroll _nextScroll = NextScroll.KeepOffset;

    private long? _dragFileId;      // row armed for a file drag-out (only if cached)
    private Rectangle _dragBox;     // move past this before a drag actually starts

    private int _newSinceLastVisit; // tracks the last load pulled in that weren't already known - badged on the refresh button
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
        JsonUiStateStore uiStateStore,
        JsonFeedCacheStore feedCacheStore)
        : base(StyledFormOptions.CreateStandard(
            "Viewer for Telegram",
            titleTextAlign: ContentAlignment.MiddleLeft,
            icon: AppAssets.TitleBarLogo,
            windowIcon: AppAssets.WindowIcon))
    {
        _telegram = telegram;
        _configStore = configStore;
        _cache = cache;
        _feed = feed;
        _downloader = downloader;
        _audio = audio;
        _uiStateStore = uiStateStore;
        _feedCacheStore = feedCacheStore;
        _updateAppUpdater = new AppUpdater(UpdateRepoOwner, UpdateRepoName, _updateCheckClient, _updateDownloadClient);

        MinimumSize = new Size(820, 520);
        Size = new Size(1040, 720);
        StartPosition = FormStartPosition.CenterScreen;
        _toolTip.ReviveOnFormActivate(this);

        // Changing the chat AND the range in quick succession should trigger one
        // load for the final state, not two (the first would run to completion
        // over the shared connection before the second could start). Same idea
        // for search-box typing vs re-filtering a few thousand rows.
        _feedDebounce = new System.Windows.Forms.Timer { Interval = 350 };
        _feedDebounce.Tick += (_, _) => { _feedDebounce.Stop(); _ = LoadFeedAsync(); };
        _filterDebounce = new System.Windows.Forms.Timer { Interval = 250 };
        _filterDebounce.Tick += (_, _) => { _filterDebounce.Stop(); RenderList(); };

        // ---- top bar ----
        _settingsButton = UIStyles.Buttons.CreatePrimary("", Loc.S("top.settings.tip"), new Size(30, 30));
        _settingsButton.Anchor = AnchorStyles.None;   // square, centred in its cell, no clipping
        _settingsButton.Paint += (s, e) => GlyphIcons.DrawGear(
            e.Graphics, ((Control)s!).ClientRectangle, ((Control)s).ForeColor);
        _settingsButton.Click += async (_, _) => await OpenSettingsAsync(isStartup: false);

        _groupCombo = UIStyles.ComboBoxes.CreateStandard();
        _groupCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        // Long chat names get clipped in the narrow box - show the full one in a
        // tooltip, and widen the dropdown list so it isn't clipped there too.
        _groupCombo.SelectedIndexChanged += (_, _) => UpdateGroupComboTooltip();
        _groupCombo.SizeChanged += (_, _) => UpdateGroupComboTooltip();
        _groupCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressComboEvents)
            {
                return;
            }
            // A different chat's list has nothing to do with the old search
            // term - start fresh (clears before the load so no stale filter
            // flashes over the new list).
            _searchBox.Clear();
            // Land on the last-played row in the new list (or its top), not
            // wherever this chat's predecessor happened to be scrolled.
            _nextScroll = NextScroll.KeptRow;
            OnFilterChanged();
        };

        _rangeCombo = UIStyles.ComboBoxes.CreateStandard();
        _rangeCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _rangeCombo.SelectedIndexChanged += (_, _) => OnFilterChanged();

        // Filters the already-loaded list by file format - no re-fetch, just a
        // re-render, so it's not routed through OnFilterChanged.
        _formatCombo = UIStyles.ComboBoxes.CreateStandard();
        _formatCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _formatCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressComboEvents)
            {
                return;
            }
            SaveUiState();
            RenderList();
        };

        _refreshButton = UIStyles.Buttons.CreatePrimary("", Loc.S("top.refresh.tip"), new Size(30, 30));
        _refreshButton.Anchor = AnchorStyles.None;
        _refreshButton.Paint += (s, e) =>
        {
            var ctl = (Control)s!;
            GlyphIcons.DrawRefresh(e.Graphics, ctl.ClientRectangle, ctl.ForeColor);
            if (_newSinceLastVisit > 0)
            {
                DrawNewBadge(e.Graphics, ctl.ClientRectangle, _newSinceLastVisit);
            }
        };
        _refreshButton.Click += async (_, _) => await RefreshAsync();

        // No factory placeholder - that variant writes the placeholder string
        // into .Text, which would then be read as a filter. Use the native one.
        _searchBox = UIStyles.TextBoxes.CreateStandard();
        _searchBox.PlaceholderText = Loc.S("top.filter.placeholder");
        _searchBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _searchBox.TextChanged += (_, _) => { _filterDebounce.Stop(); _filterDebounce.Start(); };

        // Plain Label (not UIStyles.Labels.CreateMuted): that one is owner-drawn
        // and ignores ForeColor, so the "cache full" red would never show.
        _cacheLabel = new Label
        {
            Text = Loc.S("cache.initial"),
            AutoSize = false,
            AutoEllipsis = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = UIStyles.Fonts.Small,
            ForeColor = UIStyles.Colors.TextMuted,
            BackColor = UIStyles.Colors.BackgroundDarkElevated,
            Margin = new Padding(6, 0, 0, 0),
        };

        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(10, 6, 10, 4),
            BackColor = UIStyles.Colors.BackgroundDarkElevated
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        topRow.Controls.Add(_settingsButton, 0, 0);
        topRow.Controls.Add(_groupCombo, 1, 0);
        topRow.Controls.Add(_rangeCombo, 2, 0);
        topRow.Controls.Add(_formatCombo, 3, 0);
        topRow.Controls.Add(_refreshButton, 4, 0);
        topRow.Controls.Add(_searchBox, 5, 0);
        topRow.Controls.Add(_cacheLabel, 6, 0);

        // ---- list ----
        _list = new StyledGrid
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            MultiSelect = false,
            AllowUserToAddRows = false,      // no phantom row (and lets RowCount go to 0 in VirtualMode)
            AllowUserToDeleteRows = false,
            AllowUserToResizeColumns = false,
            AllowUserToOrderColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ScrollBars = ScrollBars.Vertical,   // no horizontal scrollbar, ever
            VirtualMode = true,   // cell values come from _view via CellValueNeeded
        };
        _list.CellValueNeeded += OnCellValueNeeded;
        _list.CellFormatting += OnCellFormatting;
        _list.RowTemplate.Height = 26;
        AddColumn(Loc.S("col.date"), width: 84);
        AddColumn(Loc.S("col.title"), fill: 62);
        AddColumn(Loc.S("col.artist"), fill: 38);
        AddColumn(Loc.S("col.length"), width: 64);
        AddColumn(Loc.S("col.size"), width: 90, alignRight: true);
        AddCachedColumn();   // last: a check for tracks already downloaded
        // The list only shows info. The player's one button does the work:
        // download / cancel / play / pause on the selected row.
        // ShowCellToolTips (WinForms default) shows a tooltip only for a cell
        // whose text is clipped - no explicit ToolTipText, so nothing else.
        _list.ShowCellToolTips = true;
        for (int c = 0; c <= 4; c++)
        {
            _list.Columns[c].SortMode = DataGridViewColumnSortMode.Programmatic;
        }
        _list.ColumnHeaderMouseClick += OnColumnHeaderClick;
        _list.SelectionChanged += (_, _) => ShowSelected();
        _list.CellMouseDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)   // not a header / row-header double-click
            {
                OnMainButton();
            }
        };
        _list.KeyDown += OnListKeyDown;
        _list.MouseDown += OnListMouseDown;
        _list.MouseMove += OnListMouseMove;

        // ---- player ----
        _player = new PlayerPanel();
        _player.MainButton += OnMainButton;
        _player.Save += SaveSelected;
        _player.BrowseFolder += OpenDownloadFolder;
        _player.Seek += seconds => Seek(TimeSpan.FromSeconds(seconds));
        _player.VolumeChanged += OnVolumeChanged;
        _audio.VolumeChangedExternally += (_, pos) =>
        {
            _player.Volume = pos;   // slider setter doesn't re-raise VolumeChanged
            SaveUiState();
        };

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
            HighlightPlayingRow();
            ShowSelected();
            PlaybackStatus(Loc.T("status.playbackFailed", ex.Message));
        };

        // A 3-row grid so nothing can overlap regardless of window size.
        // (The status / error line lives at the bottom of the player itself.)
        _player.Dock = DockStyle.Fill;
        _player.Margin = new Padding(0);   // the default 3px margin ate into the panel

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = UIStyles.Colors.BackgroundMedium
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, PlayerPanel.PanelHeight));
        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(_player, 0, 2);

        ContentPanel.Controls.Add(root);

        ApplyTexts();   // also fills the range combo
        Loc.Changed += OnLanguageChanged;

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
            Loc.Changed -= OnLanguageChanged;
            _playCts?.Cancel();
            _feedCts?.Cancel();
            _prefetchCts?.Cancel();
            _positionTimer.Stop();
            _feedDebounce.Stop();
            _filterDebounce.Stop();
            _audio.Stop();
            SaveUiState();
            try { if (Directory.Exists(DragTempDir)) { Directory.Delete(DragTempDir, recursive: true); } }
            catch { /* temp files - the OS clears %TEMP% eventually anyway */ }
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => this.WithRedrawSuspended(ApplyTexts);

    /// <summary>(Re-)applies every visible string from <see cref="Loc"/>.</summary>
    private void ApplyTexts()
    {
        FormTitle = Loc.S("app.title");
        UIStyles.Buttons.UpdateTooltip(_settingsButton, Loc.S("top.settings.tip"));
        UIStyles.Buttons.UpdateTooltip(_refreshButton, _newSinceLastVisit > 0
            ? Loc.T("top.refresh.tipNew", _newSinceLastVisit)
            : Loc.S("top.refresh.tip"));
        _searchBox.PlaceholderText = Loc.S("top.filter.placeholder");

        _list.Columns[0].HeaderText = Loc.S("col.date");
        _list.Columns[1].HeaderText = Loc.S("col.title");
        _list.Columns[2].HeaderText = Loc.S("col.artist");
        _list.Columns[3].HeaderText = Loc.S("col.length");
        _list.Columns[4].HeaderText = Loc.S("col.size");
        _list.Columns[CachedColumnIndex].ToolTipText = Loc.S("col.cached");

        _suppressComboEvents = true;

        int rangeSel = _rangeCombo.SelectedIndex;
        _rangeCombo.Items.Clear();
        foreach (int d in RangeDayOptions)
        {
            _rangeCombo.Items.Add(d > 0 ? Loc.T("range.days", d) : Loc.T("range.newest", -d));
        }
        if (rangeSel >= 0 && rangeSel < _rangeCombo.Items.Count)
        {
            _rangeCombo.SelectedIndex = rangeSel;
        }

        int formatSel = Math.Max(0, _formatCombo.SelectedIndex);
        _formatCombo.Items.Clear();
        _formatCombo.Items.Add(Loc.S("top.format.all"));
        foreach ((string _, string label, string[] _) in FormatOptions)
        {
            _formatCombo.Items.Add(label);
        }
        _formatCombo.SelectedIndex = formatSel < _formatCombo.Items.Count ? formatSel : 0;

        int groupSel = _groupCombo.SelectedIndex;
        _groupCombo.Items.Clear();
        foreach (TelegramChat chat in _chats)
        {
            _groupCombo.Items.Add(new ChatChoice(chat));
        }
        if (groupSel >= 0 && groupSel < _groupCombo.Items.Count)
        {
            _groupCombo.SelectedIndex = groupSel;
        }

        _suppressComboEvents = false;
        RefreshGroupComboHints();

        _player.ApplyTexts();
        PushCacheInfo();
        if (_items.Count > 0 || _connected)
        {
            RenderList();   // refresh the "{n} audios" status line
        }
    }

    /// <summary>
    /// Widen the chat dropdown to fit its longest entry, and refresh the
    /// clipped-name tooltip on the closed box.
    /// </summary>
    private void RefreshGroupComboHints()
    {
        if (_groupCombo.Items.Count == 0)
        {
            _groupCombo.DropDownWidth = _groupCombo.Width;
        }
        else
        {
            int widest = _groupCombo.Width;
            foreach (object item in _groupCombo.Items)
            {
                int w = TextRenderer.MeasureText(item.ToString(), _groupCombo.Font).Width;
                if (w > widest)
                {
                    widest = w;
                }
            }
            _groupCombo.DropDownWidth = widest + SystemInformation.VerticalScrollBarWidth + 6;
        }

        UpdateGroupComboTooltip();
    }

    private void UpdateGroupComboTooltip()
    {
        string text = _groupCombo.SelectedItem?.ToString() ?? "";
        int fits = _groupCombo.Width - SystemInformation.VerticalScrollBarWidth - 6;   // less the drop arrow
        bool clipped = text.Length > 0 && TextRenderer.MeasureText(text, _groupCombo.Font).Width > fits;
        _toolTip.SetToolTip(_groupCombo, clipped ? text : "");
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
        int formatIdx = Array.FindIndex(FormatOptions, o => o.Key == state.FormatFilter);
        _formatCombo.SelectedIndex = formatIdx >= 0 ? formatIdx + 1 : 0;
        _suppressComboEvents = false;
        _player.Volume = Math.Clamp(state.VolumePercent, 0, 100) / 100f;
        _audio.Volume = _player.Volume;
        _lastTrackId = state.LastPlayedFileId;   // select + tint this row once the feed loads

        // Show the last session's list for this chat immediately - before even
        // connecting - instead of a blank grid while a fresh "Newest N" is
        // re-fetched from scratch. LoadFeedAsync's own incremental reuse then
        // only tops it up (count mode) or does a normal fresh fetch that
        // replaces it (day mode).
        EnsureLargestFor(state.LastChatId);
        if (_largestItems.Count > 0)
        {
            _items = _largestItems;
            RebuildByFileId();
            RenderList();
            Toast(Loc.T("toast.listUpdated", Loc.Files(_items.Count)));
        }

        PushCacheInfo();

        _ = CheckForUpdatesAsync();   // best-effort, never blocks startup

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

    /// <summary>
    /// Checks the repo's latest GitHub release against this build and, if
    /// newer than what we've already notified about, shows the update
    /// prompt once. Best-effort - swallows everything so a GitHub outage or
    /// a rate limit never affects the rest of the app. The Settings dialog's
    /// update button (see <see cref="OpenSettingsAsync"/>) stays available
    /// regardless, so skipping a release here doesn't hide it for good.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            Version current = typeof(MainForm).Assembly.GetName().Version ?? new Version(1, 0, 0);
            _pendingUpdateResult = await _updateAppUpdater.CheckForUpdateAsync(current, TimeSpan.FromSeconds(5));

            if (_pendingUpdateResult == null)
            {
                return;
            }

            string latestVersionText = _pendingUpdateResult.LatestVersion.ToString();
            if (string.Equals(UpdateNotificationStore.LoadLastNotifiedVersion(), latestVersionText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UpdateNotificationStore.SaveLastNotifiedVersion(latestVersionText);
            await _updateAppUpdater.ShowUpdatePromptAsync(_pendingUpdateResult, current, this);
        }
        catch
        {
            // never let a failed update check affect the running app
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
            await RunWithRetryAsync("connecting", ConnectAndListChatsAsync);
            _connected = true;
        }
        catch (Exception ex)
        {
            _connected = false;
            Status(DescribeFailure("signin", ex));
        }
        finally
        {
            _connecting = false;
        }
    }

    private const int NetworkRetries = 3;

    /// <summary>
    /// Runs <paramref name="op"/>, retrying a few times on a transient network
    /// error (with backoff). A rate limit (FLOOD_WAIT) or an unsupported account
    /// is not retried - it is rethrown for the caller to report.
    /// </summary>
    private async Task RunWithRetryAsync(string what, Func<Task> op)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await op();
                return;
            }
            catch (Exception ex) when (attempt < NetworkRetries && IsTransient(ex))
            {
                AppLog.Error(what, $"attempt {attempt}/{NetworkRetries}: {ex.GetType().Name}: {ex.Message}");
                Status(Loc.T("status.retry", Loc.S("op." + what), attempt, NetworkRetries));
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is not OperationCanceledException
        && !IsRateLimit(ex, out _)
        && ex is not NotSupportedException and not InvalidOperationException;

    /// <summary>Matches WTelegramClient's "FLOOD_WAIT_&lt;seconds&gt;" rate-limit error.</summary>
    private static bool IsRateLimit(Exception ex, out int seconds)
    {
        seconds = 0;
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(e.Message ?? "", @"FLOOD_WAIT_(\d+)");
            if (m.Success)
            {
                seconds = int.Parse(m.Groups[1].Value);
                return true;
            }
        }
        return false;
    }

    private static string DescribeFailure(string what, Exception ex)
    {
        AppLog.Error(what, ex.ToString());
        if (IsRateLimit(ex, out int seconds))
        {
            return Loc.T("status.rateLimit", seconds);
        }
        return Loc.T("status.opFailed", Loc.S("op." + what), ex.Message);
    }

    private async Task ConnectAndListChatsAsync()
    {
        if (!_configStore.Load().IsComplete)
        {
            throw new InvalidOperationException(Loc.S("err.credentialsMissing"));
        }

        Status(Loc.S("status.connecting"));
        _passwordAttempts = 0;
        await _telegram.ConnectAsync(AskForCodeAsync, AskForPasswordAsync, CancellationToken.None);
        await ListChatsAndLoadAsync();
    }

    /// <summary>
    /// (Re)fetches the chat list into the combo and reloads the feed for the
    /// current (or remembered) chat. Assumes the client is already connected.
    /// </summary>
    private async Task ListChatsAndLoadAsync()
    {
        long keepChatId = SelectedChat?.Id ?? _uiStateStore.Load().LastChatId;

        _chats = (await _telegram.GetChatsAsync(CancellationToken.None))
            .OrderBy(c => c.Kind == TelegramChatKind.SavedMessages ? 0 : 1)
            .ThenBy(c => c.Title)
            .ToList();

        // A chat the user has left/been removed from no longer shows up here -
        // drop its persisted list too, or it would sit in feed-cache.json
        // forever, never displayed again.
        _feedCacheStore.PruneToKnownChats(_chats.Select(c => c.Id));

        _suppressComboEvents = true;
        _groupCombo.Items.Clear();
        foreach (TelegramChat chat in _chats)
        {
            _groupCombo.Items.Add(new ChatChoice(chat));
        }
        int idx = _chats.FindIndex(c => c.Id == keepChatId);
        _groupCombo.SelectedIndex = idx >= 0 ? idx : (_chats.Count > 0 ? 0 : -1);
        _suppressComboEvents = false;
        RefreshGroupComboHints();

        Status(Loc.T("status.signedIn", _chats.Count));

        if (_groupCombo.SelectedIndex >= 0)
        {
            await LoadFeedAsync();   // toasts internally on success

            // Correction pass, in the background: catches audios deleted from
            // the chat while this app wasn't connected (no update event for
            // those ever arrives after the fact). Deliberately only here - on
            // startup and on Refresh - not on every ordinary range/chat switch,
            // since re-verifying the whole known list isn't free.
            if (SelectedChat is { } loadedChat)
            {
                _ = CheckForDeletedAsync(loadedChat.Id);
            }
        }
    }

    private long _correctingChatId;   // != 0 while a correction pass (below) is running

    /// <summary>
    /// Re-verifies every audio known for <paramref name="chatId"/> still exists
    /// on Telegram. Anything gone is dropped, and - so "Newest 1000" still
    /// means 1000 once that many exist, not quietly fewer - the shortfall is
    /// refilled with the next-older ones via the normal incremental "grow"
    /// fetch. Updates <see cref="_largestItems"/> (persisted cache included)
    /// and, if currently shown, <see cref="_items"/>. Best-effort: any failure
    /// is silently ignored, nothing here is critical.
    /// </summary>
    private async Task CheckForDeletedAsync(long chatId)
    {
        if (_correctingChatId != 0 || chatId != _largestChatId || _largestItems.Count == 0)
        {
            return;
        }

        _correctingChatId = chatId;
        try
        {
            int targetCount = _largestItems.Count;   // refill back up to this, not just "minus deleted"
            List<int> ids = _largestItems.Select(i => i.Audio.MessageId).ToList();
            IReadOnlyList<int> deletedIds;
            try
            {
                deletedIds = await _telegram.FindDeletedMessagesAsync(chatId, ids, CancellationToken.None);
            }
            catch (Exception ex)
            {
                AppLog.Error("DeletionCheck", ex.ToString());
                return;
            }

            // The user may have switched to a different chat while this ran.
            if (deletedIds.Count == 0 || chatId != _largestChatId)
            {
                return;
            }

            var deletedSet = new HashSet<int>(deletedIds);
            List<FeedItem> corrected = _largestItems.Where(i => !deletedSet.Contains(i.Audio.MessageId)).ToList();

            if (corrected.Count < targetCount)
            {
                try
                {
                    corrected = (await _feed.LoadAsync(
                        chatId, -targetCount, CancellationToken.None,
                        previous: corrected.Select(i => i.Audio).ToList())).ToList();
                }
                catch (Exception ex)
                {
                    AppLog.Error("DeletionCheck", ex.ToString());   // keep the removal, skip the refill
                }

                if (chatId != _largestChatId)
                {
                    return;   // the user moved on during the (possibly slow) refill
                }
            }

            _largestItems = corrected;
            _largestRange = -_largestItems.Count;
            _feedCacheStore.Save(new PersistedFeed(
                chatId, _largestRange, _largestItems.Select(i => i.Audio).ToList()));

            bool visibleChanged = false;
            if (SelectedChat?.Id == chatId && SelectedRange <= 0)
            {
                // Showing this chat in count mode right now - re-slice from the
                // corrected (and possibly topped-up) superset, keeping however
                // many rows were already on screen.
                List<FeedItem> recut = _largestItems.Take(_items.Count).ToList();
                if (!recut.SequenceEqual(_items))
                {
                    _items = recut;
                    RebuildByFileId();
                    visibleChanged = true;
                }
            }
            else
            {
                // A different chat or a day window is on screen - no "refill to
                // N" concept applies there, just drop whatever was deleted.
                for (int idx = _items.Count - 1; idx >= 0; idx--)
                {
                    if (deletedSet.Contains(_items[idx].Audio.MessageId))
                    {
                        _byFileId.Remove(_items[idx].Audio.FileId);
                        _items.RemoveAt(idx);
                        visibleChanged = true;
                    }
                }
            }
            if (visibleChanged)
            {
                RenderList();
            }
        }
        finally
        {
            _correctingChatId = 0;
        }
    }

    /// <summary>Refresh button: reload the chat list and the feed (connect first if needed).</summary>
    private async Task RefreshAsync()
    {
        if (_connecting)
        {
            return;
        }
        if (!_connected)
        {
            await ConnectAsync();   // toasts internally (via ListChatsAndLoadAsync) on success
            return;
        }

        _connecting = true;
        try
        {
            Status(Loc.S("status.refreshing"));
            await RunWithRetryAsync("refresh", ListChatsAndLoadAsync);   // toasts internally too
        }
        catch (Exception ex)
        {
            _connected = false;
            Status(DescribeFailure("refresh", ex));
        }
        finally
        {
            _connecting = false;
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
                throw new OperationCanceledException(Loc.S("err.noCode"));
            });
            return Task.FromResult(code);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new OperationCanceledException(Loc.S("err.signinCancelled"));
        }
    }

    private int _passwordAttempts;   // reset per connect; a retry means the last one was wrong

    private Task<string> AskForPasswordAsync(string? hint)
    {
        try
        {
            string password = Invoke(() =>
            {
                bool retry = _passwordAttempts++ > 0;
                using var form = new CloudPasswordForm(hint, retry);
                if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(form.Password))
                {
                    return form.Password!;
                }
                throw new OperationCanceledException(Loc.S("err.noPassword"));
            });
            return Task.FromResult(password);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new OperationCanceledException(Loc.S("err.signinCancelled"));
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
        _feedDebounce.Stop();
        _feedDebounce.Start();   // fires ~350ms after the last chat/range change
    }

    /// <summary>Positive = day window; negative = "newest |n| audios".</summary>
    private int SelectedRange =>
        _rangeCombo.SelectedIndex >= 0 ? RangeDayOptions[_rangeCombo.SelectedIndex] : 7;

    /// <summary>Extensions the format combo is filtering to, or null for "all".</summary>
    private string[]? SelectedFormatExtensions
    {
        get
        {
            int i = _formatCombo.SelectedIndex - 1;   // 0 = "all"
            return i >= 0 && i < FormatOptions.Length ? FormatOptions[i].Ext : null;
        }
    }

    private TelegramChat? SelectedChat =>
        (_groupCombo.SelectedItem as ChatChoice)?.Chat;

    private async Task LoadFeedAsync()
    {
        if (SelectedChat is not { } chat)
        {
            return;
        }

        // Switching chat/range mid-load must not let the earlier (slower) load
        // win the render. Cancel the previous one and tag this with a sequence.
        _feedCts?.Cancel();
        _feedCts?.Dispose();
        CancellationToken token = (_feedCts = new CancellationTokenSource()).Token;
        int seq = ++_feedSeq;
        long chatId = chat.Id;

        // Don't leave a stale "N new" badge up while the (possibly different)
        // chat reloads - SetNewCount refreshes it when the load finishes.
        if (_newSinceLastVisit != 0)
        {
            _newSinceLastVisit = 0;
            _refreshButton.Invalidate();
        }
        int range = SelectedRange;

        // Switching to a chat we haven't touched yet this session picks up its
        // own persisted list (if any) - so revisiting an earlier chat is just
        // as cheap as restarting the app on the current one.
        EnsureLargestFor(chatId);

        // The newest message id this chat's list already reached before the
        // load. Anything above it that the load pulls in is genuinely new (not
        // just older history being fetched) and gets counted on the refresh
        // button. On startup this is the restored feed cache, so the count is
        // "arrived since you last closed the app".
        int highestKnown = (_largestItems.Count > _items.Count ? _largestItems : _items)
            .Select(i => i.Audio.MessageId)
            .DefaultIfEmpty(0)
            .Max();

        // While a feed load is running, its progress is more important than a
        // playback status update (e.g. "Playing: X") - PlaybackStatus defers
        // those until the load finishes instead of letting them overwrite it.
        _feedLoadingSeq = seq;
        void EndFeedLoading()
        {
            if (_feedLoadingSeq != seq)
            {
                return;   // a newer load has already taken over the "loading" spot
            }
            _feedLoadingSeq = 0;
            if (_deferredStatus is { } text)
            {
                _deferredStatus = null;
                Status(text);
            }
        }

        // Reuse the biggest count-mode list known for this chat - then only new
        // posts (and any extra older ones) are fetched, regardless of whether
        // what's currently on screen is smaller (a previous "shrink").
        //
        // But only when that list is actually large: the incremental fetch can
        // add newer/older messages, yet never notices one deleted from *inside*
        // the range it already covers. A short list (a bot chat, Saved Messages,
        // a small group) is re-fetched in full instead - a page or three, and it
        // drops anything that's gone. The shortcut is for the genuine "Newest
        // thousands" case where re-pulling everything would hurt.
        int wantCount = range < 0 ? -range : 0;
        bool worthReusing = _largestItems.Count >= wantCount
            || _largestItems.Count > IncrementalReuseFloor;
        List<FeedItem>? previousItems =
            range <= 0 && chatId == _largestChatId && _largestItems.Count > 0 && worthReusing
                ? _largestItems
                : null;
        IReadOnlyList<AudioMessage>? previous = previousItems?.Select(i => i.Audio).ToList();

        Status(previous is null ? Loc.S("status.loading") : Loc.S("status.checkingNew"));

        // Count mode has a fixed goal ("newest N"); a day window does not.
        int target = range < 0 ? -range : 0;
        // A Progress<int> callback is posted to the UI queue and the last one can
        // land just after RenderList - then it would overwrite the final count
        // with "Loading … N" again. Stop honouring it once we start rendering.
        bool rendering = false;
        var progress = new Progress<int>(n =>
        {
            if (seq != _feedSeq || rendering)
            {
                return;
            }
            Status(target > 0
                ? Loc.T("status.loadingProgress", Math.Min(n, target), target)
                : Loc.T("status.loadingCount", Loc.Files(n)));
        });

        // A large fresh "newest N" pull (or growing a reused list, e.g. Newest
        // 1000 -> 5000) can take a while - show rows as pages arrive (every
        // ~500) instead of only once the whole thing is done. Growing starts
        // from everything already known (previousItems - which can be bigger
        // than what's currently on screen, e.g. after an earlier shrink), not
        // from what's merely displayed and not from zero.
        List<FeedItem> partial = null!;
        int nextRenderAt = 0;
        void ResetPartial()
        {
            partial = previousItems is null ? new List<FeedItem>() : new List<FeedItem>(previousItems);
            nextRenderAt = partial.Count + 500;
        }
        ResetPartial();
        var onBatch = new Progress<IReadOnlyList<FeedItem>>(batch =>
        {
            if (seq != _feedSeq || rendering)
            {
                return;
            }
            partial.AddRange(batch);
            if (partial.Count < nextRenderAt)
            {
                return;
            }
            nextRenderAt += 500;
            _items = new List<FeedItem>(partial);
            RebuildByFileId();
            RenderList(announceStatus: false);   // the loading-progress status line stays as-is
        });

        List<FeedItem>? loaded = null;
        try
        {
            await RunWithRetryAsync("loading", async () =>
            {
                // A retry re-fetches from the start - reset so onBatch doesn't
                // append the previous, failed attempt's pages on top.
                ResetPartial();
                loaded = (await _feed.LoadAsync(chatId, range, token, progress, previous, onBatch)).ToList();
            });
        }
        catch (OperationCanceledException)
        {
            EndFeedLoading();
            return;   // superseded by a newer load
        }
        catch (Exception ex)
        {
            if (seq == _feedSeq)
            {
                Status(DescribeFailure("loading", ex));
            }
            EndFeedLoading();
            return;
        }

        if (seq != _feedSeq || token.IsCancellationRequested || loaded is null)
        {
            EndFeedLoading();
            return;   // a newer load started while this one ran
        }

        rendering = true;   // from here on, late progress callbacks must not talk
        _items = loaded;
        RebuildByFileId();

        RenderList(afterLoad: true);
        EndFeedLoading();

        int newCount = highestKnown == 0
            ? 0   // no prior list for this chat - nothing is "new" yet
            : loaded.Count(i => i.Audio.MessageId > highestKnown);
        SetNewCount(newCount);
        Toast(newCount > 0
            ? Loc.T("toast.listUpdatedNew", newCount, Loc.Files(_items.Count))
            : Loc.T("toast.listUpdated", Loc.Files(_items.Count)));

        // So a restart (or switching back to this chat) can show the list
        // instantly and only fetch what's changed, instead of re-pulling e.g.
        // "Newest 5000" from scratch. Count mode: fold into the running
        // superset (a smaller N here must not shrink what's remembered) and
        // persist that.
        if (range <= 0)
        {
            _largestChatId = chatId;
            // A full re-fetch (previousItems null) is the authoritative current
            // list - replace, so a deleted track can't linger. An incremental
            // grow only added to previousItems, so union it in (a smaller N here
            // must not shrink the remembered superset).
            _largestItems = previousItems is null
                ? loaded
                : MergeLargest(_largestItems, loaded);
            _largestRange = -_largestItems.Count;
            _feedCacheStore.Save(new PersistedFeed(
                chatId, _largestRange, _largestItems.Select(i => i.Audio).ToList()));
        }
        else if (_largestChatId == chatId && _largestItems.Count > 0)
        {
            // Day mode has no "superset" of its own - but this chat already
            // has a count-mode superset in memory, and it's always a bigger,
            // strictly better reuse/restart basis than this narrow day
            // window. Keep persisting THAT instead of overwriting it with
            // the day window (which used to silently throw the superset away
            // on disk - the in-memory copy survived, but a later restart, or
            // switching chats and back, would have restored the tiny one).
            _feedCacheStore.Save(new PersistedFeed(
                chatId, _largestRange, _largestItems.Select(i => i.Audio).ToList()));
        }
        else
        {
            _feedCacheStore.Save(new PersistedFeed(chatId, range, _items.Select(i => i.Audio).ToList()));
        }
    }

    /// <summary>
    /// Switches the "biggest known list" bookkeeping to <paramref name="chatId"/>
    /// if it isn't already there - restoring its persisted list (if any), so
    /// revisiting a chat this session (or on the next app start) reuses it
    /// instead of starting from nothing.
    /// </summary>
    private void EnsureLargestFor(long chatId)
    {
        if (chatId == _largestChatId)
        {
            return;
        }

        PersistedFeed? persisted = _feedCacheStore.Load(chatId);
        _largestChatId = chatId;
        _largestItems = persisted is { Audios.Count: > 0 }
            ? _feed.Restore(persisted.Audios).ToList()
            : new List<FeedItem>();
    }

    /// <summary>
    /// Merges <paramref name="incoming"/> into <paramref name="existing"/> by
    /// <see cref="AudioMessage.MessageId"/> (incoming wins on a conflict - it's
    /// the freshest), newest first, capped at <see cref="MaxLargestAudios"/>.
    /// </summary>
    private static List<FeedItem> MergeLargest(List<FeedItem> existing, List<FeedItem> incoming)
    {
        var byId = new Dictionary<int, FeedItem>(existing.Count + incoming.Count);
        foreach (FeedItem item in existing)
        {
            byId[item.Audio.MessageId] = item;
        }
        foreach (FeedItem item in incoming)
        {
            byId[item.Audio.MessageId] = item;
        }

        return byId.Values
            .OrderByDescending(i => i.Audio.DateUtc)
            .ThenByDescending(i => i.Audio.MessageId)
            .Take(MaxLargestAudios)
            .ToList();
    }

    /// <summary>Add a column: pass <paramref name="fill"/> for a stretchy column, or <paramref name="width"/> for a fixed one.</summary>
    private const int CachedColumnIndex = 5;

    private void AddCachedColumn()
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = "",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Width = 26,
            MinimumWidth = 26,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            Resizable = DataGridViewTriState.False,
            ToolTipText = Loc.S("col.cached"),
        };
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        // The ✓ is white on a plain or selected row (SelectionForeColor keeps it
        // white under the selection bar); on the player-tinted row it goes blue
        // with the rest of that row via OnCellFormatting.
        column.DefaultCellStyle.ForeColor = Color.White;
        column.DefaultCellStyle.SelectionForeColor = Color.White;
        _list.Columns.Add(column);
    }

    private void AddColumn(string header, int fill = 0, int width = 0, bool alignRight = false)
    {
        bool isFill = fill > 0;
        var column = new DataGridViewTextBoxColumn
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
        };
        if (alignRight)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.DefaultCellStyle.Padding = new Padding(0, 0, 8, 0);
        }
        _list.Columns.Add(column);
    }

    // Tint for the row of the track in the player (playing / paused), and on a
    // fresh launch the last track from ui-state - a muted blue so it stands out
    // without looking selected.
    private static readonly Color PlayingRowBack = Color.FromArgb(26, 52, 78);
    private static readonly Color PlayingRowFore = Color.FromArgb(156, 198, 242);

    private long? _tintedFileId;   // which row currently carries the playing tint

    private long? PlayingMark =>
        _currentFileId ?? (_lastTrackId != 0 ? _lastTrackId : (long?)null);

    /// <summary>
    /// Moves the muted-blue tint to the current / last-played track's row.
    /// Only touches the two rows that change, not the whole (possibly huge) list.
    /// </summary>
    private void HighlightPlayingRow()
    {
        long? mark = PlayingMark;
        if (mark == _tintedFileId)
        {
            return;
        }

        long? previous = _tintedFileId;
        _tintedFileId = mark;   // OnCellFormatting reads PlayingMark; just repaint the two rows
        InvalidateRowFor(previous);
        InvalidateRowFor(mark);
    }

    private void InvalidateRowFor(long? fileId)
    {
        if (fileId is not long f)
        {
            return;
        }
        int idx = _view.FindIndex(i => i.Audio.FileId == f);
        if (idx >= 0)
        {
            _list.InvalidateRow(idx);
        }
    }

    /// <summary>Reindexes <see cref="_byFileId"/> from the current <see cref="_items"/>.</summary>
    private void RebuildByFileId()
    {
        _byFileId.Clear();
        foreach (FeedItem item in _items)
        {
            _byFileId[item.Audio.FileId] = item.Audio;
        }
    }

    // VirtualMode: the grid asks for each visible cell's text here.
    private void OnCellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count)
        {
            return;
        }

        AudioMessage a = _view[e.RowIndex].Audio;
        e.Value = e.ColumnIndex switch
        {
            0 => AppDateFormatter.Format(a.DateUtc.ToLocalTime()),
            1 => a.Title,
            2 => a.Performer,
            3 => a.Duration is { } d ? TrackFormat.Duration(d) : "–",
            4 => TrackFormat.SizeMb(a.SizeBytes),
            CachedColumnIndex => _cachedIds.Contains(a.FileId) ? "✓" : "",
            _ => "",
        };
    }

    // The muted-blue tint for the track sitting in the player. VirtualMode shares
    // one row object, so the tint can't live on a row style - it's re-applied per
    // paint here, and HighlightPlayingRow just invalidates the rows that change.
    private void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _view.Count || e.CellStyle is null)
        {
            return;
        }

        if (PlayingMark is long m && _view[e.RowIndex].Audio.FileId == m)
        {
            e.CellStyle.BackColor = PlayingRowBack;
            e.CellStyle.ForeColor = PlayingRowFore;   // whole row, ✓ included, goes blue
        }
    }

    /// <param name="afterLoad">The render that ends a feed load - the status line then reads "… finished".</param>
    /// <param name="announceStatus">
    /// False for a progressive render mid-load: the loading-progress line owns
    /// the status, and the "land on the kept row" request is kept for the final render.
    /// </param>
    private void RenderList(bool afterLoad = false, bool announceStatus = true)
    {
        DismissHangingCellTooltip();

        _view = BuildView();
        _cachedIds = _cache.CachedFileIds();

        _suppressListEvents = true;
        _list.SuspendLayout();
        int scrollBefore = Math.Max(0, _list.FirstDisplayedScrollingRowIndex);
        _tintedFileId = PlayingMark;

        // VirtualMode: just tell the grid the row count and let it pull the ~30
        // visible cells from _view. Drop CurrentCell first so shrinking the
        // count past the old selection can't throw.
        try { _list.CurrentCell = null; } catch { }
        _list.RowCount = _view.Count;
        _list.ClearSelection();
        _list.Invalidate();   // re-fetch every visible value

        int keptIdx = RestoreKeptSelection();
        _list.ResumeLayout();

        PositionViewport(keptIdx, scrollBefore, settled: announceStatus);

        _suppressListEvents = false;
        ShowSelected();   // the playing-row tint comes from OnCellFormatting

        if (announceStatus)
        {
            AnnounceListStatus(afterLoad);
        }
    }

    // Dismiss a truncation tooltip still showing over a row we're about to
    // remove - WinForms would otherwise leave it hanging.
    private void DismissHangingCellTooltip()
    {
        if (_list.ShowCellToolTips)
        {
            _list.ShowCellToolTips = false;
            _list.ShowCellToolTips = true;
        }
    }

    /// <summary><see cref="_items"/> after the format + text filter and the column sort.</summary>
    private List<FeedItem> BuildView()
    {
        string[] queryTerms = TrackSearch.Terms(_searchBox.Text);
        string[]? formatExt = SelectedFormatExtensions;
        return ApplySort(_items
            .Where(i => (queryTerms.Length == 0
                         || TrackSearch.Matches($"{i.Audio.Performer} {i.Audio.Title} {i.Audio.FileName}", queryTerms))
                     && (formatExt is null || formatExt.Contains(FileExtension(i.Audio)))))
            .ToList();
    }

    // Re-select the row of the track that's selected / playing / was last played
    // (ui-state on a fresh launch), if it survived the filter. Returns its index
    // in _view, or -1.
    private int RestoreKeptSelection()
    {
        long? keep = _selectedFileId ?? _currentFileId
            ?? (_lastTrackId != 0 ? _lastTrackId : (long?)null);
        if (keep is not long fid)
        {
            return -1;
        }

        int idx = _view.FindIndex(i => i.Audio.FileId == fid);
        if (idx >= 0)
        {
            _list.CurrentCell = _list.Rows[idx].Cells[0];
            _list.Rows[idx].Selected = true;
        }
        return idx;
    }

    // Place the viewport after a rebuild. Setting CurrentCell in
    // RestoreKeptSelection already scrolled the kept row into view, so even
    // "keep the offset" has to actively override that.
    private void PositionViewport(int keptIdx, int scrollBefore, bool settled)
    {
        if (_list.RowCount > 0)
        {
            int target = _nextScroll switch
            {
                NextScroll.Top => 0,
                NextScroll.KeptRow => Math.Max(0, keptIdx),
                _ => Math.Min(scrollBefore, _list.RowCount - 1),
            };
            try { _list.FirstDisplayedScrollingRowIndex = target; }
            catch { /* not scrollable yet */ }
        }

        // A one-shot request: consumed on the first settled render, but kept
        // through the progressive renders of a still-loading list.
        if (settled || _nextScroll == NextScroll.Top)
        {
            _nextScroll = NextScroll.KeepOffset;
        }
    }

    private void AnnounceListStatus(bool afterLoad)
    {
        if (_items.Count == 0)
        {
            Status(_connected ? Loc.S("status.noAudioRange") : Loc.S("status.notSignedIn"));
        }
        else if (_view.Count == _items.Count)
        {
            Status(afterLoad ? Loc.T("status.loadingFinished", Loc.Files(_items.Count)) : Loc.Files(_items.Count));
        }
        else
        {
            Status(Loc.T("status.filtered", _view.Count, Loc.Files(_items.Count)));
        }
    }

    private static string FileExtension(AudioMessage a) =>
        System.IO.Path.GetExtension(a.FileName).ToLowerInvariant();

    // Windows' Media Foundation has no decoder for these - they can be saved
    // ("Save a copy") but not played in-app.
    private static readonly string[] SaveOnlyExtensions = { ".ogg", ".opus" };
    private static bool CanPlay(AudioMessage a) =>
        !SaveOnlyExtensions.Contains(FileExtension(a));

    // ---------- playback ----------
    private AudioMessage? SelectedAudio =>
        _list.SelectedRows.Count > 0
        && _list.SelectedRows[0].Index is int i
        && i >= 0 && i < _view.Count
            ? _view[i].Audio
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

        // Only reached on a genuine user selection (a re-render's own selection
        // restore runs under _suppressListEvents above) - acknowledge the "N
        // new" badge the moment the user actually looks at the list, not only
        // when the next refresh happens to reset it.
        if (_newSinceLastVisit > 0)
        {
            SetNewCount(0);
        }

        if (SelectedAudio is not { } audio)
        {
            _selectedFileId = null;
            _player.SetIdle();
            return;
        }

        _selectedFileId = audio.FileId;
        bool cached = _cache.Contains(audio);
        // "Save a copy" downloads an un-cached file on demand, so it's usable
        // whenever the track is cached or we're still connected - this is the
        // only way to get a save-only format (ogg/opus) out, since Play can't.
        bool canSave = cached || _connected;

        if (_pendingFileId == audio.FileId)
        {
            _player.ShowTrack(audio, PlayerButton.Cancel, canSave: false);
            _player.SetDownloadProgress(_lastProgress);
        }
        else if (_currentFileId == audio.FileId)
        {
            bool playing = _audio.State == PlaybackState.Playing;
            _player.ShowTrack(audio, playing ? PlayerButton.Pause : PlayerButton.Play, canSave);
            _player.SetLoaded(_audio.Duration, _audio.BitrateKbps);
            _player.SetPosition(_audio.Position);
        }
        else
        {
            _player.ShowTrack(audio, PlayerButton.Play, canSave);
            PrefetchDocument(audio, cached);
        }
    }

    /// <summary>
    /// While the user looks at a freshly-selected, not-yet-cached row, quietly
    /// fetch its Telegram document in the background - so pressing Play doesn't
    /// then pay a round-trip first (older tracks restored from the on-disk feed
    /// cache have no document in memory). Best-effort; a new selection cancels
    /// the previous warm-up.
    /// </summary>
    private void PrefetchDocument(AudioMessage audio, bool cached)
    {
        if (cached || !_connected)
        {
            return;
        }

        _prefetchCts?.Cancel();
        _prefetchCts?.Dispose();
        _prefetchCts = new CancellationTokenSource();
        CancellationToken token = _prefetchCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await _telegram.PrefetchAsync(audio, token);
            }
            catch
            {
                // warm-up only - the download re-fetches if this didn't land
            }
        });
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

        if (!CanPlay(audio))
        {
            Toast(Loc.T("status.saveOnly", FileExtension(audio).TrimStart('.').ToUpperInvariant()));
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
            AppLog.Error("Audio", $"Load failed for {audio.FileName}: {ex}");
            PlaybackStatus(Loc.T("status.cannotPlay", Path.GetExtension(audio.FileName), ex.Message));
            return;
        }

        _currentFileId = audio.FileId;
        _lastTrackId = audio.FileId;
        BackfillDuration(audio.FileId, _audio.Duration);
        HighlightPlayingRow();
        SaveUiState();   // remember this track for the next launch
        _player.SetLoaded(_audio.Duration, _audio.BitrateKbps);
        _audio.Play();
        _player.SetButton(PlayerButton.Pause);
        _positionTimer.Start();
        PlaybackStatus(Loc.T("status.playing", audio.DisplayName));
    }

    /// <summary>
    /// The little red count on the refresh button: how many tracks the last
    /// load pulled in that the user hadn't seen ("new since last visit"). Drawn
    /// as a capsule sized to the text so a two-digit count isn't clipped.
    /// </summary>
    private static void DrawNewBadge(Graphics g, Rectangle bounds, int count)
    {
        string text = count > 99 ? "99+" : count.ToString();
        const int h = 13;

        var oldSmoothing = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var font = new Font("Segoe UI", 6.5f, FontStyle.Bold);
        int textWidth = (int)Math.Ceiling(g.MeasureString(text, font).Width);
        int w = Math.Max(h, textWidth + 4);
        // Top-right corner, kept inside the button - the Paint clip would cut off
        // anything drawn past the edge.
        var badge = new Rectangle(bounds.Right - w, bounds.Top, w, h);

        using (var fill = new SolidBrush(UIStyles.Colors.RedLight))
        {
            g.FillEllipse(fill, badge.Left, badge.Top, h, h);
            g.FillEllipse(fill, badge.Right - h, badge.Top, h, h);
            g.FillRectangle(fill, badge.Left + h / 2, badge.Top, badge.Width - h, h);
        }

        using (var sf = new StringFormat
               {
                   Alignment = StringAlignment.Center,
                   LineAlignment = StringAlignment.Center,
                   FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
                   Trimming = StringTrimming.None,
               })
        {
            g.DrawString(text, font, Brushes.White, badge, sf);
        }

        g.SmoothingMode = oldSmoothing;
    }

    private void SetNewCount(int count)
    {
        _newSinceLastVisit = count;
        _refreshButton.Invalidate();
        UIStyles.Buttons.UpdateTooltip(_refreshButton, count > 0
            ? Loc.T("top.refresh.tipNew", count)
            : Loc.S("top.refresh.tip"));
    }

    // Drag a cached track's file out onto Explorer / a DAW / a chat app.
    // Only cached rows are draggable - the FileDrop format needs a real path
    // (the "downloaded" check column shows which rows qualify).
    private void OnListMouseDown(object? sender, MouseEventArgs e)
    {
        _dragFileId = null;
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var hit = _list.HitTest(e.X, e.Y);
        if (hit.RowIndex < 0
            || hit.RowIndex >= _view.Count
            || !File.Exists(_cache.GetPath(_view[hit.RowIndex].Audio)))
        {
            return;
        }

        _dragFileId = _view[hit.RowIndex].Audio.FileId;
        Size ds = SystemInformation.DragSize;
        _dragBox = new Rectangle(e.X - ds.Width / 2, e.Y - ds.Height / 2, ds.Width, ds.Height);
    }

    private void OnListMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragFileId is not long fid
            || e.Button != MouseButtons.Left
            || _dragBox.Contains(e.X, e.Y))
        {
            return;
        }

        _dragFileId = null;
        if (!_byFileId.TryGetValue(fid, out AudioMessage? a))
        {
            return;
        }

        string cachePath = _cache.GetPath(a);
        if (!File.Exists(cachePath))
        {
            return;
        }

        // The cache file is named "<FileId>__<name>.<ext>" - dropping that
        // straight out leaves the numeric prefix on the copy. Drag a copy under
        // the clean file name instead, same one "Save a copy" uses.
        string dragPath = cachePath;
        try
        {
            dragPath = PrepareDragFile(a, cachePath);
        }
        catch (Exception ex)
        {
            AppLog.Line("Drag", $"could not stage a clean copy of {a.FileName}: {ex.Message}");
        }

        _list.DoDragDrop(
            new DataObject(DataFormats.FileDrop, new[] { dragPath }),
            DragDropEffects.Copy);
    }

    // Copies under %TEMP%\ViewerForTelegram\drag so a drag-out lands with a
    // human file name. Refreshed per drag; the folder is wiped on close.
    private static readonly string DragTempDir =
        Path.Combine(Path.GetTempPath(), "ViewerForTelegram", "drag");

    private static string PrepareDragFile(AudioMessage a, string cachePath)
    {
        string name = CleanFileName(a);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"{a.FileId}{Path.GetExtension(cachePath)}";
        }

        Directory.CreateDirectory(DragTempDir);
        string dest = Path.Combine(DragTempDir, name);
        if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(cachePath).Length)
        {
            File.Copy(cachePath, dest, overwrite: true);
        }
        return dest;
    }

    /// <summary>The track's own file name, stripped of characters Windows rejects.</summary>
    private static string CleanFileName(AudioMessage a) =>
        string.Join("_", a.FileName.Split(Path.GetInvalidFileNameChars()));

    private void OnColumnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.ColumnIndex is < 0 or > 4)
        {
            return;   // Cached column and out-of-range: not sortable
        }

        // Three-state cycle per column: ascending -> descending -> off (feed order).
        if (_sortColumn != e.ColumnIndex)
        {
            _sortColumn = e.ColumnIndex;
            _sortAscending = true;
        }
        else if (_sortAscending)
        {
            _sortAscending = false;
        }
        else
        {
            _sortColumn = -1;
        }

        for (int c = 0; c <= 4; c++)
        {
            _list.Columns[c].HeaderCell.SortGlyphDirection =
                c == _sortColumn
                    ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending)
                    : SortOrder.None;
        }

        _nextScroll = NextScroll.Top;
        RenderList();
    }

    private IEnumerable<FeedItem> ApplySort(IEnumerable<FeedItem> items)
    {
        if (_sortColumn < 0)
        {
            return items;   // feed order, newest first
        }

        Func<FeedItem, object> key = _sortColumn switch
        {
            0 => i => i.Audio.DateUtc,
            1 => i => i.Audio.Title ?? "",
            2 => i => i.Audio.Performer ?? "",
            3 => i => i.Audio.Duration ?? TimeSpan.Zero,
            _ => i => i.Audio.SizeBytes,
        };

        IComparer<object> comparer = _sortColumn is 1 or 2
            ? Comparer<object>.Create((a, b) =>
                string.Compare((string)a, (string)b, StringComparison.CurrentCultureIgnoreCase))
            : Comparer<object>.Default;

        return _sortAscending
            ? items.OrderBy(key, comparer)
            : items.OrderByDescending(key, comparer);
    }

    /// <summary>
    /// Telegram sometimes doesn't report a track's length (posted "as a file").
    /// Once the audio engine has decoded it we know the real duration - write it
    /// back into the model, the list and the cache so it survives a reload.
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
        _cache.RememberDuration(updated, duration);

        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].Audio.FileId == fileId)
            {
                _items[i] = _items[i] with { Audio = updated };
                break;
            }
        }

        int idx = _view.FindIndex(i => i.Audio.FileId == fileId);
        if (idx >= 0)
        {
            _view[idx] = _view[idx] with { Audio = updated };
            _list.InvalidateRow(idx);
        }
    }

    /// <summary>
    /// Re-read which files are in the cache and repaint the "downloaded" check
    /// column, without a full re-render. Run after a download (which may have
    /// pruned the oldest file to stay under the size limit) and after the cache
    /// is cleared from Settings.
    /// </summary>
    private void RefreshCachedColumn()
    {
        _cachedIds = _cache.CachedFileIds();
        _list.InvalidateColumn(CachedColumnIndex);
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
        PlaybackStatus(Loc.T("status.loadingFile", audio.DisplayName));

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
                PlaybackStatus(Loc.S("status.cancelled"));
                ShowSelected();
            }
            return;
        }
        catch (Exception ex)
        {
            if (seq == _playSeq)
            {
                _pendingFileId = 0;
                PlaybackStatus(Loc.T("status.downloadFailed", ex.Message));
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
        RefreshCachedColumn();

        StopCurrent();
        try
        {
            _audio.Load(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("Audio", $"Load failed for {audio.FileName}: {ex}");
            PlaybackStatus(Loc.T("status.cannotPlayElsewhere", Path.GetExtension(audio.FileName), ex.Message));
            ShowSelected();
            return;
        }

        _currentFileId = audio.FileId;
        _lastTrackId = audio.FileId;
        BackfillDuration(audio.FileId, _audio.Duration);
        HighlightPlayingRow();
        SaveUiState();   // remember this track for the next launch
        _audio.Play();
        _positionTimer.Start();
        if (_selectedFileId == audio.FileId)
        {
            _player.SetLoaded(_audio.Duration, _audio.BitrateKbps);
            _player.SetButton(PlayerButton.Pause);
        }
        PlaybackStatus(Loc.T("status.playing", audio.DisplayName));
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
            IoUtil.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            PlaybackStatus(Loc.T("status.cannotOpenFolder", ex.Message));
        }
    }

    private void StopCurrent()
    {
        _positionTimer.Stop();
        _audio.Stop();
        _currentFileId = null;
        HighlightPlayingRow();
    }

    private async void SaveSelected()
    {
        if (SelectedAudio is not { } audio)
        {
            return;
        }

        // Not cached yet - fetch it first (works for the save-only formats too,
        // which can never be cached by playing them).
        if (!_cache.Contains(audio))
        {
            _pendingFileId = audio.FileId;
            PlaybackStatus(Loc.T("status.loadingFile", audio.DisplayName));
            var dlProgress = new Progress<int>(p =>
            {
                if (_pendingFileId == audio.FileId)
                {
                    PlaybackStatus(Loc.T("status.loadingFileProgress", audio.DisplayName, p));
                }
            });
            try
            {
                await _downloader.EnsureLocalAsync(audio, dlProgress, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _pendingFileId = 0;
                AppLog.Error("Download", $"{audio.FileName}: {ex.Message}");
                Toast(Loc.T("status.downloadFailed", ex.Message));
                return;
            }
            _pendingFileId = 0;
            PushCacheInfo();
            RefreshCachedColumn();
            if (!_cache.Contains(audio))
            {
                return;
            }
        }

        string source = _cache.GetPath(audio);
        string name = CleanFileName(audio);
        TelegramConfig cfg = _configStore.Load();

        try
        {
            if (cfg.UseDownloadFolder && Directory.Exists(cfg.EffectiveDownloadFolder))
            {
                File.Copy(source, Path.Combine(cfg.EffectiveDownloadFolder, name), overwrite: true);
                Toast(Loc.T("toast.downloaded", name));
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
                Toast(Loc.T("toast.downloaded", Path.GetFileName(dlg.FileName)));
            }
            // dialog cancelled -> nothing happened, no toast
        }
        catch (Exception ex)
        {
            AppLog.Error("Download", $"{name}: {ex.Message}");
            Toast(Loc.T("status.downloadFailed", ex.Message));
        }
    }

    /// <summary>Bottom-of-player status line plus a brief pop-up toast.</summary>
    private void Toast(string message)
    {
        Status(message);
        ToastForm.ShowToast(message, this);
    }

    // ---------- settings ----------
    private async Task OpenSettingsAsync(bool isStartup)
    {
        while (true)
        {
            TelegramConfig before = _configStore.Load();

            using var dlg = new SettingsForm(before, _cache, _connected);

            if (_pendingUpdateResult != null)
            {
                Version current = typeof(MainForm).Assembly.GetName().Version ?? new Version(1, 0, 0);
                Button updateButton = _updateAppUpdater.CreateUpdateAvailableButton(_pendingUpdateResult, current, dlg);
                dlg.VersionStrip.Controls.Add(updateButton);
                // The strip flows right-to-left (version label added first sits
                // at the far right) - index 0 puts the button right of it.
                dlg.VersionStrip.Controls.SetChildIndex(updateButton, 0);
                // Tuck the button into the corner (the strip's own right inset
                // is meant for the lone version label).
                dlg.VersionStrip.Padding = new Padding(0, 0, 4, 0);
            }

            dlg.ShowDialog(this);
            PushCacheInfo();

            if (dlg.CacheCleared)
            {
                // The ✓ column and the player's play/download state both read the
                // cache - refresh them now instead of waiting for the next render.
                RefreshCachedColumn();
                ShowSelected();
            }

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
                    Loc.S("msg.credsIncomplete.body"),
                    Loc.S("msg.credsIncomplete.title"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this);

                if (r == DialogResult.Yes)
                {
                    continue;
                }
                Status(Loc.S("status.notSignedIn"));
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
                Status(Loc.S("status.notSignedIn"));
            }
            return;
        }
    }

    private async Task LogoutAsync(bool wipeConfig)
    {
        _playCts?.Cancel();
        _feedCts?.Cancel();
        _prefetchCts?.Cancel();
        _downloader.CancelAll();
        _playSeq++;              // abandon any in-flight PlayAsync
        _feedSeq++;              // and any in-flight feed load
        _pendingFileId = 0;
        StopCurrent();

        await _telegram.DisposeAsync();
        IoUtil.TryDelete(AppPaths.SessionFile);
        if (wipeConfig)
        {
            IoUtil.TryDelete(AppPaths.ConfigFile);
            IoUtil.TryDelete(AppPaths.FeedCacheFile);
            IoUtil.TryDelete(AppPaths.UiStateFile);
            IoUtil.TryDelete(AppPaths.DurationsFile);
            _cache.Clear();
        }

        _connected = false;
        _chats.Clear();
        _items = new();
        _largestChatId = 0;
        _largestItems = new();
        _byFileId.Clear();
        _suppressComboEvents = true;
        _groupCombo.Items.Clear();
        _suppressComboEvents = false;
        RefreshGroupComboHints();
        _suppressListEvents = true;
        try { _list.CurrentCell = null; } catch { }
        _view = new();
        _list.RowCount = 0;
        _suppressListEvents = false;
        _selectedFileId = null;
        _player.SetIdle();
        Toast(wipeConfig ? Loc.S("status.credentialsDeleted") : Loc.S("status.signedOut"));
    }

    // ---------- helpers ----------

    /// <summary>Cache size at which the top-bar label turns red as a warning.</summary>
    private const long CacheWarnBytes = 2560L * 1024 * 1024;   // 2.5 GB

    private void PushCacheInfo()
    {
        (int count, long bytes) = _cache.GetStats();
        string size = bytes >= 1024L * 1024 * 1024
            ? $"{bytes / 1024d / 1024d / 1024d:0.0} GB"
            : $"{bytes / 1024d / 1024d:0} MB";
        long limitMb = CachePolicy.LimitBytes / 1024 / 1024;
        _cacheLabel.Text = Loc.T("cache.label", size, count);
        _cacheLabel.ForeColor = bytes > CacheWarnBytes
            ? UIStyles.Colors.RedLight
            : UIStyles.Colors.TextMuted;
        _toolTip.SetToolTip(_cacheLabel, Loc.T("cache.tip", size, limitMb, count));
    }

    private void SaveUiState()
    {
        _uiStateStore.Save(new UiState(
            LastChatId: SelectedChat?.Id ?? 0,
            RangeDays: SelectedRange,
            VolumePercent: (int)Math.Round(_player.Volume * 100),
            LastPlayedFileId: _lastTrackId,
            FormatFilter: _formatCombo.SelectedIndex >= 1
                ? FormatOptions[_formatCombo.SelectedIndex - 1].Key
                : ""));
    }

    private void Status(string text) => _player.SetStatus(text);

    /// <summary>
    /// Status update from playback/download - shown right away, unless a feed
    /// load is currently running (its progress matters more); then it's held
    /// back and shown as soon as that load finishes.
    /// </summary>
    private void PlaybackStatus(string text)
    {
        if (_feedLoadingSeq != 0)
        {
            _deferredStatus = text;
        }
        else
        {
            Status(text);
        }
    }

    private sealed record ChatChoice(TelegramChat Chat)
    {
        public override string ToString() => Chat.Kind switch
        {
            TelegramChatKind.SavedMessages => Loc.S("chat.saved"),
            TelegramChatKind.Bot => $"[{Loc.S("chat.bot")}] {Chat.Title}",
            TelegramChatKind.Channel => $"[{Loc.S("chat.channel")}] {Chat.Title}",
            _ => $"[{Loc.S("chat.group")}] {Chat.Title}",
        };
    }
}

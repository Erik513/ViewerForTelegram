using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Helpers;
using ErikwnkWFUI.Styles;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.UI.Localization;
using Panel = System.Windows.Forms.Panel;
using TableLayoutPanel = System.Windows.Forms.TableLayoutPanel;
using TrackBar = ErikwnkWFUI.Controls.TrackBar;

namespace ViewerForTelegram.UI.Controls;

/// <summary>What the single main button currently does.</summary>
public enum PlayerButton
{
    None,     // nothing selected - button disabled
    Cancel,   // a download is running - click to abort
    Play,     // not playing (whether or not it's downloaded yet)
    Pause     // currently playing
}

/// <summary>
/// The player at the bottom of the main window. The big button on the left
/// plays / pauses / cancels the selected track (a download starts on the first
/// press). Top-right: a compact cluster with a save-to-disk button (its tooltip
/// is the file name), the size / format, and a button to open the download
/// folder. Seek and volume are on their own row.
/// </summary>
public sealed class PlayerPanel : Panel
{
    /// <summary>Fixed height the host should give this panel.</summary>
    public const int PanelHeight = 124;

    private const int StatusRowHeight = 18;

    private Button _mainButton;   // whichever of the three below is shown for the state
    private readonly Button _playButton;
    private readonly Button _pauseButton;
    private readonly Button _cancelButton;
    private readonly TableLayoutPanel _leftColumn;
    private readonly Button _saveButton;
    private readonly Button _browseButton;
    private readonly TextBox _title;
    private readonly TextBox _performer;
    private readonly Label _fileSize;
    private readonly Label _bitrate;
    private readonly Label _fileFormat;
    private readonly TrackBar _seek;
    private readonly SlimProgressBar _downloadBar;
    private readonly Spinner _spinner;
    private readonly TableLayoutPanel _seekRow;
    private readonly Label _time;
    private readonly VolumeControl _volume;   // mute button + slider, coupled to the player (BindVolume)
    private readonly TextBox _status;
    private string? _saveTipFileName;   // file name currently shown on the save button's tooltip
    private readonly ToolTip _tips = AppSurfaces.CreateToolTip(autoPopMs: 20000);

    private TimeSpan _duration;
    private long _sizeBytes;
    private bool _showingDownloadBar;
    private PlayerButton _state = PlayerButton.None;

    public PlayerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = PanelHeight;
        Padding = new Padding(12, 5, 14, 3);
        AppSurfaces.Follow(this, () => BackColor = AppSurfaces.Bar);
        _tips.ReviveOnFormActivate(this);

        // One library preset per state; SetButton swaps the one that is shown.
        _playButton = MakeMainButton(UIStyles.Buttons.Presets.Play.WithColors(UIStyles.Colors.Sets.Primary));
        _pauseButton = MakeMainButton(UIStyles.Buttons.Presets.Pause.WithColors(UIStyles.Colors.Sets.Primary));
        _cancelButton = MakeMainButton(UIStyles.Buttons.Presets.Cancel);
        _mainButton = _playButton;
        _playButton.Enabled = false;   // nothing selected yet

        // No factory tooltip - the tooltip (with the file name) is managed
        // through _tips only, so the button doesn't end up with two.
        _saveButton = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Download, "", new Size(34, 34));
        _saveButton.Enabled = false;
        _saveButton.Anchor = AnchorStyles.None;
        _saveButton.Click += (_, _) => Save?.Invoke();

        // The library's yellow browse button; it draws its folder symbol itself.
        _browseButton = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Browse, Loc.S("player.tip.browse"), new Size(34, 34));
        _browseButton.Anchor = AnchorStyles.None;
        _browseButton.Click += (_, _) => BrowseFolder?.Invoke();

        // Title / artist as read-only text boxes (white) so they can be selected
        // and copied.
        _title = MakeReadonlyText(bold: true);
        _title.Text = Loc.S("player.nothingSelected");
        _performer = MakeReadonlyText(bold: false);

        _fileSize = MakeLabel(UIStyles.Labels.CreateMuted(""));
        _fileSize.TextAlign = ContentAlignment.MiddleRight;
        _bitrate = MakeLabel(UIStyles.Labels.CreateMuted(""));
        _bitrate.TextAlign = ContentAlignment.MiddleRight;
        _fileFormat = MakeLabel(UIStyles.Labels.CreateMuted(""));
        _fileFormat.TextAlign = ContentAlignment.MiddleRight;

        _seek = UIStyles.TrackBars.CreatePrimary();
        _seek.Enabled = false;
        _seek.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _seek.Margin = new Padding(0);
        _seek.ValueChanged += (_, _) =>
        {
            if (_seek.Enabled)
            {
                Seek?.Invoke(_seek.Value);
            }
        };

        // Shown in place of the seek bar while a download runs - red→yellow→green.
        // Same left/right inset as the seek track so the two line up exactly.
        _downloadBar = UIStyles.SlimProgressBars.CreateStatus();
        _downloadBar.BackColor = UIStyles.Colors.BorderMedium;
        _downloadBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _downloadBar.Margin = new Padding(TrackBar.TrackInset, 0, TrackBar.TrackInset, 0);

        // Sits just right of the seek bar and spins for the whole time a track
        // is loading (download + decode) - i.e. exactly while the download bar
        // is shown in place of the seek bar.
        // Status colours (red→yellow→green) to match the download bar it stands
        // in for; the percentage shows in the centre.
        _spinner = UIStyles.Spinners.CreateProgressStatus(34, 3);
        _spinner.Anchor = AnchorStyles.None;
        _spinner.Margin = new Padding(0);
        _spinner.Visible = false;

        _time = MakeLabel(UIStyles.Labels.CreateMuted("–:– / –:–"));
        _time.TextAlign = ContentAlignment.MiddleCenter;

        _volume = UIStyles.VolumeControls.CreatePrimary(0.1, new Size(150, 26));
        _volume.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _volume.Margin = new Padding(8, 0, 0, 0);
        _volume.VolumeChanged += (_, _) => VolumeChanged?.Invoke(this, EventArgs.Empty);

        // Top-right cluster: size / bitrate / format stacked, then [save][open folder]
        var fileInfoStack = UIStyles.TableLayoutPanels.CreateTransparent(1, 3);
        fileInfoStack.Dock = DockStyle.Fill;
        fileInfoStack.Margin = new Padding(0);
        fileInfoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        fileInfoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        fileInfoStack.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        fileInfoStack.Controls.Add(_fileSize, 0, 0);
        fileInfoStack.Controls.Add(_bitrate, 0, 1);
        fileInfoStack.Controls.Add(_fileFormat, 0, 2);

        var clusterButtons = UIStyles.TableLayoutPanels.CreateTransparent(2, 1);
        clusterButtons.Dock = DockStyle.Fill;
        clusterButtons.Margin = new Padding(0);
        clusterButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        clusterButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        clusterButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        clusterButtons.Controls.Add(_saveButton, 0, 0);
        clusterButtons.Controls.Add(_browseButton, 1, 0);

        var cluster = UIStyles.TableLayoutPanels.CreateTransparent(2, 1);
        cluster.Dock = DockStyle.Fill;
        cluster.Margin = new Padding(0);
        cluster.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cluster.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        cluster.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        cluster.Controls.Add(fileInfoStack, 0, 0);
        cluster.Controls.Add(clusterButtons, 1, 0);

        // Row 0: title (fill) + the cluster
        var titleRow = UIStyles.TableLayoutPanels.CreateTransparent(2, 1);
        titleRow.Dock = DockStyle.Fill;
        titleRow.Margin = new Padding(0);
        titleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        titleRow.Controls.Add(_title, 0, 0);
        titleRow.Controls.Add(cluster, 1, 0);

        // Row 2: seek (or download bar)  time  Vol  volume - flush with the
        // title / performer above it. The load spinner is in the left column
        // under the play button, not in this row.
        _seekRow = UIStyles.TableLayoutPanels.CreateTransparent(4, 1);
        _seekRow.Dock = DockStyle.Fill;
        _seekRow.Margin = new Padding(0);
        _seekRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166));
        _seekRow.Controls.Add(_seek, 0, 0);
        _seekRow.Controls.Add(_time, 1, 0);
        _seekRow.Controls.Add(_volume, 2, 0);

        var stack = UIStyles.TableLayoutPanels.CreateTransparent(1, 3);
        stack.Dock = DockStyle.Fill;
        stack.Margin = new Padding(12, 0, 0, 0);
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));   // title + the size/kbps/format stack
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // artist text box
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // seek row absorbs the rest
        stack.Controls.Add(titleRow, 0, 0);
        stack.Controls.Add(_performer, 0, 1);
        stack.Controls.Add(_seekRow, 0, 2);

        // Left column: play button pulled up to sit over the title / performer,
        // the load spinner directly under it - level with the seek row, so the
        // seek bar itself lines up with the title and performer above it.
        _leftColumn = UIStyles.TableLayoutPanels.CreateTransparent(1, 2);
        _leftColumn.Dock = DockStyle.Fill;
        _leftColumn.Margin = new Padding(0);
        _leftColumn.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));    // title + performer band
        _leftColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // seek row band
        _leftColumn.Controls.Add(_mainButton, 0, 0);   // both anchored None -> centred in their band
        _leftColumn.Controls.Add(_spinner, 0, 1);

        var controls = UIStyles.TableLayoutPanels.CreateTransparent(2, 1);
        controls.Dock = DockStyle.Fill;
        controls.Margin = new Padding(0);
        controls.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.Controls.Add(_leftColumn, 0, 0);
        controls.Controls.Add(stack, 1, 0);

        // Status / error line along the bottom edge - read-only so the text
        // (paths, error messages) can be selected and copied. Primary accent.
        _status = UIStyles.TextBoxes.CreateBorderless();
        _status.ReadOnly = true;
        _status.TabStop = false;
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _status.Margin = new Padding(0);
        _status.Font = UIStyles.Fonts.Small;
        _status.ForeColor = UIStyles.Colors.PrimaryLight;
        AppSurfaces.Follow(_status, () => _status.BackColor = BackColor);

        var root = UIStyles.TableLayoutPanels.CreateTransparent(1, 2);
        root.Dock = DockStyle.Fill;
        root.Margin = new Padding(0);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, StatusRowHeight));
        root.Controls.Add(controls, 0, 0);
        root.Controls.Add(_status, 0, 1);

        Controls.Add(root);
        Loc.Changed += OnLanguageChanged;
        Disposed += (_, _) =>
        {
            Loc.Changed -= OnLanguageChanged;
            _tips.Dispose();
        };
        SetIdle();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyTexts();

    /// <summary>(Re-)applies every visible string from <see cref="Loc"/>.</summary>
    public void ApplyTexts()
    {
        _tips.SetToolTip(_browseButton, Loc.S("player.tip.browse"));

        SetButton(_state);   // re-localises the main button's tooltip

        if (_state == PlayerButton.None)
        {
            _title.Text = Loc.S("player.nothingSelected");
        }

        _tips.SetToolTip(_saveButton, _saveTipFileName is { } f
            ? Loc.T("player.tip.downloadFile", f)
            : Loc.S("player.tip.download"));
    }

    /// <summary>Sets the status / error line along the bottom of the player.</summary>
    public void SetStatus(string text) => _status.Text = text ?? "";

    public event Action? MainButton;
    public event Action? Save;                  // save a copy to disk
    public event Action? BrowseFolder;          // open the download folder
    public event Action<double>? Seek;          // target position in seconds
    /// <summary>The user changed what is heard (slider or mute) - not raised when code sets the volume.</summary>
    public event EventHandler? VolumeChanged;

    /// <summary>
    /// Couples the volume control to the player: it takes over the player's volume
    /// and mute and from then on both follow each other.
    /// </summary>
    public void BindVolume(ErikwnkCore.IAudioVolume target) => _volume.Target = target;

    public void SetIdle()
    {
        _duration = TimeSpan.Zero;
        _sizeBytes = 0;
        SetButton(PlayerButton.None);   // greyed-out play icon
        _saveButton.Enabled = false;
        _title.Text = Loc.S("player.nothingSelected");
        _performer.Text = "";
        _fileSize.Text = "";
        _bitrate.Text = "";
        _fileFormat.Text = "";
        _saveTipFileName = null;
        _tips.SetToolTip(_saveButton, Loc.S("player.tip.download"));
        _seek.Enabled = false;
        _seek.Value = 0;
        _time.Text = "–:– / –:–";
        ShowDownloadBar(false);
    }

    /// <summary>Show a track's details and set the button to <paramref name="button"/>.</summary>
    /// <param name="canSave">
    /// Whether the save button works for this track - true when it is cached or
    /// can still be downloaded on demand (connected). "Save a copy" itself
    /// fetches an un-cached file first, so this need not wait for a download.
    /// </param>
    public void ShowTrack(AudioMessage a, PlayerButton button, bool canSave)
    {
        _title.Text = string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title;
        _performer.Text = a.Performer;
        _sizeBytes = a.SizeBytes;
        _fileSize.Text = TrackFormat.SizeMb(a.SizeBytes);
        _bitrate.Text = a.BitrateKbps is { } kb ? $"{kb} kbps" : "";
        _fileFormat.Text = TrackFormat.Format(a);
        _saveTipFileName = a.FileName;
        _tips.SetToolTip(_saveButton, Loc.T("player.tip.downloadFile", a.FileName));

        SetButton(button);
        _saveButton.Enabled = canSave;

        if (button != PlayerButton.Cancel)
        {
            ShowDownloadBar(false);
            _seek.Enabled = false;
            _seek.Value = 0;
            _duration = a.Duration ?? TimeSpan.Zero;
            _time.Text = _duration > TimeSpan.Zero ? $"0:00 / {Fmt(_duration)}" : "–:– / –:–";
        }
    }

    public void SetButton(PlayerButton button)
    {
        _state = button;
        Button target = button switch
        {
            PlayerButton.Cancel => _cancelButton,
            PlayerButton.Pause => _pauseButton,
            _ => _playButton,   // Play + None
        };
        if (!ReferenceEquals(target, _mainButton))
        {
            _leftColumn.SuspendLayout();
            _leftColumn.Controls.Remove(_mainButton);
            _mainButton = target;
            _leftColumn.Controls.Add(target, 0, 0);
            _leftColumn.ResumeLayout();
        }

        _mainButton.Enabled = button != PlayerButton.None;
        UIStyles.Buttons.UpdateTooltip(_mainButton, button switch
        {
            PlayerButton.Cancel => Loc.S("player.tip.cancelDownload"),
            PlayerButton.Pause => Loc.S("player.tip.pause"),
            PlayerButton.Play => Loc.S("player.tip.play"),
            _ => Loc.S("player.nothingSelected")
        });
    }

    public void SetDownloadProgress(int percent)
    {
        SetButton(PlayerButton.Cancel);
        _saveButton.Enabled = false;
        _seek.Enabled = false;
        ShowDownloadBar(true);
        int clamped = Math.Clamp(percent, 0, 100);
        _downloadBar.Value = clamped;
        _spinner.Value = clamped;
        _spinner.ShowPercentage = true;   // the spinner shows the percentage now
        _time.Text = "–:– / –:–";
    }

    /// <summary>The track is now loaded in the audio player - enable the seek bar.</summary>
    public void SetLoaded(TimeSpan duration, int? bitrateKbps = null)
    {
        ShowDownloadBar(false);
        _duration = duration;
        _saveButton.Enabled = true;
        _seek.Enabled = duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        _seek.Value = 0;
        UpdateTime(TimeSpan.Zero);

        // Prefer the exact rate the decoder read from the file (mp3); otherwise
        // fall back to file-size ÷ duration once we know the real length.
        if (bitrateKbps is > 0)
        {
            _bitrate.Text = $"{bitrateKbps} kbps";
        }
        else if (string.IsNullOrEmpty(_bitrate.Text) && _sizeBytes > 0 && duration.TotalSeconds > 0)
        {
            _bitrate.Text = $"{(int)Math.Round(_sizeBytes * 8 / duration.TotalSeconds / 1000)} kbps";
        }
    }

    private void ShowDownloadBar(bool show)
    {
        if (show == _showingDownloadBar)
        {
            return;
        }
        _showingDownloadBar = show;
        _seekRow.SuspendLayout();
        _seekRow.Controls.Remove(show ? _seek : (Control)_downloadBar);
        _seekRow.Controls.Add(show ? _downloadBar : (Control)_seek, 0, 0);
        _spinner.Visible = show;   // spins for the whole load (download + decode)
        if (!show)
        {
            _spinner.ShowPercentage = false;
            _spinner.Value = 0;
        }
        _seekRow.ResumeLayout();
    }

    public void SetPosition(TimeSpan pos)
    {
        if (!_seek.Enabled || _seek.IsDragging)
        {
            return;
        }
        _seek.Value = pos.TotalSeconds;
        UpdateTime(pos);
    }

    private void UpdateTime(TimeSpan pos) =>
        _time.Text = $"{Fmt(pos)} / {Fmt(_duration)}";

    private Button MakeMainButton(UIButtonPreset preset)
    {
        Button b = UIStyles.Buttons.CreateSymbol(preset, "", new Size(44, 44));
        b.Anchor = AnchorStyles.None;
        b.Click += (_, _) => MainButton?.Invoke();
        return b;
    }

    private static Label MakeLabel(Label l)
    {
        l.AutoSize = false;
        l.AutoEllipsis = true;
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleLeft;
        return l;
    }

    private TextBox MakeReadonlyText(bool bold)
    {
        TextBox tb = UIStyles.TextBoxes.CreateBorderless();
        tb.ReadOnly = true;
        tb.TabStop = false;
        tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        // blend into the panel, in every theme
        AppSurfaces.Follow(tb, () =>
        {
            tb.BackColor = BackColor;
            tb.ForeColor = UIStyles.Colors.TextPrimary;
        });
        if (bold)
        {
            tb.Font = new Font(tb.Font, FontStyle.Bold);
        }
        return tb;
    }

    private static string Fmt(TimeSpan t) =>
        t <= TimeSpan.Zero ? "0:00" : TrackFormat.Duration(t);
}

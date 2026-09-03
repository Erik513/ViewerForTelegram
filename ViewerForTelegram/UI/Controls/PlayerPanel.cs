using ErikwnkWFUI;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.UI.Controls;

/// <summary>
/// The player at the bottom of the main window. Shows the current track's title,
/// performer and file details, with play/pause, a seek slider, elapsed / total
/// time, a volume slider and a download button. While a track is still
/// downloading it shows progress and the button cancels.
/// </summary>
public sealed class PlayerPanel : Panel
{
    /// <summary>Fixed height the host should give this panel.</summary>
    public const int PanelHeight = 106;

    private enum Mode { Idle, Downloading, Loaded }

    private readonly Label _title;
    private readonly Label _performer;
    private readonly Label _meta;
    private readonly Button _playButton;
    private readonly SliderBar _seek;
    private readonly Label _time;
    private readonly SliderBar _volume;
    private readonly Button _downloadButton;

    private Mode _mode = Mode.Idle;
    private TimeSpan _duration;

    private const string GlyphPlay = "▶";
    private const string GlyphPause = "⏸";
    private const string GlyphStop = "■";
    private const string GlyphDownload = "⭳";

    public PlayerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = PanelHeight;
        Padding = new Padding(12, 6, 12, 8);
        BackColor = UIStyles.Colors.BackgroundDarkElevated;

        _title = UIStyles.Labels.CreateNormal("Nothing playing");
        _title.Font = new Font(_title.Font, FontStyle.Bold);
        _title.AutoEllipsis = true;
        _title.Dock = DockStyle.Fill;

        _performer = UIStyles.Labels.CreateMuted("");
        _performer.AutoEllipsis = true;
        _performer.Dock = DockStyle.Fill;

        _meta = UIStyles.Labels.CreateMuted("");
        _meta.AutoEllipsis = true;
        _meta.Dock = DockStyle.Fill;

        _playButton = UIStyles.Buttons.CreateStandard(GlyphPlay, "Play / pause", new Size(34, 30));
        _playButton.Enabled = false;
        _playButton.Anchor = AnchorStyles.Left;
        _playButton.Click += (_, _) =>
        {
            if (_mode == Mode.Downloading) CancelDownload?.Invoke();
            else if (_mode == Mode.Loaded) PlayPause?.Invoke();
        };

        _seek = new SliderBar { Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _seek.ValueChanged += (_, _) =>
        {
            if (_mode == Mode.Loaded)
            {
                Seek?.Invoke(_seek.Value);
            }
        };

        _time = UIStyles.Labels.CreateMuted("–:– / –:–");
        _time.Dock = DockStyle.Fill;
        _time.TextAlign = ContentAlignment.MiddleCenter;
        _time.AutoSize = false;

        var volLabel = UIStyles.Labels.CreateMuted("Vol");
        volLabel.Dock = DockStyle.Fill;
        volLabel.TextAlign = ContentAlignment.MiddleRight;

        _volume = new SliderBar { Maximum = 1.0, Value = 0.4, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _volume.ValueChanged += (_, _) => VolumeChanged?.Invoke((float)_volume.Value);

        _downloadButton = UIStyles.Buttons.CreateStandard(GlyphDownload, "Save a copy of this track", new Size(34, 30));
        _downloadButton.Enabled = false;
        _downloadButton.Anchor = AnchorStyles.Right;
        _downloadButton.Click += (_, _) => Save?.Invoke();

        var controls = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.Transparent
        };
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        controls.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        controls.Controls.Add(_playButton, 0, 0);
        controls.Controls.Add(_seek, 1, 0);
        controls.Controls.Add(_time, 2, 0);
        controls.Controls.Add(volLabel, 3, 0);
        controls.Controls.Add(_volume, 4, 0);
        controls.Controls.Add(_downloadButton, 5, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Color.Transparent
        };
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 17));
        grid.Controls.Add(_title, 0, 0);
        grid.Controls.Add(_performer, 0, 1);
        grid.Controls.Add(controls, 0, 2);
        grid.Controls.Add(_meta, 0, 3);

        Controls.Add(grid);
        SetIdle();
    }

    public event Action? PlayPause;
    public event Action<double>? Seek;          // target position in seconds
    public event Action<float>? VolumeChanged;  // 0..1
    public event Action? Save;
    public event Action? CancelDownload;

    public float Volume
    {
        get => (float)_volume.Value;
        set => _volume.Value = Math.Clamp(value, 0f, 1f);
    }

    public void SetIdle()
    {
        _mode = Mode.Idle;
        _duration = TimeSpan.Zero;
        _title.Text = "Nothing playing";
        _performer.Text = "";
        _meta.Text = "";
        _playButton.Text = GlyphPlay;
        _playButton.Enabled = false;
        _seek.Enabled = false;
        _seek.Value = 0;
        _downloadButton.Enabled = false;
        _time.Text = "–:– / –:–";
    }

    public void SetDownloading(AudioMessage a, int percent)
    {
        _mode = Mode.Downloading;
        ShowInfo(a);
        _playButton.Text = GlyphStop;
        _playButton.Enabled = true;
        _seek.Enabled = false;
        _seek.Maximum = 100;
        _seek.Value = Math.Clamp(percent, 0, 100);
        _downloadButton.Enabled = false;
        _time.Text = $"↓ {Math.Clamp(percent, 0, 100)}%";
    }

    public void SetLoaded(AudioMessage a, TimeSpan duration)
    {
        _mode = Mode.Loaded;
        _duration = duration;
        ShowInfo(a);
        _playButton.Enabled = true;
        _seek.Enabled = duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        _seek.Value = 0;
        _downloadButton.Enabled = true;
        SetPlaying(false);
        UpdateTime(TimeSpan.Zero);
    }

    public void SetPlaying(bool playing) =>
        _playButton.Text = playing ? GlyphPause : GlyphPlay;

    public void SetPosition(TimeSpan pos)
    {
        if (_mode != Mode.Loaded || _seek.IsDragging)
        {
            return;
        }
        _seek.Value = pos.TotalSeconds;
        UpdateTime(pos);
    }

    private void ShowInfo(AudioMessage a)
    {
        _title.Text = string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title;
        _performer.Text = a.Performer;
        string ext = Path.GetExtension(a.FileName).TrimStart('.').ToUpperInvariant();
        _meta.Text = $"{a.FileName}   ·   {a.SizeBytes / 1024d / 1024d:0.0} MB"
                     + (ext.Length > 0 ? $"   ·   {ext}" : "");
    }

    private void UpdateTime(TimeSpan pos) =>
        _time.Text = $"{Fmt(pos)} / {Fmt(_duration)}";

    private static string Fmt(TimeSpan t) =>
        t <= TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

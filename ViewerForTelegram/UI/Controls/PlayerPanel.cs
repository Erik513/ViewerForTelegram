using ErikwnkWFUI;

namespace ViewerForTelegram.UI.Controls;

/// <summary>
/// The player strip docked at the bottom of the main window: play/pause, a seek
/// slider, elapsed / total time, a volume slider and a "Save" button. While a
/// track is still downloading it shows progress instead and the button cancels.
/// </summary>
public sealed class PlayerPanel : Panel
{
    private enum Mode { Idle, Downloading, Loaded }

    private readonly Button _playButton;
    private readonly SliderBar _seek;
    private readonly Label _time;
    private readonly Label _nowPlaying;
    private readonly SliderBar _volume;
    private readonly Button _saveButton;

    private Mode _mode = Mode.Idle;
    private bool _playing;
    private TimeSpan _duration;

    private const string GlyphPlay = "▶";
    private const string GlyphPause = "⏸";
    private const string GlyphStop = "■";

    public PlayerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = 64;
        Padding = new Padding(12, 8, 12, 8);
        BackColor = UIStyles.Colors.BackgroundDarkElevated;

        _nowPlaying = UIStyles.Labels.CreateMuted("");
        _nowPlaying.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _nowPlaying.AutoEllipsis = true;
        _nowPlaying.AutoSize = false;
        _nowPlaying.Height = 16;

        _playButton = UIStyles.Buttons.CreateStandard(GlyphPlay, "Play / pause", new Size(34, 30));
        _playButton.Enabled = false;
        _playButton.Click += (_, _) =>
        {
            if (_mode == Mode.Downloading)
            {
                CancelDownload?.Invoke();
            }
            else if (_mode == Mode.Loaded)
            {
                PlayPause?.Invoke();
            }
        };

        _seek = new SliderBar { Enabled = false };
        _seek.ValueChanged += (_, _) =>
        {
            if (_mode == Mode.Loaded && _seek.IsDragging)
            {
                Seek?.Invoke(_seek.Value);
            }
        };
        _seek.MouseUp += (_, _) =>
        {
            if (_mode == Mode.Loaded)
            {
                Seek?.Invoke(_seek.Value);
            }
        };

        _time = UIStyles.Labels.CreateMuted("–:– / –:–");
        _time.AutoSize = false;
        _time.TextAlign = ContentAlignment.MiddleCenter;

        var volLabel = UIStyles.Labels.CreateMuted("Vol");
        volLabel.AutoSize = false;
        volLabel.TextAlign = ContentAlignment.MiddleRight;

        _volume = new SliderBar { Maximum = 1.0, Value = 0.4 };
        _volume.ValueChanged += (_, _) => VolumeChanged?.Invoke((float)_volume.Value);

        _saveButton = UIStyles.Buttons.CreateStandard("Save", "Save a copy of this track", new Size(72, 30));
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => Save?.Invoke();

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));

        _playButton.Anchor = AnchorStyles.Left;
        _seek.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _time.Dock = DockStyle.Fill;
        volLabel.Dock = DockStyle.Fill;
        _volume.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _saveButton.Anchor = AnchorStyles.Right;

        row.Controls.Add(_playButton, 0, 0);
        row.Controls.Add(_seek, 1, 0);
        row.Controls.Add(_time, 2, 0);
        row.Controls.Add(volLabel, 3, 0);
        row.Controls.Add(_volume, 4, 0);
        row.Controls.Add(_saveButton, 5, 0);

        Controls.Add(row);
        Controls.Add(_nowPlaying);

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
        _playing = false;
        _duration = TimeSpan.Zero;
        _nowPlaying.Text = "";
        _playButton.Text = GlyphPlay;
        _playButton.Enabled = false;
        _seek.Enabled = false;
        _seek.Value = 0;
        _saveButton.Enabled = false;
        _time.Text = "–:– / –:–";
    }

    public void SetDownloading(string title, int percent)
    {
        _mode = Mode.Downloading;
        _nowPlaying.Text = title;
        _playButton.Text = GlyphStop;
        _playButton.Enabled = true;
        _seek.Enabled = false;
        _seek.Maximum = 100;
        _seek.Value = Math.Clamp(percent, 0, 100);
        _saveButton.Enabled = false;
        _time.Text = $"Downloading… {Math.Clamp(percent, 0, 100)}%";
    }

    public void SetLoaded(string title, TimeSpan duration)
    {
        _mode = Mode.Loaded;
        _duration = duration;
        _nowPlaying.Text = title;
        _playButton.Enabled = true;
        _seek.Enabled = duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        _seek.Value = 0;
        _saveButton.Enabled = true;
        SetPlaying(false);
        UpdateTime(TimeSpan.Zero);
    }

    public void SetPlaying(bool playing)
    {
        _playing = playing;
        _playButton.Text = playing ? GlyphPause : GlyphPlay;
    }

    public void SetPosition(TimeSpan pos)
    {
        if (_mode != Mode.Loaded || _seek.IsDragging)
        {
            return;
        }
        _seek.Value = pos.TotalSeconds;
        UpdateTime(pos);
    }

    private void UpdateTime(TimeSpan pos) =>
        _time.Text = $"{Fmt(pos)} / {Fmt(_duration)}";

    private static string Fmt(TimeSpan t) =>
        t <= TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

using ErikwnkWFUI;
using ViewerForTelegram.Data.Models;

namespace ViewerForTelegram.UI.Controls;

/// <summary>What the single main button currently does.</summary>
public enum PlayerButton
{
    None,       // nothing selected - button disabled
    Download,   // track not in the cache yet
    Cancel,     // download running
    Play,       // in the cache / paused / stopped
    Pause       // currently playing
}

/// <summary>
/// The player at the bottom of the main window. Shows the selected track's
/// title, performer and file details. One button on the left drives everything
/// (download / cancel / play / pause); MainForm decides what it does. Seek and
/// volume sliders sit next to the info.
/// </summary>
public sealed class PlayerPanel : Panel
{
    /// <summary>Fixed height the host should give this panel.</summary>
    public const int PanelHeight = 118;

    private readonly Button _mainButton;
    private readonly Label _title;
    private readonly Label _performer;
    private readonly Label _meta;
    private readonly SliderBar _seek;
    private readonly Label _time;
    private readonly SliderBar _volume;

    private PlayerButton _state = PlayerButton.None;
    private TimeSpan _duration;

    public PlayerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = PanelHeight;
        Padding = new Padding(12, 8, 14, 10);
        BackColor = UIStyles.Colors.BackgroundDarkElevated;

        _mainButton = UIStyles.Buttons.CreateStandard("", "", new Size(46, 46));
        _mainButton.Font = new Font(_mainButton.Font.FontFamily, 15f);
        _mainButton.Enabled = false;
        _mainButton.Anchor = AnchorStyles.None;
        _mainButton.Click += (_, _) => MainButton?.Invoke();

        _title = MakeLabel(UIStyles.Labels.CreateNormal("Nothing selected"));
        _title.Font = new Font(_title.Font, FontStyle.Bold);

        _performer = MakeLabel(UIStyles.Labels.CreateMuted(""));
        _meta = MakeLabel(UIStyles.Labels.CreateMuted(""));

        _seek = new SliderBar { Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _seek.ValueChanged += (_, _) =>
        {
            if (_seek.Enabled)
            {
                Seek?.Invoke(_seek.Value);
            }
        };

        _time = MakeLabel(UIStyles.Labels.CreateMuted("–:– / –:–"));
        _time.TextAlign = ContentAlignment.MiddleCenter;

        var volLabel = MakeLabel(UIStyles.Labels.CreateMuted("Vol"));
        volLabel.TextAlign = ContentAlignment.MiddleRight;

        _volume = new SliderBar { Maximum = 1.0, Value = 0.1, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _volume.ValueChanged += (_, _) => VolumeChanged?.Invoke((float)_volume.Value);

        var mid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            Margin = new Padding(0), BackColor = Color.Transparent
        };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        mid.Controls.Add(_seek, 0, 0);
        mid.Controls.Add(_time, 1, 0);
        mid.Controls.Add(volLabel, 2, 0);
        mid.Controls.Add(_volume, 3, 0);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = new Padding(10, 0, 0, 0), BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        stack.Controls.Add(_title, 0, 0);
        stack.Controls.Add(_performer, 0, 1);
        stack.Controls.Add(mid, 0, 2);
        stack.Controls.Add(_meta, 0, 3);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0), BackColor = Color.Transparent
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(_mainButton, 0, 0);
        root.Controls.Add(stack, 1, 0);

        Controls.Add(root);
        SetIdle();
    }

    public event Action? MainButton;
    public event Action<double>? Seek;          // target position in seconds
    public event Action<float>? VolumeChanged;  // 0..1

    public float Volume
    {
        get => (float)_volume.Value;
        set => _volume.Value = Math.Clamp(value, 0f, 1f);
    }

    public void SetIdle()
    {
        _state = PlayerButton.None;
        _duration = TimeSpan.Zero;
        _mainButton.Text = "";
        _mainButton.Enabled = false;
        _title.Text = "Nothing selected";
        _performer.Text = "";
        _meta.Text = "";
        _seek.Enabled = false;
        _seek.Value = 0;
        _time.Text = "–:– / –:–";
    }

    /// <summary>Show a track's details and set the button to <paramref name="button"/>.</summary>
    public void ShowTrack(AudioMessage a, PlayerButton button)
    {
        _title.Text = string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title;
        _performer.Text = a.Performer;
        string ext = Path.GetExtension(a.FileName).TrimStart('.').ToUpperInvariant();
        _meta.Text = $"{a.FileName}   ·   {a.SizeBytes / 1024d / 1024d:0.0} MB"
                     + (ext.Length > 0 ? $"   ·   {ext}" : "");
        SetButton(button);
        if (button != PlayerButton.Cancel)
        {
            _seek.Enabled = false;
            _seek.Value = 0;
            _duration = a.Duration ?? TimeSpan.Zero;
            _time.Text = _duration > TimeSpan.Zero ? $"0:00 / {Fmt(_duration)}" : "–:– / –:–";
        }
    }

    public void SetButton(PlayerButton button)
    {
        _state = button;
        _mainButton.Enabled = button != PlayerButton.None;
        _mainButton.Text = button switch
        {
            PlayerButton.Download => "⇩",
            PlayerButton.Cancel => "✕",
            PlayerButton.Play => "▶",
            PlayerButton.Pause => "⏸",
            _ => ""
        };
    }

    public void SetDownloadProgress(int percent)
    {
        SetButton(PlayerButton.Cancel);
        _seek.Enabled = false;
        _seek.Maximum = 100;
        _seek.Value = Math.Clamp(percent, 0, 100);
        _time.Text = $"↓ {Math.Clamp(percent, 0, 100)}%";
    }

    /// <summary>The track is now loaded in the audio player - enable the seek bar.</summary>
    public void SetLoaded(TimeSpan duration)
    {
        _duration = duration;
        _seek.Enabled = duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        _seek.Value = 0;
        UpdateTime(TimeSpan.Zero);
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

    private static Label MakeLabel(Label l)
    {
        l.AutoSize = false;
        l.AutoEllipsis = true;
        l.Dock = DockStyle.Fill;
        l.TextAlign = ContentAlignment.MiddleLeft;
        return l;
    }

    private static string Fmt(TimeSpan t) =>
        t <= TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

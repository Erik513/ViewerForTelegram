using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ViewerForTelegram.Data.Models;

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
/// The player at the bottom of the main window. One button on the left plays /
/// pauses / cancels the selected track (a download starts automatically the
/// first time you press play). A separate button saves a copy to disk. Seek and
/// volume sit below the file details.
/// </summary>
public sealed class PlayerPanel : Panel
{
    /// <summary>Fixed height the host should give this panel.</summary>
    public const int PanelHeight = 138;

    private readonly Button _mainButton;
    private readonly Button _saveButton;
    private readonly Label _title;
    private readonly Label _performer;
    private readonly Label _fileInfo;
    private readonly SliderBar _seek;
    private readonly SlimProgressBar _downloadBar;
    private readonly TableLayoutPanel _seekRow;
    private readonly Label _time;
    private readonly SliderBar _volume;

    private TimeSpan _duration;
    private bool _showingDownloadBar;
    private PlayerButton _state = PlayerButton.None;

    public PlayerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = PanelHeight;
        Padding = new Padding(12, 8, 14, 10);
        BackColor = UIStyles.Colors.BackgroundDarkElevated;

        _mainButton = MakeGlyphButton("Play / pause");
        _mainButton.Anchor = AnchorStyles.None;
        _mainButton.Click += (_, _) => MainButton?.Invoke();
        // The pause icon is drawn by hand - no bar glyph renders as two clean
        // strokes in Segoe UI (they all collapse into one block).
        _mainButton.Paint += OnMainButtonPaint;

        _saveButton = MakeGlyphButton("Save a copy to disk");
        _saveButton.Text = "⭳";
        _saveButton.Anchor = AnchorStyles.None;
        _saveButton.Click += (_, _) => Save?.Invoke();

        _title = MakeLabel(UIStyles.Labels.CreateNormal("Nothing selected"));
        _title.Font = new Font(_title.Font, FontStyle.Bold);
        _performer = MakeLabel(UIStyles.Labels.CreateMuted(""));
        _fileInfo = MakeLabel(UIStyles.Labels.CreateMuted(""));

        _seek = new SliderBar { Enabled = false, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _seek.ValueChanged += (_, _) =>
        {
            if (_seek.Enabled)
            {
                Seek?.Invoke(_seek.Value);
            }
        };

        // Shown in place of the seek bar while a download runs - red→yellow→green.
        _downloadBar = UIStyles.SlimProgressBars.CreateStatus();
        _downloadBar.BackColor = UIStyles.Colors.BorderMedium;
        _downloadBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        _time = MakeLabel(UIStyles.Labels.CreateMuted("–:– / –:–"));
        _time.TextAlign = ContentAlignment.MiddleCenter;

        var volLabel = MakeLabel(UIStyles.Labels.CreateMuted("Vol"));
        volLabel.TextAlign = ContentAlignment.MiddleRight;

        _volume = new SliderBar { Maximum = 1.0, Value = 0.1, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        _volume.ValueChanged += (_, _) => VolumeChanged?.Invoke((float)_volume.Value);

        // Row: [save]  filename · size · format
        var fileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0), BackColor = Color.Transparent
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.Controls.Add(_saveButton, 0, 0);
        fileRow.Controls.Add(_fileInfo, 1, 0);

        // Row: seek (or download bar)  time  Vol  volume
        _seekRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            Margin = new Padding(0), BackColor = Color.Transparent
        };
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        _seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        _seekRow.Controls.Add(_seek, 0, 0);
        _seekRow.Controls.Add(_time, 1, 0);
        _seekRow.Controls.Add(volLabel, 2, 0);
        _seekRow.Controls.Add(_volume, 3, 0);

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Margin = new Padding(12, 0, 0, 0), BackColor = Color.Transparent
        };
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        stack.Controls.Add(_title, 0, 0);
        stack.Controls.Add(_performer, 0, 1);
        stack.Controls.Add(fileRow, 0, 2);
        stack.Controls.Add(_seekRow, 0, 3);

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
    public event Action? Save;                  // save a copy to disk
    public event Action<double>? Seek;          // target position in seconds
    public event Action<float>? VolumeChanged;  // 0..1

    public float Volume
    {
        get => (float)_volume.Value;
        set => _volume.Value = Math.Clamp(value, 0f, 1f);
    }

    public void SetIdle()
    {
        _duration = TimeSpan.Zero;
        _mainButton.Text = "";
        _mainButton.Enabled = false;
        _saveButton.Enabled = false;
        _title.Text = "Nothing selected";
        _performer.Text = "";
        _fileInfo.Text = "";
        _seek.Enabled = false;
        _seek.Value = 0;
        _time.Text = "–:– / –:–";
        ShowDownloadBar(false);
    }

    /// <summary>Show a track's details and set the button to <paramref name="button"/>.</summary>
    public void ShowTrack(AudioMessage a, PlayerButton button, bool cached)
    {
        _title.Text = string.IsNullOrWhiteSpace(a.Title) ? a.FileName : a.Title;
        _performer.Text = a.Performer;
        string ext = Path.GetExtension(a.FileName).TrimStart('.').ToUpperInvariant();
        _fileInfo.Text = $"{a.FileName}   ·   {a.SizeBytes / 1024d / 1024d:0.0} MB"
                         + (ext.Length > 0 ? $"   ·   {ext}" : "");
        SetButton(button);
        _saveButton.Enabled = cached;

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
        _mainButton.Enabled = button != PlayerButton.None;
        _mainButton.Text = button switch
        {
            PlayerButton.Cancel => "✕",
            PlayerButton.Play => "▶",
            _ => ""   // Pause is drawn in OnMainButtonPaint
        };
        _mainButton.Invalidate();
    }

    private void OnMainButtonPaint(object? sender, PaintEventArgs e)
    {
        if (_state != PlayerButton.Pause || sender is not Control b)
        {
            return;
        }
        const int barW = 5, barH = 16, gap = 6;
        int cx = b.ClientSize.Width / 2;
        int cy = b.ClientSize.Height / 2;
        using var brush = new SolidBrush(
            b.Enabled ? b.ForeColor : UIStyles.Colors.TextDisabled);
        e.Graphics.FillRectangle(brush, cx - gap / 2 - barW, cy - barH / 2, barW, barH);
        e.Graphics.FillRectangle(brush, cx + gap / 2, cy - barH / 2, barW, barH);
    }

    public void SetDownloadProgress(int percent)
    {
        SetButton(PlayerButton.Cancel);
        _saveButton.Enabled = false;
        _seek.Enabled = false;
        ShowDownloadBar(true);
        _downloadBar.Value = Math.Clamp(percent, 0, 100);
        _time.Text = $"↓ {Math.Clamp(percent, 0, 100)}%";
    }

    /// <summary>The track is now loaded in the audio player - enable the seek bar.</summary>
    public void SetLoaded(TimeSpan duration)
    {
        ShowDownloadBar(false);
        _duration = duration;
        _saveButton.Enabled = true;
        _seek.Enabled = duration > TimeSpan.Zero;
        _seek.Maximum = Math.Max(1, duration.TotalSeconds);
        _seek.Value = 0;
        UpdateTime(TimeSpan.Zero);
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

    private static Button MakeGlyphButton(string tooltip)
    {
        Button b = UIStyles.Buttons.CreateStandard("", tooltip, new Size(44, 44));
        b.Font = new Font(b.Font.FontFamily, 15f);
        b.TextAlign = ContentAlignment.MiddleCenter;
        b.Enabled = false;
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

    private static string Fmt(TimeSpan t) =>
        t <= TimeSpan.Zero ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
}

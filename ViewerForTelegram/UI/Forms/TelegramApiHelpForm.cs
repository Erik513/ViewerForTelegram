using System.Diagnostics;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.UI.Localization;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Short guide: how to create api_id / api_hash on my.telegram.org.
/// </summary>
public sealed class TelegramApiHelpForm : StyledForm
{
    private const string Url = "https://my.telegram.org";

    private const int Pad = 16;
    private const int ButtonRow = 44;
    private const int TextBottomMargin = 12;
    private const int Width_ = 600;

    private readonly TextBox _text;

    public TelegramApiHelpForm()
        : base(StyledFormOptions.CreateDialog(
            Loc.S("help.title"),
            titleTextAlign: ContentAlignment.MiddleLeft,
            icon: AppAssets.TitleBarLogo,
            windowIcon: AppAssets.WindowIcon))
    {
        StartPosition = FormStartPosition.CenterParent;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UIStyles.Colors.BackgroundLight,
            Padding = new Padding(Pad),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, ButtonRow));

        _text = UIStyles.TextBoxes.CreateStandard();
        _text.Text = Loc.S("help.body");
        _text.Dock = DockStyle.Fill;
        _text.Multiline = true;
        _text.ReadOnly = true;
        _text.TabStop = false;
        _text.ScrollBars = ScrollBars.None; // form is sized (ctor estimate + OnLoad) to fit the whole text
        _text.Margin = new Padding(0, 0, 0, TextBottomMargin);

        // Generous first estimate; OnLoad measures the real layout and tops it up.
        int estimate = _text.Text.Split('\n').Length * Math.Max(18, _text.Font.Height) + 28;
        ClientSize = new Size(
            Width_,
            TitleBar.Height + Padding.Vertical
                + 2 * Pad + estimate + TextBottomMargin + ButtonRow);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        Button close = UIStyles.Buttons.CreatePrimary(Loc.S("help.close"), "", new Size(110, 32));
        close.Margin = new Padding(8, 4, 0, 4);
        close.Click += (_, _) => Close();

        Button open = UIStyles.Buttons.CreatePrimary(Loc.S("help.open"), "", new Size(200, 32));
        open.Margin = new Padding(0, 4, 0, 4);
        open.Click += (_, _) => OpenUrl();

        buttons.Controls.Add(close);
        buttons.Controls.Add(open);
        CancelButton = close;

        layout.Controls.Add(_text, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        ContentPanel.Controls.Add(layout);

        Shown += (_, _) =>
        {
            _text.SelectionStart = 0;
            _text.SelectionLength = 0;
            close.Focus();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Layout has run - the TextBox now has its real width. If the wrapped
        // text needs more vertical space than it got, grow the form (and
        // re-center on the owner) so the full guide shows without a scrollbar.
        int needed = TextRenderer.MeasureText(
            _text.Text, _text.Font,
            new Size(_text.ClientSize.Width, 0),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 10;

        int deficit = needed - _text.ClientSize.Height;
        if (deficit <= 0)
        {
            return;
        }

        Height += deficit;
        MinimumSize = Size;

        if (Owner is { } owner)
        {
            Location = new Point(
                owner.Left + (owner.Width - Width) / 2,
                owner.Top + (owner.Height - Height) / 2);
        }
    }

    private void OpenUrl()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Url, UseShellExecute = true });
        }
        catch
        {
            // ignore - the user can also type the address manually
        }
    }

}

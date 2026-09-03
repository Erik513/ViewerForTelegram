using System.Diagnostics;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Short guide: how to create api_id / api_hash on my.telegram.org.
/// </summary>
public sealed class TelegramApiHelpForm : StyledForm
{
    private const string Url = "https://my.telegram.org";

    public TelegramApiHelpForm()
        : base(StyledFormOptions.CreateDialog("Create credentials"))
    {
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(600, 420);
        MinimumSize = new Size(600, 420);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UIStyles.Colors.BackgroundLight,
            Padding = new Padding(16),
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        TextBox text = UIStyles.TextBoxes.CreateStandard();
        text.Text = InstructionText();
        text.Dock = DockStyle.Fill;
        text.Multiline = true;
        text.ReadOnly = true;
        text.TabStop = false;
        text.ScrollBars = ScrollBars.Vertical;
        text.Margin = new Padding(0, 0, 0, 12);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };

        Button close = UIStyles.Buttons.CreateStandard("Close", "", new Size(110, 32));
        close.Margin = new Padding(8, 4, 0, 4);
        close.Click += (_, _) => Close();

        Button open = UIStyles.Buttons.CreatePrimary("Open my.telegram.org", "", new Size(200, 32));
        open.Margin = new Padding(0, 4, 0, 4);
        open.Click += (_, _) => OpenUrl();

        buttons.Controls.Add(close);
        buttons.Controls.Add(open);
        CancelButton = close;

        layout.Controls.Add(text, 0, 0);
        layout.Controls.Add(buttons, 0, 1);
        ContentPanel.Controls.Add(layout);

        Shown += (_, _) =>
        {
            text.SelectionStart = 0;
            text.SelectionLength = 0;
            close.Focus();
        };
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

    private static string InstructionText() =>
        "ViewerForTelegram ships no shared API credentials - every user creates\r\n" +
        "their own. It is free and takes about 2 minutes.\r\n\r\n" +
        "How to get api_id and api_hash:\r\n\r\n" +
        "1. Click \"Open my.telegram.org\" below.\r\n" +
        "2. Sign in with your Telegram phone number - the confirmation code\r\n" +
        "   arrives in your Telegram app.\r\n" +
        "3. Click \"API development tools\".\r\n" +
        "4. Fill in the form:\r\n" +
        "     App title:   e.g. ViewerForTelegram\r\n" +
        "     Short name:  e.g. tgviewer\r\n" +
        "     Platform:    Desktop\r\n" +
        "     (URL and description can stay empty)\r\n" +
        "5. Click \"Create application\".\r\n" +
        "6. The next page shows \"App api_id\" (a number) and\r\n" +
        "   \"App api_hash\" (a long hex string).\r\n" +
        "7. Enter both here in the settings - the pencil icon unlocks the\r\n" +
        "   respective field.\r\n\r\n" +
        "Important: the api_hash is like a password - do not share it.";
}

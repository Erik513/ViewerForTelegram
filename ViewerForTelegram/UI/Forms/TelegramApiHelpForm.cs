using System.Diagnostics;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Kurzanleitung: wie man api_id / api_hash auf my.telegram.org anlegt.
/// </summary>
public sealed class TelegramApiHelpForm : StyledForm
{
    private const string Url = "https://my.telegram.org";

    public TelegramApiHelpForm()
        : base(StyledFormOptions.CreateDialog("Zugangsdaten anlegen"))
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

        Button close = UIStyles.Buttons.CreateStandard("Schließen", "", new Size(110, 32));
        close.Margin = new Padding(8, 4, 0, 4);
        close.Click += (_, _) => Close();

        Button open = UIStyles.Buttons.CreatePrimary("my.telegram.org öffnen", "", new Size(200, 32));
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
            // ignorieren - der Nutzer kann die Adresse auch abtippen
        }
    }

    private static string InstructionText() =>
        "ViewerForTelegram liefert keine gemeinsamen API-Zugangsdaten mit - jeder Nutzer\r\n" +
        "legt seine eigenen an. Das ist kostenlos und dauert 2 Minuten.\r\n\r\n" +
        "So kommst du an api_id und api_hash:\r\n\r\n" +
        "1. Unten auf \"my.telegram.org öffnen\" klicken.\r\n" +
        "2. Mit deiner Telegram-Telefonnummer anmelden - der Bestätigungscode\r\n" +
        "   kommt in deiner Telegram-App.\r\n" +
        "3. \"API development tools\" anklicken.\r\n" +
        "4. Das Formular ausfüllen:\r\n" +
        "     App title:   z. B. ViewerForTelegram\r\n" +
        "     Short name:  z. B. tgviewer\r\n" +
        "     Platform:    Desktop\r\n" +
        "     (URL und Beschreibung können leer bleiben)\r\n" +
        "5. \"Create application\" klicken.\r\n" +
        "6. Auf der nächsten Seite stehen \"App api_id\" (eine Zahl) und\r\n" +
        "   \"App api_hash\" (langer Hex-String).\r\n" +
        "7. Beide hier in den Einstellungen eintragen - das Stift-Symbol\r\n" +
        "   entsperrt das jeweilige Feld.\r\n\r\n" +
        "Wichtig: der api_hash ist wie ein Passwort - nicht weitergeben.";
}

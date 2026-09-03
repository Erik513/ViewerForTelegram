using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
using MessageBox = ErikwnkWFUI.Forms.MessageBox;
using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;

namespace ViewerForTelegram.UI.Forms;

/// <summary>Was der Nutzer beim Schließen der Einstellungen ausgelöst hat.</summary>
public enum SettingsAction
{
    /// <summary>Nur Änderungen übernehmen.</summary>
    None,

    /// <summary>Anmelden (mit den aktuellen Zugangsdaten).</summary>
    Connect,

    /// <summary>Abmelden – nur die gespeicherte Sitzung löschen.</summary>
    Logout,

    /// <summary>Zugangsdaten und Sitzung komplett löschen.</summary>
    Wipe
}

/// <summary>
/// Einstellungen: Zugangsdaten (schreibgeschützt, per Stift entsperrbar),
/// Anmeldung, Download-Ordner, Cache. Kein Speichern-Knopf – beim Schließen
/// wird automatisch übernommen.
/// </summary>
public sealed class SettingsForm : StyledForm
{
    private const int RowH = 40;
    private const string Pencil = "✎";
    private const string Check = "✓";

    private static readonly Size ButtonSize = new(150, 30);
    private const int ButtonColumn = 162;

    private readonly IMediaCache _cache;
    private readonly PropertyTable _table;
    private readonly ToolTip _toolTip;

    private readonly TextBox _apiId;
    private readonly TextBox _apiHash;
    private readonly TextBox _phone;
    private readonly TextBox _downloadFolder;
    private readonly ToggleSwitch _useDownloadFolder;
    private readonly ToggleSwitch _clearCacheOnStart;
    private readonly Label _cacheSizeLabel;
    private readonly Button _clearCacheButton;
    private readonly bool _isConnected;

    /// <summary>Aktueller Stand – wird beim Schließen aus den Feldern befüllt.</summary>
    public TelegramConfig Result { get; private set; }

    /// <summary>Was beim Schließen ausgelöst wurde.</summary>
    public SettingsAction Action { get; private set; } = SettingsAction.None;

    public SettingsForm(TelegramConfig current, IMediaCache cache, bool isConnected)
        : base(StyledFormOptions.CreateDialog("Einstellungen"))
    {
        _cache = cache;
        _isConnected = isConnected;
        Result = current;
        StartPosition = FormStartPosition.CenterParent;

        _toolTip = UIStyles.ToolTips.CreateToolTip();
        Disposed += (_, _) => _toolTip.Dispose();

        // Hintergrund einen Tick heller als die PropertyTable (die auf
        // BackgroundMedium sitzt) - so hebt sie sich als "Karte" ab.
        ContentPanel.BackColor = UIStyles.Colors.BackgroundMediumElevated;

        // ---- Hilfe-Leiste oben ----
        Panel top = UIStyles.Panels.CreateMedium();
        top.BackColor = UIStyles.Colors.BackgroundMediumElevated;
        top.Dock = DockStyle.Top;
        top.Height = 46;

        Button help = UIStyles.Buttons.CreateStandard(
            "?  Anleitung", "Wie komme ich an api_id / api_hash?", ButtonSize);
        help.TabStop = false;
        help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        help.Location = new Point(top.Width - ButtonSize.Width - 16, 8);
        help.Click += (_, _) =>
        {
            using var h = new TelegramApiHelpForm();
            h.ShowDialog(this);
        };
        top.Controls.Add(help);

        // ---- PropertyTable ----
        _table = UIStyles.PropertyTables.CreateStandard();
        _table.Dock = DockStyle.Top;
        _table.Padding = new Padding(5);

        _apiId = Field(current.ApiId > 0 ? current.ApiId.ToString() : "");
        _apiHash = Field(current.ApiHash);
        _phone = Field(current.PhoneNumber);

        _downloadFolder = Field(current.DownloadFolder);
        _downloadFolder.ReadOnly = true;
        _downloadFolder.TabStop = false;

        _useDownloadFolder = UIStyles.ToggleSwitches.CreateStandard(
            current.UseDownloadFolder,
            "Downloads gehen ohne Nachfrage in diesen Ordner",
            "Bei jedem Download den Ordner wählen");
        _useDownloadFolder.TabStop = false;

        _clearCacheOnStart = UIStyles.ToggleSwitches.CreateStandard(
            current.ClearCacheOnStart,
            "Cache wird bei jedem Programmstart geleert",
            "Cache bleibt zwischen Sitzungen (nur Obergrenze greift)");
        _clearCacheOnStart.TabStop = false;

        Button browse = UIStyles.Buttons.CreateBrowse("Ordner wählen");
        browse.TabStop = false;
        browse.Click += (_, _) => Browse();

        _cacheSizeLabel = UIStyles.Labels.CreateNormal("");
        _clearCacheButton = UIStyles.Buttons.CreateRed("Cache leeren", size: ButtonSize);
        _clearCacheButton.TabStop = false;
        _clearCacheButton.Click += (_, _) => ClearCache();

        Button loginBtn = _isConnected
            ? UIStyles.Buttons.CreateStandard("Abmelden", "", ButtonSize)
            : UIStyles.Buttons.CreatePrimary("Anmelden", "", ButtonSize);
        loginBtn.TabStop = false;
        loginBtn.Click += (_, _) =>
        {
            if (_isConnected)
            {
                if (MessageBox.Show(
                        "Abmelden? Die gespeicherte Anmeldung wird gelöscht.\r\n" +
                        "Die Zugangsdaten bleiben erhalten.",
                        "Abmelden", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
                    != DialogResult.Yes)
                {
                    return;
                }
                Action = SettingsAction.Logout;
            }
            else
            {
                Action = SettingsAction.Connect;
            }
            Close();
        };

        Button wipe = UIStyles.Buttons.CreateRed("Löschen", size: ButtonSize);
        wipe.TabStop = false;
        wipe.Click += (_, _) => Wipe();

        // ---- Aufbau ----
        _table.AddSection("Telegram-API");
        AddLockedRow("api_id", _apiId);
        AddLockedRow("api_hash", _apiHash);
        AddLockedRow("Telefon", _phone);

        _table.AddSection("Anmeldung");
        _table.AddRow(
            "Status", RowH,
            UIColumn.Percent(Desc(_isConnected ? "angemeldet" : "nicht angemeldet"), 100),
            UIColumn.Absolute(loginBtn, ButtonColumn));

        _table.AddSection("Downloads");
        _table.AddRow(
            "Ordner", RowH,
            UIColumn.Percent(_downloadFolder, 100),
            UIColumn.Absolute(browse, 44),
            UIColumn.Absolute(_useDownloadFolder, 56));

        _table.AddSection("Cache");
        _table.AddRow(
            "Belegt", RowH,
            UIColumn.Percent(_cacheSizeLabel, 100),
            UIColumn.Absolute(_clearCacheOnStart, 56),
            UIColumn.Absolute(_clearCacheButton, ButtonColumn));

        _table.AddSection("Zugangsdaten");
        _table.AddRow(
            "Zurücksetzen", RowH,
            UIColumn.Percent(Desc("Daten löschen und abmelden"), 100),
            UIColumn.Absolute(wipe, ButtonColumn));

        _toolTip.SetToolTip(_downloadFolder, current.DownloadFolder);
        UpdateCacheLabel();

        // Tabelle einmal messen, dann auf feste Größe fixieren und in einem
        // helleren Bereich zentrieren - so gibt ein breiteres Fenster nur mehr
        // Freiraum, die Tabelle bleibt gleich groß.
        _table.PerformLayout();
        Size tableSize = _table.PreferredSize;

        _table.AutoSize = false;
        _table.Dock = DockStyle.None;
        _table.Size = new Size(Math.Max(540, tableSize.Width), tableSize.Height);
        _table.Anchor = AnchorStyles.Top;
        _table.Top = 16;

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UIStyles.Colors.BackgroundMediumElevated,
            AutoScroll = true
        };
        host.Controls.Add(_table);
        host.Resize += (_, _) =>
            _table.Left = Math.Max(16, (host.ClientSize.Width - _table.Width) / 2);

        ContentPanel.Controls.Add(host);
        ContentPanel.Controls.Add(top);

        ClientSize = new Size(
            _table.Width + 220,
            top.Height + _table.Height + TitleBar.Height + 40);

        void Recenter() =>
            _table.Left = Math.Max(16, (host.ClientSize.Width - _table.Width) / 2);

        Recenter();
        Shown += (_, _) => Recenter();

        FormClosing += (_, _) => Save();
    }

    private void AddLockedRow(string label, TextBox field)
    {
        field.ReadOnly = true;
        field.TabStop = false;

        Button lockBtn = UIStyles.Buttons.CreateStandard(Pencil, "Bearbeiten", new Size(40, 28));
        lockBtn.TabStop = false;

        // Solange angemeldet, dürfen die Zugangsdaten nicht geändert werden -
        // erst abmelden.
        if (_isConnected)
        {
            lockBtn.Enabled = false;
            _toolTip.SetToolTip(lockBtn, "Zum Ändern zuerst abmelden");
        }

        lockBtn.Click += (_, _) =>
        {
            if (field.ReadOnly)
            {
                field.ReadOnly = false;
                field.TabStop = true;
                lockBtn.Text = Check;
                field.Focus();
                field.SelectAll();
            }
            else
            {
                field.ReadOnly = true;
                field.TabStop = false;
                lockBtn.Text = Pencil;
            }
        };

        _table.AddRow(label, RowH, UIColumn.Percent(field, 100), UIColumn.Absolute(lockBtn, 44));
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = _downloadFolder.Text };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _downloadFolder.Text = dlg.SelectedPath;
            _toolTip.SetToolTip(_downloadFolder, dlg.SelectedPath);
        }
    }

    private void ClearCache()
    {
        (int count, long bytes) = _cache.GetStats();
        if (count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                $"{count} Dateien ({Mb(bytes)}) aus dem Cache löschen?",
                "Cache leeren", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
            == DialogResult.Yes)
        {
            _cache.Clear();
            UpdateCacheLabel();
        }
    }

    private void UpdateCacheLabel()
    {
        (int count, long bytes) = _cache.GetStats();
        string text = $"{Mb(bytes)} / {Mb(CachePolicy.LimitBytes)} ({count} Dateien)";
        _cacheSizeLabel.Text = text;
        _toolTip.SetToolTip(_cacheSizeLabel, text);
        _clearCacheButton.Enabled = count > 0;
    }

    private void Wipe()
    {
        if (MessageBox.Show(
                "Wirklich api_id, api_hash und Telefonnummer löschen und abmelden?",
                "Zugangsdaten löschen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this)
            == DialogResult.Yes)
        {
            Action = SettingsAction.Wipe;
            Close();
        }
    }

    private void Save()
    {
        if (Action == SettingsAction.Wipe)
        {
            return;
        }

        int.TryParse(_apiId.Text.Trim(), out int id);
        Result = new TelegramConfig(
            id,
            _apiHash.Text.Trim(),
            PhoneNumbers.ToPlusForm(_phone.Text),
            _clearCacheOnStart.Checked,
            _downloadFolder.Text.Trim(),
            _useDownloadFolder.Checked);
    }

    /// <summary>
    /// Borderless-Textfeld ohne Platzhalter (der Platzhalter-Mechanismus der
    /// Factory überschreibt sonst einen vorbelegten Wert).
    /// </summary>
    private static TextBox Field(string value)
    {
        TextBox tb = UIStyles.TextBoxes.CreateBorderstyleNone();
        tb.Text = value ?? "";
        return tb;
    }

    /// <summary>
    /// Beschreibungs-Label links neben einem Bedienelement – mit Tooltip auf
    /// den vollen Text, falls die Zelle ihn abschneidet.
    /// </summary>
    private Label Desc(string text)
    {
        Label l = UIStyles.Labels.CreateNormal(text);
        l.Anchor = AnchorStyles.Left;
        _toolTip.SetToolTip(l, text);
        return l;
    }

    private static string Mb(long bytes) => $"{bytes / 1024d / 1024d:0} MB";
}

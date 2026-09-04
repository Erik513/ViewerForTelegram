using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Styles;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
using MessageBox = ErikwnkWFUI.Forms.MessageBox;
using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;

namespace ViewerForTelegram.UI.Forms;

/// <summary>What the user triggered when closing the settings.</summary>
public enum SettingsAction
{
    /// <summary>Just apply the changes.</summary>
    None,

    /// <summary>Sign in (with the current credentials).</summary>
    Connect,

    /// <summary>Sign out - only delete the stored session.</summary>
    Logout,

    /// <summary>Delete credentials and session entirely.</summary>
    Wipe
}

/// <summary>
/// Settings: credentials (read-only, unlockable via the pencil), sign-in,
/// download folder, cache. No save button - applied automatically on close.
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

    private readonly ComboBox _language;
    private readonly TextBox _apiId;
    private readonly TextBox _apiHash;
    private readonly TextBox _phone;
    private readonly TextBox _downloadFolder;
    private readonly ToggleSwitch _useDownloadFolder;
    private readonly ToggleSwitch _clearCacheOnStart;
    private readonly Label _cacheSizeLabel;
    private readonly Button _clearCacheButton;
    private readonly bool _isConnected;

    /// <summary>Current state - filled from the fields on close.</summary>
    public TelegramConfig Result { get; private set; }

    /// <summary>What was triggered on close.</summary>
    public SettingsAction Action { get; private set; } = SettingsAction.None;

    public SettingsForm(TelegramConfig current, IMediaCache cache, bool isConnected)
        : base(StyledFormOptions.CreateDialog("Settings"))
    {
        _cache = cache;
        _isConnected = isConnected;
        Result = current;
        StartPosition = FormStartPosition.CenterParent;

        _toolTip = UIStyles.ToolTips.CreateToolTip();
        Disposed += (_, _) => _toolTip.Dispose();

        // Background a touch lighter than the PropertyTable (which sits on
        // BackgroundMedium) - so it stands out as a "card".
        ContentPanel.BackColor = UIStyles.Colors.BackgroundMediumElevated;

        // ---- help bar on top ----
        Panel top = UIStyles.Panels.CreateMedium();
        top.BackColor = UIStyles.Colors.BackgroundMediumElevated;
        top.Dock = DockStyle.Top;
        top.Height = 46;

        Button help = UIStyles.Buttons.CreatePrimary(
            "?  Guide", "How do I get api_id / api_hash?", ButtonSize);
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

        _language = UIStyles.ComboBoxes.CreateStandard();
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.TabStop = false;
        _language.Items.AddRange(new object[] { "English", "Deutsch" });
        _language.SelectedIndex = current.Language == DisplayLanguage.German ? 1 : 0;
        _toolTip.SetToolTip(_language,
            "Language of the library's built-in dialogs. Full effect after a restart.");

        _apiId = Field(current.ApiId > 0 ? current.ApiId.ToString() : "");
        _apiHash = Field(current.ApiHash);
        _phone = Field(current.PhoneNumber);

        _downloadFolder = Field(current.EffectiveDownloadFolder);
        _downloadFolder.ReadOnly = true;
        _downloadFolder.TabStop = false;

        _useDownloadFolder = UIStyles.ToggleSwitches.CreateStandard(
            current.UseDownloadFolder,
            "Downloads go to this folder without asking",
            "Pick the folder on every download");
        _useDownloadFolder.TabStop = false;

        _clearCacheOnStart = UIStyles.ToggleSwitches.CreateStandard(
            current.ClearCacheOnStart,
            "Cache is wiped on every startup",
            "Cache is kept between sessions (only the size limit applies)");
        _clearCacheOnStart.TabStop = false;

        Button browse = UIStyles.Buttons.CreateBrowse("Choose folder");
        browse.TabStop = false;
        browse.Click += (_, _) => Browse();

        _cacheSizeLabel = UIStyles.Labels.CreateNormal("");
        _clearCacheButton = UIStyles.Buttons.CreateRed("Clear cache", size: ButtonSize);
        _clearCacheButton.TabStop = false;
        _clearCacheButton.Click += (_, _) => ClearCache();

        TextBox cacheFolder = Field(AppPaths.CacheDir);
        cacheFolder.ReadOnly = true;
        cacheFolder.TabStop = false;
        _toolTip.SetToolTip(cacheFolder, AppPaths.CacheDir);

        Button cacheBrowse = UIStyles.Buttons.CreateBrowse("Open the cache folder");
        cacheBrowse.TabStop = false;
        cacheBrowse.Click += (_, _) => OpenCacheFolder();

        Button loginBtn = _isConnected
            ? UIStyles.Buttons.CreatePrimary("Sign out", "", ButtonSize)
            : UIStyles.Buttons.CreatePrimary("Sign in", "", ButtonSize);
        loginBtn.TabStop = false;
        loginBtn.Click += (_, _) =>
        {
            if (_isConnected)
            {
                if (MessageBox.Show(
                        "Sign out? The stored login is deleted.\r\n" +
                        "The credentials are kept.",
                        "Sign out", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
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

        Button wipe = UIStyles.Buttons.CreateRed("Delete", size: ButtonSize);
        wipe.TabStop = false;
        wipe.Click += (_, _) => Wipe();

        // ---- layout ----
        _table.AddSection("General");
        _table.AddRow("Language", RowH, UIColumn.Percent(_language, 100));

        _table.AddSection("Telegram API");
        AddLockedRow("api_id", _apiId);
        AddLockedRow("api_hash", _apiHash);
        AddLockedRow("Phone", _phone);

        _table.AddSection("Sign-in");
        _table.AddRow(
            "Status", RowH,
            UIColumn.Percent(Desc(_isConnected ? "signed in" : "not signed in"), 100),
            UIColumn.Absolute(loginBtn, ButtonColumn));

        _table.AddSection("Downloads");
        _table.AddRow(
            "Folder", RowH,
            UIColumn.Percent(_downloadFolder, 100),
            UIColumn.Absolute(browse, 44),
            UIColumn.Absolute(_useDownloadFolder, 56));

        _table.AddSection("Cache");
        _table.AddRow(
            "Used", RowH,
            UIColumn.Percent(_cacheSizeLabel, 100),
            UIColumn.Absolute(_clearCacheOnStart, 56));
        _table.AddRow(
            "Folder", RowH,
            UIColumn.Percent(cacheFolder, 100),
            UIColumn.Absolute(cacheBrowse, 44),
            UIColumn.Absolute(_clearCacheButton, ButtonColumn));

        _table.AddSection("Credentials");
        _table.AddRow(
            "Reset", RowH,
            UIColumn.Percent(Desc("Delete data and sign out"), 100),
            UIColumn.Absolute(wipe, ButtonColumn));

        _toolTip.SetToolTip(_downloadFolder, current.EffectiveDownloadFolder);
        UpdateCacheLabel();

        // Measure the table once, then fix it to a static size and center it in
        // a lighter area - so a wider window only adds whitespace, the table
        // stays the same size.
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

        Button lockBtn = UIStyles.Buttons.CreatePrimary(Pencil, "Edit", new Size(40, 28));
        lockBtn.TabStop = false;

        // While signed in, the credentials must not be changed - sign out first.
        if (_isConnected)
        {
            lockBtn.Enabled = false;
            _toolTip.SetToolTip(lockBtn, "Sign out first to change this");
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

    private void OpenCacheFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.CacheDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = AppPaths.CacheDir, UseShellExecute = true });
        }
        catch
        {
            // opening a folder is a convenience - never worth an error dialog
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
                $"Delete {count} files ({Mb(bytes)}) from the cache?",
                "Clear cache", MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
            == DialogResult.Yes)
        {
            _cache.Clear();
            UpdateCacheLabel();
        }
    }

    private void UpdateCacheLabel()
    {
        (int count, long bytes) = _cache.GetStats();
        string text = $"{Mb(bytes)} / {Mb(CachePolicy.LimitBytes)} ({count} files)";
        _cacheSizeLabel.Text = text;
        _toolTip.SetToolTip(_cacheSizeLabel, text);
        _clearCacheButton.Enabled = count > 0;
    }

    private void Wipe()
    {
        if (MessageBox.Show(
                "Really delete api_id, api_hash and phone number and sign out?",
                "Delete credentials", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this)
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

        DisplayLanguage language =
            _language.SelectedIndex == 1 ? DisplayLanguage.German : DisplayLanguage.English;
        // Apply right away for any dialog opened afterwards; the main window
        // picks it up fully on the next start.
        UIStyles.Language = language == DisplayLanguage.German ? UILanguage.German : UILanguage.English;

        // Store blank while the field still shows the OS Downloads folder, so a
        // later move of that folder keeps being followed.
        string folder = _downloadFolder.Text.Trim();
        if (string.Equals(folder, AppPaths.DownloadsFolder, StringComparison.OrdinalIgnoreCase))
        {
            folder = "";
        }

        Result = new TelegramConfig(
            id,
            _apiHash.Text.Trim(),
            PhoneNumbers.ToPlusForm(_phone.Text),
            _clearCacheOnStart.Checked,
            folder,
            _useDownloadFolder.Checked,
            language);
    }

    /// <summary>
    /// Borderless text field without a placeholder (the factory's placeholder
    /// mechanism would otherwise overwrite a pre-filled value).
    /// </summary>
    private static TextBox Field(string value)
    {
        TextBox tb = UIStyles.TextBoxes.CreateBorderstyleNone();
        tb.Text = value ?? "";
        return tb;
    }

    /// <summary>
    /// Description label to the left of a control - with a tooltip on the full
    /// text in case the cell truncates it.
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

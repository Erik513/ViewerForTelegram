using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
using ViewerForTelegram.UI.Localization;
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
/// Settings: language, credentials (read-only, unlockable via the pencil),
/// sign-in, download folder, cache. No save button - applied automatically on
/// close. Every string comes from <see cref="Loc"/> and refreshes live when the
/// language changes.
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
    private readonly Panel _host;
    private readonly ToolTip _toolTip;

    private readonly ComboBox _language;
    private readonly TextBox _apiId;
    private readonly TextBox _apiHash;
    private readonly TextBox _phone;
    private readonly TextBox _downloadFolder;
    private readonly TextBox _cacheFolder;
    private readonly ToggleSwitch _useDownloadFolder;
    private readonly ToggleSwitch _clearCacheOnStart;
    private readonly Label _cacheSizeLabel;
    private readonly Button _help;
    private readonly Button _browse;
    private readonly Button _cacheBrowse;
    private readonly Button _clearCacheButton;
    private readonly Button _loginBtn;
    private readonly Button _wipe;
    private readonly bool _isConnected;

    /// <summary>Current state - filled from the fields on close.</summary>
    public TelegramConfig Result { get; private set; }

    /// <summary>What was triggered on close.</summary>
    public SettingsAction Action { get; private set; } = SettingsAction.None;

    public SettingsForm(TelegramConfig current, IMediaCache cache, bool isConnected)
        : base(StyledFormOptions.CreateDialog(Loc.S("settings.title")))
    {
        _cache = cache;
        _isConnected = isConnected;
        Result = current;
        StartPosition = FormStartPosition.CenterParent;

        _toolTip = UIStyles.ToolTips.CreateToolTip();

        // Background a touch lighter than the PropertyTable (which sits on
        // BackgroundMedium) - so it stands out as a "card".
        ContentPanel.BackColor = UIStyles.Colors.BackgroundMediumElevated;

        // ---- help bar on top ----
        Panel top = UIStyles.Panels.CreateMedium();
        top.BackColor = UIStyles.Colors.BackgroundMediumElevated;
        top.Dock = DockStyle.Top;
        top.Height = 46;

        _help = UIStyles.Buttons.CreatePrimary(Loc.S("settings.guide"), Loc.S("settings.guide.tip"), ButtonSize);
        _help.TabStop = false;
        _help.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _help.Location = new Point(top.Width - ButtonSize.Width - 16, 8);
        _help.Click += (_, _) =>
        {
            using var h = new TelegramApiHelpForm();
            h.ShowDialog(this);
        };
        top.Controls.Add(_help);

        // ---- PropertyTable ----
        _table = UIStyles.PropertyTables.CreateStandard();
        _table.Dock = DockStyle.Top;
        _table.Padding = new Padding(5);

        _language = UIStyles.ComboBoxes.CreateStandard();
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.TabStop = false;
        _language.Items.AddRange(new object[] { "English", "Deutsch" });
        _language.SelectedIndex = current.Language == AppLanguage.German ? 1 : 0;
        _language.SelectedIndexChanged += (_, _) =>
            // Switch the whole UI right now (Loc.Changed -> ApplyTexts here too).
            Loc.Current = _language.SelectedIndex == 1
                ? AppLanguage.German
                : AppLanguage.English;

        _apiId = Field(current.ApiId > 0 ? current.ApiId.ToString() : "");
        _apiHash = Field(current.ApiHash);
        _phone = Field(current.PhoneNumber);

        _downloadFolder = Field(current.EffectiveDownloadFolder);
        _downloadFolder.ReadOnly = true;
        _downloadFolder.TabStop = false;

        _useDownloadFolder = UIStyles.ToggleSwitches.CreateStandard(
            current.UseDownloadFolder,
            Loc.S("settings.toggle.folder.on"),
            Loc.S("settings.toggle.folder.off"));
        _useDownloadFolder.TabStop = false;

        _clearCacheOnStart = UIStyles.ToggleSwitches.CreateStandard(
            current.ClearCacheOnStart,
            Loc.S("settings.toggle.clearCache.on"),
            Loc.S("settings.toggle.clearCache.off"));
        _clearCacheOnStart.TabStop = false;

        _browse = UIStyles.Buttons.CreateBrowse(Loc.S("settings.btn.chooseFolder"));
        _browse.TabStop = false;
        _browse.Click += (_, _) => Browse();

        _cacheSizeLabel = UIStyles.Labels.CreateNormal("");
        _clearCacheButton = UIStyles.Buttons.CreateRed(Loc.S("settings.btn.clearCache"), size: ButtonSize);
        _clearCacheButton.TabStop = false;
        _clearCacheButton.Click += (_, _) => ClearCache();

        _cacheFolder = Field(AppPaths.CacheDir);
        _cacheFolder.ReadOnly = true;
        _cacheFolder.TabStop = false;
        _toolTip.SetToolTip(_cacheFolder, AppPaths.CacheDir);

        _cacheBrowse = UIStyles.Buttons.CreateBrowse(Loc.S("settings.btn.openCache"));
        _cacheBrowse.TabStop = false;
        _cacheBrowse.Click += (_, _) => OpenCacheFolder();

        _loginBtn = UIStyles.Buttons.CreatePrimary("", "", ButtonSize);
        _loginBtn.TabStop = false;
        _loginBtn.Click += (_, _) =>
        {
            if (_isConnected)
            {
                if (MessageBox.Show(
                        Loc.S("settings.msg.signout.body"),
                        Loc.S("settings.msg.signout.title"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
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

        _wipe = UIStyles.Buttons.CreateRed(Loc.S("settings.btn.delete"), size: ButtonSize);
        _wipe.TabStop = false;
        _wipe.Click += (_, _) => Wipe();

        _toolTip.SetToolTip(_downloadFolder, current.EffectiveDownloadFolder);

        BuildTable();
        FixTableSize();

        _host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UIStyles.Colors.BackgroundMediumElevated,
            AutoScroll = true
        };
        _host.Controls.Add(_table);
        _host.Resize += (_, _) => Recenter();

        ContentPanel.Controls.Add(_host);
        ContentPanel.Controls.Add(top);

        ClientSize = new Size(
            _table.Width + 220,
            top.Height + _table.Height + TitleBar.Height + 40);

        ApplyTexts();
        Recenter();
        Loc.Changed += OnLanguageChanged;
        Shown += (_, _) => Recenter();

        FormClosing += (_, _) => Save();
        Disposed += (_, _) =>
        {
            Loc.Changed -= OnLanguageChanged;
            _toolTip.Dispose();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyTexts();
        FixTableSize();
        Recenter();
    }

    private void Recenter() =>
        _table.Left = Math.Max(16, (_host.ClientSize.Width - _table.Width) / 2);

    private void FixTableSize()
    {
        _table.AutoSize = true;
        _table.Dock = DockStyle.Top;
        _table.PerformLayout();
        Size size = _table.PreferredSize;

        _table.AutoSize = false;
        _table.Dock = DockStyle.None;
        _table.Size = new Size(Math.Max(540, size.Width), size.Height);
        _table.Anchor = AnchorStyles.Top;
        _table.Top = 16;
    }

    /// <summary>(Re-)builds the sections and rows with the current language.</summary>
    private void BuildTable()
    {
        _table.ClearRows();

        _table.AddSection(Loc.S("settings.sec.general"));
        _table.AddRow(Loc.S("settings.row.language"), RowH, UIColumn.Percent(_language, 100));

        _table.AddSection(Loc.S("settings.sec.api"));
        AddLockedRow("api_id", _apiId);
        AddLockedRow("api_hash", _apiHash);
        AddLockedRow(Loc.S("settings.row.phone"), _phone);

        _table.AddSection(Loc.S("settings.sec.signin"));
        _table.AddRow(
            Loc.S("settings.row.status"), RowH,
            UIColumn.Percent(Desc(_isConnected
                ? Loc.S("settings.status.connected")
                : Loc.S("settings.status.disconnected")), 100),
            UIColumn.Absolute(_loginBtn, ButtonColumn));

        _table.AddSection(Loc.S("settings.sec.downloads"));
        _table.AddRow(
            Loc.S("settings.row.folder"), RowH,
            UIColumn.Percent(_downloadFolder, 100),
            UIColumn.Absolute(_browse, 44),
            UIColumn.Absolute(_useDownloadFolder, 56));

        _table.AddSection(Loc.S("settings.sec.cache"));
        _table.AddRow(
            Loc.S("settings.row.used"), RowH,
            UIColumn.Percent(_cacheSizeLabel, 100),
            UIColumn.Absolute(_clearCacheOnStart, 56));
        _table.AddRow(
            Loc.S("settings.row.folder"), RowH,
            UIColumn.Percent(_cacheFolder, 100),
            UIColumn.Absolute(_cacheBrowse, 44),
            UIColumn.Absolute(_clearCacheButton, ButtonColumn));

        _table.AddSection(Loc.S("settings.sec.credentials"));
        _table.AddRow(
            Loc.S("settings.row.reset"), RowH,
            UIColumn.Percent(Desc(Loc.S("settings.reset.desc")), 100),
            UIColumn.Absolute(_wipe, ButtonColumn));

        UpdateCacheLabel();
    }

    /// <summary>(Re-)applies strings that live outside the table rows.</summary>
    private void ApplyTexts()
    {
        FormTitle = Loc.S("settings.title");
        _help.Text = Loc.S("settings.guide");
        _toolTip.SetToolTip(_help, Loc.S("settings.guide.tip"));

        _loginBtn.Text = _isConnected ? Loc.S("settings.btn.signout") : Loc.S("settings.btn.signin");
        _clearCacheButton.Text = Loc.S("settings.btn.clearCache");
        _wipe.Text = Loc.S("settings.btn.delete");

        // CreateBrowse keeps a folder glyph in .Text - only its tooltip is text.
        UIStyles.Buttons.UpdateTooltip(_browse, Loc.S("settings.btn.chooseFolder"));
        UIStyles.Buttons.UpdateTooltip(_cacheBrowse, Loc.S("settings.btn.openCache"));

        _useDownloadFolder.ToolTipTextChecked = Loc.S("settings.toggle.folder.on");
        _useDownloadFolder.ToolTipTextUnchecked = Loc.S("settings.toggle.folder.off");
        _clearCacheOnStart.ToolTipTextChecked = Loc.S("settings.toggle.clearCache.on");
        _clearCacheOnStart.ToolTipTextUnchecked = Loc.S("settings.toggle.clearCache.off");

        BuildTable();
    }

    private void AddLockedRow(string label, TextBox field)
    {
        field.ReadOnly = true;
        field.TabStop = false;

        Button lockBtn = UIStyles.Buttons.CreatePrimary(
            field.ReadOnly ? Pencil : Check, Loc.S("settings.lock.tip"), new Size(40, 28));
        lockBtn.TabStop = false;

        // While signed in, the credentials must not be changed - sign out first.
        if (_isConnected)
        {
            lockBtn.Enabled = false;
            _toolTip.SetToolTip(lockBtn, Loc.S("settings.lock.tip.locked"));
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
                Loc.T("settings.msg.clearCache.body", count, Mb(bytes)),
                Loc.S("settings.msg.clearCache.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
            == DialogResult.Yes)
        {
            _cache.Clear();
            UpdateCacheLabel();
            ToastForm.ShowToast(Loc.T("toast.cacheCleared", count, Mb(bytes)), this);
        }
    }

    private void UpdateCacheLabel()
    {
        (int count, long bytes) = _cache.GetStats();
        string text = Loc.T("settings.cache.used", Mb(bytes), Mb(CachePolicy.LimitBytes), count);
        _cacheSizeLabel.Text = text;
        _toolTip.SetToolTip(_cacheSizeLabel, text);
        _clearCacheButton.Enabled = count > 0;
    }

    private void Wipe()
    {
        if (MessageBox.Show(
                Loc.S("settings.msg.wipe.body"),
                Loc.S("settings.msg.wipe.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, this)
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

        AppLanguage language =
            _language.SelectedIndex == 1 ? AppLanguage.German : AppLanguage.English;

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

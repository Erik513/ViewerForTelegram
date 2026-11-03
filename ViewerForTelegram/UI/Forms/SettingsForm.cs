using ErikwnkCore;
using ErikwnkWFUI;
using ErikwnkWFUI.Controls;
using ErikwnkWFUI.Forms;
using ErikwnkWFUI.Helpers;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Data.Models;
using ViewerForTelegram.Logic;
using ViewerForTelegram.UI.Localization;
using ViewerForTelegram.UI;
using MessageBox = ErikwnkWFUI.Forms.MessageBox;
using MessageBoxButtons = ErikwnkWFUI.Forms.MessageBoxButtons;
using MessageBoxIcon = ErikwnkWFUI.Forms.MessageBoxIcon;
using ComboBox = System.Windows.Forms.ComboBox;
using Panel = System.Windows.Forms.Panel;

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
public sealed class SettingsForm : OptionsForm
{
    private const int RowH = 40;

    private static readonly Size SymbolSize = new(44, 30);
    private const int ButtonColumn = 44;

    private readonly IMediaCache _cache;
    private readonly PropertyTable _table;
    private readonly ToolTip _toolTip;

    private readonly ComboBox _language;
    private readonly LockableTextBox _apiId;
    private readonly LockableTextBox _apiHash;
    private readonly LockableTextBox _phone;
    private readonly TextBox _downloadFolder;
    private readonly TextBox _cacheFolder;
    private readonly ToggleSwitch _useDownloadFolder;
    private readonly ToggleSwitch _clearCacheOnStart;
    private readonly Label _cacheSizeLabel;
    private readonly Button _help;
    private Button? _themeButton;
    private readonly Button _browse;
    private readonly Button _cacheBrowse;
    private readonly Button _clearCacheButton;
    private readonly Button _loginBtn;
    private readonly Button _wipe;
    private Label _statusDesc = null!;   // set in BuildTable
    private Label _resetDesc = null!;
    private readonly bool _isConnected;

    /// <summary>Current state - filled from the fields on close.</summary>
    public TelegramConfig Result { get; private set; }

    /// <summary>What was triggered on close.</summary>
    public SettingsAction Action { get; private set; } = SettingsAction.None;

    /// <summary>The user cleared the media cache while this dialog was open.</summary>
    public bool CacheCleared { get; private set; }

    public SettingsForm(TelegramConfig current, IMediaCache cache, bool isConnected)
        : base(
            FormOptions.CreateSettings(
                Loc.S("settings.title"), icon: AppAssets.TitleBarLogo, windowIcon: AppAssets.WindowIcon),
            // The library's own option we use: the theme button in the corner. Language
            // is the combo box below (the app keeps it in its config); accent color,
            // always on top and start with Windows make no sense for this app.
            new[] { OptionsPreset.Theme })
    {
        ShowButtons = false;   // changes apply at once and are stored on close, as before
        ShowToasts = false;
        _table = PropertyTable;

        _cache = cache;
        _isConnected = isConnected;
        Result = current;
        StartPosition = FormStartPosition.CenterParent;

        _toolTip = UIStyles.ToolTips.CreateToolTip();
        _toolTip.ReviveOnFormActivate(this);

        // ---- help button: next to the theme button, same size ----
        _help = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Help.WithColors(UIStyles.Colors.Sets.Primary), Loc.S("settings.guide.tip"));
        _help.TabStop = false;
        _help.Click += (_, _) =>
        {
            using var h = new TelegramApiHelpForm();
            h.ShowDialog(this);
        };
        PlaceHelpNextToThemeButton();

        _language = UIStyles.ComboBoxes.CreatePrimary();
        _language.DropDownStyle = ComboBoxStyle.DropDownList;
        _language.TabStop = false;
        _language.Items.AddRange(new object[] { "English", "Deutsch" });
        _language.SelectedIndex = current.Language == AppLanguage.German ? 1 : 0;
        _language.SelectedIndexChanged += (_, _) =>
        {
            AppLanguage chosen = _language.SelectedIndex == 1
                ? AppLanguage.German
                : AppLanguage.English;

            // The switch rebuilds the table, which takes this very combo box out
            // of its cell and puts it back. Doing that inside its own selection
            // event (dropdown still closing) left it unusable - so let the event
            // finish first, then switch the whole UI (Loc.Changed -> ApplyTexts).
            BeginInvoke(() =>
            {
                if (Loc.Current != chosen)
                {
                    Loc.Current = chosen;
                }
            });
        };

        _apiId = LockedField(current.ApiId > 0 ? current.ApiId.ToString() : "");
        _apiHash = LockedField(current.ApiHash);
        _phone = LockedField(current.PhoneNumber);

        _downloadFolder = Field(current.EffectiveDownloadFolder);
        _downloadFolder.ReadOnly = true;
        _downloadFolder.TabStop = false;

        _useDownloadFolder = UIStyles.ToggleSwitches.CreatePrimary(
            current.UseDownloadFolder,
            Loc.S("settings.toggle.folder.on"),
            Loc.S("settings.toggle.folder.off"));
        _useDownloadFolder.TabStop = false;

        _clearCacheOnStart = UIStyles.ToggleSwitches.CreatePrimary(
            current.ClearCacheOnStart,
            Loc.S("settings.toggle.clearCache.on"),
            Loc.S("settings.toggle.clearCache.off"));
        _clearCacheOnStart.TabStop = false;

        _browse = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Browse, Loc.S("settings.btn.chooseFolder"));
        _browse.TabStop = false;
        _browse.Click += (_, _) => Browse();

        _cacheSizeLabel = UIStyles.Labels.CreateStandard("");
        _clearCacheButton = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Remove, Loc.S("settings.btn.clearCache"));
        _clearCacheButton.TabStop = false;
        _clearCacheButton.Click += (_, _) => ClearCache();

        _cacheFolder = Field(AppPaths.CacheDir);
        _cacheFolder.ReadOnly = true;
        _cacheFolder.TabStop = false;
        _toolTip.SetToolTip(_cacheFolder, AppPaths.CacheDir);

        _cacheBrowse = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Browse, Loc.S("settings.btn.openCache"));
        _cacheBrowse.TabStop = false;
        _cacheBrowse.Click += (_, _) => OpenCacheFolder();

        _loginBtn = UIStyles.Buttons.CreateSymbol(
            (isConnected ? UIStyles.Buttons.Presets.Logout : UIStyles.Buttons.Presets.Login)
                .WithColors(UIStyles.Colors.Sets.Primary), "");
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

        _wipe = UIStyles.Buttons.CreateSymbol(UIStyles.Buttons.Presets.Delete, Loc.S("settings.btn.delete"));
        _wipe.TabStop = false;
        _wipe.Click += (_, _) => Wipe();

        _toolTip.SetToolTip(_downloadFolder, current.EffectiveDownloadFolder);

        BuildTable();

        ApplyTexts();
        FitToContent();
        Loc.Changed += OnLanguageChanged;

        FormClosing += (_, _) => Save();
        Disposed += (_, _) =>
        {
            Loc.Changed -= OnLanguageChanged;
            _toolTip.Dispose();
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        this.WithRedrawSuspended(() =>
        {
            ApplyTexts();
            FitToContent();
        });

    // The library puts its theme button in a strip at the top right. The help button
    // goes into the same strip, to the left of it, as big as it.
    private void PlaceHelpNextToThemeButton()
    {
        Panel? strip = ContentPanel.Controls.OfType<Panel>()
            .FirstOrDefault(p => p.Dock == DockStyle.Top && p.Controls.OfType<Button>().Any());
        Button? theme = strip?.Controls.OfType<Button>().FirstOrDefault();
        if (strip is null || theme is null)
        {
            return;
        }

        _themeButton = theme;
        strip.Controls.Add(_help);

        void Place()
        {
            _help.Size = theme.Size;
            _help.Location = new Point(theme.Left - _help.Width - 6, theme.Top);
        }

        Place();
        theme.LocationChanged += (_, _) => Place();
        theme.SizeChanged += (_, _) => Place();
    }

    /// <summary>
    /// Builds the sections and rows once. Their labels are given as functions, so the
    /// table follows a language switch by itself - the rows (and the language combo
    /// box in them) are never torn down and put back.
    /// </summary>
    private void BuildTable()
    {
        _table.AddSection(() => Loc.S("settings.sec.general"));
        _table.AddRow(() => Loc.S("settings.row.language"), RowH, PropertyColumn.Percent(_language, 100));

        _table.AddSection(() => Loc.S("settings.sec.api"));
        AddLockedRow(() => "api_id", _apiId);
        AddLockedRow(() => "api_hash", _apiHash);
        AddLockedRow(() => Loc.S("settings.row.phone"), _phone);

        _statusDesc = Desc("");
        _table.AddSection(() => Loc.S("settings.sec.signin"));
        _table.AddRow(
            () => Loc.S("settings.row.status"), RowH,
            PropertyColumn.Percent(_statusDesc, 100),
            PropertyColumn.Absolute(_loginBtn, ButtonColumn));

        _table.AddSection(() => Loc.S("settings.sec.downloads"));
        _table.AddRow(
            () => Loc.S("settings.row.folder"), RowH,
            PropertyColumn.Percent(_downloadFolder, 100),
            PropertyColumn.Absolute(_browse, 44),
            PropertyColumn.Absolute(_useDownloadFolder, 56));

        _table.AddSection(() => Loc.S("settings.sec.cache"));
        _table.AddRow(
            () => Loc.S("settings.row.used"), RowH,
            PropertyColumn.Percent(_cacheSizeLabel, 100),
            PropertyColumn.Absolute(_clearCacheOnStart, 56));
        _table.AddRow(
            () => Loc.S("settings.row.folder"), RowH,
            PropertyColumn.Percent(_cacheFolder, 100),
            PropertyColumn.Absolute(_cacheBrowse, 44),
            PropertyColumn.Absolute(_clearCacheButton, ButtonColumn));

        _resetDesc = Desc("");
        _table.AddSection(() => Loc.S("settings.sec.credentials"));
        _table.AddRow(
            () => Loc.S("settings.row.reset"), RowH,
            PropertyColumn.Percent(_resetDesc, 100),
            PropertyColumn.Absolute(_wipe, ButtonColumn));

        UpdateCacheLabel();
    }

    /// <summary>(Re-)applies strings that live outside the table rows.</summary>
    private void ApplyTexts()
    {
        FormTitle = Loc.S("settings.title");
        // All of these are symbol buttons - only their tooltip is text.
        UIStyles.Buttons.UpdateTooltip(_help, Loc.S("settings.guide.tip"));
        UIStyles.Buttons.UpdateTooltip(_loginBtn, _isConnected ? Loc.S("settings.btn.signout") : Loc.S("settings.btn.signin"));
        UIStyles.Buttons.UpdateTooltip(_clearCacheButton, Loc.S("settings.btn.clearCache"));
        UIStyles.Buttons.UpdateTooltip(_wipe, Loc.S("settings.btn.delete"));

        // The browse buttons are symbol buttons - only their tooltip is text.
        UIStyles.Buttons.UpdateTooltip(_browse, Loc.S("settings.btn.chooseFolder"));
        UIStyles.Buttons.UpdateTooltip(_cacheBrowse, Loc.S("settings.btn.openCache"));

        _useDownloadFolder.ToolTipTextChecked = Loc.S("settings.toggle.folder.on");
        _useDownloadFolder.ToolTipTextUnchecked = Loc.S("settings.toggle.folder.off");
        _clearCacheOnStart.ToolTipTextChecked = Loc.S("settings.toggle.clearCache.on");
        _clearCacheOnStart.ToolTipTextUnchecked = Loc.S("settings.toggle.clearCache.off");

        SetDesc(_statusDesc, _isConnected
            ? Loc.S("settings.status.connected")
            : Loc.S("settings.status.disconnected"));
        SetDesc(_resetDesc, Loc.S("settings.reset.desc"));
        if (_isConnected)
        {
            foreach (LockableTextBox field in new[] { _apiId, _apiHash, _phone })
            {
                _toolTip.SetToolTip(field, Loc.S("settings.lock.tip.locked"));
            }
        }
        UpdateCacheLabel();
    }

    private void AddLockedRow(Func<string> label, LockableTextBox field)
    {
        // While signed in, the credentials must not be changed - sign out first.
        if (_isConnected)
        {
            field.Enabled = false;
            _toolTip.SetToolTip(field, Loc.S("settings.lock.tip.locked"));
        }

        _table.AddRow(label, RowH, PropertyColumn.Percent(field, 100));
    }

    private void Browse()
    {
        // Both: InitialDirectory makes the Vista-style dialog actually navigate
        // there, SelectedPath makes it the pre-selected node.
        using var dlg = new FolderBrowserDialog
        {
            InitialDirectory = _downloadFolder.Text,
            SelectedPath = _downloadFolder.Text
        };
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
            IoUtil.OpenFolder(AppPaths.CacheDir);
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
                Loc.T("settings.msg.clearCache.body", Loc.Files(count), Mb(bytes)),
                Loc.S("settings.msg.clearCache.title"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, this)
            == DialogResult.Yes)
        {
            _cache.Clear();
            CacheCleared = true;
            UpdateCacheLabel();
            ToastForm.ShowToast(Loc.T("toast.cacheCleared", Loc.Files(count), Mb(bytes)), this);
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

        UIStyles.SaveAppearance();   // the theme and accent chosen in the corner / the options rows

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
    private static LockableTextBox LockedField(string value)
    {
        LockableTextBox tb = UIStyles.TextBoxes.CreateLockable(value ?? "");
        tb.TabStop = false;
        return tb;
    }

    private static TextBox Field(string value)
    {
        TextBox tb = UIStyles.TextBoxes.CreateBorderless();
        tb.Text = value ?? "";
        return tb;
    }

    /// <summary>
    /// Description label to the left of a control - with a tooltip on the full
    /// text in case the cell truncates it.
    /// </summary>
    private Label Desc(string text)
    {
        Label l = UIStyles.Labels.CreateStandard(text);
        l.Anchor = AnchorStyles.Left;
        _toolTip.SetToolTip(l, text);
        return l;
    }

    private void SetDesc(Label l, string text)
    {
        l.Text = text;
        _toolTip.SetToolTip(l, text);
    }

    private static string Mb(long bytes) => $"{bytes / 1024d / 1024d:0} MB";
}

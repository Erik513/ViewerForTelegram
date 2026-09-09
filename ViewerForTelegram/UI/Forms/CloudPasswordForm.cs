using System.Drawing;
using System.Windows.Forms;
using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.UI.Localization;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Modal prompt for the Telegram cloud password (two-factor auth). Requested by
/// <see cref="Data.TelegramSource"/> through a callback and shown again with a
/// "wrong password" line if the previous attempt failed. The value is handed
/// back and never stored.
/// </summary>
public sealed class CloudPasswordForm : StyledForm
{
    // Segoe MDL2 Assets: RedEye (E7B3) / Hide (ED1A) - the same glyphs Windows
    // uses for its own password reveal button.
    private static readonly string EyeShow = char.ConvertFromUtf32(0xE7B3);
    private static readonly string EyeHide = char.ConvertFromUtf32(0xED1A);

    private readonly TextBox _passwordBox;

    /// <summary>The entered password, or <c>null</c> if the dialog was cancelled.</summary>
    public string? Password { get; private set; }

    public CloudPasswordForm(string? hint, bool retry)
        : base(StyledFormOptions.CreateDialog(
            Loc.S("pwd.title"),
            titleTextAlign: ContentAlignment.MiddleLeft,
            icon: AppAssets.TitleBarLogo,
            windowIcon: AppAssets.WindowIcon))
    {
        const int width = 380;
        const int x = 24;
        const int w = width - 2 * x;   // 332

        Label prompt = UIStyles.Labels.CreateNormal(retry ? Loc.S("pwd.retry") : Loc.S("pwd.prompt"));
        prompt.SetBounds(x, 14, w, 44);
        if (retry)
        {
            prompt.ForeColor = UIStyles.Colors.RedLight;
        }

        int y = 66;

        Label? hintLabel = null;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            hintLabel = UIStyles.Labels.CreateMuted(Loc.T("pwd.hint", hint));
            hintLabel.SetBounds(x, 60, w, 18);
            y = 84;
        }

        _passwordBox = UIStyles.TextBoxes.CreateStandard();
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.SetBounds(x, y, w - 40, 30);

        var eye = UIStyles.Buttons.CreateStandard("", Loc.S("pwd.show"), new Size(34, 30));
        eye.Font = new Font("Segoe MDL2 Assets", 10f);
        eye.Text = EyeShow;
        eye.TabStop = false;
        eye.SetBounds(x + w - 34, y, 34, 30);
        eye.Click += (_, _) =>
        {
            bool masked = !_passwordBox.UseSystemPasswordChar;
            _passwordBox.UseSystemPasswordChar = masked;
            eye.Text = masked ? EyeShow : EyeHide;
            UIStyles.Buttons.UpdateTooltip(eye, Loc.S(masked ? "pwd.show" : "pwd.hide"));
            _passwordBox.Focus();
        };

        y += 46;

        Button ok = UIStyles.Buttons.CreatePrimary(Loc.S("pwd.confirm"));
        ok.SetBounds(x, y, w, 42);
        ok.Click += (_, _) =>
        {
            if (_passwordBox.Text.Length == 0)
            {
                return;
            }
            Password = _passwordBox.Text;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = ok;

        Size = new Size(width, y + 42 + 24 + 30);   // + bottom padding + title bar
        StartPosition = FormStartPosition.CenterScreen;

        var controls = new System.Collections.Generic.List<Control> { prompt, _passwordBox, eye, ok };
        if (hintLabel != null)
        {
            controls.Add(hintLabel);
        }
        ContentPanel.Controls.AddRange(controls.ToArray());

        ActiveControl = _passwordBox;
    }
}

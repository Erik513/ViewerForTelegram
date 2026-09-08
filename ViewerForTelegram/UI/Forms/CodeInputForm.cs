using ErikwnkWFUI;
using ErikwnkWFUI.Forms;
using ViewerForTelegram.UI.Localization;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Small modal dialog for the Telegram login code. Requested by
/// <see cref="Data.TelegramSource"/> through a callback, so the Data layer
/// itself needs to know nothing about windows.
/// </summary>
public sealed class CodeInputForm : StyledForm
{
    private readonly TextBox _codeBox;

    /// <summary>The entered code, or null on cancel.</summary>
    public string? Code { get; private set; }

    public CodeInputForm()
        : base(StyledFormOptions.CreateDialog(
            Loc.S("code.title"), icon: AppAssets.TitleBarLogo, windowIcon: AppAssets.WindowIcon))
    {
        Size = new Size(380, 230);
        StartPosition = FormStartPosition.CenterScreen;

        Label label = UIStyles.Labels.CreateNormal(Loc.S("code.prompt"));
        label.SetBounds(24, 16, 320, 48);

        _codeBox = UIStyles.TextBoxes.CreateStandard("", "12345");
        _codeBox.SetBounds(24, 74, 320, 30);

        Button okButton = UIStyles.Buttons.CreatePrimary(Loc.S("code.confirm"));
        okButton.SetBounds(24, 122, 320, 42);
        okButton.Click += (_, _) =>
        {
            string code = _codeBox.Text.Trim();
            if (code.Length == 0)
            {
                return;
            }

            Code = code;
            DialogResult = DialogResult.OK;
        };

        ContentPanel.Controls.AddRange(new Control[] { label, _codeBox, okButton });
    }
}

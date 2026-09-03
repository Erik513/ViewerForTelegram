using ErikwnkWFUI;
using ErikwnkWFUI.Forms;

namespace ViewerForTelegram.UI.Forms;

/// <summary>
/// Kleiner modaler Dialog für den Telegram-Login-Code. Wird von der
/// <see cref="Data.TelegramSource"/> über einen Callback angefordert, damit
/// die Data-Schicht selbst nichts von Fenstern wissen muss.
/// </summary>
public sealed class CodeInputForm : StyledForm
{
    private readonly TextBox _codeBox;

    /// <summary>Der eingegebene Code, oder null bei Abbruch.</summary>
    public string? Code { get; private set; }

    public CodeInputForm()
        : base(StyledFormOptions.CreateDialog("Telegram-Code"))
    {
        Size = new Size(380, 230);
        StartPosition = FormStartPosition.CenterScreen;

        Label label = UIStyles.Labels.CreateNormal(
            "Telegram hat dir einen Login-Code geschickt\r\n(in der App oder per SMS). Bitte eingeben:");
        label.SetBounds(24, 16, 320, 48);

        _codeBox = UIStyles.TextBoxes.CreateStandard("", "12345");
        _codeBox.SetBounds(24, 74, 320, 30);

        Button okButton = UIStyles.Buttons.CreatePrimary("Bestätigen");
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

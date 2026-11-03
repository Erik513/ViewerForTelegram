using ErikwnkWFUI;
using ErikwnkWFUI.Forms;

namespace ViewerForTelegram.UI;

/// <summary>
/// The one toast of the app: its settings (the accent color) are made once, and
/// every message is shown with them - only the text and the owner window change.
/// </summary>
internal static class AppToast
{
    private static Toaster? _toaster;

    public static void Show(string message, Form owner) =>
        (_toaster ??= UIStyles.Toasts.CreatePrimary()).Show(message, owner);
}

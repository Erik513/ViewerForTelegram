using System.Runtime.InteropServices;

namespace ViewerForTelegram.UI;

/// <summary>
/// Suspends painting of a control tree while <paramref name="action"/> rebuilds
/// a lot of child controls/layout at once - <see cref="Control.SuspendLayout"/>
/// alone only batches layout math, it does not stop already-visible controls
/// from repainting mid-rebuild (visible as a brief jitter, e.g. when the whole
/// UI re-localizes on a language switch).
/// </summary>
public static class RedrawSuspender
{
    private const int WM_SETREDRAW = 0x000B;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public static void WithRedrawSuspended(this Control control, Action action)
    {
        if (!control.IsHandleCreated)
        {
            action();
            return;
        }

        SendMessage(control.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        try
        {
            action();
        }
        finally
        {
            SendMessage(control.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
            control.Invalidate(true);
            control.Refresh();
        }
    }
}

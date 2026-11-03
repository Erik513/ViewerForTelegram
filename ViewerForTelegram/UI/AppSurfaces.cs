using ErikwnkWFUI;

namespace ViewerForTelegram.UI;

/// <summary>
/// The app's own surface colors that must differ per theme. The library colors
/// follow a theme change by themselves, but a color an app copies into a control
/// (<c>BackColor = UIStyles.Colors.X</c>) is a snapshot and stays - and the
/// library has no way to give one control a color for the dark theme and another
/// for the light one. So the app picks per theme here and re-applies on a change.
/// </summary>
internal static class AppSurfaces
{
    /// <summary>Whether the theme that is applied now is a light one.</summary>
    public static bool IsLight => UIStyles.Colors.BackgroundMedium.GetBrightness() > 0.5f;

    /// <summary>
    /// The player panel and the top bar: the library's elevated background, read
    /// again at every theme change (a copied color would stay). One place to give
    /// the light theme a color of its own, should that be wanted.
    /// </summary>
    public static Color Bar => UIStyles.Colors.BackgroundDarkElevated;

    /// <summary>The row of the track in the player: background and text.</summary>
    public static Color PlayingRowBack => IsLight ? Color.FromArgb(196, 220, 245) : Color.FromArgb(26, 52, 78);
    public static Color PlayingRowFore => IsLight ? Color.FromArgb(8, 56, 108) : Color.FromArgb(156, 198, 242);

    /// <summary>
    /// Sets <paramref name="apply"/> now and again whenever the theme changes, for as
    /// long as <paramref name="control"/> lives.
    /// </summary>
    public static void Follow(Control control, Action apply)
    {
        apply();

        void OnChanged(object? s, EventArgs e)
        {
            if (control.IsDisposed)
            {
                return;
            }
            if (control.InvokeRequired)
            {
                control.BeginInvoke(apply);
            }
            else
            {
                apply();
            }
        }

        UIStyles.Colors.ThemeChanged += OnChanged;
        control.Disposed += (_, _) => UIStyles.Colors.ThemeChanged -= OnChanged;
    }
}

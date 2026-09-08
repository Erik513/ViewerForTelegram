using System.Drawing;
using System.Reflection;

namespace ViewerForTelegram.UI;

/// <summary>Bundled app assets loaded once from embedded resources.</summary>
public static class AppAssets
{
    /// <summary>
    /// The multi-resolution window icon (taskbar / Alt-Tab / system menu).
    /// Passed to every <c>StyledFormOptions</c> so windows get the full icon
    /// rather than <c>StyledForm</c>'s 32px <c>ExtractAssociatedIcon</c> fallback.
    /// </summary>
    public static Icon WindowIcon { get; } = LoadIcon("ViewerForTelegram.app.ico");

    /// <summary>The "vT" logo shown at the top-left of every window's title bar.</summary>
    public static Image TitleBarLogo { get; } = LoadImage("ViewerForTelegram.logo.png");

    private static Icon LoadIcon(string resource)
    {
        using Stream? stream = typeof(AppAssets).Assembly.GetManifestResourceStream(resource);
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    private static Image LoadImage(string resource)
    {
        using Stream? stream = typeof(AppAssets).Assembly.GetManifestResourceStream(resource);
        // Copy out into an owned Bitmap: Image.FromStream keeps the stream open
        // for the image's lifetime, and the using closes it here.
        return stream != null ? new Bitmap(Image.FromStream(stream)) : new Bitmap(1, 1);
    }
}

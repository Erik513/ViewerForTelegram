namespace ViewerForTelegram.Data;

/// <summary>
/// Lightweight file logger for troubleshooting. Writes to
/// <c>%AppData%\ViewerForTelegram\app.log</c> only in a Debug build (or when
/// <see cref="Verbose"/> is set at runtime); in Release only the Visual Studio
/// debug output remains.
/// </summary>
public static class AppLog
{
    private static readonly object Lock = new();

    public static bool Verbose { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif

    public static void Line(string category, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{category}] {message}");

        if (!Verbose)
        {
            return;
        }

        lock (Lock)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppPaths.Root, "app.log"),
                    $"{DateTime.Now:HH:mm:ss} [{category}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never disrupt the flow.
            }
        }
    }
}

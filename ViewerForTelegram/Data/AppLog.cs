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

    // Cap each log file; the previous contents roll to "<name>.1", so a
    // long-lived install keeps at most ~2 MB of logs, not an ever-growing file.
    private const long MaxLogBytes = 1024 * 1024;

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

        Append("app.log", category, message);
    }

    /// <summary>
    /// Always written (Debug and Release) to <c>errors.log</c> - for failures the
    /// user should be able to send in without turning on verbose logging.
    /// </summary>
    public static void Error(string category, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[{category}] ERROR {message}");
        Append("errors.log", category, message);
    }

    private static void Append(string file, string category, string message)
    {
        lock (Lock)
        {
            try
            {
                string path = Path.Combine(AppPaths.Root, file);
                IoUtil.RollIfTooLarge(path, MaxLogBytes);
                File.AppendAllText(
                    path,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{category}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never disrupt the flow.
            }
        }
    }
}

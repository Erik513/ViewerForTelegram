namespace ViewerForTelegram.Data;

/// <summary>
/// Schlanker Datei-Logger für die Fehlersuche. Schreibt nur im Debug-Build
/// (oder wenn <see cref="Verbose"/> zur Laufzeit gesetzt wird) nach
/// <c>%AppData%\ViewerForTelegram\app.log</c> – im Release bleibt nur die
/// Ausgabe im Visual-Studio-Fenster.
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
                // Logging darf nie den Ablauf stören.
            }
        }
    }
}

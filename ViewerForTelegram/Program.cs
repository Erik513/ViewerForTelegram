using ErikwnkWFUI;
using ErikwnkWFUI.Styles;
using ViewerForTelegram.Data;
using ViewerForTelegram.Data.Interfaces;
using ViewerForTelegram.Logic;
using ViewerForTelegram.Logic.Services;
using ViewerForTelegram.UI.Forms;

namespace ViewerForTelegram;

static class Program
{
    /// <summary>
    ///  Composition Root: erzeugt die konkreten Data-Klassen und reicht sie
    ///  als Interfaces in die UI.
    /// </summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        UIStyles.Language = UILanguage.German;
        HookCrashLogging();

        IConfigStore configStore = new JsonConfigStore();
        IMediaCache cache = new FileMediaCache(AppPaths.CacheDir);
        ITelegramSource telegram = new TelegramSource(configStore, AppPaths.SessionFile);

        var feed = new AudioFeedService(telegram, cache);
        var downloader = new MediaDownloader(telegram, cache, CachePolicy.LimitBytes);

        // Cache-Aufräumen beim Start: entweder ganz leeren oder auf die Grenze stutzen.
        if (configStore.Load().ClearCacheOnStart)
        {
            cache.Clear();
        }
        else
        {
            cache.PruneToLimit(CachePolicy.LimitBytes);
        }

        try
        {
            Application.Run(new MainForm(telegram, configStore, cache, feed, downloader));
        }
        finally
        {
            telegram.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    ///  Unerwartete Fehler landen in %AppData%\ViewerForTelegram\crash.log statt
    ///  die App wortlos zu beenden. Debug-Hilfe, später entfernbar.
    /// </summary>
    private static void HookCrashLogging()
    {
        string logPath = Path.Combine(AppPaths.Root, "crash.log");

        void Write(string source, object? error)
        {
            try
            {
                File.AppendAllText(
                    logPath, $"--- {DateTime.Now:u} [{source}] ---{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // egal
            }
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Write("UI-Thread", e.Exception);
            try
            {
                ErikwnkWFUI.Forms.MessageBox.Show(
                    e.Exception.Message + "\r\n\r\n(Details in crash.log)",
                    "Unerwarteter Fehler",
                    ErikwnkWFUI.Forms.MessageBoxButtons.OK,
                    ErikwnkWFUI.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                // der Fehlerdialog selbst darf nicht die App killen
            }
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain", e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("Task", e.Exception);
            e.SetObserved();
        };
    }
}

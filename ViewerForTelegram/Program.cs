using System.Globalization;
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
    ///  Composition root: creates the concrete Data classes and passes them
    ///  into the UI as interfaces.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // The app is English-only - run everything in en-US so number/date
        // formatting is consistent regardless of the user's Windows locale.
        var culture = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        ApplicationConfiguration.Initialize();
        UIStyles.Language = UILanguage.English;
        HookCrashLogging();

        IConfigStore configStore = new JsonConfigStore();
        IMediaCache cache = new FileMediaCache(AppPaths.CacheDir);
        ITelegramSource telegram = new TelegramSource(configStore, AppPaths.SessionFile);
        IAudioPlayer audio = new AudioPlayer();
        var uiState = new JsonUiStateStore();

        var feed = new AudioFeedService(telegram, cache);
        var downloader = new MediaDownloader(telegram, cache, CachePolicy.LimitBytes);

        // Cache housekeeping on start: either wipe it or trim it to the limit.
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
            Application.Run(new MainForm(
                telegram, configStore, cache, feed, downloader, audio, uiState));
        }
        finally
        {
            audio.Dispose();
            telegram.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    ///  Unexpected errors go to %AppData%\ViewerForTelegram\crash.log instead of
    ///  the app quitting silently. A debugging aid, removable later.
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
                // never mind
            }
        }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Write("UI thread", e.Exception);
            try
            {
                ErikwnkWFUI.Forms.MessageBox.Show(
                    e.Exception.Message + "\r\n\r\n(Details in crash.log)",
                    "Unexpected error",
                    ErikwnkWFUI.Forms.MessageBoxButtons.OK,
                    ErikwnkWFUI.Forms.MessageBoxIcon.Error);
            }
            catch
            {
                // the error dialog itself must not kill the app
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

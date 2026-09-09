using ErikwnkCore;
using ErikwnkWFUI;

namespace ViewerForTelegram.UI.Localization;

/// <summary>
/// App-wide UI text. Wraps ErikwnkCore's <see cref="AppLocalization"/> (our own
/// strings; moved there from ErikwnkWFUI since it has no WinForms dependency).
///
/// Call <see cref="Register"/> once at startup. Every form re-applies its texts
/// from a handler on <see cref="Changed"/> - the event only signals; it does not
/// re-translate anything by itself.
/// </summary>
public static class Loc
{
    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    public static event EventHandler Changed
    {
        add => AppLocalization.LanguageChanged += value;
        remove => AppLocalization.LanguageChanged -= value;
    }

    /// <summary>The string for <paramref name="key"/> in the current language.</summary>
    public static string S(string key) => AppLocalization.Get(key);

    /// <summary><see cref="S"/> run through <see cref="string.Format(string,object[])"/>.</summary>
    public static string T(string key, params object[] args) => AppLocalization.Get(key, args);

    public static AppLanguage Current
    {
        get => AppLocalization.Language;
        // UIStyles.SetLanguage keeps this app's own strings AND ErikwnkWFUI's
        // built-in dialog text (UIStyles.Language) in lockstep from one value.
        set => UIStyles.SetLanguage(value);
    }

    public static void Register()
    {
        AppLocalization.Register(AppLanguage.English, English);
        AppLocalization.Register(AppLanguage.German, German);
    }

    private static readonly Dictionary<string, string> English = new()
    {
        ["app.title"] = "Viewer for Telegram",
        ["app.alreadyRunning"] = "Viewer for Telegram is already running.",

        // --- top bar ---
        ["top.settings.tip"] = "Settings",
        ["top.refresh.tip"] = "Reload chats and list",
        ["top.filter.placeholder"] = "Filter …",
        ["top.format.all"] = "All audio files",
        ["range.days"] = "Last {0} days",
        ["range.newest"] = "Newest {0} files",
        ["chat.group"] = "Group",
        ["chat.channel"] = "Channel",
        ["chat.bot"] = "Bot",
        ["chat.saved"] = "Saved Messages",

        // --- list columns ---
        ["col.date"] = "Date",
        ["col.title"] = "Title",
        ["col.artist"] = "Artist",
        ["col.length"] = "Length",
        ["col.size"] = "Size",

        // --- cache label ---
        ["cache.initial"] = "Cache: –",
        ["cache.label"] = "Cache {0} · {1}",
        ["cache.tip"] = "Downloaded songs kept locally: {0} / {1} MB ({2} files).\r\n"
                        + "The oldest are removed once the limit is reached.",

        // --- operation names (retry / failure text) ---
        ["op.connecting"] = "Connecting",
        ["op.signin"] = "Sign-in",
        ["op.loading"] = "Loading",
        ["op.refresh"] = "Refresh",

        // --- status line ---
        ["status.connecting"] = "Connecting …",
        ["status.signedIn"] = "Signed in – {0} groups/channels.",
        ["status.refreshing"] = "Refreshing …",
        ["status.loading"] = "Loading …",
        ["status.checkingNew"] = "Checking for new files …",
        ["status.loadingProgress"] = "Loading … {0} / {1} files",
        ["status.loadingCount"] = "Loading … {0} files",
        ["status.loadingFinished"] = "Loading … finished, {0} files",
        ["status.noAudioRange"] = "No files in this time range.",
        ["status.notSignedIn"] = "Not signed in – open Settings.",
        ["status.count"] = "{0} files",
        ["status.filtered"] = "{0} of {1} files",
        ["status.retry"] = "{0} failed, retrying ({1}/{2}) …",
        ["status.rateLimit"] = "Telegram rate limit – wait {0}s, then Settings › Sign in.",
        ["status.opFailed"] = "{0} failed ({1}). Settings › Sign in to retry.",
        ["status.playbackFailed"] = "Playback failed: {0}",
        ["status.playing"] = "Playing: {0}",
        ["status.loadingFile"] = "Loading: {0}",
        ["status.loadingFileProgress"] = "Loading: {0} … {1}%",
        ["status.cancelled"] = "Cancelled.",
        ["status.downloadFailed"] = "Download failed: {0}",
        ["status.cannotPlay"] = "Cannot play {0}: {1}",
        ["status.cannotPlayElsewhere"] = "Cannot play {0} – open it elsewhere via \"Save a copy\". ({1})",
        ["status.saveOnly"] = "{0} can't be played in-app – use the save button to download it.",
        ["status.cannotOpenFolder"] = "Cannot open folder: {0}",
        ["status.credentialsDeleted"] = "All data deleted.",
        ["status.signedOut"] = "Signed out.",

        // --- toasts / save ---
        ["toast.playFirst"] = "Download failed – play the track first so it is cached.",
        ["toast.downloaded"] = "Downloaded: {0}",
        ["toast.listUpdated"] = "List updated: {0} files",
        ["toast.cacheCleared"] = "Cache cleared: {0} files ({1})",
        ["save.filter"] = "Audio file|*{0}|All files|*.*",

        // --- credentials-incomplete prompt ---
        ["msg.credsIncomplete.body"] = "api_id, api_hash and phone number must all be filled in.\r\n"
                                       + "Enter them again?",
        ["msg.credsIncomplete.title"] = "Credentials incomplete",

        // --- two-factor (cloud password) not supported ---
        ["msg.twoFactor.title"] = "Two-factor authentication",
        // The message box does not word-wrap - the lines are pre-broken and
        // it's shown at the Large size preset (see MainForm.ConnectAsync).
        ["msg.twoFactor.body"] = "This account is protected by a cloud password\r\n"
                                 + "(two-factor authentication), which this app\r\n"
                                 + "can't sign in with yet.\r\n\r\n"
                                 + "You can sign in with an account that has no\r\n"
                                 + "cloud password, or turn the cloud password off in\r\n"
                                 + "Telegram under Settings › Privacy and Security.",

        // --- exceptions surfaced to the user ---
        ["err.credentialsMissing"] = "Credentials are missing.",
        ["err.noCode"] = "No login code entered.",
        ["err.signinCancelled"] = "Sign-in cancelled.",
        ["err.twoFactorUnsupported"] = "Sign-in failed: this account uses a cloud password (2FA), which isn't supported yet.",

        // --- player ---
        ["player.nothingSelected"] = "Nothing selected",
        ["player.vol"] = "Vol",
        ["player.tip.main"] = "Play / pause",
        ["player.tip.cancelDownload"] = "Cancel download",
        ["player.tip.pause"] = "Pause",
        ["player.tip.play"] = "Play",
        ["player.tip.download"] = "Download",
        ["player.tip.downloadFile"] = "Download: {0}",
        ["player.tip.browse"] = "Open the download folder",

        // --- settings ---
        ["settings.title"] = "Settings",
        ["settings.guide"] = "?  Guide",
        ["settings.guide.tip"] = "How do I get api_id / api_hash?",
        ["settings.sec.general"] = "General",
        ["settings.sec.api"] = "Telegram API",
        ["settings.sec.signin"] = "Sign-in",
        ["settings.sec.downloads"] = "Downloads",
        ["settings.sec.cache"] = "Cache",
        ["settings.sec.credentials"] = "Data",
        ["settings.row.language"] = "Language",
        ["settings.row.phone"] = "Phone",
        ["settings.row.status"] = "Status",
        ["settings.row.folder"] = "Folder",
        ["settings.row.used"] = "Used",
        ["settings.row.reset"] = "Delete",
        ["settings.status.connected"] = "signed in",
        ["settings.status.disconnected"] = "not signed in",
        ["settings.btn.signin"] = "Sign in",
        ["settings.btn.signout"] = "Sign out",
        ["settings.btn.chooseFolder"] = "Choose folder",
        ["settings.btn.clearCache"] = "Clear cache",
        ["settings.btn.openCache"] = "Open the cache folder",
        ["settings.btn.delete"] = "Delete",
        ["settings.reset.desc"] = "Delete data and sign out",
        ["settings.lock.tip"] = "Edit",
        ["settings.lock.tip.locked"] = "Sign out first to change this",
        ["settings.toggle.folder.on"] = "Downloads go to this folder without asking",
        ["settings.toggle.folder.off"] = "Pick the folder on every download",
        ["settings.toggle.clearCache.on"] = "Cache is wiped on every startup",
        ["settings.toggle.clearCache.off"] = "Cache is kept between sessions (only the size limit applies)",
        ["settings.cache.used"] = "{0} / {1} ({2} files)",
        ["settings.msg.signout.body"] = "Sign out? The stored login is deleted.\r\nThe credentials are kept.",
        ["settings.msg.signout.title"] = "Sign out",
        ["settings.msg.clearCache.body"] = "Delete {0} files ({1}) from the cache?",
        ["settings.msg.clearCache.title"] = "Clear cache",
        ["settings.msg.wipe.body"] = "Really delete api_id, api_hash, phone number, cached files and saved lists, and sign out?",
        ["settings.msg.wipe.title"] = "Delete data",

        // --- login-code dialog ---
        ["code.title"] = "Telegram code",
        ["code.prompt"] = "Telegram sent you a login code\r\n(in the app or by SMS). Please enter it:",
        ["code.confirm"] = "Confirm",

        // --- api-help dialog ---
        ["help.title"] = "Create credentials",
        ["help.close"] = "Close",
        ["help.open"] = "Open my.telegram.org",
        ["help.body"] =
            "Viewer for Telegram ships no shared API credentials - every user creates\r\n" +
            "their own. It is free and takes about 2 minutes.\r\n\r\n" +
            "How to get api_id and api_hash:\r\n\r\n" +
            "1. Click \"Open my.telegram.org\" below.\r\n" +
            "2. Sign in with your Telegram phone number - the confirmation code\r\n" +
            "   arrives in your Telegram app.\r\n" +
            "3. Click \"API development tools\".\r\n" +
            "4. Fill in the form:\r\n" +
            "     App title:   e.g. ViewerForTelegram\r\n" +
            "     Short name:  e.g. tgviewer\r\n" +
            "     Platform:    Desktop\r\n" +
            "     (URL and description can stay empty)\r\n" +
            "5. Click \"Create application\".\r\n" +
            "6. The next page shows \"App api_id\" (a number) and\r\n" +
            "   \"App api_hash\" (a long hex string).\r\n" +
            "7. Enter both here in the settings - the pencil icon unlocks the\r\n" +
            "   respective field.\r\n\r\n" +
            "Important: the api_hash is like a password - do not share it.",
    };

    private static readonly Dictionary<string, string> German = new()
    {
        ["app.title"] = "Viewer for Telegram",
        ["app.alreadyRunning"] = "Viewer for Telegram läuft bereits.",

        ["top.settings.tip"] = "Einstellungen",
        ["top.refresh.tip"] = "Chats und Liste neu laden",
        ["top.filter.placeholder"] = "Filtern …",
        ["top.format.all"] = "Alle Audiodateien",
        ["range.days"] = "Letzte {0} Tage",
        ["range.newest"] = "Neueste {0} Dateien",
        ["chat.group"] = "Gruppe",
        ["chat.channel"] = "Kanal",
        ["chat.bot"] = "Bot",
        ["chat.saved"] = "Gespeicherte Nachrichten",

        ["col.date"] = "Datum",
        ["col.title"] = "Titel",
        ["col.artist"] = "Interpret",
        ["col.length"] = "Länge",
        ["col.size"] = "Größe",

        ["cache.initial"] = "Cache: –",
        ["cache.label"] = "Cache {0} · {1}",
        ["cache.tip"] = "Lokal gehaltene heruntergeladene Songs: {0} / {1} MB ({2} Dateien).\r\n"
                        + "Die ältesten werden entfernt, sobald das Limit erreicht ist.",

        ["op.connecting"] = "Verbindung",
        ["op.signin"] = "Anmeldung",
        ["op.loading"] = "Laden",
        ["op.refresh"] = "Aktualisierung",

        ["status.connecting"] = "Verbinde …",
        ["status.signedIn"] = "Angemeldet – {0} Gruppen/Kanäle.",
        ["status.refreshing"] = "Aktualisiere …",
        ["status.loading"] = "Lade …",
        ["status.checkingNew"] = "Suche nach neuen Dateien …",
        ["status.loadingProgress"] = "Lade … {0} / {1} Dateien",
        ["status.loadingCount"] = "Lade … {0} Dateien",
        ["status.loadingFinished"] = "Laden … fertig, {0} Dateien",
        ["status.noAudioRange"] = "Keine Dateien in diesem Zeitraum.",
        ["status.notSignedIn"] = "Nicht angemeldet – Einstellungen öffnen.",
        ["status.count"] = "{0} Dateien",
        ["status.filtered"] = "{0} von {1} Dateien",
        ["status.retry"] = "{0} fehlgeschlagen, neuer Versuch ({1}/{2}) …",
        ["status.rateLimit"] = "Telegram-Ratenlimit – {0}s warten, dann Einstellungen › Anmelden.",
        ["status.opFailed"] = "{0} fehlgeschlagen ({1}). Einstellungen › Anmelden zum erneuten Versuch.",
        ["status.playbackFailed"] = "Wiedergabe fehlgeschlagen: {0}",
        ["status.playing"] = "Wiedergabe: {0}",
        ["status.loadingFile"] = "Lade: {0}",
        ["status.loadingFileProgress"] = "Lade: {0} … {1} %",
        ["status.cancelled"] = "Abgebrochen.",
        ["status.downloadFailed"] = "Download fehlgeschlagen: {0}",
        ["status.cannotPlay"] = "{0} kann nicht abgespielt werden: {1}",
        ["status.cannotPlayElsewhere"] = "{0} kann nicht abgespielt werden – über \"Kopie speichern\" woanders öffnen. ({1})",
        ["status.saveOnly"] = "{0} kann in der App nicht abgespielt werden – mit dem Speichern-Button herunterladen.",
        ["status.cannotOpenFolder"] = "Ordner kann nicht geöffnet werden: {0}",
        ["status.credentialsDeleted"] = "Alle Daten gelöscht.",
        ["status.signedOut"] = "Abgemeldet.",

        ["toast.playFirst"] = "Download fehlgeschlagen – zuerst den Titel abspielen, damit er im Cache liegt.",
        ["toast.downloaded"] = "Heruntergeladen: {0}",
        ["toast.listUpdated"] = "Liste aktualisiert: {0} Dateien",
        ["toast.cacheCleared"] = "Cache geleert: {0} Dateien ({1})",
        ["save.filter"] = "Audiodatei|*{0}|Alle Dateien|*.*",

        ["msg.credsIncomplete.body"] = "api_id, api_hash und Telefonnummer müssen alle ausgefüllt sein.\r\n"
                                       + "Erneut eingeben?",
        ["msg.credsIncomplete.title"] = "Zugangsdaten unvollständig",

        ["msg.twoFactor.title"] = "Zwei-Faktor-Authentifizierung",
        // Die MessageBox bricht Text nicht um - die Zeilen sind vorgebrochen,
        // Anzeige in der großen Größe (siehe MainForm.ConnectAsync).
        ["msg.twoFactor.body"] = "Dieses Konto ist mit einem Cloud-Passwort\r\n"
                                 + "(Zwei-Faktor-Authentifizierung) geschützt, mit\r\n"
                                 + "dem sich diese App noch nicht anmelden kann.\r\n\r\n"
                                 + "Melde dich mit einem Konto ohne Cloud-Passwort an,\r\n"
                                 + "oder deaktiviere das Cloud-Passwort in Telegram\r\n"
                                 + "unter Einstellungen › Datenschutz und Sicherheit.",

        ["err.credentialsMissing"] = "Zugangsdaten fehlen.",
        ["err.noCode"] = "Kein Anmeldecode eingegeben.",
        ["err.signinCancelled"] = "Anmeldung abgebrochen.",
        ["err.twoFactorUnsupported"] = "Anmeldung fehlgeschlagen: Dieses Konto nutzt ein Cloud-Passwort (2FA), das noch nicht unterstützt wird.",

        ["player.nothingSelected"] = "Nichts ausgewählt",
        ["player.vol"] = "Vol",
        ["player.tip.main"] = "Wiedergabe / Pause",
        ["player.tip.cancelDownload"] = "Download abbrechen",
        ["player.tip.pause"] = "Pause",
        ["player.tip.play"] = "Wiedergabe",
        ["player.tip.download"] = "Herunterladen",
        ["player.tip.downloadFile"] = "Herunterladen: {0}",
        ["player.tip.browse"] = "Download-Ordner öffnen",

        ["settings.title"] = "Einstellungen",
        ["settings.guide"] = "?  Anleitung",
        ["settings.guide.tip"] = "Wie bekomme ich api_id / api_hash?",
        ["settings.sec.general"] = "Allgemein",
        ["settings.sec.api"] = "Telegram-API",
        ["settings.sec.signin"] = "Anmeldung",
        ["settings.sec.downloads"] = "Downloads",
        ["settings.sec.cache"] = "Cache",
        ["settings.sec.credentials"] = "Daten",
        ["settings.row.language"] = "Sprache",
        ["settings.row.phone"] = "Telefon",
        ["settings.row.status"] = "Status",
        ["settings.row.folder"] = "Ordner",
        ["settings.row.used"] = "Belegt",
        ["settings.row.reset"] = "Löschen",
        ["settings.status.connected"] = "angemeldet",
        ["settings.status.disconnected"] = "nicht angemeldet",
        ["settings.btn.signin"] = "Anmelden",
        ["settings.btn.signout"] = "Abmelden",
        ["settings.btn.chooseFolder"] = "Ordner wählen",
        ["settings.btn.clearCache"] = "Cache leeren",
        ["settings.btn.openCache"] = "Cache-Ordner öffnen",
        ["settings.btn.delete"] = "Löschen",
        ["settings.reset.desc"] = "Daten löschen und abmelden",
        ["settings.lock.tip"] = "Bearbeiten",
        ["settings.lock.tip.locked"] = "Zum Ändern zuerst abmelden",
        ["settings.toggle.folder.on"] = "Downloads landen ohne Nachfrage in diesem Ordner",
        ["settings.toggle.folder.off"] = "Bei jedem Download den Ordner wählen",
        ["settings.toggle.clearCache.on"] = "Cache wird bei jedem Start geleert",
        ["settings.toggle.clearCache.off"] = "Cache bleibt zwischen Sitzungen erhalten (nur das Größenlimit greift)",
        ["settings.cache.used"] = "{0} / {1} ({2} Dateien)",
        ["settings.msg.signout.body"] = "Abmelden? Die gespeicherte Anmeldung wird gelöscht.\r\nDie Zugangsdaten bleiben erhalten.",
        ["settings.msg.signout.title"] = "Abmelden",
        ["settings.msg.clearCache.body"] = "{0} Dateien ({1}) aus dem Cache löschen?",
        ["settings.msg.clearCache.title"] = "Cache leeren",
        ["settings.msg.wipe.body"] = "Wirklich api_id, api_hash, Telefonnummer, heruntergeladene Dateien und gespeicherte Listen löschen und abmelden?",
        ["settings.msg.wipe.title"] = "Daten löschen",

        ["code.title"] = "Telegram-Code",
        ["code.prompt"] = "Telegram hat dir einen Anmeldecode geschickt\r\n(in der App oder per SMS). Bitte eingeben:",
        ["code.confirm"] = "Bestätigen",

        ["help.title"] = "Zugangsdaten erstellen",
        ["help.close"] = "Schließen",
        ["help.open"] = "my.telegram.org öffnen",
        ["help.body"] =
            "Viewer for Telegram bringt keine gemeinsamen API-Zugangsdaten mit - jeder\r\n" +
            "Nutzer erstellt seine eigenen. Das ist kostenlos und dauert etwa 2 Minuten.\r\n\r\n" +
            "So bekommst du api_id und api_hash:\r\n\r\n" +
            "1. Unten auf \"my.telegram.org öffnen\" klicken.\r\n" +
            "2. Mit deiner Telegram-Telefonnummer anmelden - der Bestätigungscode\r\n" +
            "   kommt in deiner Telegram-App an.\r\n" +
            "3. Auf \"API development tools\" klicken.\r\n" +
            "4. Das Formular ausfüllen:\r\n" +
            "     App title:   z. B. ViewerForTelegram\r\n" +
            "     Short name:  z. B. tgviewer\r\n" +
            "     Platform:    Desktop\r\n" +
            "     (URL und Beschreibung können leer bleiben)\r\n" +
            "5. Auf \"Create application\" klicken.\r\n" +
            "6. Die nächste Seite zeigt \"App api_id\" (eine Zahl) und\r\n" +
            "   \"App api_hash\" (eine lange Hex-Zeichenfolge).\r\n" +
            "7. Beide hier in den Einstellungen eintragen - das Stift-Symbol gibt\r\n" +
            "   das jeweilige Feld frei.\r\n\r\n" +
            "Wichtig: der api_hash ist wie ein Passwort - nicht weitergeben.",
    };
}

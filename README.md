# Viewer for Telegram

A small Windows desktop app that lists the **audio messages** shared in a
Telegram group or channel you're in – or in your own **Saved Messages** or a
**bot chat**. Pick a chat, choose either a rolling time window
(last 3 / 7 / 14 / 30 / 60 days) or a fixed count ("Newest 50" up to
"Newest 5000"), double-click a track to play it in the player at the bottom,
and save the ones you want to keep.

It signs in as **you** via Telegram's MTProto API (like the official clients),
so it can see full history with no bot restrictions and no file-size limit.

> Built for the "let me hear what's new" case, not as a re-listen library. The
> local cache is just a size-capped playback buffer – clear it any time, or
> switch on "wipe on every start" in Settings.

## Download

Grab the latest `ViewerForTelegram.exe` from the
[Releases](https://github.com/Erik513/ViewerForTelegram/releases) page and run
it – no installer, nothing to set up.

The exe is not code-signed, so on first launch Windows SmartScreen may say
"Windows protected your PC". Click **More info → Run anyway**. After that the
app checks for newer releases on startup and can update itself.

## Features

- Reads from any group or channel you're in, plus your **Saved Messages** and
  your **bot chats** (person-to-person DMs are left out on purpose)
- One player docked at the bottom: play/pause, seek, volume, "Save a copy"
- Plays mp3, m4a/aac, wav, wma, aiff/aif and (on Windows 10+) flac; ogg/opus
  can be saved but not played in-app
- Rolling time window (precise to the hour) or a fixed "Newest N" count, up
  to 5000 files
- The loaded list is cached per chat on disk, so switching back to a chat or
  restarting the app doesn't re-download everything from scratch; on refresh
  the app also detects tracks that were deleted on Telegram and fills the gap
  back up from older history
- A ✓ column marks the tracks already in the local cache
- Drag a downloaded track straight out of the list into Explorer, a music
  app or a chat window
- Click a column header to sort by date, title, artist, length or size
  (click again to reverse, once more for the normal newest-first order)
- After a load or refresh, the refresh button badges how many tracks came
  in that you hadn't seen yet
- Filter the list by file format, plus a live text filter over performer / title / file name
  (the text filter clears when you switch chats)
- Volume, last chat, time range and format filter remembered between sessions
- UI language: English or German, switches instantly, no restart needed
- Save to a fixed folder or via a save dialog, original file name preserved
- Size-capped local cache (default 3000 MB) with a "clear now" button
- Checks GitHub releases for updates at startup and offers to install them
- Only one instance runs at a time

## Requirements

- Windows 10 or 11 (x64)
- Your own Telegram **api_id** and **api_hash** (see below)

That's it – the release is a single self-contained `ViewerForTelegram.exe`
with the .NET runtime bundled in, so there is nothing to install. No browser
runtime either: the UI is plain WinForms and playback uses the Windows audio
stack via [NAudio](https://github.com/naudio/NAudio).

## Getting your api_id / api_hash

This app ships **no shared API credentials** – every user creates their own.
It is free and takes about two minutes.

1. Open <https://my.telegram.org> and sign in with your Telegram phone number
   (the confirmation code arrives in your Telegram app).
2. Open **API development tools**.
3. Fill in the form – *App title* e.g. `ViewerForTelegram`, *Short name* e.g.
   `tgviewer`, *Platform* `Desktop`. URL and description can stay empty.
4. Click **Create application**.
5. The next page shows **App api_id** (a number) and **App api_hash** (a long
   hex string).

Enter both in the app under **Settings** (the ✎ icon unlocks each field).

The **api_hash is like a password** – do not share it, and never commit it.

## First run

1. Start the app. With no credentials stored it opens **Settings** directly.
2. Enter api_id, api_hash and your phone number (international format, e.g.
   `+491701234567`), then click **Sign in**.
3. Enter the login code Telegram sends you. If your account has a **cloud
   password** (two-factor authentication), you'll be asked for that too.
4. Pick a chat and a time range – the list loads automatically.
5. Double-click a row to download (if needed) and play it.

After that the app signs in silently from the stored session; you only need the
code (and password) again if you sign out.

If you've *forgotten* your cloud password, recover it in the official Telegram
app first – this app doesn't do the e-mail recovery flow.

## Where your data lives

Everything is under `%AppData%\ViewerForTelegram\`:

| File / folder            | Contents                                          |
|--------------------------|---------------------------------------------------|
| `appsettings.local.json` | api_id, api_hash, phone number, options           |
| `telegram.session`       | the completed login – treat like a password       |
| `ui-state.json`          | remembered chat / time range / volume / format filter |
| `feed-cache.json`        | the loaded audio list per chat (up to 5000 each)  |
| `durations.json`         | track lengths learned by playing "as a file" audio |
| `cache\`                 | downloaded audio files                            |
| `crash.log` / `errors.log` | diagnostics for bug reports (safe to delete)    |

Nothing leaves your machine except the traffic to Telegram itself. None of the
above is part of this repository (see `.gitignore`).

## Building from source

```
dotnet build ViewerForTelegram.sln
dotnet test  ViewerForTelegram.sln
```

The shared UI library (`ErikwnkWFUI.dll` + `ErikwnkCore.dll`) is checked in
under `lib/ErikwnkWFUI/`, so the solution builds without any extra setup.
`lib/update-erikwnk-libs.ps1` re-copies those DLLs from the sibling
`ErikwnkWFUI` repo if you are developing that library too.

The release build is one self-contained exe:

```
dotnet publish ViewerForTelegram/ViewerForTelegram.csproj -p:PublishProfile=win-x64
```

→ `ViewerForTelegram/bin/publish/win-x64/ViewerForTelegram.exe`, which is
the file attached to each GitHub release (the in-app updater swaps exactly
that file).

## Project layout

One project, three folders, dependency direction `UI → Logic → Data`:

| Folder   | Responsibility                                                       |
|----------|---------------------------------------------------------------------|
| `Data/`  | Telegram access (WTelegramClient), the file cache, the NAudio player, JSON config, models |
| `Logic/` | `AudioFeedService` (time window + cache check), `MediaDownloader`   |
| `UI/`    | `MainForm` (top bar + list + player), `SettingsForm`, and `UI/Controls/` (`PlayerPanel`, `GlyphIcons`) |

## License

MIT – see [LICENSE](LICENSE).

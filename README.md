# Viewer for Telegram

A small Windows desktop app that lists the **audio messages** posted in a
Telegram group or channel you are a member of. Pick a chat, choose a rolling
time window (last 3 / 7 / 14 / 30 days), and every track shows up as an inline
player. Download the ones you want to keep straight from the player menu.

It signs in as **you** via Telegram's MTProto API (like the official clients),
so it can see full history with no bot restrictions and no file-size limit.

> Built for the "let me hear what's new" case, not as a re-listen library. The
> local cache is a throwaway playback buffer and is wiped on every start by
> default.

## Features

- Inline HTML5 audio player per message (any format Chromium plays: mp3, m4a,
  ogg/opus, flac, wav, …)
- Rolling time window, precise to the hour
- Sortable list, live text filter
- Global volume, remembered between sessions
- Download to a fixed folder or via a save dialog, original file name preserved
- Size-capped local cache (default 3000 MB) with a "clear now" button

## Requirements

- Windows 10 or 11 (x64)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) –
  preinstalled on current Windows 11; the app offers a download link if it is
  missing
- Your own Telegram **api_id** and **api_hash** (see below)

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
3. Enter the login code Telegram sends you.
4. Pick a group/channel and a time range – the list loads automatically.

After that the app signs in silently from the stored session; you only need the
code again if you sign out.

**Two-factor authentication (cloud password) is not supported yet** – accounts
with 2FA enabled cannot currently sign in.

## Where your data lives

Everything is under `%AppData%\ViewerForTelegram\`:

| File / folder            | Contents                                          |
|--------------------------|---------------------------------------------------|
| `appsettings.local.json` | api_id, api_hash, phone number, options           |
| `telegram.session`       | the completed login – treat like a password       |
| `cache\`                 | downloaded audio files                            |
| `WebView2\`              | the embedded browser's profile                    |

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

## Project layout

One project, three folders, dependency direction `UI → Logic → Data`:

| Folder   | Responsibility                                                       |
|----------|---------------------------------------------------------------------|
| `Data/`  | Telegram access (WTelegramClient), the file cache, JSON config, models |
| `Logic/` | `AudioFeedService` (time window + cache check), `MediaDownloader`   |
| `UI/`    | `MainForm` = the embedded WebView2; the interface lives in `UI/Web/` (`index.html` / `app.js` / `app.css`). `SettingsForm` handles sign-in and options. |

## License

MIT – see [LICENSE](LICENSE).

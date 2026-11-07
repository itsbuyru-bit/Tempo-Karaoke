English | [Русский](PRIVACY.ru.md)

# Tempo Karaoke Privacy Policy

Effective date: November 5, 2026.
Developer: Cappuccino Dev (the "Developer").

## Summary

Tempo Karaoke **does not collect, transmit, or sell personal data**. Everything the
app stores stays on your computer. The app has no accounts, no advertising, no
analytics, and no telemetry.

## Data the app processes

The app works only with data that you have provided yourself:

- paths to video folders and a metadata cache (`media.db`), so that your media
  library is not rescanned on every launch;
- settings (`settings.json`): theme, language, media library paths, and remote
  control options;
- the queue (`queue.json`), playlists (`playlists.json`), history, and favorites;
- the names of devices that have connected to the web remote
  (`remote-clients.json`).

All of this data is stored locally: in `%LocalAppData%\TempoKaraoke` for the
portable version, or in the MSIX package's `LocalState` folder. The Developer has
no access to it.

The app reads video files only from the folders you have added in settings. It does
not copy or modify your files.

## Network connections

- **Web remote.** The built-in HTTP server runs on your local network or over
  Wi-Fi Direct, and starts only when you ask it to. The remote page and queue data
  are transmitted only between your computer and your devices on that network; they
  never travel over the internet.
- **Update check.** If "Check for updates at startup" is enabled in settings, the
  app sends a request to GitHub (`api.github.com`) for information about the latest
  release. The request contains no identifiers, files, history, or media library
  content — only a standard HTTP request with a User-Agent containing the app name
  and version. GitHub processes such requests in accordance with its own privacy
  policy. The check can be disabled in settings.
- The app makes no other network connections.

## Disclosure to third parties

The Developer does not disclose data to third parties, because no such data exists
on the Developer's side. The only external request is the update check through
GitHub described above.

## Retention and deletion

Data is stored on your device until you delete it:

- uninstalling the app (through the installer or Settings → Apps) removes the
  program's files;
- user data is deleted together with the `%LocalAppData%\TempoKaraoke` folder
  (portable version), or by resetting the app's data in Windows settings (MSIX
  version).

No backups exist on the Developer's side.

## Children

The app does not collect user data, including data from children, and contains no
advertising or purchases.

## Changes to this policy

The current version is always available in this file in the repository
<https://github.com/itsbuyru-bit/Tempo-Karaoke>. The effective date is updated when
material changes are made.

## Contact

Questions about data processing: GitHub Issues —
<https://github.com/itsbuyru-bit/Tempo-Karaoke/issues>.

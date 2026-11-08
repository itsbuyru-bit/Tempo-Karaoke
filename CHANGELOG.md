English | [Русский](CHANGELOG.ru.md)

# Changelog

All notable changes to this project. The format is based on
[Keep a Changelog](https://keepachangelog.com/ru/1.1.0/), and versions use four
components (`major.minor.patch.revision`).

Detailed user-facing release notes for each release live in
`docs/releases/<tag>.md` and are published together with the GitHub Release.

## [1.10.3.6] — 2026-11-09

Full details: [`docs/releases/v1.10.3.6.md`](docs/releases/v1.10.3.6.md).

### Changed

- One PIN field per role for host and administrator, without duplicate
  confirmation fields.
- Plain minutes text input for remote auto-stop, normalised to 1–120 minutes;
  no spinner arrows and no focus jump while editing a PIN.

## [1.10.3.5] — 2026-11-08

Full details: [`docs/releases/v1.10.3.5.md`](docs/releases/v1.10.3.5.md).

### Changed

- The detached player controls are a real overlay above the video, so showing
  or hiding them never changes the video size.
- The overlay appears only in the bottom pointer strip and hides immediately
  when the pointer leaves it. Phone-remote commands no longer reveal it.

## [1.10.3.4] — 2026-11-08

Full details: [`docs/releases/v1.10.3.4.md`](docs/releases/v1.10.3.4.md).

### Fixed

- Update badges compare versions semantically, so a previously stored
  three-component pending version (`1.10.3`) no longer marks an installed
  four-component release (`1.10.3.3`) as new.
- New update metadata keeps four-component versions.
- An explicit language choice is preserved when a translation catalog is
  temporarily unavailable, instead of being overwritten by the fallback.

## [1.10.3.3] — 2026-11-08

Full details: [`docs/releases/v1.10.3.3.md`](docs/releases/v1.10.3.3.md).

### Changed

- The detached/fullscreen player controls overlay the video instead of taking a
  separate row, so showing or hiding them never resizes the video.
- The rounded, semi-transparent overlay appears on mouse movement and hides
  2.5 seconds after the pointer leaves; phone-remote commands do not open it.

## [1.10.3.2] — 2026-11-08

Full details: [`docs/releases/v1.10.3.2.md`](docs/releases/v1.10.3.2.md).

### Fixed

- The phone remote controls the detached/fullscreen video window: play/pause,
  stop, seek and volume use its real position and playback state instead of the
  paused main player.

## [1.10.3.1] — 2026-11-07

Full details: [`docs/releases/v1.10.3.1.md`](docs/releases/v1.10.3.1.md).

### Fixed

- Installed build crashed at startup in `MainPage.Connect` with
  `InvalidCastException` while connecting the search-empty hint. The hint no
  longer uses a generated `x:Name` field and is updated through its parent
  panel, so the application starts normally again.

## [1.10.3.0] — 2026-11-07

Full details: [`docs/releases/v1.10.3.0.md`](docs/releases/v1.10.3.0.md).

### Search

- Universal filename parsing: separators, track numbers, reverse order,
  collaboration markers and artist names in `(Artist)` / `[Artist]` brackets.
- Raw-name indexing, technical-tag cleanup, stop-word handling, aliases,
  transliteration/layout correction, Damerau–Levenshtein typos, phonetic keys,
  stemming fallback and N−1/free-text matching.
- Favorites, play counts, recency, artist intent and diversity cap in ranking.
- LRU cache invalidation, grouped versions and `Suggest()`.
- Automatically created `search_aliases.json` and `search_config.json` with safe
  fallback to built-in weights.

### Web remote

- Mobile catalog cards wrap long titles and use one consistent icon-button row.
- Language selector shows `EN` / `RU` short codes.
- Playlist loading indicator, host-mode prefetch and a short-lived cache.
- Search suggestions and localized “refine your query” feedback.

## [1.10.2.7] — 2026-11-07

Full details: [`docs/releases/v1.10.2.7.md`](docs/releases/v1.10.2.7.md).

### Changed

- Distribution is now **Inno Setup only**: MSIX/AppX manifest, packaging scripts,
  package-only assets and Microsoft Store instructions were removed.
- User data always lives in `%LocalAppData%\TempoKaraoke`.

### Fixed

- Single-instance handling is Mutex-first; a second launch restores and focuses
  the existing window. Removed the AppLifecycle redirect that could crash with a
  `CoreMessagingXP.dll` stowed exception (`0xc000027b`).

### Localization

- The installed `localization` folder is read at startup and by the web remote;
  adding a manifest entry and catalog there is picked up after restarting the app
  without rebuilding that installed copy.

## [1.10.2.6] — 2026-11-07

Full details: [`docs/releases/v1.10.2.6.md`](docs/releases/v1.10.2.6.md).

### Fixed

- The Start/Pause button on the remote no longer gets stuck: playback is tracked
  by the player's own flag, so the button and the status change immediately.
- The time slider moves and performs a seek; incoming updates no longer
  interrupt dragging.
- The current track stays visible while paused.
- WebSocket reconnects automatically, state is polled every 3 s, and a refresh
  is requested when the tab or the network comes back; the server sends
  keepalives and broadcasts snapshots in parallel with a timeout.

## [1.10.2.5] — 2026-11-07

Full details: [`docs/releases/v1.10.2.5.md`](docs/releases/v1.10.2.5.md).

### Fixed

- The right-click context menu on a video card no longer leaves the cursor
  "busy": the normal arrow is shown while the menu is open, and the cursor is
  restored once it closes.

## [1.10.2.4] — 2026-11-07

Full details: [`docs/releases/v1.10.2.4.md`](docs/releases/v1.10.2.4.md).

### Fixed

- In the collapsed navigation pane, an update-available badge is shown on
  "Settings"; in the expanded pane, the previous "Update"/"New" chip is kept.
- The update toast now appears only during the automatic check at startup, not
  when "Check for updates" is clicked.

## [1.10.2.3] — 2026-11-07

Full details: [`docs/releases/v1.10.2.3.md`](docs/releases/v1.10.2.3.md).

### Added

- Right-click context menu on video cards.
- Control panel for selected library cards: counter, clear, "Play next",
  "Add to queue", "Add to", and select all.

### Changed

- The selection checkbox is now in the top-left corner and appears only on
  hover/selection; the version counter has moved to the top-right corner.
- The "Play" button ring changes its gradient on hover and focus.

## [1.10.2.2] — 2026-11-07

Full details: [`docs/releases/v1.10.2.2.md`](docs/releases/v1.10.2.2.md).

### Fixed

- In the light theme, the "minimize/maximize/close" buttons no longer get lost
  against the light background: the system caption colors are set from the
  window theme and updated when it is switched.

## [1.10.2.1] — 2026-11-07

Full details: [`docs/releases/v1.10.2.1.md`](docs/releases/v1.10.2.1.md).

### Fixed

- The toast now collapses multi-line changelog entries into the first sentence;
  the version and the installer size are visible from the first line.
- Cold start from the toast: notifications are registered before the window is
  created, a `PendingUpdateAction` queue, command-line arguments, and protection
  against double handling.
- Activation diagnostics in the log.

## [1.10.2.0] — 2026-11-07

Full details: [`docs/releases/v1.10.2.0.md`](docs/releases/v1.10.2.0.md).

### Added

- Informative update toast: versions, changelog, size, publication date, logo,
  Tag/Group, and a 24-hour lifetime.
- Clicking the toast body opens "Settings › Updates"; the
  "Install"/"Later"/"What's new" buttons, cold start, and an AppLifecycle
  redirect.
- An "Update"/"New" chip on the "Settings" item instead of a grey dot.
- A testable `UpdateToastBuilder` and tests for changelog markup and parsing.

### Fixed

- The toast no longer shows the `Update_ToastTitle`/`Update_ToastBody` keys:
  strings are resolved before the XML is built, with fallback literals.
- A repeated notification replaces the previous one (Tag/Group) instead of
  duplicating it.

## [1.10.1.0] — 2026-11-07

Full details: [`docs/releases/v1.10.1.0.md`](docs/releases/v1.10.1.0.md).

### Added

- Full update checking in "Settings › Updates": a loading indicator, a disabled
  button, and the "checking", "up to date", "update available", and
  "network error" states.
- Confirmation of the update download and installation in a `ContentDialog`
  matching the window theme; an "Install vX" button.
- Localized update statuses (RU/EN) that refresh without a restart.
- Inno Setup installer improvements: a bilingual question about deleting data, a
  safe default button, automatic version detection, GitHub links,
  `taskkill /t`, removal of `install-scope.txt`, and automatic launch after a
  silent update.

### Fixed

- A `404 Not Found` from GitHub is no longer reported as a network error.
- The update status no longer stays empty in the "App version" card.

## [1.9.0.0] — 2026-11-06

Full details: [`docs/releases/v1.9.0.0.md`](docs/releases/v1.9.0.0.md).

### Added

- Data-driven localization: `localization/manifest.json`, JSON catalogs,
  generation of `.resw` files and web locales, OS language selection, and
  per-key fallback.
- A persistent Host/Admin PIN with PBKDF2-SHA256 and a security section.
- Rotating logs and a ZIP error report with masking of private data.
- Wi-Fi Direct safe mode, a state machine, operation serialization, and adapter
  diagnostics.
- JS error reporting from the web remote.

### Changed

- The desktop and web language selectors are built from the manifest.
- English plural forms `One` use `{0}`.
- `/api/auth` no longer consumes admin attempts when the host signs in
  successfully.

### Fixed

- Overlapping nested playlists and leftovers from the previous screen during
  navigation.
- Stale deferred renders after a fast section switch.

## [1.8.22.3] — earlier

History before 1.9.0.0 is available in the tags and GitHub Releases.

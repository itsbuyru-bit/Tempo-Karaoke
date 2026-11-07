English | [Русский](../ru/FEATURES.md)

# Tempo Karaoke — complete feature guide

Tempo Karaoke is a native Windows karaoke player for local video libraries. It
combines a fast catalog, smart search, a persistent queue, playlists, and a
browser-based remote control for phones and tablets. The application does not
ship with media: it indexes the folders you choose and never modifies them.

## System requirements

| Item | Requirement |
|---|---|
| OS | Windows 10 version 1809 (build 17763) or newer, Windows 11 recommended |
| Architecture | x64 |
| Runtime | None for the portable installer — .NET and Windows App SDK are bundled |
| VLC | Not required; `libvlc` is shipped with the application |
| Disk | About 110 MB for the installer plus space for your media library |

## Library and indexing

- Add any number of video folders in **Settings → Libraries**.
- Each folder is scanned recursively. Only video files are indexed:
  `.mkv`, `.mp4`, `.avi`, `.mov`, `.webm`, `.m4v`, `.wmv`. Audio-only files such
  as `.mp3` are intentionally excluded.
- The media library is read-only. Tempo Karaoke never renames, moves or deletes
  source files.
- A SQLite cache (`media.db`) stores file size, modification time, duration,
  resolution and thumbnail paths. Repeated launches start instantly and only
  changed files are re-read.
- Hidden folders and unreadable paths are skipped safely; scanning shows
  progress and can be cancelled.
- The **All videos** view supports sorting by title or modification date,
  filtering by folder, and switching between the flat catalog and the folder
  browser.
- Duplicate performances and different versions of the same song are grouped
  into one card. The version counter opens a list with folder, format and size.
- Cards show a title, duration and a thumbnail when one can be extracted.
- Background metadata and thumbnail workers keep scrolling responsive.

## Instant smart search

- Search starts as you type and is served from an in-memory index — no database
  round-trip per keystroke.
- File names are cleaned of technical noise: quality tags, codec markers,
  `[KARAOKE]`, `[TEXT]`, underscores and repeated separators.
- Transliteration works both ways (Cyrillic ↔ Latin), so `kishlak` finds
  `Кишлак` and vice versa.
- A wrong keyboard layout is corrected automatically (`rjpf` → `киза`-style
  mistakes are handled by layout mapping).
- Fuzzy matching tolerates typos and partial words; results are ranked by
  relevance rather than plain alphabetical order.
- Search works across the current library and keeps the same version grouping.
- `Ctrl+F` focuses global search from anywhere in the application.

## Cards, selection and context actions

- Double-click or press **Play** on a card to start a song.
- The **···** button opens the card menu:
  - Play now
  - Play next
  - Add to queue
  - Add to a new or existing playlist
  - Add to / remove from favorites
  - Properties (codec, resolution, duration, size, path)
  - Select
- Right-clicking anywhere on a card opens the same menu at the pointer.
- Hovering a card reveals a selection checkbox in the top-left corner and the
  play/ellipsis overlay. The checkbox stays visible for already selected cards,
  so the grid is not cluttered with permanent checkboxes.
- With one or more cards selected, a selection bar appears above the grid:
  - selected count
  - clear selection
  - play selected next
  - add selected to queue
  - add selected to a playlist
  - select all / clear all
- Bulk actions preserve the order of the visible cards.

## Playback

- Video playback is powered by **LibVLC**, with hardware decoding where the
  system supports it.
- Container support follows LibVLC: MKV, MP4, AVI, MOV, WebM, M4V, WMV and other
  video formats.
- Embedded subtitles and karaoke text tracks are rendered by the player.
- Full-screen mode: `F11` or the full-screen button; `Esc` exits.
- Detached video window: move the video to a second monitor, projector or TV
  while keeping the control panel on the main screen.
- Transport controls: play/pause, previous, next, stop, seek, volume, mute.
- Playback speed: 0.5×, 0.75×, 1×, 1.25×, 1.5×.
- Ten-band equalizer with presets.
- Shuffle and repeat modes: off, repeat all, repeat one.
- Optional countdown before the next track, with a configurable duration.
- The last played position is remembered and offered when the same file is
  opened again.
- Recently played tracks and favorites are recorded locally.

## Queue

- Add songs to the queue from cards, search results, folders, playlists or the
  remote control.
- **Play next** inserts a track immediately after the current one.
- Reorder by drag and drop; remove individual tracks or clear the queue.
- Undo the last removal.
- Selection mode in the queue mirrors the library: select all, clear, play next,
  add to a playlist, remove.
- The queue survives application restarts. Requester names are preserved for
  songs ordered from the web remote.
- The queue shows the current track, the list of upcoming tracks and the total
  length.

## Playlists

- Create, rename and delete named playlists.
- Add tracks from the library, queue, search or another playlist.
- Reorder playlist items by drag and drop.
- Play a playlist directly or append it to the current queue.
- Import and export playlists in **M3U/M3U8** format.
- Playlists are stored locally in `playlists.json`.

## Web remote for phones and tablets

- Start the built-in HTTP/WebSocket server in **Settings → Web remote** or from
  the sidebar.
- Connect a phone by scanning the QR code; no app installation is required.
- Works over the local Wi-Fi network or through **Wi-Fi Direct** (direct phone
  connection without a router).
- Roles:
  - **Guest** — search the catalog, inspect the queue, order songs.
  - **Host** — sign in with a 4-digit PIN to control playback, volume, queue
    order, moderation and skipping.
  - **Admin** — sign in with the admin PIN to change remote settings.
- The remote shows the current track, state, position, duration, queue and
  upcoming tracks.
- Host controls: play/pause, stop, previous, next, seek slider, volume.
- Guests can request songs; requests can be limited per guest and moderated by
  the host.
- Optional auto-stop after a period of inactivity.
- A list of connected devices with names and connection state.
- PINs can be temporary (regenerated on every launch) or persistent. Only a
  salted PBKDF2 hash is stored, never the PIN itself.
- The remote state updates over WebSocket, with automatic reconnect, keepalive
  pings and a polling fallback. After the phone wakes up or the network changes,
  the page refreshes immediately.
- The remote UI is available in Russian and English.

## Settings

Settings are organized as a catalog of sections:

- **Interface** — language (system, Russian, English), light/dark/system theme,
  window placement reset.
- **Libraries** — add and remove video folders, rescan, inspect indexed files.
- **Playback** — countdown before the next track and its duration.
- **Web remote** — server, QR code, PINs, roles, auto-stop, Wi-Fi Direct,
  network diagnostics.
- **Security** — host/admin PINs, persistent PIN option, PIN state.
- **Diagnostics** — startup timings, system and display information,
  configuration integrity, logs and error reports.
- **Updates** — current version, manual check, install update, release notes.
- **About** — version, links, licenses.

## Updates

- Automatic check for the latest GitHub release once per launch (can be turned
  off).
- Manual **Check for updates** in **Settings → Updates**:
  - progress ring and a disabled button while the request runs
  - up-to-date state with a green check
  - available update state with the new and current versions
  - clear network error state
- The automatic check shows a system toast with the version transition, size,
  1–2 changelog highlights and publish date. Buttons: **Install**, **Later**,
  **What's new**. Clicking the body opens **Settings → Updates**.
- A repeated notification replaces the previous one instead of stacking.
- The sidebar badge shows **Update** for an available version and **New** when
  the installer is already downloaded. In the collapsed sidebar an accent badge
  appears on the Settings gear.
- Before installation, a confirmation dialog explains that the app will close;
  the installer runs silently and starts the new version while preserving
  settings, queue, playlists and cache.
- `404 Not Found` from GitHub (no releases yet) is treated as up to date.

## Diagnostics and reliability

- Rotating local logs with configurable logging.
- One-click ZIP error report with system details and privacy masking.
- Startup timeline from process start to interactive UI.
- Display, DPI and window placement diagnostics.
- Configuration integrity check for `settings.json` and localization catalogs.
- Network and Wi-Fi Direct diagnostics.
- Crash logging even when normal logging is disabled.

## Localization

- Russian and English UI, switchable at runtime without restart.
- The language list is driven by `localization/manifest.json`; adding a language
  does not require rebuilding the desktop UI.
- Desktop `.resw` files and web locale JSON files are generated from one source.
- Dates and numbers follow the current culture.
- Keyboard navigation and accessible names are provided for interactive
  controls.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+F` | Focus global search |
| `F11` | Toggle full screen |
| `Esc` | Exit full screen |
| `Ctrl+I` | Media information |
| `Ctrl+E` | Equalizer |
| `Enter` | Play the selected card / queue item |

## Data and privacy

- User data is stored in `%LocalAppData%\TempoKaraoke` (portable build) or in the
  package `LocalState` (MSIX):
  - `media.db` — indexed media metadata
  - `settings.json` — settings, window placement, update state
  - `queue.json` — saved queue
  - `playlists.json` — playlists
  - `remote-clients.json` — authorized remote devices
  - `activity.json` — favorites and history
  - `logs/` — rotating logs
  - `Reports/` — user-created diagnostic ZIPs
- The media library is never modified. Media files are not uploaded anywhere.
- Permanent PINs are stored as salt + PBKDF2 hash.
- Error reports mask private paths and hostnames.
- See [PRIVACY.md](../../PRIVACY.md) for the full policy.

## Installation and packaging

- **Portable + Inno Setup installer** — per-user installation without UAC by
  default; the folder can be changed; optional all-users mode.
- **MSIX** — for Microsoft Store or managed deployment; updates are delivered by
  Windows/Store.
- The installer removes only its own files; user data is kept by default.
- Build scripts and release process: [PACKAGING.md](PACKAGING.md).
- Architecture decisions: [UX-DECISIONS.md](UX-DECISIONS.md).
- Window and DPI behavior: [WINDOW-PLACEMENT.md](WINDOW-PLACEMENT.md).
- Web remote internals: [WIFI-DIRECT.md](WIFI-DIRECT.md),
  [PIN-SECURITY.md](PIN-SECURITY.md).
- Logging and reports: [DIAGNOSTICS.md](DIAGNOSTICS.md).
- Localization workflow: [LOCALIZATION.md](LOCALIZATION.md).

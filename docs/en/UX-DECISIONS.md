English | [Русский](../ru/UX-DECISIONS.md)

# UX Decisions — Tempo Karaoke

Status: the UX interview was completed on 2026-10-28. The decisions below are the basis for v1.

> For the current state of the implementation and for the differences from this
> document, see [`README.md`](../../README.md). Some of the decisions (online cover
> art, the link to the system indexing settings) are not implemented yet.

## Name

- App: **Tempo Karaoke**
- Repository: **`tempo-karaoke`**
- The working folder is still called `AppPlayList_Karaoke`; the project is
  `src/TempoKaraoke`.

## Settled

### Library

- Video only: `.mkv`, `.mp4` and other video extensions.
- `.mp3` files are not indexed.
- Switching between "backing/vocals" audio tracks is not needed: the files carry a
  single track with embedded lyrics.

### Settings

- "Video library paths": a list of roots, add a folder, remove a folder.
- "Refresh libraries": re-read the selected roots.
- The "Manage system indexing settings" link.
- "App theme": system, light, dark.
- "Accent color": the system accent only for now.
- "Search for cover art online": the toggle is persisted, downloading cover art is
  the next stage.
- Music paths are not implemented: the project works with video only.

### Main screen

- The selected page takes the full width: "Home", "Video library",
  "Playback queue", "Playlists" or "Settings".
- The 160×90 video preview sits in the bottom-left corner of the now-playing bar.
- On the left, an expanded `NavigationView` with labels: Home, Video library,
  Queue, Playlists, Settings; search lives in `PaneHeader`.
- Custom title bar: menu, "back" when there is history, icon and title;
  the right region starts at the top edge.
- "Home": recent files and quick access.
- "Video library": a grid of cards, "All videos" / "Video folders" tabs,
  filter by folder, sorting, "Add folder", "Play all".
- "Settings" — collapsible cards (`Expander`).
- Mini player: the preview is highlighted on hover; a click opens the video in a
  separate expanded window, with the title and a return button remaining at the
  bottom.
- The queue is a separate page, not a permanent panel on the right.
- Bottom now-playing bar: a seek bar with the time on the left and on the right;
  below it the 160×90 preview and the title; in the center shuffle / prev /
  play-pause / next / repeat; on the right media info, volume, fullscreen,
  PiP/detach and overflow (holding −10, +30 and stop). Volume is a flyout popup:
  a speaker icon for mute, a slider and a numeric value.
- Initial window size matches Media Player: 1810×1123.
- "Playlists" and "Settings" are still stubs / partially ready.
- The playback window can be **detached** from the main window that holds search
  and the playlist.
- Normal mode — embedded video; detached mode — a separate window.
- In the detached window: video plus a compact control panel (play/pause, stop,
  prev/next, seek, volume); the panel auto-hides.
- While detached, the main window keeps playlist and search controls;
  the transport controls are not duplicated.
- In place of the video, the main window shows a status line "Video in a separate
  window" with a "Return" button; the library and the queue take the freed space.
- The detached window opens **on the current monitor**; the user drags it to the
  TV/projector themselves. Position and size are remembered, but when the monitor
  changes they are clamped to the work area so the window does not end up
  off-screen.

### Detach implementation

- The main window stays on libvlc; on detach the player is paused.
- The separate window uses `MediaPlayerElement` (Media Foundation), because
  libvlc WinUI does not support two simultaneous video outputs.
- Return: the main window is restored, the position is handed over to libvlc, and
  then the separate window is closed.
- Limitation: rare codecs that do not open in Media Foundation may fail to play in
  the detached window; libvlc plays them in the main window.

### Search

- A single box on the main screen; results update from the first character.
- `↑/↓` — navigate, `Enter` — default action, `Esc` — clear,
  `/` — put the cursor in the search box.
- Filters — chips above the list: folder/collection, format, duration.
- Name normalization, transliteration, keyboard-layout and typo correction happen
  on the search index side, with no separate settings.

### Video controls

- play/pause, stop, previous/next;
- seek bar: current/total time, click and drag;
- volume and mute;
- fullscreen;
- seek ±5/10 seconds;
- speed 0.5–2×;
- aspect ratio;
- panel auto-hide in fullscreen mode;
- hotkeys: `Space`, `←/→`, `Ctrl+←/→`, `↑/↓`, `F`, `Esc`, `Enter`.

### Queue and playlists

- The queue is a separate "Playback queue" page.
- "Add files" (split): files, an entire folder, URL.
- "Clear" and removing individual items; "Add to" is disabled for now until
  playlists are available.
- The current queue is saved and restored after a restart.
- Named playlists: save, load, rename, delete.
- M3U/M3U8 export and import — compatibility with VLC and other players.
- Auto-advance to the next song; repeat and shuffle are off by default.
- The queue order can be changed by dragging; the current song is not reset.

### Fullscreen

- `F11` maximizes the current window, `Esc` exits.
- In the detached window, `F11` maximizes it on the monitor where it currently is.
- In fullscreen mode the control panel auto-hides and appears on mouse movement or
  on pressing any transport key.

### Phone remote (v1)

- v1 includes a phone remote: a direct connection on the same network.
- If the phone and the PC are not on the same Wi-Fi network, provide a direct
  Wi-Fi Direct connection or a clear fallback if Windows/the adapter cannot bring
  it up.
- A QR code is shown on the PC; the phone scans it and opens a window with the
  playlist.
- The remote is a web page, with no separate app to install on the phone.
- Full remote: search, queue, play/pause, next/prev, volume.
- "Host only" mode: guests can be denied playback control while keeping search and
  adding to the queue.
- Several phones at once; the shared queue is synchronized in real time. Roles:
  host (controls playback) and guest (searches and adds).
- If there is no shared Wi-Fi: the app brings up Wi-Fi Direct automatically; if
  Windows or the adapter does not allow it, it shows step-by-step instructions.
  For Wi-Fi Direct two QR codes are shown: joining the TempoKaraoke network and
  opening the remote.
- Protection: the QR code carries a one-time session token. With it a guest can
  search and add songs right away; playback control is unlocked after entering the
  host's PIN.

### Duplicates and versions

- One card per song: grouping by normalized artist and title.
- An "N versions" badge; the card expands and shows files, folders and quality.
- The best version plays by default: higher resolution/bitrate first, then the
  newer file.
- If the artist/title could not be parsed or the match is doubtful, the files are
  shown separately, without merging.

### Sorting

- Default: artist → title; when there is no artist, by title.
- The user can switch sorting in the UI (title, collection, date modified).

### Appearance

- Dark theme by default, large controls, Fluent/Mica.
- A light theme can be added later; there is no switch in v1.
- The list and the queue are readable from the couch/TV: large font, prominent
  highlighting of the current song, clear focus states.

### Behavior

- Clicking a song selects it; double-click or `Enter` plays it; the "+" button adds
  it to the queue. Browsing the list neither starts nor adds anything on its own.
- "Next" — the next item in the queue; if the queue is empty, the next item in the
  current list.
- "Stop" resets to `00:00`; "start" plays from the beginning.

### Language

- The UI is Russian only.
- All strings live in resources so that English can be added later without
  rewriting XAML.
- Song titles and paths stay as they are in the files.

## Not blocking v1 (accepted defaults)

- Distribution: a portable build first, MSIX later.
- History and favorites: after v1; v1 has only the queue and named playlists.
- Playback position: always from the beginning; for karaoke this is expected.
- Storage: SQLite; the search index is SQLite FTS5 or in-memory, decided at
  implementation time.
- Remote port: a random high port; the QR code contains the address and the token.
  Windows Firewall may ask for permission — that is normal.
- The host PIN is stored locally in hashed form.

## v1 acceptance criteria

1. The app launches and shows three zones: search/library, queue, player.
2. Search responds in < 50 ms, finds by Russian, by Latin and with a typo; versions
   of the same song are grouped.
3. Double-click/`Enter` plays, "+" adds to the queue; the queue can be dragged,
   is saved and restored.
4. play/pause, stop, prev/next, seek, volume/mute, fullscreen, ±5/10 s, speed,
   aspect ratio and hotkeys all work.
5. The video window detaches and returns; the detached window has video plus a
   compact panel, the main window has search/queue and a "Return" line.
6. Named playlists save/load, M3U exports and imports.
7. A phone connects via QR on the same network; guests add songs, the host
   controls playback after entering the PIN; the queue is synchronized between
   devices.
8. 20 random MKV/MP4 files from `G:\Karaoke` play without desync or hangs.

## First step

1. The project is renamed: `src/TempoKaraoke`, namespace, manifest,
   window title.
2. `git init` in the project folder and the first commit.
3. Build the main window skeleton: three zones, dark theme, transport controls,
   detaching and returning the video.

Verifiable result: **Tempo Karaoke launches, plays MKV, all transport controls
work, the video window detaches and returns**; search and the queue are still
stubs.

---

## Navigation and nested playlists (2026-11-06)

An overlay bug has been fixed: when navigating from the menu straight into a
nested playlist, the previous screen (settings, "About", library) stayed visible in
the same `Grid` cell. All top-level transitions now go through
`ShowTopLevelView`, which first hides all screens and nested panels and then shows
the requested one. Deferred localization application checks the navigation
generation number and does not re-render a screen that has been left.

Manual checklist:

- [ ] click several menu items in quick succession;
- [ ] open a nested playlist from the list and from the side menu;
- [ ] go back and open another playlist;
- [ ] open a playlist and switch the language;
- [ ] open a playlist, change the queue or wait for the list to refresh;
- [ ] open a library folder, go to another section and come back;
- [ ] check that the parent container holds no remnants of the previous screen.

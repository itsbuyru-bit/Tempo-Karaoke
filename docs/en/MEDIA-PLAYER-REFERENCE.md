English | [Русский](../ru/MEDIA-PLAYER-REFERENCE.md)

# Microsoft Media Player — UI and codec reference

Inspection date: 2026-10-28.
Package: `Microsoft.ZuneMusic_11.2609.11.0_x64__8wekyb3d8bbwe`.
The app was launched and inspected through UI Automation; this is a **reference**,
not a copy of Microsoft's code or resources.

## What we looked at

- Home: "Recent media files", the `Karaoke` folder card.
- Video library: "All videos" / "Video folders" tabs, "Add folder", "Play all",
  A–Z sorting, a grid of cards with cover images.
- Playback queue: current item highlighted, duration, "Clear", "Add to", a list
  with drag reordering.
- Now Playing: fullscreen mode with a "Back" button.
- Settings: music and video library paths, library refresh, theme, contrast,
  online cover art lookup.

## UI structure

```
┌──────────────────────────────────────────────────────────────┐
│ Back  Search                                 Open files ▾    │
├───────────────┬──────────────────────────────────────────────┤
│ Home          │  Page title                                  │
│ Music         │  Tabs / filters / sorting                    │
│ Video         │  Cards or list                               │
│ Queue         │                                              │
│ Playlists     │                                              │
│               │                                              │
│ Settings      │                                              │
├───────────────┴──────────────────────────────────────────────┤
│ 00:00 ─────────●────────────────────────────── 03:23         │
│ [cover] Title         ⤨  ⏮  −10  ▶  +30  ⏭  ⟳  ⧉  🔊  ⛶  ▣  … │
└──────────────────────────────────────────────────────────────┘
```

Key elements:

- a left `NavigationView` with sections and settings at the bottom;
- global search in the top bar;
- an "Open files" `SplitButton` with additional options;
- a bottom now-playing bar: progress, cover, title, shuffle, previous, skip
  −10 s, play/pause, skip +30 s, next, repeat, cast, volume, fullscreen, mini
  player, overflow;
- a dedicated queue page rather than only a side panel;
- a fullscreen Now Playing view with minimal controls.

## Codecs

Media Player **does not ship its own codecs**: it uses Windows Media Foundation
and system extensions. The only dependencies in the package are `Microsoft.UI.Xaml`,
.NET Native and VCLibs — no codecs among them.

Extensions installed on the system:

| Package | Version |
|---|---|
| `Microsoft.HEVCVideoExtension` | 2.5.33.0 |
| `Microsoft.AV1VideoExtension` | 2.0.35.0 |
| `Microsoft.VP9VideoExtensions` | 1.2.20.0 |
| `Microsoft.MPEG2VideoExtension` | 1.2.32.0 |
| `Microsoft.WebMediaExtensions` | 2.1.51.0 |
| `Microsoft.AVCEncoderVideoExtension` | 1.1.47.0 |

Practical takeaways:

- H.264/AAC, MP4 and MKV on Windows 10/11 usually play through the built-in MF;
- HEVC, AV1, VP9 and MPEG-2 are available thanks to the installed extensions;
- AC3/DTS and exotic formats depend on system decoders and may fail to open;
- our **libvlc** in the main window ships its own ffmpeg and supports more
  formats than MF;
- the detached window currently runs on `MediaPlayerElement`/MF, so its format
  support matches the system's rather than libvlc's.

## What is worth adopting

1. **Bottom now-playing bar**: cover/frame, title, progress, ±10/30 s,
   shuffle/repeat, volume, fullscreen, mini player, overflow.
2. **NavigationView**: Home, Video library, Queue, Playlists, Settings; settings
   at the bottom, search at the top.
3. **Video library**: "All videos" / "Video folders" tabs, "Add folder",
   "Play all", sorting, cards with previews.
4. **Queue as its own page**: current item, duration, clear, add to playlist,
   drag reordering.
5. **Now Playing**: fullscreen mode with minimal controls and a back button.
6. **Mini player / compact overlay**: useful for collapsing karaoke and still
   managing the queue.
7. **Languages and subtitles**, media info, overflow menu.
8. **Settings**: library paths, refresh, theme, contrast.
9. **Recent files / resume** and drag-and-drop file opening.
10. **Keyboard/media keys**: play/pause, next/prev, seek, volume, fullscreen.

## Settings

Sections reviewed:

- "Music library paths";
- "Video library paths";
- "Refresh libraries";
- "App theme";
- "Contrast color";
- "Search the internet for missing album art and artist images" (enabled);
- "Media Player" — a service section of the app;
- "Help" and "Feedback".

There are no separate video/codec/decoder settings in the UI: codecs are
entirely system-provided (Media Foundation plus the installed extensions). This
confirms that there is nothing to copy from Media Player's "codec settings" —
only library paths, refresh, theme, contrast and online cover art can be adopted.

## Icon font

The app resources include `resources/fonts/MediaPlayerIcons.ttf` — a proprietary
Microsoft font. **Copying it into Tempo Karaoke is neither necessary nor
advisable**: it is someone else's asset. Instead we use the system
`Segoe Fluent Icons` through `SymbolIcon`/`FontIcon` — icons with the same
meaning (prev/next, play/pause, stop, volume, fullscreen, detach, search,
navigation), without copying a proprietary font.

- Microsoft icons, cover art, fonts and other assets;
- the app's XAML/code;
- brand names and styling as a "Media Player clone".

We reproduce the structure and behavior with WinUI 3/Fluent, under Tempo
Karaoke's own visual identity.

## How this maps onto Tempo Karaoke

- Today we have three zones: library + player + queue, with the transport at
  the bottom.
- From Media Player it makes sense to take the bottom now-playing bar, the
  cards/grid, a dedicated queue page, fullscreen Now Playing and the mini
  player.
- The NavView variant is closer to Media Player, but the queue matters more in
  karaoke than in an ordinary player; we may want to keep the queue always
  accessible.
- Codecs: we keep libvlc in the main window; the detached window on MF is a
  deliberate trade-off — if a codec fails, we bring the video back into the main
  window.

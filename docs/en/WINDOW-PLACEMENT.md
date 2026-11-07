English | [Русский](../ru/WINDOW-PLACEMENT.md)

# Window, DPI, and startup

This document describes how the main window is placed, how the user's position is
persisted, and how startup is sped up. Implementation:
`Services/WindowPlacementService.cs`, `Services/WindowPlacementMath.cs` (Core),
`Services/StartupTrace.cs`, `MainWindow.xaml.cs`, `MainPage.xaml.cs`.

## Initial size and centering

The size is derived from the **work area** of the current monitor (excluding the
taskbar), not from the physical screen size in inches:

```
minWidth  = 1024 DIP     minHeight = 700 DIP
maxWidth  = 1920 DIP     maxHeight = 1200 DIP
width  = clamp(workArea.DIP.Width  * 0.75, minWidth,  maxWidth)
height = clamp(workArea.DIP.Height * 0.75, minHeight, maxHeight)
x = workArea.X + (workArea.Width  - width)  / 2
y = workArea.Y + (workArea.Height - height) / 2
```

The limits are specified in DIP and converted to physical pixels using the
monitor scale (`GetDpiForMonitor`), so on a 4K display at 150% the window does not
become gigantic. `AppWindow.MoveAndResize` takes physical pixels — the conversion
happens in `WindowPlacementService`.

## Saving the placement

`settings.json`:

```json
{
  "Window": {
    "UserPositioned": true,
    "Maximized": false,
    "X": 100,
    "Y": 80,
    "Width": 1200,
    "Height": 800,
    "DisplayId": "\\\\.\\DISPLAY1",
    "DpiScale": 1.5,
    "SavedAt": "2026-11-06T23:00:00Z"
  }
}
```

- `X/Y/Width/Height` are in **DIP relative to the display's work area**, so copying
  the settings to a monitor with a different resolution or DPI does not leave the
  window off screen.
- `DisplayId` is the Win32 device name (`\\.\DISPLAY1`), and `DpiScale` is the scale
  factor at the time of saving (for diagnostics and migration).

The settings are written **only after the user actually changes** the window: we
subscribe to `AppWindow.Changed` (`DidPositionChange`/`DidSizeChange`/
`DidPresenterChange`) with an 800 ms debounce. Programmatic changes (initial
centering, reset, snapping to screen) run under the `_applyingPlacement` flag and
are not saved. A minimized window is skipped; on maximize, the `Maximized` flag and
the last "normal" rectangle are saved.

## Restoration and validation

1. No `UserPositioned` → the window is centered with the default size.
2. `UserPositioned` is set, but `DisplayId` no longer exists (the monitor was
   disconnected) → the window is centered on the current or primary monitor.
3. The saved rectangle is validated: the visible part must be at least 100 DIP on
   each side and 20% of the area; the size is clamped to the minimum/maximum and
   the work area, and the position is snapped to the screen.
4. An invalid, offscreen, or oversized rectangle is reset to the centered default
   size.
5. `Maximized` is restored after the normal rectangle has been applied.

Migration: older `settings.json` files without a `Window` block open centered;
blocks with coordinates from another monitor are automatically deemed invalid (the
visibility check and clamp).

## Resetting the placement

- Settings → "Window position" → **"Reset window position"** (applied
  immediately, no restart required).
- Command line: `TempoKaraoke.exe --reset-window` (resets the saved placement on
  the next launch).

## Monitors and DPI

- Monitors are enumerated with the Win32 `EnumDisplayMonitors`/`GetMonitorInfo`:
  `DisplayArea.FindAll()` fails with "class not registered" in an unpackaged build,
  so it remains only a fallback for the window work area.
- The DPI of each monitor comes from `GetDpiForMonitor`; the scale of the current
  window comes from `GetDpiForWindow`. The manifest declares `PerMonitorV2`.
- `DisplayAreaWatcher` (Added/Removed/Updated) and `XamlRoot.Changed` trigger a
  check: the window is snapped to the work area of the current monitor, and on a
  scale change the saved `DpiScale` is updated.

## Faster startup

- `StartupTrace` always writes milestones to the log (even when logging is
  disabled): `ProcessStart`, `App ctor done`, `OnLaunched`, `MainWindow ctor`,
  `Window placed`, `Navigate -> MainPage`, `MainPage ctor (done)`,
  `Window activated`, `MainPage loaded`, `First render`, `Deferred init`,
  `UI interactive`, plus a summary table `Startup timeline`.
- Heavy page operations (`InitializeAfterFirstRender`) run after the first frame:
  playlists, queue, recent items, and refreshing the remote device list. Each block
  is wrapped in `try/catch` with logging — a failure does not break startup.
- The media library is scanned asynchronously (as before), and the update check is
  started after `Loaded`.
- Example on the developer machine: first frame ≈ 750 ms, `MainPage ctor` ≈ 120 ms,
  deferred initialization ≈ 5 ms.

## Splash-screen limitation

`MainPage` is created **before** `Window.Activate()`: if the page is created after
activation, MRT fails to resolve the `x:Uid` resources and loading fails
(`Unable to resolve property ... Uid ...`). That is why no separate splash or
overlay is shown — the window appears already showing the UI, and the "black
window" is eliminated by moving the heavy work after the first frame. If an
explicit indicator is needed, it must be implemented as a separate lightweight
window.

## Verification

Manual scenarios: first launch on 1080p and on 4K/150%; copying the settings from
4K to 1080p; moving to a second monitor and restarting; disconnecting the second
monitor; changing the scale from 100% to 150%; maximized and restore; manually
shrinking the window to its minimum; a corrupted `settings.json`; `--reset-window`.

Automated tests: `WindowPlacementMathTests` (size, centering, clamp, visibility,
offscreen, minimum size) and `XamlResourceUidTests` (rejects `x:Uid` values that
differ only in case — such a pair prevented the app from opening the main page).

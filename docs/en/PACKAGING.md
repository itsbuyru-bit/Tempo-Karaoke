English | [Русский](../ru/PACKAGING.md)

# Packaging and installation

Tempo Karaoke ships as a portable Windows build wrapped in an **Inno Setup**
installer. There is no MSIX/AppX package and no Microsoft Store distribution.

## Source of truth

- Application version: `<Version>` in `src/TempoKaraoke/TempoKaraoke.csproj`.
  The installer reads the version from the built `TempoKaraoke.exe` by default.
- Installer script: `installer/TempoKaraoke.iss`.
- Portable publish profile: `src/TempoKaraoke/Properties/PublishProfiles/portable-x64.pubxml`.
- Output: `artifacts\TempoKaraoke-Setup-<version>.exe`.

## Build

Requirements: .NET SDK 10 and Inno Setup 6+ (`ISCC.exe`).

```powershell
dotnet publish src/TempoKaraoke/TempoKaraoke.csproj `
  -c Release -p:PublishProfile=portable-x64

iscc installer\TempoKaraoke.iss
```

Or run the helper script, which publishes the portable build and then compiles
the installer:

```powershell
.\scripts\build-installer.ps1
```

If Inno Setup is not on `PATH`, pass the compiler path or add it to `PATH`.
`/DMyAppVersion=1.2.3.4` overrides the version read from the exe.

## What the installer does

- Installs per user by default (no UAC); an all-users mode is available.
- Lets the user choose the installation folder.
- Creates Start Menu and optional desktop shortcuts with a stable AppUserModelID.
- Keeps `%LocalAppData%\TempoKaraoke` (settings, queue, playlists, cache, logs)
  unless the user explicitly chooses to remove it during uninstall.
- Stops running Tempo Karaoke processes before replacing files.
- Restarts the app after a silent update.

## Updates

The app checks GitHub Releases for `TempoKaraoke-Setup-<version>.exe`, downloads
it, and launches it silently after confirmation. The installer closes the app,
replaces files, and starts the new version.

## Localization after installation

The `localization\` folder is installed next to `TempoKaraoke.exe`. Editing
`manifest.json` and adding a `<tag>.json` there is enough for the desktop app to
pick the language up after a restart; no rebuild is required. Run
`node scripts/localization/generate.mjs` only when updating the repository
artifacts (`.resw` and `public/` fallback files).

## Troubleshooting

- Check that `ISCC.exe` exists and the publish folder is present.
- Use `SetupLogging=yes` output in `%TEMP%` for installer failures.
- If the app does not start, check `%LocalAppData%\TempoKaraoke\logs\`.
- If a language is missing, verify the `localization` folder next to the exe and
  restart the app.

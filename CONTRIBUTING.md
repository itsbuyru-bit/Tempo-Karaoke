English | [Русский](CONTRIBUTING.ru.md)

# Contributing to Tempo Karaoke

Thanks for helping! Tempo Karaoke is a native Windows karaoke player built with
WinUI 3, Windows App SDK, LibVLCSharp and SQLite.

## Before you start

- Use an issue to discuss a new feature or an architectural change first.
- The media library is read-only: never rename, move or delete source media.
- Only video files are indexed. Audio-only files such as `.mp3` are excluded.
- User-facing strings must be localized in both Russian and English.
- Do not commit media, build output, logs, certificates or local agent configs.

## Build and test

Requirements: Windows 10 1809+, .NET SDK 10, Visual Studio with Windows App SDK
components or WinApp CLI 0.7.1.

```powershell
dotnet build TempoKaraoke.slnx -c Debug
dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj
node scripts/localization/generate.mjs --check
```

Run the application:

```powershell
winapp run src/TempoKaraoke --arch x64
```

Build the installer (Inno Setup 7 or 6.3+):

```powershell
.\scripts\build-installer.ps1
```

## Code style

- C#: file-scoped namespaces, `_camelCase` private fields, nullable enabled.
- XAML: prefer `x:Bind` with an explicit `Mode=OneWay` for changing values.
- Keep UI text in `localization/<tag>.json`; regenerate the `.resw` files.
- Log failures with `LogService`; never log PINs, tokens or private paths.
- Update `CHANGELOG.md` and `CHANGELOG.ru.md` for user-visible changes.

## Documentation language

English is the primary language of the repository. Every product document has a
Russian counterpart:

| English | Russian |
|---|---|
| `README.md` | `README.ru.md` |
| `CHANGELOG.md` | `CHANGELOG.ru.md` |
| `PRIVACY.md` | `PRIVACY.ru.md` |
| `docs/en/*` | `docs/ru/*` |

When adding or editing a document, update both sides and keep the language
switch line at the top.

## Release process

Releases are cut from `main`:

1. Bump `<Version>` in `src/TempoKaraoke/TempoKaraoke.csproj` and
   `Identity/@Version` in `Package.appxmanifest`.
2. Add `docs/releases/vX.Y.Z.W.md` (user-facing release notes).
3. Add the same version to `CHANGELOG.md` and `CHANGELOG.ru.md`.
4. Commit, tag `vX.Y.Z.W`, and push the tag. The release workflow builds the
   portable installer and publishes it to GitHub Releases.

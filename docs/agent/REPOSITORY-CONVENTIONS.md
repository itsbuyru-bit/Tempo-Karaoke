# Repository conventions

Notes for maintainers and agents working in this project.

## Language and layout

- English is the primary documentation language. Root documents have Russian
  counterparts: `README.md` / `README.ru.md`, `CHANGELOG.md` /
  `CHANGELOG.ru.md`, `PRIVACY.md` / `PRIVACY.ru.md`, `CONTRIBUTING.md` /
  `CONTRIBUTING.ru.md`.
- Technical guides live in `docs/en/` and `docs/ru/`; each file starts with one
  language-switcher line.
- User-facing release notes live in `docs/releases/<tag>.md`. They are required
  for every tag and contain an English section followed by a Russian section.
- Agent-only notes live in `docs/agent/`; `AGENTS.md` is the workspace rulebook.
- UI strings are authored in `localization/*.json`; generated `.resw` and
  `public/locale*` files are never edited by hand.

## Release process

1. Bump `<Version>` in `src/TempoKaraoke/TempoKaraoke.csproj`.
2. Add `docs/releases/vX.Y.Z.W.md` with English and Russian notes.
3. Add the same version to `CHANGELOG.md` and `CHANGELOG.ru.md`.
4. Verify `dotnet build TempoKaraoke.slnx -c Debug`,
   `dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj` and the Inno
   installer (`scripts/build-installer.ps1`).
5. Commit with a conventional message, tag `vX.Y.Z.W`, and push the tag. The
   `release` workflow publishes `TempoKaraoke-Setup-<version>.exe`.

## Packaging

- Installation is **Inno Setup only**. Do not add MSIX/AppX packaging, Store
  manifests, package certificates or Store publishing steps.
- Publish with `Properties/PublishProfiles/portable-x64.pubxml`
  (`WindowsPackageType=None`), then compile `installer/TempoKaraoke.iss`.
- The version comes from the built exe; `/DMyAppVersion=` is an override.
- User data: `%LocalAppData%\TempoKaraoke`.

## Localization

- Source: `localization/manifest.json` + `<tag>.json`.
- `node scripts/localization/generate.mjs` regenerates `.resw` and
  `public/` artifacts; `--check` is used in CI.
- The installed app reads `localization` next to the exe. Editing that folder and
  restarting picks up a language without rebuilding. The web remote serves the
  same folder, with generated `public/` files as fallback.

## Website

- Site lives in `site/` and deploys through `.github/workflows/pages.yml`.
- Historical site snapshots are not stored in the repository; `site/` is the
  only current site source. Use Git history if an older revision is needed.
- The live remote in the site is a clone of `RemotePage`; keep English controls
  on the English page and Russian controls on the Russian page.
- Screenshots are captured from the running app and stored in
  `site/assets/screenshots/`.

## Verification before committing

- `dotnet build TempoKaraoke.slnx -c Debug`
- `dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj`
- `node scripts/localization/generate.mjs --check`
- `git diff --check`

## Русский

- Английский — основной язык документации; для корневых файлов есть русские
  пары. Технические руководства лежат в `docs/en/` и `docs/ru/`.
- Заметки к релизу обязательны для каждого тега: английская часть, затем
  русская, в `docs/releases/<tag>.md`.
- Версия задаётся в `TempoKaraoke.csproj`; установщик — только Inno Setup,
  MSIX/AppX и Microsoft Store не используются.
- Локализация: источник — `localization/`; установленная папка читается при
  старте, веб-пульт использует её же с fallback на `public/`.
- Сайт — `site/`; исторические версии сайта в репозитории не хранятся, при
  необходимости используйте историю Git.
- Перед коммитом: сборка, тесты, проверка локализации и `git diff --check`.

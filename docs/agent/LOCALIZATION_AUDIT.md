# Tempo Karaoke — localization audit

Date: 2026-11-03

## Scope

The audit covers the WinUI 3 desktop client and the mobile web remote.

Desktop files inspected:

- `src/TempoKaraoke/MainPage.xaml`
- `src/TempoKaraoke/MainPage.xaml.cs`
- `src/TempoKaraoke/PlayerWindow.xaml`
- `src/TempoKaraoke/PlayerWindow.xaml.cs`
- `src/TempoKaraoke/Services/RemotePage.cs`
- `src/TempoKaraoke/Services/RemoteServer.cs`
- `src/TempoKaraoke/Models/RemoteClientRow.cs`
- `src/TempoKaraoke.Core/Models/*` for displayed system labels

Web files inspected:

- embedded HTML/CSS/JS in `src/TempoKaraoke/Services/RemotePage.cs`
- `src/TempoKaraoke/public/locales/en-US.json`
- `src/TempoKaraoke/public/locales/ru-RU.json`

## Result

- Base locale: `en-US`.
- Secondary locale: `ru-RU`.
- Desktop resources: `src/TempoKaraoke/Strings/en-US/Resources.resw` and `ru-RU/Resources.resw` — **531 keys each**.
- Web resources: `src/TempoKaraoke/public/locales/en-US.json` and `ru-RU.json` — **123 keys each**.
- Source catalogs: `localization/en-US.json` and `localization/ru-RU.json` — **654 keys each** (531 desktop + 123 web).
- All generated `.resw` and web locale files are produced from the source catalogs by `scripts/localization/generate.mjs`.
- All visible XAML attributes with Russian text were removed; XAML now uses `x:Uid` and attached localization keys.
- All visible C# button labels, dialog titles/buttons/messages, context-menu labels, statuses and toast messages were moved to resource keys.
- Web static text uses `data-i18n` / `data-i18n-placeholder`; dynamic cards and toasts use `t(key)` / `tp(key, count)`.

## Architecture

### Desktop

- `localization/manifest.json` is the only supported-language list; `LocalizationService.Languages` is built from it.
- `x:Uid` provides standard MRT binding for initial visual-tree loading; generated `.resw` files stay as the compiled startup fallback.
- `Localization.Key` / `SecondaryKey` / `ToolTipKey` / `AutomationNameKey` register runtime elements for live language changes.
- `LocalizationService.Get(key)` resolves per key: current JSON catalog → `en-US` catalog → MRT resource → key. Missing keys never produce an empty string.
- `LocalizationService.Format` and `FormatPlural` handle interpolation and plural categories from the catalogs.
- The language is stored as a full BCP 47 tag from the manifest; startup resolution is saved choice → Windows language → language-group match → `en-US`.
- The settings selector is populated from the manifest and displays `nativeName`, not a hardcoded list.

### Web

- `localStorage['lang']` wins; otherwise `navigator.language` is matched against `/locale-manifest.json`, then the manifest fallback is used.
- The locale JSON is loaded from `/locales/{language}.json`; English is merged first, so each missing key falls back individually.
- `#langSelect` and the admin language selector are populated from the generated locale manifest; there are no hardcoded options.
- The header language selector is available to guests and the host.

## Pluralization

- English: `One` / `Other`.
- Russian: `One` / `Few` / `Many` / `Other`, selected by the standard Russian modulo rules.
- Desktop keys: `Plural_SongCount_*`, `Plural_VideoCount_*`, `Selection_Count_*`.
- Web keys: `playlist_tracks_one`, `_few`, `_many`, `_other`, selected with `Intl.PluralRules`.
- Counts are never composed by string concatenation in the UI.

## Deliberate exceptions

These are data, not UI strings, and intentionally remain untranslated:

- media titles, artists, file names, folder names and paths;
- playlist names and guest names entered by users;
- diagnostic log entries and raw exception messages;
- protocol/technical values such as `M3U`, `MKV`, `PIN`, IP addresses and MAC addresses.

System-generated labels such as `Computer`, `Guest`, `Host`, online/offline statuses and track counters are localized.

`MediaItem.QueueSubtitle`, `Playlist.CountText`, `FolderEntry.CountText`, duration captions and size units currently choose their system text from `CultureInfo.CurrentUICulture` because they live in the UI-independent Core project. The visible result follows the selected app language, including English/Russian plurals and `min`/`мин.` units, but those formatters are not read from `.resw`. Moving them behind an app-level converter is a follow-up if Core must become completely resource-free.

## Verification

- `dotnet build src/TempoKaraoke/TempoKaraoke.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64`
- `dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj`
- Automated checks:
  - en-US/ru-RU desktop key parity;
  - no Cyrillic values in en-US desktop resources;
  - Russian plural categories present;
  - en-US/ru-RU web key parity;
  - no Cyrillic in en-US web values;
  - every static/dynamic web key exists in both locales;
  - no hardcoded Russian UI attributes in XAML.
- Manual smoke: desktop English/Russian switch, library/queue/playlists/settings/remote, web English/Russian switch and persisted selection.

## Follow-up risks

- New UI strings must be added to the `localization/<tag>.json` catalogs; generated `.resw` and web locales must be regenerated, not edited by hand.
- `x:Uid` keys must remain unique per element; reusing one base across controls with different properties makes MRT try to resolve a property the control does not have.
- Adding a language requires only a `localization/<tag>.json` file and a `manifest.json` entry; the desktop and web selectors update automatically.
- The Core-only formatters mentioned above still use `CultureInfo.CurrentUICulture` rather than reading the JSON catalogs.



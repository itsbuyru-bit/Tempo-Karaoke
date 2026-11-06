# Tempo Karaoke localization guide

## 1. Single source of truth

All UI strings live under `localization/` in the repository root:

```text
localization/
  manifest.json      ← the only supported-language list
  en-US.json         ← base / fallback catalog
  ru-RU.json
  de-DE.json         ← add a file, add one manifest entry
```

The desktop `.resw` files and the web locale files are **generated** from these
catalogs and are committed for reproducible builds. Do not edit generated files
by hand:

```text
localization/<tag>.json
        ↓  node scripts/localization/generate.mjs
src/TempoKaraoke/Strings/<tag>/Resources.resw       (desktop / MRT)
src/TempoKaraoke/public/locales/<tag>.json          (web remote)
src/TempoKaraoke/public/locale-manifest.json        (web language selector)
```

`manifest.json` is the only place that decides which languages exist, what they
are called and whether they are enabled.

## 2. Manifest format

```json
{
  "version": 1,
  "fallback": "en-US",
  "languages": [
    {
      "tag": "en-US",
      "file": "en-US.json",
      "nativeName": "English",
      "englishName": "English",
      "enabled": true
    },
    {
      "tag": "ru-RU",
      "file": "ru-RU.json",
      "nativeName": "Русский",
      "englishName": "Russian",
      "enabled": true
    }
  ]
}
```

Rules:

* `tag` is a **full BCP 47 tag**: `en-US`, `ru-RU`, `de-DE`, `zh-CN`.
  Short tags (`de`, `fr`, `es`, `zh`) are rejected by validation and must not be
  used as file names, folder names or saved settings values.
* `file` is a plain file name next to `manifest.json` (no folders, no `..`).
* `nativeName` is shown in the settings selector. It is required.
* `englishName` is optional metadata for diagnostics and tests.
* `enabled: false` hides a language without deleting its file.
* `fallback` must be an enabled language. `en-US` exists by definition.

## 3. Catalog format

Each language file is a flat JSON object. Keys never change when a language is
added; only values are translated:

```json
{
  "Navigation_Home.Content": "Home",
  "Navigation_Home.ToolTip": "Open the home page",
  "Queue_ClearQueueButton.Content": "Clear queue",
  "Plural_SongCount_One": "{0} song",
  "Plural_SongCount_Other": "{0} songs",
  "tab_queue": "Queue",
  "playlist_tracks_one": "{count} track",
  "playlist_tracks_other": "{count} tracks"
}
```

Placeholders must be preserved exactly: `{0}`, `{1}`, `{count}`, `{name}`,
`{folder}`, `{0:dd.MM.yyyy}`. Order matters for validation.

### Key surfaces

One catalog feeds two surfaces. The generator routes by key shape:

* **Desktop key** — contains a property suffix (`Navigation_Home.Content`) or
  uppercase letters (`Plural_SongCount_One`). Goes to `.resw`.
* **Web key** — lowercase snake_case, no dot (`tab_queue`,
  `playlist_tracks_one`). Goes to `public/locales/<tag>.json`.

The suffix after the last dot selects the XAML property:

* `.Text` — `TextBlock.Text`, `SelectorBarItem.Text`, etc.;
* `.Content` — `Button.Content`, `NavigationViewItem.Content`, etc.;
* `.Header` / `.Description` — SettingsCard/SettingsExpander;
* `.PlaceholderText` — search boxes;
* `.OnContent` / `.OffContent` — toggles;
* `.AutomationName` / `.ToolTip` — accessibility and tooltips.

## 4. Generating platform files

```powershell
node scripts/localization/generate.mjs          # write artifacts
node scripts/localization/generate.mjs --check  # fail if artifacts are stale
```

The generated files are committed. Run the generator after every change to a
manifest or catalog; `--check` is suitable for CI. The desktop project copies the
whole `localization/` folder next to the executable, so JSON catalogs are also
available at runtime.

## 5. Desktop runtime

`LocalizationService`:

1. Loads `localization/manifest.json` and every enabled catalog.
2. Resolves the initial language in this order:
   saved choice → Windows UI language → language-group match (`ru` → `ru-RU`) →
   `en-US`.
3. Saves the resolved full tag to `settings.json` (including automatic choices).
4. Applies `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`
   so the initial `x:Uid` MRT render uses the right `.resw` when it exists.
5. Resolves every runtime string per key:
   current catalog → `en-US` catalog → compiled MRT resource → key.

`x:Uid` still drives the first visual-tree render from `.resw`. Attached
properties drive live language changes:

```xml
<Button
    x:Uid="Queue_ClearQueueButton"
    loc:Localization.Key="Queue_ClearQueueButton.Content"
    loc:Localization.ToolTipKey="Queue_ClearQueueButton.ToolTip"
    loc:Localization.AutomationNameKey="Queue_ClearQueueButton.AutomationName" />
```

The language selector is populated from `LocalizationService.Languages`, so its
items are `nativeName` values from the manifest. `en-US` remains the compiled
fallback for XAML startup; other languages can be added without rebuilding the
desktop app.

If a key is missing in both catalogs, the app returns the key, logs the fact and
never shows an empty string. In `DEBUG`, missing keys are also written to the
debug output.

## 6. Web remote

The web remote uses the same tags:

1. `localStorage['lang']` → `navigator.language` → manifest fallback (`en-US`).
2. The language selector and the admin language selector are built from
   `/locale-manifest.json`; there is no hardcoded `<option>` list.
3. `/locales/<tag>.json` is generated from the same source. The JS merges the
   English catalog first and then the selected catalog, which gives per-key
   fallback.
4. A failed catalog request leaves English (and the built-in fallback messages)
   in place.

The web remote does not follow the desktop language automatically; the user can
pick a different language in the header.

## 7. Adding a language (example: German)

1. Create `localization/de-DE.json` by copying `localization/en-US.json`.
2. Translate only the values; keep keys, placeholders and plural families.
3. Add the manifest entry:

```json
{
  "tag": "de-DE",
  "file": "de-DE.json",
  "nativeName": "Deutsch",
  "englishName": "German",
  "enabled": true
}
```

4. Run `node scripts/localization/generate.mjs`.
5. Run `dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj`.
6. Start the app (or reload the web remote). The language appears in the
   selector automatically.

No code changes are needed — not in `LocalizationService`, not in the settings
XAML, not in the web `#langSelect`.

## 8. Plurals

Catalog families:

```text
Plural_SongCount_One / _Few / _Many / _Other
Plural_VideoCount_*
Selection_Count_*

playlist_tracks_one / _few / _many / _other   (web)
```

* English: `One` and `Other` are required.
* Russian: `One`, `Few`, `Many`, `Other` are required.
* If a category is missing, the `Other` form of the same language is used, then
  the `en-US` value.
* Use `{0}` (desktop) / `{count}` (web) for the number.

## 9. What not to translate

* song titles, artists, file and folder names;
* playlist names and guest names;
* media paths;
* log messages and raw exception text;
* protocol/technical values (M3U, MKV, PIN, IP, MAC).

System labels **are** translated: Guest/Host/Computer, Online/Offline, counts,
date labels, buttons, tooltips and automation names.

## 10. Validation and tests

`LocalizationCatalogTests` checks:

* the manifest parses; fallback is valid and enabled; every language file exists;
* all tags are full BCP 47 tags; native names are not empty;
* `en-US` contains no Cyrillic values;
* missing keys fall back per key to English;
* placeholders match English for every translated key;
* Russian plural families contain all four categories; English has One/Other;
* language resolution: saved → system, short `ru` → `ru-RU`, unknown → `en-US`;
* adding a language only needs a manifest entry and a JSON file;
* generated `.resw` / web locale key sets match the source surfaces.

Useful commands:

```powershell
node scripts/localization/generate.mjs --check
dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj
```

## 11. Checklist for a localization change

- [ ] Keys are unchanged in every language file.
- [ ] Placeholders and date formats are identical to `en-US`.
- [ ] Plural categories are complete for the language.
- [ ] `en-US` has no Cyrillic values.
- [ ] Generated `.resw` and web locales were regenerated and committed.
- [ ] The language selector shows `nativeName` values from the manifest.
- [ ] `dotnet build` and `dotnet test` pass.
- [ ] Desktop and web were checked in the changed languages.

## 12. Migration note

Before the data-driven mode, desktop keys lived only in
`Strings/<tag>/Resources.resw` and web keys only in `public/locales/<tag>.json`.
They were imported once into `localization/<tag>.json` without changing any value
(531 desktop keys and 123 web keys at the time of migration). From now on the
catalogs under `localization/` are authoritative; the generated files are build
artifacts kept in version control for reproducibility.

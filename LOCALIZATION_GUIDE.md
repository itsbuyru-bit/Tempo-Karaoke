# Tempo Karaoke localization guide

## 1. Locale layout

Languages use **BCP 47** tags. The base and fallback language is `en-US`.

```text
src/
  TempoKaraoke/
    Strings/
      en-US/Resources.resw     ← base / fallback
      ru-RU/Resources.resw
    public/
      locales/
        en-US.json             ← web base / fallback
        ru-RU.json
```

Future languages follow the same pattern, for example:

```text
Strings/de-DE/Resources.resw
Strings/fr-FR/Resources.resw
Strings/es-ES/Resources.resw
Strings/zh-CN/Resources.resw

public/locales/de-DE.json
public/locales/fr-FR.json
public/locales/es-ES.json
public/locales/zh-CN.json
```

Do not use `de`, `fr`, `es` or `zh` as folder names for these files. Use the full BCP 47 tag (`de-DE`, `fr-FR`, `es-ES`, `zh-CN`). The app stores the same tag in `settings.json` and the web app stores it in `localStorage['lang']`.

## 2. Desktop resource keys

Desktop keys use semantic names:

```xml
<data name="Navigation_Home.Content" xml:space="preserve">
  <value>Home</value>
</data>
```

The suffix after the last dot is the target property:

- `.Text` — `TextBlock.Text`, `SelectorBarItem.Text`, `MenuFlyoutItem.Text`;
- `.Content` — `Button.Content`, `NavigationViewItem.Content`, `ComboBoxItem.Content`;
- `.Header` / `.Description` — SettingsCard/SettingsExpander properties;
- `.PlaceholderText` — search boxes;
- `.OnContent` / `.OffContent` — toggle switches.

XAML uses `x:Uid` for the initial MRT load and `Localization.Key` for live language changes:

```xml
<TextBlock
    x:Uid="Navigation_Home"
    loc:Localization.Key="Navigation_Home.Content" />
```

Do not put two unrelated controls under the same `x:Uid` base. MRT resolves every `Base.*` resource against the same element; a resource for a property the element does not have causes a startup `XamlParseException`.

## 3. Adding a desktop language

1. Copy `Strings/en-US/Resources.resw` to the new BCP 47 folder, for example `Strings/de-DE/Resources.resw`.
2. Keep every `data name` unchanged.
3. Translate only the `<value>` text.
4. Keep placeholders exactly as written: `{0}`, `{1}`, `{0:dd.MM.yyyy}`.
5. Add the language to `LocalizationService.NormalizeLanguage` and to the Settings language selector until the supported-language list becomes data-driven.
6. Check key parity:

```powershell
dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj
```

The parity test fails if a key is missing or if the English file contains Cyrillic.

## 4. Plural forms

Counts use plural resource families instead of concatenation.

Desktop:

```text
Plural_SongCount_One
Plural_SongCount_Few
Plural_SongCount_Many
Plural_SongCount_Other

Plural_VideoCount_One
Plural_VideoCount_Few
Plural_VideoCount_Many
Plural_VideoCount_Other

Selection_Count_One
Selection_Count_Few
Selection_Count_Many
Selection_Count_Other
```

English needs `One` and `Other`. Russian needs all four. Use `{0}` for the number:

```xml
<data name="Plural_VideoCount_Few" xml:space="preserve">
  <value>{0} видео</value>
</data>
```

The Russian selector follows the standard rules:

- `1, 21, 31…` except `11` → `One`;
- `2–4, 22–24…` except `12–14` → `Few`;
- everything else → `Many`.

Web JSON uses the same idea with `Intl.PluralRules`:

```json
{
  "playlist_tracks_one": "{count} track",
  "playlist_tracks_few": "{count} tracks",
  "playlist_tracks_many": "{count} tracks",
  "playlist_tracks_other": "{count} tracks"
}
```

## 5. Web translation

Static text:

```html
<span data-i18n="tab_queue">Queue</span>
<input data-i18n-placeholder="search_placeholder" placeholder="Song or artist">
```

Dynamic text:

```js
element.textContent = t('empty_queue');
button.textContent = t('btn_add_to_queue');
sub.textContent = tp('playlist_tracks', count);
```

To add a web language:

1. Copy `public/locales/en-US.json` to `public/locales/de-DE.json`.
2. Keep all JSON keys unchanged; translate only the values.
3. Keep placeholders `{name}`, `{count}`, `{folder}`.
4. Add the BCP 47 tag to the web supported-language list and to the `#langSelect` options.
5. Verify that every `data-i18n`, `t()` and `tp()` key exists in both files; the automated test enforces parity.

The web remote does not follow the desktop language: it detects `navigator.language` on the first visit and then honors the user's saved `localStorage['lang']`.

## 6. What not to translate

Do not translate user data:

- song titles, artists, file names and folder names;
- playlist names and guest names;
- media paths;
- log messages and raw exception details.

Do translate system labels that the app generates:

- `Guest`, `Host`, `Computer`;
- `Online` / `Offline`;
- queue counts, selected counts, date labels, button labels, tooltips and accessibility names.

## 7. Accessibility and tooltips

Tooltips and `AutomationProperties.Name` are localized with separate attached keys:

```xml
<Button
    x:Uid="Queue_ClearQueueButton"
    loc:Localization.ToolTipKey="Queue_ClearQueueButton.ToolTip"
    loc:Localization.AutomationNameKey="Queue_ClearQueueButton.AutomationName" />
```

Add both values to every new language file.

## 8. Review checklist

- [ ] Both `.resw` files have the same keys.
- [ ] English values contain no Cyrillic.
- [ ] Both JSON files have the same keys.
- [ ] Every `x:Uid` has matching resources for its properties.
- [ ] No Russian `Text`, `Content`, `Header` or `Description` remains in XAML.
- [ ] Plural strings use the correct categories.
- [ ] Placeholders and date formats are unchanged.
- [ ] Desktop and web were checked in both languages.
- [ ] `dotnet build` and `dotnet test` pass.

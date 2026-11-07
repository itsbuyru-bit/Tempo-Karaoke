<!--
  Translation pull-request template.
  Open a PR with: ?template=translation.md
  Use English for the main text; a Russian section is included at the bottom.
-->

# Translation contribution

## Language

- **BCP 47 tag** (for example `fr-FR`, `de-DE`, `pt-BR`):
- **Native name** (shown in the language selector):
- **English name**:
- **Catalog file** (`localization/<tag>.json`):
- **Completeness**: full / partial (English fallback is used for missing keys)

## What changed

<!-- New language or update to an existing one. Mention notable wording choices. -->

## Checklist

- [ ] I added or updated the entry in `localization/manifest.json`.
- [ ] I added the catalog as a flat JSON object: `"Key": "value"` strings only.
- [ ] Keys and placeholders (`{0}`, `{name}`, etc.) match `localization/en-US.json`.
- [ ] Plural forms are complete for this language, or the PR is marked partial.
- [ ] `node scripts/localization/validate.mjs` passes.
- [ ] `node scripts/localization/generate.mjs` was run and the generated
      `src/TempoKaraoke/Strings/<tag>/Resources.resw` and
      `src/TempoKaraoke/public/locales/<tag>.json` files are committed.
- [ ] `dotnet build TempoKaraoke.slnx -c Debug` and `dotnet test` pass.
- [ ] I checked the desktop UI and the web remote in this language.
- [ ] No personal data, secrets, logs or media files are included.

## Maintainer notes

The `localization` workflow validates JSON, manifest entries, placeholders and
generated artifacts on every pull request that touches localization files.
Generated files are committed; do not edit `.resw` or `public/locales/*.json` by
hand.

---

## Русский

## Язык

- **Тег BCP 47** (например `fr-FR`, `de-DE`, `pt-BR`):
- **Самоназвание** (в выборе языка):
- **Название по-английски**:
- **Файл каталога** (`localization/<tag>.json`):
- **Полнота**: полный / частичный (для пропущенных ключей используется английский fallback)

## Что изменено

<!-- Новый язык или правки существующего. Укажите важные особенности формулировок. -->

## Чек-лист

- [ ] Добавлена или обновлена запись в `localization/manifest.json`.
- [ ] Каталог — плоский JSON из строк: только `"Key": "value"`.
- [ ] Ключи и плейсхолдеры (`{0}`, `{name}` и т.п.) совпадают с `localization/en-US.json`.
- [ ] Формы множественного числа заполнены, либо PR помечен как частичный.
- [ ] `node scripts/localization/validate.mjs` проходит.
- [ ] Выполнен `node scripts/localization/generate.mjs`; сгенерированные
      `src/TempoKaraoke/Strings/<tag>/Resources.resw` и
      `src/TempoKaraoke/public/locales/<tag>.json` добавлены в коммит.
- [ ] `dotnet build TempoKaraoke.slnx -c Debug` и `dotnet test` проходят.
- [ ] Проверены desktop-интерфейс и веб-пульт на этом языке.
- [ ] В PR нет личных данных, секретов, логов и медиафайлов.

Русский | [English](../en/LOCALIZATION.md)
# Руководство по локализации Tempo Karaoke

## 1. Единый источник истины

Все строки интерфейса находятся в `localization/` в корне репозитория:

```text
localization/
  manifest.json      ← единственный список поддерживаемых языков
  en-US.json         ← базовый / fallback-каталог
  ru-RU.json
  de-DE.json         ← добавьте файл и одну запись в манифест
```

Файлы `.resw` для desktop и файлы локалей для веба **генерируются** из этих
каталогов и коммитятся для воспроизводимых сборок. Не редактируйте
сгенерированные файлы вручную:

```text
localization/<tag>.json
        ↓  node scripts/localization/generate.mjs
src/TempoKaraoke/Strings/<tag>/Resources.resw       (desktop / MRT)
src/TempoKaraoke/public/locales/<tag>.json          (веб-пульт)
src/TempoKaraoke/public/locale-manifest.json        (выбор языка в вебе)
```

`manifest.json` — единственное место, которое определяет, какие языки
существуют, как они называются и включены ли они.

## 2. Формат манифеста

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

Правила:

* `tag` — **полный тег BCP 47**: `en-US`, `ru-RU`, `de-DE`, `zh-CN`.
  Короткие теги (`de`, `fr`, `es`, `zh`) отклоняются валидацией; их нельзя
  использовать как имена файлов, папок или сохранённых значений настроек.
* `file` — простое имя файла рядом с `manifest.json` (без папок и `..`).
* `nativeName` показывается в селекторе настроек. Обязательное поле.
* `englishName` — необязательные метаданные для диагностики и тестов.
* `enabled: false` скрывает язык, не удаляя его файл.
* `fallback` должен быть включённым языком. `en-US` существует по определению.

## 3. Формат каталога

Каждый языковой файл — плоский JSON-объект. Ключи не меняются при добавлении
языка; переводятся только значения:

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

Плейсхолдеры должны сохраняться точно: `{0}`, `{1}`, `{count}`, `{name}`,
`{folder}`, `{0:dd.MM.yyyy}`. Порядок важен для валидации.

### Виды ключей

Один каталог обслуживает две поверхности. Генератор распределяет ключи по
форме:

* **Ключ desktop** — содержит суффикс свойства (`Navigation_Home.Content`) или
  заглавные буквы (`Plural_SongCount_One`). Попадает в `.resw`.
* **Ключ веба** — нижний snake_case без точки (`tab_queue`,
  `playlist_tracks_one`). Попадает в `public/locales/<tag>.json`.

Суффикс после последней точки выбирает свойство XAML:

* `.Text` — `TextBlock.Text`, `SelectorBarItem.Text` и т.п.;
* `.Content` — `Button.Content`, `NavigationViewItem.Content` и т.п.;
* `.Header` / `.Description` — SettingsCard/SettingsExpander;
* `.PlaceholderText` — поля поиска;
* `.OnContent` / `.OffContent` — переключатели;
* `.AutomationName` / `.ToolTip` — доступность и подсказки.

## 4. Генерация файлов платформы

```powershell
node scripts/localization/generate.mjs          # записать артефакты
node scripts/localization/generate.mjs --check  # упасть, если артефакты устарели
```

Сгенерированные файлы коммитятся. Запускайте генератор после каждого изменения
манифеста или каталога; `--check` подходит для CI. Desktop-проект копирует всю
папку `localization/` рядом с исполняемым файлом, поэтому JSON-каталоги
доступны и во время выполнения.

## 5. Desktop-среда выполнения

`LocalizationService`:

1. Загружает `localization/manifest.json` и каждый включённый каталог.
2. Определяет исходный язык в таком порядке:
   сохранённый выбор → язык интерфейса Windows → совпадение языковой группы
   (`ru` → `ru-RU`) → `en-US`.
3. Сохраняет разрешённый полный тег в `settings.json` (включая автоматический
   выбор).
4. Применяет `Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`,
   чтобы первый рендер `x:Uid` из MRT использовал правильный `.resw`, если он
   существует.
5. Разрешает каждую runtime-строку по ключу:
   текущий каталог → каталог `en-US` → скомпилированный ресурс MRT → ключ.

`x:Uid` по-прежнему управляет первым рендером визуального дерева из `.resw`.
Присоединённые свойства обеспечивают смену языка на лету:

```xml
<Button
    x:Uid="Queue_ClearQueueButton"
    loc:Localization.Key="Queue_ClearQueueButton.Content"
    loc:Localization.ToolTipKey="Queue_ClearQueueButton.ToolTip"
    loc:Localization.AutomationNameKey="Queue_ClearQueueButton.AutomationName" />
```

Селектор языка заполняется из `LocalizationService.Languages`, поэтому его
элементы — значения `nativeName` из манифеста. `en-US` остаётся
скомпилированным fallback для запуска XAML; другие языки можно добавлять без
пересборки desktop-приложения.

Если ключ отсутствует в обоих каталогах, приложение возвращает ключ, пишет об
этом в лог и никогда не показывает пустую строку. В `DEBUG` отсутствующие ключи
также выводятся в отладочный вывод.

## 6. Веб-пульт

Веб-пульт использует те же теги:

1. `localStorage['lang']` → `navigator.language` → fallback манифеста (`en-US`).
2. Селектор языка и селектор языка администратора строятся из
   `/locale-manifest.json`; жёстко заданного списка `<option>` нет.
3. `/locales/<tag>.json` генерируется из того же источника. JS сначала
   объединяет английский каталог, затем выбранный — так получается fallback по
   каждому ключу.
4. Если запрос каталога не удался, остаются английский язык и встроенные
   fallback-сообщения.

Веб-пульт не следует за языком desktop автоматически; пользователь может
выбрать другой язык в шапке.

## 7. Добавление языка (пример: немецкий)

1. Создайте `localization/de-DE.json`, скопировав `localization/en-US.json`.
2. Переведите только значения; сохраните ключи, плейсхолдеры и семейства
   множественного числа.
3. Добавьте запись в манифест:

```json
{
  "tag": "de-DE",
  "file": "de-DE.json",
  "nativeName": "Deutsch",
  "englishName": "German",
  "enabled": true
}
```

4. Запустите `node scripts/localization/generate.mjs`.
5. Запустите `dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj`.
6. Запустите приложение (или перезагрузите веб-пульт). Язык появится в селекторе
   автоматически.

Изменения кода не нужны — ни в `LocalizationService`, ни в XAML настроек, ни в
`#langSelect` веба.

## 8. Множественное число

Семейства каталога:

```text
Plural_SongCount_One / _Few / _Many / _Other
Plural_VideoCount_*
Selection_Count_*

playlist_tracks_one / _few / _many / _other   (веб)
```

* Английский: обязательны `One` и `Other`.
* Русский: обязательны `One`, `Few`, `Many`, `Other`.
* Если категория отсутствует, используется форма `Other` того же языка, затем
  значение из `en-US`.
* Для числа используйте `{0}` (desktop) / `{count}` (веб).

## 9. Что не переводить

* названия песен, исполнителей, файлов и папок;
* названия плейлистов и имена гостей;
* пути к медиа;
* сообщения логов и сырой текст исключений;
* протокольные и технические значения (M3U, MKV, PIN, IP, MAC).

Системные подписи **переводятся**: Guest/Host/Computer, Online/Offline,
счётчики, подписи дат, кнопки, подсказки и automation names.

## 10. Валидация и тесты

`LocalizationCatalogTests` проверяет:

* манифест разбирается; fallback валиден и включён; каждый языковой файл
  существует;
* все теги — полные BCP 47; native names не пусты;
* в `en-US` нет кириллических значений;
* отсутствующие ключи откатываются к английскому по каждому ключу;
* плейсхолдеры совпадают с английскими для каждого переведённого ключа;
* русские семейства множественного числа содержат все четыре категории, а
  английские — `One`/`Other`;
* разрешение языка: сохранённый → системный, короткий `ru` → `ru-RU`,
  неизвестный → `en-US`;
* для нового языка достаточно записи в манифесте и JSON-файла;
* наборы ключей сгенерированных `.resw` / веб-локалей соответствуют исходным
  поверхностям.

Полезные команды:

```powershell
node scripts/localization/generate.mjs --check
dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj
```

## 11. Чек-лист изменения локализации

- [ ] Ключи не изменены ни в одном языковом файле.
- [ ] Плейсхолдеры и форматы дат совпадают с `en-US`.
- [ ] Категории множественного числа заполнены для языка.
- [ ] В `en-US` нет кириллических значений.
- [ ] Сгенерированные `.resw` и веб-локали перегенерированы и закоммичены.
- [ ] Селектор языка показывает значения `nativeName` из манифеста.
- [ ] `dotnet build` и `dotnet test` проходят.
- [ ] Desktop и веб проверены на изменённых языках.

## 12. Примечание о миграции

До перехода на data-driven режим ключи desktop хранились только в
`Strings/<tag>/Resources.resw`, а ключи веба — только в
`public/locales/<tag>.json`. Их один раз импортировали в
`localization/<tag>.json`, не меняя значений (531 ключ desktop и 123 ключа веба
на момент миграции). Теперь каталоги в `localization/` — источник истины;
сгенерированные файлы остаются артефактами сборки в системе контроля версий для
воспроизводимости.

## 13. Подхват перевода после перезапуска без пересборки

Desktop-приложение читает `localization/manifest.json` и плоские каталоги из
папки `localization`, установленной рядом с `TempoKaraoke.exe` (Inno Setup
кладёт её туда вместе с приложением). Чтобы добавить или поправить язык без
пересборки:

1. Отредактируйте `manifest.json` в установленной папке `localization`.
2. Положите рядом нужный каталог `<tag>.json`.
3. Перезапустите Tempo Karaoke.

Список языков в настройках строится из манифеста при старте, а runtime-строки
берутся из JSON-каталога. Веб-пульт также читает установленную папку
`localization`, поэтому видит то же изменение. `node scripts/localization/generate.mjs`
нужен при подготовке артефактов для новой сборки (`.resw` и fallback-файлы в
`public/`), но не для изменения языка в уже установленном приложении.

## 14. Pull request с переводом

Используйте `.github/PULL_REQUEST_TEMPLATE/translation.md` (добавьте
`?template=translation.md` к ссылке сравнения) и укажите тег, самоназвание,
файл каталога и полноту перевода.

Workflow `localization` проверяет `manifest.json`, синтаксис JSON, теги BCP 47,
плейсхолдеры, непустые значения и сгенерированные артефакты `.resw`/`public`:

```powershell
node scripts/localization/validate.mjs
node scripts/localization/generate.mjs --check
```

Сгенерированные файлы коммитятся, поэтому PR с переводом должен содержать
обновлённые `src/TempoKaraoke/Strings/<tag>/Resources.resw` и
`src/TempoKaraoke/public/locales/<tag>.json`.

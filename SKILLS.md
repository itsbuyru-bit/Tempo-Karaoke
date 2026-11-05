# Скиллы проекта

Все скиллы лежат в `.reasonix/skills/<name>/SKILL.md` и подхватываются Reasonix как
**project scope** (высший приоритет для этого workspace). Вызов — `/имя` или
автоматически по описанию.

## Что установлено (27)

### Microsoft WinUI 3 — 7 скиллов

Источник: [microsoft/winappCli](https://github.com/microsoft/winappCli),
`plugins/winui/agent-plugin/skills`, коммит `31fc2ac` (2026-10-28), MIT.

| Скилл | Когда использовать |
|---|---|
| `winui-design` | Перед написанием любого XAML: выбор контролов, Fluent, темы, доступность, `x:Bind`, типичные ошибки |
| `winui-code-review` | Ревью WinUI-кода: MVVM, биндинги, темы, a11y, безопасность, производительность |
| `winui-dev-workflow` | Создание, сборка, запуск и починка WinUI 3 приложения (`winapp new/run`) |
| `winui-packaging` | MSIX, подпись, Native AOT, self-contained, CI/CD |
| `winui-setup` | Установка и проверка .NET SDK, WinApp CLI, Developer Mode |
| `winui-ui-testing` | UI-тесты через `winapp ui` (UIA), в том числе в Windows Sandbox |
| `winui-wpf-migration` | Если позже понадобится переезд с WPF на WinUI 3 |

### Microsoft WinApp CLI — 14 скиллов

Источник тот же, `plugins/winapp/skills`, MIT. Требуют `winapp` CLI 0.7+
(на машине установлен **0.7.1**).

| Скилл | Когда использовать |
|---|---|
| `winapp-setup` | Подключить Windows SDK / Windows App SDK к проекту, `winapp init/restore` |
| `winapp-package` | Собрать MSIX из build output |
| `winapp-signing` | Подписать `.msix`/`.exe`, сертификаты, Azure Trusted Signing |
| `winapp-manifest` | `Package.appxmanifest`: ассоциации файлов, протоколы, capabilities |
| `winapp-identity` | Package identity для toast/background/share target |
| `winapp-find-api` | Проверить реальный Windows/WinRT API вместо догадок |
| `winapp-find-ui` | Найти подходящий WinUI-контрол и компилируемый пример |
| `winapp-frameworks` | Windows-специфика для Electron, WPF, WinForms, C++, Rust, Flutter, Tauri |
| `winapp-ui-automation` | Драйвить и проверять запущенное приложение через UIA |
| `winui-devtools` | Смотреть живые значения XAML, биндинги, править на лету |
| `winapp-performance` | ETW-профилирование WinUI (layout, scrolling, virtualization) |
| `winapp-sandbox` | Запуск и отладка в Windows Sandbox |
| `winapp-maui` | Только если выберете .NET MAUI |
| `winapp-troubleshoot` | Ошибки упаковки, подписи, установки, identity |

### Microsoft WinUI Gallery — 2 скилла

Источник: [microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery),
коммит `7360ac322414e118eadfb0c7c2f4ecc31a218f59` (2026-10-27), MIT.
Текст скиллов синтезирован под Tempo Karaoke, а не скопирован из Gallery.

| Скилл | Когда использовать |
|---|---|
| `winui-gallery-gost` | Нормативный ГОСТ оформления: навигация, типографика, отступы, карточки, темы, a11y, приёмка. Перед любой страницей или ревью XAML |
| `winui-gallery-patterns` | Готовые рецепты: shell, заголовок с вкладками, тулбар, сетка, настройки, очередь, свойства, медиа-панель |

### Microsoft libraries — 2 скилла

| Скилл | Когда использовать | Источник / лицензия |
|---|---|---|
| `win2d` | Кастомная GPU-2D-графика в WinUI 3: waveform, анимация текста, частицы, визуализаторы. Не для обычного видео | [Microsoft/Win2D](https://github.com/Microsoft/Win2D), MIT |
| `cswin32` | Win32 P/Invoke и COM, когда WinRT/Windows App SDK API не хватает: мониторы/DPI, hotkeys, window styles | [Microsoft/CsWin32](https://github.com/Microsoft/CsWin32), MIT |

### Проектные — 2 скилла

| Скилл | Назначение |
|---|---|
| `grill-me` | Допрос требований до кода: по одному вопросу через `ask`, дерево решений, критерии приёмки. Адаптирован под Reasonix и под этот проект |
| `chrome-devtools-mcp` | Отладка мобильного пульта через Chrome DevTools MCP: консоль, сеть, эмуляция телефона, скриншоты; конфиг в `.mcp.json`, версия MCP-сервера — `latest` (на 2026-11-05 это 1.12.0) |

`grill-me` — не копия чужого текста. Идея взята из публичного
[RobMitt/grill-me-skill](https://github.com/RobMitt/grill-me-skill) (≈1,2k звёзд,
файла лицензии в репозитории нет), текст написан заново под инструмент `ask`.

## Лицензии и происхождение

- `winui-*`, `winapp-*` — MIT, Microsoft. Текст лицензии:
  `.reasonix/skills/.licenses/winappCli-LICENSE.txt`.
- `winui-gallery-*` — MIT, Microsoft. Текст лицензии:
  `.reasonix/skills/.licenses/WinUI-Gallery-LICENSE.txt`. Скиллы — внутренняя
  нормативная надстройка проекта, не официальный стандарт Microsoft.
- `win2d` — MIT, Microsoft. Текст лицензии:
  `.reasonix/skills/.licenses/Win2D-LICENSE.txt`.
- `cswin32` — MIT, Microsoft. Текст лицензии:
  `.reasonix/skills/.licenses/CsWin32-LICENSE.txt`.
- `grill-me` — оригинальный текст этого проекта.
- `chrome-devtools-mcp` — проектный скилл; MCP-сервер `chrome-devtools-mcp@latest`
  (на 2026-11-05 — 1.12.0).
- Каталог [microsoft/win-dev-skills](https://github.com/microsoft/win-dev-skills)
  (коммит `5ce74fa`, 2026-10-07) сам содержит только плагин `windbg`; `winui` и
  `winappcli` он подключает из `microsoft/winappCli` по пину `v0.7.1`.

## Проверено, но не добавлено

- **Microsoft Learn: design principles / guidelines / accessibility** — уже
  покрыто `winui-design`, `winui-code-review`, `winapp-ui-automation` и
  `winui-gallery-gost` (в ГОСТ добавлен раздел 0 с принципами Windows 11 и
  ссылками). Отдельный дублирующий скилл не нужен.
- **`ub-mode` (Robert C. Martin) и его инструменты `crapper` / `dryer` /
  `mutator` / `uml-viewer`** — оценены 2026-11-04 и не взяты. Скилл — чистый
  чек-лист без кода (MIT, веттинг 🟢), но его гейты опираются на Python-утилиты,
  которые не понимают C#: поддержаны Clojure, Java, Go, TypeScript, Rust,
  Python. «Commit often / push в конце» противоречит `AGENTS.md`, а
  `uml-viewer` — Clojure/JVM-GUI без файла лицензии. Полезные правила
  (`Subtract Before You Add`, `Test Behavior`, `Boundary Discipline`,
  `Guard the Context Window`) частично пересекаются с `grill-me`,
  `winui-design` и `winui-code-review`; отдельный скилл не нужен. Для CRAP /
  дублирования / мутаций в C# есть свои средства (CA1502 + coverlet,
  jscpd/CPD, Stryker.NET) — вернуться, если понадобится гейт качества.
- **ColorCode-Universal** — подсветка синтаксиса в UWP `RichTextBlock`; в
  караоке-плеере нет редактора кода, а лицензия репозитория — `NOASSERTION`.
  Не добавляю; вернуться, если появится просмотр кода/сниппетов.

## Что намеренно не установлено

- **`windbg-*` (11 скиллов)** — триаж kernel/user-mode дампов, драйверы, TTD. Для
  караоке-плеера это шум. Добавить при необходимости:
  ```bash
  cp -a /tmp/win-dev-skills/plugins/windbg/skills/* .reasonix/skills/
  ```
- **311 скиллов ECC целиком** — раздуют индекс и будут срабатывать не туда.
  Ниже — точечный список.

## Что ещё стоит добавить из ECC

Источник: [affaan-m/ecc](https://github.com/affaan-m/ecc), v2.2.3, коммит
`25278e7` (2026-10-26), MIT, 311 скиллов.

| Скилл | Зачем проекту | Приоритет |
|---|---|---|
| [`windows-desktop-e2e`](https://github.com/affaan-m/ecc/tree/main/skills/windows-desktop-e2e) | E2E-тесты Windows-приложения через pywinauto + UIA; дополняет `winui-ui-testing` | высокий |
| [`dotnet-patterns`](https://github.com/affaan-m/ecc/tree/main/skills/dotnet-patterns) | Идиоматичный C#/.NET: DI, async/await, жизненный цикл | высокий |
| [`csharp-testing`](https://github.com/affaan-m/ecc/tree/main/skills/csharp-testing) | xUnit, FluentAssertions, интеграционные тесты | высокий |
| [`intent-driven-development`](https://github.com/affaan-m/ecc/tree/main/skills/intent-driven-development) | Превращает размытую фичу в проверяемые критерии приёмки; пара к `grill-me` | высокий |
| [`architecture-decision-records`](https://github.com/affaan-m/ecc/tree/main/skills/architecture-decision-records) | Зафиксировать выбор WinUI vs WPF, libvlc vs mpv в `docs/adr/` | средний |
| [`coding-standards`](https://github.com/affaan-m/ecc/tree/main/skills/coding-standards) | Базовые соглашения по именованию и читаемости | средний |
| [`verification-loop`](https://github.com/affaan-m/ecc/tree/main/skills/verification-loop) | Прогон build → typecheck → lint → tests → diff перед сдачей | средний |
| [`documentation-lookup`](https://github.com/affaan-m/ecc/tree/main/skills/documentation-lookup) | Свежие доки WinUI/Windows App SDK через Context7 вместо памяти модели | средний |
| [`blueprint`](https://github.com/affaan-m/ecc/tree/main/skills/blueprint) | Пошаговый план на несколько сессий, если проект разрастётся | низкий |
| [`git-workflow`](https://github.com/affaan-m/ecc/tree/main/skills/git-workflow) | Ветки, conventional commits, чистая история | низкий |
| [`benchmark`](https://github.com/affaan-m/ecc/tree/main/skills/benchmark) | Замеры холодного старта и поиска, защита от регрессий | низкий |
| [`skill-scout`](https://github.com/affaan-m/ecc/tree/main/skills/skill-scout) | Искать готовый скилл, прежде чем писать свой | низкий |

Установка любого из них:

```bash
git clone --depth 1 https://github.com/affaan-m/ecc.git "$TMPDIR/ecc"
cp -a "$TMPDIR/ecc/skills/<name>" .reasonix/skills/
```

После копирования проверить, что в `SKILL.md` есть frontmatter с `name` и
`description`, и что имя совпадает с папкой.

## Варианты скиллов на стандарты дизайна

| Вариант | Что даёт | Для какого стека |
|---|---|---|
| **`winui-design`** (уже стоит) | Fluent-контролы, темы, типографика, отступы, a11y, антипаттерны XAML | WinUI 3 |
| **`winui-code-review`** (уже стоит) | Ревью вёрстки и тем, доступность, глобализация | WinUI 3 |
| [`design-system`](https://github.com/affaan-m/ecc/tree/main/skills/design-system) | Токены (цвета, шрифты, отступы, тени) в `design-tokens.json` + `DESIGN.md`, аудит по 10 измерениям, поиск «AI-slop» | Web (Tauri/Electron) или как методика |
| [`make-interfaces-feel-better`](https://github.com/affaan-m/ecc/tree/main/skills/make-interfaces-feel-better) | Полировка: отступы, типографика, границы, тени, motion, hit areas, состояния | Любой UI, концептуально |
| [`frontend-design-direction`](https://github.com/affaan-m/ecc/tree/main/skills/frontend-design-direction) | Направление дизайна под продукт, а не «шаблонный дашборд» | Web |
| [`accessibility`](https://github.com/affaan-m/ecc/tree/main/skills/accessibility) | WCAG 2.2 AA: фокус, контраст, target size, screen reader | Web/iOS/Android |
| [`frontend-a11y`](https://github.com/affaan-m/ecc/tree/main/skills/frontend-a11y) | ARIA, формы, модалки, клавиатура | React/Next |
| [`liquid-glass-design`](https://github.com/affaan-m/ecc/tree/main/skills/liquid-glass-design) | Стеклянный стиль Apple | iOS/web, не для Fluent |

Если нужен **один** выбор: для WinUI 3 берите `winui-design` + `winui-code-review`
(они уже установлены и заточены под Fluent). Для web-варианта — `design-system`.
Можно также завести собственный `fluent-standards` с чек-листом проекта: сетка
4/8 px, размеры шрифтов, контраст, фокус, состояния, пустые экраны, горячие
клавиши — скажите, и я его напишу.

## Как пользоваться

- `/grill-me` — перед новой фичей: допрос требований и критерии приёмки.
- `/winui-design` — перед XAML.
- `/winui-code-review` — перед коммитом UI-кода.
- `/winapp-setup` — если `winapp`/SDK не находятся.
- `/winui-ui-testing` — после сборки, для проверки сценария целиком.

## Важно про git

`AppPlayList_Karaoke` — отдельный git-репозиторий в `D:\Laba\AppPlayList_Karaoke`.
Родительский `D:\Laba\ReasonixLocal_RU` больше не используется. Скиллы в
`.reasonix/skills` входят в этот репозиторий.

Русский | [English](CONTRIBUTING.md)

# Участие в разработке Tempo Karaoke

Спасибо за помощь! Tempo Karaoke — нативный караоке-плеер для Windows на WinUI 3,
Windows App SDK, LibVLCSharp и SQLite.

## Перед началом

- Новую функцию или архитектурное изменение сначала обсудите в issue.
- Медиатека доступна только на чтение: не переименовывайте, не перемещайте и не
  удаляйте исходные медиафайлы.
- Индексируются только видеофайлы. Аудио вроде `.mp3` исключено.
- Пользовательские строки должны быть локализованы на русском и английском.
- Не коммитьте медиа, результаты сборки, логи, сертификаты и локальные
  конфигурации агентов.

## Сборка и тесты

Требования: Windows 10 1809+, .NET SDK 10, Visual Studio с компонентами
Windows App SDK или WinApp CLI 0.7.1.

```powershell
dotnet build TempoKaraoke.slnx -c Debug
dotnet test tests/TempoKaraoke.Tests/TempoKaraoke.Tests.csproj
node scripts/localization/generate.mjs --check
```

Запуск приложения:

```powershell
winapp run src/TempoKaraoke --arch x64
```

Сборка установщика (Inno Setup 7 или 6.3+):

```powershell
.\scripts\build-installer.ps1
```

## Стиль кода

- C#: file-scoped namespace, приватные поля `_camelCase`, nullable включён.
- XAML: предпочитайте `x:Bind` с явным `Mode=OneWay` для изменяемых значений.
- Текст интерфейса — в `localization/<tag>.json`; затем перегенерируйте `.resw`.
- Ошибки пишите через `LogService`; не логируйте PIN, токены и приватные пути.
- Для пользовательских изменений обновляйте `CHANGELOG.md` и `CHANGELOG.ru.md`.

## Язык документации

Основной язык репозитория — английский. У каждого продуктового документа есть
русская версия:

| Английский | Русский |
|---|---|
| `README.md` | `README.ru.md` |
| `CHANGELOG.md` | `CHANGELOG.ru.md` |
| `PRIVACY.md` | `PRIVACY.ru.md` |
| `docs/en/*` | `docs/ru/*` |

При изменении документа обновляйте обе версии и сохраняйте строку переключения
языка в начале файла.

## Процесс релиза

Релизы выпускаются из `main`:

1. Поднимите `<Version>` в `src/TempoKaraoke/TempoKaraoke.csproj` и
   `Identity/@Version` в `Package.appxmanifest`.
2. Добавьте `docs/releases/vX.Y.Z.W.md` с пользовательскими заметками.
3. Добавьте версию в `CHANGELOG.md` и `CHANGELOG.ru.md`.
4. Закоммитьте, поставьте тег `vX.Y.Z.W` и отправьте его. Workflow релиза соберёт
   портативный установщик и опубликует его в GitHub Releases.

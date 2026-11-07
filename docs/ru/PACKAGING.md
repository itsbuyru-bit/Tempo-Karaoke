Русский | [English](../en/PACKAGING.md)

# Упаковка и установка

Tempo Karaoke поставляется как портативная Windows-сборка, упакованная в
установщик **Inno Setup**. MSIX/AppX и Microsoft Store не используются.

## Источник версии

- Версия приложения: `<Version>` в `src/TempoKaraoke/TempoKaraoke.csproj`.
  Установщик по умолчанию читает версию из собранного `TempoKaraoke.exe`.
- Скрипт установщика: `installer/TempoKaraoke.iss`.
- Профиль публикации: `src/TempoKaraoke/Properties/PublishProfiles/portable-x64.pubxml`.
- Результат: `artifacts\TempoKaraoke-Setup-<версия>.exe`.

## Сборка

Нужны .NET SDK 10 и Inno Setup 6+ (`ISCC.exe`).

```powershell
dotnet publish src/TempoKaraoke/TempoKaraoke.csproj `
  -c Release -p:PublishProfile=portable-x64

iscc installer\TempoKaraoke.iss
```

Или скрипт, который публикует portable-сборку и сразу компилирует установщик:

```powershell
.\scripts\build-installer.ps1
```

Если `ISCC.exe` не в `PATH`, укажите путь к компилятору.
`/DMyAppVersion=1.2.3.4` переопределяет версию из exe.

## Что делает установщик

- По умолчанию ставит приложение в профиль пользователя (без UAC); есть режим
  установки для всех пользователей.
- Позволяет выбрать папку установки.
- Создаёт ярлыки в меню «Пуск» и, по желанию, на рабочем столе с постоянным
  AppUserModelID.
- Сохраняет `%LocalAppData%\TempoKaraoke` (настройки, очередь, плейлисты, кэш,
  логи), если при удалении явно не выбрать очистку данных.
- Закрывает запущенные процессы Tempo Karaoke перед заменой файлов.
- Перезапускает приложение после тихого обновления.

## Обновления

Приложение проверяет GitHub Releases на `TempoKaraoke-Setup-<версия>.exe`,
скачивает установщик и после подтверждения запускает его в тихом режиме.
Установщик закрывает приложение, заменяет файлы и запускает новую версию.

## Локализация после установки

Папка `localization\` устанавливается рядом с `TempoKaraoke.exe`. Достаточно
отредактировать `manifest.json` и добавить `<tag>.json` в эту папку — после
перезапуска desktop-приложение подхватит язык без пересборки. Генератор
`node scripts/localization/generate.mjs` нужен только для обновления артефактов
репозитория (`.resw` и fallback-файлов в `public/`).

## Диагностика

- Проверьте наличие `ISCC.exe` и папки публикации.
- При ошибке установки смотрите лог Inno Setup в `%TEMP%`.
- Если приложение не запускается — `%LocalAppData%\TempoKaraoke\logs\`.
- Если язык не появился, проверьте папку `localization` рядом с exe и
  перезапустите приложение.

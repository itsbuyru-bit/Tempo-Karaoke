# Политика конфиденциальности Tempo Karaoke

Дата вступления в силу: 5 ноября 2026 г.
Разработчик: Cappuccino Dev (далее — «Разработчик»).

## Кратко

Tempo Karaoke **не собирает, не передаёт и не продаёт персональные данные**.
Всё, что приложение сохраняет, остаётся на вашем компьютере. У приложения нет
аккаунтов, рекламы, аналитики и телеметрии.

## Какие данные обрабатываются

Приложение работает только с данными, которые вы указали сами:

- пути к папкам с видео и кэш метаданных (`media.db`) — чтобы не сканировать
  медиатеку заново при каждом запуске;
- настройки (`settings.json`): тема, язык, пути к медиатеке, параметры пульта;
- очередь (`queue.json`), плейлисты (`playlists.json`), история и избранное;
- имена устройств, подключавшихся к веб-пульту (`remote-clients.json`).

Все эти данные хранятся локально: `%LocalAppData%\TempoKaraoke` в портативной
версии или в `LocalState` пакета MSIX. Разработчик к ним доступа не имеет.

Приложение читает видеофайлы только в папках, которые вы добавили в настройках.
Оно не копирует и не изменяет ваши файлы.

## Сетевые подключения

- **Веб-пульт.** Встроенный HTTP-сервер работает в вашей локальной сети или через
  Wi-Fi Direct и включается только по вашей команде. Страница пульта и данные
  очереди передаются только между вашим компьютером и вашими устройствами в этой
  сети; через интернет они не проходят.
- **Проверка обновлений.** Если в настройках включена «Проверка обновлений при
  запуске», приложение отправляет запрос к GitHub (`api.github.com`) за
  информацией о последнем релизе. В запросе нет идентификаторов, файлов,
  истории или содержимого медиатеки — только стандартный HTTP-запрос с
  User-Agent, содержащим название и версию приложения. GitHub обрабатывает
  такие запросы согласно собственной политике конфиденциальности.
  Проверку можно отключить в настройках.
- Других сетевых подключений приложение не выполняет.

## Передача данных третьим лицам

Разработчик не передаёт данные третьим лицам: их попросту нет на стороне
разработчика. Единственное внешнее обращение — проверка обновлений через GitHub,
описанная выше.

## Хранение и удаление

Данные хранятся на вашем устройстве до тех пор, пока вы их не удалите:

- удаление приложения (установщик или «Параметры → Приложения») убирает файлы
  программы;
- пользовательские данные удаляются вместе с папкой `%LocalAppData%\TempoKaraoke`
  (портативная версия) или сбросом данных приложения в параметрах Windows
  (версия MSIX).

Резервных копий на стороне Разработчика не существует.

## Дети

Приложение не собирает данные пользователей, в том числе детей, и не содержит
рекламы или покупок.

## Изменения политики

Актуальная версия всегда находится в этом файле в репозитории
<https://github.com/itsbuyru-bit/Tempo-Karaoke>. При существенных изменениях
обновляется дата вступления в силу.

## Контакты

Вопросы по обработке данных: GitHub Issues —
<https://github.com/itsbuyru-bit/Tempo-Karaoke/issues>.

---

# Tempo Karaoke Privacy Policy

Effective date: November 5, 2026.
Developer: Cappuccino Dev (the "Developer").

## Summary

Tempo Karaoke **does not collect, transmit, or sell personal data**. Everything the
app stores stays on your computer. There are no accounts, ads, analytics, or
telemetry.

## Data the app processes

The app only works with data you provide:

- paths to your video folders and a metadata cache (`media.db`) so the library is
  not rescanned on every launch;
- settings (`settings.json`): theme, language, library paths, remote options;
- playback queue (`queue.json`), playlists (`playlists.json`), history, favorites;
- names of devices that connected to the web remote (`remote-clients.json`).

All of it is stored locally: `%LocalAppData%\TempoKaraoke` for the portable build or
the package's `LocalState` for the MSIX build. The Developer has no access to it.

The app reads video files only from folders you add in settings. It does not copy
or modify your files.

## Network connections

- **Web remote.** The built-in HTTP server runs on your local network or over
  Wi-Fi Direct and starts only when you ask it to. The remote page and queue data
  travel only between your computer and your devices on that network; they do not
  go through the internet.
- **Update check.** If "Check for updates at startup" is enabled, the app requests
  the latest release information from GitHub (`api.github.com`). The request
  contains no identifiers, files, history, or library content — only a standard
  HTTP request whose User-Agent carries the app name and version. GitHub processes
  such requests under its own privacy policy. The check can be disabled in
  settings.
- The app makes no other network connections.

## Third parties

The Developer does not share data with third parties because no data reaches the
Developer. The only external request is the GitHub update check described above.

## Retention and deletion

Data stays on your device until you remove it:

- uninstalling the app (installer or Settings → Apps) removes the program files;
- user data is removed with the `%LocalAppData%\TempoKaraoke` folder (portable
  build) or by resetting the app's data in Windows settings (MSIX build).

The Developer keeps no backups.

## Children

The app collects no user data, including data from children, and contains no ads
or purchases.

## Changes

The current version always lives in this file in the repository
<https://github.com/itsbuyru-bit/Tempo-Karaoke>. Material changes update the
effective date.

## Contact

Data questions: GitHub Issues —
<https://github.com/itsbuyru-bit/Tempo-Karaoke/issues>.

# Сборка, установка и обновление

В проекте две схемы поставки:

| Схема | Когда нужна | Раздел |
|---|---|---|
| **Портативная сборка + Inno Setup** | основная раздача: установка без админа, самообновление из приложения | [ниже](#портативная-сборка-и-установщик-inno) |
| **MSIX через WinApp CLI** | Microsoft Store, корпоративная раздача с управляемым сертификатом | [MSIX](#сборка-msix) |

Документ проверен 2026-11-05 на этом репозитории.

# Портативная сборка и установщик Inno

Схема повторяет UniGetUI: приложение ставится распакованными файлами в профиль
пользователя (`Inno Setup`, `PrivilegesRequired=lowest`), а обновление скачивает
и запускает тот же установщик — без UAC и без доверия к сертификату.

## Сборка

```powershell
# Публикация + установщик одной командой (Inno Setup 6/7 должен быть установлен)
.\scripts\build-installer.ps1

# То же по шагам
dotnet publish src\TempoKaraoke\TempoKaraoke.csproj -c Release -p:PublishProfile=portable-x64
"D:\Program Files\Inno Setup 7\ISCC.exe" installer\TempoKaraoke.iss /DMyAppVersion=1.8.21.0
```

Результат: `artifacts\TempoKaraoke-Setup-<версия>.exe` (~110 МБ).

Профиль `portable-x64.pubxml` задаёт `WindowsPackageType=None` и **полностью
self-contained** сборку: рядом с exe лежат и .NET, и Windows App SDK
(`Microsoft.WindowsAppRuntime.dll`). Поэтому на целевой машине не нужно ничего
доустанавливать, а установщик не проверяет рантаймы и не просит админ.

Версию держат в двух местах, они должны совпадать:

- `src/TempoKaraoke/TempoKaraoke.csproj` — `<Version>` (её читает приложение
  через `AppInfo` и её передают в `iscc` через `/DMyAppVersion`);
- `src/TempoKaraoke/Package.appxmanifest` — `Identity/@Version` (только для MSIX).

## Что делает установщик

- показывает страницу выбора папки; по умолчанию `{localappdata}\Programs\Tempo Karaoke`
  (без UAC), но в диалоге можно выбрать установку «для всех пользователей» —
  тогда путь в Program Files и потребуется UAC;
- закрывает запущенное приложение и его helper-процессы (`taskkill`);
- создаёт ярлыки (с AUMID для уведомлений) и деинсталлятор, после установки
  запускает приложение (в том числе при тихом обновлении);
- при удалении спрашивает, стирать ли настройки, очередь и кэш
  (`%LocalAppData%\TempoKaraoke`). По умолчанию данные сохраняются; тихая
  деинсталляция (`/VERYSILENT`) данные не трогает.

## Скрипты

| Скрипт | Что делает |
|---|---|
| `scripts\build-installer.ps1` | publish + Inno Setup → `artifacts\TempoKaraoke-Setup-<версия>.exe` |
| `scripts\make-cert.ps1` | самоподписанный сертификат для MSIX: `certs\TempoKaraoke.pfx` (с ключом) и `certs\TempoKaraoke.cer` (раздавать) |
| `scripts\build-msix.ps1 -Mode sideload` | подписанный MSIX для установки на ПК |
| `scripts\build-msix.ps1 -Mode store` | MSIX без подписи для Partner Center |
| `scripts\install-msix.ps1` / `.cmd` | ставит сертификат в `LocalMachine\TrustedPeople` (UAC) и выполняет `Add-AppxPackage` |
| `scripts\uninstall-msix.ps1` | удаляет пакет, данные сохраняет (`-PreserveData:$false` — удалить) |

Установка на чужой ПК: положить рядом `TempoKaraoke_<версия>_x64.msix`,
`TempoKaraoke.cer` и папку `scripts`, затем запустить `install-msix.cmd`
(скрипт сам запросит права администратора для импорта сертификата).
Сертификат `.pfx` никому не передавать: он позволяет подписывать пакеты от вашего имени.

## Самообновление

1. Заполните `UpdateService.RepositoryOwner` / `RepositoryName` в
   `src/TempoKaraoke/Services/UpdateService.cs`.
2. Публикуйте релиз с тегом `v<версия>` и приложенным
   `TempoKaraoke-Setup-<версия>.exe` (это делает `.github/workflows/release.yml`
   по тегу).
3. Приложение раз в запуск (настройка «Проверять обновления при запуске»)
   спрашивает `releases/latest`, сравнивает версию и показывает InfoBar, а при
   старте ещё и системное уведомление Windows (правый нижний угол) через
   `AppNotificationManager` — его видно, даже если окно свёрнуто.
   По кнопке «Обновить» скачивается установщик и запускается с
   `/SILENT /CLOSEAPPLICATIONS /ALLUSERS|/CURRENTUSER`: он закрывает приложение,
   заменяет файлы и запускает новую версию. Область установки сохраняется:
   установщик пишет маркер `install-scope.txt` (`all`/`user`) в папку
   приложения, приложение передаёт `/ALLUSERS` или `/CURRENTUSER`, а при
   отсутствии маркера Inno повторяет область предыдущей установки
   (`UsePreviousPrivileges=yes`).

Ограничения, заложенные в код:

- скачивается только установщик с `github.com`/`githubusercontent.com` по HTTPS;
- в MSIX-сборке (`AppInfo.IsPackaged`) раздел обновлений скрыт — пакет
  обновляет Windows/Store, а не установщик;
- настройки, очередь и кэш лежат в `%LocalAppData%\TempoKaraoke` и при
  обновлении не трогаются.

## CI

`.github/workflows/release.yml` по тегу `v*` (или вручную) собирает портативный
publish, ставит Inno Setup через Chocolatey и прикладывает установщик к релизу.
Для подписи установщика раскомментируйте `SignTool=` в `.iss` и передайте
сертификат шагом CI.

# Сборка MSIX

Документ описывает, как собрать `Tempo Karaoke` в MSIX-пакет через WinApp CLI
0.7.1 и поставить его на машину. Проверено 2026-11-05 на этом репозитории:
`winapp package` собрал `TempoKaraoke_1.8.21.0_x64.msix` (180 МБ, 1386 файлов,
включая `libvlc` для win-x64/win-x86/win-arm64), пакет подписан сертификатом
`CN=9A24F022-CB13-4663-A71C-17E8B461355B` (identity из Partner Center).

## Что получается

| Свойство | Значение (из `src/TempoKaraoke/Package.appxmanifest`) |
|---|---|
| Identity Name | `CappuccinoDev.TempoKaraoke` |
| Publisher | `CN=9A24F022-CB13-4663-A71C-17E8B461355B` |
| PublisherDisplayName | `Cappuccino Dev` |
| Version | `1.8.21.0` (четыре компонента, четвёртый — для обновлений) |
| Зависимости | нет: `scripts\build-msix.ps1` собирает **self-contained** пакет |
| Capabilities | `runFullTrust`, `broadFileSystemAccess` (restricted) |

`scripts\build-msix.ps1` передаёт `--self-contained -p SelfContained=true`:
внутри пакета лежат и `.NET` (`coreclr.dll`, `hostfxr.dll`), и Windows App SDK
(`Microsoft.WindowsAppRuntime.dll`), поэтому `PackageDependency` в манифесте нет
и на целевом ПК ничего доустанавливать не нужно.

Если запускать `winapp package` вручную **без** этих флагов, получится
framework-dependent пакет: он потребует установленные .NET 10 Desktop Runtime и
Windows App Runtime 2.6, а в манифесте появится
`PackageDependency Microsoft.WindowsAppRuntime.2`.

## Требования на сборочной машине

- Windows 10 1809+ / Windows 11;
- .NET SDK 10.0.403 (`global.json` закрепляет версию, нужен именно 10.0.4xx);
- WinApp CLI 0.7.1 (`winapp --version`) — он же приносит `makeappx`/`signtool`;
- доступ к NuGet (пакеты восстанавливаются по `packages.lock.json`).

Visual Studio не обязателен: проект собирается `dotnet publish`, нативная
инструментальная цепочка нужна только для Native AOT (у нас он выключен).

## Шаг 1. Сертификат разработчика (один раз)

Publisher сертификата обязан совпадать с `Identity.Publisher` в манифесте,
поэтому subject берём из манифеста:

```powershell
cd D:\Laba\AppPlayList_Karaoke
winapp cert generate --manifest .\src\TempoKaraoke\Package.appxmanifest
```

Получается `devcert.pfx` в текущем каталоге, subject `CN=9A24F022-CB13-4663-A71C-17E8B461355B`, пароль по
умолчанию `password`. Сертификат **только для разработки**: любой, у кого есть
`.pfx`, может подписывать пакеты от этого имени. В git его не коммитим
(`*.pfx` в `.gitignore`), срок действия — 1 год, по истечении перегенерировать.

Свой пароль и свой publisher:

```powershell
winapp cert generate --manifest .\src\TempoKaraoke\Package.appxmanifest --password "P@ssw0rd"
```

Если пароль/имя publisher меняются, `--cert-password` передаётся и в `winapp package`.

## Шаг 2. Сборка пакета

```powershell
cd D:\Laba\AppPlayList_Karaoke
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --cert .\devcert.pfx
```

Что делает команда: восстанавливает зависимости, публикует проект в **Release**
(по умолчанию), подставляет манифест/ассеты, генерирует `resources.pri`, пакует
`makeappx` и подписывает `signtool`. Результат печатается в конце:

```
📦 Package: D:\Laba\AppPlayList_Karaoke\TempoKaraoke_1.8.21.0_x64.msix
🔒 Package has been signed
```

Полезные варианты:

```powershell
# другой каталог/имя артефакта
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --cert .\devcert.pfx `
  --output .\artifacts\TempoKaraoke_x64.msix

# один .msixbundle на x64 + arm64
winapp package .\src/TempoKaraoke\TempoKaraoke.csproj --arch x64 --arch arm64 --cert .\devcert.pfx

# без подписи (подписать отдельно, в т.ч. сертификатом CA)
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-sign

# пересобрать пакет из уже готового publish-вывода
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-build --cert .\devcert.pfx
```

Готовый `.msix` уже игнорируется git (`.gitignore`: `*.msix`), но держите его вне
репозитория, если раздаёте через сеть/флешку.

## Шаг 3. Чтобы пакет ставился на «чистой» машине

По умолчанию в манифесте пакета есть `PackageDependency` на Windows App SDK
2.6.4, а `.NET` берётся с машины. Два варианта:

| Вариант | Команда | Что нужно на целевом ПК |
|---|---|---|
| Компактный (по умолчанию) | `winapp package ... --arch x64 --cert .\devcert.pfx` | .NET 10 Desktop Runtime + Windows App Runtime 2.6 |
| Self-contained | `winapp package ... --arch x64 --self-contained -p SelfContained=true --cert .\devcert.pfx` | подпись/доверие к сертификату (+ Visual C++ Redistributable, если его нет в системе) |

- `--self-contained` задаёт только `WindowsAppSDKSelfContained=true`;
  `-p SelfContained=true` добавляет в пакет сам .NET. Нужны оба флага, если
  хотим «поставил и работает». Проверено: в таком пакете нет
  `PackageDependency` на Windows App Runtime, но есть `coreclr.dll`,
  `hostfxr.dll` и `Microsoft.WindowsAppRuntime.dll`.
- Пакет от этого только растёт: 180 МБ → 265 МБ (x64, проверено 2026-11-05),
  в распакованном виде ~650 МБ. Перед раздачей такую сборку прогоните:
  зависимости `libvlc` и `onnxruntime` лежат рядом с exe — проверьте
  воспроизведение MKV.
- Ставить Windows App Runtime 2.6 вручную: «Windows App Runtime Redistributable»
  с aka.ms/windowsappsdk, либо дать пользователю поставить его из Store.

## Шаг 4. Установка на ПК

Нужны два файла: `TempoKaraoke_1.8.21.0_x64.msix` и `devcert.pfx`.

1. **Доверить сертификат** (один раз на каждую машину, нужен админ):

   ```powershell
   winapp cert install .\devcert.pfx --password password
   ```

   Команда принимает `.pfx` или `.cer` и кладёт сертификат в доверенное
   хранилище машины. Без этого установка падает с `0x800B0109` («сертификат не из
   доверенной цепочки»). Повторная установка того же сертификата — с `--force`,
   другой пароль — через `--password`.

2. **Установить пакет** (можно без админа, установка идёт в профиль пользователя):

   ```powershell
   Add-AppxPackage .\TempoKaraoke_1.8.21.0_x64.msix
   ```

   Либо двойной клик по `.msix` → «Установить». Приложение появится в «Пуск»
   как **Tempo Karaoke**.

3. Проверить установку и запустить:

   ```powershell
   Get-AppxPackage CappuccinoDev.TempoKaraoke | Select-Object Name,Version,PackageFamilyName
   Get-StartApps | Where-Object Name -like '*Tempo*'
   ```

   Запуск — из «Пуск» или `explorer.exe shell:AppsFolder\<AppID>`, где `AppID`
   берётся из `Get-StartApps`.

Если Windows не даёт поставить доверенный пакет: «Параметры → Система → Для
разработчиков» → включить «Режим разработчика» (или «Боковая загрузка
приложений»). На Windows 11 подписанный доверенным сертификатом пакет ставится и
без этого, но режим разработчика убирает лишние проверки.

## Обновление и удаление

- Перед новой сборкой поднимите `Version` в `Package.appxmanifest` — иначе
  `Add-AppxPackage` откажется ставить пакет поверх (`0x80073CFB`). Версия в UI
  («О приложении») читается из `Package.Current.Id.Version`, отдельно её
  дублировать не нужно.
- Переустановка той же версии или откат на старую:

  ```powershell
  Add-AppxPackage .\TempoKaraoke_1.8.21.0_x64.msix -ForceUpdateFromAnyVersion
  ```

- Удалить:

  ```powershell
  Get-AppxPackage CappuccinoDev.TempoKaraoke | Remove-AppxPackage
  ```

  Данные приложения (SQLite-кэш, настройки) лежат в
  `%LOCALAPPDATA%\Packages\<PackageFamilyName>` и при таком удалении остаются;
  `Remove-AppxPackage -PreserveApplicationData:$false` стирает и их.

## Частые ошибки

| Код / сообщение | Причина | Что делать |
|---|---|---|
| `0x800B0109` | корневой сертификат не доверен | `winapp cert install .\devcert.pfx` (админ) |
| `0x80073CF3` / «не найдена зависимость» | нет Windows App Runtime 2.6 или .NET 10 | поставить рантаймы или собрать с `--self-contained -p SelfContained=true` |
| `0x80073CFB` | такая версия уже установлена | поднять `Version` в манифесте или `-ForceUpdateFromAnyVersion` |
| `0x80073CF9` / регистрация не удалась | конфликт с dev-регистрацией от `winapp run` | `winapp unregister --manifest .\src\TempoKaraoke\Package.appxmanifest` |
| `Publisher mismatch` при сборке | subject сертификата ≠ `Identity.Publisher` | `winapp cert generate --manifest ...` заново |
| Пакет ставится, но нет доступа к `G:\Karaoke` | restricted capability `broadFileSystemAccess` | «Параметры → Конфиденциальность → Файловая система» → разрешить приложению; путь к медиатеке задаётся в настройках приложения |

## Раздача не «для себя»

Dev-сертификат не годится для чужих машин: каждый получатель должен сам
доверять самоподписанному корню, а это ровно то, чего просить у пользователей
нельзя. Варианты для нормальной поставки:

1. **Сертификат от CA** (OV/EV code signing) — subject обязан совпадать с
   `Identity.Publisher`, иначе манифест придётся править под сертификат.
   Подпись с меткой времени, чтобы пакет оставался валидным после истечения
   сертификата:

   ```powershell
   winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-sign
   winapp sign .\TempoKaraoke_1.8.21.0_x64.msix .\prod.pfx --timestamp http://timestamp.digicert.com
   ```

2. **Azure Trusted Signing** — `winapp az-sign`.
3. **Microsoft Store** — партнёрский identity из Partner Center; подпись делает
   Store. Restricted capability `broadFileSystemAccess` при этом придётся
   обосновывать при сабмите.

При любой раздаче прикладывайте `THIRD-PARTY-NOTICES.md`: `libvlc` распространяется
по LGPL-2.1-or-later, и пользователь должен иметь возможность заменить библиотеку.

## CI

В `.github/workflows/build.yml` уже есть сборка и тесты; для пакета добавьте шаг
(Windows-раннер с .NET SDK 10 и WinApp CLI):

```yaml
- uses: microsoft/setup-winapp@v1
- run: winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --arch arm64 --no-sign --quiet
```

Артефакты — по путям, которые печатает команда; подпись держите отдельным
защищённым шагом с секретом PFX, dev-сертификат в CI не нужен.

## Заметки по этому проекту

- `VideoLAN.LibVLC.Windows` кладёт в publish **три** копии `libvlc`:
  `win-x64` (108 МБ), `win-x86` (105 МБ), `win-arm64` (91 МБ) — 1253 записи из
  1386 и большая часть 426 МБ распакованного пакета. Для x64-сборки две копии
  лишние: их можно вырезать в `.csproj` (`<Target AfterTargets="Publish">` с
  `<Delete>` по `$(PublishDir)libvlc\win-x86\**` и `win-arm64\**`) — сжатый
  `.msix` тоже уменьшится, но после правки обязательно прогоните воспроизведение
  MKV, чтобы проверить, что LibVLCSharp находит нужную копию.
- Сборка тянет `onnxruntime.dll` (23 МБ) и `DirectML.dll` (19 МБ) из
  Windows App SDK 2.6; для караоке они не используются, но вырезать их без
  проверки WinUI-фич нельзя.
- `runFullTrust` + `broadFileSystemAccess` разрешены для боковой загрузки;
  для Store это restricted capability с обоснованием.
- Приложение запускает дочерний процесс `--wifi-direct-helper` из того же exe,
  путь берёт через `Environment.ProcessPath`, ассеты — через
  `AppContext.BaseDirectory`. В MSIX оба указывают в каталог установки, правок
  под упаковку не требуется.

# Публикация в Microsoft Store

Пакет для Store — тот же MSIX, но с identity из Partner Center и без подписи:
Store подписывает пакет сам.

## Identity

В Partner Center: **Products → Tempo Karaoke (9NDSMQP32F91) → Product identity**.
Оттуда нужны три значения:

| Поле Partner Center | Куда в `Package.appxmanifest` | Значение в репозитории |
|---|---|---|
| Package/Identity/Name | `Identity/@Name` | `CappuccinoDev.TempoKaraoke` |
| Package/Identity/Publisher | `Identity/@Publisher` | `CN=9A24F022-CB13-4663-A71C-17E8B461355B` |
| Package/Properties/PublisherDisplayName | `Properties/PublisherDisplayName` | `Cappuccino Dev` |

Эти значения уже стоят в манифесте. Package Family Name получается
`CappuccinoDev.TempoKaraoke_zxrfg130ja2cc`, Store ID — `9NDSMQP32F91`.

## Сборка и отправка

```powershell
.\scripts\build-msix.ps1 -Mode store
# -> artifacts\msix\store\TempoKaraoke_<версия>_x64.msix
```

Пакет для sideload собирается в `artifacts\msix\sideload\` и **подписан**
самодельным сертификатом — его в Partner Center загружать нельзя (Store
принимает только неподписанный пакет и подписывает его сам).

Далее в Partner Center: **Packages → Add package** → загрузить `.msix`
(принимаются `.msix`, `.msixbundle`, `.msixupload`) → заполнить Store listing →
Submit for certification.

## Правила, о которых легко забыть

- Версия в манифесте должна быть **выше** последней опубликованной; четыре
  компонента, четвёртый — счётчик сборки.
- `broadFileSystemAccess` — restricted capability. При сабмите её нужно
  обосновать: приложению нужен доступ к папкам с видео, которые выбирает
  пользователь. Без обоснования пакет не пропустят.
- `runFullTrust` тоже restricted: обоснование обязательно (см. ниже).
- Архитектура — x64. Для ARM64 соберите отдельный пакет (`-Arch arm64`) или
  bundle через `winapp package ... --arch x64 --arch arm64 --no-sign`.
- Скриншоты для листинга делайте с реального экрана приложения на своей
  медиатеке: в репозитории нет ни чужих библиотек, ни обложек.
  Store logo 300×300 — `poster/tempo-karaoke-store-logo-300.png`.
- После смены identity старые установки (и MSIX, и установщик) считаются другим
  приложением: обновление поверх них не встанет, нужна переустановка.

## Обоснование restricted capabilities (Partner Center)

Поля «Почему вам требуется …» в разделе «Примечания о сертификации».
Ниже — проверенные формулировки.

### runFullTrust (EN)

```
Tempo Karaoke is a WinUI 3 / .NET desktop application that runs as a full-trust
Win32 process, not inside the AppContainer sandbox. The app plays video through
the native libvlc engine (VideoLAN.LibVLC), which loads its own native DLLs and
needs GPU and audio access; it keeps a local SQLite cache of the user's media
metadata; it hosts an in-process HTTP/WebSocket server so phones on the same
network can control playback; and it starts a companion process of the same
executable to run the Wi-Fi Direct advertisement publisher. None of these work
inside the AppContainer sandbox. The app installs no services or drivers, does
not start with Windows unless the user enables it, and does not touch anything
outside the user's own session.
```

### broadFileSystemAccess (EN)

```
The app is a karaoke player for the user's own video collection, which can live
on any local drive, an external disk or a network share. The user explicitly
adds the folders in the app's settings through the system folder picker; Tempo
Karaoke then enumerates video files in those folders, reads metadata to build a
searchable library, generates thumbnails and plays the files with libvlc.
broadFileSystemAccess is needed so the library works for the folders the user
chooses, without copying or moving media into app storage. The app only reads
files and never modifies, deletes or uploads them. File paths, names and content
never leave the device.
```

### Те же тексты по-русски

```
runFullTrust: Tempo Karaoke — настольное приложение WinUI 3 / .NET, работающее
как процесс Win32 с полным доверием, а не в песочнице AppContainer. Видео
воспроизводится нативным движком libvlc (VideoLAN.LibVLC), который загружает
собственные DLL и требует доступа к GPU и аудио; приложение ведёт локальный
кэш метаданных в SQLite; поднимает встроенный HTTP/WebSocket-сервер, чтобы
телефоны в той же сети управляли воспроизведением; и запускает второй процесс
того же exe для Wi-Fi Direct. В песочнице AppContainer это невозможно.
Приложение не устанавливает службы и драйверы, не стартует с Windows без
согласия пользователя и не выходит за пределы его сеанса.

broadFileSystemAccess: приложение — караоке-плеер для собственной коллекции
видео пользователя, которая может лежать на любом локальном диске, внешнем
носителе или сетевой папке. Пользователь сам добавляет папки в настройках
через системный выбор папок; Tempo Karaoke перечисляет видеофайлы в этих
папках, читает метаданные для поиска, строит миниатюры и воспроизводит файлы
через libvlc. broadFileSystemAccess нужен, чтобы библиотека работала с
выбранными пользователем папками без копирования медиа в хранилище
приложения. Приложение только читает файлы и никогда их не изменяет, не
удаляет и не выгружает. Пути, имена и содержимое файлов не покидают устройство.
```

### Что написать в «Дополнительная информация о тестировании»

```
1. Запустите приложение. Библиотека пустая: «Add a video folder in Settings».
2. Параметры → «Add folder» → выберите любую папку с видеофайлами (MKV/MP4).
   Каталог появится через несколько секунд.
3. Нажмите Ctrl+F и введите часть имени файла — работает поиск.
4. Откройте «Web remote» → «Start remote» — появится QR-код и адрес.
5. Никаких тестовых медиа в пакете нет — подойдёт любой видеофайл.
```

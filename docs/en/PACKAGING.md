English | [Русский](../ru/PACKAGING.md)

# Building, Installing and Updating

The project has two delivery models:

| Model | When you need it | Section |
|---|---|---|
| **Portable build + Inno Setup** | the main distribution: installation without admin rights, self-update from inside the app | [below](#portable-build-and-inno-installer) |
| **MSIX via WinApp CLI** | Microsoft Store, corporate distribution with a managed certificate | [MSIX](#building-msix) |

This document was verified on 2026-11-05 against this repository.

# Portable Build and Inno Installer

The approach follows UniGetUI: the app is installed as unpacked files into the user
profile (`Inno Setup`, `PrivilegesRequired=lowest`), and the update downloads and
runs the same installer — no UAC and no certificate trust.

## Building

```powershell
# Publish + installer in a single command (Inno Setup 6/7 must be installed)
.\scripts\build-installer.ps1

# The same, step by step
dotnet publish src\TempoKaraoke\TempoKaraoke.csproj -c Release -p:PublishProfile=portable-x64
"D:\Program Files\Inno Setup 7\ISCC.exe" installer\TempoKaraoke.iss /DMyAppVersion=1.8.21.0
```

Result: `artifacts\TempoKaraoke-Setup-<version>.exe` (~110 MB).

The `portable-x64.pubxml` profile sets `WindowsPackageType=None` and produces a
**fully self-contained** build: both .NET and the Windows App SDK
(`Microsoft.WindowsAppRuntime.dll`) sit next to the exe. That is why nothing has to
be installed on the target machine, and the installer neither checks for runtimes
nor asks for admin rights.

The version is kept in two places, and they must match:

- `src/TempoKaraoke/TempoKaraoke.csproj` — `<Version>` (read by the app through
  `AppInfo` and passed to `iscc` via `/DMyAppVersion`);
- `src/TempoKaraoke/Package.appxmanifest` — `Identity/@Version` (MSIX only).

## What the Installer Does

- shows a folder-selection page; the default is `{localappdata}\Programs\Tempo Karaoke`
  (no UAC), but the dialog also offers an "all users" installation — that one goes
  to Program Files and requires UAC;
- closes the running app and its helper processes (`taskkill`);
- creates shortcuts (with an AUMID for notifications) and an uninstaller, and
  launches the app after installation (including during a silent update);
- on uninstall, asks whether to erase settings, queue and cache
  (`%LocalAppData%\TempoKaraoke`). Data is preserved by default; a silent
  uninstall (`/VERYSILENT`) leaves the data untouched.

## Scripts

| Script | What it does |
|---|---|
| `scripts\build-installer.ps1` | publish + Inno Setup → `artifacts\TempoKaraoke-Setup-<version>.exe` |
| `scripts\make-cert.ps1` | self-signed certificate for MSIX: `certs\TempoKaraoke.pfx` (with the key) and `certs\TempoKaraoke.cer` (to hand out) |
| `scripts\build-msix.ps1 -Mode sideload` | signed MSIX for installation on a PC |
| `scripts\build-msix.ps1 -Mode store` | unsigned MSIX for Partner Center |
| `scripts\install-msix.ps1` / `.cmd` | installs the certificate into `LocalMachine\TrustedPeople` (UAC) and runs `Add-AppxPackage` |
| `scripts\uninstall-msix.ps1` | removes the package and keeps the data (`-PreserveData:$false` deletes it) |

Installing on someone else's PC: put `TempoKaraoke_<version>_x64.msix`,
`TempoKaraoke.cer` and the `scripts` folder side by side, then run `install-msix.cmd`
(the script asks for administrator rights to import the certificate itself).
Never pass the `.pfx` certificate to anyone: it allows signing packages in your name.

## Self-Update

1. Fill in `UpdateService.RepositoryOwner` / `RepositoryName` in
   `src/TempoKaraoke/Services/UpdateService.cs`.
2. Publish a release tagged `v<version>` with the attached
   `TempoKaraoke-Setup-<version>.exe` (`.github/workflows/release.yml` does this
   on tag).
3. Once per launch (the "Check for updates at startup" setting) the app queries
   `releases/latest` and compares the version. The automatic check shows no
   windows: when an update is found, an `InfoBadge` dot appears next to the
   "Settings" item, and the "Application version" card shows the status
   `A new version is available: vX (current: vY)` together with the "What's new",
   "Remind me later" and "Install vX" buttons. The "Check for updates" button
   runs a check manually and shows a progress indicator and the states
   "checking", "up to date", "update available" and "network error"; before the
   first check a neutral label reads `Current version: vX`. A `404` response from
   GitHub (no releases) counts as up to date.
   A confirmation dialog opens before installation. The installer is then
   downloaded and started with
   `/SILENT /CLOSEAPPLICATIONS /ALLUSERS|/CURRENTUSER`: it closes the app,
   replaces the files and launches the new version. The installation scope is
   preserved: the installer writes an `install-scope.txt` marker (`all`/`user`)
   into the app folder, the app passes `/ALLUSERS` or `/CURRENTUSER`, and when
   the marker is absent Inno reuses the scope of the previous installation
   (`UsePreviousPrivileges=yes`).

The installer (`installer/TempoKaraoke.iss`) targets Inno Setup 6.3+/7:

- the default version is read from `TempoKaraoke.exe` (4 components), so `iscc`
  can be run without `/DMyAppVersion`;
- with `/SILENT` (an update from the app) the new version starts by itself, with
  no final wizard page;
- the data-deletion question is localized (RU/EN), with the "No" button as the
  default;
- `taskkill /t` closes child processes too, and `install-scope.txt` is removed on
  uninstall.

### Update Toast and Badge

- Toast strings are resolved before the XML is built: `LocalizationService` (JSON,
  works in portable) → `ResourceLoader` → literal fallback. A resource key can
  never end up on the screen.
- The toast contains a title, a `current → new` transition, 1–3 changelog items,
  the installer size and the publication date. The XML is assembled by
  `UpdateToastBuilder` (Core), so the markup can be checked with unit tests.
- `Tag="update-available"`, `Group="updates"`, a 24-hour lifetime: a repeated
  check replaces the previous toast.
- The toast body (`action=open_update_page`) opens "Settings › Updates", and the
  buttons pass `action=install` / `action=postpone`. A running app receives the
  `NotificationInvoked` event, a cold start reads
  `AppInstance.GetActivatedEventArgs()`; a second process is redirected into the
  first one via `AppInstance.RedirectActivationToAsync`.
- The "Settings" item shows a badge: "Update" (a new version is available) or
  "New" (the installer has already been downloaded); with no update it is
  hidden. The badge is clickable and leads to the updates section.

Constraints built into the code:

- only an installer hosted on `github.com`/`githubusercontent.com` over HTTPS is
  downloaded;
- in the MSIX build (`AppInfo.IsPackaged`) the updates section is hidden — the
  package is updated by Windows/Store, not by the installer;
- settings, queue and cache live in `%LocalAppData%\TempoKaraoke` and are left
  untouched by updates.

## CI

`.github/workflows/release.yml` runs on the tag `v*` (or manually), builds the
portable publish, installs Inno Setup via Chocolatey and attaches the installer to
the release. To sign the installer, uncomment `SignTool=` in the `.iss` and pass
the certificate in a CI step.

Every tag must have a `docs/releases/<tag>.md` file — the workflow verifies it
before building and publishes its contents as "what's new" in the GitHub Release.
If the file is missing, the release fails. Template: `docs/releases/v1.9.0.0.md`;
a brief version history lives in `CHANGELOG.md`.

# Building MSIX

This document describes how to build `Tempo Karaoke` into an MSIX package with
WinApp CLI 0.7.1 and install it on a machine. Verified on 2026-11-05 against this
repository: `winapp package` produced `TempoKaraoke_1.8.21.0_x64.msix` (180 MB,
1386 files, including `libvlc` for win-x64/win-x86/win-arm64), and the package was
signed with the certificate `CN=9A24F022-CB13-4663-A71C-17E8B461355B` (the
identity from Partner Center).

## What You Get

| Property | Value (from `src/TempoKaraoke/Package.appxmanifest`) |
|---|---|
| Identity Name | `CappuccinoDev.TempoKaraoke` |
| Publisher | `CN=9A24F022-CB13-4663-A71C-17E8B461355B` |
| PublisherDisplayName | `Cappuccino Dev` |
| Version | `1.8.21.0` (four components, the fourth is for updates) |
| Dependencies | none: `scripts\build-msix.ps1` builds a **self-contained** package |
| Capabilities | `runFullTrust`, `broadFileSystemAccess` (restricted) |

`scripts\build-msix.ps1` passes `--self-contained -p SelfContained=true`: the
package carries both `.NET` (`coreclr.dll`, `hostfxr.dll`) and the Windows App SDK
(`Microsoft.WindowsAppRuntime.dll`), so there is no `PackageDependency` in the
manifest and nothing has to be installed on the target PC.

If you run `winapp package` manually **without** these flags, you get a
framework-dependent package: it requires .NET 10 Desktop Runtime and Windows App
Runtime 2.6 to be installed, and
`PackageDependency Microsoft.WindowsAppRuntime.2` appears in the manifest.

## Build Machine Requirements

- Windows 10 1809+ / Windows 11;
- .NET SDK 10.0.403 (`global.json` pins the version, and the 10.0.4xx band is
  required specifically);
- WinApp CLI 0.7.1 (`winapp --version`) — it also brings `makeappx`/`signtool`;
- access to NuGet (packages are restored from `packages.lock.json`).

Visual Studio is not required: the project builds with `dotnet publish`, and the
native toolchain is only needed for Native AOT (which is disabled here).

## Step 1. Developer Certificate (Once)

The certificate publisher must match `Identity.Publisher` in the manifest, so the
subject is taken from the manifest:

```powershell
cd D:\Laba\AppPlayList_Karaoke
winapp cert generate --manifest .\src\TempoKaraoke\Package.appxmanifest
```

You get `devcert.pfx` in the current directory, subject
`CN=9A24F022-CB13-4663-A71C-17E8B461355B`, and the default password `password`. The
certificate is **development-only**: anyone holding the `.pfx` can sign packages
under that name. Do not commit it to git (`*.pfx` is in `.gitignore`); it is valid
for 1 year, so regenerate it when it expires.

Your own password and publisher:

```powershell
winapp cert generate --manifest .\src\TempoKaraoke\Package.appxmanifest --password "P@ssw0rd"
```

If the password or the publisher name changes, pass `--cert-password` to
`winapp package` as well.

## Step 2. Building the Package

```powershell
cd D:\Laba\AppPlayList_Karaoke
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --cert .\devcert.pfx
```

What the command does: restores dependencies, publishes the project in **Release**
(the default), injects the manifest and assets, generates `resources.pri`, packs
with `makeappx` and signs with `signtool`. The result is printed at the end:

```
📦 Package: D:\Laba\AppPlayList_Karaoke\TempoKaraoke_1.8.21.0_x64.msix
🔒 Package has been signed
```

Useful variants:

```powershell
# a different artifact directory/name
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --cert .\devcert.pfx `
  --output .\artifacts\TempoKaraoke_x64.msix

# a single .msixbundle for x64 + arm64
winapp package .\src/TempoKaraoke\TempoKaraoke.csproj --arch x64 --arch arm64 --cert .\devcert.pfx

# unsigned (sign separately, including with a CA certificate)
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-sign

# rebuild the package from an existing publish output
winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-build --cert .\devcert.pfx
```

The finished `.msix` is already ignored by git (`.gitignore`: `*.msix`), but keep
it outside the repository if you distribute it over the network or on a flash
drive.

## Step 3. Making the Package Install on a Clean Machine

By default the package manifest contains a `PackageDependency` on Windows App SDK
2.6.4, and `.NET` is taken from the machine. Two options:

| Option | Command | What the target PC needs |
|---|---|---|
| Compact (default) | `winapp package ... --arch x64 --cert .\devcert.pfx` | .NET 10 Desktop Runtime + Windows App Runtime 2.6 |
| Self-contained | `winapp package ... --arch x64 --self-contained -p SelfContained=true --cert .\devcert.pfx` | signature/trust in the certificate (+ Visual C++ Redistributable, if it is missing from the system) |

- `--self-contained` only sets `WindowsAppSDKSelfContained=true`;
  `-p SelfContained=true` adds .NET itself to the package. Both flags are needed
  for "install it and it just works". Verified: such a package has no
  `PackageDependency` on Windows App Runtime, but does contain `coreclr.dll`,
  `hostfxr.dll` and `Microsoft.WindowsAppRuntime.dll`.
- The package only grows: 180 MB → 265 MB (x64, verified 2026-11-05), around
  650 MB unpacked. Smoke-test such a build before distributing it: the `libvlc`
  and `onnxruntime` dependencies sit next to the exe — check MKV playback.
- Installing Windows App Runtime 2.6 by hand: the "Windows App Runtime
  Redistributable" from aka.ms/windowsappsdk, or let the user install it from the
  Store.

## Step 4. Installing on a PC

Two files are needed: `TempoKaraoke_1.8.21.0_x64.msix` and `devcert.pfx`.

1. **Trust the certificate** (once per machine, needs admin):

   ```powershell
   winapp cert install .\devcert.pfx --password password
   ```

   The command accepts a `.pfx` or `.cer` and puts the certificate into the
   machine's trusted store. Without this, installation fails with `0x800B0109`
   ("the certificate is not from a trusted chain"). To install the same
   certificate again, use `--force`; for a different password, use `--password`.

2. **Install the package** (admin not required, it installs into the user
   profile):

   ```powershell
   Add-AppxPackage .\TempoKaraoke_1.8.21.0_x64.msix
   ```

   Or double-click the `.msix` → "Install". The app then appears in Start as
   **Tempo Karaoke**.

3. Verify the installation and launch it:

   ```powershell
   Get-AppxPackage CappuccinoDev.TempoKaraoke | Select-Object Name,Version,PackageFamilyName
   Get-StartApps | Where-Object Name -like '*Tempo*'
   ```

   Launch it from Start or with `explorer.exe shell:AppsFolder\<AppID>`, where
   `AppID` comes from `Get-StartApps`.

If Windows refuses to install a trusted package: "Settings → System → For
developers" → turn on "Developer Mode" (or "Sideload apps"). On Windows 11 a
package signed by a trusted certificate installs without this, but Developer Mode
removes the extra checks.

## Updating and Uninstalling

- Raise `Version` in `Package.appxmanifest` before a new build — otherwise
  `Add-AppxPackage` refuses to install over the old one (`0x80073CFB`). The
  version shown in the UI ("About") is read from `Package.Current.Id.Version`, so
  there is no need to duplicate it.
- Reinstalling the same version or rolling back to an older one:

  ```powershell
  Add-AppxPackage .\TempoKaraoke_1.8.21.0_x64.msix -ForceUpdateFromAnyVersion
  ```

- Uninstall:

  ```powershell
  Get-AppxPackage CappuccinoDev.TempoKaraoke | Remove-AppxPackage
  ```

  App data (the SQLite cache, settings) lives in
  `%LOCALAPPDATA%\Packages\<PackageFamilyName>` and survives this kind of removal;
  `Remove-AppxPackage -PreserveApplicationData:$false` erases it as well.

## Common Errors

| Code / message | Cause | What to do |
|---|---|---|
| `0x800B0109` | the root certificate is not trusted | `winapp cert install .\devcert.pfx` (admin) |
| `0x80073CF3` / "dependency not found" | Windows App Runtime 2.6 or .NET 10 is missing | install the runtimes or build with `--self-contained -p SelfContained=true` |
| `0x80073CFB` | this version is already installed | raise `Version` in the manifest or use `-ForceUpdateFromAnyVersion` |
| `0x80073CF9` / registration failed | conflict with a dev registration left by `winapp run` | `winapp unregister --manifest .\src\TempoKaraoke\Package.appxmanifest` |
| `Publisher mismatch` during the build | the certificate subject ≠ `Identity.Publisher` | regenerate with `winapp cert generate --manifest ...` |
| The package installs but there is no access to `G:\Karaoke` | the restricted capability `broadFileSystemAccess` | "Settings → Privacy → File system" → allow the app; the media library path is set in the app settings |

## Distributing Beyond Your Own Machine

A dev certificate is no good for other people's machines: every recipient would
have to trust the self-signed root themselves, and that is exactly what you cannot
ask of users. Options for a real distribution:

1. **A CA certificate** (OV/EV code signing) — the subject must match
   `Identity.Publisher`, otherwise the manifest has to be edited to fit the
   certificate. Sign with a timestamp so the package stays valid after the
   certificate expires:

   ```powershell
   winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --no-sign
   winapp sign .\TempoKaraoke_1.8.21.0_x64.msix .\prod.pfx --timestamp http://timestamp.digicert.com
   ```

2. **Azure Trusted Signing** — `winapp az-sign`.
3. **Microsoft Store** — the partner identity from Partner Center; signing is done
   by the Store. The restricted capability `broadFileSystemAccess` then has to be
   justified at submission.

With any distribution channel, ship `THIRD-PARTY-NOTICES.md` alongside: `libvlc` is
distributed under LGPL-2.1-or-later, and the user must be able to replace the
library.

## CI

`.github/workflows/build.yml` already builds and tests; add a step for the package
(a Windows runner with .NET SDK 10 and WinApp CLI):

```yaml
- uses: microsoft/setup-winapp@v1
- run: winapp package .\src\TempoKaraoke\TempoKaraoke.csproj --arch x64 --arch arm64 --no-sign --quiet
```

Artifacts go to the paths the command prints; keep signing in a separate protected
step with the PFX secret — CI does not need a dev certificate.

## Notes on This Project

- `VideoLAN.LibVLC.Windows` puts **three** copies of `libvlc` into the publish
  output: `win-x64` (108 MB), `win-x86` (105 MB), `win-arm64` (91 MB) — 1253 of the
  1386 entries and most of the 426 MB unpacked package. For an x64 build two copies
  are redundant: you can trim them in the `.csproj`
  (`<Target AfterTargets="Publish">` with `<Delete>` over `$(PublishDir)libvlc\win-x86\**`
  and `win-arm64\**`) — the compressed `.msix` shrinks too, but after the change
  always run MKV playback to confirm LibVLCSharp still finds the right copy.
- The build pulls `onnxruntime.dll` (23 MB) and `DirectML.dll` (19 MB) from
  Windows App SDK 2.6; karaoke does not use them, but they cannot be removed
  without testing the WinUI features.
- `runFullTrust` + `broadFileSystemAccess` are permitted for sideloading; for the
  Store they are restricted capabilities that require a justification.
- The app launches a child process `--wifi-direct-helper` from the same exe, takes
  the path via `Environment.ProcessPath` and resolves assets via
  `AppContext.BaseDirectory`. In MSIX both point to the installation directory, so
  no changes are needed for packaging.

# Publishing to the Microsoft Store

The Store package is the same MSIX, but with the identity from Partner Center and
without a signature: the Store signs the package itself.

## Identity

In Partner Center: **Products → Tempo Karaoke (9NDSMQP32F91) → Product identity**.
Three values are needed from there:

| Partner Center field | Where it goes in `Package.appxmanifest` | Value in the repository |
|---|---|---|
| Package/Identity/Name | `Identity/@Name` | `CappuccinoDev.TempoKaraoke` |
| Package/Identity/Publisher | `Identity/@Publisher` | `CN=9A24F022-CB13-4663-A71C-17E8B461355B` |
| Package/Properties/PublisherDisplayName | `Properties/PublisherDisplayName` | `Cappuccino Dev` |

These values are already in the manifest. The Package Family Name is
`CappuccinoDev.TempoKaraoke_zxrfg130ja2cc`, the Store ID is `9NDSMQP32F91`.

## Building and Submitting

```powershell
.\scripts\build-msix.ps1 -Mode store
# -> artifacts\msix\store\TempoKaraoke_<version>_x64.msix
```

The sideload package is built into `artifacts\msix\sideload\` and is **signed**
with a home-made certificate — it cannot be uploaded to Partner Center (the Store
accepts only an unsigned package and signs it itself).

Then in Partner Center: **Packages → Add package** → upload the `.msix`
(`.msix`, `.msixbundle` and `.msixupload` are accepted) → fill in the Store listing
→ Submit for certification.

## Rules That Are Easy to Forget

- The version in the manifest must be **higher** than the last published one; four
  components, with the fourth as the build counter.
- `broadFileSystemAccess` is a restricted capability. It has to be justified at
  submission: the app needs access to the video folders the user selects. Without
  a justification the package will not pass.
- `runFullTrust` is restricted too: a justification is mandatory (see below).
- The architecture is x64. For ARM64, build a separate package (`-Arch arm64`) or a
  bundle via `winapp package ... --arch x64 --arch arm64 --no-sign`.
- Take listing screenshots from the real app screen with your own media library:
  the repository contains neither third-party libraries nor artwork.
  The 300×300 Store logo is `poster/tempo-karaoke-store-logo-300.png`.
- After a change of identity, existing installations (both MSIX and the installer)
  count as a different app: an update will not install over them, a reinstall is
  required.

## Justifying Restricted Capabilities (Partner Center)

The "Why does your product need ..." fields in the "Certification notes" section.
Below are the verified wordings.

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

### The Same Texts in Russian

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

### What to Write in "Additional Testing Information"

```
1. Launch the app. The library is empty: "Add a video folder in Settings".
2. Settings → "Add folder" → pick any folder with video files (MKV/MP4).
   The catalog appears within a few seconds.
3. Press Ctrl+F and type part of a file name — search works.
4. Open "Web remote" → "Start remote" — a QR code and an address appear.
5. There is no test media in the package — any video file will do.
```

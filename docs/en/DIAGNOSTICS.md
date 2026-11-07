English | [Русский](../ru/DIAGNOSTICS.md)

# Logging and error reports

## Log files

Location:

* MSIX build: `%LocalAppData%\Packages\<package>\LocalState\logs\`
* portable build: `%LocalAppData%\TempoKaraoke\logs\`

Files:

* `tempo-karaoke-YYYYMMDD.log` — main application log;
* `tempo-karaoke-wifi-direct.log` — step-by-step log of the isolated Wi-Fi Direct
  helper process.

The main log rotates at 5 MB and keeps five files (`*.log.1` … `*.log.5`).

## What is written

Every entry has a timestamp, level (`INFO`, `WARN`, `ERROR`, `FATAL`), category,
session id and optional operation id:

```text
2026-11-06 21:14:02.517 [INFO] [wifi-direct] [session:9f2a11c3] [op:4f8c21aa] Starting Wi-Fi Direct: ..., ssid=T***********
```

The session header contains:

* application version and install mode (MSIX / portable);
* OS description and architectures (OS and process);
* .NET runtime;
* current culture, UI culture and selected app language;
* log directory;
* host machine name and processor count.

Wi-Fi related entries contain the adapter state, active infrastructure
connection (SSID masked), Wi-Fi Direct state machine transition, result and
`HRESULT` when a native/WinRT call throws. PIN failures log only the role, attempt
number and lockout state — never the PIN.

Normal events are written only when **Settings → Diagnostics → Logging** is on.
Crashes and user-created reports are written regardless.

## Global handlers

`App` registers:

* `Application.UnhandledException` (WinUI);
* `AppDomain.CurrentDomain.UnhandledException`;
* `TaskScheduler.UnobservedTaskException`.

All three write a `FATAL` entry with the exception chain before the process
terminates. The web remote additionally reports unhandled JS errors and promise
rejections to `/api/client-error`; the server writes only the first 400 characters
to the log, without URL tokens.

## Collecting a report

**Settings → Diagnostics → Error report → Create report**.

The archive is saved as:

```text
<LocalAppData>\TempoKaraoke\Reports\tempo-karaoke-report-YYYYMMDD-HHmmss.zip
```

(or the packaged equivalent). Contents:

```text
system-info.txt        version, OS, runtime, locale, Wi-Fi adapter/connection,
                       Wi-Fi Direct state, localization issues
settings-summary.txt   theme, language, feature switches, counts
localization.txt       manifest status, loaded languages, validation issues
logs/01-tempo-karaoke-....log
logs/02-...
```

The same dialog offers to open the reports folder. Send the ZIP manually by
e-mail or attach it to an issue; there is no automatic upload endpoint.

## Privacy

The report and logs deliberately exclude:

* song titles, artists and media file paths;
* playlist and guest names;
* full SSIDs (only the first character is kept);
* MAC addresses (only vendor and last octet are kept);
* PINs and PIN hashes;
* authentication tokens;
* recent-file lists and library roots.

Media paths are replaced by `<path hidden>`; PIN material by `<pin hidden>`.
`PrivacyMasking` in `TempoKaraoke.Core/Diagnostics` implements the masks.

## Known limitation: mini-dumps

The report does not create a process mini-dump. A dump requires explicit user
consent and native `MiniDumpWriteDump` interop; the current implementation covers
diagnosis with logs, environment, adapter state and exception stacks. If a kernel
BSOD occurs, Windows writes its own dump under `%SystemRoot%\Minidump` — attach
that file separately if needed.

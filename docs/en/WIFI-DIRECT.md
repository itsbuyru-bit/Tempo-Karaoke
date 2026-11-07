English | [Русский](../ru/WIFI-DIRECT.md)

# Wi-Fi Direct: BSOD risk and safe mode

## Observed problem

On one PC the sequence was:

1. QR code appears.
2. The remote window looks like Wi-Fi Direct was never started.
3. QR code appears again.
4. A phone connection sometimes ends in `IRQL_NOT_LESS_OR_EQUAL (0xA)` in
   `ntoskrnl.exe`.
5. Wi-Fi Direct starts reliably when Wi-Fi is enabled but not associated with an
   infrastructure network.
6. When the PC is associated with a Wi-Fi network, the group is created but the
   phone cannot connect, and the crash risk is higher.

This is consistent with a wireless driver / SoftAP coexistence conflict: the
adapter is asked to run infrastructure Wi-Fi and Wi-Fi Direct at the same time,
possibly while it is still in a transitional state. A user-mode application
cannot guarantee that a kernel driver will not bugcheck. It can only remove the
risky combinations, serialize adapter operations and leave a useful trail.

## What the app does now

### Isolated helper process

The `WiFiDirectAdvertisementPublisher` runs in a child instance of the executable
(`--wifi-direct-helper`), not in the WinUI process. The helper owns exactly one
publisher for its lifetime and stops it on `stop` or when standard input closes.
It writes every step to `logs/tempo-karaoke-wifi-direct.log`.

### Pre-flight checks

Before starting, `WiFiDirectCoordinator` checks:

* is a Wi-Fi radio present and is it on (`Radio.GetRadiosAsync`);
* is an infrastructure WLAN profile connected (`NetworkInformation`);
* is a session already active (the coordinator state / helper process);
* is the adapter in a usable state (radio off/unknown is reported as
  unsupported).

The adapter's support for simultaneous infrastructure + SoftAP operation is not
exposed by a reliable public Windows API. The app therefore treats an active
Wi-Fi connection as the risk signal and uses safe mode as the mitigation.

### Safe mode (default: on)

`Settings → Wi-Fi Direct safe mode`.

| Condition | Safe mode ON | Safe mode OFF |
| --- | --- | --- |
| Wi-Fi disconnected | start | start |
| Wi-Fi connected | **blocked**, dialog explains why and opens Windows Wi-Fi settings | warning + explicit risk confirmation, then start |
| Wi-Fi adapter off | blocked with an error | blocked with an error |

Windows has no public API to disconnect an arbitrary Wi-Fi profile silently, so
the dialog offers “Open Wi-Fi settings” instead of pretending to disconnect.

### State machine

```text
Idle → CheckingSupport → Starting → Advertising
                                 ↘ WaitingForConnection ⇄ Connected
     ↘ Error                         ↘ Stopping → Idle
```

The remote page shows this state (`Wi-Fi Direct: advertising/waiting/connected`)
instead of jumping between a QR code and “not started”. A second click is
idempotent: it does not restart the publisher and does not recreate the QR
control.

### Serialization, timeouts, cleanup

* Every operation that changes adapter state (start, stop, listener, QR refresh)
  runs under one `SemaphoreSlim` in `WiFiDirectCoordinator`.
* Start has a 35-second budget; the helper must report `started` within 15
  seconds. Stop waits 4 seconds, then kills the helper.
* `StopAsync` is called when the remote is stopped, the page unloads and the main
  window closes, so no publisher/listener survives the screen.
* Unexpected helper exit becomes `Error` and is surfaced with a retry action.

## Diagnostics

**Settings → Diagnostics → Wi-Fi Direct diagnostics** shows:

* adapter name and radio state (or the driver version when available);
* current Wi-Fi connection with a masked SSID and connectivity level;
* current Wi-Fi Direct state;
* last error;
* a **Create report** button (see [`DIAGNOSTICS.md`](DIAGNOSTICS.md)).

## If the BSOD continues

Kernel crashes cannot be fixed from the application. If `0xA` still occurs:

1. Update the wireless adapter driver from the laptop/board vendor (not only
   Windows Update), then reboot.
2. Update BIOS/UEFI and Windows.
3. Temporarily disable Wi-Fi on the PC while using Wi-Fi Direct, or use a router
   and the normal Wi-Fi remote mode.
4. Collect `%SystemRoot%\Minidump\*.dmp` and the app report ZIP, then contact the
   adapter vendor with the stop code.

## Manual test checklist

- [ ] Wi-Fi disconnected, safe mode on: Wi-Fi Direct starts once; QR does not
      flicker; state shows advertising → waiting → connected.
- [ ] Wi-Fi connected, safe mode on: start is blocked; the dialog opens Windows
      Wi-Fi settings; no helper process is left behind.
- [ ] Wi-Fi connected, safe mode off: risk dialog appears; cancel leaves the
      adapter untouched; continue starts once.
- [ ] Double-click “Start access point” quickly: only one start operation runs.
- [ ] Stop the remote, leave the page, close the app: no `--wifi-direct-helper`
      process remains (Task Manager → Details).
- [ ] Toggle safe mode and reopen diagnostics: the toggle persists and the
      diagnostics card reflects it.
- [ ] Create a report: the ZIP contains logs, `system-info.txt` and no media
      paths, SSIDs, MACs or PINs.

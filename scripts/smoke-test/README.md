# Single-machine smoke test

Everything required to verify a KSPBridge build runs on the same
Windows box as KSP itself: a user-mode mosquitto broker, a
Python HTTP server for the bundled FDO console, a `mosquitto_sub`
window for the wire view, and the canonical browser console.

## What's here

| File | Purpose |
|---|---|
| `mosquitto.conf` | Smoke-test broker config: TCP 1885 + WebSocket 9003, anonymous, no persistence. Alternate ports avoid conflict with the system mosquitto service that's typically running on 1883. |
| `run-smoke.bat` | One-click launcher. Backs up the deployed `Settings.cfg`, writes a localhost override, then opens four windows (broker / subscriber / HTTP server / browser). |
| `cleanup-smoke.bat` | Restores the original `Settings.cfg` after testing. |

## Prerequisites

1. **Mosquitto** installed at `C:\Program Files\mosquitto\` (the
   default Windows install path). The `mosquitto.exe` and
   `mosquitto_sub.exe` binaries are the only ones used.
2. **Python** on PATH (`python -m http.server` serves the FDO
   console). Any 3.x works.
3. **KSPBridge already deployed** to the KSP install — the
   csproj's post-build target handles this automatically when
   you run `dotnet build` or `scripts\make-release.ps1`.

## Running it

From the repo root:

```cmd
scripts\smoke-test\run-smoke.bat
```

Four console windows pop up:

- **broker** — mosquitto running with `-v` so every connection
  and publish is logged. Closes when killed.
- **subscriber** — `mosquitto_sub` printing every payload that
  hits `ksp/telemetry/#`. The wire view of all 18 producers.
- **HTTP server** — `python -m http.server 8000` serving the
  console directory. Browser hits this for the console HTML.
- **browser** — opens to
  `http://localhost:8000/hardscifi-fdo-console.html?broker=ws://localhost:9003`.
  The `?broker=` query-string override is supported by both
  console variants (canonical and -cdn) so the same source file
  serves homelab AND localhost users without edits.

The script then prints a "now launch KSP from Steam" message
and exits. Launch KSP normally; within a few seconds of the
main menu, the subscriber window will show the
`_bridge/status` heartbeat, and entering the flight scene
starts all 18 telemetry topics ticking.

## When done

```cmd
scripts\smoke-test\cleanup-smoke.bat
```

This restores `Settings.cfg` from the backup made by
`run-smoke.bat`. The four console windows opened by the
launcher are not killed — close them manually.

## Why alternate ports (1885 / 9003) instead of 1883 / 9002?

A typical Windows mosquitto install runs as a Windows service
on port 1883 with no WebSocket listener. Modifying that
service's config to add a WebSocket listener requires admin
elevation and a service restart. By running our own user-mode
broker on 1885 + 9003, the smoke test needs zero admin
privileges — the system service keeps doing whatever it was
doing on 1883, and our test broker runs on 1885 alongside it.

The ports themselves are arbitrary; the smoke test exercises
the same producer / scheduler / MQTT code paths as a real
homelab deployment. The only thing the port choice changes is
which `localhost:NNNN` you connect to.

## What the smoke test verifies

- Plugin loads successfully on KSP startup
  (`[KSPBridge] plugin loaded, version 0.15.0` in `KSP.log`).
- MQTT connection establishes (`MQTT connected to localhost:1885`).
- `_bridge/status` payload reports the correct version string.
- All 18 telemetry topics tick at their declared rates in the
  flight scene.
- WebSocket bridge works end-to-end (FDO console populates).
- Clean shutdown publishes `online: false` within the 500 ms
  budget rather than relying on LWT.

See `docs/RELEASING.md` for the full smoke-test acceptance
checklist (per-topic spot checks, KSP.log greps, etc.).

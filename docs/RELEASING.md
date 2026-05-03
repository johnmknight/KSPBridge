# Release process

Maintainer guide for cutting a KSPBridge release. The mechanical
build / package / publish steps are scripted; the human-only steps
(smoke test, listing submissions) are listed below as a checklist.

## Mechanical steps (scripted)

For a routine bump (no KSP-version-compatibility change):

1. Bump `<Version>`, `<AssemblyVersion>`, `<FileVersion>` in
   `src/KSPBridge/KSPBridge.csproj`.
2. Bump the `Version` constant in `src/KSPBridge/Plugin.cs`.
3. Add an entry at the top of `CHANGELOG.md` summarising the
   release.
4. Run `pwsh scripts/make-release.ps1`. This regenerates
   `GameData/KSPBridge/KSPBridge.version` from csproj, builds
   Release, validates the expected DLLs are present, and writes
   the zip to `_release/KSPBridge-v<version>.zip` in the standard
   KSP-mod layout.
5. Commit everything (csproj, Plugin.cs, CHANGELOG.md, the
   regenerated `KSPBridge.version`).
6. Push `master`, tag `v<version>`, push the tag.
7. `gh release create v<version> --notes-file <notes> <zip>` to
   publish on GitHub.

If the KSP version range changes (for a future KSP 1.13 etc.),
also update the `KSP_VERSION_MIN` / `KSP_VERSION_MAX` literals in
`scripts/make-release.ps1`'s JSON template, and update the
`ksp_version_min` / `ksp_version_max` fields in
`.ckan/KSPBridge.netkan` then PR the netkan back to
[CKAN-meta](https://github.com/KSP-CKAN/CKAN-meta).

## Smoke test (human, required before announcing)

KSPBridge has no automated runtime tests — the build verifies it
compiles against KSP's API, but it can't catch null-reference
spam or broken telemetry math. Before announcing a release on
the forum or in CKAN, run the manual smoke test below.

### 1. Verify the deployed install

```powershell
Get-ChildItem 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\GameData\KSPBridge' -Recurse |
    Select-Object FullName, Length, LastWriteTime
```

Expected after `make-release.ps1` runs: KSPBridge.dll +
MQTTnet*.dll in `Plugins/`, and Settings.cfg + KSPBridge.version
at the mod folder root.

### 2. Start the broker on appserv1

The homelab broker is configured for KSPBridge on TCP 1883 +
WebSocket 9002. Confirm it's reachable:

```powershell
Test-NetConnection -ComputerName appserv1.local -Port 1883
Test-NetConnection -ComputerName appserv1.local -Port 9002
```

Both should report `TcpTestSucceeded : True`.

### 3. Subscribe to all topics

From any machine that can reach the broker:

```bash
mosquitto_sub -h appserv1.local -p 1883 -t 'ksp/telemetry/#' -v
```

Leave it running. Initially you should see one
`ksp/telemetry/_bridge/status` message (the retained heartbeat
from a prior run) and nothing else.

### 4. Launch KSP

Start KSP. Within a few seconds of the main menu loading, the
subscriber should see a fresh `_bridge/status` payload:

```
ksp/telemetry/_bridge/status {"online":true,"version":"0.15.0","ts":...}
```

The `version` field MUST match the released version. If it
doesn't, the deployed DLL is from a stale build — re-run
`scripts/make-release.ps1` and copy `Plugins/*.dll` into the
KSP install (or just rebuild — the post-build target
auto-deploys).

### 5. Enter the flight scene

Load any saved vessel or quick-launch a stock craft. The
subscriber should immediately start receiving:

- 10 Hz: `vehicle`, `navigation`, `attitude`, `state_vectors`
  (and `target/*` if a target is selected)
- 5 Hz: `dynamics`, `atmosphere`
- 2 Hz: `orbit`, `parent_body`, `maneuver`, `encounter`,
  `performance`, `resources`
- 1 Hz: `situation`, `staging` (retained), `docking/context`
  (retained, only when controlling from a docking port)

### 6. Spot-check the new v0.15 topics

Each of the five v0.15 topics has a quick way to validate:

- **`dynamics`** — pump WASD; `bodyRatePitch` / `bodyRateYaw` /
  `bodyRateRoll` should respond. After a vessel switch, the
  `angularAccel*` fields should be 0 for one tick (suppression
  check), then resume normal values.
- **`resources`** — current LiquidFuel `amount` should drop
  during a burn. After staging away a tank, that tank's parts
  disappear and the LiquidFuel `amount` and `maxAmount` both
  decrease accordingly.
- **`situation`** — at the launchpad, `prelaunch: true` and
  everything else `false`. After lift-off, `flying: true`. In
  orbit, `orbiting: true`. The string `situation` field on this
  topic should match `vehicle.situation`.
- **`atmosphere`** — at sea level on Kerbin, `staticPressure`
  ≈ 101.325 (kPa). At ~70 km, `density` ≈ 0. Above
  `atmosphereDepth` (Kerbin: 70000 m), `inAtmosphere: false`.
- **`staging`** — on a multi-stage rocket, `currentStage`
  starts high. Press space; `currentStage` decrements;
  `partsInNextStage` updates to reflect what fires next.

### 7. Check KSP.log

```powershell
Select-String -Path 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\KSP.log' -Pattern 'KSPBridge'
```

Expected lines: plugin loaded version banner, broker target,
"MQTT connected", scheduler started message, no `producer 'X'
failed:` lines. Any producer-failed line is a regression and
needs a fix before announcing.

### 8. Clean shutdown

Quit KSP via the main menu. Subscriber should receive a final
`{"online":false,...}` status. If you instead see a delay of
about 7-8 seconds before the offline status (the LWT firing),
the in-process Dispose path missed its budget — investigate
before announcing.

## Listing on Spacedock (manual)

[Spacedock](https://spacedock.info/) is the major
non-CKAN-managed mod host. Listing there gives KSPBridge a
non-GitHub home page that KSP players actually browse.

1. Sign in to spacedock.info (account, not GitHub OAuth).
2. Click "Add a mod".
3. Fields:
   - **Name:** KSPBridge
   - **Game version:** 1.12.5
   - **License:** MIT
   - **Short description:** "MQTT telemetry bridge for KSP
     1.12.5 — publishes vessel state over MQTT for browsers,
     scripts, ESP32s, Grafana, or any subscriber."
   - **Source code URL:** https://github.com/johnmknight/KSPBridge
4. On the next screen, upload `KSPBridge-v0.15.0.zip` from the
   GitHub release page.
5. Spacedock will propagate to the homepage's "New mods" feed
   automatically.

For subsequent releases: Spacedock has a per-mod "Add new
version" button — upload the new zip, fill in the changelog
(can copy from CHANGELOG.md).

## CKAN submission (one-time)

See `.ckan/README.md` for the full submission walkthrough. TL;DR:

1. Fork `KSP-CKAN/CKAN-meta`.
2. Copy `.ckan/KSPBridge.netkan` from this repo into
   `NetKAN/N/KSPBridge/` of the fork.
3. Open a PR.
4. Once merged, the CKAN inflater bot auto-publishes new
   GitHub releases.

## Forum thread (optional)

The KSP forum at [forum.kerbalspaceprogram.com](https://forum.kerbalspaceprogram.com/)
is where the KSP modding community discusses releases. A
forum thread is not technically required (CKAN and Spacedock
both work without one) but it's the standard place to point
users for support.

Suggested first post structure:

- Title: `[1.12.5] KSPBridge v<version> — MQTT telemetry bridge`
- Description: lift the abstract from `.ckan/KSPBridge.netkan`
- Topic list: lift from `docs/TOPICS.md` summary
- Download links: GitHub release, Spacedock (once listed),
  CKAN identifier (once listed)
- Link to source repo for issue tracking

For each subsequent release: reply to the thread with the
CHANGELOG entry; update the first post's version + download
links (most forum software lets you edit the OP indefinitely).

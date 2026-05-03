# Changelog

All notable changes to KSPBridge are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.15.0] — 2026-05-03

First release with full KSA-Bridge schema parity. Eighteen telemetry
producers shipping.

### Added

- `dynamics` topic (5 Hz) — body-frame angular velocity (pitch / yaw /
  roll rates), body-frame linear acceleration, finite-differenced
  angular acceleration, and current g-load (from `Vessel.geeForce` so
  it matches the stock G-meter).
- `resources` topic (2 Hz) — wet / dry / resource mass plus a
  per-resource breakdown aggregated across every part on the vessel.
  All mass-bearing fields in kilograms.
- `situation` topic (1 Hz) — the eight `Vessel.situation` enum values
  expanded into individual booleans plus derived flags `onSurface`,
  `inAtmosphere`, `controllable`.
- `atmosphere` topic (5 Hz) — atmospheric density, static and dynamic
  pressure, ambient and external (heated) temperature, Mach number,
  and parent-body atmosphere context flags.
- `staging` topic (1 Hz, retained) — what fires on the next staging
  event: parts-, engine-, and decoupler-counts plus part display
  titles for both the next-to-fire stage and the one after that.
- `scripts/make-release.ps1` — KSP-mod-standard release zip builder
  driven from csproj's `<Version>` property.
- `GameData/KSPBridge/KSPBridge.version` — KSP-AVC / CKAN compatibility
  metadata. Regenerated on every release by `make-release.ps1`.
- README "Installing" section with Steam path hint, extract step,
  Settings.cfg edit, and verification commands (KSP.log grep,
  `mosquitto_sub`).

### Changed

- Release zip now follows the standard KSP-mod packaging convention
  (used by Spacedock and CKAN): `LICENSE` and `THIRD-PARTY-NOTICES.md`
  live inside `GameData/KSPBridge/` rather than at the zip root, so
  extraction does not litter the user's KSP install.
- `Settings.cs` default for `BrokerPort` corrected from 1884 to 1883
  (matching the broker actually exposed on `appserv1.local`).
- `Settings.cfg` comment line about the homelab broker corrected from
  "1884 for MQTT and 9001 for WebSocket" to "1883 for MQTT and 9002
  for WebSocket" — what's actually live.
- `Plugin.Version` runtime constant aligned with `csproj` (`0.15.0`);
  `_bridge/status` payloads now report the matching version string.

## [0.14.0] — 2026-05-03

First public GitHub release. Thirteen telemetry producers.

### Added

- `target/vehicle`, `target/state_vectors`, `target/attitude` topics
  (10 Hz) mirroring the active-vessel schemas for the selected target.
  Schemas are byte-for-byte identical to the active-vessel topics, so
  consoles built for the active vessel work for the target with zero
  parsing changes.
- `docking/context` topic (1 Hz, retained) — docking lifecycle: own /
  target docking-port `persistentId` + module index plus a coarse
  engagement state (`idle` / `armed` / `soft_dock` / `hard_dock` /
  `disabled`).
- `IRetainable` companion interface — opt-in MQTT retention for
  producers whose payloads describe state rather than streaming
  telemetry.
- `rotationX/Y/Z/W` fields on the `attitude` topic — full Unity
  quaternion of the vessel root transform, lets external 3D renderers
  pose a vessel without re-deriving rotation from heading/pitch/roll.

### Pre-0.14 history (tagged but never released to GitHub)

- **v0.13.0** — added the `target/*` trio.
- **v0.12.0** — added quaternion attitude.
- **v0.11.0** — unified multi-contour body pipeline for the bundled
  hard-scifi FDO console (Tier 1 / Tier 2 / special-case Jool).
- **v0.10.0** — `performance` topic (ΔV, TWR, mass, thrust),
  body-aware globe rendering in the console.
- **v0.8.0** — initial commit. Eight producers: `vehicle`,
  `navigation`, `attitude`, `state_vectors`, `orbit`, `parent_body`,
  `maneuver`, `encounter`.

[0.15.0]: https://github.com/johnmknight/KSPBridge/releases/tag/v0.15.0
[0.14.0]: https://github.com/johnmknight/KSPBridge/releases/tag/v0.14.0

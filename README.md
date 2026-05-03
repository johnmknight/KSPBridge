# KSPBridge

MQTT telemetry bridge for Kerbal Space Program 1.12.5. A KSP 1.x companion to
[KSA-Bridge](https://github.com/johnmknight/KSA-Bridge): the plugin publishes
vessel telemetry from inside the game over MQTT so browsers, scripts, ESP32s,
Grafana, or any MQTT subscriber can consume real-time flight data. Wire format
matches KSA-Bridge's topic names and JSON schemas, so consoles and dashboards
built for one game work against the other with only a topic-prefix change.

## Status — v0.15.0

**Eighteen telemetry topics shipping — full KSA-Bridge schema parity.**
The hard-scifi FDO console included in this repo lights up end-to-end:
vessel identification, orbital elements, state vectors, navigation
readouts, attitude (with full Unity quaternion), maneuver plan, SOI
encounters, performance (ΔV / TWR), and live planet rotation all
populate from real game data.

v0.15 closes out the original KSA-Bridge schema backlog by adding the
last five planned topics:

- **`dynamics`** (5 Hz) — body-frame angular velocity (pitch/yaw/roll
  rates), body-frame linear acceleration, finite-differenced angular
  acceleration, and current g-load (sourced from `Vessel.geeForce` so
  it matches the stock G-meter exactly).
- **`resources`** (2 Hz) — wet/dry/resource mass plus a per-resource
  breakdown aggregated across every part on the vessel. One entry per
  resource type (LiquidFuel, Oxidizer, MonoPropellant, ElectricCharge,
  …) with current amount, capacity, density, and pre-computed mass.
- **`situation`** (1 Hz) — the eight `Vessel.situation` enum values
  expanded into individual booleans (`landed`, `splashed`,
  `prelaunch`, `flying`, `subOrbital`, `orbiting`, `escaping`,
  `docked`) plus derived flags (`onSurface`, `inAtmosphere`,
  `controllable`).
- **`atmosphere`** (5 Hz) — atmospheric density, static and dynamic
  pressure, ambient and external (heated) temperature, Mach number,
  and parent-body atmosphere context flags.
- **`staging`** (1 Hz, retained) — what fires on the next staging
  event: parts-, engine-, and decoupler-counts plus part display
  titles for both the next-to-fire stage and the one after that.

Since v0.11 the bridge has also added:

- **Quaternion attitude.** The `attitude` payload now carries
  `rotationX/Y/Z/W` — the vessel root transform's full Unity
  quaternion. External 3D renderers can pose a vessel as a rigid body
  without re-deriving rotation from heading/pitch/roll.
- **Target-vessel telemetry.** `target/vehicle`, `target/state_vectors`,
  and `target/attitude` mirror the active-vessel topics whenever a
  vessel-bearing target is selected. Schemas are byte-for-byte
  identical to the active-vessel versions, so consoles built for the
  active vessel work for the target with zero parsing changes.
- **Docking context.** `docking/context` is a retained, 1 Hz lifecycle
  topic for docking scenarios. It reports controlling/target docking
  ports by `persistentId` + module index, plus a coarse engagement
  state (`idle` / `armed` / `soft_dock` / `hard_dock` / `disabled`).
  Late subscribers see the current state immediately thanks to MQTT
  retention.

The console is **body-aware** for every stock world. When the active vessel
transitions between SOIs, the globe swaps to the appropriate body's vector
layers automatically. v0.11 unified every body under one multi-contour
pipeline (`scripts/build_body_contours.py`):

- **Tier 1 — rich-feature bodies:** Kerbin, Eve, Moho, Ike, Tylo, Vall,
  Laythe — each gets 3–6 traced contour layers with a per-body colour ramp
  (deep ocean → highland peaks for Kerbin; rust basins → tan peaks for
  Duna; purple valleys → bright highlands for Eve; etc.).
- **Tier 2 — uniform bodies:** Gilly, Bop, Pol, Minmus, Eeloo, Dres — 1–4
  traced layers showing their principal features.
- **Special — Jool:** synthetic horizontal cloud bands since it's a gas
  giant. No image tracing; the bands are emitted directly by the script.
- **Mun, Duna:** retain their original dedicated pipelines; both still use
  the same `bodyLayersFromContours()` renderer on the console side.

Surface sources come from the stock game plus biome maps pulled from the
[JNSQ](https://github.com/Galileo88/JNSQ) mod for Laythe and Minmus. The
Python pipeline converts every source image into a per-body `*_contours.js`
asset with traced polygon rings and a colour palette, then the FDO console's
`BODY_REGISTRY` wires each one to the globe renderer.

All KSA-Bridge schema topics now have producers. See
[docs/TOPICS.md](docs/TOPICS.md) for the full authoritative schema
reference for every published topic.

## Installing

The simplest path is to grab a built release from the GitHub Releases
page rather than building from source. The release zip follows the
standard KSP mod layout — extract it into your KSP install root and the
plugin lands in the right place.

1. **Download** the latest `KSPBridge-vX.Y.Z.zip` from
   [Releases](https://github.com/johnmknight/KSPBridge/releases).
2. **Extract into your KSP install root** — the folder containing
   `KSP_x64.exe`. On a default Steam install this is
   `C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program\`.
   The zip's `GameData/KSPBridge/` folder will land alongside KSP's
   built-in `GameData/Squad/` (and any other mods).
3. **Edit `GameData/KSPBridge/Settings.cfg`** to point at your MQTT
   broker. The shipped defaults (`appserv1.local:1883`, topic prefix
   `ksp/telemetry`) target the author's homelab — almost certainly
   not yours. See [Configuration](#configuration) below.
4. **Launch KSP.** The plugin connects to the broker on startup and
   begins publishing as soon as you enter the flight scene.

### Verifying it works

Two quick checks. First, KSP's own log:

```
<KSP root>\KSP.log
```

Look for `[KSPBridge]` lines — you should see the version banner, the
broker target, and `MQTT connected to <host>:<port>`. If you see
`MQTT connect failed:` instead, fix `Settings.cfg` and relaunch.

Second, from any machine that can reach your broker, watch the topics:

```
mosquitto_sub -h <broker_host> -p <broker_port> -t 'ksp/telemetry/#' -v
```

In the flight scene you should see all 18 topics ticking on their
respective rates. `_bridge/status` is retained — even outside the
flight scene a fresh subscriber should see at least the heartbeat.

### Upgrading

The release zip contains both the plugin DLLs and a reference
`Settings.cfg`. If you're upgrading and have already customised your
broker settings, **back up your `Settings.cfg` before extracting** —
Windows extraction tools overwrite by default and will silently clobber
your edits. Alternatively, extract only the `GameData/KSPBridge/Plugins/`
subfolder and skip the rest of the archive.

If you're building from source instead, the `dotnet build` post-build
target only deploys `*.dll`s; your existing `Settings.cfg` is preserved
across rebuilds.

## Architecture at a glance

```
KSP 1.12.5 (Unity 2019.4, .NET Framework 4.7.1)
   │
   ├── KSPBridge.dll              Our plugin
   │   ├── KSPBridgePlugin        KSPAddon entry point, heartbeat pump
   │   ├── Settings               Loads GameData/KSPBridge/Settings.cfg
   │   ├── Mqtt.MqttBridge        ManagedMqttClient wrapper, LWT, reconnect
   │   └── Telemetry.TelemetryScheduler
   │       └── Producers/         One ITelemetryProducer per topic
   │
   ├── MQTTnet.dll                Third-party, MIT
   └── MQTTnet.Extensions.ManagedClient.dll
                │
                │ TCP  1883
                ▼
           Mosquitto 2.0 (Docker, homelab)
                │ WebSocket  9002
                ▼
      consoles/hard-scifi/*.html   Browser console
                                  (Three.js + MQTT.js)
```

Scheduler runs at a fixed 10 Hz tick on Unity's main thread; each producer
declares a `RateDivisor` so 10 Hz, 5 Hz, and 2 Hz topics all fire from a
single beat. One try/catch per producer keeps a bad payload from starving
the rest. See
[src/KSPBridge/Telemetry/TelemetryScheduler.cs](src/KSPBridge/Telemetry/TelemetryScheduler.cs)
for the full implementation.

## Project layout

```
KSPBridge/
  src/KSPBridge/              C# plugin source (.NET Framework 4.7.1)
    KSPBridge.csproj
    Plugin.cs                 KSPAddon entry point
    Settings.cs
    Mqtt/
      MqttBridge.cs
    Telemetry/
      ITelemetryProducer.cs   Interface contract
      TelemetryScheduler.cs   10Hz tick + rate division
      VehicleTelemetry.cs     POCOs per topic
      OrbitTelemetry.cs
      ... (one per topic)
      Producers/              One class per topic
        VehicleProducer.cs
        OrbitProducer.cs
        ... (one per topic)

  GameData/KSPBridge/         Deployment-shaped folder
    Plugins/                  Build populates this
    Settings.cfg              Broker host/port/prefix

  consoles/hard-scifi/        Browser FDO console
    hardscifi-fdo-console.html
    lib/                      Three.js, MQTT.js, topojson-client
    data/                     Planet topojson/geojson assets

  docs/
    TOPICS.md                 Full schema reference

  THIRD-PARTY-NOTICES.md
  LICENSE (MIT)
```

## Building

Requires Visual Studio 2022 or 2026 with the .NET desktop development
workload and the .NET Framework 4.7.1 targeting pack installed.

1. Open `KSPBridge.sln`.
2. Build → Build Solution (`Ctrl+Shift+B`).
3. Post-build automatically deploys the built DLLs to
   `<KSP install>\GameData\KSPBridge\Plugins\`.
4. On first install, also copy `GameData/KSPBridge/Settings.cfg` from this
   repo to the matching folder in your KSP install. Subsequent builds will
   NOT overwrite it, so your edits persist.

### KSP install path

The csproj defaults `KSPRoot` to
`C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`. If
your install is elsewhere, override via an env var named `KSPRoot` or add
a `Directory.Build.props` next to the sln setting the property.

## Configuration

Edit `GameData/KSPBridge/Settings.cfg` in your KSP install:

```
KSPBRIDGE
{
    broker_host  = appserv1.local
    broker_port  = 1883
    topic_prefix = ksp/telemetry
    client_id    = kspbridge
}
```

Defaults target the author's homelab. Change `broker_host` and `broker_port`
to match your own Mosquitto (or other MQTT broker).

## Browser console

The bundled `consoles/hard-scifi/hardscifi-fdo-console.html` is an FDO-style
mission-control dashboard adapted from KSA-Bridge. Open it via a local HTTP
server (file:// breaks texture loads in most browsers) — connects to the
homelab Mosquitto broker over WebSocket at `ws://appserv1.local:9002` and
subscribes to `ksp/telemetry/#`. Three.js draws the planet and orbit; the
panels pull live data from the topics documented in
[docs/TOPICS.md](docs/TOPICS.md).

Quick start:

```powershell
cd consoles/hard-scifi
python -m http.server 8000
# then open http://localhost:8000/hardscifi-fdo-console.html
```

### Per-body assets

The globe rendering switches dynamically based on the `parent_body` topic.
Each body's data lives next to the console:

```
consoles/hard-scifi/
  data/
    kerbin_mask.png + kerbin_coastline.js   (full coastlines + ice rings)
    mun_mask.png + mun_basin.js             (basin outlines)
    duna_mask.png + duna_ice.js             (multi-level topo contours + ice)
  source/
    bodies/                                  (raw body maps awaiting wire-up)
```

To add a new body's detailed map:

1. Drop the equirectangular surface image into `source/bodies/<body>.{jpg,png}`.
2. Run `python scripts/build_kerbin_topojson.py --body <body> --input ... --js-output ... --mask-png ...` (Kerbin / Mun / Duna pipelines exist; new bodies typically need a small dedicated pipeline branch tuned to their palette).
3. Add a `<script src="...">` tag to the console HTML head.
4. Add an entry to `BODY_REGISTRY` in the console's JS.

Bodies without detailed assets render as flat-coloured spheres via
`BODY_FALLBACK_COLORS` (every stock body has a tuned colour).

## Compatibility with KSA-Bridge consumers

The plugin's wire format deliberately mirrors KSA-Bridge's. Field names,
JSON shapes, unit conventions (metres / seconds / radians), and topic
suffixes match. The only structural difference is the topic prefix
(`ksp/telemetry/*` vs `ksa/telemetry/*`). Any KSA-Bridge console or
subscriber can be pointed at KSPBridge with a single string change.

KSPBridge adds two fields not present in KSA-Bridge:

- `id` — KSP `Vessel.id` as a Guid string, stable across save/load.
- `persistentId` — KSP `Vessel.persistentId` as a uint, stable within a save.

Both are emitted on every topic payload so consumers can correlate data
across topics for a specific vessel (vessel names alone are not unique).

## License

MIT. See [LICENSE](LICENSE). Bundled third-party libraries listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

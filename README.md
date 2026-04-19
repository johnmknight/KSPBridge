# KSPBridge

MQTT telemetry bridge for Kerbal Space Program 1.12.5. A KSP 1.x companion to
[KSA-Bridge](https://github.com/johnmknight/KSA-Bridge): the plugin publishes
vessel telemetry from inside the game over MQTT so browsers, scripts, ESP32s,
Grafana, or any MQTT subscriber can consume real-time flight data. Wire format
matches KSA-Bridge's topic names and JSON schemas, so consoles and dashboards
built for one game work against the other with only a topic-prefix change.

## Status — v0.10.0

**Nine telemetry topics shipping.** The hard-scifi FDO console included in
this repo lights up end-to-end: vessel identification, orbital elements,
state vectors, navigation readouts, attitude, maneuver plan, SOI encounters,
performance (ΔV / TWR), and live planet rotation all populate from real game
data.

The console is now **body-aware**: when the active vessel transitions between
SOIs, the globe swaps to the appropriate body's mask and vector layers
automatically. Detailed surface assets ship for **Kerbin** (coastlines + ice
caps + filled continents), **Mun** (basin contours + grey terrain), and
**Duna** (multi-level topographic contours + polar caps). Every other stock
body falls back to a flat sphere in that body's signature colour
(Eve = purple, Jool = green, Eeloo = pale, etc.).

Source map images for the remaining bodies (Moho, Eve, Gilly, Minmus, Ike,
Dres, Jool, Laythe, Vall, Tylo, Bop, Pol) are stashed in
`consoles/hard-scifi/source/bodies/` ready to be wired up via the same Python
pipeline used for Mun and Duna.

Topics still on the backlog (no producers yet):

- `dynamics` — body rates, linear & angular acceleration
- `resources` — propellant masses, total vehicle mass
- `situation` — expanded landed/splashed/flying bit flags
- `atmosphere` — density, static pressure, temperature

See [docs/TOPICS.md](docs/TOPICS.md) for the full authoritative schema
reference for every currently published topic.

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

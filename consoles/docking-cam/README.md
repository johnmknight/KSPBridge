# Docking Cam Console

Browser-based external docking view for KSPBridge v0.14+ and
KSPEVU v0.10+. Renders own ship and target ship from MQTT telemetry
and overlays a Crew-Dragon-inspired HUD (concentric reticle,
dual-tier cardinal readouts, flight-director chevrons).

## Status -- v1

Diagnostic-first first cut. Renders the two vessels as labelled boxes
posed from telemetry, not from KSPEVU glbs. Purpose: verify the
telemetry infrastructure end-to-end before adding the mesh-loading
pipeline. A visible debug panel at the bottom shows every observed
topic's latest payload and freshness; toggle with `d`.

Known gaps:
- Boxes instead of actual vessel meshes (KSPEVU glb loader -- next pass)
- Docking-port anchors not yet consumed from `extras.dockingPorts`
- No corner warning icons populated (sun-in-FOV, comms, etc.)
- No ground-truth axes/gizmo for sanity-checking quaternion conversion

## Running

Like the FDO console, this is a static HTML page that speaks MQTT over
WebSocket. Needs to be served over HTTP (not `file://`) for the module
imports to work. From this directory:

```powershell
python -m http.server 8000
# then open http://localhost:8000/docking-cam-console.html
```

Override the broker URL via `?broker=` if you're not on the homelab:

```
http://localhost:8000/docking-cam-console.html?broker=ws://localhost:9002
```

Default broker is `ws://appserv1.local:9002` to match KSPBridge's
hard-scifi FDO console.

## Topics consumed

From `ksp/telemetry/`:
- `vehicle`, `state_vectors`, `attitude` -- own ship
- `target/vehicle`, `target/state_vectors`, `target/attitude` -- target ship
- `docking/context` -- lifecycle state (retained)

From `ksp/vessel_model/`:
- `ready` -- KSPEVU glb URLs (wired for future glb loading)

All topic shapes are documented in `docs/TOPICS.md`.

## Key conventions

- Coordinate conversion Unity-LH -> Three.js-RH happens in one
  place (`vecUnityToThree`, `quatUnityToThree`). Applied consistently
  to every position and quaternion received from the bridge.
- Rate computation for the dual-tier readouts caches the previous
  raw value per metric; first observation returns 0 rate.
- MQTT reconnect is handled by mqtt.js automatically (2 s period).
  Connection pill top-center turns red on disconnect.

## Keyboard

- `d` -- toggle the debug panel

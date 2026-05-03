# KSPBridge Topic Reference (v0.14.0)

All topics are published under the prefix configured in `Settings.cfg`
(default `ksp/telemetry`). Examples below use that default.

Field naming is **camelCase** to match KSA-Bridge's wire format.
Linear distances are **metres**, masses **kilograms**, times **seconds**,
angles **radians**. Per-payload deviations from these defaults are noted
explicitly.

Every payload carries two correlation keys:

- `id` — `Vessel.id` Guid as a string, e.g. `"5f3e8a2c-9b1d-4a37-8e6f-..."`.
  Stable across save/load. Globally unique.
- `persistentId` — `Vessel.persistentId` uint, e.g. `2851634927`. Stable
  across save/load. Unique within a save.

These two fields appear in every topic table below but are not repeated
in each row description.

---

## `_bridge/status` — 1 Hz (published from bridge, not vessel state)

Broker-retained liveness signal. Published on connect, periodically while
alive, and finally on disconnect (clean shutdown or via Last-Will-and-
Testament on unclean disconnect).

| Field | Type | Meaning |
|---|---|---|
| `online` | bool | `true` while the bridge is connected; `false` on goodbye / LWT. |
| `version` | string | Plugin version string, e.g. `"0.14.0"`. |
| `ts` | number | Unix epoch seconds at publish time. |

---

## `vehicle` — 10 Hz

Identifies the active vessel plus a cheap summary of its current state.

| Field | Type | Units | Source | Meaning |
|---|---|---|---|---|
| `vehicleName` | string | — | `Vessel.vesselName` | Display name (user-editable, NOT unique). |
| `parentBody` | string | — | `Vessel.mainBody.bodyName` | Current SOI body. |
| `situation` | string | — | `Vessel.situation.ToString()` | One of LANDED / SPLASHED / PRELAUNCH / FLYING / SUB_ORBITAL / ORBITING / ESCAPING / DOCKED. |
| `speed` | number | m/s | `Vessel.obt_velocity.magnitude` | Orbital (inertial-frame) speed. Matches KSA-Bridge's `speed` semantics on the vehicle topic. |

---

## `navigation` — 10 Hz

Pilot-readout values. `speed` here is **surface** speed (rotating frame),
which differs from `vehicle.speed` (orbital / inertial).

| Field | Type | Units | Source |
|---|---|---|---|
| `altitude` | number | m | `Vessel.altitude` (above mean surface) |
| `altitudeKm` | number | km | `altitude / 1000` |
| `speed` | number | m/s | `Vessel.srf_velocity.magnitude` (surface frame) |
| `orbitalSpeed` | number | m/s | `Vessel.obt_velocity.magnitude` (inertial frame) |

---

## `attitude` — 10 Hz

Vessel pointing angles relative to the local surface reference frame (north
/ east / up constructed at the vessel's position). All three angles are in
**radians**. Heading/pitch are numerically robust near the zenith; roll is
computed via a cross-product form that avoids Euler gimbal lock at
pitch = ±π/2.

| Field | Type | Range | Meaning |
|---|---|---|---|
| `heading` | number | `[0, 2π)` | Compass bearing of the nose, 0 = north, clockwise. |
| `pitch` | number | `[-π/2, π/2]` | Nose elevation above horizon; +π/2 straight up. |
| `roll` | number | `(-π, π]` | Rotation about nose axis; 0 = wings level, + = right wing down. |
| `rotationX` | number | `[-1, 1]` | Vessel root-transform quaternion X, Unity world frame. |
| `rotationY` | number | `[-1, 1]` | Vessel root-transform quaternion Y, Unity world frame. |
| `rotationZ` | number | `[-1, 1]` | Vessel root-transform quaternion Z, Unity world frame. |
| `rotationW` | number | `[-1, 1]` | Vessel root-transform quaternion W, Unity world frame. |

`rotationX/Y/Z/W` is `Vessel.transform.rotation` as a unit quaternion — the
rotation of the vessel's root part in Unity's world frame. This is **separate
from and not derivable from** `heading`/`pitch`/`roll`, which describe nose
direction in the local surface frame. Use the quaternion to pose the whole
vessel as a rigid body in an external 3D renderer (docking cam, external
viewer). KSPEVU's glb is organised relative to this same root transform, so
applying the quaternion to the glb's root node poses every part correctly.

Handedness matches `state_vectors`: Unity left-handed, y-up. Consumers
swapping to right-handed axes must convert the quaternion consistently with
their position swap — the naive elementwise axis swap used for position
vectors does not produce a valid rotation quaternion.

---

## `orbit` — 2 Hz

Full Keplerian set plus convenience and parent-body bulk properties. KSP
exposes inclination / LAN / argumentOfPeriapsis in degrees; the producer
converts to radians before emitting.

| Field | Type | Units | Meaning |
|---|---|---|---|
| `apoapsis` | number | m | Apoapsis radius from parent centre. |
| `periapsis` | number | m | Periapsis radius from parent centre. |
| `apoapsisElevation` | number | m | Apoapsis altitude above mean surface. |
| `periapsisElevation` | number | m | Periapsis altitude above mean surface. |
| `eccentricity` | number | — | 0 = circular, <1 closed, =1 parabolic, >1 hyperbolic. |
| `inclination` | number | rad | — |
| `longitudeOfAscendingNode` | number | rad | — |
| `argumentOfPeriapsis` | number | rad | — |
| `period` | number | s | Orbital period; undefined for unbound orbits. |
| `semiMajorAxis` | number | m | Signed; negative for hyperbolic. |
| `semiMinorAxis` | number | m | `|a| · sqrt(|1 − e²|)` — defined for both elliptical and hyperbolic cases. |
| `timeToApoapsis` | number | s | — |
| `timeToPeriapsis` | number | s | — |
| `orbitType` | string | — | CIRCULAR / ELLIPTICAL / PARABOLIC / HYPERBOLIC, derived from `e`. |
| `parentRadius` | number | m | Parent body mean radius. |
| `parentMass` | number | kg | Parent body mass. |

---

## `state_vectors` — 10 Hz

Vessel position and velocity in the parent-body-centred inertial (CCI)
frame. Handedness follows KSP's Unity world frame as-is (left-handed, y-up);
consumers rendering in right-handed frames (e.g. Three.js default) should
apply a handedness swap at receive time — the hard-scifi console uses
`x→x, z→y, -y→z`.

| Field | Type | Units | Source |
|---|---|---|---|
| `positionX` | number | m | `(Vessel.GetWorldPos3D() − mainBody.position).x` |
| `positionY` | number | m | same, `.y` |
| `positionZ` | number | m | same, `.z` |
| `velocityX` | number | m/s | `Vessel.obt_velocity.x` |
| `velocityY` | number | m/s | same, `.y` |
| `velocityZ` | number | m/s | same, `.z` |

---

## `parent_body` — 2 Hz

Live state of the celestial body the vessel is currently orbiting. The
rotation quaternion drives ground-track rendering in the console.

| Field | Type | Units | Meaning |
|---|---|---|---|
| `bodyName` | string | — | e.g. "Kerbin", "Mun", "Duna". |
| `radius` | number | m | Mean radius. |
| `mass` | number | kg | — |
| `rotationPeriod` | number | s | Sidereal period; negative if retrograde. |
| `rotationQuatX` | number | — | Unity `transform.rotation.x`. |
| `rotationQuatY` | number | — | Unity `transform.rotation.y`. |
| `rotationQuatZ` | number | — | Unity `transform.rotation.z`. |
| `rotationQuatW` | number | — | Unity `transform.rotation.w`. |
| `axialTilt` | number | rad | Angle between spin axis and the normal to the body's own orbital plane. 0 for stock Kerbin; 0 for root bodies without an orbital parent. |

---

## `maneuver` — 2 Hz

Patched-conic flight-plan summary. Reads `Vessel.patchedConicSolver.maneuverNodes`.

| Field | Type | Units | Meaning |
|---|---|---|---|
| `burnCount` | number | — | Number of currently-planned nodes. |
| `hasActiveBurns` | bool | — | Any node within 30 s of UT now (upcoming or already started). |
| `flightPlanComplete` | bool | — | No remaining future nodes. |
| `nextNodeIn` | number | s | Seconds until the soonest future node. 0 if none. |
| `nextNodeDeltaV` | number | m/s | `|DeltaV|` of the soonest future node. 0 if none. |

---

## `encounter` — 2 Hz

Upcoming SOI transition detected by walking the patched-conic chain. This
is intentionally simpler than true closest-approach math — the console
renders only "there's an encounter" plus the distance at capture.

| Field | Type | Units | Meaning |
|---|---|---|---|
| `hasEncounter` | bool | — | A future patch enters a different parent body's SOI. |
| `closestApproachDistance` | number | m | SOI radius of the body being entered; 0 if no encounter. |
| `encounterBody` | string | — | Name of the body whose SOI will be entered; empty if no encounter. |
| `timeToEncounter` | number | s | Seconds from now to SOI entry. 0 if no encounter. |

---

## `performance` — 2 Hz

Reports propulsive performance figures (ΔV at vacuum / ASL / actual,
current-stage TWR, total mass, current thrust). Uses KSP 1.12's stock
`Vessel.VesselDeltaV` calculator under the hood.

| Field | Type | Units | Meaning |
|---|---|---|---|
| `deltaV` | number | m/s | Total ΔV in current conditions. |
| `deltaVVac` | number | m/s | Total ΔV in vacuum. |
| `deltaVAsl` | number | m/s | Total ΔV at sea level. |
| `twr` | number | — | Current-stage TWR against local gravity. |
| `currentStageDeltaV` | number | m/s | ΔV of the current stage only. |
| `mass` | number | kg | Total wet mass. |
| `thrust` | number | N | Sum of currently-ignited engine thrust. |

NaN / ∞ values from the calculator (transient states like empty stages
or pre-staging) are filtered to 0.

---

## `target/vehicle`, `target/state_vectors`, `target/attitude` - 10 Hz

Target-vessel mirror of the corresponding active-vessel topics. Each
publishes only when the active vessel has a *vessel-bearing* target
selected — i.e., another vessel or a specific `ModuleDockingNode`.
Celestial-body targets and self-targeting are filtered out.

**Payloads are byte-for-byte identical to their active-vessel counterparts.**
A browser console that already parses `vehicle`, `state_vectors`, and
`attitude` can subscribe to the `target/*` variants with zero additional
parsing code — same fields, same units, same semantics.

Silent when no target is selected (no message published, not a zeroed
message). Consumers should treat absence of a recent `target/*` message
as "no target selected" and hide target-related UI accordingly.

Frames of reference:

- `target/state_vectors` position and velocity are in the **target's
  own parent-body** inertial (CCI) frame, NOT the active vessel's. In
  docking scenarios both vessels are always in the same SOI (you can't
  physically dock across an SOI boundary), but general target telemetry
  may see cross-SOI cases. Consumers computing relative pose should
  compare `vehicle.parentBody` vs `target/vehicle.parentBody` before
  subtracting state vectors.
- `target/attitude` HPR fields are in the target's own local surface
  frame and are of limited use to an external observer. The
  `rotationX/Y/Z/W` quaternion is the field that matters for posing
  the target's glb in an external 3D renderer.
- `target/vehicle.speed` is the target's own orbital speed, not the
  closing speed between active and target. Compute closing speed
  client-side from the two state_vectors.

The `id` and `persistentId` correlation fields describe the target
vessel, not the active one — use them to look up the target's KSPEVU
glb at `/vessel/<hash>.glb` when rendering.

---

## `docking/context` - 1 Hz, retained

Lifecycle description of the current docking scenario. Publishes only
when the active vessel is being controlled from one of its docking
ports (i.e., the reference transform is on a part carrying a
`ModuleDockingNode`). Retained on the broker so a late-subscribing
viewer immediately sees the current state without waiting up to a
second for the next scheduled tick.

| Field | Type | Meaning |
|---|---|---|
| `ownVesselId` | string | Active vessel's Guid id (matches `vehicle.id`). |
| `ownVesselPersistentId` | number | Active vessel's persistentId. |
| `ownPortPersistentId` | number | Part persistentId that carries the controlling docking node. Keys into KSPEVU's glb as a part-root node. |
| `ownPortModuleIndex` | number | Index of the controlling `ModuleDockingNode` within its part's `Modules` list. Matches KSPEVU's `extras.dockingPorts[].moduleIndex`. |
| `targetVesselId` | string | Target vessel's Guid id, empty if no target or non-vessel target. |
| `targetVesselPersistentId` | number | Target vessel's persistentId, 0 if none. |
| `targetPortPersistentId` | number | Part persistentId on the target vessel that carries the specific docking node being targeted. 0 if the target is a generic vessel rather than a specific port. |
| `targetPortModuleIndex` | number | Module index of the targeted node within its part, 0 if no specific port targeted. |
| `state` | string | Coarse engagement state. One of: `idle`, `armed`, `soft_dock`, `hard_dock`, `disabled`. See below. |
| `rawState` | string | Raw `ModuleDockingNode.state` from KSP (e.g. `"Docked (docker)"`, `"Acquire (dockee)"`). For debugging / fine-grained UI; not stable across KSP versions. |

### `state` values

- `idle` — controlling from a port, no target or target is a generic vessel
- `armed` — specific target port selected, approach underway but not engaged
- `soft_dock` — magnetic acquire in progress (KSP `Acquire*` states)
- `hard_dock` — physically docked (KSP `Docked*` / `PreAttached`)
- `disabled` — own port is shielded or otherwise unavailable

Absence of a recent `docking/context` message means "not in a docking
scenario" — the viewer should hide docking UI. On transition from a
docking scenario back to a non-docking control reference, the retained
value stays on the broker until the next genuine docking scenario
overwrites it, so consumers should also treat a stale retained message
(via their own freshness check) as equivalent to "no scenario."

### Cross-referencing with KSPEVU

The `ownPortPersistentId` + `ownPortModuleIndex` pair keys into the
target vessel's KSPEVU glb: walk the glb's part-root nodes, match by
`part_<persistentId>_*` node name, read `extras.dockingPorts[moduleIndex]`
for the mating-point pose in part-local coordinates. Same pattern for
the target port on the target vessel's glb.

---

## Not yet implemented

The following topics are defined in the KSA-Bridge schema and may be added
in future versions. They are NOT currently published.

| Topic | Planned rate | Notes |
|---|---|---|
| `dynamics` | 2 Hz | Body rates, linear acceleration, angular acceleration. |
| `resources` | 2 Hz | Per-resource masses (LiquidFuel, Oxidizer, ElectricCharge, etc.). |
| `situation` | 2 Hz | Expanded bit flags (landed / splashed / flying / docked). |
| `atmosphere` | 2 Hz | Static pressure, density, temperature. |

---

## Topic-prefix compatibility with KSA-Bridge

Every schema here mirrors KSA-Bridge's exactly, down to field names and
units. Subscribers that worked with `ksa/telemetry/*` can be pointed at
`ksp/telemetry/*` with only a topic-prefix change; no payload parsing
changes are required. The two KSP-specific correlation fields (`id`,
`persistentId`) are additive — KSA-Bridge consumers that ignore unknown
fields will continue to work unchanged.

# KSPBridge Topic Reference (v0.8.0)

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
| `version` | string | Plugin version string, e.g. `"0.8.0-body-maneuver-encounter"`. |
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

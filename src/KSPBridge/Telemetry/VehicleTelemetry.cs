using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/vehicle</c> topic.
    ///
    /// Field names are camelCase to match KSA-Bridge's wire format exactly,
    /// so the same browser console / dashboard / subscriber code can
    /// consume telemetry from either game with only a topic-prefix change.
    /// KSA-Bridge's authoritative C# source uses anonymous objects
    /// (<c>new { vehicleName = ..., parentBody = ..., speed = ... }</c>)
    /// which <c>System.Text.Json</c> serialises verbatim, fixing camelCase
    /// as the de-facto schema convention for the broader ecosystem.
    ///
    /// JsonUtility constraints worth remembering:
    ///   - Must be marked [Serializable].
    ///   - Must use public fields (not properties).
    ///   - No Dictionary / polymorphic / nullable primitive support.
    /// For our fixed-shape telemetry payloads these constraints are fine.
    /// </summary>
    [Serializable]
    public class VehicleTelemetry
    {
        /// <summary>
        /// KSP's legacy Guid identifier (<see cref="Vessel.id"/>), formatted
        /// as a string. Globally unique across saves and installs. Stable
        /// across save/load. Consumers that need to correlate KSP data with
        /// other universes or across save files should key on this. Field
        /// is KSPBridge-specific — KSA-Bridge does not emit it.
        /// </summary>
        public string id;

        /// <summary>
        /// KSP's newer persistent identifier (<see cref="Vessel.persistentId"/>).
        /// A 32-bit unsigned integer, unique within a single save game and
        /// stable across save/load. Cheaper on the wire than the Guid and
        /// easier to type when filtering in MQTT tools. Field is
        /// KSPBridge-specific — KSA-Bridge does not emit it.
        /// </summary>
        public uint persistentId;

        /// <summary>
        /// Vessel name as set by the player (or default). Display-only —
        /// NOT unique (defaults to "Untitled Space Craft" for many vessels,
        /// and players can manually collide names). Use <see cref="id"/> or
        /// <see cref="persistentId"/> as the correlation key. Matches
        /// KSA-Bridge's <c>vehicleName</c> field.
        /// </summary>
        public string vehicleName;

        /// <summary>
        /// Name of the celestial body whose SOI the vessel is in.
        /// Matches KSA-Bridge's <c>parentBody</c> field.
        /// </summary>
        public string parentBody;

        /// <summary>
        /// KSP vessel situation enum as a string: LANDED, SPLASHED,
        /// PRELAUNCH, FLYING, SUB_ORBITAL, ORBITING, ESCAPING, DOCKED.
        /// String form is more useful to dashboards than the raw int.
        /// </summary>
        public string situation;

        /// <summary>
        /// Magnitude of the vessel's orbital (inertial-frame) velocity in
        /// m/s. KSA-Bridge calls this <c>speed</c> on the vehicle topic
        /// (and <c>orbitalSpeed</c> on the navigation topic) — we follow
        /// their naming so the existing console's <c>d.speed</c> reference
        /// works against us unchanged.
        /// </summary>
        public double speed;
    }
}

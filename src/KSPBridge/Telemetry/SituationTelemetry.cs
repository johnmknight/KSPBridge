using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/situation</c> topic.
    ///
    /// Expands KSP's single <c>Vessel.Situations</c> enum into one
    /// boolean field per state plus a couple of derived flags
    /// consumers commonly want (<see cref="onSurface"/>,
    /// <see cref="inAtmosphere"/>). The string form of the enum is
    /// also emitted on this topic so a subscriber can use this
    /// topic as a self-contained source of situation truth without
    /// also having to subscribe to <c>vehicle</c>.
    ///
    /// At any moment exactly one of <see cref="landed"/>,
    /// <see cref="splashed"/>, <see cref="prelaunch"/>,
    /// <see cref="flying"/>, <see cref="subOrbital"/>,
    /// <see cref="orbiting"/>, <see cref="escaping"/>, and
    /// <see cref="docked"/> is true. Derived flags
    /// (<see cref="onSurface"/>, <see cref="inAtmosphere"/>,
    /// <see cref="controllable"/>) are independent.
    /// </summary>
    [Serializable]
    public class SituationTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// String form of <c>Vessel.situation</c>: one of LANDED,
        /// SPLASHED, PRELAUNCH, FLYING, SUB_ORBITAL, ORBITING,
        /// ESCAPING, DOCKED. Identical to <c>vehicle.situation</c>.
        /// </summary>
        public string situation;

        /// <summary>True when on solid ground.</summary>
        public bool landed;

        /// <summary>True when in liquid (oceans, etc.).</summary>
        public bool splashed;

        /// <summary>True when on the launchpad / runway.</summary>
        public bool prelaunch;

        /// <summary>
        /// True when in atmospheric flight (not on the ground, not
        /// orbiting). Distinct from <see cref="inAtmosphere"/>
        /// which is purely altitude-based.
        /// </summary>
        public bool flying;

        /// <summary>True on a sub-orbital trajectory.</summary>
        public bool subOrbital;

        /// <summary>True on a closed (bound) orbit.</summary>
        public bool orbiting;

        /// <summary>True on a hyperbolic / escape trajectory.</summary>
        public bool escaping;

        /// <summary>
        /// True when this vessel is the docked descendant of a
        /// physically larger vessel (KSP merges the docked vessels
        /// for physics; this flag indicates we're not in control
        /// of an independent physical object).
        /// </summary>
        public bool docked;

        /// <summary>
        /// Convenience flag: <c>landed || splashed || prelaunch</c>.
        /// True when the vessel is in physical contact with the
        /// surface of its current SOI body.
        /// </summary>
        public bool onSurface;

        /// <summary>
        /// True when the vessel is inside its parent body's
        /// atmosphere — checked via <c>Vessel.atmDensity &gt; 0</c>
        /// rather than altitude bands so the flag agrees with
        /// engine Isp / aero forces on every body. Always false
        /// for vessels around airless bodies.
        /// </summary>
        public bool inAtmosphere;

        /// <summary>
        /// <c>Vessel.IsControllable</c> — true when the vessel has
        /// at least one functional command source (active probe
        /// core with electric charge, or a manned cockpit) and
        /// signal/comms requirements (if enabled) are satisfied.
        /// </summary>
        public bool controllable;
    }
}

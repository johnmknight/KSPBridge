using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/encounter</c> topic.
    ///
    /// Detects upcoming SOI transitions in the current patched-conic
    /// trajectory. If the vessel's orbit is going to enter a new
    /// celestial body's sphere of influence, <c>hasEncounter</c> is
    /// true and <c>closestApproachDistance</c> reports the SOI radius
    /// at the moment of capture. This is a deliberate simplification —
    /// "true closest approach distance" requires solving for the
    /// minimum distance between two orbits over time, which is
    /// expensive and not what the console actually uses.
    /// </summary>
    [Serializable]
    public class EncounterTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>True if any future patch enters a new SOI.</summary>
        public bool hasEncounter;

        /// <summary>
        /// Distance at SOI transition, metres. 0 when no encounter is
        /// detected.
        /// </summary>
        public double closestApproachDistance;

        /// <summary>
        /// Name of the body whose SOI will be entered, or empty string
        /// if no encounter.
        /// </summary>
        public string encounterBody;

        /// <summary>
        /// Time until SOI entry, seconds. 0 if no encounter. May be
        /// useful to consumers that want a countdown.
        /// </summary>
        public double timeToEncounter;
    }
}

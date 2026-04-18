using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/navigation</c> topic.
    ///
    /// Mirrors KSA-Bridge's field names exactly so the hard-scifi
    /// console's switch case matches ours without edits. Notably,
    /// <c>speed</c> on this topic means <em>surface</em> speed
    /// (velocity relative to the rotating planet surface), in contrast
    /// with <c>speed</c> on the <c>vehicle</c> topic which means
    /// <em>orbital</em> speed (velocity in the inertial frame). This
    /// is an inherited quirk of the KSA-Bridge schema — we keep it
    /// to stay interoperable.
    /// </summary>
    [Serializable]
    public class NavigationTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>Altitude above parent body mean surface, metres.</summary>
        public double altitude;

        /// <summary>Altitude above parent body mean surface, kilometres.</summary>
        public double altitudeKm;

        /// <summary>Surface speed — velocity relative to the rotating planet, m/s.</summary>
        public double speed;

        /// <summary>Orbital speed — velocity in the inertial frame, m/s.</summary>
        public double orbitalSpeed;
    }
}

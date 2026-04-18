using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/attitude</c> topic.
    ///
    /// All three angles are in radians, matching KSA-Bridge's wire
    /// convention. Angles are measured in the local surface reference
    /// frame (north/east/down at the vessel's current position).
    /// </summary>
    [Serializable]
    public class AttitudeTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// Compass heading of the vessel nose, radians, clockwise from
        /// north. Range [0, 2π). 0 = north, π/2 = east, π = south,
        /// 3π/2 = west.
        /// </summary>
        public double heading;

        /// <summary>
        /// Pitch of the vessel nose above the local horizon, radians.
        /// Range [−π/2, π/2]. +π/2 = nose straight up, 0 = level,
        /// −π/2 = nose straight down.
        /// </summary>
        public double pitch;

        /// <summary>
        /// Roll of the vessel about its nose axis, radians. Range (−π, π].
        /// 0 = wings level (vessel "roof" aligned with local up).
        /// Positive = right wing down (right roll), negative = left wing down.
        /// </summary>
        public double roll;
    }
}

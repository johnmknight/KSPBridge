using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/parent_body</c> topic.
    ///
    /// Describes the celestial body whose SOI the vessel is currently
    /// in. The hard-scifi console uses this to render the planet
    /// rotating underneath the fixed orbit — without live body
    /// rotation, the ground track on the globe wouldn't march with
    /// real time.
    /// </summary>
    [Serializable]
    public class ParentBodyTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>Body name (e.g. "Kerbin", "Mun", "Duna").</summary>
        public string bodyName;

        /// <summary>Body mean radius, metres.</summary>
        public double radius;

        /// <summary>Body mass, kilograms.</summary>
        public double mass;

        /// <summary>Sidereal rotation period, seconds. Negative if retrograde.</summary>
        public double rotationPeriod;

        /// <summary>
        /// Body rotation quaternion X component. Taken straight from
        /// KSP's <c>CelestialBody.transform.rotation</c>, which is the
        /// Unity rotation that carries body-fixed coordinates to world
        /// (and hence CCI, which differs from world by translation only).
        /// </summary>
        public double rotationQuatX;
        /// <summary>Body rotation quaternion Y component.</summary>
        public double rotationQuatY;
        /// <summary>Body rotation quaternion Z component.</summary>
        public double rotationQuatZ;
        /// <summary>Body rotation quaternion W component.</summary>
        public double rotationQuatW;

        /// <summary>
        /// Axial tilt in radians — angle between the body's spin axis
        /// and the normal to its own orbital plane. Zero for stock
        /// Kerbin (and most stock bodies). Undefined for root-of-tree
        /// bodies (emit 0).
        /// </summary>
        public double axialTilt;
    }
}

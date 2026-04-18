using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/orbit</c> topic.
    ///
    /// All linear distances in metres, masses in kilograms, times in
    /// seconds. **Angles are in radians**, matching KSA-Bridge's wire
    /// convention — the hard-scifi console feeds them directly into
    /// rotation matrices that expect radians, so converting to degrees
    /// here would break the existing rendering pipeline.
    ///
    /// KSP's native <c>Orbit</c> class returns inclination, LAN, and
    /// argument-of-periapsis in degrees, so the producer converts before
    /// emitting.
    /// </summary>
    [Serializable]
    public class OrbitTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>Apoapsis radius from parent body centre, metres.</summary>
        public double apoapsis;
        /// <summary>Periapsis radius from parent body centre, metres.</summary>
        public double periapsis;
        /// <summary>Apoapsis altitude above parent mean surface, metres.</summary>
        public double apoapsisElevation;
        /// <summary>Periapsis altitude above parent mean surface, metres.</summary>
        public double periapsisElevation;

        /// <summary>Orbital eccentricity. 0 = circular, &lt;1 closed, =1 parabolic, &gt;1 hyperbolic.</summary>
        public double eccentricity;
        /// <summary>Inclination, radians.</summary>
        public double inclination;
        /// <summary>Longitude of ascending node, radians.</summary>
        public double longitudeOfAscendingNode;
        /// <summary>Argument of periapsis, radians.</summary>
        public double argumentOfPeriapsis;

        /// <summary>Orbital period, seconds. Undefined for unbound orbits.</summary>
        public double period;
        /// <summary>Semi-major axis, metres. Negative for hyperbolic orbits in KSP convention.</summary>
        public double semiMajorAxis;
        /// <summary>
        /// Semi-minor axis magnitude, metres. Computed as
        /// |a| · sqrt(|1 − e²|) so it stays defined for both elliptical
        /// and hyperbolic cases. Used by the console for ellipse rendering.
        /// </summary>
        public double semiMinorAxis;

        /// <summary>Time until next apoapsis pass, seconds.</summary>
        public double timeToApoapsis;
        /// <summary>Time until next periapsis pass, seconds.</summary>
        public double timeToPeriapsis;

        /// <summary>
        /// Classification: "CIRCULAR", "ELLIPTICAL", "PARABOLIC", or
        /// "HYPERBOLIC". Derived from eccentricity.
        /// </summary>
        public string orbitType;

        /// <summary>Parent body mean radius, metres. Convenience field.</summary>
        public double parentRadius;
        /// <summary>Parent body mass, kilograms. Convenience field.</summary>
        public double parentMass;
    }
}

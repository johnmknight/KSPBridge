using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/state_vectors</c> topic.
    ///
    /// Contains the vessel's position and velocity vectors in the
    /// parent-body-centered inertial (CCI) frame — the standard
    /// astrodynamics frame in which the orbit is a stationary ellipse.
    /// Position components are in metres, velocity components in m/s.
    ///
    /// Coordinate handedness follows KSP's world frame as-is (Unity
    /// left-handed, y-up). Consumers rendering in right-handed coordinate
    /// systems (e.g., Three.js with y-up default) may need to swap axes —
    /// KSA-Bridge's hard-scifi console applies the mapping
    /// <c>x→x, z→y, -y→z</c> at receive time.
    /// </summary>
    [Serializable]
    public class StateVectorsTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>CCI position X component, metres.</summary>
        public double positionX;
        /// <summary>CCI position Y component, metres.</summary>
        public double positionY;
        /// <summary>CCI position Z component, metres.</summary>
        public double positionZ;

        /// <summary>CCI velocity X component, m/s.</summary>
        public double velocityX;
        /// <summary>CCI velocity Y component, m/s.</summary>
        public double velocityY;
        /// <summary>CCI velocity Z component, m/s.</summary>
        public double velocityZ;
    }
}

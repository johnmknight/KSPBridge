using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/dynamics</c> topic.
    ///
    /// Reports body-frame rotational rates, linear and angular
    /// accelerations, and current g-load. Distinct from
    /// <c>state_vectors</c> (positional / inertial-frame velocity)
    /// and <c>attitude</c> (orientation): this topic captures the
    /// derivatives a pilot or autopilot cares about for stability
    /// and load monitoring.
    ///
    /// Body axes follow KSP's vessel transform convention:
    /// <list type="bullet">
    ///   <item>x = right (pitch axis)</item>
    ///   <item>y = up — toward the cockpit roof (yaw axis)</item>
    ///   <item>z = forward — out the nose (roll axis)</item>
    /// </list>
    ///
    /// JsonUtility constraints (see <see cref="VehicleTelemetry"/> for
    /// the full list) apply: <c>[Serializable]</c>, public fields, no
    /// nullables. Field names are camelCase to match KSA-Bridge style.
    /// </summary>
    [Serializable]
    public class DynamicsTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>Pitch rate (rad/s). Rotation about the body x axis.</summary>
        public double bodyRatePitch;

        /// <summary>Yaw rate (rad/s). Rotation about the body y axis.</summary>
        public double bodyRateYaw;

        /// <summary>Roll rate (rad/s). Rotation about the body z axis.</summary>
        public double bodyRateRoll;

        /// <summary>
        /// Linear acceleration along the body x axis (m/s²).
        /// World-frame <c>Vessel.acceleration</c> projected into the
        /// controlling part's local frame so a HUD reads
        /// forward / right / up components a pilot expects.
        /// </summary>
        public double linearAccelX;

        /// <summary>Linear acceleration along the body y axis (m/s²).</summary>
        public double linearAccelY;

        /// <summary>Linear acceleration along the body z axis (m/s²).</summary>
        public double linearAccelZ;

        /// <summary>
        /// Magnitude of linear acceleration (m/s²). Frame-independent —
        /// the same value as <c>sqrt(x²+y²+z²)</c> in either body or
        /// world frame.
        /// </summary>
        public double linearAccelMag;

        /// <summary>
        /// Current g-load magnitude in standard g (≈9.80665 m/s²).
        /// Sourced from <c>Vessel.geeForce</c> so the value matches
        /// the stock G-meter exactly. Equivalent (within rounding)
        /// to <c>linearAccelMag / 9.80665</c>.
        /// </summary>
        public double gForce;

        /// <summary>
        /// Angular acceleration about body x (pitch axis), rad/s².
        /// Computed as the finite difference of body-frame angular
        /// velocity between scheduler ticks. The first sample after
        /// a vessel change or scene gap is 0 to suppress the
        /// spurious spike a stale reference would produce.
        /// </summary>
        public double angularAccelPitch;

        /// <summary>Angular acceleration about body y (yaw axis), rad/s².</summary>
        public double angularAccelYaw;

        /// <summary>Angular acceleration about body z (roll axis), rad/s².</summary>
        public double angularAccelRoll;
    }
}

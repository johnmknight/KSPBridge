using UnityEngine;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/dynamics</c> topic at 5 Hz.
    ///
    /// Reports body-frame angular velocity (pitch/yaw/roll rates),
    /// body-frame linear acceleration, the magnitude of linear
    /// acceleration, current g-load, and finite-differenced
    /// angular acceleration. The g-load is sourced from KSP's own
    /// <c>Vessel.geeForce</c> so the published value matches the
    /// stock G-meter readout.
    ///
    /// Producer state: caches the previous tick's angular velocity
    /// and timestamp so the next tick can compute angular
    /// acceleration as a finite difference. State is keyed on
    /// <c>vessel.persistentId</c> — when the active vessel changes
    /// (vessel switch, undock to debris) the previous-sample state
    /// is invalidated and angular acceleration emits 0 for one
    /// tick rather than producing a giant spurious spike from
    /// differencing across two unrelated vessels.
    /// </summary>
    public class DynamicsProducer : ITelemetryProducer
    {
        public string TopicSuffix => "dynamics";

        // 5 Hz. Body rates change visibly under SAS / RCS / pilot
        // input but rarely need finer-grained sampling than this.
        // 2 Hz looked choppy on a vessel oscillating about a setpoint.
        public int RateDivisor => 2;

        // Previous-sample state for the finite-difference angular
        // acceleration computation. _prevTime starts at 0 (no valid
        // previous sample); the first tick after construction emits
        // alpha = 0.
        private uint _prevVesselPid;
        private Vector3 _prevAngularVelocity;
        private float _prevTime;

        // Maximum dt allowed between samples for finite-difference
        // angular acceleration to be considered valid. Anything
        // longer is treated as a gap (scene change, vessel switch
        // pause, time-warp transition) and suppressed to 0.
        private const float MaxDifferenceDt = 1.0f;

        public object Build(Vessel vessel)
        {
            if (vessel == null) return null;

            // Body-axis angular velocity (rad/s). KSP's
            // Vessel.angularVelocity is already in the vessel's local
            // frame using aerospace convention: x = pitch rate,
            // y = yaw rate, z = roll rate.
            Vector3 wBody = vessel.angularVelocity;

            // Linear acceleration. World-frame Vessel.acceleration
            // projected into the controlling part's local frame so
            // the published x / y / z map to the pilot's
            // right / up / forward — what a HUD typically wants.
            Vector3 aWorld = vessel.acceleration;
            Vector3 aBody = vessel.ReferenceTransform != null
                ? vessel.ReferenceTransform.InverseTransformDirection(aWorld)
                : aWorld;

            // Angular acceleration (rad/s²) by finite difference. We
            // only emit a non-zero value when (a) the previous sample
            // is from the same vessel and (b) the elapsed time looks
            // sensible. Time.time pauses with the game and resets on
            // some scene changes, so guard accordingly.
            float now = Time.time;
            Vector3 alphaBody = Vector3.zero;
            bool haveValidPrev =
                _prevVesselPid == vessel.persistentId &&
                _prevTime > 0f &&
                now > _prevTime &&
                (now - _prevTime) < MaxDifferenceDt;
            if (haveValidPrev)
            {
                float dt = now - _prevTime;
                alphaBody = (wBody - _prevAngularVelocity) / dt;
            }

            // Update state for the next tick — even when we couldn't
            // compute a valid difference this tick, so the *next*
            // tick has a fresh reference.
            _prevVesselPid = vessel.persistentId;
            _prevAngularVelocity = wBody;
            _prevTime = now;

            return new DynamicsTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,

                bodyRatePitch = SafeDouble(wBody.x),
                bodyRateYaw = SafeDouble(wBody.y),
                bodyRateRoll = SafeDouble(wBody.z),

                linearAccelX = SafeDouble(aBody.x),
                linearAccelY = SafeDouble(aBody.y),
                linearAccelZ = SafeDouble(aBody.z),
                linearAccelMag = SafeDouble(aWorld.magnitude),

                gForce = SafeDouble(vessel.geeForce),

                angularAccelPitch = SafeDouble(alphaBody.x),
                angularAccelYaw = SafeDouble(alphaBody.y),
                angularAccelRoll = SafeDouble(alphaBody.z),
            };
        }

        // Replace NaN / ±∞ with 0 — JsonUtility emits non-finite
        // doubles as the literal tokens "NaN" / "Infinity", which
        // are not valid JSON per RFC 8259 and break strict parsers.
        private static double SafeDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v;
        }

        // Float overload so callers don't need to cast at every site.
        private static double SafeDouble(float v) => SafeDouble((double)v);
    }
}

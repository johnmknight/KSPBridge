using UnityEngine;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/state_vectors</c> topic at 10 Hz.
    ///
    /// Position is computed as vessel world position minus parent body
    /// world position — this cancels out the body's movement around its
    /// own parent (Mun around Kerbin, Kerbin around Sun, etc.) and
    /// leaves us with a vessel position in the parent body's inertial
    /// frame. Velocity uses <c>Vessel.obt_velocity</c>, which KSP already
    /// expresses in that same inertial frame.
    /// </summary>
    public class StateVectorsProducer : ITelemetryProducer
    {
        public string TopicSuffix => "state_vectors";

        // 10 Hz. Matches KSA-Bridge cadence. Consumers that need smooth
        // globe rendering at 60 fps will interpolate from these samples.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            // Defensive: if the vessel has no parent body (shouldn't
            // happen in normal flight but has been seen during
            // scene-transition glitches), skip this tick rather than
            // emit nonsense coordinates.
            if (vessel.mainBody == null) return null;

            // Position relative to parent body centre in Unity world
            // coordinates. Subtracting the body's world position removes
            // the body's own translational motion — what remains is the
            // vessel's inertial position in the body's frame.
            Vector3d pos = vessel.GetWorldPos3D() - vessel.mainBody.position;

            // obt_velocity is already in the parent body's inertial
            // (non-rotating) frame — exactly what CCI means. No
            // subtraction needed.
            Vector3d vel = vessel.obt_velocity;

            return new StateVectorsTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                positionX = pos.x,
                positionY = pos.y,
                positionZ = pos.z,
                velocityX = vel.x,
                velocityY = vel.y,
                velocityZ = vel.z,
            };
        }
    }
}

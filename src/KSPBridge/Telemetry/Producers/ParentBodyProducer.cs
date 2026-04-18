using System;
using UnityEngine;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/parent_body</c> topic at 2 Hz.
    /// Body properties change slowly (only when the vessel crosses an
    /// SOI), but the rotation quaternion changes continuously as the
    /// planet spins — 2 Hz is enough for smooth-looking ground track
    /// updates because the console interpolates between samples.
    /// </summary>
    public class ParentBodyProducer : ITelemetryProducer
    {
        public string TopicSuffix => "parent_body";

        // 2 Hz. 5 base ticks at 0.1 s each.
        public int RateDivisor => 5;

        public object Build(Vessel vessel)
        {
            CelestialBody body = vessel.mainBody;
            if (body == null) return null;

            // Grab the current Unity rotation quaternion for the body.
            // KSP rotates the body transform each frame to reflect the
            // sidereal angle; reading it gives us the "live" orientation.
            Quaternion q = body.transform.rotation;

            return new ParentBodyTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                bodyName = body.bodyName ?? "",
                radius = body.Radius,
                mass = body.Mass,
                rotationPeriod = body.rotationPeriod,
                rotationQuatX = q.x,
                rotationQuatY = q.y,
                rotationQuatZ = q.z,
                rotationQuatW = q.w,
                axialTilt = ComputeAxialTilt(body),
            };
        }

        // Axial tilt: angle between the body's spin axis and the normal
        // to its own orbit plane around its parent.
        //
        // For root bodies (Sun / Kerbol) we return 0 because "axial tilt"
        // has no well-defined meaning without a parent-orbit reference.
        //
        // For orbiting bodies we compute the orbital-plane normal as
        // r × v (position cross velocity, in the parent body's frame),
        // then measure the angle between that normal and the body's
        // own transform.up (its rotation axis in world coordinates).
        // Result in radians, always in [0, π].
        private static double ComputeAxialTilt(CelestialBody body)
        {
            if (body.orbit == null || body.referenceBody == null)
                return 0.0;

            Vector3d r = body.orbit.pos;
            Vector3d v = body.orbit.vel;

            // Degenerate case: if r and v are near-parallel (impossible
            // for a real orbit but possible during initialisation frames)
            // the cross product is undefined. Bail to 0 rather than
            // return a NaN through JSON.
            Vector3d orbitNormal = Vector3d.Cross(r, v);
            if (orbitNormal.sqrMagnitude < 1e-12)
                return 0.0;

            orbitNormal.Normalize();
            Vector3d spinAxis = ((Vector3d)body.transform.up).normalized;

            double cos = Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(spinAxis, orbitNormal)));
            return Math.Acos(cos);
        }
    }
}

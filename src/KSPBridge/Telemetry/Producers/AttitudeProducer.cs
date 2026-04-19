using System;
using UnityEngine;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/attitude</c> topic at 10 Hz.
    ///
    /// Heading / pitch / roll are derived from the vessel's
    /// ReferenceTransform rotation projected onto a locally constructed
    /// surface reference frame (north / east / up at the vessel's
    /// position). We compute them with explicit atan2/asin calls rather
    /// than extracting Euler angles from a quaternion, because Euler
    /// extraction suffers from gimbal lock near pitch = ±π/2 (nose
    /// straight up, which happens every launch).
    ///
    /// KSP coordinate quirks worth remembering:
    ///   - <c>ReferenceTransform.up</c> is the vessel's NOSE direction
    ///     (not the "roof"). This mirrors KSP's old rocket-first
    ///     convention where "up" along the hull meant forward.
    ///   - <c>ReferenceTransform.forward</c> points from nose toward
    ///     tail (along the hull, downward when sitting on the pad).
    ///   - <c>ReferenceTransform.right</c> is the vessel's right side.
    ///   - The vessel's "roof" (pilot's up) is therefore
    ///     <c>-ReferenceTransform.forward</c>.
    /// </summary>
    public class AttitudeProducer : ITelemetryProducer
    {
        public string TopicSuffix => "attitude";

        // 10 Hz. Attitude can change fast (SAS fights, re-entry tumble,
        // manual flying) so we match KSA-Bridge's high rate here.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            if (vessel.mainBody == null) return null;

            // ---- Build the local surface reference frame -----------
            //
            // up     = radial outward from body centre
            // north  = body rotation axis projected onto the horizon
            // east   = up × north (right-handed triad)
            //
            // We use CoM rather than GetWorldPos3D() for stability —
            // GetWorldPos3D can hiccup during physics scene transitions.
            Vector3d up = (vessel.CoM - vessel.mainBody.position).normalized;
            Vector3d bodySpin = vessel.mainBody.transform.up;
            Vector3d north = Vector3d.Exclude(up, bodySpin).normalized;
            Vector3d east = Vector3d.Cross(up, north);

            // ---- Vessel basis vectors in world coordinates ---------
            Vector3d nose = (Vector3d)vessel.ReferenceTransform.up;
            // Vessel roof (pilot's up): opposite of ReferenceTransform.forward.
            Vector3d roof = -(Vector3d)vessel.ReferenceTransform.forward;

            // ---- Heading -------------------------------------------
            // Project the nose onto the horizon plane, measure compass
            // angle clockwise from north. atan2(east-component, north-component)
            // naturally yields clockwise-from-north.
            double heading = Math.Atan2(
                Vector3d.Dot(nose, east),
                Vector3d.Dot(nose, north));
            if (heading < 0) heading += 2.0 * Math.PI;

            // ---- Pitch ---------------------------------------------
            // Angle of the nose above the horizon. asin(nose·up) gives
            // [-π/2, π/2] directly. Clamp the dot product to handle
            // floating-point slop past ±1 that would make asin return NaN.
            double noseDotUp = Math.Max(-1.0, Math.Min(1.0, Vector3d.Dot(nose, up)));
            double pitch = Math.Asin(noseDotUp);

            // ---- Roll ----------------------------------------------
            // Compare the vessel's roof direction to the local up, both
            // projected onto the plane perpendicular to the nose. The
            // signed angle between them (measured around the nose axis)
            // is the roll. Zero when wings are level.
            //
            // Vector3d.Exclude(a, b) removes b's component along a.
            // So rooInPlane = roof with its nose-component removed.
            Vector3d roofInPlane = Vector3d.Exclude(nose, roof).normalized;
            Vector3d upInPlane = Vector3d.Exclude(nose, up).normalized;

            // Signed angle about the nose axis: atan2(cross·nose, dot).
            // Positive when a rotation about +nose takes up→roof, which
            // corresponds to a right-wing-down (positive) roll.
            double rollCross = Vector3d.Dot(Vector3d.Cross(upInPlane, roofInPlane), nose);
            double rollDot = Vector3d.Dot(upInPlane, roofInPlane);
            double roll = Math.Atan2(rollCross, rollDot);

            // ---- Root-transform quaternion (world frame) ----------
            // For external 3D renderers that need to pose the full
            // vessel as a rigid body (docking cam, VirtualCupola-style
            // overlays). Read from vessel.transform rather than
            // ReferenceTransform so that switching "control from here"
            // does not flip the rendered vessel. KSPEVU's glb is
            // organised relative to this same root transform, so
            // applying this quaternion to the glb's root node poses
            // every part correctly in a rigid-body render.
            Quaternion q = vessel.transform.rotation;

            return new AttitudeTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                heading = heading,
                pitch = pitch,
                roll = roll,
                rotationX = q.x,
                rotationY = q.y,
                rotationZ = q.z,
                rotationW = q.w,
            };
        }
    }
}

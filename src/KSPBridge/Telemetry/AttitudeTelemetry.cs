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

        /// <summary>
        /// Vessel root transform rotation as a unit quaternion, Unity
        /// world frame. This is <c>Vessel.transform.rotation</c> — the
        /// rotation of the vessel's root part — NOT derived from
        /// <see cref="heading"/> / <see cref="pitch"/> / <see cref="roll"/>,
        /// which describe nose direction in the local surface frame.
        ///
        /// Use this to pose the whole vessel as a rigid body in an
        /// external 3D renderer. KSPEVU's glb is organised with part
        /// nodes at <c>orgPos</c> / <c>orgRot</c> relative to this same
        /// root transform, so applying the quaternion to the glb root
        /// poses every part correctly.
        ///
        /// Handedness matches state_vectors: Unity left-handed, y-up.
        /// Consumers swapping to right-handed axes must convert the
        /// quaternion consistently with their position swap — the
        /// naive elementwise mapping used for position vectors does
        /// not work for quaternions.
        /// </summary>
        public double rotationX;
        /// <inheritdoc cref="rotationX"/>
        public double rotationY;
        /// <inheritdoc cref="rotationX"/>
        public double rotationZ;
        /// <inheritdoc cref="rotationX"/>
        public double rotationW;
    }
}

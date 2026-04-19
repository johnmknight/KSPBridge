namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes <c>{prefix}/target/attitude</c> at 10 Hz when the active
    /// vessel has a vessel-bearing target selected.
    ///
    /// Payload schema is identical to <see cref="AttitudeProducer"/>'s —
    /// heading / pitch / roll in the target vessel's own local surface
    /// frame, plus <c>rotationX/Y/Z/W</c> as its root-transform quaternion
    /// in Unity world space.
    ///
    /// The HPR fields are of limited use for a target vessel (the local
    /// surface frame is defined at the target's position, not the
    /// observer's) — they're included mainly for schema symmetry with
    /// the active-vessel topic. The <c>rotationX/Y/Z/W</c> quaternion is
    /// the field that actually matters: external 3D renderers use it to
    /// pose the target's KSPEVU glb correctly relative to own-ship.
    ///
    /// Silent when no target is selected. See <see cref="TargetResolver"/>.
    /// </summary>
    public class TargetAttitudeProducer : ITelemetryProducer
    {
        private readonly AttitudeProducer _inner = new AttitudeProducer();

        public string TopicSuffix => "target/attitude";

        // 10 Hz. Matches the active-vessel topic.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            var target = TargetResolver.Resolve(vessel);
            if (target == null) return null;
            return _inner.Build(target);
        }
    }
}

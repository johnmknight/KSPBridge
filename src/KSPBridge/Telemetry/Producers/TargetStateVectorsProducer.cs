namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes <c>{prefix}/target/state_vectors</c> at 10 Hz when the
    /// active vessel has a vessel-bearing target selected.
    ///
    /// Payload schema is identical to <see cref="StateVectorsProducer"/>'s.
    /// Position is expressed in the target vessel's *own* parent-body
    /// inertial (CCI) frame — i.e. if the target is in Kerbin SOI its
    /// position is relative to Kerbin, if it's in Mun SOI it's relative
    /// to Mun, etc. Consumers computing relative pose between active and
    /// target must therefore check that both vessels share a parent body
    /// (via <c>vehicle.parentBody</c> vs <c>target/vehicle.parentBody</c>)
    /// before subtracting state vectors. In docking scenarios they always
    /// will — you can't physically dock across an SOI boundary — but
    /// general target telemetry may see cross-SOI cases.
    ///
    /// Silent when no target is selected. See <see cref="TargetResolver"/>.
    /// </summary>
    public class TargetStateVectorsProducer : ITelemetryProducer
    {
        private readonly StateVectorsProducer _inner = new StateVectorsProducer();

        public string TopicSuffix => "target/state_vectors";

        // 10 Hz. For close-range operations (docking) a higher rate may
        // be desirable; that will live on a dedicated ksp/docking/delta
        // topic that also pre-differences the poses to sidestep
        // floating-origin precision loss.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            var target = TargetResolver.Resolve(vessel);
            if (target == null) return null;
            return _inner.Build(target);
        }
    }
}

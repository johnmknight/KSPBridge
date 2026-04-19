namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes <c>{prefix}/target/vehicle</c> at 10 Hz when the active
    /// vessel has a vessel-bearing target selected.
    ///
    /// Payload schema is identical to <see cref="VehicleProducer"/>'s —
    /// same <see cref="VehicleTelemetry"/> shape, same field semantics —
    /// just describing the target vessel instead of the active one. This
    /// deliberate schema reuse lets a browser console that already parses
    /// <c>vehicle</c> render a target panel with zero additional parsing
    /// code: same fields, different topic.
    ///
    /// Silent when no target is selected or the target has no associated
    /// vessel (e.g. a celestial-body target). See <see cref="TargetResolver"/>
    /// for the resolution rules.
    ///
    /// Implementation delegates to an inner <see cref="VehicleProducer"/>
    /// instance so the target topic inherits any future changes to the
    /// active-vessel <c>vehicle</c> topic automatically — there is only
    /// one place to maintain the payload shape.
    /// </summary>
    public class TargetVehicleProducer : ITelemetryProducer
    {
        private readonly VehicleProducer _inner = new VehicleProducer();

        public string TopicSuffix => "target/vehicle";

        // 10 Hz. Matches the active-vessel topic — consumers rendering a
        // target panel want the same refresh cadence as own-ship.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            var target = TargetResolver.Resolve(vessel);
            if (target == null) return null;
            return _inner.Build(target);
        }
    }
}

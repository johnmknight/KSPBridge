namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Contract for a single telemetry topic publisher. Each implementation
    /// owns the schema, rate, and KSP-API-to-payload conversion for one
    /// topic. The scheduler holds a list of producers and ticks them.
    ///
    /// Adding a new telemetry topic should be a one-file change: drop a
    /// new class implementing this interface into <c>Producers/</c> and
    /// register it in the scheduler's producer list. No scheduler changes
    /// required for ordinary topics.
    /// </summary>
    public interface ITelemetryProducer
    {
        /// <summary>
        /// Sub-topic appended to the configured prefix. For example,
        /// returning <c>"vehicle"</c> publishes to <c>{prefix}/vehicle</c>
        /// (typically <c>ksp/telemetry/vehicle</c>).
        /// </summary>
        string TopicSuffix { get; }

        /// <summary>
        /// Publish rate divisor relative to the scheduler's base 10 Hz tick.
        /// 1 = publish on every tick (10 Hz).
        /// 5 = publish every fifth tick (2 Hz).
        /// 10 = once per second.
        /// Values &lt;= 0 disable the producer (it will never publish).
        /// </summary>
        int RateDivisor { get; }

        /// <summary>
        /// Build the JSON payload for the current vessel state. Called on
        /// the Unity main thread, so it is safe to read KSP / Unity APIs
        /// directly. The returned object must be a class marked
        /// <c>[Serializable]</c> with public fields — JsonUtility will
        /// serialise it to JSON. Return <c>null</c> to skip this tick
        /// (e.g. if some prerequisite isn't ready yet).
        /// </summary>
        object Build(Vessel vessel);
    }
}

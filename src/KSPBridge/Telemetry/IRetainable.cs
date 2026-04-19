namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Optional marker interface for producers whose messages should
    /// be published with MQTT retain=true.
    ///
    /// Most telemetry is streaming (position, attitude, etc.) — a new
    /// subscriber should wait for the next tick rather than receive a
    /// stale frame, so those topics publish with retain=false. A small
    /// number of topics describe *state that persists until it changes*
    /// (docking context, active control-point identity, etc.), and a
    /// late subscriber wants the current value immediately; those
    /// topics set retain=true.
    ///
    /// Implemented as an optional companion to <see cref="ITelemetryProducer"/>
    /// rather than a new required property on the main interface, so
    /// existing producers don't need to be touched when retention is
    /// added. The scheduler pattern-matches on this interface when
    /// deciding which retain flag to pass to the broker.
    /// </summary>
    public interface IRetainable
    {
        /// <summary>
        /// When <c>true</c>, the scheduler publishes this producer's
        /// messages with the MQTT retain flag set. Implementations may
        /// return a constant or compute the flag per-tick if they have
        /// modes where retention is and isn't appropriate.
        /// </summary>
        bool Retain { get; }
    }
}

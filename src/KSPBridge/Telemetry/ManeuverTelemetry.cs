using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/maneuver</c> topic.
    ///
    /// Reports the state of the patched-conic flight plan: how many
    /// maneuver nodes are scheduled, whether one is "active" (currently
    /// being executed or about to start), and whether the plan is
    /// considered complete. Beyond the three console-driven fields we
    /// also emit basic information about the next upcoming node so
    /// future consumers (e.g., a burn-prep dashboard) have something
    /// to work with.
    /// </summary>
    [Serializable]
    public class ManeuverTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>Number of maneuver nodes currently planned.</summary>
        public int burnCount;

        /// <summary>
        /// True if any planned node's burn window starts within the
        /// next 30 seconds OR is already in progress (UT &lt; now and
        /// node still exists).
        /// </summary>
        public bool hasActiveBurns;

        /// <summary>
        /// True when the flight plan has no remaining future nodes —
        /// either because no nodes were ever planned, or because all
        /// scheduled nodes have UTs in the past.
        /// </summary>
        public bool flightPlanComplete;

        /// <summary>
        /// Seconds until the next upcoming node (negative if it is in
        /// the past). 0 if no nodes exist.
        /// </summary>
        public double nextNodeIn;

        /// <summary>Magnitude of the next upcoming node's ΔV vector, m/s. 0 if no nodes.</summary>
        public double nextNodeDeltaV;
    }
}

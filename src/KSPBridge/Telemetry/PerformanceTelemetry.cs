using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/performance</c> topic.
    ///
    /// Reports propulsive performance figures: total remaining ΔV,
    /// current-stage thrust-to-weight ratio, total vessel mass, and
    /// current summed engine thrust. KSP 1.12 ships a stock ΔV
    /// calculator (<c>Vessel.VesselDeltaV</c>) that does the stage
    /// analysis for us — this producer is a thin layer over it with
    /// defensive guards for vessels that have no engines or whose
    /// calculator is still spinning up.
    /// </summary>
    [Serializable]
    public class PerformanceTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// Total remaining ΔV in the current conditions (atmospheric or
        /// vacuum as appropriate), m/s. 0 when no ΔV is available or
        /// the calculator hasn't produced a finite value yet.
        /// </summary>
        public double deltaV;

        /// <summary>Total ΔV in vacuum, m/s.</summary>
        public double deltaVVac;

        /// <summary>Total ΔV at sea level (atmospheric), m/s.</summary>
        public double deltaVAsl;

        /// <summary>
        /// Current stage thrust-to-weight ratio against local gravity.
        /// Above 1 = can lift off / accelerate upward. 0 when no engines
        /// are active.
        /// </summary>
        public double twr;

        /// <summary>Current-stage ΔV, m/s. 0 if no stage info available.</summary>
        public double currentStageDeltaV;

        /// <summary>Total vessel mass (wet), kilograms.</summary>
        public double mass;

        /// <summary>
        /// Sum of current thrust from all ignited engines, Newtons.
        /// Accounts for throttle setting and atmospheric derating.
        /// </summary>
        public double thrust;
    }
}

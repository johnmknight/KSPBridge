namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/maneuver</c> topic at 2 Hz.
    ///
    /// Reads from <c>vessel.patchedConicSolver.maneuverNodes</c>, which
    /// is the live list of planned burns the player has set up via the
    /// map-view UI. The solver itself can be null for vessels in
    /// physics ranges where it is disabled (rare, but defensive
    /// guarding is cheap).
    /// </summary>
    public class ManeuverProducer : ITelemetryProducer
    {
        public string TopicSuffix => "maneuver";

        // 2 Hz. Maneuver state changes only when the player edits a
        // node or burns through one — high frequency would just be
        // wasted bandwidth.
        public int RateDivisor => 5;

        // Window (seconds) within which an upcoming node is considered
        // "active." 30 s captures the typical burn-prep window where the
        // player has lined up the maneuver and is about to ignite.
        private const double ActiveBurnWindowSeconds = 30.0;

        public object Build(Vessel vessel)
        {
            var solver = vessel.patchedConicSolver;
            int count = 0;
            bool active = false;
            double nextIn = 0.0;
            double nextDv = 0.0;

            if (solver != null && solver.maneuverNodes != null)
            {
                count = solver.maneuverNodes.Count;
                double now = Planetarium.GetUniversalTime();
                double earliestFutureUT = double.PositiveInfinity;
                ManeuverNode earliest = null;

                // Walk all nodes once. We need:
                //   (a) whether any node falls within the active window,
                //   (b) the earliest future node (for nextNodeIn / DV).
                foreach (var node in solver.maneuverNodes)
                {
                    if (node == null) continue;

                    double until = node.UT - now;
                    if (until <= ActiveBurnWindowSeconds)
                        active = true;

                    if (node.UT >= now && node.UT < earliestFutureUT)
                    {
                        earliestFutureUT = node.UT;
                        earliest = node;
                    }
                }

                if (earliest != null)
                {
                    nextIn = earliest.UT - now;
                    nextDv = earliest.DeltaV.magnitude;
                }
            }

            // "Complete" means no nodes remaining in the future. This
            // matches the intuitive "you've finished the plan" meaning,
            // though KSA-Bridge may interpret it differently — adjust
            // if observed behaviour diverges.
            bool complete = (count == 0) || double.IsPositiveInfinity(NextFutureUT(solver));

            return new ManeuverTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                burnCount = count,
                hasActiveBurns = active,
                flightPlanComplete = complete,
                nextNodeIn = nextIn,
                nextNodeDeltaV = nextDv,
            };
        }

        // Helper: the UT of the soonest node strictly in the future,
        // or +infinity if none exist.
        private static double NextFutureUT(PatchedConicSolver solver)
        {
            if (solver == null || solver.maneuverNodes == null)
                return double.PositiveInfinity;

            double now = Planetarium.GetUniversalTime();
            double earliest = double.PositiveInfinity;

            foreach (var node in solver.maneuverNodes)
            {
                if (node != null && node.UT >= now && node.UT < earliest)
                    earliest = node.UT;
            }
            return earliest;
        }
    }
}

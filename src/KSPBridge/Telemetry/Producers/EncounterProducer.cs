namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/encounter</c> topic at 2 Hz.
    ///
    /// Walks the patched-conic chain forward from the vessel's current
    /// orbit. The first patch whose <c>referenceBody</c> differs from
    /// its parent patch's body marks an SOI transition (encounter).
    /// We report the SOI radius of the new body as a reasonable proxy
    /// for "closest approach distance" — it is the distance at which
    /// the encounter physically begins.
    ///
    /// True closest-approach math (solving the minimum-distance problem
    /// across two Keplerian orbits over time) is an order of magnitude
    /// more expensive and isn't what the hard-scifi console actually
    /// renders. If a future consumer needs sub-SOI closest approach we
    /// can extend this producer or split it into a separate topic.
    /// </summary>
    public class EncounterProducer : ITelemetryProducer
    {
        public string TopicSuffix => "encounter";

        public int RateDivisor => 5; // 2 Hz

        // Safety bound on how many patches we walk before giving up.
        // The patched-conic solver is configured in KSP settings to a
        // small number (typically 4–10). 20 is generous and protects
        // against any pathological cycle.
        private const int MaxPatchWalk = 20;

        public object Build(Vessel vessel)
        {
            if (vessel.orbit == null) return null;

            bool hasEncounter = false;
            double closestDist = 0.0;
            string bodyName = "";
            double timeUntil = 0.0;

            Orbit current = vessel.orbit;
            int safety = MaxPatchWalk;
            while (current != null && safety-- > 0)
            {
                Orbit next = current.nextPatch;
                if (next == null) break;

                // SOI transition: the next patch is computed against a
                // different parent body than the current patch. KSP's
                // solver populates this when it predicts capture into
                // (or escape from) another SOI.
                if (next.referenceBody != null &&
                    next.referenceBody != current.referenceBody)
                {
                    hasEncounter = true;

                    // The SOI radius of the entering body is a clean
                    // physical interpretation of "encounter distance" —
                    // it's literally the boundary you cross at the
                    // moment of the encounter.
                    closestDist = next.referenceBody.sphereOfInfluence;
                    bodyName = next.referenceBody.bodyName ?? "";

                    // EndUT on the current patch is the UT at which the
                    // transition happens (the patch ends because the
                    // vessel left the current SOI).
                    timeUntil = current.EndUT - Planetarium.GetUniversalTime();

                    break;
                }

                current = next;
            }

            return new EncounterTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                hasEncounter = hasEncounter,
                closestApproachDistance = closestDist,
                encounterBody = bodyName,
                timeToEncounter = timeUntil,
            };
        }
    }
}

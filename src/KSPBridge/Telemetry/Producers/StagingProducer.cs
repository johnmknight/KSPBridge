using System.Collections.Generic;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/staging</c> topic at 1 Hz, retained.
    ///
    /// The README backlog described this topic as "event-driven"
    /// because it logically describes events (next staging, the
    /// one after that). The implementation, however, polls at
    /// 1 Hz rather than hooking <c>GameEvents.onStageActivate</c>
    /// for two reasons:
    ///
    /// <list type="number">
    ///   <item>
    ///     <strong>Architectural simplicity</strong>. The
    ///     scheduler is a poll loop. Every other producer fits
    ///     that model. A single hand-rolled event listener for
    ///     this one topic would add a special case the rest of
    ///     the codebase doesn't pay for.
    ///   </item>
    ///   <item>
    ///     <strong>Retention covers the late-subscriber case</strong>.
    ///     The "event-driven feel" (a viewer that just connected
    ///     sees current state immediately) is provided by the
    ///     MQTT retain flag — no polling delay matters at all
    ///     to a late subscriber. For an existing subscriber, a
    ///     1 Hz refresh is faster than human reaction time
    ///     anyway.
    ///   </item>
    /// </list>
    ///
    /// If the staging topic ever needs sub-second freshness for
    /// an automated subscriber (an autopilot reacting within a
    /// frame of staging), wire up <c>GameEvents.onStageActivate</c>
    /// in <see cref="KSPBridgePlugin"/> and have it call a public
    /// helper here that publishes immediately. The current
    /// retained-poll design is upgrade-compatible.
    /// </summary>
    public class StagingProducer : ITelemetryProducer, IRetainable
    {
        public string TopicSuffix => "staging";

        // 1 Hz. Staging events are rare; the value between events
        // is "what's about to happen on next press of space",
        // which is a relatively static description.
        public int RateDivisor => 10;

        // Retained so a late subscriber sees the current
        // ready-to-fire stage state without waiting for the
        // next tick — the same rationale as docking/context.
        public bool Retain => true;

        public object Build(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return null;

            int currentStage = vessel.currentStage;

            // Two index buckets matter: the stage that fires next
            // (== currentStage) and the one after that
            // (== currentStage - 1). Anything else is too far
            // out for a "what's happening soon" UI.
            int nextStageParts = 0;
            int nextStageEngines = 0;
            int nextStageDecouplers = 0;
            var nextStageNames = new List<string>();

            int followingStageParts = 0;
            int followingStageEngines = 0;
            int followingStageDecouplers = 0;
            var followingStageNames = new List<string>();

            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null) continue;

                int s = part.inverseStage;
                bool isNext = (s == currentStage);
                bool isFollowing = (s == currentStage - 1);
                if (!isNext && !isFollowing) continue;

                bool hasEngine = HasModule<ModuleEngines>(part);
                bool hasDecoupler =
                    HasModule<ModuleDecouple>(part) ||
                    HasModule<ModuleAnchoredDecoupler>(part);

                string label = !string.IsNullOrEmpty(part.partInfo?.title)
                    ? part.partInfo.title
                    : (part.name ?? "");

                if (isNext)
                {
                    nextStageParts++;
                    if (hasEngine) nextStageEngines++;
                    if (hasDecoupler) nextStageDecouplers++;
                    nextStageNames.Add(label);
                }
                else // isFollowing
                {
                    followingStageParts++;
                    if (hasEngine) followingStageEngines++;
                    if (hasDecoupler) followingStageDecouplers++;
                    followingStageNames.Add(label);
                }
            }

            return new StagingTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,

                currentStage = currentStage,
                // currentStage counts down to 0; +1 gives the count
                // of staging events still possible (one of which is
                // the final fire of stage 0).
                stagesRemaining = currentStage >= 0 ? currentStage + 1 : 0,

                partsInNextStage = nextStageParts,
                enginesInNextStage = nextStageEngines,
                decouplersInNextStage = nextStageDecouplers,
                partsInNextStageNames = nextStageNames,

                partsInFollowingStage = followingStageParts,
                enginesInFollowingStage = followingStageEngines,
                decouplersInFollowingStage = followingStageDecouplers,
                partsInFollowingStageNames = followingStageNames,
            };
        }

        // Reflection-style helper: walk the part's PartModule list
        // and check for at least one instance assignable to T. We
        // avoid LINQ here because part.Modules isn't a generic
        // List<PartModule> — it's KSP's PartModuleList collection
        // type, which works fine with index iteration but not
        // always with extension methods.
        private static bool HasModule<T>(Part part) where T : PartModule
        {
            if (part?.Modules == null) return false;
            for (int i = 0; i < part.Modules.Count; i++)
            {
                if (part.Modules[i] is T) return true;
            }
            return false;
        }
    }
}

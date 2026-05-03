using System;
using System.Collections.Generic;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/staging</c> topic.
    ///
    /// Describes the staging stack from a "what fires when the
    /// pilot next presses space?" point of view rather than
    /// listing every part by inverseStage. Two stage-groups are
    /// reported:
    /// <list type="bullet">
    ///   <item>
    ///     <strong>Next stage</strong> — parts at
    ///     <c>inverseStage == Vessel.currentStage</c>. These are
    ///     the parts that activate on the next staging event:
    ///     decouplers detach, engines ignite, parachutes deploy.
    ///   </item>
    ///   <item>
    ///     <strong>Following stage</strong> — parts at
    ///     <c>inverseStage == Vessel.currentStage - 1</c>. The
    ///     stage AFTER next, useful for "is the next decoupler
    ///     also going to take an engine with it?" UI hints.
    ///   </item>
    /// </list>
    ///
    /// Topic is <strong>retained</strong> (via the
    /// <c>IRetainable</c> companion interface). Staging
    /// transitions are rare and a late subscriber wants the
    /// current ready-to-fire state immediately, not after
    /// waiting up to a second for the next 1 Hz tick.
    ///
    /// Stage indexing convention follows KSP: stage 0 is the
    /// final (landed) stage with nothing left to fire;
    /// <see cref="currentStage"/> is the stage about to fire
    /// when the player next presses space; firing it decrements
    /// the value.
    /// </summary>
    [Serializable]
    public class StagingTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// <c>Vessel.currentStage</c> — the stage that will fire
        /// when the pilot next stages. 0 means there is nothing
        /// left to fire (final stage / payload).
        /// </summary>
        public int currentStage;

        /// <summary>
        /// Number of staging events left in the flight, equal to
        /// <c>currentStage + 1</c> (stages count down from
        /// <c>currentStage</c> through 0 inclusive). 1 if all
        /// remaining stages are spent — the pilot has one more
        /// staging event possible (firing stage 0) before the
        /// stack is fully exhausted.
        /// </summary>
        public int stagesRemaining;

        /// <summary>
        /// Number of parts in the next-to-fire stage group. Sum
        /// of <see cref="enginesInNextStage"/>,
        /// <see cref="decouplersInNextStage"/>, and any other
        /// stageable parts (parachutes, separators, etc.).
        /// </summary>
        public int partsInNextStage;

        /// <summary>
        /// Count of <c>ModuleEngines</c>-bearing parts at
        /// <c>inverseStage == currentStage</c>. These are the
        /// engines that will <strong>ignite</strong> on the next
        /// staging event.
        /// </summary>
        public int enginesInNextStage;

        /// <summary>
        /// Count of <c>ModuleDecouple</c> /
        /// <c>ModuleAnchoredDecoupler</c>-bearing parts at
        /// <c>inverseStage == currentStage</c>. These are the
        /// joins that will <strong>release</strong> on the next
        /// staging event.
        /// </summary>
        public int decouplersInNextStage;

        /// <summary>
        /// Display titles of all parts in the next-to-fire stage
        /// (e.g. <c>"LV-T45 'Swivel' Liquid Fuel Engine"</c>).
        /// Pulled from <c>part.partInfo.title</c>; falls back to
        /// the internal <c>part.name</c> if the title is empty.
        /// </summary>
        public List<string> partsInNextStageNames;

        /// <summary>
        /// Number of parts in the stage <em>after</em> next —
        /// <c>inverseStage == currentStage - 1</c>. Useful for
        /// UI hints like "the next decoupler will jettison the
        /// engine with it." 0 when no further stages remain.
        /// </summary>
        public int partsInFollowingStage;

        /// <summary>Engine count for the following stage. 0 if none.</summary>
        public int enginesInFollowingStage;

        /// <summary>Decoupler count for the following stage. 0 if none.</summary>
        public int decouplersInFollowingStage;

        /// <summary>
        /// Display titles of parts in the following stage. Empty
        /// list if no further stages remain.
        /// </summary>
        public List<string> partsInFollowingStageNames;
    }
}

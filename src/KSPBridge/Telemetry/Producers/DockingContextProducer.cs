using System;
using UnityEngine;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes <c>{prefix}/docking/context</c> when the active vessel
    /// is being controlled from one of its docking ports.
    ///
    /// Runs at 1 Hz — the state this topic describes changes slowly
    /// (target selection, engagement lifecycle transitions, control
    /// reference changes). High-frequency flight data lives on the
    /// other topics; this one is lifecycle-only.
    ///
    /// Retained so a late-subscribing viewer immediately sees the
    /// current docking scenario without waiting up to a second for
    /// the next scheduled tick. Producers opt into retention via the
    /// <see cref="IRetainable"/> companion interface; see that type
    /// for rationale.
    ///
    /// Silent when the active vessel's reference transform is not on
    /// a part that carries a <c>ModuleDockingNode</c>. "Silent" in the
    /// retained-topic sense means we publish nothing — the viewer
    /// keeps seeing the last retained message until we do something
    /// new. For idle state transitions the producer publishes an
    /// <c>idle</c> message explicitly, so the retained value goes
    /// from "in docking scenario" back to "idle, no target" cleanly.
    /// </summary>
    public class DockingContextProducer : ITelemetryProducer, IRetainable
    {
        public string TopicSuffix => "docking/context";

        // 1 Hz. Docking context transitions are rare and UI-scale;
        // higher rates waste broker bandwidth. High-frequency flight
        // data (relative pose, closure rate) belongs on its own
        // dedicated topic.
        public int RateDivisor => 10;

        // Retained so a viewer subscribing mid-flight sees the
        // current state immediately. See IRetainable docs.
        public bool Retain => true;

        public object Build(Vessel vessel)
        {
            if (vessel == null) return null;

            // Find the docking node the active vessel is being
            // controlled from. The simplest heuristic: walk the
            // reference-transform part's modules, take the first
            // ModuleDockingNode we find. Multi-port parts (rare) are
            // disambiguated better by comparing controlTransform to
            // vessel.ReferenceTransform, which we do as a refinement
            // once we know a docking node exists.
            Part refPart = vessel.GetReferenceTransformPart();
            if (refPart == null || refPart.Modules == null) return null;

            ModuleDockingNode ownDN = null;
            int ownIndex = -1;
            int fallbackIndex = -1;
            ModuleDockingNode fallbackDN = null;

            for (int i = 0; i < refPart.Modules.Count; i++)
            {
                var dn = refPart.Modules[i] as ModuleDockingNode;
                if (dn == null) continue;

                // Remember the first docking node we see as a
                // fallback for parts whose controlTransform field
                // doesn't match exactly (some stock and modded parts
                // share the part transform for both).
                if (fallbackDN == null)
                {
                    fallbackDN = dn;
                    fallbackIndex = i;
                }

                // Preferred match: this node's declared control
                // transform equals the vessel's reference transform.
                // Pins the "this port is being controlled from"
                // semantics unambiguously.
                if (dn.controlTransform != null &&
                    vessel.ReferenceTransform != null &&
                    ReferenceEquals(dn.controlTransform, vessel.ReferenceTransform))
                {
                    ownDN = dn;
                    ownIndex = i;
                    break;
                }
            }

            if (ownDN == null)
            {
                ownDN = fallbackDN;
                ownIndex = fallbackIndex;
            }

            // No docking node on the reference part -> not a docking
            // scenario. Producer returns null; the scheduler skips
            // this tick silently. Previously-retained value stays on
            // the broker until the next genuine context.
            if (ownDN == null) return null;

            // Resolve the target. Reuses TargetResolver's filtering
            // (self-target ignored, celestial bodies ignored). Extra
            // step: detect when the target is specifically a docking
            // node (not just a vessel) so we can emit target port
            // details.
            Vessel targetVessel = TargetResolver.Resolve(vessel);

            uint targetPortPersistentId = 0;
            int targetPortModuleIndex = 0;
            ModuleDockingNode targetDN = null;

            var targetObj = vessel.targetObject;
            if (targetObj is ModuleDockingNode tdn &&
                tdn.part != null &&
                tdn.part.Modules != null)
            {
                targetDN = tdn;
                targetPortPersistentId = tdn.part.persistentId;
                // Find the index of the targeted module in its part's
                // Modules list. IndexOf does a reference comparison
                // on object which matches the identity we want.
                for (int i = 0; i < tdn.part.Modules.Count; i++)
                {
                    if (ReferenceEquals(tdn.part.Modules[i], tdn))
                    {
                        targetPortModuleIndex = i;
                        break;
                    }
                }
            }

            return new DockingContextTelemetry
            {
                ownVesselId = vessel.id.ToString(),
                ownVesselPersistentId = vessel.persistentId,
                ownPortPersistentId = refPart.persistentId,
                ownPortModuleIndex = ownIndex,
                targetVesselId = targetVessel != null ? targetVessel.id.ToString() : "",
                targetVesselPersistentId = targetVessel != null ? targetVessel.persistentId : 0u,
                targetPortPersistentId = targetPortPersistentId,
                targetPortModuleIndex = targetPortModuleIndex,
                state = MapState(ownDN, targetDN),
                rawState = ownDN.state ?? "",
            };
        }

        // Simplified state for UI logic. KSP's ModuleDockingNode.state
        // is a free-form string whose exact wording has drifted over
        // versions ("Ready", "Acquire", "Acquire (dockee)", "Docked
        // (docker)", "Docked (dockee)", "Disengage", "Disabled",
        // "PreAttached", ...). We collapse to five buckets that
        // cover every UI decision the viewer needs:
        //
        //   idle       - controlling from a port, no engagement
        //   armed      - target port selected, approach underway
        //   soft_dock  - magnetic acquire in progress
        //   hard_dock  - physically docked
        //   disabled   - port is shielded / inactive
        //
        // Fine-grained variants (which side is docker vs dockee, for
        // example) are exposed via the untouched rawState field so
        // advanced viewers can still discriminate without relying on
        // string matching against a drifting KSP enum.
        private static string MapState(ModuleDockingNode ownDN, ModuleDockingNode targetDN)
        {
            string raw = ownDN.state ?? "";

            if (raw.StartsWith("Docked", StringComparison.Ordinal)) return "hard_dock";
            if (raw.StartsWith("PreAttached", StringComparison.Ordinal)) return "hard_dock";
            if (raw.StartsWith("Acquire", StringComparison.Ordinal)) return "soft_dock";
            if (raw.StartsWith("Disabled", StringComparison.Ordinal)) return "disabled";

            // "Ready", "Disengage", and anything else: fall back to
            // armed/idle based on whether we have a target port.
            if (targetDN != null) return "armed";
            return "idle";
        }
    }
}

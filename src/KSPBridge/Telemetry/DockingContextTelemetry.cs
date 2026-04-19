using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/docking/context</c> topic.
    ///
    /// Describes the current docking scenario: which port on the active
    /// vessel is in control, which port on the target (if any) is being
    /// approached, and where in the engagement lifecycle we are. The
    /// external viewer uses this to decide which part glb anchors to
    /// mount the virtual camera / reticle / alignment aids on.
    ///
    /// Published only when the active vessel's reference transform is
    /// owned by a part carrying at least one <c>ModuleDockingNode</c>
    /// (i.e. the pilot has invoked "Control from Here" on a docking
    /// port). Absence of a recent message means "not in a docking
    /// scenario" — the viewer should hide docking UI.
    /// </summary>
    [Serializable]
    public class DockingContextTelemetry
    {
        /// <summary>
        /// Active vessel's Guid id string. Matches the <c>id</c> field
        /// on the <c>vehicle</c> topic — use it to cross-reference.
        /// </summary>
        public string ownVesselId;

        /// <summary>
        /// Active vessel's persistentId. Matches the <c>vehicle</c>
        /// topic's <c>persistentId</c>.
        /// </summary>
        public uint ownVesselPersistentId;

        /// <summary>
        /// <c>persistentId</c> of the active vessel's part that carries
        /// the docking node currently being controlled from. Use with
        /// <see cref="ownPortModuleIndex"/> to identify the port
        /// uniquely within the KSPEVU glb.
        /// </summary>
        public uint ownPortPersistentId;

        /// <summary>
        /// Index of the controlling <c>ModuleDockingNode</c> within its
        /// part's <c>Modules</c> list. Matches the
        /// <c>extras.dockingPorts[].moduleIndex</c> emitted by KSPEVU.
        /// </summary>
        public int ownPortModuleIndex;

        /// <summary>
        /// Target vessel's Guid id, or empty string if no target or
        /// the target has no vessel (e.g. a celestial body target).
        /// </summary>
        public string targetVesselId;

        /// <summary>Target vessel's persistentId, 0 if no target.</summary>
        public uint targetVesselPersistentId;

        /// <summary>
        /// <c>persistentId</c> of the part on the target vessel that
        /// carries the specific docking node being targeted, or 0 if
        /// the target is a generic vessel (not a specific port).
        /// </summary>
        public uint targetPortPersistentId;

        /// <summary>
        /// Module index of the targeted docking node within its part,
        /// or 0 if <see cref="targetPortPersistentId"/> is 0.
        /// </summary>
        public int targetPortModuleIndex;

        /// <summary>
        /// Coarse-grained engagement state for UI logic. One of:
        /// <list type="bullet">
        ///   <item><c>idle</c> — controlling from a docking port, no target</item>
        ///   <item><c>armed</c> — target port resolved, approach begun but not magnetically engaged</item>
        ///   <item><c>soft_dock</c> — magnetic acquire in progress (KSP "Acquire*" states)</item>
        ///   <item><c>hard_dock</c> — physically docked (KSP "Docked*" / "PreAttached")</item>
        ///   <item><c>disabled</c> — port is shielded or otherwise unavailable</item>
        /// </list>
        /// </summary>
        public string state;

        /// <summary>
        /// Raw <c>ModuleDockingNode.state</c> string from KSP for
        /// debugging and fine-grained UI (e.g. distinguishing "Docked
        /// (docker)" from "Docked (dockee)"). Not stable across KSP
        /// versions — prefer <see cref="state"/> for logic.
        /// </summary>
        public string rawState;
    }
}

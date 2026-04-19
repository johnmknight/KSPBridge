namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Small helper that resolves the active vessel's current target to
    /// a concrete <see cref="Vessel"/> when one exists.
    ///
    /// KSP exposes the target as <c>Vessel.targetObject</c>, typed as the
    /// <c>ITargetable</c> interface. Implementations include:
    ///
    ///   - <see cref="Vessel"/> itself (another spacecraft as target)
    ///   - <c>ModuleDockingNode</c> (a specific docking port on a vessel)
    ///   - <see cref="CelestialBody"/> (the Mun, Duna, etc.)
    ///   - a handful of niche targetables (flags, anomalies, asteroids)
    ///
    /// For target-vessel telemetry we only care about targets with an
    /// associated vessel — i.e. the first two cases. <c>ITargetable.GetVessel()</c>
    /// returns the appropriate vessel for those cases and <c>null</c> for
    /// celestial-body targets, which is exactly the filter we want.
    ///
    /// Self-targeting is filtered out: KSP lets you target your own
    /// docking port (useful for alignment visualization in-game) but
    /// publishing "target vessel" data for your own ship is noise on the
    /// wire, so we skip it.
    /// </summary>
    public static class TargetResolver
    {
        /// <summary>
        /// Returns the vessel currently targeted by <paramref name="active"/>,
        /// or <c>null</c> if there is no target, the target has no associated
        /// vessel (e.g. a celestial body), or the target is the active
        /// vessel itself.
        /// </summary>
        public static Vessel Resolve(Vessel active)
        {
            if (active == null) return null;

            var t = active.targetObject;
            if (t == null) return null;

            // GetVessel() is the ITargetable-native accessor. It returns
            // the correct vessel for Vessel and ModuleDockingNode targets,
            // and null for CelestialBody and other non-vessel targets.
            var target = t.GetVessel();
            if (target == null) return null;

            // Filter out self-targeting. Targeting your own docking port
            // is a legitimate in-game technique for eyeball alignment,
            // but "target vessel = own vessel" is not useful telemetry.
            if (ReferenceEquals(target, active)) return null;

            return target;
        }
    }
}

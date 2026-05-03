namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/situation</c> topic at 1 Hz.
    ///
    /// Expands <c>Vessel.situation</c> (a single enum value) into
    /// one boolean field per state, plus two derived flags
    /// (<c>onSurface</c>, <c>inAtmosphere</c>) and KSP's
    /// <c>IsControllable</c>. UI consumers using this topic can
    /// switch on individual fields rather than parsing the enum
    /// string.
    ///
    /// 1 Hz is more than enough — situation transitions are rare
    /// (one per significant flight event: liftoff, atmosphere
    /// exit, orbit insertion, SOI change, landing). The topic
    /// is not retained: a late subscriber waits at most one
    /// second for a fresh reading.
    /// </summary>
    public class SituationProducer : ITelemetryProducer
    {
        public string TopicSuffix => "situation";

        // 1 Hz. Situation enum changes only at major flight
        // events; higher rates would just retransmit the same
        // payload over and over.
        public int RateDivisor => 10;

        public object Build(Vessel vessel)
        {
            if (vessel == null) return null;

            // Capture the enum once. The boolean expansion is just
            // an explicit equality check per case — there's no
            // bitmask trick here because Vessel.Situations is a
            // plain enum, not [Flags].
            Vessel.Situations s = vessel.situation;

            bool landed = s == Vessel.Situations.LANDED;
            bool splashed = s == Vessel.Situations.SPLASHED;
            bool prelaunch = s == Vessel.Situations.PRELAUNCH;
            bool flying = s == Vessel.Situations.FLYING;
            bool subOrbital = s == Vessel.Situations.SUB_ORBITAL;
            bool orbiting = s == Vessel.Situations.ORBITING;
            bool escaping = s == Vessel.Situations.ESCAPING;
            bool docked = s == Vessel.Situations.DOCKED;

            // Derived: in atmosphere iff there's measurable
            // atmospheric density at our position. atmDensity is
            // 0 in vacuum and on airless bodies, so this single
            // check covers Kerbin / Eve / Duna / Laythe / Jool
            // and excludes Mun / Minmus / Moho / Gilly / etc.
            bool inAtmosphere = vessel.atmDensity > 0.0;

            return new SituationTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,

                situation = s.ToString(),

                landed = landed,
                splashed = splashed,
                prelaunch = prelaunch,
                flying = flying,
                subOrbital = subOrbital,
                orbiting = orbiting,
                escaping = escaping,
                docked = docked,

                onSurface = landed || splashed || prelaunch,
                inAtmosphere = inAtmosphere,
                controllable = vessel.IsControllable,
            };
        }
    }
}

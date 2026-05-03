namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/atmosphere</c> topic at 5 Hz.
    ///
    /// Reads the vessel's current atmospheric environment directly
    /// from KSP-provided fields — no math beyond NaN cleansing.
    /// During ascent through Kerbin's atmosphere these values
    /// change quickly enough (max-Q happens around 8-10 km, often
    /// inside a few seconds) that 2 Hz would be choppy on a HUD.
    /// 5 Hz lines up well with the <c>dynamics</c> topic so a
    /// re-entry display can correlate density, pressure, and
    /// g-load on the same beat.
    /// </summary>
    public class AtmosphereProducer : ITelemetryProducer
    {
        public string TopicSuffix => "atmosphere";

        // 5 Hz. Atmospheric values change visibly during ascent
        // and re-entry; matches the dynamics topic cadence so
        // consumers can correlate ρ / pressure / g-load on the
        // same scheduler beat.
        public int RateDivisor => 2;

        public object Build(Vessel vessel)
        {
            if (vessel == null) return null;

            // Body context. mainBody can in principle be null
            // very briefly during scene transitions; guard.
            CelestialBody body = vessel.mainBody;
            bool bodyHasAtmosphere = body != null && body.atmosphere;
            double atmosphereDepth = body != null ? body.atmosphereDepth : 0.0;

            return new AtmosphereTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,

                // Vessel.atmDensity is already in kg/m³.
                density = SafeDouble(vessel.atmDensity),

                // Pressures: KSP exposes both atm-units and kPa
                // accessors. We use the kPa variants directly so
                // the wire format requires no client-side
                // multiplication by 101.325.
                staticPressure = SafeDouble(vessel.staticPressurekPa),
                dynamicPressure = SafeDouble(vessel.dynamicPressurekPa),

                // Two distinct temperatures KSP tracks separately.
                atmosphereTemperature = SafeDouble(vessel.atmosphericTemperature),
                externalTemperature = SafeDouble(vessel.externalTemperature),

                mach = SafeDouble(vessel.mach),

                inAtmosphere = vessel.atmDensity > 0.0,
                bodyHasAtmosphere = bodyHasAtmosphere,
                atmosphereDepth = SafeDouble(atmosphereDepth),
            };
        }

        // Replace NaN / ±∞ with 0 — JsonUtility emits non-finite
        // doubles as literal "NaN" / "Infinity" tokens, which are
        // not valid JSON per RFC 8259 and break strict parsers.
        private static double SafeDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v;
        }
    }
}

using System;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/orbit</c> topic at 2 Hz.
    /// Carries the six classical orbital elements plus convenience fields
    /// (Ap/Pe in both centre-radius and surface-elevation forms, period,
    /// time-to-Ap/Pe, parent body bulk properties).
    ///
    /// KSP exposes inclination/LAN/argumentOfPeriapsis in degrees; we
    /// convert to radians here so the wire format matches KSA-Bridge.
    /// </summary>
    public class OrbitProducer : ITelemetryProducer
    {
        public string TopicSuffix => "orbit";

        // 2 Hz. Orbital elements change slowly; 10 Hz would be wasteful.
        // 5 base ticks at 0.1 s each = 0.5 s = 2 Hz.
        public int RateDivisor => 5;

        // Eccentricity threshold below which we report "CIRCULAR" rather
        // than "ELLIPTICAL". 0.01 is the conventional astrodynamics
        // tolerance — orbits below this are visually indistinguishable
        // from circles.
        private const double CircularEccentricityThreshold = 0.01;

        // Tolerance for declaring an orbit parabolic (e ≈ 1). KSP almost
        // never produces exactly parabolic orbits, but the math goes
        // singular very close to e=1, so we widen the bucket slightly.
        private const double ParabolicEccentricityTolerance = 1e-4;

        public object Build(Vessel vessel)
        {
            if (vessel.orbit == null || vessel.mainBody == null) return null;

            var orbit = vessel.orbit;
            var body = vessel.mainBody;
            double e = orbit.eccentricity;
            double a = orbit.semiMajorAxis;

            // Semi-minor axis magnitude works for both bound and unbound
            // orbits via the absolute-value form. Mathematically:
            //   ellipse: b = a · sqrt(1 - e²)
            //   hyperbola: b = |a| · sqrt(e² - 1)
            // |a| · sqrt(|1 - e²|) collapses both into one expression.
            double b = Math.Abs(a) * Math.Sqrt(Math.Abs(1.0 - e * e));

            return new OrbitTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,

                // ApR / PeR = radius from parent body centre.
                apoapsis = orbit.ApR,
                periapsis = orbit.PeR,
                // ApA / PeA = altitude above mean surface (radius - body radius).
                apoapsisElevation = orbit.ApA,
                periapsisElevation = orbit.PeA,

                eccentricity = e,
                // KSP returns these three angles in degrees; the wire format
                // is radians. π/180 conversion.
                inclination = orbit.inclination * Math.PI / 180.0,
                longitudeOfAscendingNode = orbit.LAN * Math.PI / 180.0,
                argumentOfPeriapsis = orbit.argumentOfPeriapsis * Math.PI / 180.0,

                period = orbit.period,
                semiMajorAxis = a,
                semiMinorAxis = b,

                timeToApoapsis = orbit.timeToAp,
                timeToPeriapsis = orbit.timeToPe,
                orbitType = ClassifyOrbit(e),

                parentRadius = body.Radius,
                parentMass = body.Mass,
            };
        }

        // Classify the orbit shape from eccentricity. The thresholds are
        // visual / pragmatic, not strictly mathematical — a real orbit
        // with e=0.005 is technically elliptical but indistinguishable
        // from a circle on any practical display.
        private static string ClassifyOrbit(double eccentricity)
        {
            if (eccentricity < CircularEccentricityThreshold)
                return "CIRCULAR";
            if (Math.Abs(eccentricity - 1.0) < ParabolicEccentricityTolerance)
                return "PARABOLIC";
            if (eccentricity < 1.0)
                return "ELLIPTICAL";
            return "HYPERBOLIC";
        }
    }
}

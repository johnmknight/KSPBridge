using System;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/atmosphere</c> topic.
    ///
    /// Reports the atmospheric environment around the vessel:
    /// density, static and dynamic pressures, ambient and external
    /// (heated) temperatures, Mach number, and a couple of context
    /// flags about the parent body's atmosphere. Intended for
    /// re-entry HUDs, wing-loading meters, parachute-arming logic,
    /// and any consumer that needs to know "is there air here, and
    /// how dense is it?"
    ///
    /// Pressures are in <strong>kilopascals</strong> (KSP's native
    /// unit for these fields). 1 atm at sea level on Kerbin is
    /// 101.325 kPa.
    ///
    /// Temperatures are in <strong>kelvin</strong>.
    /// <see cref="atmosphereTemperature"/> is the ambient air
    /// temperature; <see cref="externalTemperature"/> includes
    /// heating from re-entry / aerodynamic effects and so reads
    /// higher when moving fast through dense atmosphere.
    /// </summary>
    [Serializable]
    public class AtmosphereTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// Atmospheric mass density at the vessel's position
        /// (kg/m³). 0 in vacuum and on airless bodies.
        /// </summary>
        public double density;

        /// <summary>
        /// Local static (ambient) pressure (kPa). 0 in vacuum.
        /// </summary>
        public double staticPressure;

        /// <summary>
        /// Dynamic pressure — <c>0.5 * ρ * v²</c> using the
        /// vessel's surface velocity (kPa). The "Q" pilots
        /// monitor for max-Q during ascent.
        /// </summary>
        public double dynamicPressure;

        /// <summary>
        /// Ambient atmospheric temperature (K). Independent of
        /// vessel motion — this is the temperature of the air
        /// before any aerodynamic heating.
        /// </summary>
        public double atmosphereTemperature;

        /// <summary>
        /// External temperature (K). Includes aerodynamic /
        /// re-entry heating; reads above
        /// <see cref="atmosphereTemperature"/> when the vessel is
        /// moving quickly through dense atmosphere.
        /// </summary>
        public double externalTemperature;

        /// <summary>
        /// Mach number — vessel's surface speed divided by the
        /// local speed of sound. 0 in vacuum (no sound to compare
        /// against). Subsonic &lt;1, supersonic &gt;1.
        /// </summary>
        public double mach;

        /// <summary>
        /// True iff <see cref="density"/> is non-zero — i.e. the
        /// vessel is currently in atmosphere thick enough to
        /// produce drag and heating. Always false on airless bodies.
        /// </summary>
        public bool inAtmosphere;

        /// <summary>
        /// True iff the parent body has any atmosphere at all
        /// (<c>CelestialBody.atmosphere</c>). Useful for UI logic
        /// that wants to grey-out atmosphere-related controls
        /// outside the body's atmosphere — distinct from
        /// <see cref="inAtmosphere"/> which depends on altitude.
        /// </summary>
        public bool bodyHasAtmosphere;

        /// <summary>
        /// Top of the parent body's atmosphere (m above mean
        /// surface). 0 if the body has no atmosphere. The vessel
        /// is in atmosphere whenever altitude &lt; this value
        /// AND <see cref="bodyHasAtmosphere"/> is true.
        /// </summary>
        public double atmosphereDepth;
    }
}

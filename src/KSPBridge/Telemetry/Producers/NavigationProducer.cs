namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/navigation</c> topic at 10 Hz.
    /// Cheap to compute — every field is a direct KSP property access.
    /// </summary>
    public class NavigationProducer : ITelemetryProducer
    {
        public string TopicSuffix => "navigation";

        // 10 Hz. Matches KSA-Bridge cadence. Altitude and speed change
        // fast enough during ascent / re-entry that 2 Hz would look
        // choppy on a live readout.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            // vessel.altitude is altitude above mean sea level of the
            // current SOI parent body, in metres. Can go negative when
            // below "sea level" (e.g., inside a crater on an airless body
            // if the terrain dips below body.Radius).
            double altM = vessel.altitude;

            return new NavigationTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                altitude = altM,
                altitudeKm = altM / 1000.0,
                // srf_velocity = velocity in the rotating surface frame;
                // its magnitude is the ground speed a pilot would read.
                speed = vessel.srf_velocity.magnitude,
                // obt_velocity = velocity in the non-rotating inertial
                // frame; its magnitude is the true orbital speed.
                orbitalSpeed = vessel.obt_velocity.magnitude,
            };
        }
    }
}

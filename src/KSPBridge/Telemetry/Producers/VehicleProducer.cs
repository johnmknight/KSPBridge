namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/vehicle</c> topic at 10 Hz.
    /// Schema deliberately mirrors KSA-Bridge's vehicle topic so the same
    /// browser console / dashboard code works against either game with
    /// only a topic-prefix change. Two non-KSA fields (<c>id</c>,
    /// <c>persistentId</c>) are added on top to make stable per-vessel
    /// correlation possible — KSP vessel names are not unique.
    /// </summary>
    public class VehicleProducer : ITelemetryProducer
    {
        public string TopicSuffix => "vehicle";

        // 10 Hz = every base tick. Matches KSA-Bridge's vehicle cadence.
        public int RateDivisor => 1;

        public object Build(Vessel vessel)
        {
            return new VehicleTelemetry
            {
                // Both KSP identifiers are emitted so consumers can key on
                // whichever suits them. Guid for cross-save correlation,
                // persistentId for cheap-to-type within-save uniqueness.
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                vehicleName = vessel.vesselName ?? "",
                parentBody = vessel.mainBody != null ? vessel.mainBody.bodyName : "",
                // Vessel.Situations is an enum — ToString() gives us the
                // readable name (ORBITING, LANDED, etc.) rather than an int.
                situation = vessel.situation.ToString(),
                // obt_velocity is the vessel's velocity vector in the
                // inertial frame of the current SOI's parent body. Its
                // magnitude is the orbital speed in m/s. KSA-Bridge names
                // this field "speed" on the vehicle topic.
                speed = vessel.obt_velocity.magnitude,
            };
        }
    }
}

using System.Collections.Generic;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/resources</c> topic at 2 Hz.
    ///
    /// Iterates every part on the vessel and aggregates resources
    /// by canonical name into a single per-vessel breakdown. A
    /// rocket with six liquid-fuel tanks emits one
    /// <c>"LiquidFuel"</c> entry summing all six, not six entries
    /// with the same name.
    ///
    /// Mass conversions: KSP stores resource densities in
    /// tons-per-unit and vessel masses in tons. The producer
    /// converts everything to kilograms on the wire so the
    /// <c>resources</c> topic and the <c>performance</c> topic
    /// agree on units (both emit <c>mass</c> in kg).
    /// </summary>
    public class ResourcesProducer : ITelemetryProducer
    {
        public string TopicSuffix => "resources";

        // 2 Hz. Resource amounts change continuously during burns
        // but a half-second refresh is plenty for a fuel readout
        // or staging gauge. Higher rates would add wire traffic
        // without changing what a human or autopilot decides on.
        public int RateDivisor => 5;

        public object Build(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return null;

            // Aggregate by canonical resource name. We use a small
            // local accumulator class rather than a tuple so the
            // intent is self-documenting at allocation sites.
            var byName = new Dictionary<string, ResourceAccumulator>();

            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null || part.Resources == null) continue;

                for (int j = 0; j < part.Resources.Count; j++)
                {
                    PartResource r = part.Resources[j];
                    if (r == null || r.info == null) continue;

                    string name = r.info.name ?? "";
                    if (!byName.TryGetValue(name, out var acc))
                    {
                        acc = new ResourceAccumulator
                        {
                            name = name,
                            // Density is constant per resource type, so we
                            // just take it from the first part we see for
                            // this resource and trust that all subsequent
                            // parts agree.
                            densityTPerUnit = r.info.density,
                        };
                        byName[name] = acc;
                    }
                    acc.amount += r.amount;
                    acc.maxAmount += r.maxAmount;
                }
            }

            // Materialise into the wire-format list. Total resource
            // mass is summed alongside so we can report it as a
            // top-level field without a second pass.
            var entries = new List<ResourceEntry>(byName.Count);
            double totalResourceMassTons = 0.0;
            foreach (var acc in byName.Values)
            {
                double massTons = acc.amount * acc.densityTPerUnit;
                totalResourceMassTons += massTons;

                entries.Add(new ResourceEntry
                {
                    name = acc.name,
                    amount = SafeDouble(acc.amount),
                    maxAmount = SafeDouble(acc.maxAmount),
                    // KSP density is t/unit; convert to kg/unit so
                    // consumers don't need to remember which fields
                    // are in which units.
                    density = SafeDouble(acc.densityTPerUnit * 1000.0),
                    mass = SafeDouble(massTons * 1000.0),
                });
            }

            // Compute dry mass from wet (= Vessel.totalMass) minus
            // total resource mass. Numerical error can in principle
            // push this slightly negative; clamp to zero rather
            // than emit a confusing -0.0001 kg dry mass.
            double wetMassTons = vessel.totalMass;
            double dryMassTons = wetMassTons - totalResourceMassTons;
            if (dryMassTons < 0.0) dryMassTons = 0.0;

            return new ResourcesTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                wetMass = SafeDouble(wetMassTons * 1000.0),
                dryMass = SafeDouble(dryMassTons * 1000.0),
                resourceMass = SafeDouble(totalResourceMassTons * 1000.0),
                resources = entries,
            };
        }

        // Replace NaN / ±∞ with 0 — JsonUtility emits non-finite
        // doubles as "NaN" / "Infinity" tokens that break strict
        // JSON parsers downstream.
        private static double SafeDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v;
        }

        // Internal helper for the per-name aggregation pass.
        // Held as a class (not a struct) so the dictionary entry
        // remains a single shared reference we can mutate
        // in-place across multiple part visits.
        private class ResourceAccumulator
        {
            public string name;
            public double densityTPerUnit;
            public double amount;
            public double maxAmount;
        }
    }
}

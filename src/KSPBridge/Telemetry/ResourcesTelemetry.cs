using System;
using System.Collections.Generic;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Payload shape for the <c>{prefix}/resources</c> topic.
    ///
    /// Reports vessel-wide totals (wet mass, dry mass, total
    /// resource mass) plus a per-resource breakdown aggregated
    /// across every part on the vessel. A six-tank rocket reads
    /// as one entry per resource type — matching what a pilot
    /// sees in the stock resource panel — rather than six
    /// separate entries with the same name.
    ///
    /// Mass fields are in <strong>kilograms</strong> for
    /// consistency with the rest of the schema. KSP internally
    /// works in tons; the producer multiplies by 1000 before
    /// emitting.
    /// </summary>
    [Serializable]
    public class ResourcesTelemetry
    {
        public string id;
        public uint persistentId;

        /// <summary>
        /// Total (wet) vessel mass in kilograms. Equivalent to
        /// <c>Vessel.totalMass * 1000</c>.
        /// </summary>
        public double wetMass;

        /// <summary>
        /// Dry mass in kilograms — <c>wetMass - resourceMass</c>.
        /// Clamped to zero when (rare) numerical error pushes the
        /// difference slightly negative.
        /// </summary>
        public double dryMass;

        /// <summary>
        /// Sum of <c>mass</c> across every entry in <see cref="resources"/>,
        /// in kilograms. Provided as a top-level convenience so
        /// consumers don't need to walk the list and sum themselves.
        /// </summary>
        public double resourceMass;

        /// <summary>
        /// Per-resource breakdown, one entry per distinct resource
        /// name aggregated across the entire vessel. Zero-amount
        /// resources are still emitted if any part lists the
        /// resource definition (so consumers see capacity even when
        /// empty).
        /// </summary>
        public List<ResourceEntry> resources;
    }

    /// <summary>
    /// One row of <see cref="ResourcesTelemetry.resources"/>.
    /// JsonUtility serialises this verbatim — keep it
    /// <c>[Serializable]</c> with public fields only.
    /// </summary>
    [Serializable]
    public class ResourceEntry
    {
        /// <summary>
        /// Canonical resource name, e.g. <c>"LiquidFuel"</c>,
        /// <c>"Oxidizer"</c>, <c>"MonoPropellant"</c>,
        /// <c>"ElectricCharge"</c>, <c>"XenonGas"</c>, <c>"Ore"</c>.
        /// Matches <c>PartResourceDefinition.name</c>; consumers
        /// that want pretty labels should map client-side.
        /// </summary>
        public string name;

        /// <summary>Current amount in resource units.</summary>
        public double amount;

        /// <summary>Maximum capacity in resource units.</summary>
        public double maxAmount;

        /// <summary>
        /// Density in <strong>kilograms per unit</strong>. KSP
        /// stores density as tons-per-unit; the producer multiplies
        /// by 1000 before emitting so all mass-bearing fields on
        /// this topic share kg as their unit.
        /// </summary>
        public double density;

        /// <summary>
        /// Mass of the current amount in kilograms —
        /// <c>amount * density</c>, pre-computed for consumer
        /// convenience.
        /// </summary>
        public double mass;
    }
}

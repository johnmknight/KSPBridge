using System;

namespace KSPBridge.Telemetry.Producers
{
    /// <summary>
    /// Publishes the <c>{prefix}/performance</c> topic at 2 Hz.
    ///
    /// KSP 1.12 added a stock ΔV calculator (<see cref="VesselDeltaV"/>)
    /// that handles stage analysis, engine priorities, fuel crossfeed,
    /// and atmospheric Isp adjustments. We lean on it heavily rather
    /// than re-implementing the rocket equation from scratch — stock
    /// covers way more edge cases (asparagus staging, ullage, varying
    /// Isp curves) than a hand-rolled version would.
    ///
    /// The calculator can legitimately return NaN / ∞ during transients:
    /// no engines yet unlocked in career mode, an empty stage about to
    /// be discarded, or the few frames after staging before the solver
    /// re-converges. We filter those values out and emit 0 instead so
    /// downstream consumers don't have to handle non-finite JSON numbers.
    /// </summary>
    public class PerformanceProducer : ITelemetryProducer
    {
        public string TopicSuffix => "performance";

        // 2 Hz. Performance figures change slowly under normal flight —
        // only really during staging, throttle changes, or Isp shifts
        // from atmospheric density. 10 Hz would be overkill.
        public int RateDivisor => 5;

        public object Build(Vessel vessel)
        {
            VesselDeltaV vdv = vessel.VesselDeltaV;

            // Calculator can be null on vessels that don't support it
            // (e.g., debris after separation, EVA kerbals). Emit zeros
            // so the console still renders sensibly rather than
            // freezing the last finite reading.
            if (vdv == null)
            {
                return new PerformanceTelemetry
                {
                    id = vessel.id.ToString(),
                    persistentId = vessel.persistentId,
                    mass = SafeDouble(vessel.totalMass * 1000.0), // tons → kg
                    thrust = SumActiveThrust(vessel),
                };
            }

            // Find the DeltaVStageInfo entry for the current stage, if
            // one exists. OperatingStageInfo is an ordered list from
            // current-to-last stage, but we match explicitly on stage
            // number to tolerate future-stock reorderings.
            double stageTwr = 0.0;
            double stageDv = 0.0;
            if (vdv.OperatingStageInfo != null)
            {
                foreach (var s in vdv.OperatingStageInfo)
                {
                    if (s == null) continue;
                    if (s.stage == vessel.currentStage)
                    {
                        stageTwr = SafeDouble(s.TWRActual);
                        stageDv = SafeDouble(s.deltaVActual);
                        break;
                    }
                }

                // Fallback: if no entry matched currentStage (can happen
                // briefly during stage transitions), just use the first
                // entry so we emit *something* useful rather than zeros.
                if (stageTwr == 0.0 && stageDv == 0.0 &&
                    vdv.OperatingStageInfo.Count > 0)
                {
                    var first = vdv.OperatingStageInfo[0];
                    if (first != null)
                    {
                        stageTwr = SafeDouble(first.TWRActual);
                        stageDv = SafeDouble(first.deltaVActual);
                    }
                }
            }

            return new PerformanceTelemetry
            {
                id = vessel.id.ToString(),
                persistentId = vessel.persistentId,
                deltaV = SafeDouble(vdv.TotalDeltaVActual),
                deltaVVac = SafeDouble(vdv.TotalDeltaVVac),
                deltaVAsl = SafeDouble(vdv.TotalDeltaVASL),
                twr = stageTwr,
                currentStageDeltaV = stageDv,
                mass = SafeDouble(vessel.totalMass * 1000.0),
                thrust = SumActiveThrust(vessel),
            };
        }

        // Replace NaN / ±∞ with 0. JsonUtility will happily serialise
        // non-finite doubles as "NaN" / "Infinity" tokens, which aren't
        // valid JSON per RFC 8259 and break strict parsers. Easier to
        // cleanse at the source.
        private static double SafeDouble(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v;
        }

        // Sum current thrust (in Newtons) over all ignited engine
        // modules on the vessel. finalThrust already accounts for
        // throttle and atmospheric Isp, so no further scaling needed.
        private static double SumActiveThrust(Vessel vessel)
        {
            double total = 0.0;
            if (vessel.parts == null) return 0.0;

            foreach (var part in vessel.parts)
            {
                if (part == null || part.Modules == null) continue;
                foreach (var module in part.Modules)
                {
                    var engine = module as ModuleEngines;
                    if (engine == null) continue;
                    if (!engine.EngineIgnited) continue;
                    // finalThrust is in kN — multiply by 1000 for N.
                    total += engine.finalThrust * 1000.0;
                }
            }
            return SafeDouble(total);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using KSPBridge.Mqtt;
using KSPBridge.Telemetry.Producers;
using UnityEngine;

namespace KSPBridge.Telemetry
{
    /// <summary>
    /// Drives periodic telemetry publishing. Runs as a Unity coroutine on
    /// the main thread — this is the only safe way to touch KSP / Unity
    /// APIs like <c>FlightGlobals.ActiveVessel</c>, which are not thread-safe.
    ///
    /// The scheduler ticks at a fixed base rate (10 Hz). Each registered
    /// <see cref="ITelemetryProducer"/> publishes at a rate determined by
    /// its <see cref="ITelemetryProducer.RateDivisor"/>: divisor=1 fires
    /// every tick (10 Hz), divisor=5 every fifth tick (2 Hz), and so on.
    /// All producers share a single tick counter so divisors phase-align —
    /// a 10 Hz topic and a 2 Hz topic will always co-tick on the slower
    /// topic's beat, which makes downstream correlation easier.
    ///
    /// Scene awareness: every tick we check <c>HighLogic.LoadedSceneIsFlight</c>
    /// and <c>FlightGlobals.ActiveVessel</c>. If either condition isn't
    /// satisfied we silently skip the tick — no stale data, no noisy errors,
    /// and the telemetry topics simply stop updating outside the flight
    /// scene. The <c>_bridge/status</c> heartbeat continues regardless.
    /// </summary>
    public class TelemetryScheduler
    {
        // 10 Hz = 0.1 s per tick. Producers publish at this rate or some
        // integer fraction of it (see ITelemetryProducer.RateDivisor).
        private const float TickInterval = 0.1f;

        private readonly MqttBridge _bridge;
        private readonly MonoBehaviour _host;

        // Producer registry. Adding a new telemetry topic is a one-file
        // change: implement ITelemetryProducer in Producers/, then add
        // an instance to this list. Order doesn't matter — divisor
        // governs cadence, not list position.
        private readonly List<ITelemetryProducer> _producers;

        // Tick counter, monotonically increasing while the scheduler runs.
        // Used for rate division (count % producer.RateDivisor == 0).
        // long is overkill but means we can't possibly overflow within any
        // reasonable game session.
        private long _tickCount;

        private Coroutine _loop;

        public TelemetryScheduler(MqttBridge bridge, MonoBehaviour host)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _host = host ?? throw new ArgumentNullException(nameof(host));

            _producers = new List<ITelemetryProducer>
            {
                new VehicleProducer(),
                new StateVectorsProducer(),
                new OrbitProducer(),
                new NavigationProducer(),
                new AttitudeProducer(),
                new ParentBodyProducer(),
                new ManeuverProducer(),
                new EncounterProducer(),
                new PerformanceProducer(),

                // Target-vessel telemetry. Each of these publishes only
                // when the active vessel has a vessel-bearing target
                // selected; otherwise their Build() returns null and the
                // scheduler silently skips the tick. Schema matches the
                // corresponding active-vessel topic, so a console that
                // understands vehicle / state_vectors / attitude can
                // consume the target/* variants with zero extra parsing.
                new TargetVehicleProducer(),
                new TargetStateVectorsProducer(),
                new TargetAttitudeProducer(),

                // Docking-scenario lifecycle. Retained (via IRetainable)
                // so a late subscriber immediately sees the current
                // engagement state. Low rate (1 Hz) — this topic
                // describes UI-scale state transitions, not flight
                // dynamics.
                new DockingContextProducer(),

                // Future producers added here — one line per topic.
            };
        }

        /// <summary>Begin publishing. Idempotent — calling twice is safe.</summary>
        public void Start()
        {
            if (_loop != null) return;
            _loop = _host.StartCoroutine(Tick());
            Debug.Log(
                $"{KSPBridgePlugin.LogPrefix} telemetry scheduler started at " +
                $"{1f / TickInterval:F1} Hz, {_producers.Count} producer(s) registered");
        }

        /// <summary>Stop publishing. Idempotent.</summary>
        public void Stop()
        {
            if (_loop == null) return;
            _host.StopCoroutine(_loop);
            _loop = null;
            Debug.Log($"{KSPBridgePlugin.LogPrefix} telemetry scheduler stopped");
        }

        // The main loop. A Unity coroutine that yields every TickInterval
        // seconds, then walks the producer list and publishes any whose
        // divisor matches this tick.
        private IEnumerator Tick()
        {
            // WaitForSeconds allocates once here and is reused on every
            // yield. Allocating per-tick would cause constant GC churn at
            // 10 Hz, easily measurable in profilers.
            var wait = new WaitForSeconds(TickInterval);

            while (true)
            {
                yield return wait;
                _tickCount++;

                // Scene gate: telemetry is only meaningful in flight. On
                // the main menu, KSC, editor, and tracking station there
                // is no vessel to describe, so we publish nothing.
                if (!HighLogic.LoadedSceneIsFlight)
                    continue;

                Vessel vessel = FlightGlobals.ActiveVessel;
                if (vessel == null)
                    continue;

                // Walk producers in registration order. Each producer is
                // independently rate-gated and independently fault-isolated
                // (one exception cannot disrupt other producers or future
                // ticks).
                foreach (var producer in _producers)
                {
                    if (producer.RateDivisor <= 0)
                        continue; // disabled

                    if (_tickCount % producer.RateDivisor != 0)
                        continue; // not our turn this tick

                    PublishOne(producer, vessel);
                }
            }
        }

        // Build, serialise, and publish a single producer's payload.
        // Wrapped in try/catch so one producer's bug can't halt the loop
        // or starve other producers of their ticks.
        private void PublishOne(ITelemetryProducer producer, Vessel vessel)
        {
            try
            {
                object payload = producer.Build(vessel);
                if (payload == null)
                    return; // producer opted out this tick

                // JsonUtility.ToJson works on object — it serialises
                // based on the runtime type's [Serializable] public fields.
                string json = JsonUtility.ToJson(payload);

                // Retain: default false (streaming telemetry — new
                // subscribers should wait for the next publish rather
                // than be handed a stale frame). Producers that own
                // slow-changing lifecycle state opt into retention by
                // implementing IRetainable; the scheduler flips the
                // flag on their publishes. Bridge meta topics (status)
                // handle their own retention independently inside
                // MqttBridge.
                bool retain = producer is IRetainable retainable && retainable.Retain;
                _bridge.Publish(producer.TopicSuffix, json, retain: retain);
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"{KSPBridgePlugin.LogPrefix} producer '{producer.TopicSuffix}' " +
                    $"failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}

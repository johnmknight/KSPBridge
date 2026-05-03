using KSPBridge.Mqtt;
using KSPBridge.Telemetry;
using UnityEngine;

namespace KSPBridge
{
    /// <summary>
    /// KSPBridge entry point.
    ///
    /// KSPAddon tells KSP when to instantiate this MonoBehaviour. We use
    /// Startup.Instantly (the earliest hook) combined with <c>once: true</c>
    /// so a single instance is created very early in the game's lifecycle.
    /// The <c>DontDestroyOnLoad</c> call in Awake keeps it alive across all
    /// scene transitions (main menu, KSC, flight, tracking station, editor),
    /// which matches KSA-Bridge's "always-on telemetry daemon" model.
    ///
    /// This phase-2 version connects to the configured MQTT broker on
    /// startup and publishes a heartbeat every <see cref="HeartbeatSeconds"/>
    /// seconds to <c>{topic_prefix}/_bridge/status</c>. Telemetry topics
    /// proper come online in phase 3.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPBridgePlugin : MonoBehaviour
    {
        /// <summary>Human-readable plugin version. Update on each release.</summary>
        public const string Version = "0.15.1";

        /// <summary>
        /// Prefix used in every log line this plugin emits, so they are easy
        /// to grep for in Player.log among the hundreds of other sources.
        /// </summary>
        public const string LogPrefix = "[KSPBridge]";

        /// <summary>Heartbeat publish interval in seconds.</summary>
        private const float HeartbeatSeconds = 5f;

        // Cached settings, populated in Start().
        private Settings _settings;

        // The MQTT bridge. Created in Start, disposed in OnDestroy.
        private MqttBridge _bridge;

        // Telemetry scheduler. Runs a 10 Hz Unity coroutine that publishes
        // per-topic payloads when in the flight scene.
        private TelemetryScheduler _scheduler;

        /// <summary>
        /// Awake runs once immediately after the MonoBehaviour is created,
        /// before any Start() calls. This is the right place to register
        /// DontDestroyOnLoad so the object survives scene changes.
        /// </summary>
        public void Awake()
        {
            DontDestroyOnLoad(this);
            Debug.Log($"{LogPrefix} plugin loaded, version {Version}");
        }

        /// <summary>
        /// Start runs once after Awake. We defer settings loading and MQTT
        /// connection to here so that any KSP systems the loader depends on
        /// (ConfigNode parsing, filesystem APIs) are fully initialised.
        /// </summary>
        public void Start()
        {
            _settings = Settings.Load();
            Debug.Log(
                $"{LogPrefix} broker = {_settings.BrokerHost}:{_settings.BrokerPort}, " +
                $"prefix = {_settings.TopicPrefix}, client_id = {_settings.ClientId}");

            _bridge = new MqttBridge(_settings);
            _bridge.Start();

            // Kick off the heartbeat. InvokeRepeating runs on the Unity main
            // thread, which is safe because MqttBridge.PublishHeartbeat only
            // enqueues a message — it never blocks on the network.
            InvokeRepeating(nameof(HeartbeatTick), HeartbeatSeconds, HeartbeatSeconds);

            // Telemetry producers tick on a separate Unity coroutine so that
            // heartbeat and telemetry cadences stay independent. The
            // scheduler is scene-aware and silently idles outside flight.
            _scheduler = new TelemetryScheduler(_bridge, this);
            _scheduler.Start();
        }

        /// <summary>
        /// Publishes one heartbeat message. Invoked by Unity's
        /// InvokeRepeating scheduler on the main thread.
        /// </summary>
        private void HeartbeatTick()
        {
            _bridge?.PublishHeartbeat();
        }

        /// <summary>
        /// Called by Unity when this MonoBehaviour is being destroyed. With
        /// DontDestroyOnLoad this only happens on game shutdown, giving us a
        /// chance to publish a final "offline" status and close the MQTT
        /// connection cleanly.
        /// </summary>
        public void OnDestroy()
        {
            CancelInvoke(nameof(HeartbeatTick));
            _scheduler?.Stop();
            _scheduler = null;
            _bridge?.Dispose();
            _bridge = null;
        }
    }
}

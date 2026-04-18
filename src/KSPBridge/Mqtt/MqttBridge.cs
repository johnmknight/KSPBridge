using System;
using System.Text;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Extensions.ManagedClient;
using MQTTnet.Protocol;
using UnityEngine;

namespace KSPBridge.Mqtt
{
    /// <summary>
    /// Owns the MQTT connection to the broker. Wraps MQTTnet's
    /// <see cref="IManagedMqttClient"/>, which handles auto-reconnect,
    /// outbound message queuing while disconnected, and connection lifecycle
    /// events. We never block the Unity main thread on network I/O — every
    /// publish call enqueues a message and returns immediately, and the
    /// managed client drains the queue from its own internal threads.
    /// </summary>
    public class MqttBridge : IDisposable
    {
        // Sub-topic suffix for the bridge's own status messages. Underscore
        // prefix keeps it visually distinct from telemetry topics.
        private const string StatusSubTopic = "_bridge/status";

        // How long to wait between reconnect attempts when the broker is
        // unreachable. Aggressive enough that the bridge feels responsive,
        // forgiving enough that we don't hammer a down broker.
        private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

        // MQTT keep-alive interval. We ping the broker this often to prove
        // the connection is alive; the broker times out a client that misses
        // ~1.5x this interval. Short keep-alive means the broker will publish
        // our LWT quickly after an unclean disconnect (crash, force-quit,
        // network drop) — roughly 7-8s instead of the default ~22s. The cost
        // is a tiny pulse of traffic every 5s, which is negligible at MQTT's
        // wire-format sizes.
        private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(5);

        private readonly Settings _settings;
        private IManagedMqttClient _client;
        private bool _disposed;

        public MqttBridge(Settings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Full topic path for bridge status messages, e.g.
        /// <c>ksp/telemetry/_bridge/status</c>.
        /// </summary>
        public string StatusTopic => $"{_settings.TopicPrefix}/{StatusSubTopic}";

        /// <summary>True once <see cref="Start"/> has run successfully.</summary>
        public bool IsStarted => _client != null && _client.IsStarted;

        /// <summary>
        /// True if the underlying client currently has a live connection.
        /// Useful for status displays and conditional-publish logic.
        /// </summary>
        public bool IsConnected => _client != null && _client.IsConnected;

        /// <summary>
        /// Initialise and start the managed MQTT client. After this returns,
        /// the client will keep trying to reach the broker in the background;
        /// callers should not block waiting for a connection to succeed.
        /// </summary>
        public void Start()
        {
            if (_client != null)
            {
                Debug.LogWarning($"{KSPBridgePlugin.LogPrefix} MqttBridge.Start called but client already exists");
                return;
            }

            var factory = new MqttFactory();
            _client = factory.CreateManagedMqttClient();

            // The Last Will and Testament: if our connection drops without a
            // clean disconnect (game crash, network failure, etc.), the broker
            // will publish this message on our behalf. With Retain=true, any
            // dashboard subscribing later will immediately learn the bridge is
            // offline rather than seeing stale "online" state.
            byte[] offlinePayload = Encoding.UTF8.GetBytes(BuildStatusPayload(online: false));

            var clientOptions = new MqttClientOptionsBuilder()
                .WithTcpServer(_settings.BrokerHost, _settings.BrokerPort)
                .WithClientId(_settings.ClientId)
                .WithCleanSession(true)
                .WithKeepAlivePeriod(KeepAlive)
                .WithWillTopic(StatusTopic)
                .WithWillPayload(offlinePayload)
                .WithWillRetain(true)
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            var managedOptions = new ManagedMqttClientOptionsBuilder()
                .WithClientOptions(clientOptions)
                .WithAutoReconnectDelay(ReconnectDelay)
                .Build();

            // Wire up lifecycle event logging. These handlers run on MQTTnet's
            // internal threads, so we MUST NOT touch any Unity API beyond
            // Debug.Log (which is documented thread-safe).
            _client.ConnectedAsync += e =>
            {
                Debug.Log($"{KSPBridgePlugin.LogPrefix} MQTT connected to {_settings.BrokerHost}:{_settings.BrokerPort}");
                // Publish a fresh "online" status now that we're connected.
                // Fire-and-forget — Enqueue is an in-memory operation.
                EnqueueStatus(online: true);
                return System.Threading.Tasks.Task.CompletedTask;
            };

            _client.DisconnectedAsync += e =>
            {
                Debug.LogWarning($"{KSPBridgePlugin.LogPrefix} MQTT disconnected: {e.Reason}");
                return System.Threading.Tasks.Task.CompletedTask;
            };

            _client.ConnectingFailedAsync += e =>
            {
                Debug.LogWarning($"{KSPBridgePlugin.LogPrefix} MQTT connect failed: {e.Exception?.Message ?? "unknown"}");
                return System.Threading.Tasks.Task.CompletedTask;
            };

            // StartAsync returns once the managed client has accepted the
            // options; it does NOT block waiting for the actual broker
            // connection. The connection happens on a background thread.
            try
            {
                _client.StartAsync(managedOptions).GetAwaiter().GetResult();
                Debug.Log($"{KSPBridgePlugin.LogPrefix} MQTT bridge started, target = {_settings.BrokerHost}:{_settings.BrokerPort}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"{KSPBridgePlugin.LogPrefix} MQTT bridge failed to start: {ex.Message}");
            }
        }

        /// <summary>
        /// Publish an "online" heartbeat. Called periodically by the plugin
        /// from the Unity main thread (via InvokeRepeating). Fire-and-forget.
        /// </summary>
        public void PublishHeartbeat()
        {
            EnqueueStatus(online: true);
        }

        /// <summary>
        /// Enqueue an arbitrary telemetry payload on a topic under the
        /// configured prefix. Currently unused — added for the next phase
        /// when real telemetry topics come online.
        /// </summary>
        public void Publish(string subTopic, string jsonPayload, bool retain = false)
        {
            if (_client == null) return;

            string fullTopic = $"{_settings.TopicPrefix}/{subTopic}";
            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(fullTopic)
                .WithPayload(Encoding.UTF8.GetBytes(jsonPayload))
                .WithRetainFlag(retain)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            // Fire-and-forget: EnqueueAsync just appends to the in-memory
            // outbound queue and returns. The actual network send happens
            // on the managed client's worker thread.
            _ = _client.EnqueueAsync(msg);
        }

        // Build a minimal JSON status payload by hand — JsonUtility can't
        // serialise primitive bools cleanly into a {} root, and we want zero
        // allocations / dependencies for this hot path.
        private string BuildStatusPayload(bool online)
        {
            long ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string onlineStr = online ? "true" : "false";
            return $"{{\"online\":{onlineStr},\"version\":\"{KSPBridgePlugin.Version}\",\"ts\":{ts}}}";
        }

        // Internal helper that constructs and enqueues a retained status
        // message. Used by both the Start-time online publish and the
        // periodic heartbeat.
        private void EnqueueStatus(bool online)
        {
            if (_client == null) return;

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(StatusTopic)
                .WithPayload(Encoding.UTF8.GetBytes(BuildStatusPayload(online)))
                .WithRetainFlag(true)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            _ = _client.EnqueueAsync(msg);
        }

        // Upper bound on how long Dispose() is allowed to block Unity's
        // shutdown. The two sub-budgets below must sum to <= this. If we
        // overrun, we abandon the clean shutdown and rely on the broker's
        // LWT to eventually publish an offline status on our behalf.
        private static readonly TimeSpan ShutdownBudget = TimeSpan.FromMilliseconds(500);

        // Max time to wait for the final offline status message to leave
        // the outbound queue. Small but non-zero — the network send is
        // usually sub-10ms on localhost-ish broker latencies.
        private static readonly TimeSpan FlushBudget = TimeSpan.FromMilliseconds(150);

        /// <summary>
        /// Graceful shutdown: publish a final "offline" status (so subscribers
        /// learn we left intentionally), give it a brief moment to flush,
        /// then stop the managed client with a strict time budget. If we
        /// exceed the budget we abandon the clean path — the process is
        /// exiting anyway, and the broker's LWT will still republish the
        /// offline status within one keep-alive window. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_client == null) return;

            // Capture the client locally and clear the field up-front so any
            // late callers see a disposed-looking bridge instead of a
            // half-stopped one.
            var client = _client;
            _client = null;

            try
            {
                EnqueueStatus(online: false);

                // Run the flush-and-stop on a background thread so we can
                // enforce a hard deadline. Wait() with a TimeSpan returns
                // false on timeout instead of throwing, which is exactly
                // the semantics we want here.
                var stopTask = System.Threading.Tasks.Task.Run(async () =>
                {
                    // Give the outbound queue a chance to ship the offline
                    // message before we tear the connection down.
                    await System.Threading.Tasks.Task.Delay(FlushBudget).ConfigureAwait(false);
                    await client.StopAsync().ConfigureAwait(false);
                });

                if (stopTask.Wait(ShutdownBudget))
                {
                    Debug.Log($"{KSPBridgePlugin.LogPrefix} MQTT bridge stopped cleanly");
                }
                else
                {
                    // Over budget. The background task will keep running
                    // until the process exits, which is fine — we're not
                    // about to reuse this client. The broker will see the
                    // TCP connection drop and fire our LWT within ~1.5x
                    // the keep-alive interval.
                    Debug.LogWarning(
                        $"{KSPBridgePlugin.LogPrefix} MQTT stop exceeded {ShutdownBudget.TotalMilliseconds}ms budget, " +
                        "abandoning clean shutdown (LWT will fire)");
                }
            }
            catch (Exception ex)
            {
                // AggregateException from Task.Wait wraps the real cause.
                var inner = (ex as AggregateException)?.InnerException ?? ex;
                Debug.LogWarning(
                    $"{KSPBridgePlugin.LogPrefix} error during MQTT shutdown: {inner.GetType().Name}: {inner.Message}");
            }
        }
    }
}

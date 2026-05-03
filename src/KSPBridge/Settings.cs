using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace KSPBridge
{
    /// <summary>
    /// Plugin settings loaded from <c>GameData/KSPBridge/Settings.cfg</c>.
    ///
    /// Uses KSP's built-in <see cref="ConfigNode"/> parser for the .cfg
    /// format, which is the same syntax KSP and ModuleManager use. The
    /// loader is forgiving: missing file, missing root node, or missing
    /// individual fields all fall back to safe defaults rather than
    /// throwing — a misconfigured settings file should never prevent the
    /// plugin from loading.
    /// </summary>
    public class Settings
    {
        /// <summary>Hostname or IP of the MQTT broker.</summary>
        public string BrokerHost { get; set; } = "appserv1.local";

        /// <summary>MQTT TCP port (not WebSocket).</summary>
        public int BrokerPort { get; set; } = 1883;

        /// <summary>Prefix prepended to every published topic.</summary>
        public string TopicPrefix { get; set; } = "ksp/telemetry";

        /// <summary>MQTT client identifier; must be unique on the broker.</summary>
        public string ClientId { get; set; } = "kspbridge";

        /// <summary>
        /// Locate, parse, and return Settings.cfg. On any failure path the
        /// caller still receives a fully-defaulted Settings object so the
        /// rest of the plugin can run.
        /// </summary>
        public static Settings Load()
        {
            var s = new Settings();

            // The DLL lives at GameData/KSPBridge/Plugins/KSPBridge.dll, so
            // Settings.cfg is one directory up from the DLL.
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string configPath = Path.Combine(Path.GetDirectoryName(dllDir) ?? dllDir, "Settings.cfg");

            if (!File.Exists(configPath))
            {
                Debug.LogWarning(
                    $"{KSPBridgePlugin.LogPrefix} Settings.cfg not found at " +
                    $"'{configPath}', using built-in defaults");
                return s;
            }

            try
            {
                // ConfigNode.Load returns the file's root node, which itself
                // is unnamed and contains our KSPBRIDGE child node.
                ConfigNode root = ConfigNode.Load(configPath);
                if (root == null)
                {
                    Debug.LogWarning(
                        $"{KSPBridgePlugin.LogPrefix} ConfigNode.Load returned null " +
                        $"for '{configPath}', using defaults");
                    return s;
                }

                // Prefer the explicitly-named child node, but tolerate a
                // settings file written without the wrapping node.
                ConfigNode node = root.GetNode("KSPBRIDGE") ?? root;

                if (node.HasValue("broker_host"))
                    s.BrokerHost = node.GetValue("broker_host");

                if (node.HasValue("broker_port") &&
                    int.TryParse(node.GetValue("broker_port"), out int port))
                    s.BrokerPort = port;

                if (node.HasValue("topic_prefix"))
                    s.TopicPrefix = node.GetValue("topic_prefix");

                if (node.HasValue("client_id"))
                    s.ClientId = node.GetValue("client_id");
            }
            catch (Exception ex)
            {
                // Catch broadly here: a corrupt settings file should never
                // crash the plugin. Log loudly so the user can find it,
                // then fall through and return the partially-populated
                // (or fully-defaulted) Settings object.
                Debug.LogError(
                    $"{KSPBridgePlugin.LogPrefix} failed to parse Settings.cfg " +
                    $"at '{configPath}': {ex.Message}");
            }

            return s;
        }
    }
}

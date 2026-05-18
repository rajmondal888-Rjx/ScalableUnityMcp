using System;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace ScalableMCP.Editor
{
    [Serializable]
    public class ScalableMcpSettings
    {
        public const int RequestTimeoutMinimum = 10;
        private const string SettingsPath = "ProjectSettings/ScalableMcpSettings.json";

        private static ScalableMcpSettings _instance;

        [Tooltip("Port number for the MCP WebSocket server")]
        public int Port = 8096;

        [Tooltip("Timeout in seconds for tool requests")]
        public int RequestTimeoutSeconds = RequestTimeoutMinimum;

        [Tooltip("Whether to automatically start the MCP server when Unity opens")]
        public bool AutoStartServer = true;

        [Tooltip("Whether to show info logs in the Unity console")]
        public bool EnableInfoLogs = true;

        [Tooltip("Allow connections from remote hosts (binds to 0.0.0.0). Default: localhost only.")]
        public bool AllowRemoteConnections = false;

        public static ScalableMcpSettings Instance
        {
            get
            {
                if (_instance == null) _instance = new ScalableMcpSettings();
                return _instance;
            }
        }

        private ScalableMcpSettings() => LoadSettings();

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(SettingsPath), this);
                else
                    SaveSettings();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Failed to load settings: {ex.Message}");
            }
        }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(this, true));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Failed to save settings: {ex.Message}");
            }
        }
    }
}

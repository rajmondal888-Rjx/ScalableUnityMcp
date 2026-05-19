using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using WebSocketSharp.Server;

namespace ScalableMCP.Editor
{
    public static class UnityCloseCode
    {
        public const ushort PlayMode = 4001;
    }

    [InitializeOnLoad]
    public class ScalableMcpServer : IDisposable
    {
        private static ScalableMcpServer _instance;

        private readonly Dictionary<string, IToolHandler>     _tools     = new();
        private readonly Dictionary<string, IResourceHandler> _resources = new();

        private WebSocketServer _wsServer;
        private ConsoleLogsService _consoleLogs;
        private TestRunnerService  _testRunner;

        public ConcurrentDictionary<string, string> Clients { get; } = new();

        [DidReloadScripts]
        private static void AfterReload()
        {
            if (Application.isBatchMode) return;
            var _ = Instance;
        }

        public static ScalableMcpServer Instance
        {
            get
            {
                if (Application.isBatchMode) return null;
                if (_instance == null) _instance = new ScalableMcpServer();
                return _instance;
            }
        }

        public bool IsListening => _wsServer?.IsListening ?? false;

        private ScalableMcpServer()
        {
            if (Application.isBatchMode) return;

            EditorApplication.quitting                   -= OnEditorQuitting;
            EditorApplication.quitting                   += OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload    -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload    += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload     -= OnAfterReload;
            AssemblyReloadEvents.afterAssemblyReload     += OnAfterReload;
            EditorApplication.playModeStateChanged       -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged       += OnPlayModeChanged;

            InitServices();
            HandlerRegistry.RegisterAll(_tools, _resources, _consoleLogs, _testRunner);

            if (ScalableMcpSettings.Instance.AutoStartServer)
                StartServer();
        }

        public void Dispose()
        {
            StopServer();
            EditorApplication.quitting                -= OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload  -= OnAfterReload;
            EditorApplication.playModeStateChanged    -= OnPlayModeChanged;
            GC.SuppressFinalize(this);
        }

        public void StartServer(bool isRetry = false)
        {
            if (IsListening)
            {
                Debug.Log($"[ScalableMCP] Already listening on port {ScalableMcpSettings.Instance.Port}");
                return;
            }
            try
            {
                var host = ScalableMcpSettings.Instance.AllowRemoteConnections ? "0.0.0.0" : "localhost";
                _wsServer = new WebSocketServer($"ws://{host}:{ScalableMcpSettings.Instance.Port}");
                _wsServer.AddWebSocketService("/McpUnity", () => new ScalableMcpSocketHandler(this));
                _wsServer.Start();
                Debug.Log($"[ScalableMCP] Server started on {host}:{ScalableMcpSettings.Instance.Port}");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                _wsServer = null;
                if (!isRetry)
                {
                    // Port still in TIME_WAIT after previous domain reload — retry once after 1.5 s
                    Debug.LogWarning($"[ScalableMCP] Port {ScalableMcpSettings.Instance.Port} busy, retrying in 1.5s...");
                    var captured = this;
                    EditorApplication.delayCall += () =>
                    {
                        System.Threading.Tasks.Task.Delay(1500).ContinueWith(_ =>
                            UnityEditor.EditorApplication.delayCall += () => captured.StartServer(isRetry: true));
                    };
                }
                else
                {
                    Debug.LogError($"[ScalableMCP] Port {ScalableMcpSettings.Instance.Port} still in use after retry. Use Tools > Scalable MCP → Refresh.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Failed to start server: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public void StopServer(ushort? closeCode = null, string closeReason = null)
        {
            if (!IsListening) return;
            try
            {
                if (closeCode.HasValue && _wsServer != null)
                    CloseAllClients(closeCode.Value, closeReason ?? "Server stopping");
                _wsServer?.Stop();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Error stopping server: {ex.Message}");
            }
            finally
            {
                _wsServer = null;
                Clients.Clear();
                Debug.Log("[ScalableMCP] Server stopped");
            }
        }

        private void CloseAllClients(ushort code, string reason)
        {
            try
            {
                var service = _wsServer?.WebSocketServices["/McpUnity"];
                if (service?.Sessions == null) return;
                var ids = new System.Collections.Generic.List<string>(service.Sessions.IDs);
                foreach (var id in ids)
                {
                    try { service.Sessions.CloseSession(id, code, reason); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Error closing clients: {ex.Message}");
            }
        }

        public IReadOnlyDictionary<string, IToolHandler>     RegisteredTools     => _tools;
        public IReadOnlyDictionary<string, IResourceHandler> RegisteredResources => _resources;

        public bool TryGetTool(string name, out IToolHandler tool)         => _tools.TryGetValue(name, out tool);
        public bool TryGetResource(string name, out IResourceHandler res)  => _resources.TryGetValue(name, out res);

        private void InitServices()
        {
            _testRunner  = new TestRunnerService();
            _consoleLogs = new ConsoleLogsService();
        }

        private static void OnEditorQuitting()
        {
            if (Application.isBatchMode || _instance == null) return;
            _instance.Dispose();
        }

        private static void OnBeforeReload()
        {
            if (Application.isBatchMode || _instance == null) return;
            if (_instance.IsListening) _instance.StopServer();
        }

        private static void OnAfterReload()
        {
            if (Application.isBatchMode || _instance == null) return;
            if (ScalableMcpSettings.Instance.AutoStartServer && !_instance.IsListening)
                _instance.StartServer();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (Application.isBatchMode || _instance == null) return;
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    if (_instance.IsListening)
                        _instance.StopServer(UnityCloseCode.PlayMode, "Unity entering Play mode");
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (!_instance.IsListening && ScalableMcpSettings.Instance.AutoStartServer)
                        _instance.StartServer();
                    break;
            }
        }
    }
}

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

        // Port-retry state — all accessed on main thread only via EditorApplication.update
        private int    _portRetryCount;
        private double _portRetryAt = -1;

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
            EditorApplication.update -= OnRetryUpdate;
            StopServer();
            EditorApplication.quitting                -= OnEditorQuitting;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload  -= OnAfterReload;
            EditorApplication.playModeStateChanged    -= OnPlayModeChanged;
            GC.SuppressFinalize(this);
        }

        public void StartServer()
        {
            if (IsListening) return;

            try
            {
                int port = ScalableMcpSettings.Instance.Port;

                if (IsPortInUse(port))
                {
                    const int maxRetries = 6;
                    if (_portRetryCount < maxRetries)
                    {
                        _portRetryCount++;
                        _portRetryAt = EditorApplication.timeSinceStartup + _portRetryCount;
                        EditorApplication.update -= OnRetryUpdate;
                        EditorApplication.update += OnRetryUpdate;
                        Debug.LogWarning($"[ScalableMCP] Port {port} busy, retry {_portRetryCount}/{maxRetries} in {_portRetryCount}s...");
                    }
                    else
                    {
                        _portRetryCount = 0;
                        Debug.LogError($"[ScalableMCP] Port {port} still occupied after {maxRetries} retries. Use Tools > Scalable MCP → Refresh.");
                    }
                    return;
                }

                _portRetryCount = 0;
                EditorApplication.update -= OnRetryUpdate;

                var host = ScalableMcpSettings.Instance.AllowRemoteConnections ? "0.0.0.0" : "localhost";
                _wsServer = new WebSocketServer($"ws://{host}:{port}");
                _wsServer.AddWebSocketService("/McpUnity", () => new ScalableMcpSocketHandler(this));
                _wsServer.Start();
                Debug.Log($"[ScalableMCP] Server started on {host}:{port}");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                // IsPortInUse said free but bind failed — schedule a single retry, no recursion
                _wsServer = null;
                _portRetryCount = 0;
                EditorApplication.delayCall += StartServer;
            }
            catch (Exception ex)
            {
                _wsServer = null;
                Debug.LogError($"[ScalableMCP] Failed to start server: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnRetryUpdate()
        {
            if (EditorApplication.timeSinceStartup < _portRetryAt) return;
            EditorApplication.update -= OnRetryUpdate;
            if (!IsListening) StartServer();
        }

        private static bool IsPortInUse(int port)
        {
            try
            {
                var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return false;
            }
            catch (SocketException)
            {
                return true;
            }
            catch (Exception)
            {
                return false; // unknown error — let websocket-sharp try and report
            }
        }

        public void ForceRestart()
        {
            EditorApplication.update -= OnRetryUpdate;
            _portRetryCount = 0;
            StopServer();
            StartServer();
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
            EditorApplication.update -= _instance.OnRetryUpdate;
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

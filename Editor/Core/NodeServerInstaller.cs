using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    /// <summary>
    /// Runs once when the package is first installed (or when Server~/build is missing).
    /// Executes: npm install && npm run build  inside Server~/
    /// Then writes .mcp.json in the project root so Claude Code auto-discovers the server.
    /// </summary>
    [InitializeOnLoad]
    public static class NodeServerInstaller
    {
        public static string ServerDir   { get; private set; }
        public static string BuildEntry  { get; private set; }
        public static bool   IsInstalled { get; private set; }

        static NodeServerInstaller()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += () => _ = InitAsync();
        }

        public static async Task InitAsync()
        {
            ServerDir  = ResolveServerDir();
            BuildEntry = ServerDir != null ? Path.Combine(ServerDir, "build", "index.js") : null;
            IsInstalled = BuildEntry != null && File.Exists(BuildEntry);

            if (IsInstalled)
            {
                EnsureMcpJson();
                return;
            }

            if (ServerDir == null)
            {
                UnityEngine.Debug.LogWarning("[ScalableMCP] Could not locate Server~ folder. Open Tools > Scalable MCP and click Setup.");
                return;
            }

            UnityEngine.Debug.Log("[ScalableMCP] First-time setup: running npm install && npm run build...");
            await RunSetupAsync();
        }

        public static async Task RunSetupAsync()
        {
            if (ServerDir == null)
            {
                UnityEngine.Debug.LogError("[ScalableMCP] Server~ directory not found.");
                return;
            }

            bool ok = await RunNpmAsync(ServerDir, "install") && await RunNpmAsync(ServerDir, "run build");
            IsInstalled = ok && File.Exists(BuildEntry);

            if (IsInstalled)
            {
                EnsureMcpJson();
                UnityEngine.Debug.Log("[ScalableMCP] Node.js server ready. Restart Claude Code to pick up the MCP server.");
            }
            else
            {
                UnityEngine.Debug.LogError("[ScalableMCP] Setup failed. Check the console for npm errors.");
            }
        }

        // --------------------------------------------------------------------------
        // .mcp.json — writes/updates the project-root Claude Code config
        // --------------------------------------------------------------------------
        public static void EnsureMcpJson()
        {
            if (BuildEntry == null) return;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string mcpJsonPath = Path.Combine(projectRoot, ".mcp.json");

            // Normalize path separators for JSON
            string entryPath = BuildEntry.Replace("\\", "/");

            JObject root;
            if (File.Exists(mcpJsonPath))
            {
                try { root = JObject.Parse(File.ReadAllText(mcpJsonPath)); }
                catch { root = new JObject(); }
            }
            else
            {
                root = new JObject();
            }

            if (!(root["mcpServers"] is JObject servers))
            {
                servers = new JObject();
                root["mcpServers"] = servers;
            }

            servers["scalable-unity-mcp"] = new JObject
            {
                ["command"] = "node",
                ["args"]    = new JArray { entryPath }
            };

            File.WriteAllText(mcpJsonPath, root.ToString(Newtonsoft.Json.Formatting.Indented));
            UnityEngine.Debug.Log($"[ScalableMCP] .mcp.json updated → {mcpJsonPath}");
        }

        // --------------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------------
        private static string ResolveServerDir()
        {
            // 1. Via PackageManager API (works for registry/git installs and local file: refs)
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.scalable.mcp-unity");
                if (info != null)
                {
                    string candidate = Path.Combine(info.resolvedPath, "Server~");
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch { }

            // 2. Fallback: project root sibling (developer setup: file:../../scalable-unity-mcp/unity-plugin)
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string devPath     = Path.GetFullPath(Path.Combine(projectRoot, "..", "..", "scalable-unity-mcp", "unity-plugin", "Server~"));
            if (Directory.Exists(devPath)) return devPath;

            return null;
        }

        private static Task<bool> RunNpmAsync(string workingDir, string args)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                string npmCmd = IsWindows() ? "npm.cmd" : "npm";
                var psi = new ProcessStartInfo
                {
                    FileName               = npmCmd,
                    Arguments              = args,
                    WorkingDirectory       = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

                proc.OutputDataReceived += (_, e) => { if (e.Data != null) UnityEngine.Debug.Log($"[npm] {e.Data}"); };
                proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) UnityEngine.Debug.LogWarning($"[npm] {e.Data}"); };

                proc.Exited += (_, __) =>
                {
                    bool success = proc.ExitCode == 0;
                    if (!success) UnityEngine.Debug.LogError($"[ScalableMCP] npm {args} exited with code {proc.ExitCode}");
                    proc.Dispose();
                    tcs.TrySetResult(success);
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[ScalableMCP] Failed to run npm {args}: {ex.Message}");
                tcs.TrySetResult(false);
            }
            return tcs.Task;
        }

        private static bool IsWindows()
            => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
    }
}

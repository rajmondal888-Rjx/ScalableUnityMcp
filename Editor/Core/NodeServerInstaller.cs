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
            // Re-resolve in case InitAsync hasn't run yet
            if (ServerDir == null)
                ServerDir = ResolveServerDir();

            if (ServerDir == null)
            {
                UnityEngine.Debug.LogError("[ScalableMCP] Server~ directory not found.");
                return;
            }

            BuildEntry = Path.Combine(ServerDir, "build", "index.js");

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
            // 1. Resolve via Unity's virtual Packages/ path (works for git, registry, and local file: installs)
            try
            {
                string resolved = Path.GetFullPath("Packages/com.scalable.mcp-unity");
                string candidate = Path.Combine(resolved, "Server~");
                if (Directory.Exists(candidate)) return candidate;
            }
            catch { }

            // 2. Fallback: scan PackageCache directly
            try
            {
                string cacheRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "PackageCache"));
                foreach (var dir in Directory.GetDirectories(cacheRoot, "com.scalable.mcp-unity*"))
                {
                    string candidate = Path.Combine(dir, "Server~");
                    if (Directory.Exists(candidate)) return candidate;
                }
            }
            catch { }

            return null;
        }

        private static Task<bool> RunNpmAsync(string workingDir, string args)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                string npmCmd = ResolveNpmPath();
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

        private static string ResolveNpmPath()
        {
            // Check common install locations first — Unity's spawned process may have a stripped PATH
            string[] candidates = IsWindows()
                ? new[] {
                    @"C:\Program Files\nodejs\npm.cmd",
                    @"C:\Program Files (x86)\nodejs\npm.cmd",
                    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "npm", "npm.cmd"),
                  }
                : new[] { "/usr/local/bin/npm", "/usr/bin/npm", "/opt/homebrew/bin/npm" };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            return IsWindows() ? "npm.cmd" : "npm"; // fallback to PATH
        }

        private static bool IsWindows()
            => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows);
    }
}

using ScalableMCP.Editor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.TestTools.TestRunner.Api;
using Unity.EditorCoroutines.Editor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers all editor-level tool handlers:
    /// execute_menu_item, recompile_scripts, run_tests, send_console_log,
    /// batch_execute, add_package, get_console_logs.
    /// </summary>
    public static class EditorHandler
    {
        // Shared state for recompile_scripts
        private class CompilationRequest
        {
            public readonly bool ReturnWithLogs;
            public readonly int LogsLimit;
            public readonly TaskCompletionSource<JObject> CompletionSource;
            public CompilationRequest(bool returnWithLogs, int logsLimit, TaskCompletionSource<JObject> cs)
            { ReturnWithLogs = returnWithLogs; LogsLimit = logsLimit; CompletionSource = cs; }
        }
        private class CompilationResult
        {
            public readonly List<CompilerMessage> SortedLogs;
            public readonly int WarningsCount;
            public readonly int ErrorsCount;
            public bool HasErrors => ErrorsCount > 0;
            public CompilationResult(List<CompilerMessage> sortedLogs, int warnings, int errors)
            { SortedLogs = sortedLogs; WarningsCount = warnings; ErrorsCount = errors; }
        }
        private static readonly List<CompilationRequest> _pendingRequests = new List<CompilationRequest>();
        private static readonly List<CompilerMessage>    _compilationLogs  = new List<CompilerMessage>();
        private static int _processedAssemblies = 0;

        // Shared state for add_package
        private class PackageOperation
        {
            public AddRequest Request;
            public TaskCompletionSource<JObject> CompletionSource;
        }
        private static readonly List<PackageOperation> _activePackageOps = new List<PackageOperation>();
        private static bool _packageUpdateRegistered = false;

        // Reference to all tools (set in RegisterAll) - used by batch_execute
        private static Dictionary<string, IToolHandler> _allTools;

        public static void RegisterAll(Dictionary<string, IToolHandler> tools, TestRunnerService testRunner)
        {
            _allTools = tools;

            tools["execute_menu_item"]  = new DelegateToolHandler("execute_menu_item",  ExecuteMenuItem);
            tools["send_console_log"]   = new DelegateToolHandler("send_console_log",   SendConsoleLog);

            // Async tools use AsyncDelegateToolHandler wrappers
            tools["recompile_scripts"]  = new AsyncDelegateToolHandler("recompile_scripts",  RecompileScriptsAsync);
            tools["run_tests"]          = new AsyncDelegateToolHandler("run_tests",           (p, tcs) => RunTestsAsync(p, tcs, testRunner));
            tools["batch_execute"]      = new AsyncDelegateToolHandler("batch_execute",       BatchExecuteAsync);
            tools["add_package"]        = new AsyncDelegateToolHandler("add_package",         AddPackageAsync);
            tools["get_console_logs"]   = new DelegateToolHandler("get_console_logs",   GetConsoleLogs);
        }

        // -------------------------------------------------------------------------
        // execute_menu_item
        // -------------------------------------------------------------------------
        private static JObject ExecuteMenuItem(JObject parameters)
        {
            string menuPath = parameters["menuPath"]?.ToObject<string>();
            if (string.IsNullOrEmpty(menuPath))
                return JsonResponseFactory.Error("Required parameter 'menuPath' not provided", "validation_error");

            Debug.Log($"[ScalableMCP] Executing menu item: {menuPath}");
            bool success = EditorApplication.ExecuteMenuItem(menuPath);

            return new JObject
            {
                ["success"] = success,
                ["type"]    = "text",
                ["message"] = success
                    ? $"Successfully executed menu item: {menuPath}"
                    : $"Failed to execute menu item: {menuPath}"
            };
        }

        // -------------------------------------------------------------------------
        // send_console_log
        // -------------------------------------------------------------------------
        private static JObject SendConsoleLog(JObject parameters)
        {
            string message = parameters["message"]?.ToObject<string>();
            string type    = parameters["type"]?.ToObject<string>()?.ToLower() ?? "info";

            if (string.IsNullOrEmpty(message))
                return JsonResponseFactory.Error("Required parameter 'message' not provided", "validation_error");

            switch (type)
            {
                case "error":   Debug.LogError($"[MCP]: {message}");   break;
                case "warning": Debug.LogWarning($"[MCP]: {message}"); break;
                default:        Debug.Log($"[MCP]: {message}");        break;
            }

            return new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = $"Message displayed: {message}"
            };
        }

        // -------------------------------------------------------------------------
        // get_console_logs  (sync, delegates to ConsoleLogsService passed at call time)
        // This variant reads a stored service reference; we keep it simple with a
        // static field set once the server is initialised.
        // -------------------------------------------------------------------------
        private static ConsoleLogsService _consoleLogsService;

        /// <summary>
        /// Called by the server initialisation to provide the console logs service instance.
        /// </summary>
        public static void SetConsoleLogsService(ConsoleLogsService service)
        {
            _consoleLogsService = service;
        }

        private static JObject GetConsoleLogs(JObject parameters)
        {
            if (_consoleLogsService == null)
                return JsonResponseFactory.Error("ConsoleLogsService not initialised", "service_error");

            string logType         = parameters?["logType"]?.ToString();
            if (string.IsNullOrWhiteSpace(logType)) logType = null;

            int offset            = Math.Max(0, GetIntParam(parameters, "offset", 0));
            int limit             = Math.Max(1, Math.Min(1000, GetIntParam(parameters, "limit", 100)));
            bool includeStackTrace= GetBoolParam(parameters, "includeStackTrace", true);

            JObject result = _consoleLogsService.GetLogsAsJson(logType, offset, limit, includeStackTrace);

            string typeFilter  = logType != null ? $" of type '{logType}'" : "";
            int returnedCount  = result["_returnedCount"]?.Value<int>() ?? 0;
            int filteredCount  = result["_filteredCount"]?.Value<int>() ?? 0;
            int totalCount     = result["_totalCount"]?.Value<int>()    ?? 0;

            result["message"] = $"Retrieved {returnedCount} of {filteredCount} log entries{typeFilter} (offset: {offset}, limit: {limit}, includeStackTrace: {includeStackTrace}, total: {totalCount})";
            result["success"] = true;
            result.Remove("_totalCount");
            result.Remove("_filteredCount");
            result.Remove("_returnedCount");

            return result;
        }

        // -------------------------------------------------------------------------
        // recompile_scripts  (async)
        // -------------------------------------------------------------------------
        private static void RecompileScriptsAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            bool returnWithLogs = GetBoolParam(parameters, "returnWithLogs", true);
            int logsLimit       = Mathf.Clamp(GetIntParam(parameters, "logsLimit", 100), 0, 1000);
            var request         = new CompilationRequest(returnWithLogs, logsLimit, tcs);

            bool hasActive = false;
            lock (_pendingRequests)
            {
                hasActive = _pendingRequests.Count > 0;
                _pendingRequests.Add(request);
            }

            if (hasActive)
            {
                Debug.Log("[ScalableMCP] Recompilation already in progress. Waiting for completion...");
                return;
            }

            StartCompilationTracking();

            if (!EditorApplication.isCompiling)
            {
                Debug.Log("[ScalableMCP] Recompiling all scripts in the Unity project");
                CompilationPipeline.RequestScriptCompilation();
            }
        }

        private static void StartCompilationTracking()
        {
            _compilationLogs.Clear();
            _processedAssemblies = 0;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished         += OnCompilationFinished;
        }

        private static void StopCompilationTracking()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished         -= OnCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _processedAssemblies++;
            _compilationLogs.AddRange(messages);
        }

        private static void OnCompilationFinished(object _)
        {
            Debug.Log($"[ScalableMCP] Recompilation completed. Processed {_processedAssemblies} assemblies with {_compilationLogs.Count} compiler messages");

            List<CompilerMessage> sortedLogs = _compilationLogs.OrderBy(x => x.type).ToList();
            int errorsCount   = _compilationLogs.Count(l => l.type == CompilerMessageType.Error);
            int warningsCount = _compilationLogs.Count(l => l.type == CompilerMessageType.Warning);
            CompilationResult result = new CompilationResult(sortedLogs, warningsCount, errorsCount);

            StopCompilationTracking();

            List<CompilationRequest> toComplete;
            lock (_pendingRequests)
            {
                toComplete = new List<CompilationRequest>(_pendingRequests);
                _pendingRequests.Clear();
            }

            foreach (var req in toComplete)
                CompleteCompilationRequest(req, result);
        }

        private static void CompleteCompilationRequest(CompilationRequest request, CompilationResult result)
        {
            JArray logsArray = new JArray();
            IEnumerable<CompilerMessage> logsToReturn = request.ReturnWithLogs
                ? result.SortedLogs.Take(request.LogsLimit)
                : Enumerable.Empty<CompilerMessage>();

            foreach (var message in logsToReturn)
            {
                var logObject = new JObject { ["message"] = message.message, ["type"] = message.type.ToString() };
                if (!string.IsNullOrEmpty(message.file))
                {
                    logObject["file"]   = message.file;
                    logObject["line"]   = message.line;
                    logObject["column"] = message.column;
                }
                logsArray.Add(logObject);
            }

            string summaryMessage = result.HasErrors
                ? $"Recompilation completed with {result.ErrorsCount} error(s) and {result.WarningsCount} warning(s)"
                : $"Successfully recompiled all scripts with {result.WarningsCount} warning(s)";
            summaryMessage += $" (returnWithLogs: {request.ReturnWithLogs}, logsLimit: {request.LogsLimit})";

            request.CompletionSource.SetResult(new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = summaryMessage,
                ["logs"]    = logsArray
            });
        }

        // -------------------------------------------------------------------------
        // run_tests  (async)
        // -------------------------------------------------------------------------
        private static async void RunTestsAsync(JObject parameters, TaskCompletionSource<JObject> tcs, TestRunnerService testRunner)
        {
            string testModeStr       = parameters?["testMode"]?.ToObject<string>() ?? "EditMode";
            string testFilter        = parameters?["testFilter"]?.ToObject<string>();
            bool returnOnlyFailures  = parameters?["returnOnlyFailures"]?.ToObject<bool>() ?? false;
            bool returnWithLogs      = parameters?["returnWithLogs"]?.ToObject<bool>() ?? false;

            TestMode testMode = TestMode.EditMode;
            if (Enum.TryParse(testModeStr, true, out TestMode parsedMode))
                testMode = parsedMode;

            Debug.Log($"[ScalableMCP] Executing RunTests: Mode={testMode}, Filter={testFilter ?? "(none)"}");

            JObject result = await testRunner.ExecuteTestsAsync(testMode, returnOnlyFailures, returnWithLogs, testFilter);
            tcs.SetResult(result);
        }

        // -------------------------------------------------------------------------
        // batch_execute  (async, coroutine)
        // -------------------------------------------------------------------------
        private static void BatchExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            EditorCoroutineUtility.StartCoroutineOwnerless(BatchExecuteCoroutine(parameters, tcs));
        }

        private static IEnumerator BatchExecuteCoroutine(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            JArray operations = parameters["operations"] as JArray;
            bool stopOnError  = parameters["stopOnError"]?.ToObject<bool?>() ?? true;
            bool atomic       = parameters["atomic"]?.ToObject<bool?>() ?? false;

            if (operations == null || operations.Count == 0)
            {
                tcs.SetResult(JsonResponseFactory.Error("The 'operations' array is required and must contain at least one operation.", "validation_error"));
                yield break;
            }

            if (operations.Count > 100)
            {
                tcs.SetResult(JsonResponseFactory.Error("Maximum of 100 operations allowed per batch.", "validation_error"));
                yield break;
            }

            JArray results  = new JArray();
            int succeeded   = 0;
            int failed      = 0;
            int undoGroup   = -1;

            if (atomic)
            {
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Batch Execute");
            }

            for (int i = 0; i < operations.Count; i++)
            {
                JObject operation = operations[i] as JObject;
                if (operation == null)
                {
                    results.Add(CreateOpResult(i, null, false, null, "Invalid operation format"));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                    continue;
                }

                string toolName    = operation["tool"]?.ToString();
                JObject toolParams = operation["params"] as JObject ?? new JObject();
                string opId        = operation["id"]?.ToString() ?? i.ToString();

                if (string.IsNullOrEmpty(toolName))
                {
                    results.Add(CreateOpResult(i, opId, false, null, "Missing 'tool' name in operation"));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                    continue;
                }

                if (toolName == "batch_execute")
                {
                    results.Add(CreateOpResult(i, opId, false, null, "Cannot nest batch_execute operations"));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                    continue;
                }

                if (_allTools == null || !_allTools.TryGetValue(toolName, out IToolHandler tool))
                {
                    results.Add(CreateOpResult(i, opId, false, null, $"Unknown tool: {toolName}"));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                    continue;
                }

                JObject toolResult    = null;
                Exception toolException = null;

                if (tool.IsAsync)
                {
                    var toolTcs = new TaskCompletionSource<JObject>();
                    try { tool.ExecuteAsync(toolParams, toolTcs); }
                    catch (Exception ex) { toolException = ex; }

                    if (toolException == null)
                    {
                        while (!toolTcs.Task.IsCompleted) yield return null;

                        if (toolTcs.Task.IsFaulted)
                            toolException = toolTcs.Task.Exception?.InnerException ?? toolTcs.Task.Exception;
                        else
                            toolResult = toolTcs.Task.Result;
                    }
                }
                else
                {
                    try { toolResult = tool.Execute(toolParams); }
                    catch (Exception ex) { toolException = ex; }
                }

                if (toolException != null)
                {
                    results.Add(CreateOpResult(i, opId, false, null, toolException.Message));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                }
                else if (toolResult != null)
                {
                    bool isError   = toolResult["error"] != null;
                    bool isSuccess = toolResult["success"]?.ToObject<bool?>() ?? !isError;

                    if (isSuccess && !isError)
                    {
                        results.Add(CreateOpResult(i, opId, true, toolResult, null));
                        succeeded++;
                    }
                    else
                    {
                        string errorMessage = toolResult["error"]?["message"]?.ToString()
                            ?? toolResult["message"]?.ToString()
                            ?? "Tool execution failed";
                        results.Add(CreateOpResult(i, opId, false, toolResult, errorMessage));
                        failed++;
                        if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                    }
                }
                else
                {
                    results.Add(CreateOpResult(i, opId, false, null, "Tool returned null result"));
                    failed++;
                    if (stopOnError) { RevertIfAtomic(atomic, undoGroup); break; }
                }

                yield return null;
            }

            if (atomic && undoGroup >= 0 && failed == 0)
                Undo.CollapseUndoOperations(undoGroup);

            string message;
            if (failed == 0)
                message = $"Successfully executed {succeeded}/{operations.Count} operations.";
            else if (atomic && stopOnError)
                message = $"Batch execution failed and rolled back. {succeeded} operations succeeded before failure.";
            else if (stopOnError)
                message = $"Batch execution stopped on error. {succeeded}/{operations.Count} operations succeeded.";
            else
                message = $"Batch execution completed with errors. {succeeded}/{operations.Count} operations succeeded, {failed} failed.";

            tcs.SetResult(new JObject
            {
                ["success"] = failed == 0,
                ["type"]    = "text",
                ["message"] = message,
                ["results"] = results,
                ["summary"] = new JObject
                {
                    ["total"]     = operations.Count,
                    ["succeeded"] = succeeded,
                    ["failed"]    = failed,
                    ["executed"]  = succeeded + failed
                }
            });
        }

        private static void RevertIfAtomic(bool atomic, int undoGroup)
        {
            if (atomic && undoGroup >= 0)
                Undo.RevertAllDownToGroup(undoGroup);
        }

        private static JObject CreateOpResult(int index, string id, bool success, JObject result, string error)
        {
            var op = new JObject { ["index"] = index, ["id"] = id ?? index.ToString(), ["success"] = success };
            if (success && result != null)  op["result"] = result;
            else if (!success)              op["error"]  = error ?? "Unknown error";
            return op;
        }

        // -------------------------------------------------------------------------
        // add_package  (async)
        // -------------------------------------------------------------------------
        private static void AddPackageAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            string source = parameters["source"]?.ToObject<string>();
            if (string.IsNullOrEmpty(source))
            {
                tcs.SetResult(JsonResponseFactory.Error("Required parameter 'source' not provided", "validation_error"));
                return;
            }

            var operation = new PackageOperation { CompletionSource = tcs };

            switch (source.ToLowerInvariant())
            {
                case "registry": operation.Request = AddFromRegistry(parameters, tcs); break;
                case "github":   operation.Request = AddFromGitHub(parameters, tcs);   break;
                case "disk":     operation.Request = AddFromDisk(parameters, tcs);     break;
                default:
                    tcs.SetResult(JsonResponseFactory.Error($"Unknown method '{source}'. Valid methods are: registry, github, disk", "validation_error"));
                    return;
            }

            if (operation.Request == null) return;

            lock (_activePackageOps)
            {
                _activePackageOps.Add(operation);
                if (!_packageUpdateRegistered)
                {
                    EditorApplication.update += CheckPackageOperations;
                    _packageUpdateRegistered = true;
                }
            }
        }

        private static AddRequest AddFromRegistry(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            string packageName = parameters["packageName"]?.ToObject<string>();
            if (string.IsNullOrEmpty(packageName))
            {
                tcs.SetResult(JsonResponseFactory.Error("Required parameter 'packageName' not provided for registry method", "validation_error"));
                return null;
            }
            string version = parameters["version"]?.ToObject<string>();
            string id = packageName;
            if (!string.IsNullOrEmpty(version)) id = $"{packageName}@{version}";
            Debug.Log($"[ScalableMCP] Adding package from registry: {id}");
            try { return Client.Add(id); }
            catch (Exception ex) { tcs.SetResult(JsonResponseFactory.Error($"Exception adding package: {ex.Message}", "package_manager_error")); return null; }
        }

        private static AddRequest AddFromGitHub(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            string packageUrl = parameters["repositoryUrl"]?.ToObject<string>();
            if (string.IsNullOrEmpty(packageUrl))
            {
                tcs.SetResult(JsonResponseFactory.Error("Required parameter 'repositoryUrl' not provided for github method", "validation_error"));
                return null;
            }
            string branch = parameters["branch"]?.ToObject<string>();
            string path   = parameters["path"]?.ToObject<string>();

            if (packageUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                packageUrl = packageUrl.Substring(0, packageUrl.Length - 4);
            if (!string.IsNullOrEmpty(branch)) packageUrl += "#" + branch;
            if (!string.IsNullOrEmpty(path))
                packageUrl += (string.IsNullOrEmpty(branch) ? "#" : "/") + path;

            Debug.Log($"[ScalableMCP] Adding package from GitHub: {packageUrl}");
            try { return Client.Add(packageUrl); }
            catch (Exception ex) { tcs.SetResult(JsonResponseFactory.Error($"Exception adding package: {ex.Message}", "package_manager_error")); return null; }
        }

        private static AddRequest AddFromDisk(JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            string path = parameters["path"]?.ToObject<string>();
            if (string.IsNullOrEmpty(path))
            {
                tcs.SetResult(JsonResponseFactory.Error("Required parameter 'path' not provided for disk method", "validation_error"));
                return null;
            }
            string encodedPath = path.Replace(" ", "%20");
            string packageUrl  = $"file:{encodedPath}";
            Debug.Log($"[ScalableMCP] Adding package from disk: {packageUrl}");
            try { return Client.Add(packageUrl); }
            catch (Exception ex) { tcs.SetResult(JsonResponseFactory.Error($"Exception adding package: {ex.Message}", "package_manager_error")); return null; }
        }

        private static void CheckPackageOperations()
        {
            int initialCount = _activePackageOps.Count;

            lock (_activePackageOps)
            {
                for (int i = _activePackageOps.Count - 1; i >= 0; i--)
                {
                    var op = _activePackageOps[i];
                    if (op.Request != null && op.Request.IsCompleted)
                    {
                        ProcessCompletedPackageOperation(op);
                        _activePackageOps.RemoveAt(i);
                    }
                }

                if (_activePackageOps.Count == 0 && _packageUpdateRegistered)
                {
                    EditorApplication.update -= CheckPackageOperations;
                    _packageUpdateRegistered  = false;
                }
            }

            if (initialCount != _activePackageOps.Count)
                GC.Collect();
        }

        private static void ProcessCompletedPackageOperation(PackageOperation operation)
        {
            if (operation.CompletionSource == null) { Debug.LogError("[ScalableMCP] TaskCompletionSource is null."); return; }

            if (operation.Request.Status == StatusCode.Success)
            {
                var result = operation.Request.Result;
                if (result != null)
                {
                    operation.CompletionSource.SetResult(new JObject
                    {
                        ["success"] = true, ["type"] = "text",
                        ["message"] = $"Successfully added package: {result.displayName} ({result.name}) version {result.version}",
                        ["packageInfo"] = JObject.FromObject(new { name = result.name, displayName = result.displayName, version = result.version })
                    });
                }
                else
                {
                    operation.CompletionSource.SetResult(new JObject
                    {
                        ["success"] = true, ["type"] = "text",
                        ["message"] = "Package operation completed successfully, but no package information was returned."
                    });
                }
            }
            else if (operation.Request.Status == StatusCode.Failure)
            {
                operation.CompletionSource.SetResult(JsonResponseFactory.Error($"Failed to add package: {operation.Request.Error.message}", "package_manager_error"));
            }
            else
            {
                operation.CompletionSource.SetResult(JsonResponseFactory.Error($"Unknown package manager status: {operation.Request.Status}", "package_manager_error"));
            }
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------
        private static int GetIntParam(JObject p, string key, int def)
        {
            if (p?[key] != null && int.TryParse(p[key].ToString(), out int v)) return v;
            return def;
        }

        private static bool GetBoolParam(JObject p, string key, bool def)
        {
            if (p?[key] != null && bool.TryParse(p[key].ToString(), out bool v)) return v;
            return def;
        }
    }

    // -------------------------------------------------------------------------
    // AsyncDelegateToolHandler — wraps an async Action into IToolHandler
    // -------------------------------------------------------------------------
    /// <summary>
    /// IToolHandler implementation for async tools backed by an Action delegate.
    /// </summary>
    public class AsyncDelegateToolHandler : ToolHandlerBase
    {
        private readonly Action<JObject, TaskCompletionSource<JObject>> _fn;
        public override string Name    { get; }
        public override bool   IsAsync => true;

        public AsyncDelegateToolHandler(string name, Action<JObject, TaskCompletionSource<JObject>> fn)
        {
            Name = name;
            _fn  = fn;
        }

        public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
            => _fn(parameters, tcs);
    }
}

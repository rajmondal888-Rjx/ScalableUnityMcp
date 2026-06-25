using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace ScalableMCP.Editor
{
    /// <summary>
    /// Service for managing Unity console logs.
    /// Primary path reads directly from UnityEditor.LogEntries via reflection, giving access to
    /// the same full history shown in the Unity Console window — including logs that were emitted
    /// before MCP started listening.  When reflection is unavailable the service falls back to the
    /// in-memory buffer populated by Application.logMessageReceivedThreaded.
    /// </summary>
    public class ConsoleLogsService
    {
        // ---------------------------------------------------------------------------
        // Log-type mapping: MCP type strings → Unity LogType name strings.
        // "error" intentionally covers Error, Exception AND Assert to match
        // the original behaviour.
        // ---------------------------------------------------------------------------
        private static readonly Dictionary<string, HashSet<string>> LogTypeMapping =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "info",    new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Log" } },
                { "error",   new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Error", "Exception", "Assert" } }
            };

        // ---------------------------------------------------------------------------
        // LogEntry.mode bit-masks taken from the internal Unity source.
        // They are used when converting a raw int mode to a Unity LogType.
        // ---------------------------------------------------------------------------

        // Error-family bits: Error | ScriptingError | ScriptCompileError |
        //                    ScriptingException | GraphCompileError |
        //                    ScriptingAssertion (treated below) | ...
        private const int ModeMaskError   = 0x001 | 0x010 | 0x040 | 0x080 | 0x400 | 0x1000 | 0x2000;
        private const int ModeMaskAssert  = 0x002;
        private const int ModeMaskWarning = 0x100 | 0x800;

        // ---------------------------------------------------------------------------
        // In-memory fallback buffer (populated by Application.logMessageReceivedThreaded)
        // ---------------------------------------------------------------------------
        private class LogEntry
        {
            public string   Message    { get; set; }
            public string   StackTrace { get; set; }
            public LogType  Type       { get; set; }
            public DateTime Timestamp  { get; set; }
        }

        private const int MaxLogEntries      = 1000;
        private const int CleanupThreshold   = 200;

        private readonly List<LogEntry> _logEntries = new List<LogEntry>();

        // ---------------------------------------------------------------------------
        // Constructor
        // ---------------------------------------------------------------------------
        public ConsoleLogsService()
        {
            StartListening();
        }

        // ---------------------------------------------------------------------------
        // Public lifecycle
        // ---------------------------------------------------------------------------

        /// <summary>Starts listening for new log messages via the Unity callback.</summary>
        public void StartListening()
        {
            Application.logMessageReceivedThreaded += OnLogMessageReceived;

#if UNITY_6000_0_OR_NEWER
            ConsoleWindowUtility.consoleLogsChanged += OnConsoleCountChanged;
#else
            EditorApplication.update += CheckConsoleClearViaReflection;
#endif
        }

        /// <summary>Stops listening for new log messages.</summary>
        public void StopListening()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;

#if UNITY_6000_0_OR_NEWER
            ConsoleWindowUtility.consoleLogsChanged -= OnConsoleCountChanged;
#else
            EditorApplication.update -= CheckConsoleClearViaReflection;
#endif
        }

        // ---------------------------------------------------------------------------
        // Primary public API
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Returns paginated console logs as a JSON object.
        /// Attempts to source logs directly from UnityEditor.LogEntries (full history).
        /// Falls back to the in-memory buffer when reflection is unavailable.
        /// </summary>
        /// <param name="logType">Filter by MCP log type ("info" | "error" | "warning" | "" for all).</param>
        /// <param name="offset">Zero-based starting index within the filtered result set.</param>
        /// <param name="limit">Maximum number of entries to return.</param>
        /// <param name="includeStackTrace">When true, each entry includes its stack trace.</param>
        /// <returns>
        /// JObject with keys: logs (JArray), _totalCount, _filteredCount, _returnedCount,
        /// message (human-readable summary), success (bool).
        /// </returns>
        public JObject GetLogsAsJson(string logType = "", int offset = 0, int limit = 100, bool includeStackTrace = true)
        {
            // --- Primary path: read from the editor's LogEntries store ---
            JObject reflectionResult = TryGetLogsViaReflection(logType, offset, limit, includeStackTrace);
            if (reflectionResult != null)
                return reflectionResult;

            // --- Fallback path: in-memory buffer ---
            JArray logsArray   = new JArray();
            bool filter        = !string.IsNullOrEmpty(logType);
            int  totalCount    = 0;
            int  filteredCount = 0;
            int  currentIndex  = 0;

            HashSet<string> unityLogTypes = null;
            if (filter)
            {
                unityLogTypes = LogTypeMapping.TryGetValue(logType, out var mapped)
                    ? mapped
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { logType };
            }

            lock (_logEntries)
            {
                totalCount = _logEntries.Count;

                // Iterate newest-first to match the reflection path ordering.
                for (int i = _logEntries.Count - 1; i >= 0; i--)
                {
                    var entry = _logEntries[i];

                    if (filter && !unityLogTypes.Contains(entry.Type.ToString()))
                        continue;

                    filteredCount++;

                    if (currentIndex >= offset && logsArray.Count < limit)
                    {
                        var logObject = new JObject
                        {
                            ["message"] = entry.Message,
                            ["type"]    = entry.Type.ToString(),
                            ["timestamp"] = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")
                        };

                        if (includeStackTrace)
                            logObject["stackTrace"] = entry.StackTrace;

                        logsArray.Add(logObject);
                    }

                    currentIndex++;

                    if (currentIndex >= offset + limit) break;
                }
            }

            return new JObject
            {
                ["logs"]           = logsArray,
                ["_totalCount"]    = totalCount,
                ["_filteredCount"] = filteredCount,
                ["_returnedCount"] = logsArray.Count,
                ["message"]        = $"Returned {logsArray.Count} log(s) from in-memory buffer (reflection unavailable).",
                ["success"]        = true
            };
        }

        // ---------------------------------------------------------------------------
        // Reflection-based log reader (primary path)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Reads console log entries directly from UnityEditor.LogEntries via reflection.
        /// This gives access to the full console history — identical to what the Unity
        /// Console window displays — regardless of when MCP started.
        /// </summary>
        /// <param name="logType">MCP log-type filter (empty = all).</param>
        /// <param name="offset">Pagination start index within filtered entries.</param>
        /// <param name="limit">Maximum number of entries to return.</param>
        /// <param name="includeStackTrace">When true each entry includes its stack trace.</param>
        /// <returns>
        /// A populated JObject on success, or null if any reflection step fails so the
        /// caller can switch to the fallback path.
        /// </returns>
        private JObject TryGetLogsViaReflection(string logType, int offset, int limit, bool includeStackTrace)
        {
            try
            {
                // ------------------------------------------------------------------
                // 1. Resolve internal Unity editor types.
                // ------------------------------------------------------------------
                Type logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                if (logEntriesType == null) return null;

                Type logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                if (logEntryType == null) return null;

                // ------------------------------------------------------------------
                // 2. Resolve the required static methods on LogEntries.
                // ------------------------------------------------------------------
                const BindingFlags staticAny =
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

                MethodInfo getCountMethod = logEntriesType.GetMethod("GetCount", staticAny);
                if (getCountMethod == null) return null;

                MethodInfo startMethod = logEntriesType.GetMethod("StartGettingEntries", staticAny);
                if (startMethod == null) return null;

                MethodInfo endMethod = logEntriesType.GetMethod("EndGettingEntries", staticAny);
                if (endMethod == null) return null;

                MethodInfo getEntryMethod = logEntriesType.GetMethod("GetEntryInternal", staticAny);
                if (getEntryMethod == null) return null;

                // ------------------------------------------------------------------
                // 3. Resolve field accessors on LogEntry.
                // ------------------------------------------------------------------
                const BindingFlags instanceAny =
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

                FieldInfo messageField = logEntryType.GetField("message",  instanceAny);
                FieldInfo modeField    = logEntryType.GetField("mode",     instanceAny);
                if (messageField == null || modeField == null) return null;

                // ------------------------------------------------------------------
                // 4. Optionally build the Unity-type filter set.
                // ------------------------------------------------------------------
                bool filter = !string.IsNullOrEmpty(logType);
                HashSet<string> unityLogTypes = null;
                if (filter)
                {
                    unityLogTypes = LogTypeMapping.TryGetValue(logType, out var mapped)
                        ? mapped
                        : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { logType };
                }

                // ------------------------------------------------------------------
                // 5. Allocate a single reusable LogEntry instance for reading.
                // ------------------------------------------------------------------
                object entryInstance = Activator.CreateInstance(logEntryType);

                // ------------------------------------------------------------------
                // 6. Get total count, then open the enumeration session.
                // ------------------------------------------------------------------
                int totalCount = (int)getCountMethod.Invoke(null, null);

                JArray logsArray   = new JArray();
                int    filteredCount = 0;
                int    currentIndex  = 0;   // index within the filtered result set

                startMethod.Invoke(null, null);
                try
                {
                    // Iterate newest-first (highest row index → 0).
                    for (int row = totalCount - 1; row >= 0; row--)
                    {
                        getEntryMethod.Invoke(null, new object[] { row, entryInstance });

                        // ---- Read raw fields ----
                        string rawMessage = messageField.GetValue(entryInstance) as string ?? string.Empty;
                        int    modeInt    = (int)(modeField.GetValue(entryInstance) ?? 0);

                        // ---- Split message / stack trace on first newline ----
                        string messageText = rawMessage;
                        string stackTrace  = string.Empty;

                        int newlineIdx = rawMessage.IndexOf('\n');
                        if (newlineIdx >= 0)
                        {
                            messageText = rawMessage.Substring(0, newlineIdx);
                            stackTrace  = rawMessage.Substring(newlineIdx + 1);
                        }

                        // ---- Convert mode int to LogType string ----
                        string logTypeName;
                        if      ((modeInt & ModeMaskError)   != 0) logTypeName = "Error";
                        else if ((modeInt & ModeMaskAssert)  != 0) logTypeName = "Assert";
                        else if ((modeInt & ModeMaskWarning) != 0) logTypeName = "Warning";
                        else                                        logTypeName = "Log";

                        // ---- Apply logType filter ----
                        if (filter && !unityLogTypes.Contains(logTypeName))
                            continue;

                        filteredCount++;

                        // ---- Apply pagination ----
                        if (currentIndex >= offset && logsArray.Count < limit)
                        {
                            var logObject = new JObject
                            {
                                ["message"] = messageText,
                                ["type"]    = logTypeName
                            };

                            if (includeStackTrace)
                                logObject["stackTrace"] = stackTrace;

                            logsArray.Add(logObject);
                        }

                        currentIndex++;

                        // Early-out once we have collected enough filtered entries past the page.
                        if (currentIndex >= offset + limit && filteredCount > offset + limit)
                            break;
                    }
                }
                finally
                {
                    // Always release the enumeration lock even if an exception is thrown.
                    endMethod.Invoke(null, null);
                }

                return new JObject
                {
                    ["logs"]           = logsArray,
                    ["_totalCount"]    = totalCount,
                    ["_filteredCount"] = filteredCount,
                    ["_returnedCount"] = logsArray.Count,
                    ["message"]        = $"Returned {logsArray.Count} log(s) via LogEntries reflection (total in console: {totalCount}).",
                    ["success"]        = true
                };
            }
            catch (Exception ex)
            {
                // Any failure — missing types, missing members, access violation — is
                // treated as a soft failure so the caller can use the in-memory fallback.
                Debug.LogWarning($"[ScalableMCP] TryGetLogsViaReflection failed, falling back to buffer. Reason: {ex.Message}");
                return null;
            }
        }

        // ---------------------------------------------------------------------------
        // Console-clear detection helpers (unchanged from original)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Checks whether the Unity Console was cleared externally and mirrors that
        /// to the in-memory buffer.  Called each editor frame on pre-Unity-6 builds.
        /// </summary>
        private void CheckConsoleClearViaReflection()
        {
            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                if (logEntriesType == null) return;

                var getCountMethod = logEntriesType.GetMethod("GetCount",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (getCountMethod == null) return;

                int currentTotalCount = (int)getCountMethod.Invoke(null, null);

                if (currentTotalCount == 0 && _logEntries.Count > 0)
                    ClearLogs();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Error checking console clear: {ex.Message}");
            }
        }

#if UNITY_6000_0_OR_NEWER
        /// <summary>
        /// Called whenever the console log count changes on Unity 6+.
        /// Mirrors a console clear to the in-memory buffer.
        /// </summary>
        private void OnConsoleCountChanged()
        {
            ConsoleWindowUtility.GetConsoleLogCounts(out int error, out int warning, out int log);
            if (error == 0 && warning == 0 && log == 0 && _logEntries.Count > 0)
                ClearLogs();
        }
#endif

        // ---------------------------------------------------------------------------
        // In-memory buffer management (unchanged from original)
        // ---------------------------------------------------------------------------

        /// <summary>Callback wired to Application.logMessageReceivedThreaded.</summary>
        private void OnLogMessageReceived(string logString, string stackTrace, LogType type)
        {
            lock (_logEntries)
            {
                _logEntries.Add(new LogEntry
                {
                    Message    = logString,
                    StackTrace = stackTrace,
                    Type       = type,
                    Timestamp  = DateTime.Now
                });

                if (_logEntries.Count > MaxLogEntries)
                    _logEntries.RemoveRange(0, CleanupThreshold);
            }
        }

        /// <summary>Clears all entries from the in-memory buffer.</summary>
        private void ClearLogs()
        {
            lock (_logEntries)
            {
                _logEntries.Clear();
            }
        }

        /// <summary>
        /// Trims the in-memory buffer to the most recent <paramref name="keepCount"/> entries.
        /// </summary>
        /// <param name="keepCount">Number of recent entries to retain (default 500).</param>
        public void CleanupOldLogs(int keepCount = 500)
        {
            lock (_logEntries)
            {
                if (_logEntries.Count > keepCount)
                {
                    int removeCount = _logEntries.Count - keepCount;
                    _logEntries.RemoveRange(0, removeCount);
                }
            }
        }

        /// <summary>Returns the current number of entries in the in-memory buffer.</summary>
        public int GetLogCount()
        {
            lock (_logEntries)
            {
                return _logEntries.Count;
            }
        }
    }
}

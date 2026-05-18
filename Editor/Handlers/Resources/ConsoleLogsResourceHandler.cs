using ScalableMCP.Editor;
using System;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler that returns logs from the Unity console with pagination support.
    /// URI: unity://logs/{logType}
    /// </summary>
    public class ConsoleLogsResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_console_logs";
        public override string Uri  => "unity://logs/{logType}";

        private readonly ConsoleLogsService _consoleLogsService;

        public ConsoleLogsResourceHandler(ConsoleLogsService consoleLogsService)
        {
            _consoleLogsService = consoleLogsService;
        }

        public override JObject Fetch(JObject parameters)
        {
            string logType = parameters?["logType"]?.ToString();
            if (string.IsNullOrWhiteSpace(logType)) logType = null;

            int offset             = Math.Max(0, GetIntParameter(parameters, "offset", 0));
            int limit              = Math.Max(1, Math.Min(1000, GetIntParameter(parameters, "limit", 100)));
            bool includeStackTrace = GetBoolParameter(parameters, "includeStackTrace", true);

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

        private static int GetIntParameter(JObject parameters, string key, int defaultValue)
        {
            if (parameters?[key] != null && int.TryParse(parameters[key].ToString(), out int value))
                return value;
            return defaultValue;
        }

        private static bool GetBoolParameter(JObject parameters, string key, bool defaultValue)
        {
            if (parameters?[key] != null && bool.TryParse(parameters[key].ToString(), out bool value))
                return value;
            return defaultValue;
        }
    }
}

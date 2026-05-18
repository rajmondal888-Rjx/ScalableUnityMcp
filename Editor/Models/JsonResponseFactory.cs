using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    public static class JsonResponseFactory
    {
        public static JObject Success(string message, JObject extra = null)
        {
            var obj = new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = message
            };
            if (extra != null)
                foreach (var prop in extra.Properties())
                    obj[prop.Name] = prop.Value;
            return obj;
        }

        public static JObject Error(string message, string errorType = "tool_execution_error")
        {
            return new JObject
            {
                ["success"] = false,
                ["type"]    = "text",
                ["message"] = message,
                ["error"]   = new JObject
                {
                    ["type"]    = errorType,
                    ["message"] = message
                }
            };
        }
    }
}

using System.Linq;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    /// <summary>
    /// Meta-handler: returns all auto-discovered custom tools so Node.js can register them dynamically.
    /// Called once at Node.js startup via sendRequest({ method: "get_registered_tools" }).
    /// </summary>
    public class GetRegisteredToolsHandler : ToolHandlerBase
    {
        public override string Name => "get_registered_tools";

        public override JObject Execute(JObject parameters)
        {
            var server = ScalableMcpServer.Instance;
            if (server == null)
                return JsonResponseFactory.Error("Server not initialized", "server_error");

            var toolsArray = new JArray();

            foreach (var name in HandlerRegistry.CustomToolNames)
            {
                if (!server.RegisteredTools.TryGetValue(name, out var handler))
                    continue;

                var entry = new JObject
                {
                    ["name"]        = handler.Name,
                    ["description"] = (handler as ToolHandlerBase)?.Description ?? handler.Name,
                    ["category"]    = (handler as ToolHandlerBase)?.Category ?? "Custom",
                    ["paramsSchema"] = (handler as ToolHandlerBase)?.ParamsSchema
                };
                toolsArray.Add(entry);
            }

            return new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = $"{toolsArray.Count} custom tool(s) registered",
                ["tools"]   = toolsArray
            };
        }
    }
}

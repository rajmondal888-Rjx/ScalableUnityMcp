using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEngine;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace ScalableMCP.Editor
{
    public class ScalableMcpSocketHandler : WebSocketBehavior
    {
        private readonly ScalableMcpServer _server;

        public ScalableMcpSocketHandler(ScalableMcpServer server)
        {
            _server = server;
        }

        protected override async void OnMessage(MessageEventArgs e)
        {
            try
            {
                JObject requestJson;
                try
                {
                    requestJson = JObject.Parse(e.Data);
                }
                catch (JsonReaderException jre)
                {
                    Debug.LogError($"[ScalableMCP] Invalid JSON: {jre.Message}");
                    Send(MakeResponse(null, JsonResponseFactory.Error($"Invalid JSON: {jre.Message}", "invalid_json")).ToString(Formatting.None));
                    return;
                }

                var method     = requestJson["method"]?.ToString();
                var parameters = requestJson["params"] as JObject ?? new JObject();
                var requestId  = requestJson["id"]?.ToString();
                var tcs        = new TaskCompletionSource<JObject>();

                if (string.IsNullOrEmpty(method))
                {
                    tcs.SetResult(JsonResponseFactory.Error("Missing method in request", "invalid_request"));
                }
                else if (_server.TryGetTool(method, out var tool))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
                }
                else if (_server.TryGetResource(method, out var resource))
                {
                    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResource(resource, parameters, tcs));
                }
                else
                {
                    tcs.SetResult(JsonResponseFactory.Error($"Unknown method: {method}", "unknown_method"));
                }

                var result = await tcs.Task;
                Send(MakeResponse(requestId, result).ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Error processing message: {ex.Message}");
                Send(JsonResponseFactory.Error($"Internal error: {ex.Message}", "internal_error").ToString(Formatting.None));
            }
        }

        protected override void OnOpen()
        {
            // Clean up inactive sessions to prevent FD accumulation (websocket-sharp / Mono FD_SETSIZE limit ~1024)
            var inactive = Sessions.InactiveIDs.ToList();
            foreach (var id in inactive)
            {
                _server.Clients.TryRemove(id, out _);
                try { Sessions.CloseSession(id, CloseStatusCode.Normal, "Stale session cleanup"); } catch { }
            }

            string clientName = "";
            NameValueCollection headers = Context.Headers;
            if (headers != null && headers["X-Client-Name"] is string name)
                clientName = name;

            _server.Clients[ID] = clientName;
            Debug.Log($"[ScalableMCP] Client connected: {(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)} (total: {_server.Clients.Count})");
        }

        protected override void OnClose(CloseEventArgs e)
        {
            _server.Clients.TryRemove(ID, out var name);
            Debug.Log($"[ScalableMCP] Client '{name}' disconnected (remaining: {_server.Clients.Count})");
        }

        protected override void OnError(ErrorEventArgs e)
        {
            Debug.LogError($"[ScalableMCP] WebSocket error: {e.Message}");
        }

        private IEnumerator ExecuteTool(IToolHandler tool, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (tool.IsAsync) tool.ExecuteAsync(parameters, tcs);
                else              tcs.SetResult(tool.Execute(parameters));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Tool '{tool.Name}' error: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(JsonResponseFactory.Error($"Tool '{tool.Name}' failed: {ex.Message}", "tool_execution_error"));
            }
            yield return null;
        }

        private IEnumerator FetchResource(IResourceHandler resource, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (resource.IsAsync) resource.FetchAsync(parameters, tcs);
                else                  tcs.SetResult(resource.Fetch(parameters));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Resource '{resource.Name}' error: {ex.Message}\n{ex.StackTrace}");
                tcs.SetResult(JsonResponseFactory.Error($"Resource '{resource.Name}' failed: {ex.Message}", "resource_fetch_error"));
            }
            yield return null;
        }

        private static JObject MakeResponse(string requestId, JObject result)
        {
            var response = new JObject { ["id"] = requestId };
            if (result.TryGetValue("error", out var errorObj))
                response["error"] = errorObj;
            else
                response["result"] = result;
            return response;
        }
    }
}

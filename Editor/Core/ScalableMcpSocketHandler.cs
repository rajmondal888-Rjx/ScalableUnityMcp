using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
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

        protected override void OnMessage(MessageEventArgs e)
        {
            JObject requestJson;
            try
            {
                requestJson = JObject.Parse(e.Data);
            }
            catch (JsonReaderException jre)
            {
                var errMsg = MakeResponse(null, JsonResponseFactory.Error($"Invalid JSON: {jre.Message}", "invalid_json")).ToString(Formatting.None);
                try { Send(errMsg); } catch { }
                return;
            }

            var method     = requestJson["method"]?.ToString();
            var parameters = requestJson["params"] as JObject ?? new JObject();
            var requestId  = requestJson["id"]?.ToString();
            var tcs        = new TaskCompletionSource<JObject>();

            // All Unity Editor APIs must run on the main thread — queue via delayCall
            EditorApplication.delayCall += () => DispatchToMainThread(method, parameters, tcs);

            // Send response from background thread when ready — no await, no blocking, no deadlock
            tcs.Task.ContinueWith(t =>
            {
                if (t.IsFaulted || t.IsCanceled) return;
                try { Send(MakeResponse(requestId, t.Result).ToString(Formatting.None)); }
                catch { }
            });
        }

        private void DispatchToMainThread(string method, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (_server == null) { tcs.TrySetCanceled(); return; }

                if (string.IsNullOrEmpty(method))
                    tcs.TrySetResult(JsonResponseFactory.Error("Missing method in request", "invalid_request"));
                else if (_server.TryGetTool(method, out var tool))
                    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
                else if (_server.TryGetResource(method, out var resource))
                    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResource(resource, parameters, tcs));
                else
                    tcs.TrySetResult(JsonResponseFactory.Error($"Unknown method: {method}", "unknown_method"));
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(JsonResponseFactory.Error($"Internal error: {ex.Message}", "internal_error"));
            }
        }

        protected override void OnOpen()
        {
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
            var msg = $"[ScalableMCP] Client connected: {(string.IsNullOrEmpty(clientName) ? "Unknown" : clientName)} (total: {_server.Clients.Count})";
            EditorApplication.delayCall += () => Debug.Log(msg);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            _server.Clients.TryRemove(ID, out var name);
            var msg = $"[ScalableMCP] Client '{name}' disconnected (remaining: {_server.Clients.Count})";
            EditorApplication.delayCall += () => Debug.Log(msg);
        }

        protected override void OnError(ErrorEventArgs e)
        {
            var msg = $"[ScalableMCP] WebSocket error: {e.Message}";
            EditorApplication.delayCall += () => Debug.LogError(msg);
        }

        private IEnumerator ExecuteTool(IToolHandler tool, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (tool.IsAsync) tool.ExecuteAsync(parameters, tcs);
                else              tcs.TrySetResult(tool.Execute(parameters));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Tool '{tool.Name}' error: {ex.Message}\n{ex.StackTrace}");
                tcs.TrySetResult(JsonResponseFactory.Error($"Tool '{tool.Name}' failed: {ex.Message}", "tool_execution_error"));
            }
            yield return null;
        }

        private IEnumerator FetchResource(IResourceHandler resource, JObject parameters, TaskCompletionSource<JObject> tcs)
        {
            try
            {
                if (resource.IsAsync) resource.FetchAsync(parameters, tcs);
                else                  tcs.TrySetResult(resource.Fetch(parameters));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Resource '{resource.Name}' error: {ex.Message}\n{ex.StackTrace}");
                tcs.TrySetResult(JsonResponseFactory.Error($"Resource '{resource.Name}' failed: {ex.Message}", "resource_fetch_error"));
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

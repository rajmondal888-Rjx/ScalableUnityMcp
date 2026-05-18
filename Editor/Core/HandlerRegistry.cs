using System;
using System.Collections.Generic;
using System.Linq;
using ScalableMCP.Editor.Handlers;
using UnityEngine;

namespace ScalableMCP.Editor
{
    public static class HandlerRegistry
    {
        // Names of tools registered via auto-discovery (not baseline). Used by the Editor window and Node.js relay.
        public static HashSet<string> CustomToolNames { get; } = new HashSet<string>();

        public static void RegisterAll(
            Dictionary<string, IToolHandler> tools,
            Dictionary<string, IResourceHandler> resources,
            ConsoleLogsService consoleLogs,
            TestRunnerService testRunner)
        {
            CustomToolNames.Clear();

            // ---- BASELINE TOOL HANDLERS ----
            SceneHandler.RegisterAll(tools);
            GameObjectHandler.RegisterAll(tools);
            TransformHandler.RegisterAll(tools);
            ComponentHandler.RegisterAll(tools, tools);
            MaterialHandler.RegisterAll(tools);
            PrefabHandler.RegisterAll(tools);
            EditorHandler.RegisterAll(tools, testRunner);

            // ---- BASELINE RESOURCE HANDLERS ----
            Register(resources, new MenuItemsResourceHandler());
            Register(resources, new HierarchyResourceHandler());
            Register(resources, new PackagesResourceHandler());
            Register(resources, new AssetsResourceHandler());
            Register(resources, new TestsResourceHandler(testRunner));
            Register(resources, new ConsoleLogsResourceHandler(consoleLogs));
            Register(resources, new GameObjectResourceHandler());

            // ---- META HANDLER ----
            Register(tools, new GetRegisteredToolsHandler());

            // ---- AUTO-DISCOVERED CUSTOM HANDLERS ----
            // Any class that inherits ToolHandlerBase (and is not DelegateToolHandler or GetRegisteredToolsHandler)
            // is auto-registered here — no manual step needed.
            var customTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t =>
                    typeof(ToolHandlerBase).IsAssignableFrom(t) &&
                    !t.IsAbstract &&
                    t != typeof(DelegateToolHandler) &&
                    t != typeof(GetRegisteredToolsHandler));

            foreach (var type in customTypes)
            {
                try
                {
                    var handler = (IToolHandler)Activator.CreateInstance(type);
                    if (!tools.ContainsKey(handler.Name))
                    {
                        Register(tools, handler);
                        CustomToolNames.Add(handler.Name);
                        Debug.Log($"[ScalableMCP] Auto-registered custom tool: {handler.Name} ({type.Name})");
                    }
                    else
                    {
                        Debug.LogWarning($"[ScalableMCP] Skipped {type.Name} — tool name '{handler.Name}' already registered");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[ScalableMCP] Failed to auto-register {type.Name}: {ex.Message}");
                }
            }
        }

        public static void Register(Dictionary<string, IToolHandler> d, IToolHandler h)
            => d[h.Name] = h;

        public static void Register(Dictionary<string, IResourceHandler> d, IResourceHandler h)
            => d[h.Name] = h;
    }
}

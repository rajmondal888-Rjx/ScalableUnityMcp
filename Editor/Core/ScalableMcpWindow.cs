using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ScalableMCP.Editor
{
    public class ScalableMcpWindow : EditorWindow
    {
        private Vector2 _scroll;
        private bool _baselineFoldout = true;
        private bool _customFoldout   = true;
        private string _dropFeedback  = "";
        private double _feedbackClearTime;

        [MenuItem("Tools/Scalable MCP")]
        public static void ShowWindow()
            => GetWindow<ScalableMcpWindow>("Scalable MCP").minSize = new Vector2(320, 300);

        private void OnGUI()
        {
            var server   = ScalableMcpServer.Instance;
            var settings = ScalableMcpSettings.Instance;
            bool listening = server?.IsListening ?? false;

            // ── Status panel ─────────────────────────────────────────
            EditorGUILayout.Space(6);

            var statusBg = new GUIStyle(EditorStyles.helpBox);
            EditorGUILayout.BeginVertical(statusBg);

            EditorGUILayout.BeginHorizontal();

            // Coloured dot
            var prevColor = GUI.color;
            GUI.color = listening ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.35f, 0.35f);
            GUILayout.Label("●", EditorStyles.boldLabel, GUILayout.Width(20));
            GUI.color = prevColor;

            // Status text
            string statusText = listening ? "Running" : "Stopped";
            EditorGUILayout.LabelField(statusText, EditorStyles.boldLabel, GUILayout.Width(70));

            // Port
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Port:", GUILayout.Width(32));
            EditorGUILayout.LabelField(settings.Port.ToString(), EditorStyles.boldLabel, GUILayout.Width(50));

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);

            // ── Server control buttons ───────────────────────────────
            EditorGUILayout.BeginHorizontal();

            if (listening)
            {
                if (GUILayout.Button("Stop", GUILayout.Height(28)))
                    server.StopServer();
            }
            else
            {
                GUI.enabled = NodeServerInstaller.IsInstalled;
                if (GUILayout.Button("Start", GUILayout.Height(28)))
                    server?.StartServer();
                GUI.enabled = true;
            }

            if (GUILayout.Button("Restart", GUILayout.Height(28)))
            {
                server?.StopServer();
                server?.StartServer();
                Repaint();
            }

            if (GUILayout.Button("⚙", GUILayout.Height(28), GUILayout.Width(28)))
                OpenCustomFolder();

            EditorGUILayout.EndHorizontal();

            // ── Node.js setup row (only when not built) ──────────────
            if (!NodeServerInstaller.IsInstalled)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("Node.js server not built yet. Click Run Setup.", MessageType.Warning);
                if (GUILayout.Button("Run Setup", GUILayout.Height(24)))
                    _ = NodeServerInstaller.RunSetupAsync();
            }

            EditorGUILayout.Space(6);
            DrawSeparator();

            if (server == null)
            {
                EditorGUILayout.HelpBox("Server not initialized.", MessageType.Warning);
                return;
            }

            // ── Tool lists ──────────────────────────────────────────
            var allTools    = server.RegisteredTools;
            var customNames = HandlerRegistry.CustomToolNames;

            var baselineNames = allTools.Keys
                .Where(k => !customNames.Contains(k) && k != "get_registered_tools")
                .OrderBy(k => k).ToList();

            var customToolList = allTools.Keys
                .Where(k => customNames.Contains(k))
                .OrderBy(k => k).ToList();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // Baseline
            _baselineFoldout = EditorGUILayout.Foldout(
                _baselineFoldout, $"Baseline Tools  ({baselineNames.Count})", true, EditorStyles.foldoutHeader);
            if (_baselineFoldout)
            {
                EditorGUI.indentLevel++;
                foreach (var n in baselineNames)
                    EditorGUILayout.LabelField(n, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // Custom
            _customFoldout = EditorGUILayout.Foldout(
                _customFoldout, $"Custom Tools  ({customToolList.Count})", true, EditorStyles.foldoutHeader);
            if (_customFoldout)
            {
                EditorGUI.indentLevel++;
                if (customToolList.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No custom tools found.\n\nCreate a class that inherits ToolHandlerBase anywhere in the project — it is auto-registered on the next recompile.",
                        MessageType.Info);
                }
                else
                {
                    foreach (var n in customToolList)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(n, GUILayout.ExpandWidth(true));
                        if (allTools.TryGetValue(n, out var h) && h is ToolHandlerBase tb)
                        {
                            GUI.color = new Color(0.6f, 0.85f, 1f);
                            EditorGUILayout.LabelField(tb.Category, EditorStyles.miniLabel, GUILayout.Width(80));
                            GUI.color = Color.white;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();

            // ── Drag-drop zone ──────────────────────────────────────
            DrawSeparator();
            DrawDropZone(allTools);
        }

        private void DrawDropZone(System.Collections.Generic.IReadOnlyDictionary<string, IToolHandler> allTools)
        {
            var dropRect = EditorGUILayout.GetControlRect(false, 52);
            var style    = new GUIStyle(EditorStyles.helpBox) { alignment = TextAnchor.MiddleCenter, fontSize = 11 };
            GUI.Box(dropRect, string.IsNullOrEmpty(_dropFeedback)
                ? "Drop a handler script or folder here to check registration"
                : _dropFeedback, style);

            var ev = Event.current;
            if (!dropRect.Contains(ev.mousePosition)) return;

            if (ev.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = HasHandlerScripts() ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                ev.Use();
            }
            else if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ProcessDrop(allTools);
                ev.Use();
            }

            // Clear feedback after 4 seconds
            if (!string.IsNullOrEmpty(_dropFeedback) && EditorApplication.timeSinceStartup > _feedbackClearTime)
            {
                _dropFeedback = "";
                Repaint();
            }
        }

        private bool HasHandlerScripts()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is MonoScript ms && IsHandlerType(ms.GetClass()))
                    return true;
                if (obj is DefaultAsset folder && HasHandlerInFolder(AssetDatabase.GetAssetPath(folder)))
                    return true;
            }
            return false;
        }

        private void ProcessDrop(System.Collections.Generic.IReadOnlyDictionary<string, IToolHandler> allTools)
        {
            var found = new System.Collections.Generic.List<string>();
            var notFound = new System.Collections.Generic.List<string>();

            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is MonoScript ms)
                    CheckScript(ms.GetClass(), allTools, found, notFound);
                else if (obj is DefaultAsset folder)
                    CheckFolder(AssetDatabase.GetAssetPath(folder), allTools, found, notFound);
            }

            if (found.Count == 0 && notFound.Count == 0)
                _dropFeedback = "No ToolHandlerBase subclasses found in dropped files.";
            else
            {
                var lines = new System.Text.StringBuilder();
                foreach (var n in found)     lines.AppendLine($"✓ {n}  [registered]");
                foreach (var n in notFound)  lines.AppendLine($"⊘ {n}  [needs recompile]");
                _dropFeedback = lines.ToString().TrimEnd();
            }

            _feedbackClearTime = EditorApplication.timeSinceStartup + 5.0;
            Repaint();
        }

        private void CheckScript(
            Type type,
            System.Collections.Generic.IReadOnlyDictionary<string, IToolHandler> allTools,
            System.Collections.Generic.List<string> found,
            System.Collections.Generic.List<string> notFound)
        {
            if (!IsHandlerType(type)) return;
            try
            {
                var h = (ToolHandlerBase)Activator.CreateInstance(type);
                if (allTools.ContainsKey(h.Name)) found.Add(h.Name);
                else                               notFound.Add(h.Name);
            }
            catch { }
        }

        private void CheckFolder(
            string folderPath,
            System.Collections.Generic.IReadOnlyDictionary<string, IToolHandler> allTools,
            System.Collections.Generic.List<string> found,
            System.Collections.Generic.List<string> notFound)
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms   = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null) CheckScript(ms.GetClass(), allTools, found, notFound);
            }
        }

        private bool HasHandlerInFolder(string folderPath)
        {
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folderPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms   = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && IsHandlerType(ms.GetClass())) return true;
            }
            return false;
        }

        private static bool IsHandlerType(Type t)
            => t != null &&
               typeof(ToolHandlerBase).IsAssignableFrom(t) &&
               !t.IsAbstract &&
               t != typeof(DelegateToolHandler) &&
               t != typeof(GetRegisteredToolsHandler);

        private void OpenCustomFolder()
        {
            // Try to locate the custom folder relative to the project
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] candidates =
            {
                Path.Combine(projectRoot, "Packages", "com.scalable.mcp-unity", "Editor", "Custom"),
                Path.Combine(projectRoot, "..", "scalable-unity-mcp", "unity-plugin", "Editor", "Custom"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                {
                    EditorUtility.RevealInFinder(path);
                    return;
                }
            }

            EditorUtility.RevealInFinder(projectRoot);
        }

        private static void DrawSeparator()
        {
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
            EditorGUILayout.Space(4);
        }

        private void OnInspectorUpdate() => Repaint();
    }
}

using ScalableMCP.Editor;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers the create_prefab tool handler.
    /// </summary>
    public static class PrefabHandler
    {
        public static void RegisterAll(Dictionary<string, IToolHandler> tools)
        {
            tools["create_prefab"] = new DelegateToolHandler("create_prefab", CreatePrefab);
        }

        // -------------------------------------------------------------------------
        // create_prefab
        // -------------------------------------------------------------------------
        private static JObject CreatePrefab(JObject parameters)
        {
            string componentName = parameters["componentName"]?.ToObject<string>();
            string prefabName    = parameters["prefabName"]?.ToObject<string>();
            JObject fieldValues  = parameters["fieldValues"]?.ToObject<JObject>();

            if (string.IsNullOrEmpty(prefabName))
                return JsonResponseFactory.Error("Required parameter 'prefabName' not provided", "validation_error");

            GameObject tempObject = new GameObject(prefabName);

            if (!string.IsNullOrEmpty(componentName))
            {
                try
                {
                    Component component = AddComponent(tempObject, componentName);
                    ApplyFieldValues(fieldValues, component);
                }
                catch (Exception)
                {
                    return JsonResponseFactory.Error($"Failed to add component '{componentName}' to GameObject", "component_error");
                }
            }

            int counter = 1;
            string prefabPath = $"{prefabName}.prefab";
            while (AssetDatabase.AssetPathToGUID(prefabPath) != "")
            {
                prefabPath = $"{prefabName}_{counter}.prefab";
                counter++;
            }

            PrefabUtility.SaveAsPrefabAsset(tempObject, prefabPath, out bool success);
            UnityEngine.Object.DestroyImmediate(tempObject);
            AssetDatabase.Refresh();

            Debug.Log($"[ScalableMCP] Created prefab '{prefabName}' at path '{prefabPath}' from script '{componentName}'");

            string message = success
                ? $"Successfully created prefab '{prefabName}' at path '{prefabPath}'"
                : $"Failed to create prefab '{prefabName}' at path '{prefabPath}'";

            return new JObject
            {
                ["success"]    = success,
                ["type"]       = "text",
                ["message"]    = message,
                ["prefabPath"] = prefabPath
            };
        }

        private static Component AddComponent(GameObject go, string componentName)
        {
            Type scriptType = Type.GetType($"{componentName}, Assembly-CSharp")
                           ?? Type.GetType(componentName);

            if (scriptType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    scriptType = assembly.GetType(componentName);
                    if (scriptType != null) break;
                }
            }

            if (scriptType == null) return null;
            if (!typeof(MonoBehaviour).IsAssignableFrom(scriptType)) return null;

            return go.AddComponent(scriptType);
        }

        private static void ApplyFieldValues(JObject fieldValues, Component component)
        {
            if (fieldValues == null || fieldValues.Count == 0 || component == null) return;

            Undo.RecordObject(component, "Set field values");

            foreach (var property in fieldValues.Properties())
            {
                var fieldInfo = component.GetType().GetField(property.Name,
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (fieldInfo != null)
                {
                    object value = property.Value.ToObject(fieldInfo.FieldType);
                    fieldInfo.SetValue(component, value);
                }
                else
                {
                    var propInfo = component.GetType().GetProperty(property.Name,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance);

                    if (propInfo != null && propInfo.CanWrite)
                    {
                        object value = property.Value.ToObject(propInfo.PropertyType);
                        propInfo.SetValue(component, value);
                    }
                }
            }
        }
    }
}

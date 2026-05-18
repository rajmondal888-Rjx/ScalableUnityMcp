using ScalableMCP.Editor;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers the update_component tool handler.
    /// </summary>
    public static class ComponentHandler
    {
        /// <summary>
        /// Register update_component into the tools dictionary.
        /// The second parameter (allTools) is provided for future use (e.g., batch_execute references)
        /// but update_component is the only handler registered here.
        /// </summary>
        public static void RegisterAll(Dictionary<string, IToolHandler> tools, Dictionary<string, IToolHandler> allTools)
        {
            tools["update_component"] = new DelegateToolHandler("update_component", UpdateComponent);
        }

        // -------------------------------------------------------------------------
        // update_component
        // -------------------------------------------------------------------------
        private static JObject UpdateComponent(JObject parameters)
        {
            int? instanceId      = parameters["instanceId"]?.ToObject<int?>();
            string objectPath    = parameters["objectPath"]?.ToObject<string>();
            string componentName = parameters["componentName"]?.ToObject<string>();
            JObject componentData= parameters["componentData"] as JObject;

            if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
                return JsonResponseFactory.Error("Either 'instanceId' or 'objectPath' must be provided", "validation_error");

            if (string.IsNullOrEmpty(componentName))
                return JsonResponseFactory.Error("Required parameter 'componentName' not provided", "validation_error");

            // Find the GameObject
            GameObject go = null;
            string identifier = "unknown";

            if (instanceId.HasValue)
            {
                go = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifier = $"ID {instanceId.Value}";
            }
            else
            {
                go = GameObject.Find(objectPath);
                identifier = $"path '{objectPath}'";

                if (go == null)
                    go = GameObjectUtils.FindByPath(objectPath);
            }

            if (go == null)
                return JsonResponseFactory.Error($"GameObject with path '{objectPath}' or instance ID {instanceId} not found", "not_found_error");

            Debug.Log($"[ScalableMCP] Updating component '{componentName}' on GameObject '{go.name}' (found by {identifier})");

            Component component = go.GetComponent(componentName);

            if (component == null)
            {
                Type componentType = FindComponentType(componentName);
                if (componentType == null)
                    return JsonResponseFactory.Error($"Component type '{componentName}' not found in Unity", "component_error");

                component = Undo.AddComponent(go, componentType);
                EditorUtility.SetDirty(go);
                if (PrefabUtility.IsPartOfAnyPrefab(go))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);

                Debug.Log($"[ScalableMCP] Added component '{componentName}' to GameObject '{go.name}'");
            }

            if (componentData != null && componentData.Count > 0)
            {
                bool success = UpdateComponentData(component, componentData, out string errorMessage);
                if (!success)
                    return JsonResponseFactory.Error(errorMessage, "update_error");

                EditorUtility.SetDirty(go);
                if (PrefabUtility.IsPartOfAnyPrefab(go))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }

            return new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = $"Successfully updated component '{componentName}' on GameObject '{go.name}'"
            };
        }

        private static Type FindComponentType(string componentName)
        {
            Type type = Type.GetType(componentName);
            if (type != null && typeof(Component).IsAssignableFrom(type)) return type;

            string[] commonNamespaces = {
                "UnityEngine", "UnityEngine.UI", "UnityEngine.EventSystems",
                "UnityEngine.Animations", "UnityEngine.Rendering", "TMPro"
            };

            foreach (string ns in commonNamespaces)
            {
                type = Type.GetType($"{ns}.{componentName}, UnityEngine");
                if (type != null && typeof(Component).IsAssignableFrom(type)) return type;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (Type t in assembly.GetTypes())
                    {
                        if (t.Name == componentName && typeof(Component).IsAssignableFrom(t))
                            return t;
                    }
                }
                catch (Exception) { continue; }
            }

            return null;
        }

        private static bool UpdateComponentData(Component component, JObject componentData, out string errorMessage)
        {
            errorMessage = "";

            if (component == null || componentData == null)
            {
                errorMessage = "Component or component data is null";
                return false;
            }

            Type componentType = component.GetType();
            bool fullSuccess   = true;

            Undo.RecordObject(component, $"Update {componentType.Name} fields");

            foreach (var property in componentData.Properties())
            {
                string fieldName  = property.Name;
                JToken fieldValue = property.Value;

                if (string.IsNullOrEmpty(fieldName) || fieldValue.Type == JTokenType.Null)
                    continue;

                FieldInfo fieldInfo = componentType.GetField(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (fieldInfo != null)
                {
                    object value = ConvertJTokenToValue(fieldValue, fieldInfo.FieldType);
                    fieldInfo.SetValue(component, value);
                    continue;
                }

                PropertyInfo propertyInfo = componentType.GetProperty(fieldName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (propertyInfo != null)
                {
                    object value = ConvertJTokenToValue(fieldValue, propertyInfo.PropertyType);
                    propertyInfo.SetValue(component, value);
                    continue;
                }

                fullSuccess  = false;
                errorMessage = $"Field or Property with name '{fieldName}' not found on component '{componentType.Name}'";
            }

            return fullSuccess;
        }

        private static object ConvertJTokenToValue(JToken token, Type targetType)
        {
            if (token == null) return null;

            if (targetType == typeof(Vector2) && token.Type == JTokenType.Object)
            {
                JObject v = (JObject)token;
                return new Vector2(v["x"]?.ToObject<float>() ?? 0f, v["y"]?.ToObject<float>() ?? 0f);
            }

            if (targetType == typeof(Vector3) && token.Type == JTokenType.Object)
            {
                JObject v = (JObject)token;
                return new Vector3(v["x"]?.ToObject<float>() ?? 0f, v["y"]?.ToObject<float>() ?? 0f, v["z"]?.ToObject<float>() ?? 0f);
            }

            if (targetType == typeof(Vector4) && token.Type == JTokenType.Object)
            {
                JObject v = (JObject)token;
                return new Vector4(v["x"]?.ToObject<float>() ?? 0f, v["y"]?.ToObject<float>() ?? 0f, v["z"]?.ToObject<float>() ?? 0f, v["w"]?.ToObject<float>() ?? 0f);
            }

            if (targetType == typeof(Quaternion) && token.Type == JTokenType.Object)
            {
                JObject q = (JObject)token;
                return new Quaternion(q["x"]?.ToObject<float>() ?? 0f, q["y"]?.ToObject<float>() ?? 0f, q["z"]?.ToObject<float>() ?? 0f, q["w"]?.ToObject<float>() ?? 1f);
            }

            if (targetType == typeof(Color) && token.Type == JTokenType.Object)
            {
                JObject c = (JObject)token;
                return new Color(c["r"]?.ToObject<float>() ?? 0f, c["g"]?.ToObject<float>() ?? 0f, c["b"]?.ToObject<float>() ?? 0f, c["a"]?.ToObject<float>() ?? 1f);
            }

            if (targetType == typeof(Bounds) && token.Type == JTokenType.Object)
            {
                JObject b = (JObject)token;
                Vector3 center = b["center"]?.ToObject<Vector3>() ?? Vector3.zero;
                Vector3 size   = b["size"]?.ToObject<Vector3>()   ?? Vector3.one;
                return new Bounds(center, size);
            }

            if (targetType == typeof(Rect) && token.Type == JTokenType.Object)
            {
                JObject r = (JObject)token;
                return new Rect(r["x"]?.ToObject<float>() ?? 0f, r["y"]?.ToObject<float>() ?? 0f, r["width"]?.ToObject<float>() ?? 0f, r["height"]?.ToObject<float>() ?? 0f);
            }

            if (targetType == typeof(UnityEngine.Object))
                return token.ToObject<UnityEngine.Object>();

            if (targetType.IsEnum)
            {
                if (token.Type == JTokenType.String)
                {
                    string enumName = token.ToObject<string>();
                    if (Enum.TryParse(targetType, enumName, true, out object result)) return result;
                    if (int.TryParse(enumName, out int enumValue)) return Enum.ToObject(targetType, enumValue);
                }
                else if (token.Type == JTokenType.Integer)
                {
                    return Enum.ToObject(targetType, token.ToObject<int>());
                }
            }

            try
            {
                return token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScalableMCP] Error converting value to type {targetType.Name}: {ex.Message}");
                return null;
            }
        }
    }
}

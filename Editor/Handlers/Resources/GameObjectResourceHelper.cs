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
    /// Static helper that converts GameObjects to JObjects.
    /// Shared between GameObjectResourceHandler, HierarchyResourceHandler, and GameObjectHandler.
    /// </summary>
    public static class GameObjectResourceHelper
    {
        /// <summary>
        /// Convert a GameObject to a JObject with its hierarchy
        /// </summary>
        /// <param name="gameObject">The GameObject to convert</param>
        /// <param name="includeDetailedComponents">Whether to include detailed component information</param>
        public static JObject GameObjectToJObject(GameObject gameObject, bool includeDetailedComponents)
        {
            if (gameObject == null) return null;

            JArray childrenArray = new JArray();
            foreach (Transform child in gameObject.transform)
                childrenArray.Add(GameObjectToJObject(child.gameObject, includeDetailedComponents));

            return new JObject
            {
                ["name"]              = gameObject.name,
                ["activeSelf"]        = gameObject.activeSelf,
                ["activeInHierarchy"] = gameObject.activeInHierarchy,
                ["tag"]               = gameObject.tag,
                ["layer"]             = gameObject.layer,
                ["layerName"]         = LayerMask.LayerToName(gameObject.layer),
                ["instanceId"]        = gameObject.GetInstanceID(),
                ["components"]        = GetComponentsInfo(gameObject, includeDetailedComponents),
                ["children"]          = childrenArray
            };
        }

        // -------------------------------------------------------------------------
        // Safety lists (ported verbatim from GetGameObjectResource)
        // -------------------------------------------------------------------------
        private static readonly string[] UnsafeNamespacePrefixes = { "Pathfinding", "FMOD", "FMODUnity" };

        private static readonly Type[] UnsafeDetailedInspectionBaseTypes = { typeof(Collider) };

        private static readonly HashSet<string> GloballySkippedPropertyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "mesh", "sharedMesh", "material", "materials",
            "sharedMaterial", "sharedMaterials", "sprite",
            "mainTexture", "mainTextureOffset", "mainTextureScale"
        };

        private static readonly Dictionary<Type, HashSet<string>> SkippedPropertiesByComponentType = new Dictionary<Type, HashSet<string>>
        {
            [typeof(Collider)] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GeometryHolder" }
        };

        private static bool IsUnsafeNativeComponent(Type componentType)
        {
            if (componentType == null) return true;
            string fullName  = componentType.FullName ?? "";
            string namespaceName = componentType.Namespace ?? "";
            foreach (string prefix in UnsafeNamespacePrefixes)
            {
                if (namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool ShouldSkipDetailedInspection(Type componentType)
        {
            if (IsUnsafeNativeComponent(componentType)) return true;
            foreach (Type unsafeBase in UnsafeDetailedInspectionBaseTypes)
                if (unsafeBase.IsAssignableFrom(componentType)) return true;
            return false;
        }

        private static JArray GetComponentsInfo(GameObject gameObject, bool includeDetailedInfo)
        {
            Component[] components = gameObject.GetComponents<Component>();
            JArray componentsArray = new JArray();

            foreach (Component component in components)
            {
                if (component == null) continue;
                Type componentType = component.GetType();
                bool skipDetailed  = ShouldSkipDetailedInspection(componentType);

                JObject componentJson = new JObject
                {
                    ["type"]    = componentType.Name,
                    ["enabled"] = IsComponentEnabled(component)
                };

                if (includeDetailedInfo)
                {
                    if (skipDetailed)
                    {
                        componentJson["properties"] = new JObject
                        {
                            ["_skipped"] = "Detailed property serialization skipped for safety"
                        };
                    }
                    else
                    {
                        componentJson["properties"] = GetComponentProperties(component);
                    }
                }

                componentsArray.Add(componentJson);
            }

            return componentsArray;
        }

        private static bool IsComponentEnabled(Component component)
        {
            if (component is Behaviour behaviour) return behaviour.enabled;
            if (component is Renderer renderer)   return renderer.enabled;
            if (component is Collider collider)   return collider.enabled;
            return true;
        }

        private const int MaxSerializationDepth = 5;
        private const int MaxCollectionItems    = 50;

        private static JObject GetComponentProperties(Component component)
        {
            if (component == null) return null;

            JObject propertiesJson = new JObject();
            Type componentType     = component.GetType();
            HashSet<object> visited = new HashSet<object>(new ReferenceEqualityComparer());

            FieldInfo[] fields = componentType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields)
            {
                bool isSerialized = field.IsPublic || field.GetCustomAttributes(typeof(SerializeField), true).Length > 0;
                if (!isSerialized) continue;
                try
                {
                    object value = field.GetValue(component);
                    propertiesJson[field.Name] = SerializeValue(value, 0, visited);
                }
                catch (Exception) { propertiesJson[field.Name] = "Unable to serialize"; }
            }

            PropertyInfo[] properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in properties)
            {
                if (!property.CanRead || ShouldSkipProperty(componentType, property)) continue;
                try
                {
                    object value = property.GetValue(component);
                    propertiesJson[property.Name] = SerializeValue(value, 0, visited);
                }
                catch (Exception) { propertiesJson[property.Name] = "Unable to serialize"; }
            }

            return propertiesJson;
        }

        private class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private static bool ShouldSkipProperty(Type componentType, PropertyInfo property)
        {
            if (property == null) return true;
            if (property.GetMethod == null || !property.GetMethod.IsPublic || property.GetIndexParameters().Length > 0) return true;
            if (GloballySkippedPropertyNames.Contains(property.Name)) return true;

            foreach (var entry in SkippedPropertiesByComponentType)
            {
                if (entry.Key.IsAssignableFrom(componentType) && entry.Value.Contains(property.Name))
                    return true;
            }
            return false;
        }

        private static JToken SerializeValue(object value, int depth = 0, HashSet<object> visited = null)
        {
            if (value == null)           return JValue.CreateNull();
            if (depth > MaxSerializationDepth) return "[max depth exceeded]";

            Type valueType = value.GetType();

            if (!valueType.IsValueType && !(value is string))
            {
                if (visited == null) visited = new HashSet<object>(new ReferenceEqualityComparer());
                if (visited.Contains(value)) return "[circular reference]";
                visited.Add(value);
            }

            if (value is Vector2 v2) return new JObject { ["x"] = v2.x, ["y"] = v2.y };
            if (value is Vector3 v3) return new JObject { ["x"] = v3.x, ["y"] = v3.y, ["z"] = v3.z };
            if (value is Vector4 v4) return new JObject { ["x"] = v4.x, ["y"] = v4.y, ["z"] = v4.z, ["w"] = v4.w };
            if (value is Quaternion q) return new JObject { ["x"] = q.x, ["y"] = q.y, ["z"] = q.z, ["w"] = q.w };
            if (value is Color c) return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
            if (value is Bounds bounds) return new JObject
            {
                ["center"] = SerializeValue(bounds.center, depth + 1, visited),
                ["size"]   = SerializeValue(bounds.size,   depth + 1, visited)
            };
            if (value is Rect rect) return new JObject { ["x"] = rect.x, ["y"] = rect.y, ["width"] = rect.width, ["height"] = rect.height };
            if (value is UnityEngine.Object uo) return uo != null ? uo.name : null;

            if (value is System.Collections.IList list)
            {
                JArray array = new JArray();
                int count = 0;
                foreach (var item in list)
                {
                    if (count >= MaxCollectionItems) { array.Add($"[... and {list.Count - count} more items]"); break; }
                    array.Add(SerializeValue(item, depth + 1, visited));
                    count++;
                }
                return array;
            }

            if (value is System.Collections.IDictionary dict)
            {
                JObject obj = new JObject();
                int count = 0;
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (count >= MaxCollectionItems) { obj["_truncated"] = $"{dict.Count - count} more entries"; break; }
                    obj[entry.Key.ToString()] = SerializeValue(entry.Value, depth + 1, visited);
                    count++;
                }
                return obj;
            }

            if (value is Enum enumValue) return enumValue.ToString();
            if (valueType.IsPrimitive || value is string || value is decimal) return JToken.FromObject(value);

            return $"[{valueType.Name}]";
        }
    }
}

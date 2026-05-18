using ScalableMCP.Editor;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers all material-related tool handlers:
    /// create_material, assign_material, modify_material, get_material_info.
    /// </summary>
    public static class MaterialHandler
    {
        public static void RegisterAll(Dictionary<string, IToolHandler> tools)
        {
            tools["create_material"]   = new DelegateToolHandler("create_material",   CreateMaterial);
            tools["assign_material"]   = new DelegateToolHandler("assign_material",   AssignMaterial);
            tools["modify_material"]   = new DelegateToolHandler("modify_material",   ModifyMaterial);
            tools["get_material_info"] = new DelegateToolHandler("get_material_info", GetMaterialInfo);
        }

        // -------------------------------------------------------------------------
        // Shared helpers
        // -------------------------------------------------------------------------
        private static string GetDefaultShaderName()
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
            {
                string pipelineName = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().Name;
                if (pipelineName.Contains("Universal") || pipelineName.Contains("URP"))
                    return "Universal Render Pipeline/Lit";
                if (pipelineName.Contains("HD") || pipelineName.Contains("HDRP"))
                    return "HDRP/Lit";
                Debug.LogWarning("[ScalableMCP] Unknown render pipeline, defaulting to Standard shader");
            }
            return "Standard";
        }

        private static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null) return shader;

            string[] prefixes = {
                "", "Standard", "Universal Render Pipeline/", "URP/", "HDRP/",
                "Hidden/", "Legacy Shaders/", "Mobile/", "Particles/", "Skybox/", "Sprites/", "UI/", "Unlit/"
            };

            foreach (string prefix in prefixes)
            {
                shader = Shader.Find(prefix + shaderName);
                if (shader != null) return shader;
            }
            return null;
        }

        private static Material LoadMaterial(string materialPath)
        {
            if (string.IsNullOrEmpty(materialPath)) return null;
            if (!materialPath.StartsWith("Assets/")) materialPath = "Assets/" + materialPath;
            if (!materialPath.EndsWith(".mat"))      materialPath += ".mat";
            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        private static GameObject FindGameObject(int? instanceId, string objectPath)
        {
            if (instanceId.HasValue)
                return EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
            if (!string.IsNullOrEmpty(objectPath))
            {
                var go = GameObject.Find(objectPath);
                return go ?? GameObjectUtils.FindByPath(objectPath);
            }
            return null;
        }

        private static object ConvertPropertyValue(JToken token, ShaderUtil.ShaderPropertyType propertyType)
        {
            if (token == null) return null;

            switch (propertyType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    if (token.Type == JTokenType.Object)
                    {
                        JObject c = (JObject)token;
                        return new Color(c["r"]?.ToObject<float>() ?? 0f, c["g"]?.ToObject<float>() ?? 0f, c["b"]?.ToObject<float>() ?? 0f, c["a"]?.ToObject<float>() ?? 1f);
                    }
                    break;
                case ShaderUtil.ShaderPropertyType.Vector:
                    if (token.Type == JTokenType.Object)
                    {
                        JObject v = (JObject)token;
                        return new Vector4(v["x"]?.ToObject<float>() ?? 0f, v["y"]?.ToObject<float>() ?? 0f, v["z"]?.ToObject<float>() ?? 0f, v["w"]?.ToObject<float>() ?? 0f);
                    }
                    break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    return token.ToObject<float>();
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    string texPath = token.ToObject<string>();
                    if (!string.IsNullOrEmpty(texPath))
                    {
                        if (!texPath.StartsWith("Assets/")) texPath = "Assets/" + texPath;
                        return AssetDatabase.LoadAssetAtPath<Texture>(texPath);
                    }
                    break;
                case ShaderUtil.ShaderPropertyType.Int:
                    return token.ToObject<int>();
            }
            return null;
        }

        private static void SetMaterialProperty(Material material, string propName, ShaderUtil.ShaderPropertyType propType, object value)
        {
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:   material.SetColor(propName, (Color)value);   break;
                case ShaderUtil.ShaderPropertyType.Vector:  material.SetVector(propName, (Vector4)value); break;
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:   material.SetFloat(propName, (float)value);   break;
                case ShaderUtil.ShaderPropertyType.TexEnv:  material.SetTexture(propName, (Texture)value); break;
                case ShaderUtil.ShaderPropertyType.Int:     material.SetInt(propName, (int)value);        break;
            }
        }

        // -------------------------------------------------------------------------
        // create_material
        // -------------------------------------------------------------------------
        private static JObject CreateMaterial(JObject parameters)
        {
            string name       = parameters["name"]?.ToObject<string>();
            string shaderName = parameters["shader"]?.ToObject<string>();
            string savePath   = parameters["savePath"]?.ToObject<string>();
            JObject properties= parameters["properties"] as JObject;
            JObject colorParam= parameters["color"] as JObject;

            if (string.IsNullOrEmpty(name))
                return JsonResponseFactory.Error("Required parameter 'name' not provided", "validation_error");
            if (string.IsNullOrEmpty(savePath))
                return JsonResponseFactory.Error("Required parameter 'savePath' not provided", "validation_error");

            if (string.IsNullOrEmpty(shaderName))
                shaderName = GetDefaultShaderName();

            Shader shader = FindShader(shaderName);
            if (shader == null)
                return JsonResponseFactory.Error($"Shader '{shaderName}' not found in Unity", "not_found_error");

            if (!savePath.StartsWith("Assets/")) savePath = "Assets/" + savePath;
            if (!savePath.EndsWith(".mat"))       savePath += ".mat";

            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            Material material = new Material(shader);
            material.name = name;

            if (colorParam != null)
            {
                Color color = new Color(
                    colorParam["r"]?.ToObject<float>() ?? 1f,
                    colorParam["g"]?.ToObject<float>() ?? 1f,
                    colorParam["b"]?.ToObject<float>() ?? 1f,
                    colorParam["a"]?.ToObject<float>() ?? 1f
                );
                ApplyBaseColor(material, color);
            }

            if (properties != null && properties.Count > 0)
                ApplyMaterialProperties(material, properties);

            AssetDatabase.CreateAsset(material, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ScalableMCP] Created material '{name}' with shader '{shaderName}' at '{savePath}'");

            return new JObject
            {
                ["success"]      = true,
                ["type"]         = "text",
                ["message"]      = $"Successfully created material '{name}' with shader '{shaderName}'",
                ["materialPath"] = savePath,
                ["materialName"] = name,
                ["shaderName"]   = shader.name
            };
        }

        private static void ApplyMaterialProperties(Material material, JObject properties)
        {
            Shader shader         = material.shader;
            int propertyCount     = ShaderUtil.GetPropertyCount(shader);

            foreach (var prop in properties.Properties())
            {
                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyName(shader, i) == prop.Name)
                    {
                        ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);
                        object value = ConvertPropertyValue(prop.Value, propType);
                        if (value != null) SetMaterialProperty(material, prop.Name, propType, value);
                        break;
                    }
                }
            }
        }

        private static void ApplyBaseColor(Material material, Color color)
        {
            string[] colorPropertyNames = { "_BaseColor", "_Color", "_TintColor", "_MaterialColor" };
            foreach (string propName in colorPropertyNames)
            {
                if (material.HasProperty(propName)) { material.SetColor(propName, color); return; }
            }

            Shader shader     = material.shader;
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.Color)
                {
                    material.SetColor(ShaderUtil.GetPropertyName(shader, i), color);
                    return;
                }
            }
        }

        // -------------------------------------------------------------------------
        // assign_material
        // -------------------------------------------------------------------------
        private static JObject AssignMaterial(JObject parameters)
        {
            int? instanceId    = parameters["instanceId"]?.ToObject<int?>();
            string objectPath  = parameters["objectPath"]?.ToObject<string>();
            string materialPath= parameters["materialPath"]?.ToObject<string>();
            int slot           = parameters["slot"]?.ToObject<int?>() ?? 0;

            if (!instanceId.HasValue && string.IsNullOrEmpty(objectPath))
                return JsonResponseFactory.Error("Either 'instanceId' or 'objectPath' must be provided", "validation_error");
            if (string.IsNullOrEmpty(materialPath))
                return JsonResponseFactory.Error("Required parameter 'materialPath' not provided", "validation_error");

            GameObject go = FindGameObject(instanceId, objectPath);
            if (go == null)
            {
                string id = instanceId.HasValue ? $"ID {instanceId.Value}" : $"path '{objectPath}'";
                return JsonResponseFactory.Error($"GameObject with {id} not found", "not_found_error");
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return JsonResponseFactory.Error($"GameObject '{go.name}' does not have a Renderer component", "component_error");

            Material material = LoadMaterial(materialPath);
            if (material == null)
                return JsonResponseFactory.Error($"Material at path '{materialPath}' not found", "not_found_error");

            Material[] materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length)
                return JsonResponseFactory.Error($"Material slot {slot} is out of range. GameObject has {materials.Length} material slot(s) (0-{materials.Length - 1})", "validation_error");

            Undo.RecordObject(renderer, $"Assign Material to {go.name}");
            materials[slot] = material;
            renderer.sharedMaterials = materials;

            EditorUtility.SetDirty(renderer);
            if (PrefabUtility.IsPartOfAnyPrefab(go))
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

            Debug.Log($"[ScalableMCP] Assigned material '{material.name}' to '{go.name}' at slot {slot}");

            return new JObject
            {
                ["success"]      = true,
                ["type"]         = "text",
                ["message"]      = $"Successfully assigned material '{material.name}' to '{go.name}' at slot {slot}",
                ["gameObjectName"] = go.name,
                ["materialName"] = material.name,
                ["slot"]         = slot
            };
        }

        // -------------------------------------------------------------------------
        // modify_material
        // -------------------------------------------------------------------------
        private static JObject ModifyMaterial(JObject parameters)
        {
            string materialPath = parameters["materialPath"]?.ToObject<string>();
            JObject properties  = parameters["properties"] as JObject;

            if (string.IsNullOrEmpty(materialPath))
                return JsonResponseFactory.Error("Required parameter 'materialPath' not provided", "validation_error");
            if (properties == null || properties.Count == 0)
                return JsonResponseFactory.Error("Required parameter 'properties' not provided or empty", "validation_error");

            Material material = LoadMaterial(materialPath);
            if (material == null)
                return JsonResponseFactory.Error($"Material at path '{materialPath}' not found", "not_found_error");

            Undo.RecordObject(material, $"Modify Material {material.name}");

            Shader shader         = material.shader;
            int propertyCount     = ShaderUtil.GetPropertyCount(shader);
            var modifiedProperties= new System.Collections.Generic.List<string>();
            var unknownProperties = new System.Collections.Generic.List<string>();

            foreach (var prop in properties.Properties())
            {
                bool found = false;
                for (int i = 0; i < propertyCount; i++)
                {
                    if (ShaderUtil.GetPropertyName(shader, i) == prop.Name)
                    {
                        found = true;
                        ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);
                        object value = ConvertPropertyValue(prop.Value, propType);
                        if (value != null)
                        {
                            SetMaterialProperty(material, prop.Name, propType, value);
                            modifiedProperties.Add(prop.Name);
                        }
                        break;
                    }
                }
                if (!found) unknownProperties.Add(prop.Name);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ScalableMCP] Modified material '{material.name}': {string.Join(", ", modifiedProperties)}");

            JObject result = new JObject
            {
                ["success"]            = true,
                ["type"]               = "text",
                ["message"]            = $"Successfully modified material '{material.name}'",
                ["materialName"]       = material.name,
                ["modifiedProperties"] = new JArray(modifiedProperties)
            };

            if (unknownProperties.Count > 0)
            {
                result["unknownProperties"] = new JArray(unknownProperties);
                result["message"] = $"Modified material '{material.name}'. Some properties were not found: {string.Join(", ", unknownProperties)}";
            }

            return result;
        }

        // -------------------------------------------------------------------------
        // get_material_info
        // -------------------------------------------------------------------------
        private static JObject GetMaterialInfo(JObject parameters)
        {
            string materialPath = parameters["materialPath"]?.ToObject<string>();
            if (string.IsNullOrEmpty(materialPath))
                return JsonResponseFactory.Error("Required parameter 'materialPath' not provided", "validation_error");

            Material material = LoadMaterial(materialPath);
            if (material == null)
                return JsonResponseFactory.Error($"Material at path '{materialPath}' not found", "not_found_error");

            Shader shader         = material.shader;
            int propertyCount     = ShaderUtil.GetPropertyCount(shader);

            JArray propertiesArray = new JArray();
            for (int i = 0; i < propertyCount; i++)
            {
                string propName        = ShaderUtil.GetPropertyName(shader, i);
                string propDescription = ShaderUtil.GetPropertyDescription(shader, i);
                ShaderUtil.ShaderPropertyType propType = ShaderUtil.GetPropertyType(shader, i);

                JObject propInfo = new JObject
                {
                    ["name"]        = propName,
                    ["description"] = propDescription,
                    ["type"]        = propType.ToString(),
                    ["value"]       = GetPropertyValue(material, propName, propType)
                };

                if (propType == ShaderUtil.ShaderPropertyType.Range)
                {
                    propInfo["rangeMin"] = ShaderUtil.GetRangeLimits(shader, i, 1);
                    propInfo["rangeMax"] = ShaderUtil.GetRangeLimits(shader, i, 2);
                }

                propertiesArray.Add(propInfo);
            }

            int renderQueue = material.renderQueue;
            string renderQueueName = "Custom";
            if      (renderQueue <= 2000) renderQueueName = "Background";
            else if (renderQueue <= 2450) renderQueueName = "Geometry";
            else if (renderQueue <= 2500) renderQueueName = "AlphaTest";
            else if (renderQueue <= 3000) renderQueueName = "Transparent";
            else                          renderQueueName = "Overlay";

            Debug.Log($"[ScalableMCP] Retrieved info for material '{material.name}'");

            return new JObject
            {
                ["success"]             = true,
                ["type"]                = "text",
                ["message"]             = $"Material info for '{material.name}'",
                ["materialName"]        = material.name,
                ["materialPath"]        = materialPath,
                ["shaderName"]          = shader.name,
                ["renderQueue"]         = renderQueue,
                ["renderQueueCategory"] = renderQueueName,
                ["enableInstancing"]    = material.enableInstancing,
                ["doubleSidedGI"]       = material.doubleSidedGI,
                ["passCount"]           = material.passCount,
                ["properties"]          = propertiesArray
            };
        }

        private static JToken GetPropertyValue(Material material, string propName, ShaderUtil.ShaderPropertyType propType)
        {
            switch (propType)
            {
                case ShaderUtil.ShaderPropertyType.Color:
                    Color c = material.GetColor(propName);
                    return new JObject { ["r"] = c.r, ["g"] = c.g, ["b"] = c.b, ["a"] = c.a };
                case ShaderUtil.ShaderPropertyType.Vector:
                    Vector4 v = material.GetVector(propName);
                    return new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z, ["w"] = v.w };
                case ShaderUtil.ShaderPropertyType.Float:
                case ShaderUtil.ShaderPropertyType.Range:
                    return material.GetFloat(propName);
                case ShaderUtil.ShaderPropertyType.TexEnv:
                    Texture tex = material.GetTexture(propName);
                    return tex != null ? AssetDatabase.GetAssetPath(tex) : null;
                case ShaderUtil.ShaderPropertyType.Int:
                    return material.GetInt(propName);
                default:
                    return null;
            }
        }
    }
}

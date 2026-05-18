using ScalableMCP.Editor;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers all GameObject-related tool handlers:
    /// add_asset_to_scene, get_gameobject, update_gameobject, select_gameobject,
    /// duplicate_gameobject, delete_gameobject, reparent_gameobject.
    /// </summary>
    public static class GameObjectHandler
    {
        public static void RegisterAll(Dictionary<string, IToolHandler> tools)
        {
            tools["add_asset_to_scene"]  = new DelegateToolHandler("add_asset_to_scene",  AddAssetToScene);
            tools["get_gameobject"]      = new DelegateToolHandler("get_gameobject",      GetGameObject);
            tools["update_gameobject"]   = new DelegateToolHandler("update_gameobject",   UpdateGameObject);
            tools["select_gameobject"]   = new DelegateToolHandler("select_gameobject",   SelectGameObject);
            tools["duplicate_gameobject"]= new DelegateToolHandler("duplicate_gameobject",DuplicateGameObject);
            tools["delete_gameobject"]   = new DelegateToolHandler("delete_gameobject",   DeleteGameObject);
            tools["reparent_gameobject"] = new DelegateToolHandler("reparent_gameobject", ReparentGameObject);
        }

        // -------------------------------------------------------------------------
        // add_asset_to_scene
        // -------------------------------------------------------------------------
        private static JObject AddAssetToScene(JObject parameters)
        {
            string assetPath  = parameters["assetPath"]?.ToObject<string>();
            string guid       = parameters["guid"]?.ToObject<string>();
            Vector3 position  = parameters["position"]?.ToObject<JObject>() != null
                ? new Vector3(
                    parameters["position"]["x"]?.ToObject<float>() ?? 0f,
                    parameters["position"]["y"]?.ToObject<float>() ?? 0f,
                    parameters["position"]["z"]?.ToObject<float>() ?? 0f)
                : Vector3.zero;

            string parentPath = parameters["parentPath"]?.ToObject<string>();
            int? parentId     = parameters["parentId"]?.ToObject<int?>();

            if (string.IsNullOrEmpty(assetPath) && string.IsNullOrEmpty(guid))
                return JsonResponseFactory.Error("Required parameter 'assetPath' or 'guid' not provided", "validation_error");

            if (string.IsNullOrEmpty(assetPath) && !string.IsNullOrEmpty(guid))
            {
                assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath))
                    return JsonResponseFactory.Error($"Asset with GUID '{guid}' not found", "not_found_error");
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return JsonResponseFactory.Error($"Failed to load asset at path '{assetPath}'", "not_found_error");

            bool isPrefab       = PrefabUtility.GetPrefabAssetType(asset) != PrefabAssetType.NotAPrefab;
            bool canInstantiate = asset is GameObject || isPrefab;
            if (!canInstantiate)
                return JsonResponseFactory.Error($"Asset of type '{asset.GetType().Name}' cannot be instantiated in the scene", "invalid_asset_type");

            GameObject instance = null;
            try
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                instance.transform.position = position;

                if (!string.IsNullOrEmpty(parentPath) || parentId.HasValue)
                {
                    GameObject parent = null;
                    if (parentId.HasValue)
                        parent = EditorUtility.InstanceIDToObject(parentId.Value) as GameObject;
                    else if (!string.IsNullOrEmpty(parentPath))
                        parent = GameObject.Find(parentPath);

                    if (parent != null)
                        instance.transform.SetParent(parent.transform, false);
                    else
                        Debug.LogWarning("[ScalableMCP] Parent object not found, asset will be created at the root of the scene");
                }

                Selection.activeGameObject = instance;
                EditorGUIUtility.PingObject(instance);
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error instantiating asset: {ex.Message}", "instantiation_error");
            }

            Debug.Log($"[ScalableMCP] Added asset '{asset.name}' to scene from path '{assetPath}'");

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = $"Successfully added asset '{asset.name}' with instance ID {instance.GetInstanceID()} to the scene",
                ["instanceId"] = instance.GetInstanceID()
            };
        }

        // -------------------------------------------------------------------------
        // get_gameobject
        // -------------------------------------------------------------------------
        private static JObject GetGameObject(JObject parameters)
        {
            if (parameters == null || !parameters.ContainsKey("idOrName"))
                return JsonResponseFactory.Error("Missing required parameter: idOrName", "validation_error");

            string idOrName = parameters["idOrName"]?.ToObject<string>();
            if (string.IsNullOrEmpty(idOrName))
                return JsonResponseFactory.Error("Parameter 'idOrName' cannot be null or empty", "validation_error");

            GameObject go = GameObjectUtils.FindByIdOrName(idOrName);
            if (go == null)
                return JsonResponseFactory.Error($"GameObject with '{idOrName}' reference not found. Make sure the GameObject exists and is loaded in the current scene(s).", "not_found_error");

            JObject gameObjectData = GameObjectResourceHelper.GameObjectToJObject(go, true);

            return new JObject
            {
                ["success"]    = true,
                ["message"]    = $"Retrieved GameObject data for '{go.name}'",
                ["gameObject"] = gameObjectData,
                ["instanceId"] = go.GetInstanceID()
            };
        }

        // -------------------------------------------------------------------------
        // update_gameobject
        // -------------------------------------------------------------------------
        private static JObject UpdateGameObject(JObject parameters)
        {
            int? instanceId      = parameters["instanceId"]?.ToObject<int?>();
            string objectPath    = parameters["objectPath"]?.ToObject<string>();
            JObject goData       = parameters["gameObjectData"] as JObject;

            string newName          = goData?["name"]?.ToObject<string>();
            string newTag           = goData?["tag"]?.ToObject<string>();
            int? newLayer           = goData?["layer"]?.ToObject<int?>();
            bool? newIsActiveSelf   = (goData?["activeSelf"] ?? goData?["isActiveSelf"])?.ToObject<bool?>();
            bool? newIsStatic       = (goData?["isStatic"] ?? goData?["static"])?.ToObject<bool?>();

            GameObject target = null;
            string identifierInfo;

            if (instanceId.HasValue)
            {
                target = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifierInfo = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                target = GameObjectUtils.FindOrCreate(objectPath);
                identifierInfo = $"path '{objectPath}'";
            }
            else
            {
                return JsonResponseFactory.Error("Either 'instanceId' or 'objectPath' must be provided.", "validation_error");
            }

            if (target == null)
                return JsonResponseFactory.Error($"Target GameObject could not be identified or created using {identifierInfo}.", "unknown_error");

            Undo.RecordObject(target, "Update GameObject Properties");
            bool updated = false;
            string originalName = target.name;

            if (!string.IsNullOrEmpty(newName) && target.name != newName)
            {
                target.name = newName;
                updated = true;
            }

            if (!string.IsNullOrEmpty(newTag))
            {
                bool tagExists = Array.Exists(UnityEditorInternal.InternalEditorUtility.tags, t => t.Equals(newTag));
                if (!tagExists)
                    Debug.LogWarning($"[ScalableMCP] Tag '{newTag}' does not exist for GameObject '{originalName}'. Tag not changed.");
                else if (!target.CompareTag(newTag))
                {
                    target.tag = newTag;
                    updated = true;
                }
            }

            if (newLayer.HasValue)
            {
                if (newLayer.Value < 0 || newLayer.Value > 31)
                    Debug.LogWarning($"[ScalableMCP] Invalid layer value {newLayer.Value} for GameObject '{originalName}'. Layer not changed.");
                else if (target.layer != newLayer.Value)
                {
                    target.layer = newLayer.Value;
                    updated = true;
                }
            }

            if (newIsActiveSelf.HasValue && target.activeSelf != newIsActiveSelf.Value)
            {
                target.SetActive(newIsActiveSelf.Value);
                updated = true;
            }

            if (newIsStatic.HasValue && target.isStatic != newIsStatic.Value)
            {
                target.isStatic = newIsStatic.Value;
                updated = true;
            }

            if (updated)
                EditorUtility.SetDirty(target);

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = updated
                    ? $"GameObject '{target.name}' (identified by {identifierInfo}) updated successfully."
                    : $"No properties were changed for GameObject '{target.name}' (identified by {identifierInfo}).",
                ["instanceId"] = target.GetInstanceID(),
                ["name"]       = target.name,
                ["path"]       = GameObjectUtils.GetPath(target)
            };
        }

        // -------------------------------------------------------------------------
        // select_gameobject
        // -------------------------------------------------------------------------
        private static JObject SelectGameObject(JObject parameters)
        {
            string objectPath  = parameters["objectPath"]?.ToObject<string>();
            string objectName  = parameters["objectName"]?.ToObject<string>();
            int? instanceId    = parameters["instanceId"]?.ToObject<int?>();

            if (string.IsNullOrEmpty(objectPath) && string.IsNullOrEmpty(objectName) && !instanceId.HasValue)
                return JsonResponseFactory.Error("Required parameter 'objectPath', 'objectName' or 'instanceId' not provided", "validation_error");

            GameObject selected = null;
            if (instanceId.HasValue)
                selected = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
            else if (!string.IsNullOrEmpty(objectPath))
                selected = GameObject.Find(objectPath);
            else
                selected = GameObject.Find(objectName);

            Selection.activeGameObject = selected;
            EditorGUIUtility.PingObject(selected);

            Debug.Log($"[ScalableMCP] Selected GameObject: {selected?.name}");

            return new JObject
            {
                ["success"] = true,
                ["type"]    = "text",
                ["message"] = $"Successfully selected GameObject {selected?.name}"
            };
        }

        // -------------------------------------------------------------------------
        // duplicate_gameobject
        // -------------------------------------------------------------------------
        private static JObject DuplicateGameObject(JObject parameters)
        {
            int? instanceId     = parameters["instanceId"]?.ToObject<int?>();
            string objectPath   = parameters["objectPath"]?.ToObject<string>();
            string newName      = parameters["newName"]?.ToObject<string>();
            string newParentPath= parameters["newParent"]?.ToObject<string>();
            int? newParentId    = parameters["newParentId"]?.ToObject<int?>();
            int count           = parameters["count"]?.ToObject<int?>() ?? 1;

            if (count < 1 || count > 100)
                return JsonResponseFactory.Error("Count must be between 1 and 100.", "validation_error");

            JObject error = GameObjectUtils.FindGameObject(instanceId, objectPath, out GameObject sourceObject, out string identifierInfo);
            if (error != null) return error;

            GameObject newParent = null;
            if (newParentId.HasValue)
            {
                newParent = EditorUtility.InstanceIDToObject(newParentId.Value) as GameObject;
                if (newParent == null)
                    return JsonResponseFactory.Error($"New parent GameObject not found with instance ID {newParentId.Value}.", "not_found_error");
            }
            else if (!string.IsNullOrEmpty(newParentPath))
            {
                newParent = GameObject.Find(newParentPath);
                if (newParent == null)
                    return JsonResponseFactory.Error($"New parent GameObject not found at path '{newParentPath}'.", "not_found_error");
            }

            JArray duplicated = new JArray();
            for (int i = 0; i < count; i++)
            {
                GameObject dup = UnityEngine.Object.Instantiate(sourceObject);
                Undo.RegisterCreatedObjectUndo(dup, $"Duplicate {sourceObject.name}");

                if (!string.IsNullOrEmpty(newName))
                    dup.name = count > 1 ? $"{newName} ({i + 1})" : newName;
                else
                    dup.name = count > 1 ? $"{sourceObject.name} ({i + 1})" : sourceObject.name;

                Transform targetParent = newParent != null ? newParent.transform : sourceObject.transform.parent;
                if (targetParent != null)
                    dup.transform.SetParent(targetParent, true);

                duplicated.Add(new JObject
                {
                    ["instanceId"] = dup.GetInstanceID(),
                    ["name"]       = dup.name,
                    ["path"]       = GameObjectUtils.GetPath(dup)
                });
            }

            EditorUtility.SetDirty(sourceObject.scene.GetRootGameObjects()[0]);

            return new JObject
            {
                ["success"]           = true,
                ["type"]              = "text",
                ["message"]           = count == 1
                    ? $"Successfully duplicated GameObject '{sourceObject.name}'."
                    : $"Successfully created {count} duplicates of GameObject '{sourceObject.name}'.",
                ["duplicatedObjects"] = duplicated
            };
        }

        // -------------------------------------------------------------------------
        // delete_gameobject
        // -------------------------------------------------------------------------
        private static JObject DeleteGameObject(JObject parameters)
        {
            int? instanceId       = parameters["instanceId"]?.ToObject<int?>();
            string objectPath     = parameters["objectPath"]?.ToObject<string>();
            bool includeChildren  = parameters["includeChildren"]?.ToObject<bool?>() ?? true;

            JObject error = GameObjectUtils.FindGameObject(instanceId, objectPath, out GameObject target, out string identifierInfo);
            if (error != null) return error;

            string deletedName  = target.name;
            string deletedPath  = GameObjectUtils.GetPath(target);
            int childCount      = target.transform.childCount;

            if (!includeChildren && childCount > 0)
            {
                Transform parent = target.transform.parent;
                Transform[] children = new Transform[childCount];
                for (int i = 0; i < childCount; i++) children[i] = target.transform.GetChild(i);
                foreach (Transform child in children)
                    Undo.SetTransformParent(child, parent, "Reparent before delete");
            }

            Undo.DestroyObjectImmediate(target);

            return new JObject
            {
                ["success"]           = true,
                ["type"]              = "text",
                ["message"]           = includeChildren && childCount > 0
                    ? $"Successfully deleted GameObject '{deletedName}' and {childCount} children."
                    : $"Successfully deleted GameObject '{deletedName}'.",
                ["deletedPath"]       = deletedPath,
                ["childrenPreserved"] = !includeChildren && childCount > 0 ? childCount : 0
            };
        }

        // -------------------------------------------------------------------------
        // reparent_gameobject
        // -------------------------------------------------------------------------
        private static JObject ReparentGameObject(JObject parameters)
        {
            int? instanceId          = parameters["instanceId"]?.ToObject<int?>();
            string objectPath        = parameters["objectPath"]?.ToObject<string>();
            string newParentPath     = parameters["newParent"]?.ToObject<string>();
            int? newParentId         = parameters["newParentId"]?.ToObject<int?>();
            bool worldPositionStays  = parameters["worldPositionStays"]?.ToObject<bool?>() ?? true;

            JObject error = GameObjectUtils.FindGameObject(instanceId, objectPath, out GameObject target, out string identifierInfo);
            if (error != null) return error;

            string oldPath      = GameObjectUtils.GetPath(target);
            Transform oldParent = target.transform.parent;

            Transform newParentTransform = null;
            bool moveToRoot = false;

            if (parameters["newParent"] != null && parameters["newParent"].Type == JTokenType.Null)
            {
                moveToRoot = true;
            }
            else if (newParentId.HasValue)
            {
                GameObject newParent = EditorUtility.InstanceIDToObject(newParentId.Value) as GameObject;
                if (newParent == null)
                    return JsonResponseFactory.Error($"New parent GameObject not found with instance ID {newParentId.Value}.", "not_found_error");
                newParentTransform = newParent.transform;
            }
            else if (!string.IsNullOrEmpty(newParentPath))
            {
                GameObject newParent = GameObject.Find(newParentPath);
                if (newParent == null)
                    return JsonResponseFactory.Error($"New parent GameObject not found at path '{newParentPath}'.", "not_found_error");
                newParentTransform = newParent.transform;
            }
            else if (parameters["newParent"] == null && parameters["newParentId"] == null)
            {
                moveToRoot = true;
            }

            if (newParentTransform != null)
            {
                if (newParentTransform == target.transform)
                    return JsonResponseFactory.Error("Cannot parent a GameObject to itself.", "validation_error");
                if (newParentTransform.IsChildOf(target.transform))
                    return JsonResponseFactory.Error("Cannot parent a GameObject to one of its descendants.", "validation_error");
            }

            if (moveToRoot && oldParent == null)
            {
                return new JObject
                {
                    ["success"]    = true, ["type"] = "text",
                    ["message"]    = $"GameObject '{target.name}' is already at the root level.",
                    ["instanceId"] = target.GetInstanceID(), ["name"] = target.name,
                    ["path"]       = oldPath, ["changed"] = false
                };
            }

            if (!moveToRoot && newParentTransform == oldParent)
            {
                return new JObject
                {
                    ["success"]    = true, ["type"] = "text",
                    ["message"]    = $"GameObject '{target.name}' is already a child of the specified parent.",
                    ["instanceId"] = target.GetInstanceID(), ["name"] = target.name,
                    ["path"]       = oldPath, ["changed"] = false
                };
            }

            Undo.SetTransformParent(target.transform, newParentTransform, "Reparent GameObject");

            if (!worldPositionStays)
            {
                Undo.RecordObject(target.transform, "Reset Local Position");
                target.transform.localPosition = Vector3.zero;
                target.transform.localRotation = Quaternion.identity;
                target.transform.localScale    = Vector3.one;
            }

            string newPath           = GameObjectUtils.GetPath(target);
            string parentDescription = newParentTransform != null
                ? $"'{newParentTransform.gameObject.name}'"
                : "root level";

            EditorUtility.SetDirty(target);

            return new JObject
            {
                ["success"]    = true, ["type"] = "text",
                ["message"]    = $"Successfully reparented GameObject '{target.name}' to {parentDescription}.",
                ["instanceId"] = target.GetInstanceID(),
                ["name"]       = target.name,
                ["oldPath"]    = oldPath,
                ["newPath"]    = newPath,
                ["changed"]    = true
            };
        }
    }
}

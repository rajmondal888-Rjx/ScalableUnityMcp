using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor
{
    /// <summary>
    /// Static utility helpers for finding and describing GameObjects.
    /// Extracted from common patterns used across multiple tool handlers.
    /// </summary>
    public static class GameObjectUtils
    {
        /// <summary>
        /// Find a GameObject by instance ID or hierarchy path.
        /// Returns an error JObject if not found, or null on success (out parameters are filled).
        /// </summary>
        /// <param name="instanceId">Optional Unity instance ID</param>
        /// <param name="objectPath">Optional hierarchy path (e.g. "Parent/Child/Target")</param>
        /// <param name="gameObject">The found GameObject, or null if not found</param>
        /// <param name="identifierInfo">Human-readable description of how the object was identified</param>
        /// <returns>Error JObject if not found or parameters invalid; null on success</returns>
        public static JObject FindGameObject(int? instanceId, string objectPath, out GameObject gameObject, out string identifierInfo)
        {
            gameObject = null;
            identifierInfo = "";

            if (instanceId.HasValue)
            {
                gameObject = EditorUtility.InstanceIDToObject(instanceId.Value) as GameObject;
                identifierInfo = $"instance ID {instanceId.Value}";
            }
            else if (!string.IsNullOrEmpty(objectPath))
            {
                gameObject = GameObject.Find(objectPath);
                if (gameObject == null)
                {
                    gameObject = FindByPath(objectPath);
                }
                identifierInfo = $"path '{objectPath}'";
            }
            else
            {
                return JsonResponseFactory.Error(
                    "Either 'instanceId' or 'objectPath' must be provided.",
                    "validation_error"
                );
            }

            if (gameObject == null)
            {
                return JsonResponseFactory.Error(
                    $"GameObject not found using {identifierInfo}.",
                    "not_found_error"
                );
            }

            return null; // success
        }

        /// <summary>
        /// Find a GameObject by hierarchy path, traversing root objects in the active scene.
        /// Strips a leading '/' if present.
        /// </summary>
        /// <param name="path">Hierarchy path such as "Root/Child/Target"</param>
        /// <returns>The GameObject if found; null otherwise</returns>
        public static GameObject FindByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            path = path.TrimStart('/');
            string[] parts = path.Split('/');
            if (parts.Length == 0) return null;

            // Find root object in active scene
            GameObject current = null;
            GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (var root in rootObjects)
            {
                if (root.name == parts[0])
                {
                    current = root;
                    break;
                }
            }

            if (current == null) return null;

            // Traverse children
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.transform.Find(parts[i]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        /// <summary>
        /// Find a GameObject by either numeric string instance ID or name/path string.
        /// Mirrors the pattern used in GetGameObjectTool and GetGameObjectResource.
        /// </summary>
        /// <param name="idOrName">Instance ID as string, name, or hierarchy path</param>
        /// <returns>The GameObject if found; null otherwise</returns>
        public static GameObject FindByIdOrName(string idOrName)
        {
            if (string.IsNullOrEmpty(idOrName)) return null;

            if (int.TryParse(idOrName, out int instanceId))
            {
                return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            }

            return GameObject.Find(idOrName);
        }

        /// <summary>
        /// Returns the full hierarchy path of a GameObject, starting with '/'.
        /// Example: "/Root/Parent/Child"
        /// </summary>
        public static string GetPath(GameObject obj)
        {
            if (obj == null) return null;
            string path = "/" + obj.name;
            while (obj.transform.parent != null)
            {
                obj = obj.transform.parent.gameObject;
                path = "/" + obj.name + path;
            }
            return path;
        }

        /// <summary>
        /// Find or create a hierarchy of GameObjects along the given path.
        /// Intermediate nodes that do not exist are created and registered for undo.
        /// </summary>
        public static GameObject FindOrCreate(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new System.ArgumentException("Path cannot be null or empty.", nameof(path));

            path = path.Trim('/');
            string[] parts = path.Split('/');

            GameObject currentParent = null;
            GameObject result = null;

            for (int i = 0; i < parts.Length; i++)
            {
                string segmentName = parts[i];
                if (string.IsNullOrEmpty(segmentName))
                    throw new System.ArgumentException($"Empty path segment at index {i} in '{path}'.", nameof(path));

                Transform childTransform;
                if (currentParent == null)
                {
                    var rootObj = GameObject.Find(segmentName);
                    childTransform = rootObj?.transform;
                }
                else
                {
                    childTransform = currentParent.transform.Find(segmentName);
                }

                if (childTransform == null)
                {
                    var newObj = new GameObject(segmentName);
                    Undo.RegisterCreatedObjectUndo(newObj, $"Create {segmentName}");
                    if (currentParent != null)
                        newObj.transform.SetParent(currentParent.transform, false);
                    result = newObj;
                    currentParent = newObj;
                }
                else
                {
                    result = childTransform.gameObject;
                    currentParent = result;
                }
            }

            return result;
        }
    }
}

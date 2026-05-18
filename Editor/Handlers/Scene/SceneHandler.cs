using ScalableMCP.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers all scene-related tool handlers: create_scene, load_scene, save_scene,
    /// delete_scene, unload_scene, get_scene_info.
    /// </summary>
    public static class SceneHandler
    {
        public static void RegisterAll(Dictionary<string, IToolHandler> tools)
        {
            tools["create_scene"]   = new DelegateToolHandler("create_scene",   CreateScene);
            tools["load_scene"]     = new DelegateToolHandler("load_scene",     LoadScene);
            tools["save_scene"]     = new DelegateToolHandler("save_scene",     SaveScene);
            tools["delete_scene"]   = new DelegateToolHandler("delete_scene",   DeleteScene);
            tools["unload_scene"]   = new DelegateToolHandler("unload_scene",   UnloadScene);
            tools["get_scene_info"] = new DelegateToolHandler("get_scene_info", GetSceneInfo);
        }

        // -------------------------------------------------------------------------
        // create_scene
        // -------------------------------------------------------------------------
        private static JObject CreateScene(JObject parameters)
        {
            string sceneName      = parameters["sceneName"]?.ToObject<string>();
            string folderPath     = parameters["folderPath"]?.ToObject<string>();
            bool addToBuild       = parameters["addToBuildSettings"]?.ToObject<bool?>() ?? false;
            bool makeActive       = parameters["makeActive"]?.ToObject<bool?>() ?? true;

            if (string.IsNullOrEmpty(sceneName))
                return JsonResponseFactory.Error("Required parameter 'sceneName' not provided", "validation_error");

            if (string.IsNullOrEmpty(folderPath))
                folderPath = "Assets";

            // Ensure folder exists
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                string current = "Assets";
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i == 0 && parts[i] == "Assets") continue;
                    string next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(current, parts[i]);
                    current = next;
                }
            }

            string basePath  = folderPath.TrimEnd('/');
            string scenePath = AssetDatabase.GenerateUniqueAssetPath($"{basePath}/{sceneName}.unity");

            try
            {
                var newScene = EditorSceneManager.NewScene(
                    NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

                bool saved = EditorSceneManager.SaveScene(newScene, scenePath);
                if (!saved)
                    return JsonResponseFactory.Error($"Failed to save scene at '{scenePath}'", "save_error");

                AssetDatabase.Refresh();

                if (makeActive)
                    EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

                if (addToBuild)
                    AddSceneToBuildSettings(scenePath);

                Debug.Log($"[ScalableMCP] Created scene '{sceneName}' at path '{scenePath}'");

                return new JObject
                {
                    ["success"]   = true,
                    ["type"]      = "text",
                    ["message"]   = $"Successfully created scene '{sceneName}' at path '{scenePath}'",
                    ["scenePath"] = scenePath
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error creating scene: {ex.Message}", "scene_creation_error");
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == scenePath) return;

            var newList = new EditorBuildSettingsScene[scenes.Length + 1];
            for (int i = 0; i < scenes.Length; i++) newList[i] = scenes[i];
            newList[newList.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = newList;
        }

        // -------------------------------------------------------------------------
        // load_scene
        // -------------------------------------------------------------------------
        private static JObject LoadScene(JObject parameters)
        {
            string scenePath  = parameters["scenePath"]?.ToObject<string>();
            string sceneName  = parameters["sceneName"]?.ToObject<string>();
            string folderPath = parameters["folderPath"]?.ToObject<string>();
            bool additive     = parameters["additive"]?.ToObject<bool?>() ?? false;

            if (string.IsNullOrEmpty(scenePath))
            {
                if (string.IsNullOrEmpty(sceneName))
                    return JsonResponseFactory.Error("Provide either 'scenePath' or 'sceneName'", "validation_error");

                string filter = $"{sceneName} t:Scene";
                string[] searchInFolders = null;
                if (!string.IsNullOrEmpty(folderPath))
                {
                    if (!AssetDatabase.IsValidFolder(folderPath))
                        return JsonResponseFactory.Error($"Folder '{folderPath}' does not exist", "not_found_error");
                    searchInFolders = new[] { folderPath };
                }

                var guids = AssetDatabase.FindAssets(filter, searchInFolders);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    {
                        scenePath = path;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(scenePath))
                    return JsonResponseFactory.Error($"Scene named '{sceneName}' not found", "not_found_error");
            }

            try
            {
                if (!additive)
                    EditorSceneManager.SaveOpenScenes();

                var mode = additive ? OpenSceneMode.Additive : OpenSceneMode.Single;
                EditorSceneManager.OpenScene(scenePath, mode);

                Debug.Log($"[ScalableMCP] Loaded scene at path '{scenePath}' (additive={additive})");

                return new JObject
                {
                    ["success"]   = true,
                    ["type"]      = "text",
                    ["message"]   = $"Successfully loaded scene at path '{scenePath}' (additive={additive.ToString().ToLower()})",
                    ["scenePath"] = scenePath,
                    ["additive"]  = additive
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error loading scene: {ex.Message}", "scene_load_error");
            }
        }

        // -------------------------------------------------------------------------
        // save_scene
        // -------------------------------------------------------------------------
        private static JObject SaveScene(JObject parameters)
        {
            string scenePath = parameters["scenePath"]?.ToObject<string>();
            bool saveAs      = parameters["saveAs"]?.ToObject<bool?>() ?? false;

            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return JsonResponseFactory.Error("No valid active scene to save", "validation_error");

                string targetPath;

                if (saveAs || !string.IsNullOrEmpty(scenePath))
                {
                    if (string.IsNullOrEmpty(scenePath))
                        return JsonResponseFactory.Error("Parameter 'scenePath' is required when 'saveAs' is true", "validation_error");

                    if (!scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                        scenePath += ".unity";
                    if (!scenePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        scenePath = "Assets/" + scenePath;

                    string directory = System.IO.Path.GetDirectoryName(scenePath);
                    if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
                        CreateFolderHierarchy(directory);

                    targetPath = scenePath;
                }
                else
                {
                    targetPath = activeScene.path;
                    if (string.IsNullOrEmpty(targetPath))
                        return JsonResponseFactory.Error("Scene has no path. Use 'scenePath' parameter to specify where to save the scene", "validation_error");
                }

                bool saved = EditorSceneManager.SaveScene(activeScene, targetPath);
                if (!saved)
                    return JsonResponseFactory.Error($"Failed to save scene to '{targetPath}'", "save_error");

                AssetDatabase.Refresh();

                Debug.Log($"[ScalableMCP] Saved scene '{activeScene.name}' to path '{targetPath}'");

                return new JObject
                {
                    ["success"]   = true,
                    ["type"]      = "text",
                    ["message"]   = $"Successfully saved scene '{activeScene.name}' to '{targetPath}'",
                    ["scenePath"] = targetPath,
                    ["sceneName"] = activeScene.name
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error saving scene: {ex.Message}", "scene_save_error");
            }
        }

        private static void CreateFolderHierarchy(string folderPath)
        {
            string[] parts = folderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = "Assets";
            for (int i = 0; i < parts.Length; i++)
            {
                if (i == 0 && parts[i] == "Assets") continue;
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        // -------------------------------------------------------------------------
        // delete_scene
        // -------------------------------------------------------------------------
        private static JObject DeleteScene(JObject parameters)
        {
            string scenePath  = parameters["scenePath"]?.ToObject<string>();
            string sceneName  = parameters["sceneName"]?.ToObject<string>();
            string folderPath = parameters["folderPath"]?.ToObject<string>();

            if (string.IsNullOrEmpty(scenePath))
            {
                if (string.IsNullOrEmpty(sceneName))
                    return JsonResponseFactory.Error("Provide either 'scenePath' or 'sceneName'", "validation_error");

                string filter = $"{sceneName} t:Scene";
                string[] searchInFolders = null;
                if (!string.IsNullOrEmpty(folderPath))
                {
                    if (!AssetDatabase.IsValidFolder(folderPath))
                        return JsonResponseFactory.Error($"Folder '{folderPath}' does not exist", "not_found_error");
                    searchInFolders = new[] { folderPath };
                }

                var guids = AssetDatabase.FindAssets(filter, searchInFolders);
                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
                    {
                        scenePath = path;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(scenePath))
                    return JsonResponseFactory.Error($"Scene named '{sceneName}' not found", "not_found_error");
            }

            try
            {
                var scene = EditorSceneManager.GetSceneByPath(scenePath);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);

                RemoveSceneFromBuildSettings(scenePath);

                bool deleted = AssetDatabase.DeleteAsset(scenePath);
                AssetDatabase.Refresh();

                if (!deleted)
                    return JsonResponseFactory.Error($"Failed to delete scene at '{scenePath}'", "delete_error");

                Debug.Log($"[ScalableMCP] Deleted scene at path '{scenePath}' and removed from Build Settings");

                return new JObject
                {
                    ["success"]   = true,
                    ["type"]      = "text",
                    ["message"]   = $"Successfully deleted scene at path '{scenePath}' and removed from Build Settings",
                    ["scenePath"] = scenePath
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error deleting scene: {ex.Message}", "scene_delete_error");
            }
        }

        private static void RemoveSceneFromBuildSettings(string scenePath)
        {
            var scenes   = EditorBuildSettings.scenes;
            var filtered = scenes.Where(s => s.path != scenePath).ToArray();
            if (filtered.Length != scenes.Length)
                EditorBuildSettings.scenes = filtered;
        }

        // -------------------------------------------------------------------------
        // unload_scene
        // -------------------------------------------------------------------------
        private static JObject UnloadScene(JObject parameters)
        {
            string scenePath = parameters["scenePath"]?.ToObject<string>();
            string sceneName = parameters["sceneName"]?.ToObject<string>();
            bool removeScene = parameters["removeScene"]?.ToObject<bool?>() ?? true;

            if (string.IsNullOrEmpty(scenePath) && string.IsNullOrEmpty(sceneName))
                return JsonResponseFactory.Error("Provide either 'scenePath' or 'sceneName'", "validation_error");

            try
            {
                Scene sceneToUnload = !string.IsNullOrEmpty(scenePath)
                    ? SceneManager.GetSceneByPath(scenePath)
                    : SceneManager.GetSceneByName(sceneName);

                if (!sceneToUnload.IsValid())
                {
                    string identifier = !string.IsNullOrEmpty(scenePath) ? $"path '{scenePath}'" : $"name '{sceneName}'";
                    return JsonResponseFactory.Error($"Scene with {identifier} is not currently loaded", "not_found_error");
                }

                if (SceneManager.sceneCount <= 1)
                    return JsonResponseFactory.Error("Cannot unload the only loaded scene. Load another scene first or create a new scene", "validation_error");

                string unloadedSceneName = sceneToUnload.name;
                string unloadedScenePath = sceneToUnload.path;
                bool wasDirty = sceneToUnload.isDirty;

                if (wasDirty)
                {
                    bool savePrompt = parameters["saveIfDirty"]?.ToObject<bool?>() ?? true;
                    if (savePrompt && !string.IsNullOrEmpty(unloadedScenePath))
                        EditorSceneManager.SaveScene(sceneToUnload);
                }

                bool success = EditorSceneManager.CloseScene(sceneToUnload, removeScene);
                if (!success)
                    return JsonResponseFactory.Error($"Failed to unload scene '{unloadedSceneName}'", "unload_error");

                Debug.Log($"[ScalableMCP] Unloaded scene '{unloadedSceneName}' (path: '{unloadedScenePath}')");

                return new JObject
                {
                    ["success"]   = true,
                    ["type"]      = "text",
                    ["message"]   = $"Successfully unloaded scene '{unloadedSceneName}'",
                    ["sceneName"] = unloadedSceneName,
                    ["scenePath"] = unloadedScenePath,
                    ["wasDirty"]  = wasDirty,
                    ["removed"]   = removeScene
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error unloading scene: {ex.Message}", "scene_unload_error");
            }
        }

        // -------------------------------------------------------------------------
        // get_scene_info
        // -------------------------------------------------------------------------
        private static JObject GetSceneInfo(JObject parameters)
        {
            try
            {
                Scene activeScene = SceneManager.GetActiveScene();
                if (!activeScene.IsValid())
                    return JsonResponseFactory.Error("No valid active scene", "validation_error");

                int loadedSceneCount = SceneManager.sceneCount;
                var loadedScenes = new JArray();

                for (int i = 0; i < loadedSceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    loadedScenes.Add(new JObject
                    {
                        ["name"]        = scene.name,
                        ["path"]        = scene.path,
                        ["buildIndex"]  = scene.buildIndex,
                        ["isLoaded"]    = scene.isLoaded,
                        ["isDirty"]     = scene.isDirty,
                        ["rootCount"]   = scene.isLoaded ? scene.rootCount : 0,
                        ["isActive"]    = scene == activeScene
                    });
                }

                Debug.Log($"[ScalableMCP] Retrieved scene info for active scene '{activeScene.name}'");

                return new JObject
                {
                    ["success"] = true,
                    ["type"]    = "text",
                    ["message"] = $"Active scene: '{activeScene.name}'",
                    ["activeScene"] = new JObject
                    {
                        ["name"]       = activeScene.name,
                        ["path"]       = activeScene.path,
                        ["buildIndex"] = activeScene.buildIndex,
                        ["isDirty"]    = activeScene.isDirty,
                        ["isLoaded"]   = activeScene.isLoaded,
                        ["rootCount"]  = activeScene.isLoaded ? activeScene.rootCount : 0
                    },
                    ["loadedSceneCount"] = loadedSceneCount,
                    ["loadedScenes"]     = loadedScenes
                };
            }
            catch (Exception ex)
            {
                return JsonResponseFactory.Error($"Error getting scene info: {ex.Message}", "scene_info_error");
            }
        }
    }
}

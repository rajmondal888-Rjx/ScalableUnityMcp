using ScalableMCP.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler that returns the full scene hierarchy of loaded scenes.
    /// URI: unity://scenes_hierarchy
    /// </summary>
    public class HierarchyResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_scenes_hierarchy";
        public override string Uri  => "unity://scenes_hierarchy";

        public override JObject Fetch(JObject parameters)
        {
            JArray hierarchyArray = GetSceneHierarchy();

            return new JObject
            {
                ["success"]   = true,
                ["message"]   = $"Retrieved hierarchy with {hierarchyArray.Count} root objects",
                ["hierarchy"] = hierarchyArray
            };
        }

        private static JArray GetSceneHierarchy()
        {
            JArray rootObjectsArray = new JArray();
            int sceneCount = SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                JObject sceneObject = new JObject
                {
                    ["name"]        = scene.name,
                    ["path"]        = scene.path,
                    ["buildIndex"]  = scene.buildIndex,
                    ["isDirty"]     = scene.isDirty,
                    ["rootObjects"] = new JArray()
                };

                GameObject[] rootObjects = scene.GetRootGameObjects();
                JArray rootObjectsInScene = (JArray)sceneObject["rootObjects"];

                foreach (GameObject rootObject in rootObjects)
                {
                    rootObjectsInScene.Add(GameObjectResourceHelper.GameObjectToJObject(rootObject, false));
                }

                rootObjectsArray.Add(sceneObject);
            }

            return rootObjectsArray;
        }
    }
}

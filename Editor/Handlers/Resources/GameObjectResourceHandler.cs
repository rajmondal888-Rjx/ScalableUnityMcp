using ScalableMCP.Editor;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler for detailed GameObject information.
    /// URI: unity://gameobject/{idOrName}
    /// </summary>
    public class GameObjectResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_gameobject";
        public override string Uri  => "unity://gameobject/{idOrName}";

        public override JObject Fetch(JObject parameters)
        {
            if (parameters == null || !parameters.ContainsKey("idOrName"))
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Missing required parameter: idOrName"
                };
            }

            string idOrName = parameters["idOrName"]?.ToObject<string>();
            if (string.IsNullOrEmpty(idOrName))
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = "Parameter 'objectPathId' cannot be null or empty"
                };
            }

            GameObject gameObject = null;

            if (int.TryParse(idOrName, out int instanceId))
            {
                UnityEngine.Object unityObject = EditorUtility.InstanceIDToObject(instanceId);
                gameObject = unityObject as GameObject;
            }
            else
            {
                gameObject = GameObject.Find(idOrName);
            }

            if (gameObject == null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["message"] = $"GameObject with '{idOrName}' reference not found. Make sure the GameObject exists and is loaded in the current scene(s)."
                };
            }

            JObject gameObjectData = GameObjectResourceHelper.GameObjectToJObject(gameObject, true);

            return new JObject
            {
                ["success"]    = true,
                ["message"]    = $"Retrieved GameObject data for '{gameObject.name}'",
                ["gameObject"] = gameObjectData,
                ["instanceId"] = gameObject.GetInstanceID()
            };
        }
    }
}

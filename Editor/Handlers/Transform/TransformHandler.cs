using ScalableMCP.Editor;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Registers all transform-related tool handlers:
    /// move_gameobject, rotate_gameobject, scale_gameobject, set_transform.
    /// </summary>
    public static class TransformHandler
    {
        public static void RegisterAll(Dictionary<string, IToolHandler> tools)
        {
            tools["move_gameobject"]   = new DelegateToolHandler("move_gameobject",   MoveGameObject);
            tools["rotate_gameobject"] = new DelegateToolHandler("rotate_gameobject", RotateGameObject);
            tools["scale_gameobject"]  = new DelegateToolHandler("scale_gameobject",  ScaleGameObject);
            tools["set_transform"]     = new DelegateToolHandler("set_transform",     SetTransform);
        }

        // -------------------------------------------------------------------------
        // Shared helper: find a GameObject by instanceId or objectPath from parameters
        // -------------------------------------------------------------------------
        private struct FindResult
        {
            public GameObject GameObject;
            public JObject Error;
        }

        private static FindResult FindGameObject(JObject parameters)
        {
            int? instanceId  = parameters["instanceId"]?.ToObject<int?>();
            string objectPath = parameters["objectPath"]?.ToObject<string>();

            JObject err = GameObjectUtils.FindGameObject(instanceId, objectPath, out GameObject go, out string _);
            return new FindResult { GameObject = go, Error = err };
        }

        // -------------------------------------------------------------------------
        // move_gameobject
        // -------------------------------------------------------------------------
        private static JObject MoveGameObject(JObject parameters)
        {
            var found = FindGameObject(parameters);
            if (found.Error != null) return found.Error;

            GameObject go        = found.GameObject;
            Transform transform  = go.transform;

            JObject positionObj = parameters["position"] as JObject;
            if (positionObj == null)
                return JsonResponseFactory.Error("Required parameter 'position' not provided", "validation_error");

            Vector3 position = new Vector3(
                positionObj["x"]?.ToObject<float>() ?? 0f,
                positionObj["y"]?.ToObject<float>() ?? 0f,
                positionObj["z"]?.ToObject<float>() ?? 0f
            );

            string space = parameters["space"]?.ToObject<string>() ?? "world";
            bool relative = parameters["relative"]?.ToObject<bool>() ?? false;

            Undo.RecordObject(transform, "Move GameObject");

            if (space.ToLower() == "local")
            {
                if (relative) transform.localPosition += position;
                else          transform.localPosition  = position;
            }
            else
            {
                if (relative) transform.position += position;
                else          transform.position  = position;
            }

            EditorUtility.SetDirty(go);

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = $"GameObject '{go.name}' moved successfully.",
                ["instanceId"] = go.GetInstanceID(),
                ["name"]       = go.name,
                ["path"]       = GameObjectUtils.GetPath(go),
                ["position"]   = new JObject
                {
                    ["world"] = new JObject { ["x"] = transform.position.x, ["y"] = transform.position.y, ["z"] = transform.position.z },
                    ["local"] = new JObject { ["x"] = transform.localPosition.x, ["y"] = transform.localPosition.y, ["z"] = transform.localPosition.z }
                }
            };
        }

        // -------------------------------------------------------------------------
        // rotate_gameobject
        // -------------------------------------------------------------------------
        private static JObject RotateGameObject(JObject parameters)
        {
            var found = FindGameObject(parameters);
            if (found.Error != null) return found.Error;

            GameObject go        = found.GameObject;
            Transform transform  = go.transform;

            JObject rotationObj = parameters["rotation"] as JObject;
            if (rotationObj == null)
                return JsonResponseFactory.Error("Required parameter 'rotation' not provided", "validation_error");

            Vector3 eulerAngles = new Vector3(
                rotationObj["x"]?.ToObject<float>() ?? 0f,
                rotationObj["y"]?.ToObject<float>() ?? 0f,
                rotationObj["z"]?.ToObject<float>() ?? 0f
            );

            string space  = parameters["space"]?.ToObject<string>() ?? "world";
            bool relative = parameters["relative"]?.ToObject<bool>() ?? false;

            Undo.RecordObject(transform, "Rotate GameObject");

            if (relative)
            {
                Space unitySpace = space.ToLower() == "local" ? Space.Self : Space.World;
                transform.Rotate(eulerAngles, unitySpace);
            }
            else
            {
                if (space.ToLower() == "local") transform.localEulerAngles = eulerAngles;
                else                            transform.eulerAngles       = eulerAngles;
            }

            EditorUtility.SetDirty(go);

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = $"GameObject '{go.name}' rotated successfully.",
                ["instanceId"] = go.GetInstanceID(),
                ["name"]       = go.name,
                ["path"]       = GameObjectUtils.GetPath(go),
                ["rotation"]   = new JObject
                {
                    ["world"] = new JObject { ["x"] = transform.eulerAngles.x, ["y"] = transform.eulerAngles.y, ["z"] = transform.eulerAngles.z },
                    ["local"] = new JObject { ["x"] = transform.localEulerAngles.x, ["y"] = transform.localEulerAngles.y, ["z"] = transform.localEulerAngles.z }
                }
            };
        }

        // -------------------------------------------------------------------------
        // scale_gameobject
        // -------------------------------------------------------------------------
        private static JObject ScaleGameObject(JObject parameters)
        {
            var found = FindGameObject(parameters);
            if (found.Error != null) return found.Error;

            GameObject go        = found.GameObject;
            Transform transform  = go.transform;

            JObject scaleObj = parameters["scale"] as JObject;
            if (scaleObj == null)
                return JsonResponseFactory.Error("Required parameter 'scale' not provided", "validation_error");

            Vector3 scale = new Vector3(
                scaleObj["x"]?.ToObject<float>() ?? 1f,
                scaleObj["y"]?.ToObject<float>() ?? 1f,
                scaleObj["z"]?.ToObject<float>() ?? 1f
            );

            bool relative = parameters["relative"]?.ToObject<bool>() ?? false;

            Undo.RecordObject(transform, "Scale GameObject");

            if (relative) transform.localScale = Vector3.Scale(transform.localScale, scale);
            else          transform.localScale = scale;

            EditorUtility.SetDirty(go);

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = $"GameObject '{go.name}' scaled successfully.",
                ["instanceId"] = go.GetInstanceID(),
                ["name"]       = go.name,
                ["path"]       = GameObjectUtils.GetPath(go),
                ["scale"]      = new JObject
                {
                    ["x"] = transform.localScale.x,
                    ["y"] = transform.localScale.y,
                    ["z"] = transform.localScale.z
                }
            };
        }

        // -------------------------------------------------------------------------
        // set_transform
        // -------------------------------------------------------------------------
        private static JObject SetTransform(JObject parameters)
        {
            var found = FindGameObject(parameters);
            if (found.Error != null) return found.Error;

            GameObject go        = found.GameObject;
            Transform transform  = go.transform;

            JObject positionObj = parameters["position"] as JObject;
            JObject rotationObj = parameters["rotation"] as JObject;
            JObject scaleObj    = parameters["scale"] as JObject;

            if (positionObj == null && rotationObj == null && scaleObj == null)
                return JsonResponseFactory.Error("At least one of 'position', 'rotation', or 'scale' must be provided", "validation_error");

            string space  = parameters["space"]?.ToObject<string>() ?? "world";
            bool isLocal  = space.ToLower() == "local";

            Undo.RecordObject(transform, "Set Transform");

            if (positionObj != null)
            {
                Vector3 position = new Vector3(
                    positionObj["x"]?.ToObject<float>() ?? (isLocal ? transform.localPosition.x : transform.position.x),
                    positionObj["y"]?.ToObject<float>() ?? (isLocal ? transform.localPosition.y : transform.position.y),
                    positionObj["z"]?.ToObject<float>() ?? (isLocal ? transform.localPosition.z : transform.position.z)
                );
                if (isLocal) transform.localPosition = position;
                else         transform.position       = position;
            }

            if (rotationObj != null)
            {
                Vector3 eulerAngles = new Vector3(
                    rotationObj["x"]?.ToObject<float>() ?? (isLocal ? transform.localEulerAngles.x : transform.eulerAngles.x),
                    rotationObj["y"]?.ToObject<float>() ?? (isLocal ? transform.localEulerAngles.y : transform.eulerAngles.y),
                    rotationObj["z"]?.ToObject<float>() ?? (isLocal ? transform.localEulerAngles.z : transform.eulerAngles.z)
                );
                if (isLocal) transform.localEulerAngles = eulerAngles;
                else         transform.eulerAngles       = eulerAngles;
            }

            if (scaleObj != null)
            {
                Vector3 scale = new Vector3(
                    scaleObj["x"]?.ToObject<float>() ?? transform.localScale.x,
                    scaleObj["y"]?.ToObject<float>() ?? transform.localScale.y,
                    scaleObj["z"]?.ToObject<float>() ?? transform.localScale.z
                );
                transform.localScale = scale;
            }

            EditorUtility.SetDirty(go);

            return new JObject
            {
                ["success"]    = true,
                ["type"]       = "text",
                ["message"]    = $"GameObject '{go.name}' transform updated successfully.",
                ["instanceId"] = go.GetInstanceID(),
                ["name"]       = go.name,
                ["path"]       = GameObjectUtils.GetPath(go),
                ["transform"]  = new JObject
                {
                    ["position"] = new JObject
                    {
                        ["world"] = new JObject { ["x"] = transform.position.x, ["y"] = transform.position.y, ["z"] = transform.position.z },
                        ["local"] = new JObject { ["x"] = transform.localPosition.x, ["y"] = transform.localPosition.y, ["z"] = transform.localPosition.z }
                    },
                    ["rotation"] = new JObject
                    {
                        ["world"] = new JObject { ["x"] = transform.eulerAngles.x, ["y"] = transform.eulerAngles.y, ["z"] = transform.eulerAngles.z },
                        ["local"] = new JObject { ["x"] = transform.localEulerAngles.x, ["y"] = transform.localEulerAngles.y, ["z"] = transform.localEulerAngles.z }
                    },
                    ["scale"] = new JObject
                    {
                        ["x"] = transform.localScale.x,
                        ["y"] = transform.localScale.y,
                        ["z"] = transform.localScale.z
                    }
                }
            };
        }
    }
}

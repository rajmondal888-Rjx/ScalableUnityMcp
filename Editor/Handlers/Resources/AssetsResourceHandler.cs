using ScalableMCP.Editor;
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler for Unity AssetDatabase asset enumeration.
    /// URI: unity://assets
    /// </summary>
    public class AssetsResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_assets";
        public override string Uri  => "unity://assets";

        public override JObject Fetch(JObject parameters)
        {
            string assetType      = parameters?["assetType"]?.ToObject<string>();
            string searchPattern  = parameters?["searchPattern"]?.ToObject<string>();

            JArray assets = GetAllAssets(assetType, searchPattern);

            return new JObject
            {
                ["success"] = true,
                ["message"] = $"Retrieved {assets.Count} assets",
                ["assets"]  = assets
            };
        }

        private static JArray GetAllAssets(string assetType, string searchPattern)
        {
            JArray result = new JArray();
            string[] assetGuids = AssetDatabase.FindAssets(string.IsNullOrEmpty(searchPattern) ? "" : searchPattern);

            foreach (string guid in assetGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (AssetDatabase.IsValidFolder(assetPath)) continue;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (asset == null) continue;

                string fileType = asset.GetType().Name;

                if (!string.IsNullOrEmpty(assetType) && !fileType.Equals(assetType, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new JObject
                {
                    ["name"]      = Path.GetFileNameWithoutExtension(assetPath),
                    ["filename"]  = Path.GetFileName(assetPath),
                    ["path"]      = assetPath,
                    ["type"]      = fileType,
                    ["extension"] = Path.GetExtension(assetPath).TrimStart('.'),
                    ["guid"]      = guid,
                    ["size"]      = GetAssetSize(assetPath)
                });
            }

            return result;
        }

        private static long GetAssetSize(string assetPath)
        {
            string fullPath = Path.Combine(Application.dataPath, "..", assetPath);
            FileInfo fileInfo = new FileInfo(fullPath);
            return fileInfo.Exists ? fileInfo.Length : -1;
        }
    }
}

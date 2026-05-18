using ScalableMCP.Editor;
using UnityEngine;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler for Unity Package Manager package information.
    /// URI: unity://packages
    /// </summary>
    public class PackagesResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_packages";
        public override string Uri  => "unity://packages";

        private ListRequest _listRequest;

        public override JObject Fetch(JObject parameters)
        {
            JArray projectPackages  = GetProjectPackages();
            JArray registryPackages = GetRegistryPackages();

            return new JObject
            {
                ["success"]          = true,
                ["message"]          = $"Retrieved {projectPackages.Count} project packages and {registryPackages.Count} registry packages",
                ["projectPackages"]  = projectPackages,
                ["registryPackages"] = registryPackages
            };
        }

        private JArray GetProjectPackages()
        {
            JArray result = new JArray();
            _listRequest = Client.List(true);

            while (!_listRequest.IsCompleted)
                System.Threading.Thread.Sleep(100);

            if (_listRequest.Status == StatusCode.Success)
            {
                foreach (var package in _listRequest.Result)
                    result.Add(PackageToJObject(package, "installed"));
            }
            else if (_listRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"[ScalableMCP] Failed to list project packages: {_listRequest.Error.message}");
            }

            return result;
        }

        private JArray GetRegistryPackages()
        {
            JArray result = new JArray();
            SearchRequest searchRequest = Client.SearchAll();

            while (!searchRequest.IsCompleted)
                System.Threading.Thread.Sleep(100);

            if (searchRequest.Status == StatusCode.Success)
            {
                foreach (var package in searchRequest.Result)
                {
                    string state = "not_installed";
                    if (_listRequest.Status == StatusCode.Success)
                    {
                        foreach (var installed in _listRequest.Result)
                        {
                            if (installed.name == package.name) { state = "installed"; break; }
                        }
                    }
                    result.Add(PackageToJObject(package, state));
                }
            }
            else if (searchRequest.Status == StatusCode.Failure)
            {
                Debug.LogError($"[ScalableMCP] Failed to search registry packages: {searchRequest.Error.message}");
            }

            return result;
        }

        private static JObject PackageToJObject(UnityEditor.PackageManager.PackageInfo package, string state)
        {
            return new JObject
            {
                ["name"]        = package.name,
                ["displayName"] = package.displayName,
                ["version"]     = package.version,
                ["description"] = package.description,
                ["category"]    = package.category,
                ["source"]      = package.source.ToString(),
                ["state"]       = state,
                ["author"]      = new JObject
                {
                    ["name"]  = package.author?.name,
                    ["email"] = package.author?.email,
                    ["url"]   = package.author?.url
                }
            };
        }
    }
}

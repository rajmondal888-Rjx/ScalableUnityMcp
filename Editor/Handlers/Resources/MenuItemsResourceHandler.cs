using ScalableMCP.Editor;
using System;
using System.Reflection;
using UnityEditor;
using Newtonsoft.Json.Linq;

namespace ScalableMCP.Editor.Handlers
{
    /// <summary>
    /// Resource handler that lists all available Unity Editor menu items.
    /// URI: unity://menu-items
    /// </summary>
    public class MenuItemsResourceHandler : ResourceHandlerBase
    {
        public override string Name => "get_menu_items";
        public override string Uri  => "unity://menu-items";

        public override JObject Fetch(JObject parameters)
        {
            JArray menuItems = GetAllMenuItems();

            return new JObject
            {
                ["success"]   = true,
                ["message"]   = $"Retrieved {menuItems.Count} menu items",
                ["menuItems"] = menuItems
            };
        }

        private static JArray GetAllMenuItems()
        {
            var menuItemsArray = new JArray();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.StartsWith("System.") ||
                    assembly.FullName.StartsWith("Microsoft.") ||
                    assembly.FullName.StartsWith("mscorlib"))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        var attrs = method.GetCustomAttributes(typeof(MenuItem), false);
                        foreach (MenuItem attr in attrs)
                        {
                            if (attr.menuItem.StartsWith("CONTEXT")) continue;
                            menuItemsArray.Add(attr.menuItem);
                        }
                    }
                }
            }

            return menuItemsArray;
        }
    }
}

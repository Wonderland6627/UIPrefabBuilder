using System.Collections.Generic;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class AssetFinder
    {
        public static List<string> Find(string filter, int limit = 50)
        {
            var results = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(filter))
            {
                if (results.Count >= limit) break;
                results.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return results;
        }

        public static string GetInfo(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            return asset != null ? $"{asset.name} ({asset.GetType().Name}) @ {path}" : $"Not found: {path}";
        }
    }
}

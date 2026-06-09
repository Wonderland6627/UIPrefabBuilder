using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class AssetFinder
    {
        public static List<string> Find(string filter, int limit = 50)
        {
            if (filter != null && (filter.Contains("*") || filter.Contains("?")))
                return FindByGlob(filter, limit: limit);

            var results = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(filter))
            {
                if (results.Count >= limit) break;
                results.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            return results;
        }

        public static List<string> FindByGlob(string pattern, string directory = "Assets",
            string extensions = null, int limit = 50)
        {
            var results = new List<string>();
            var extSet = ParseExtensions(extensions);
            var regex = GlobToRegex(pattern);

            var relDir = directory.StartsWith("Assets/") ? directory.Substring(7)
                       : directory == "Assets" ? ""
                       : directory;
            var dir = string.IsNullOrEmpty(relDir)
                ? Application.dataPath
                : Path.Combine(Application.dataPath, relDir);

            if (!Directory.Exists(dir)) return results;

            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (results.Count >= limit) break;
                if (file.EndsWith(".meta")) continue;

                var fileName = Path.GetFileName(file);
                if (extSet != null && !extSet.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    continue;
                if (!regex.IsMatch(fileName)) continue;

                results.Add("Assets" + file.Substring(Application.dataPath.Length).Replace('\\', '/'));
            }
            return results;
        }

        private static Regex GlobToRegex(string glob)
        {
            var escaped = Regex.Escape(glob)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase);
        }

        private static HashSet<string> ParseExtensions(string extensions)
        {
            if (string.IsNullOrWhiteSpace(extensions)) return null;
            var set = new HashSet<string>();
            foreach (var ext in extensions.Split(','))
            {
                var trimmed = ext.Trim().ToLowerInvariant();
                if (!trimmed.StartsWith(".")) trimmed = "." + trimmed;
                set.Add(trimmed);
            }
            return set.Count > 0 ? set : null;
        }

        public static string GetInfo(string path)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null) return $"Not found: {path}";

            var sb = new StringBuilder();
            sb.Append($"{asset.name} ({asset.GetType().Name}) @ {path}");

            if (asset is Texture2D tex)
            {
                sb.Append($" | size={tex.width}x{tex.height}");

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    sb.Append($" | textureType={importer.textureType}");
                    if (importer.textureType == TextureImporterType.Sprite)
                    {
                        sb.Append($" | spriteMode={importer.spriteImportMode}");
                        var border = importer.spriteBorder;
                        if (border != Vector4.zero)
                            sb.Append($" | border(L={border.x},B={border.y},R={border.z},T={border.w})");
                        var pivot = importer.spritePivot;
                        sb.Append($" | pivot=({pivot.x},{pivot.y})");
                    }
                }
            }
            else if (asset is Sprite sprite)
            {
                sb.Append($" | size={sprite.rect.width}x{sprite.rect.height}");
                var border = sprite.border;
                if (border != Vector4.zero)
                    sb.Append($" | border(L={border.x},B={border.y},R={border.z},T={border.w})");
                sb.Append($" | pivot=({sprite.pivot.x},{sprite.pivot.y})");
            }

            return sb.ToString();
        }
    }
}

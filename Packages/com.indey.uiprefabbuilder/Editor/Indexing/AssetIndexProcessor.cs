using System.Collections.Generic;
using System.IO;
using System.Linq;
using Indey.UIPrefabBuilder.Config;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Indexing
{
    public class AssetIndexProcessor : AssetPostprocessor
    {
        private static readonly HashSet<string> ValidExtensions =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".tga", ".psd" };

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (!BuilderSettings.Get().EnableAssetIndexing) return;

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady) return;

            var config = ProjectConfig.Current?.indexingConfig ?? new IndexingConfig();

            var toRemove = deletedAssets
                .Concat(movedFromAssetPaths)
                .Where(p => IsIndexableSprite(p, config))
                .ToList();

            foreach (var path in toRemove)
                indexer.RemoveAsset(path);

            var toUpdate = importedAssets
                .Concat(movedAssets)
                .Where(p => IsIndexableSprite(p, config))
                .ToArray();

            if (toUpdate.Length > 0)
                indexer.UpdateAssets(toUpdate);
        }

        private static bool IsIndexableSprite(string path, IndexingConfig config)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            if (!ValidExtensions.Contains(ext)) return false;
            if (!config.IsInIndexDirectory(path)) return false;
            if (config.IsIgnored(path)) return false;
            return true;
        }
    }
}

using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Indexing
{
    /// <summary>
    /// Auto-initializes the asset indexer on editor startup when indexing is enabled.
    /// Loads the existing index from disk so that AssetPostprocessor events can
    /// immediately apply incremental updates.
    /// </summary>
    [InitializeOnLoad]
    internal static class AssetIndexBootstrap
    {
        static AssetIndexBootstrap()
        {
            EditorApplication.delayCall += OnDelayedInit;
        }

        private static void OnDelayedInit()
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing) return;

            try
            {
                AssetIndexer.Instance.Initialize();
            }
            catch (System.Exception e)
            {
                ConsoleLogger.Warning($"[AssetIndexBootstrap] Deferred init failed: {e.Message}");
            }
        }
    }
}

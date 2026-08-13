using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Indey.UIPrefabBuilder.Async;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Indexing
{
    public struct MatchResult
    {
        public string assetPath;
        public string guid;
        public float score;
        public float confidence;
    }

    public class AssetIndexer
    {
        private static AssetIndexer _instance;
        public static AssetIndexer Instance => _instance ??= new AssetIndexer();

        private EmbeddingService _embedding;
        private VectorStore _store;
        private CancellationTokenSource _cts;
        private string _projectRoot;

        private static readonly HashSet<string> ValidExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".png", ".jpg", ".jpeg", ".tga", ".psd" };

        public event Action<float, string> OnProgress;
        public event Action OnIndexingComplete;

        public bool IsIndexing { get; private set; }
        public bool IsReady => _store != null && _store.IsLoaded && _embedding != null && _embedding.IsLoaded;
        public int IndexedCount => _store?.Count ?? 0;
        public long LastBuildTimestamp => _store?.LastBuildTimestamp ?? 0;

        private string ProjectRoot
        {
            get
            {
                if (_projectRoot == null)
                    _projectRoot = Path.GetDirectoryName(Application.dataPath);
                return _projectRoot;
            }
        }

        private string MetaPath => Path.Combine(ProjectRoot, "Library", "UIPrefabBuilder", "Index", "AssetIndexMeta.json");
        private string VectorsPath => Path.Combine(ProjectRoot, "Library", "UIPrefabBuilder", "Index", "AssetVectors.bytes");

        public void Initialize()
        {
            if (_embedding != null) return;

            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing) return;

            var modelPath = settings.EmbeddingModelPath;
            ConsoleLogger.Log($"[AssetIndexer] Model path from settings: {modelPath}");

            _embedding = new EmbeddingService();
            _embedding.LoadModel(modelPath);

            _store = new VectorStore();
            _store.Load(MetaPath, VectorsPath);

            ConsoleLogger.Log($"[AssetIndexer] Initialized. {_store.Count} entries loaded. Model ready: {_embedding.IsLoaded}");
        }

        public void EnsureInitialized()
        {
            if (_embedding == null || _store == null)
            {
                Initialize();
                return;
            }

            if (!_embedding.IsLoaded)
            {
                ConsoleLogger.Log("[AssetIndexer] Model not loaded, retrying initialization...");
                _embedding.Dispose();
                _embedding = null;
                _store = null;
                Initialize();
            }
        }

        public void RebuildFullIndex()
        {
            if (IsIndexing)
            {
                ConsoleLogger.Warning("[AssetIndexer] Indexing already in progress.");
                return;
            }

            EnsureInitialized();
            if (!_embedding.IsLoaded)
            {
                ConsoleLogger.Error("[AssetIndexer] Embedding model not loaded. Cannot index.");
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            IsIndexing = true;

            var config = ProjectConfig.Current?.indexingConfig ?? new IndexingConfig();
            var root = ProjectRoot;
            // An empty list means "not configured", not "index nothing" — without this fallback a
            // rebuild silently scans zero files and every visual sprite match comes back empty.
            var dirsCopy = new List<string>(config.indexDirectories ?? new List<string>());
            if (dirsCopy.Count == 0) dirsCopy.Add("Assets");
            var ignoresCopy = new List<string>(config.ignorePatterns ?? new List<string>());

            BackgroundWorker.Run(
                () => CollectAssetPaths(dirsCopy, ignoresCopy, root),
                paths => OnPathsCollected(paths, config),
                e =>
                {
                    IsIndexing = false;
                    ConsoleLogger.Error($"[AssetIndexer] Scan failed: {e.Message}");
                },
                _cts.Token
            );
        }

        /// <summary>
        /// Update a single asset in the index. Called from AssetPostprocessor.
        /// Must be called on the main thread.
        /// </summary>
        public void UpdateAsset(string assetPath)
        {
            UpdateAssets(new[] { assetPath });
        }

        /// <summary>
        /// Update multiple assets. Called from AssetPostprocessor.
        /// Must be called on the main thread.
        /// </summary>
        public void UpdateAssets(string[] assetPaths)
        {
            EnsureInitialized();
            if (!_embedding.IsLoaded || _store == null) return;

            int updated = 0;
            foreach (var path in assetPaths)
            {
                if (!IsValidAsset(path)) continue;

                var hash = ComputeFileHash(path);
                if (!_store.NeedsUpdate(path, hash)) continue;

                var vector = ExtractEmbeddingForAsset(path);
                if (vector == null) continue;

                var guid = AssetDatabase.AssetPathToGUID(path);
                _store.Upsert(path, guid, hash, vector);
                updated++;
            }

            if (updated > 0)
            {
                _store.Save(MetaPath, VectorsPath);
                ConsoleLogger.Log($"[AssetIndexer] Incremental update: {updated} assets.");
            }
        }

        public void RemoveAsset(string assetPath)
        {
            EnsureInitialized();
            if (_store == null) return;

            if (_store.Contains(assetPath))
            {
                _store.Remove(assetPath);
                _store.Compact();
                _store.Save(MetaPath, VectorsPath);
                ConsoleLogger.Log($"[AssetIndexer] Removed from index: {assetPath}");
            }
        }

        public List<MatchResult> Query(float[] queryVector, int topK = 5)
        {
            if (_store == null || _store.Count == 0 || queryVector == null)
                return new List<MatchResult>();

            var (matrix, entries) = _store.GetMatrixView();
            var (indices, scores) = SimilaritySearch.TopK(
                queryVector, matrix, entries.Count, EmbeddingService.Dimension, topK);

            var results = new List<MatchResult>(indices.Length);
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] < 0 || indices[i] >= entries.Count) continue;
                var entry = entries[indices[i]];
                results.Add(new MatchResult
                {
                    assetPath = entry.assetPath,
                    guid = entry.guid,
                    score = scores[i],
                    confidence = Mathf.Clamp01(scores[i]) * 100f
                });
            }
            return results;
        }

        public List<MatchResult> QueryByImage(byte[] imageBytes, int topK = 5)
        {
            EnsureInitialized();
            if (!_embedding.IsLoaded)
                return new List<MatchResult>();

            var vector = _embedding.ExtractEmbeddingFromBytes(imageBytes);
            if (vector == null) return new List<MatchResult>();

            return Query(vector, topK);
        }

        /// <summary>
        /// Search sprites by text description using CLIP text encoder.
        /// e.g. "treasure chest", "cat icon", "red button"
        /// </summary>
        public List<MatchResult> QueryByText(string text, int topK = 5)
        {
            EnsureInitialized();
            EnsureTextModelLoaded();

            if (!_embedding.IsTextModelLoaded)
                return new List<MatchResult>();

            var vector = _embedding.ExtractTextEmbedding(text);
            if (vector == null) return new List<MatchResult>();

            return Query(vector, topK);
        }

        public bool IsTextSearchReady => _embedding != null && _embedding.IsTextModelLoaded;

        private void EnsureTextModelLoaded()
        {
            if (_embedding == null) return;
            if (_embedding.IsTextModelLoaded) return;

            var settings = BuilderSettings.Get();
            var textModelPath = settings.TextModelPath;
            var vocabPath = settings.TokenizerVocabPath;
            var mergesPath = settings.TokenizerMergesPath;

            if (string.IsNullOrEmpty(textModelPath)) return;

            _embedding.LoadTextModel(textModelPath, vocabPath, mergesPath);
        }

        public void Cancel()
        {
            _cts?.Cancel();
            IsIndexing = false;
        }

        public void Dispose()
        {
            Cancel();
            _embedding?.Dispose();
            _embedding = null;
            _store = null;
            _instance = null;
        }

        #region Internal

        private List<string> CollectAssetPaths(List<string> dirs, List<string> ignorePatterns, string projectRoot)
        {
            var results = new List<string>();
            var assetsDir = Path.Combine(projectRoot, "Assets");
            var tempConfig = new IndexingConfig { indexDirectories = dirs, ignorePatterns = ignorePatterns };

            foreach (var relDir in dirs)
            {
                var absDir = Path.Combine(projectRoot, relDir);
                if (!Directory.Exists(absDir))
                {
                    ConsoleLogger.Warning($"[AssetIndexer] Index directory not found, skipping: {absDir}");
                    continue;
                }

                int dirCount = 0;
                foreach (var file in Directory.EnumerateFiles(absDir, "*", SearchOption.AllDirectories))
                {
                    if (_cts != null && _cts.IsCancellationRequested) break;

                    var ext = Path.GetExtension(file);
                    if (!ValidExtensions.Contains(ext)) continue;
                    if (file.EndsWith(".meta")) continue;

                    var assetPath = "Assets" + file
                        .Substring(assetsDir.Length)
                        .Replace('\\', '/');

                    if (tempConfig.IsIgnored(assetPath)) continue;
                    results.Add(assetPath);
                    dirCount++;
                }

                ConsoleLogger.Log($"[AssetIndexer] Scanned '{relDir}': found {dirCount} image assets.");
            }

            ConsoleLogger.Log($"[AssetIndexer] Total assets collected: {results.Count}");
            return results;
        }

        private void OnPathsCollected(List<string> paths, IndexingConfig config)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                IsIndexing = false;
                return;
            }

            var toProcess = new List<string>();
            foreach (var path in paths)
            {
                var hash = ComputeFileHash(path);
                if (_store.NeedsUpdate(path, hash))
                    toProcess.Add(path);
            }

            if (toProcess.Count == 0)
            {
                IsIndexing = false;
                OnProgress?.Invoke(1f, "Index up to date.");
                OnIndexingComplete?.Invoke();
                ConsoleLogger.Log("[AssetIndexer] Full index check: all entries up to date.");
                return;
            }

            ConsoleLogger.Log($"[AssetIndexer] Processing {toProcess.Count} / {paths.Count} assets...");
            ProcessBatch(toProcess, 0);
        }

        private void ProcessBatch(List<string> paths, int startIdx)
        {
            if (_cts != null && _cts.IsCancellationRequested)
            {
                FinishIndexing();
                return;
            }

            const int batchSize = 8;
            int end = Math.Min(startIdx + batchSize, paths.Count);
            int processed = 0;

            for (int i = startIdx; i < end; i++)
            {
                var path = paths[i];
                var vector = ExtractEmbeddingForAsset(path);
                if (vector == null) continue;

                var guid = AssetDatabase.AssetPathToGUID(path);
                var hash = ComputeFileHash(path);
                _store.Upsert(path, guid, hash, vector);
                processed++;
            }

            float progress = (float)end / paths.Count;
            OnProgress?.Invoke(progress, $"Indexed {end}/{paths.Count} assets...");

            if (end < paths.Count)
            {
                MainThreadDispatcher.EnqueueNextFrame(() => ProcessBatch(paths, end));
            }
            else
            {
                FinishIndexing();
            }
        }

        private void FinishIndexing()
        {
            _store.Compact();
            _store.Save(MetaPath, VectorsPath);
            IsIndexing = false;
            OnProgress?.Invoke(1f, $"Indexing complete. {_store.Count} assets indexed.");
            OnIndexingComplete?.Invoke();
            ConsoleLogger.Log($"[AssetIndexer] Indexing complete. {_store.Count} total entries.");
        }

        private static bool IsValidAsset(string path)
        {
            var ext = Path.GetExtension(path);
            return ValidExtensions.Contains(ext);
        }

        /// <summary>
        /// 计算资源的 CLIP embedding。优先读取磁盘上的原始文件字节（PNG/JPG）解码，
        /// 避免使用 Unity 导入管线产出的 Texture2D —— 后者可能被 NPOT 缩放或压缩，
        /// 导致非 2 次幂 sprite 以变形的样子进索引、拉低视觉匹配准确率。
        /// 原始解码失败时（如 .tga/.psd）回退到导入后的 Texture2D。
        /// </summary>
        private float[] ExtractEmbeddingForAsset(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                try
                {
                    var fullPath = Path.Combine(ProjectRoot, path);
                    if (File.Exists(fullPath))
                    {
                        var vector = _embedding.ExtractEmbeddingFromBytes(File.ReadAllBytes(fullPath));
                        if (vector != null) return vector;
                    }
                }
                catch { }
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return tex == null ? null : _embedding.ExtractEmbedding(tex);
        }

        private string ComputeFileHash(string assetPath)
        {
            var fullPath = Path.Combine(ProjectRoot, assetPath);
            if (!File.Exists(fullPath)) return "";

            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(fullPath);
                var hash = md5.ComputeHash(stream);
                return "md5:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }

        #endregion
    }
}

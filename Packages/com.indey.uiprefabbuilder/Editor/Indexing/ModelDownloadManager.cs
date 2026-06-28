using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Indey.UIPrefabBuilder.Async;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Indexing
{
    public enum DownloadTaskType
    {
        VisionModel,
        TextModel
    }

    public enum DownloadState
    {
        Idle,
        Downloading,
        Completed,
        Failed,
        Cancelled
    }

    public class ModelDownloadManager
    {
        private static ModelDownloadManager _instance;
        public static ModelDownloadManager Instance => _instance ??= new ModelDownloadManager();

        private const string ModelFileName = "clip_vit_visual.onnx";
        private const string TextModelFileName = "clip_vit_textual.onnx";
        private const string VocabFileName = "clip_vocab.json";
        private const string MergesFileName = "clip_merges.txt";

        private static readonly string[] VisionModelUrls =
        {
            "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx",
            "https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx/resolve/main/model_visual.onnx",
        };

        private static readonly string[] TextModelUrls =
        {
            "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/text_model.onnx",
            "https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx/resolve/main/model_textual.onnx",
        };

        private const string VocabUrl = "https://huggingface.co/openai/clip-vit-base-patch32/resolve/main/vocab.json";
        private const string MergesUrl = "https://huggingface.co/openai/clip-vit-base-patch32/resolve/main/merges.txt";

        private static string ModelDirectory =>
            Path.GetFullPath("Packages/com.indey.uiprefabbuilder/Editor/Models");

        private readonly Queue<DownloadTaskType> _queue = new Queue<DownloadTaskType>();
        private CancellationTokenSource _cts;
        private bool _isProcessing;

        public DownloadState State { get; private set; } = DownloadState.Idle;
        public DownloadTaskType? CurrentTask { get; private set; }
        public float Progress { get; private set; }
        public string ProgressText { get; private set; } = "";
        public float SpeedMBps { get; private set; }
        public string LastError { get; private set; }
        public int QueueCount => _queue.Count;

        public event Action OnStateChanged;

        public bool IsDownloading => State == DownloadState.Downloading;

        public void EnqueueVisionModel()
        {
            if (IsTaskActiveOrQueued(DownloadTaskType.VisionModel)) return;
            _queue.Enqueue(DownloadTaskType.VisionModel);
            ConsoleLogger.Log("[ModelDownloadManager] Vision model queued for download.");
            ProcessQueue();
        }

        public void EnqueueTextModel()
        {
            if (IsTaskActiveOrQueued(DownloadTaskType.TextModel)) return;
            _queue.Enqueue(DownloadTaskType.TextModel);
            ConsoleLogger.Log("[ModelDownloadManager] Text model queued for download.");
            ProcessQueue();
        }

        public void CancelCurrent()
        {
            if (_cts == null || !IsDownloading) return;
            ConsoleLogger.Log("[ModelDownloadManager] Cancelling current download...");
            _cts.Cancel();
        }

        public void CancelAll()
        {
            _queue.Clear();
            CancelCurrent();
        }

        public void RemoveVisionModel()
        {
            var path = Path.Combine(ModelDirectory, ModelFileName);
            RemoveFileIfExists(path);

            var settings = BuilderSettings.Get();
            settings.EmbeddingModelPath = "";
            settings.Save();

            DisposeIndexerIfLoaded();
            ConsoleLogger.Log("[ModelDownloadManager] Vision model removed.");
            NotifyStateChanged();
        }

        public void RemoveTextModel()
        {
            RemoveFileIfExists(Path.Combine(ModelDirectory, TextModelFileName));
            RemoveFileIfExists(Path.Combine(ModelDirectory, VocabFileName));
            RemoveFileIfExists(Path.Combine(ModelDirectory, MergesFileName));

            var settings = BuilderSettings.Get();
            settings.TextModelPath = "";
            settings.TokenizerVocabPath = "";
            settings.TokenizerMergesPath = "";
            settings.Save();

            ConsoleLogger.Log("[ModelDownloadManager] Text model removed.");
            NotifyStateChanged();
        }

        public static bool IsVisionModelPresent()
        {
            var path = Path.Combine(ModelDirectory, ModelFileName);
            return File.Exists(path) && new FileInfo(path).Length > 1_000_000;
        }

        public static bool IsTextModelPresent()
        {
            return File.Exists(Path.Combine(ModelDirectory, TextModelFileName))
                && File.Exists(Path.Combine(ModelDirectory, VocabFileName))
                && File.Exists(Path.Combine(ModelDirectory, MergesFileName));
        }

        public static long GetVisionModelSize()
        {
            var path = Path.Combine(ModelDirectory, ModelFileName);
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        public static long GetTextModelSize()
        {
            long total = 0;
            var files = new[] { TextModelFileName, VocabFileName, MergesFileName };
            foreach (var f in files)
            {
                var p = Path.Combine(ModelDirectory, f);
                if (File.Exists(p)) total += new FileInfo(p).Length;
            }
            return total;
        }

        private bool IsTaskActiveOrQueued(DownloadTaskType type)
        {
            if (CurrentTask == type) return true;
            foreach (var t in _queue)
                if (t == type) return true;
            return false;
        }

        private void ProcessQueue()
        {
            if (_isProcessing) return;
            if (_queue.Count == 0)
            {
                if (State == DownloadState.Downloading)
                {
                    State = DownloadState.Idle;
                    NotifyStateChanged();
                }
                return;
            }

            _isProcessing = true;
            var task = _queue.Dequeue();
            CurrentTask = task;
            State = DownloadState.Downloading;
            Progress = 0f;
            ProgressText = "Connecting...";
            SpeedMBps = 0f;
            LastError = null;
            NotifyStateChanged();

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    switch (task)
                    {
                        case DownloadTaskType.VisionModel:
                            await DownloadVisionModelAsync(token);
                            break;
                        case DownloadTaskType.TextModel:
                            await DownloadTextModelAsync(token);
                            break;
                    }

                    MainThreadDispatcher.Enqueue(() => OnTaskCompleted(task));
                }
                catch (OperationCanceledException)
                {
                    MainThreadDispatcher.Enqueue(() => OnTaskCancelled(task));
                }
                catch (Exception e)
                {
                    var msg = e.Message;
                    MainThreadDispatcher.Enqueue(() => OnTaskFailed(task, msg));
                }
            }, token);
        }

        private void OnTaskCompleted(DownloadTaskType task)
        {
            ConsoleLogger.Log($"[ModelDownloadManager] {task} download completed.");
            State = DownloadState.Completed;
            Progress = 1f;
            _isProcessing = false;
            CurrentTask = null;

            var settings = BuilderSettings.Get();
            var modelDir = "Packages/com.indey.uiprefabbuilder/Editor/Models/";
            switch (task)
            {
                case DownloadTaskType.VisionModel:
                    settings.EmbeddingModelPath = modelDir + ModelFileName;
                    break;
                case DownloadTaskType.TextModel:
                    settings.TextModelPath = modelDir + TextModelFileName;
                    settings.TokenizerVocabPath = modelDir + VocabFileName;
                    settings.TokenizerMergesPath = modelDir + MergesFileName;
                    break;
            }
            settings.Save();
            AssetDatabase.Refresh();
            NotifyStateChanged();
            ProcessQueue();
        }

        private void OnTaskCancelled(DownloadTaskType task)
        {
            ConsoleLogger.Log($"[ModelDownloadManager] {task} download cancelled.");
            State = DownloadState.Cancelled;
            _isProcessing = false;
            CurrentTask = null;
            CleanupTempFiles();
            NotifyStateChanged();
            ProcessQueue();
        }

        private void OnTaskFailed(DownloadTaskType task, string error)
        {
            ConsoleLogger.Error($"[ModelDownloadManager] {task} download failed: {error}");
            State = DownloadState.Failed;
            LastError = error;
            _isProcessing = false;
            CurrentTask = null;
            CleanupTempFiles();
            NotifyStateChanged();
            ProcessQueue();
        }

        private async Task DownloadVisionModelAsync(CancellationToken ct)
        {
            if (!Directory.Exists(ModelDirectory))
                Directory.CreateDirectory(ModelDirectory);

            var destPath = Path.Combine(ModelDirectory, ModelFileName);
            var tempPath = destPath + ".downloading";

            await DownloadWithFallbackAsync(VisionModelUrls, tempPath, destPath, "Vision Model", ct);
        }

        private async Task DownloadTextModelAsync(CancellationToken ct)
        {
            if (!Directory.Exists(ModelDirectory))
                Directory.CreateDirectory(ModelDirectory);

            // Download tokenizer files first (small files)
            var vocabDest = Path.Combine(ModelDirectory, VocabFileName);
            var mergesDest = Path.Combine(ModelDirectory, MergesFileName);

            if (!File.Exists(vocabDest))
            {
                UpdateProgress(0f, "Downloading vocab...", 0f);
                try
                {
                    await DownloadSmallFileAsync(VocabUrl, vocabDest, ct);
                }
                catch (Exception e)
                {
                    ConsoleLogger.Warning($"[ModelDownloadManager] Vocab download failed (non-critical): {e.Message}");
                }
            }

            if (!File.Exists(mergesDest))
            {
                UpdateProgress(0.05f, "Downloading merges...", 0f);
                await DownloadSmallFileAsync(MergesUrl, mergesDest, ct);
            }

            // Download main text model
            var destPath = Path.Combine(ModelDirectory, TextModelFileName);
            var tempPath = destPath + ".downloading";
            await DownloadWithFallbackAsync(TextModelUrls, tempPath, destPath, "Text Model", ct);
        }

        private async Task DownloadWithFallbackAsync(string[] urls, string tempPath, string destPath, string label, CancellationToken ct)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(15);

            foreach (var url in urls)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    ConsoleLogger.Log($"[ModelDownloadManager] Trying {label}: {url}");
                    await DownloadStreamAsync(client, url, tempPath, ct);

                    var info = new FileInfo(tempPath);
                    if (info.Length < 1_000_000)
                    {
                        ConsoleLogger.Warning($"[ModelDownloadManager] File too small ({info.Length}B), trying next URL.");
                        File.Delete(tempPath);
                        continue;
                    }

                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(tempPath, destPath);
                    ConsoleLogger.Log($"[ModelDownloadManager] {label} saved: {destPath} ({info.Length / (1024 * 1024)}MB)");
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e)
                {
                    ConsoleLogger.Warning($"[ModelDownloadManager] {label} failed from {url}: {e.Message}");
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }

            throw new Exception($"All download sources failed for {label}.");
        }

        private async Task DownloadStreamAsync(HttpClient client, string url, string tempPath, CancellationToken ct)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            var sw = Stopwatch.StartNew();
            long lastSpeedBytes = 0;
            double lastSpeedTime = 0;

            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                await fileStream.WriteAsync(buffer, 0, read, ct);
                downloaded += read;

                var elapsed = sw.Elapsed.TotalSeconds;
                if (elapsed - lastSpeedTime >= 0.5)
                {
                    var speed = (downloaded - lastSpeedBytes) / (elapsed - lastSpeedTime) / (1024.0 * 1024.0);
                    lastSpeedBytes = downloaded;
                    lastSpeedTime = elapsed;

                    float progress = totalBytes > 0 ? (float)downloaded / totalBytes : 0f;
                    var mb = downloaded / (1024f * 1024f);
                    var totalMb = totalBytes > 0 ? totalBytes / (1024f * 1024f) : 0f;
                    var text = totalBytes > 0
                        ? $"{mb:F1} / {totalMb:F1} MB"
                        : $"{mb:F1} MB";

                    UpdateProgress(progress, text, (float)speed);
                }
            }

            UpdateProgress(1f, "Finalizing...", 0f);
        }

        private async Task DownloadSmallFileAsync(string url, string destPath, CancellationToken ct)
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(60);
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadAsByteArrayAsync();
            ct.ThrowIfCancellationRequested();
            File.WriteAllBytes(destPath, data);
            ConsoleLogger.Log($"[ModelDownloadManager] Downloaded: {destPath} ({data.Length / 1024}KB)");
        }

        private void UpdateProgress(float progress, string text, float speedMBps)
        {
            MainThreadDispatcher.Enqueue(() =>
            {
                Progress = progress;
                ProgressText = text;
                SpeedMBps = speedMBps;
                NotifyStateChanged();
            });
        }

        private void CleanupTempFiles()
        {
            try
            {
                var visionTemp = Path.Combine(ModelDirectory, ModelFileName + ".downloading");
                var textTemp = Path.Combine(ModelDirectory, TextModelFileName + ".downloading");
                if (File.Exists(visionTemp)) File.Delete(visionTemp);
                if (File.Exists(textTemp)) File.Delete(textTemp);
            }
            catch (Exception e)
            {
                ConsoleLogger.Warning($"[ModelDownloadManager] Temp cleanup error: {e.Message}");
            }
        }

        private static void RemoveFileIfExists(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                var metaPath = path + ".meta";
                if (File.Exists(metaPath)) File.Delete(metaPath);
            }
            catch (Exception e)
            {
                ConsoleLogger.Warning($"[ModelDownloadManager] Failed to delete {path}: {e.Message}");
            }
        }

        private static void DisposeIndexerIfLoaded()
        {
            try
            {
                AssetIndexer.Instance.Dispose();
            }
            catch { }
        }

        private void NotifyStateChanged()
        {
            OnStateChanged?.Invoke();
        }
    }
}

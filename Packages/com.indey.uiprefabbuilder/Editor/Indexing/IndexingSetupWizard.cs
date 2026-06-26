using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Indexing
{
    /// <summary>
    /// Editor wizard that handles downloading the CLIP ViT visual encoder ONNX model
    /// and verifying the Sentis package installation.
    /// </summary>
    public static class IndexingSetupWizard
    {
        private const string ModelFileName = "clip_vit_visual.onnx";
        private const string TextModelFileName = "clip_vit_textual.onnx";
        private const string VocabFileName = "clip_vocab.txt";
        private const string MergesFileName = "clip_merges.txt";

        private static readonly string[] ModelUrls =
        {
            // Non-quantized FP32 models only — Sentis 1.x does not support quantized ONNX ops
            "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model.onnx",
            "https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx/resolve/main/model_visual.onnx",
        };

        private static readonly string[] TextModelUrls =
        {
            "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/text_model.onnx",
            "https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx/resolve/main/model_textual.onnx",
        };

        private const string VocabUrl = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/vocab.txt";
        private const string MergesUrl = "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/merges.txt";

        private static string ModelDirectory =>
            Path.GetFullPath("Packages/com.indey.uiprefabbuilder/Editor/Models");

        private static string ModelPath =>
            Path.Combine(ModelDirectory, ModelFileName);

        private static string TextModelPath =>
            Path.Combine(ModelDirectory, TextModelFileName);

        private static string VocabPath =>
            Path.Combine(ModelDirectory, VocabFileName);

        private static string MergesPath =>
            Path.Combine(ModelDirectory, MergesFileName);

        [MenuItem("Window/UI Prefab Builder/Setup Asset Indexing...", priority = 2000)]
        public static void ShowSetupDialog()
        {
            bool hasSentis = IsSentisInstalled();
            bool hasModel = File.Exists(ModelPath);

            var msg = "=== Asset Indexing Setup ===\n\n";

            msg += hasSentis
                ? "✓  Unity Sentis: Installed\n"
                : "✗  Unity Sentis: NOT installed\n";

            msg += hasModel
                ? $"✓  CLIP Model: Found at {ModelPath}\n"
                : $"✗  CLIP Model: Not found\n";

            if (hasSentis && hasModel)
            {
                msg += "\nAll dependencies are ready! Enable 'Asset Indexing' in the Builder Settings panel, then click 'Build Index'.";
                EditorUtility.DisplayDialog("Asset Indexing Setup", msg, "OK");
                return;
            }

            msg += "\n";

            if (!hasSentis)
            {
                msg += "Step 1: Install Unity Sentis\n";
                msg += "  → Window > Package Manager > + > Add by name\n";
                msg += "  → Enter: com.unity.sentis\n";
                msg += "  → For Unity 2021.3, use version 1.3.0-pre.3 or 1.4.0-pre.1\n\n";
            }

            if (!hasModel)
            {
                msg += (hasSentis ? "Step 1" : "Step 2") + ": Download CLIP Model\n";
                msg += "  → Click 'Download Model' to auto-download (~85MB)\n";
                msg += "  → Or manually download from HuggingFace (see console log)\n";
            }

            int choice = EditorUtility.DisplayDialogComplex(
                "Asset Indexing Setup",
                msg,
                hasModel ? "OK" : "Download Model",
                "Cancel",
                "Copy Manual Instructions");

            switch (choice)
            {
                case 0:
                    if (!hasModel) DownloadModelAsync();
                    break;
                case 2:
                    CopyManualInstructions();
                    break;
            }
        }

        public static void InstallSentis()
        {
            ConsoleLogger.Log("[IndexingSetup] Requesting Sentis package installation...");
            UnityEditor.PackageManager.Client.Add("com.unity.sentis");
            EditorUtility.DisplayDialog("Install Sentis",
                "Sentis package installation requested.\n\n" +
                "Unity will download and import the package.\n" +
                "If it fails, try adding manually:\n" +
                "  Window > Package Manager > + > Add by name\n" +
                "  Name: com.unity.sentis",
                "OK");
        }

        public static void DownloadModel()
        {
            if (File.Exists(ModelPath))
            {
                if (!EditorUtility.DisplayDialog("Model Exists",
                    $"Model already exists at:\n{ModelPath}\n\nRedownload?", "Yes", "No"))
                    return;
            }
            DownloadModelAsync();
        }

        [MenuItem("Window/UI Prefab Builder/Download CLIP Model", priority = 2001)]
        public static void DownloadModelMenuItem()
        {
            DownloadModel();
        }

        [MenuItem("Window/UI Prefab Builder/Download CLIP Text Model", priority = 2002)]
        public static void DownloadTextModelMenuItem()
        {
            DownloadTextModel();
        }

        public static void DownloadTextModel()
        {
            if (File.Exists(TextModelPath) && File.Exists(VocabPath) && File.Exists(MergesPath))
            {
                if (!EditorUtility.DisplayDialog("Text Model Exists",
                    $"Text model already exists at:\n{TextModelPath}\n\nRedownload?", "Yes", "No"))
                    return;
            }
            DownloadTextModelAsync();
        }

        private static async void DownloadTextModelAsync()
        {
            if (!Directory.Exists(ModelDirectory))
                Directory.CreateDirectory(ModelDirectory);

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            // Download tokenizer files first
            await DownloadFileAsync(client, VocabUrl, VocabPath, "CLIP Vocab");
            await DownloadFileAsync(client, MergesUrl, MergesPath, "CLIP Merges");

            // Download text model
            EditorUtility.DisplayProgressBar("Downloading CLIP Text Model", "Connecting...", 0f);
            ConsoleLogger.Log("[IndexingSetup] Starting CLIP text model download...");

            foreach (var url in TextModelUrls)
            {
                try
                {
                    ConsoleLogger.Log($"[IndexingSetup] Trying text model: {url}");
                    EditorUtility.DisplayProgressBar("Downloading CLIP Text Model", "Downloading...", 0.1f);

                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(TextModelPath, FileMode.Create, FileAccess.Write);

                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;

                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        downloaded += read;

                        if (totalBytes > 0)
                        {
                            float progress = (float)downloaded / totalBytes;
                            var mb = downloaded / (1024f * 1024f);
                            var totalMb = totalBytes / (1024f * 1024f);
                            EditorUtility.DisplayProgressBar(
                                "Downloading CLIP Text Model",
                                $"{mb:F1} / {totalMb:F1} MB",
                                progress);
                        }
                    }

                    EditorUtility.ClearProgressBar();

                    var fileInfo = new FileInfo(TextModelPath);
                    if (fileInfo.Length < 1_000_000)
                    {
                        ConsoleLogger.Warning($"[IndexingSetup] Text model too small ({fileInfo.Length} bytes). Trying next URL...");
                        File.Delete(TextModelPath);
                        continue;
                    }

                    ConsoleLogger.Log($"[IndexingSetup] Text model downloaded: {TextModelPath} ({fileInfo.Length / (1024 * 1024)}MB)");

                    var settings = BuilderSettings.Get();
                    settings.TextModelPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/" + TextModelFileName;
                    settings.TokenizerVocabPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/" + VocabFileName;
                    settings.TokenizerMergesPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/" + MergesFileName;
                    settings.Save();

                    EditorUtility.DisplayDialog("Download Complete",
                        $"CLIP text model downloaded!\n\nYou can now use 'search_sprites_by_text' to search sprites by description.\nExample: 'treasure chest', 'red button', 'gold coin'",
                        "OK");

                    AssetDatabase.Refresh();
                    return;
                }
                catch (Exception e)
                {
                    ConsoleLogger.Warning($"[IndexingSetup] Text model download failed from {url}: {e.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Download Failed",
                "Failed to download CLIP text model.\n\nPlease download manually:\n" +
                "1. Visit: https://huggingface.co/Xenova/clip-vit-base-patch32/tree/main/onnx\n" +
                "2. Download 'text_model.onnx', rename to 'clip_vit_textual.onnx'\n" +
                "3. Download 'vocab.txt' → rename to 'clip_vocab.txt'\n" +
                "4. Download 'merges.txt' → rename to 'clip_merges.txt'\n" +
                $"5. Place all in: {ModelDirectory}",
                "OK");
        }

        private static async Task DownloadFileAsync(HttpClient client, string url, string destPath, string label)
        {
            if (File.Exists(destPath)) return;

            try
            {
                EditorUtility.DisplayProgressBar($"Downloading {label}", "Downloading...", 0.5f);
                var data = await client.GetByteArrayAsync(url);
                File.WriteAllBytes(destPath, data);
                ConsoleLogger.Log($"[IndexingSetup] Downloaded {label}: {destPath} ({data.Length / 1024}KB)");
            }
            catch (Exception e)
            {
                ConsoleLogger.Warning($"[IndexingSetup] Failed to download {label} from {url}: {e.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static async void DownloadModelAsync()
        {
            if (!Directory.Exists(ModelDirectory))
                Directory.CreateDirectory(ModelDirectory);

            EditorUtility.DisplayProgressBar("Downloading CLIP Model", "Connecting...", 0f);
            ConsoleLogger.Log("[IndexingSetup] Starting CLIP model download...");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            foreach (var url in ModelUrls)
            {
                try
                {
                    ConsoleLogger.Log($"[IndexingSetup] Trying: {url}");
                    EditorUtility.DisplayProgressBar("Downloading CLIP Model", "Downloading...", 0.1f);

                    var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1;
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(ModelPath, FileMode.Create, FileAccess.Write);

                    var buffer = new byte[81920];
                    long downloaded = 0;
                    int read;

                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        downloaded += read;

                        if (totalBytes > 0)
                        {
                            float progress = (float)downloaded / totalBytes;
                            var mb = downloaded / (1024f * 1024f);
                            var totalMb = totalBytes / (1024f * 1024f);
                            EditorUtility.DisplayProgressBar(
                                "Downloading CLIP Model",
                                $"{mb:F1} / {totalMb:F1} MB",
                                progress);
                        }
                    }

                    EditorUtility.ClearProgressBar();

                    var fileInfo = new FileInfo(ModelPath);
                    if (fileInfo.Length < 1_000_000)
                    {
                        ConsoleLogger.Warning($"[IndexingSetup] Downloaded file too small ({fileInfo.Length} bytes), might be invalid. Trying next URL...");
                        File.Delete(ModelPath);
                        continue;
                    }

                    ConsoleLogger.Log($"[IndexingSetup] Model downloaded successfully: {ModelPath} ({fileInfo.Length / (1024 * 1024)}MB)");

                    // Update settings to point to the model
                    var settings = BuilderSettings.Get();
                    settings.EmbeddingModelPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/" + ModelFileName;
                    settings.Save();

                    EditorUtility.DisplayDialog("Download Complete",
                        $"CLIP model downloaded successfully!\n\nSize: {fileInfo.Length / (1024 * 1024)} MB\nPath: {ModelPath}\n\nEnable 'Asset Indexing' in Builder Settings to start indexing.",
                        "OK");

                    AssetDatabase.Refresh();
                    return;
                }
                catch (Exception e)
                {
                    ConsoleLogger.Warning($"[IndexingSetup] Download failed from {url}: {e.Message}");
                }
            }

            EditorUtility.ClearProgressBar();
            EditorUtility.DisplayDialog("Download Failed",
                "Failed to download the CLIP model from all sources.\n\n" +
                "Please download manually:\n" +
                "1. Visit: https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx\n" +
                "2. Download 'model_visual.onnx'\n" +
                "3. Rename to 'clip_vit_visual.onnx'\n" +
                $"4. Place in: {ModelDirectory}",
                "OK");
        }

        private static void CopyManualInstructions()
        {
            var instructions =
                "=== Asset Indexing Manual Setup ===\n\n" +
                "1. Install Unity Sentis:\n" +
                "   Window > Package Manager > + > Add package by name\n" +
                "   Name: com.unity.sentis\n" +
                "   Version (for Unity 2021.3): 1.3.0-pre.3\n\n" +
                "2. Download CLIP ViT-B/32 visual encoder ONNX model:\n" +
                "   URL: https://huggingface.co/nickmuchi/clip-vit-base-patch32-onnx/resolve/main/model_visual.onnx\n\n" +
                "3. Rename downloaded file to: clip_vit_visual.onnx\n\n" +
                $"4. Place in: {ModelDirectory}\n\n" +
                "5. In Unity, open Window > UI Prefab Builder\n" +
                "   → Agent Settings > Enable Asset Indexing = ON\n" +
                "   → Click 'Build Index'\n\n" +
                "Alternative ONNX sources:\n" +
                "  - https://huggingface.co/Xenova/clip-vit-base-patch32/tree/main/onnx\n" +
                "  - Export yourself: pip install transformers onnx && python -m transformers.onnx --model openai/clip-vit-base-patch32 output/\n";

            EditorGUIUtility.systemCopyBuffer = instructions;
            ConsoleLogger.Log(instructions);
            Debug.Log("[IndexingSetup] Manual instructions copied to clipboard and printed to console.");
        }

        private static bool IsSentisInstalled()
        {
#if SENTIS_AVAILABLE
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Validates the current setup and returns a status summary.
        /// Called by SettingsPanel for inline status display.
        /// </summary>
        public static (bool sentisOk, bool modelOk, string summary) CheckStatus()
        {
            bool sentis = IsSentisInstalled();
            bool model = File.Exists(ModelPath);

            string summary;
            if (sentis && model)
                summary = "Ready";
            else if (!sentis && !model)
                summary = "Sentis not installed, model not found";
            else if (!sentis)
                summary = "Sentis package not installed";
            else
                summary = "ONNX model not found";

            return (sentis, model, summary);
        }

        public static bool IsTextModelReady =>
            File.Exists(TextModelPath) && File.Exists(VocabPath) && File.Exists(MergesPath);
    }
}

using System.IO;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Indexing
{
    /// <summary>
    /// Editor wizard for asset indexing setup — status checks, Sentis management,
    /// and model lifecycle (download/remove delegated to ModelDownloadManager).
    /// </summary>
    public static class IndexingSetupWizard
    {
        private static string ModelDirectory =>
            Path.GetFullPath("Packages/com.indey.uiprefabbuilder/Editor/Models");

        private static string ModelPath =>
            Path.Combine(ModelDirectory, "clip_vit_visual.onnx");

        private static string TextModelPath =>
            Path.Combine(ModelDirectory, "clip_vit_textual.onnx");

        private static string VocabPath =>
            Path.Combine(ModelDirectory, "clip_vocab.json");

        private static string MergesPath =>
            Path.Combine(ModelDirectory, "clip_merges.txt");

        [MenuItem("Window/UI Prefab Builder/Setup Asset Indexing...", priority = 2000)]
        public static void ShowSetupDialog()
        {
            bool hasSentis = IsSentisInstalled();
            bool hasModel = ModelDownloadManager.IsVisionModelPresent();

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
                msg += "  → Click 'Download Model' in Settings panel\n";
                msg += "  → Or manually download from HuggingFace\n";
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
                    if (!hasModel) ModelDownloadManager.Instance.EnqueueVisionModel();
                    break;
                case 2:
                    CopyManualInstructions();
                    break;
            }
        }

        #region Sentis Management

        public static void InstallSentis()
        {
            ConsoleLogger.Log("[IndexingSetup] Requesting Sentis package installation...");
            UnityEditor.PackageManager.Client.Add("com.unity.sentis");
        }

        public static void RemoveSentis()
        {
            if (!EditorUtility.DisplayDialog("Remove Unity Sentis",
                "This will remove the com.unity.sentis package.\n\n" +
                "Asset indexing will stop working until Sentis is reinstalled.\n" +
                "Unity will recompile all scripts after removal.",
                "Remove", "Cancel"))
                return;

            ConsoleLogger.Log("[IndexingSetup] Requesting Sentis package removal...");
            UnityEditor.PackageManager.Client.Remove("com.unity.sentis");
        }

        #endregion

        #region Model Download (delegates to ModelDownloadManager)

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

        public static void DownloadModel()
        {
            if (ModelDownloadManager.IsVisionModelPresent())
            {
                if (!EditorUtility.DisplayDialog("Model Exists",
                    $"Model already exists at:\n{ModelPath}\n\nRedownload?", "Yes", "No"))
                    return;

                ModelDownloadManager.Instance.RemoveVisionModel();
            }
            ModelDownloadManager.Instance.EnqueueVisionModel();
        }

        public static void DownloadTextModel()
        {
            if (ModelDownloadManager.IsTextModelPresent())
            {
                if (!EditorUtility.DisplayDialog("Text Model Exists",
                    $"Text model already exists at:\n{TextModelPath}\n\nRedownload?", "Yes", "No"))
                    return;

                ModelDownloadManager.Instance.RemoveTextModel();
            }
            ModelDownloadManager.Instance.EnqueueTextModel();
        }

        #endregion

        #region Model Removal

        public static void RemoveVisionModel()
        {
            if (!ModelDownloadManager.IsVisionModelPresent()) return;

            if (!EditorUtility.DisplayDialog("Remove Vision Model",
                "Delete the CLIP vision model file?\n\n" +
                "Asset indexing will not work until the model is re-downloaded.",
                "Remove", "Cancel"))
                return;

            ModelDownloadManager.Instance.RemoveVisionModel();
            AssetDatabase.Refresh();
        }

        public static void RemoveTextModel()
        {
            if (!ModelDownloadManager.IsTextModelPresent()) return;

            if (!EditorUtility.DisplayDialog("Remove Text Model",
                "Delete the CLIP text model and tokenizer files?\n\n" +
                "Text-based sprite search will not work until re-downloaded.",
                "Remove", "Cancel"))
                return;

            ModelDownloadManager.Instance.RemoveTextModel();
            AssetDatabase.Refresh();
        }

        #endregion

        #region Status Queries

        public static (bool sentisOk, bool modelOk, string summary) CheckStatus()
        {
            bool sentis = IsSentisInstalled();
            bool model = ModelDownloadManager.IsVisionModelPresent();

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

        public static bool IsTextModelReady => ModelDownloadManager.IsTextModelPresent();

        public static bool IsSentisInstalled()
        {
#if SENTIS_AVAILABLE
            return true;
#else
            return false;
#endif
        }

        #endregion

        #region Helpers

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

        #endregion
    }
}

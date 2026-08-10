using System.IO;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Config
{
    [FilePath("UIPrefabBuilder/Settings.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class BuilderSettings : ScriptableSingleton<BuilderSettings>
    {
        [SerializeField] private string baseUrl = "https://api.openai.com/v1";
        [SerializeField] private string modelName = "gemini-3.6-flash";
        [SerializeField] private int requestTimeoutSeconds = 60;
        [SerializeField] private int maxRetryCount = 2;
        [SerializeField] private int executeTimeoutSeconds = 5;
        [SerializeField] private bool autoExecute = true;
        [SerializeField] private int maxAgentSteps = 0;
        [SerializeField] private bool enableExtendedThinking = false;
        [SerializeField] private int thinkingBudgetTokens = 4096;
        [SerializeField] private float temperature = -1f;
        [SerializeField] private bool enableVisualVerification = true;
        [SerializeField] private bool supportsVision = false;
        [SerializeField] private bool enableAssetIndexing = false;
        [SerializeField] private string embeddingModelPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/clip_vit_visual.onnx";
        [SerializeField] private string textModelPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/clip_vit_textual.onnx";
        [SerializeField] private string tokenizerVocabPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/clip_vocab.json";
        [SerializeField] private string tokenizerMergesPath = "Packages/com.indey.uiprefabbuilder/Editor/Models/clip_merges.txt";

        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ModelName { get => modelName; set => modelName = value; }
        public int RequestTimeoutSeconds { get => requestTimeoutSeconds; set => requestTimeoutSeconds = value; }
        public int MaxRetryCount { get => maxRetryCount; set => maxRetryCount = value; }
        public int ExecuteTimeoutSeconds { get => executeTimeoutSeconds; set => executeTimeoutSeconds = value; }
        public bool AutoExecute { get => autoExecute; set => autoExecute = value; }
        /// <summary>0 = unlimited (soft cap at 500 for safety)</summary>
        public int MaxAgentSteps { get => maxAgentSteps; set => maxAgentSteps = value; }
        public bool EnableExtendedThinking { get => enableExtendedThinking; set => enableExtendedThinking = value; }
        public int ThinkingBudgetTokens { get => thinkingBudgetTokens; set => thinkingBudgetTokens = value; }
        /// <summary>-1 = not set (use model default)</summary>
        public float Temperature { get => temperature; set => temperature = value; }
        public bool EnableVisualVerification { get => enableVisualVerification; set => enableVisualVerification = value; }
        public bool SupportsVision { get => supportsVision; set => supportsVision = value; }
        public bool EnableAssetIndexing { get => enableAssetIndexing; set => enableAssetIndexing = value; }
        public string EmbeddingModelPath { get => embeddingModelPath; set => embeddingModelPath = value; }
        public string TextModelPath { get => textModelPath; set => textModelPath = value; }
        public string TokenizerVocabPath { get => tokenizerVocabPath; set => tokenizerVocabPath = value; }
        public string TokenizerMergesPath { get => tokenizerMergesPath; set => tokenizerMergesPath = value; }

        public static BuilderSettings Get() => instance;

        /// <summary>Absolute path of Settings.asset inside the Unity Preferences folder.</summary>
        public static string SettingsFilePath => GetFilePath();

        public static string SettingsFolder
        {
            get
            {
                var path = GetFilePath();
                return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
            }
        }

        public void Save() => Save(true);
    }
}

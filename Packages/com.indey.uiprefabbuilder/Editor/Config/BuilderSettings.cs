using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Config
{
    [FilePath("UIPrefabBuilder/Settings.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class BuilderSettings : ScriptableSingleton<BuilderSettings>
    {
        [SerializeField] private string baseUrl = "https://api.openai.com/v1";
        [SerializeField] private string modelName = "gpt-4o-mini";
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

        public static BuilderSettings Get() => instance;

        public void Save() => Save(true);
    }
}

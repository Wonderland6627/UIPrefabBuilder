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
        [SerializeField] private int maxRetryCount = 5;
        [SerializeField] private int executeTimeoutSeconds = 5;
        [SerializeField] private bool autoExecute = true;

        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ModelName { get => modelName; set => modelName = value; }
        public int RequestTimeoutSeconds { get => requestTimeoutSeconds; set => requestTimeoutSeconds = value; }
        public int MaxRetryCount { get => maxRetryCount; set => maxRetryCount = value; }
        public int ExecuteTimeoutSeconds { get => executeTimeoutSeconds; set => executeTimeoutSeconds = value; }
        public bool AutoExecute { get => autoExecute; set => autoExecute = value; }

        public static BuilderSettings Get() => instance;

        public void Save() => Save(true);
    }
}

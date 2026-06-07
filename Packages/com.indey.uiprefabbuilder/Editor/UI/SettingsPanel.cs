using Indey.UIPrefabBuilder.Config;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class SettingsPanel
    {
        private string _apiKeyDisplay = "";
        private bool _showKey;

        public void Draw()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Settings", EditorStyles.boldLabel);
            GUILayout.Space(6);

            var settings = BuilderSettings.Get();

            // LLM Settings
            GUILayout.Label("LLM Configuration", EditorStyles.miniBoldLabel);
            settings.BaseUrl = EditorGUILayout.TextField("Base URL", settings.BaseUrl);
            settings.ModelName = EditorGUILayout.TextField("Model", settings.ModelName);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("API Key", GUILayout.Width(100));
            if (_showKey)
                _apiKeyDisplay = EditorGUILayout.TextField(_apiKeyDisplay);
            else
                EditorGUILayout.PasswordField(_apiKeyDisplay, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(_showKey ? "Hide" : "Show", GUILayout.Width(45)))
            {
                _showKey = !_showKey;
                if (_showKey) _apiKeyDisplay = SecureKeyStore.LoadApiKey();
            }
            if (GUILayout.Button("Save", GUILayout.Width(40)))
            {
                SecureKeyStore.SaveApiKey(_apiKeyDisplay);
                Debug.Log("[UIPrefabBuilder] API Key saved.");
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("Agent Configuration", EditorStyles.miniBoldLabel);
            settings.RequestTimeoutSeconds = EditorGUILayout.IntField("Timeout (s)", settings.RequestTimeoutSeconds);
            settings.MaxRetryCount = EditorGUILayout.IntField("Max Retries", settings.MaxRetryCount);
            settings.ExecuteTimeoutSeconds = EditorGUILayout.IntField("Exec Timeout (s)", settings.ExecuteTimeoutSeconds);
            settings.AutoExecute = EditorGUILayout.Toggle("Auto Execute", settings.AutoExecute);

            GUILayout.Space(8);
            if (GUILayout.Button("Save Settings")) settings.Save();

            EditorGUILayout.EndVertical();
        }
    }
}

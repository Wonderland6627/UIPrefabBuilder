using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class SettingsPanel
    {
        private string _apiKeyDisplay = "";
        private bool _showKey;
        private bool _keyLoaded;

        private void EnsureKeyLoaded()
        {
            if (_keyLoaded) return;
            _keyLoaded = true;
            _apiKeyDisplay = SecureKeyStore.LoadApiKey();
        }

        public void DrawLLMSettings()
        {
            EnsureKeyLoaded();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("LLM Configuration", EditorStyles.miniBoldLabel);
            GUILayout.Space(4);

            var settings = BuilderSettings.Get();

            settings.BaseUrl = EditorGUILayout.TextField("Base URL", settings.BaseUrl);
            settings.ModelName = EditorGUILayout.TextField("Model", settings.ModelName);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("API Key", GUILayout.Width(60));
            if (_showKey)
                _apiKeyDisplay = EditorGUILayout.TextField(_apiKeyDisplay);
            else
                _apiKeyDisplay = EditorGUILayout.PasswordField(_apiKeyDisplay, GUILayout.ExpandWidth(true));
            if (GUILayout.Button(_showKey ? "Hide" : "Show", EditorStyles.miniButton, GUILayout.Width(40)))
                _showKey = !_showKey;
            EditorGUILayout.EndHorizontal();

            settings.Temperature = EditorGUILayout.FloatField("Temperature (-1=default)", settings.Temperature);
            settings.SupportsVision = EditorGUILayout.Toggle("Supports Vision", settings.SupportsVision);

            GUILayout.Space(2);
            if (GUILayout.Button("Save LLM Settings", EditorStyles.miniButton))
            {
                SecureKeyStore.SaveApiKey(_apiKeyDisplay);
                settings.Save();
                ConsoleLogger.Log("LLM settings saved");
            }

            EditorGUILayout.EndVertical();
        }

        public void DrawAgentSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Agent Configuration", EditorStyles.miniBoldLabel);
            GUILayout.Space(4);

            var settings = BuilderSettings.Get();

            settings.RequestTimeoutSeconds = EditorGUILayout.IntField("Timeout (s)", settings.RequestTimeoutSeconds);
            settings.MaxAgentSteps = EditorGUILayout.IntField("Max Steps (0=∞)", settings.MaxAgentSteps);
            settings.MaxRetryCount = EditorGUILayout.IntField("Max Retries", settings.MaxRetryCount);
            settings.ExecuteTimeoutSeconds = EditorGUILayout.IntField("Exec Timeout (s)", settings.ExecuteTimeoutSeconds);
            settings.AutoExecute = EditorGUILayout.Toggle("Auto Execute", settings.AutoExecute);

            GUILayout.Space(4);
            GUILayout.Label("Thinking", EditorStyles.miniBoldLabel);
            settings.EnableExtendedThinking = EditorGUILayout.Toggle("Extended Thinking (Claude)", settings.EnableExtendedThinking);
            if (settings.EnableExtendedThinking)
                settings.ThinkingBudgetTokens = EditorGUILayout.IntField("Thinking Budget", settings.ThinkingBudgetTokens);

            GUILayout.Space(4);
            GUILayout.Label("Vision", EditorStyles.miniBoldLabel);
            settings.EnableVisualVerification = EditorGUILayout.Toggle("Visual Verification", settings.EnableVisualVerification);

            GUILayout.Space(2);
            if (GUILayout.Button("Save Agent Settings", EditorStyles.miniButton))
            {
                settings.Save();
                ConsoleLogger.Log("Agent settings saved");
            }

            EditorGUILayout.EndVertical();
        }

        public void Draw()
        {
            DrawLLMSettings();
            GUILayout.Space(6);
            DrawAgentSettings();
        }
    }
}

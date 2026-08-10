using System;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Indexing;
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
        private bool _foldIndexDirs = true;
        private bool _foldIgnorePatterns;
        private Vector2 _scroll;
        private bool _repaintSubscribed;

        private void EnsureKeyLoaded()
        {
            if (_keyLoaded) return;
            _keyLoaded = true;
            _apiKeyDisplay = SecureKeyStore.LoadApiKey();
        }

        private void EnsureRepaintOnDownload()
        {
            if (_repaintSubscribed) return;
            _repaintSubscribed = true;
            ModelDownloadManager.Instance.OnStateChanged += RequestRepaint;
        }

        private static void RequestRepaint()
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (w.GetType().Name.Contains("BuilderWindow"))
                    w.Repaint();
            }
        }

        #region LLM Configuration

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

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Agent Configuration

        public void DrawAgentSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Agent Configuration", EditorStyles.miniBoldLabel);
            GUILayout.Space(4);

            var settings = BuilderSettings.Get();

            settings.RequestTimeoutSeconds = EditorGUILayout.IntField("Timeout (s)", settings.RequestTimeoutSeconds);
            settings.MaxAgentSteps = EditorGUILayout.IntField("Max Steps (0=\u221e)", settings.MaxAgentSteps);
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

            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Asset Indexing

        public void DrawAssetIndexing()
        {
            EnsureRepaintOnDownload();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Asset Indexing", EditorStyles.miniBoldLabel);
            GUILayout.Space(4);

            var settings = BuilderSettings.Get();
            settings.EnableAssetIndexing = EditorGUILayout.Toggle("Enable Asset Indexing", settings.EnableAssetIndexing);

            if (!settings.EnableAssetIndexing)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);
            DrawDependencies(settings);
            GUILayout.Space(4);
            DrawIndexDirectories();
            GUILayout.Space(4);
            DrawIgnorePatterns();
            GUILayout.Space(4);
            DrawSearchParameters();
            GUILayout.Space(4);
            DrawIndexActions();

            EditorGUILayout.EndVertical();
        }

        private void DrawDependencies(BuilderSettings settings)
        {
            GUILayout.Label("Dependencies", EditorStyles.miniBoldLabel);

            var dm = ModelDownloadManager.Instance;
            var sentisOk = IndexingSetupWizard.IsSentisInstalled();
            var visionOk = ModelDownloadManager.IsVisionModelPresent();
            var textOk = ModelDownloadManager.IsTextModelPresent();
            var prevColor = GUI.color;

            // ── Sentis ──
            EditorGUILayout.BeginHorizontal();
            GUI.color = sentisOk ? new Color(0.4f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.4f);
            GUILayout.Label(sentisOk ? "\u2713" : "\u2717", GUILayout.Width(16));
            GUI.color = prevColor;
            GUILayout.Label("Unity Sentis", EditorStyles.miniLabel, GUILayout.Width(90));
            GUILayout.Label(sentisOk ? "Installed" : "Not installed", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (sentisOk)
            {
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    IndexingSetupWizard.RemoveSentis();
            }
            else
            {
                if (GUILayout.Button("Install", EditorStyles.miniButton, GUILayout.Width(55)))
                    IndexingSetupWizard.InstallSentis();
            }
            EditorGUILayout.EndHorizontal();

            // ── Vision Model ──
            DrawModelRow(
                "CLIP Vision",
                visionOk,
                dm.IsDownloading && dm.CurrentTask == DownloadTaskType.VisionModel,
                ModelDownloadManager.GetVisionModelSize(),
                () => dm.EnqueueVisionModel(),
                () => dm.CancelCurrent(),
                () => IndexingSetupWizard.RemoveVisionModel());

            // ── Text Model ──
            DrawModelRow(
                "CLIP Text",
                textOk,
                dm.IsDownloading && dm.CurrentTask == DownloadTaskType.TextModel,
                ModelDownloadManager.GetTextModelSize(),
                () => dm.EnqueueTextModel(),
                () => dm.CancelCurrent(),
                () => IndexingSetupWizard.RemoveTextModel());

            // ── Download Progress Bar (shared) ──
            if (dm.IsDownloading)
            {
                GUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                var rect = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(rect, dm.Progress, BuildProgressLabel(dm));
                EditorGUILayout.EndHorizontal();

                if (dm.QueueCount > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(16);
                    GUILayout.Label($"{dm.QueueCount} task(s) queued", EditorStyles.miniLabel);
                    if (GUILayout.Button("Cancel All", EditorStyles.miniButton, GUILayout.Width(65)))
                        dm.CancelAll();
                    EditorGUILayout.EndHorizontal();
                }
            }

            // ── Status Messages ──
            if (dm.State == DownloadState.Failed && !string.IsNullOrEmpty(dm.LastError))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.4f, 0.3f) } };
                GUILayout.Label($"Last error: {dm.LastError}", style);
                EditorGUILayout.EndHorizontal();
            }

            // ── Model path (editable) ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label("Path:", EditorStyles.miniLabel, GUILayout.Width(30));
            settings.EmbeddingModelPath = EditorGUILayout.TextField(settings.EmbeddingModelPath);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModelRow(string label, bool isReady, bool isDownloading, long fileSize,
            Action onDownload, Action onCancel, Action onRemove)
        {
            var prevColor = GUI.color;
            var dm = ModelDownloadManager.Instance;

            EditorGUILayout.BeginHorizontal();

            // Status icon
            if (isDownloading)
            {
                GUI.color = new Color(0.5f, 0.8f, 1f);
                GUILayout.Label("\u21bb", GUILayout.Width(16));
            }
            else if (isReady)
            {
                GUI.color = new Color(0.4f, 0.9f, 0.4f);
                GUILayout.Label("\u2713", GUILayout.Width(16));
            }
            else
            {
                GUI.color = new Color(1f, 0.5f, 0.4f);
                GUILayout.Label("\u2717", GUILayout.Width(16));
            }
            GUI.color = prevColor;

            // Label
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(90));

            // Status text
            if (isDownloading)
                GUILayout.Label("Downloading...", EditorStyles.miniLabel);
            else if (isReady)
                GUILayout.Label($"Ready ({FormatFileSize(fileSize)})", EditorStyles.miniLabel);
            else
                GUILayout.Label("Not found", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            // Action buttons
            if (isDownloading)
            {
                if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(55)))
                    onCancel?.Invoke();
            }
            else if (isReady)
            {
                GUI.enabled = !dm.IsDownloading;
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    onRemove?.Invoke();
                GUI.enabled = true;
            }
            else
            {
                GUI.enabled = !dm.IsDownloading;
                if (GUILayout.Button("Download", EditorStyles.miniButton, GUILayout.Width(65)))
                    onDownload?.Invoke();
                GUI.enabled = true;
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string BuildProgressLabel(ModelDownloadManager dm)
        {
            var pct = (dm.Progress * 100f).ToString("F1") + "%";
            if (dm.SpeedMBps > 0.01f)
                return $"{dm.ProgressText}  ({pct} - {dm.SpeedMBps:F1} MB/s)";
            return $"{dm.ProgressText}  ({pct})";
        }

        private void DrawIndexDirectories()
        {
            var ic = GetOrCreateIndexingConfig();
            if (ic == null) return;

            _foldIndexDirs = EditorGUILayout.Foldout(_foldIndexDirs,
                $"Index Directories ({ic.indexDirectories.Count})", true, EditorStyles.foldoutHeader);
            if (!_foldIndexDirs) return;

            EditorGUI.indentLevel++;

            int removeIdx = -1;
            for (int i = 0; i < ic.indexDirectories.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                ic.indexDirectories[i] = EditorGUILayout.TextField(ic.indexDirectories[i]);
                if (GUILayout.Button("...", EditorStyles.miniButton, GUILayout.Width(24)))
                {
                    var selected = EditorUtility.OpenFolderPanel("Select Index Directory", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        ic.indexDirectories[i] = ToRelativeAssetPath(selected);
                        ProjectConfig.Save();
                    }
                }
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(20)))
                    removeIdx = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                ic.indexDirectories.RemoveAt(removeIdx);
                ProjectConfig.Save();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Directory", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                var selected = EditorUtility.OpenFolderPanel("Select Index Directory", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    var relative = ToRelativeAssetPath(selected);
                    if (!ic.indexDirectories.Contains(relative))
                    {
                        ic.indexDirectories.Add(relative);
                        ProjectConfig.Save();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private void DrawIgnorePatterns()
        {
            var ic = GetOrCreateIndexingConfig();
            if (ic == null) return;

            _foldIgnorePatterns = EditorGUILayout.Foldout(_foldIgnorePatterns,
                $"Ignore Patterns ({ic.ignorePatterns.Count})", true, EditorStyles.foldoutHeader);
            if (!_foldIgnorePatterns) return;

            EditorGUI.indentLevel++;

            int removeIdx = -1;
            for (int i = 0; i < ic.ignorePatterns.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                ic.ignorePatterns[i] = EditorGUILayout.TextField(ic.ignorePatterns[i]);
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(20)))
                    removeIdx = i;
                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                ic.ignorePatterns.RemoveAt(removeIdx);
                ProjectConfig.Save();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Add Pattern", EditorStyles.miniButton, GUILayout.Width(100)))
            {
                ic.ignorePatterns.Add("*.png");
                ProjectConfig.Save();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private void DrawSearchParameters()
        {
            var ic = GetOrCreateIndexingConfig();
            if (ic == null) return;

            GUILayout.Label("Search Parameters", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            ic.topKDefault = EditorGUILayout.IntField("Top K Results", ic.topKDefault);
            ic.minConfidenceThreshold = EditorGUILayout.Slider("Min Confidence", ic.minConfidenceThreshold, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
                ProjectConfig.Save();
        }

        private void DrawIndexActions()
        {
            GUILayout.Label("Index Status", EditorStyles.miniBoldLabel);

            var indexer = AssetIndexer.Instance;
            var indexDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath),
                "Library", "UIPrefabBuilder", "Index");
            var metaFile = System.IO.Path.Combine(indexDir, "AssetIndexMeta.json");
            var vecFile = System.IO.Path.Combine(indexDir, "AssetVectors.bytes");

            EditorGUILayout.BeginHorizontal();
            var prevColor = GUI.color;
            if (indexer.IsReady)
            {
                GUI.color = new Color(0.4f, 0.9f, 0.4f);
                GUILayout.Label("\u2713", GUILayout.Width(16));
                GUI.color = prevColor;
                GUILayout.Label($"Indexed: {indexer.IndexedCount} assets", EditorStyles.miniLabel);
            }
            else
            {
                GUI.color = new Color(1f, 0.7f, 0.3f);
                GUILayout.Label("\u2717", GUILayout.Width(16));
                GUI.color = prevColor;
                GUILayout.Label("Not initialized", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            long metaSize = System.IO.File.Exists(metaFile) ? new System.IO.FileInfo(metaFile).Length : 0;
            long vecSize = System.IO.File.Exists(vecFile) ? new System.IO.FileInfo(vecFile).Length : 0;
            long totalSize = metaSize + vecSize;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUILayout.Label($"Storage: {FormatFileSize(totalSize)}", EditorStyles.miniLabel);
            GUILayout.Label($"(Meta: {FormatFileSize(metaSize)}, Vectors: {FormatFileSize(vecSize)})",
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (indexer.LastBuildTimestamp > 0)
            {
                var buildTime = DateTimeOffset.FromUnixTimeSeconds(indexer.LastBuildTimestamp).LocalDateTime;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(16);
                GUILayout.Label($"Last built: {buildTime:yyyy-MM-dd HH:mm:ss}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();

            if (indexer.IsIndexing)
            {
                GUI.enabled = false;
                GUILayout.Button("Building...", EditorStyles.miniButton);
                GUI.enabled = true;
                if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(55)))
                    indexer.Cancel();
            }
            else
            {
                if (GUILayout.Button(indexer.IndexedCount > 0 ? "Rebuild Index" : "Build Index",
                    EditorStyles.miniButton))
                {
                    indexer.EnsureInitialized();
                    indexer.RebuildFullIndex();
                }
            }

            if (GUILayout.Button("Open Folder", EditorStyles.miniButton, GUILayout.Width(80)))
            {
                if (!System.IO.Directory.Exists(indexDir))
                    System.IO.Directory.CreateDirectory(indexDir);
                EditorUtility.RevealInFinder(indexDir);
            }

            GUI.enabled = indexer.IndexedCount > 0 && !indexer.IsIndexing;
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                if (EditorUtility.DisplayDialog("Clear Index",
                    $"Delete all {indexer.IndexedCount} indexed entries?\n\n" +
                    $"This will free {FormatFileSize(totalSize)} of storage.",
                    "Clear", "Cancel"))
                {
                    if (System.IO.File.Exists(metaFile)) System.IO.File.Delete(metaFile);
                    if (System.IO.File.Exists(vecFile)) System.IO.File.Delete(vecFile);
                    indexer.Dispose();
                    ConsoleLogger.Log("[AssetIndexer] Index cleared.");
                }
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
            return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
        }

        #endregion

        #region Helpers

        private void DrawSettingsToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Open Config Folder", EditorStyles.toolbarButton, GUILayout.Width(120)))
                OpenBuilderSettingsFolder();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Save Settings", EditorStyles.toolbarButton, GUILayout.Width(90)))
                SaveAllSettings();

            EditorGUILayout.EndHorizontal();
        }

        private void SaveAllSettings()
        {
            EnsureKeyLoaded();
            SecureKeyStore.SaveApiKey(_apiKeyDisplay);
            BuilderSettings.Get().Save();
            ProjectConfig.Save();
            ConsoleLogger.Log("Settings saved");
        }

        private static void OpenBuilderSettingsFolder()
        {
            BuilderSettings.Get().Save();
            var dir = BuilderSettings.SettingsFolder;
            if (string.IsNullOrEmpty(dir)) return;
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        private static IndexingConfig GetOrCreateIndexingConfig()
        {
            var config = ProjectConfig.Current;
            if (config == null) return null;

            if (config.indexingConfig == null)
                config.indexingConfig = new IndexingConfig();
            return config.indexingConfig;
        }

        private static string ToRelativeAssetPath(string absolutePath)
        {
            var dataPath = Application.dataPath.Replace('\\', '/');
            var selected = absolutePath.Replace('\\', '/');

            if (selected.StartsWith(dataPath))
                return "Assets" + selected.Substring(dataPath.Length);
            if (selected.Contains("/Assets"))
                return "Assets" + selected.Substring(selected.IndexOf("/Assets") + 7);
            return selected;
        }

        #endregion

        #region Draw Entry Points

        public void Draw(Rect rect)
        {
            GUILayout.BeginArea(rect);
            DrawSettingsToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawLLMSettings();
            GUILayout.Space(6);
            DrawAgentSettings();
            GUILayout.Space(6);
            DrawAssetIndexing();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        public void Draw()
        {
            DrawSettingsToolbar();
            DrawLLMSettings();
            GUILayout.Space(6);
            DrawAgentSettings();
            GUILayout.Space(6);
            DrawAssetIndexing();
        }

        public void DrawIndexingProgress(AssetIndexer indexer)
        {
            if (indexer == null || !indexer.IsIndexing) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label("\u26a1 Indexing in progress...", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}

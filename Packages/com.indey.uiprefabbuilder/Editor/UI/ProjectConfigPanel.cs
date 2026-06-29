using System.Collections.Generic;
using System.IO;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Indexing;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class ProjectConfigPanel
    {
        private Vector2 _scroll;
        private bool _dirty;

        private bool _foldBasic = true;
        private bool _foldSprites = true;
        private bool _foldPrefabs = true;
        private bool _foldComponents;
        private bool _foldRules;
        private bool _foldIndexing;
        private Vector2 _notesScroll;
        private readonly List<Vector2> _ruleScrolls = new List<Vector2>();

        public void Draw(Rect rect)
        {
            var config = ProjectConfig.Current;

            GUILayout.BeginArea(rect);
            DrawToolbar(config);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawBasicInfo(config);
            GUILayout.Space(2);
            DrawSpriteMapping(config);
            GUILayout.Space(2);
            DrawPrefabMapping(config);
            GUILayout.Space(2);
            DrawComponentOverrides(config);
            GUILayout.Space(2);
            DrawRules(config);
            GUILayout.Space(2);
            DrawIndexingConfig(config);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawToolbar(ProjectConfig config)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Project Config", EditorStyles.miniBoldLabel, GUILayout.Width(85));

            if (_dirty)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.3f);
                GUILayout.Label("*", EditorStyles.miniBoldLabel, GUILayout.Width(8));
                GUI.color = prev;
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(38)))
            {
                ProjectConfig.Save();
                _dirty = false;
            }
            if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                var path = EditorUtility.SaveFilePanel("Export Project Config", "", "UIPrefabBuilderConfig.json", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    ProjectConfig.Export(path);
                    EditorUtility.RevealInFinder(path);
                }
            }
            if (GUILayout.Button("Import", EditorStyles.toolbarButton, GUILayout.Width(48)))
            {
                var path = EditorUtility.OpenFilePanel("Import Project Config", "", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    ProjectConfig.Import(path);
                    _dirty = true;
                }
            }
            if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                if (EditorUtility.DisplayDialog("Reset Project Config", "Reset all project configuration to defaults?", "Reset", "Cancel"))
                {
                    ProjectConfig.Reset();
                    _dirty = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        #region Basic Info

        private void DrawBasicInfo(ProjectConfig config)
        {
            _foldBasic = EditorGUILayout.Foldout(_foldBasic, "Basic Info", true, EditorStyles.foldoutHeader);
            if (!_foldBasic) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            config.basicInfo.orientation = (BasicInfo.ScreenOrientation)EditorGUILayout.EnumPopup("Orientation", config.basicInfo.orientation);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Resolution", GUILayout.Width(EditorGUIUtility.labelWidth));
            config.basicInfo.designWidth = EditorGUILayout.IntField(config.basicInfo.designWidth, GUILayout.Width(55));
            GUILayout.Label("x", GUILayout.Width(10));
            config.basicInfo.designHeight = EditorGUILayout.IntField(config.basicInfo.designHeight, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();

            config.basicInfo.screenMatchMode = (BasicInfo.CanvasMatchMode)EditorGUILayout.EnumPopup("Screen Match Mode", config.basicInfo.screenMatchMode);
            if (config.basicInfo.screenMatchMode == BasicInfo.CanvasMatchMode.MatchWidthOrHeight)
                config.basicInfo.canvasMatchMode = EditorGUILayout.Slider("Match W/H", config.basicInfo.canvasMatchMode, 0f, 1f);

            GUILayout.Label("Notes", EditorStyles.miniLabel);
            var noteStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            var notes = config.basicInfo.projectNotes ?? "";
            var noteLines = notes.Split('\n').Length;
            var noteContentHeight = noteLines * 16f + 8f;
            var noteVisibleHeight = Mathf.Clamp(noteContentHeight, 48f, 150f);

            if (noteContentHeight > noteVisibleHeight)
            {
                _notesScroll = EditorGUILayout.BeginScrollView(
                    _notesScroll, GUILayout.Height(noteVisibleHeight), GUILayout.ExpandWidth(true));
                config.basicInfo.projectNotes = EditorGUILayout.TextArea(notes, noteStyle, GUILayout.Height(noteContentHeight), GUILayout.ExpandWidth(true));
                EditorGUILayout.EndScrollView();
            }
            else
            {
                config.basicInfo.projectNotes = EditorGUILayout.TextArea(notes, noteStyle, GUILayout.Height(noteVisibleHeight), GUILayout.ExpandWidth(true));
            }

            if (EditorGUI.EndChangeCheck()) _dirty = true;
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Sprite Mapping

        private void DrawSpriteMapping(ProjectConfig config)
        {
            EditorGUILayout.BeginHorizontal();
            _foldSprites = EditorGUILayout.Foldout(_foldSprites, $"Sprite Mapping ({config.spriteMapping.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                config.spriteMapping.Add(new SpriteEntry());
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!_foldSprites) return;

            int removeIndex = -1;
            for (int i = 0; i < config.spriteMapping.Count; i++)
            {
                var entry = config.spriteMapping[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                entry.role = EditorGUILayout.TextField("Role", entry.role);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Sprite", GUILayout.Width(EditorGUIUtility.labelWidth));

                var currentSprite = string.IsNullOrEmpty(entry.assetPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Sprite>(entry.assetPath);
                var newSprite = (Sprite)EditorGUILayout.ObjectField(currentSprite, typeof(Sprite), false);
                if (newSprite != currentSprite)
                    entry.assetPath = newSprite != null ? AssetDatabase.GetAssetPath(newSprite) : "";
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(entry.assetPath))
                    EditorGUILayout.LabelField("Path", entry.assetPath, EditorStyles.miniLabel);

                entry.description = EditorGUILayout.TextField("Desc", entry.description);
                entry.imageType = ImageTypePopup("Image Type", entry.imageType);

                if (EditorGUI.EndChangeCheck()) _dirty = true;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0) { config.spriteMapping.RemoveAt(removeIndex); _dirty = true; }
        }

        private static readonly string[] ImageTypeOptions = { "Simple", "Sliced", "Tiled", "Filled" };

        private static string ImageTypePopup(string label, string current)
        {
            int idx = System.Array.IndexOf(ImageTypeOptions, current);
            if (idx < 0) idx = 0;
            int newIdx = EditorGUILayout.Popup(label, idx, ImageTypeOptions);
            return ImageTypeOptions[newIdx];
        }

        #endregion

        #region Prefab Mapping

        private void DrawPrefabMapping(ProjectConfig config)
        {
            EditorGUILayout.BeginHorizontal();
            _foldPrefabs = EditorGUILayout.Foldout(_foldPrefabs, $"Prefab Mapping ({config.prefabMapping.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                config.prefabMapping.Add(new PrefabEntry());
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!_foldPrefabs) return;

            int removeIndex = -1;
            for (int i = 0; i < config.prefabMapping.Count; i++)
            {
                var entry = config.prefabMapping[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                entry.role = EditorGUILayout.TextField("Role", entry.role);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Prefab", GUILayout.Width(EditorGUIUtility.labelWidth));
                var currentPrefab = string.IsNullOrEmpty(entry.prefabPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(entry.prefabPath);
                var newPrefab = (GameObject)EditorGUILayout.ObjectField(currentPrefab, typeof(GameObject), false);
                if (newPrefab != currentPrefab)
                    entry.prefabPath = newPrefab != null ? AssetDatabase.GetAssetPath(newPrefab) : "";
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(entry.prefabPath))
                    EditorGUILayout.LabelField("Path", entry.prefabPath, EditorStyles.miniLabel);

                entry.description = EditorGUILayout.TextField("Desc", entry.description);

                if (EditorGUI.EndChangeCheck()) _dirty = true;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0) { config.prefabMapping.RemoveAt(removeIndex); _dirty = true; }
        }

        #endregion

        #region Component Overrides

        private void DrawComponentOverrides(ProjectConfig config)
        {
            _foldComponents = EditorGUILayout.Foldout(_foldComponents, "Component Overrides", true, EditorStyles.foldoutHeader);
            if (!_foldComponents) return;

            var ov = config.componentOverrides;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            ov.textComponent = EditorGUILayout.TextField("Text", ov.textComponent);
            ov.buttonComponent = EditorGUILayout.TextField("Button", ov.buttonComponent);
            ov.imageComponent = EditorGUILayout.TextField("Image", ov.imageComponent);
            ov.inputFieldComponent = EditorGUILayout.TextField("InputField", ov.inputFieldComponent);

            if (EditorGUI.EndChangeCheck()) _dirty = true;

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Custom Components", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                ov.customComponents.Add(new CustomComponentEntry());
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            int removeIndex = -1;
            for (int i = 0; i < ov.customComponents.Count; i++)
            {
                var c = ov.customComponents[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                c.role = EditorGUILayout.TextField("Role", c.role);
                c.typeName = EditorGUILayout.TextField("Type", c.typeName);
                c.description = EditorGUILayout.TextField("Desc", c.description);

                if (EditorGUI.EndChangeCheck()) _dirty = true;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0) { ov.customComponents.RemoveAt(removeIndex); _dirty = true; }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Rules

        private void DrawRules(ProjectConfig config)
        {
            EditorGUILayout.BeginHorizontal();
            _foldRules = EditorGUILayout.Foldout(_foldRules, $"Rules ({config.rules.Count})", true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                config.rules.Add("");
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            if (!_foldRules) return;

            EditorGUILayout.HelpBox(
                "Each rule is injected into the AI Agent's system prompt.\n" +
                "Write conventions, coding standards, or project-specific instructions.",
                MessageType.Info);
            GUILayout.Space(2);

            var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            int removeIndex = -1;

            // Ensure scroll list stays in sync
            while (_ruleScrolls.Count < config.rules.Count) _ruleScrolls.Add(Vector2.zero);
            while (_ruleScrolls.Count > config.rules.Count) _ruleScrolls.RemoveAt(_ruleScrolls.Count - 1);

            for (int i = 0; i < config.rules.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                var text = config.rules[i] ?? "";
                var lines = text.Split('\n').Length;
                var contentHeight = lines * 16f + 8f;
                var visibleHeight = Mathf.Clamp(contentHeight, 48f, 200f);

                if (contentHeight > visibleHeight)
                {
                    _ruleScrolls[i] = EditorGUILayout.BeginScrollView(
                        _ruleScrolls[i], GUILayout.Height(visibleHeight), GUILayout.ExpandWidth(true));
                    config.rules[i] = EditorGUILayout.TextArea(text, style, GUILayout.Height(contentHeight), GUILayout.ExpandWidth(true));
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    config.rules[i] = EditorGUILayout.TextArea(text, style, GUILayout.Height(visibleHeight), GUILayout.ExpandWidth(true));
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck()) _dirty = true;
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
            {
                config.rules.RemoveAt(removeIndex);
                if (removeIndex < _ruleScrolls.Count) _ruleScrolls.RemoveAt(removeIndex);
                _dirty = true;
            }
        }

        #endregion

        #region Indexing Config

        private void DrawIndexingConfig(ProjectConfig config)
        {
            _foldIndexing = EditorGUILayout.Foldout(_foldIndexing, "Asset Indexing", true, EditorStyles.foldoutHeader);
            if (!_foldIndexing) return;

            var ic = config.indexingConfig;
            if (ic == null)
            {
                config.indexingConfig = new IndexingConfig();
                ic = config.indexingConfig;
                _dirty = true;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            // Index Directories
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Index Directories", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                ic.indexDirectories.Add("Assets/");
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            int removeDirIdx = -1;
            for (int i = 0; i < ic.indexDirectories.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                ic.indexDirectories[i] = EditorGUILayout.TextField(ic.indexDirectories[i]);
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(20)))
                    removeDirIdx = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeDirIdx >= 0) { ic.indexDirectories.RemoveAt(removeDirIdx); _dirty = true; }

            GUILayout.Space(4);

            // Ignore Patterns
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Ignore Patterns", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20)))
            {
                ic.ignorePatterns.Add("");
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            int removePatIdx = -1;
            for (int i = 0; i < ic.ignorePatterns.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                ic.ignorePatterns[i] = EditorGUILayout.TextField(ic.ignorePatterns[i]);
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(20)))
                    removePatIdx = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removePatIdx >= 0) { ic.ignorePatterns.RemoveAt(removePatIdx); _dirty = true; }

            GUILayout.Space(4);

            // Defaults
            ic.topKDefault = EditorGUILayout.IntField("Default Top-K", ic.topKDefault);
            ic.minConfidenceThreshold = EditorGUILayout.Slider("Min Confidence", ic.minConfidenceThreshold, 0f, 1f);

            if (EditorGUI.EndChangeCheck()) _dirty = true;
            EditorGUILayout.EndVertical();
        }

        #endregion
    }
}

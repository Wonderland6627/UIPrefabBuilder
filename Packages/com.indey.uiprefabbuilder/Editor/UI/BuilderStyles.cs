using System;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    /// <summary>
    /// Shared IMGUI theme for UIPrefabBuilder editor panels.
    /// </summary>
    public static class BuilderStyles
    {
        public const float SectionGap = 4f;
        public const float SubGap = 4f;
        public const float AddIconWidth = 20f;
        public const float RemoveIconWidth = 20f;
        public const float AddLabeledWidth = 100f;

        public static readonly Color Success = new Color(0.4f, 0.9f, 0.4f);
        public static readonly Color Warning = new Color(1f, 0.85f, 0.2f);
        public static readonly Color Error = new Color(1f, 0.35f, 0.35f);
        public static readonly Color Accent = new Color(0.3f, 0.6f, 1f);
        public static readonly Color Dirty = new Color(1f, 0.8f, 0.3f);
        public static readonly Color InfoBlue = new Color(0.5f, 0.8f, 1f);
        public static readonly Color Splitter = new Color(0.15f, 0.15f, 0.15f, 1f);
        public static readonly Color SplitterActive = new Color(0.3f, 0.6f, 1f, 0.8f);

        public static readonly Color LogTimestamp = new Color(0.5f, 0.5f, 0.5f);
        public static readonly Color LogInfo = new Color(0.78f, 0.78f, 0.78f);
        public static readonly Color ToolCallText = new Color(0.6f, 0.8f, 0.9f);

        public static readonly Color BubbleUser = new Color(0.15f, 0.38f, 0.75f, 0.35f);
        public static readonly Color BubbleAI = new Color(0.22f, 0.22f, 0.28f, 0.35f);
        public static readonly Color BubbleThink = new Color(0.5f, 0.4f, 0.7f, 0.2f);
        public static readonly Color BubbleCode = new Color(0.1f, 0.1f, 0.15f, 0.4f);
        public static readonly Color BubbleResult = new Color(0.1f, 0.5f, 0.2f, 0.25f);
        public static readonly Color BubbleError = new Color(0.55f, 0.15f, 0.15f, 0.35f);
        public static readonly Color BubbleToolCall = new Color(0.12f, 0.3f, 0.45f, 0.25f);

        public static readonly Color StateIdle = Color.gray;
        public static readonly Color StateThinking = Accent;
        public static readonly Color StateTool = new Color(1f, 0.7f, 0.2f);
        public static readonly Color StateConfirm = new Color(0.2f, 0.9f, 0.4f);
        public static readonly Color StateDone = new Color(0.2f, 0.8f, 0.2f);
        public static readonly Color StateError = Color.red;
        public static readonly Color StateContext = new Color(0.4f, 0.7f, 1f);
        public static readonly Color StateExtract = new Color(0.5f, 0.5f, 1f);
        public static readonly Color StateCompile = new Color(1f, 0.8f, 0.2f);
        public static readonly Color StateExecute = new Color(1f, 0.6f, 0.2f);

        private static bool _stylesInit;
        private static GUIStyle _panelTitle;
        private static GUIStyle _sectionLabel;
        private static GUIStyle _wrappedTextArea;
        private static GUIStyle _errorLabel;
        private static GUIStyle _logTimestamp;
        private static GUIStyle _logInfo;
        private static GUIStyle _logWarn;
        private static GUIStyle _logError;

        public static GUIStyle PanelTitle
        {
            get
            {
                EnsureStyles();
                return _panelTitle;
            }
        }

        public static GUIStyle SectionLabel
        {
            get
            {
                EnsureStyles();
                return _sectionLabel;
            }
        }

        public static GUIStyle WrappedTextArea
        {
            get
            {
                EnsureStyles();
                return _wrappedTextArea;
            }
        }

        public static GUIStyle ErrorLabel
        {
            get
            {
                EnsureStyles();
                return _errorLabel;
            }
        }

        public static GUIStyle LogTimestampStyle
        {
            get
            {
                EnsureStyles();
                return _logTimestamp;
            }
        }

        public static GUIStyle LogInfoStyle
        {
            get
            {
                EnsureStyles();
                return _logInfo;
            }
        }

        public static GUIStyle LogWarnStyle
        {
            get
            {
                EnsureStyles();
                return _logWarn;
            }
        }

        public static GUIStyle LogErrorStyle
        {
            get
            {
                EnsureStyles();
                return _logError;
            }
        }

        public static void EnsureStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _panelTitle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
            _sectionLabel = new GUIStyle(EditorStyles.miniBoldLabel);
            _wrappedTextArea = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            _errorLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = Error }
            };
            _logTimestamp = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = LogTimestamp },
                alignment = TextAnchor.MiddleLeft
            };
            _logInfo = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = false,
                normal = { textColor = LogInfo }
            };
            _logWarn = new GUIStyle(_logInfo)
            {
                normal = { textColor = Warning }
            };
            _logError = new GUIStyle(_logInfo)
            {
                normal = { textColor = Error }
            };
        }

        /// <summary>
        /// Draws a panel toolbar with a left title and right-side action buttons.
        /// </summary>
        public static void DrawPanelToolbar(string title, Action drawButtons, float titleWidth = 0f)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (titleWidth > 0f)
                GUILayout.Label(title, PanelTitle, GUILayout.Width(titleWidth));
            else
                GUILayout.Label(title, PanelTitle);

            drawButtons?.Invoke();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws a foldout header section. Returns true when expanded.
        /// </summary>
        public static bool BeginSectionFoldout(ref bool fold, string title)
        {
            fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
            return fold;
        }

        /// <summary>
        /// Draws a foldout header with an optional trailing action (e.g. "+" button).
        /// Returns true when expanded.
        /// </summary>
        public static bool BeginSectionFoldout(ref bool fold, string title, Action drawTrailing)
        {
            EditorGUILayout.BeginHorizontal();
            fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
            GUILayout.FlexibleSpace();
            drawTrailing?.Invoke();
            EditorGUILayout.EndHorizontal();
            return fold;
        }

        public static void ColoredLabel(string text, Color color, GUIStyle style = null, params GUILayoutOption[] options)
        {
            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, style ?? EditorStyles.label, options);
            GUI.contentColor = prev;
        }

        public static void StatusIcon(bool ok, bool warning = false)
        {
            if (ok)
                ColoredLabel("\u2713", Success, EditorStyles.label, GUILayout.Width(16));
            else if (warning)
                ColoredLabel("\u2717", Warning, EditorStyles.label, GUILayout.Width(16));
            else
                ColoredLabel("\u2717", Error, EditorStyles.label, GUILayout.Width(16));
        }

        public static GUIStyle CreateBubbleStyle(Color bg)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, bg);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = tex },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(4, 4, 2, 2),
                wordWrap = true
            };
        }

        public static GUIStyle CreateInputStyle()
        {
            return new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 };
        }
    }
}

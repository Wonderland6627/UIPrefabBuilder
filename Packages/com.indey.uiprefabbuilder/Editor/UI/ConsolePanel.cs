using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class ConsolePanel
    {
        private Vector2 _scroll;
        private bool _autoScroll = true;
        private bool _showInfo = true;
        private bool _showWarning = true;
        private bool _showError = true;
        private bool _needsScroll;

        private GUIStyle _infoStyle, _warnStyle, _errorStyle, _timestampStyle;
        private bool _stylesInit;

        public ConsolePanel()
        {
            ConsoleLogger.OnLogAdded += _ => _needsScroll = true;
        }

        public void Draw(Rect rect)
        {
            InitStyles();

            GUILayout.BeginArea(rect);

            DrawToolbar();

            var entries = ConsoleLogger.Entries;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!ShouldShow(entry.Level)) continue;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(entry.Timestamp.ToString("HH:mm:ss"), _timestampStyle, GUILayout.Width(58));
                var style = GetStyleForLevel(entry.Level);
                GUILayout.Label(entry.Message, style);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (_autoScroll && _needsScroll)
            {
                _scroll.y = float.MaxValue;
                _needsScroll = false;
            }

            GUILayout.EndArea();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Console", EditorStyles.miniBoldLabel, GUILayout.Width(52));

            var infoColor = _showInfo ? Color.white : Color.gray;
            var warnColor = _showWarning ? new Color(1f, 0.85f, 0.2f) : Color.gray;
            var errColor = _showError ? new Color(1f, 0.3f, 0.3f) : Color.gray;

            var prev = GUI.contentColor;
            GUI.contentColor = infoColor;
            if (GUILayout.Button("I", EditorStyles.toolbarButton, GUILayout.Width(20))) _showInfo = !_showInfo;
            GUI.contentColor = warnColor;
            if (GUILayout.Button("W", EditorStyles.toolbarButton, GUILayout.Width(20))) _showWarning = !_showWarning;
            GUI.contentColor = errColor;
            if (GUILayout.Button("E", EditorStyles.toolbarButton, GUILayout.Width(20))) _showError = !_showError;
            GUI.contentColor = prev;

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Open", EditorStyles.toolbarButton, GUILayout.Width(40)))
                ConsoleLogger.OpenLogDirectory();
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(40)))
                ConsoleLogger.Clear();

            EditorGUILayout.EndHorizontal();
        }

        private bool ShouldShow(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Info: return _showInfo;
                case LogLevel.Warning: return _showWarning;
                case LogLevel.Error: return _showError;
                default: return true;
            }
        }

        private GUIStyle GetStyleForLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warning: return _warnStyle;
                case LogLevel.Error: return _errorStyle;
                default: return _infoStyle;
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _timestampStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
                alignment = TextAnchor.MiddleLeft
            };
            _infoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                richText = false,
                normal = { textColor = new Color(0.78f, 0.78f, 0.78f) }
            };
            _warnStyle = new GUIStyle(_infoStyle)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.2f) }
            };
            _errorStyle = new GUIStyle(_infoStyle)
            {
                normal = { textColor = new Color(1f, 0.35f, 0.35f) }
            };
        }
    }
}

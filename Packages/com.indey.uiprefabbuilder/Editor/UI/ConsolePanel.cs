using System.Collections.Generic;
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

        private readonly List<LogEntry> _snapshotEntries = new List<LogEntry>();
        private int _lastSnapshotCount = -1;
        private bool _lastShowInfo, _lastShowWarning, _lastShowError;

        public ConsolePanel()
        {
            ConsoleLogger.OnLogAdded += _ => _needsScroll = true;
        }

        public void Draw(Rect rect)
        {
            BuilderStyles.EnsureStyles();

            GUILayout.BeginArea(rect);

            DrawToolbar();

            if (Event.current.type == EventType.Layout)
            {
                var entries = ConsoleLogger.Entries;
                bool filtersChanged = _showInfo != _lastShowInfo || _showWarning != _lastShowWarning || _showError != _lastShowError;
                if (entries.Count != _lastSnapshotCount || filtersChanged)
                {
                    _snapshotEntries.Clear();
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (ShouldShow(entries[i].Level))
                            _snapshotEntries.Add(entries[i]);
                    }
                    _lastSnapshotCount = entries.Count;
                    _lastShowInfo = _showInfo;
                    _lastShowWarning = _showWarning;
                    _lastShowError = _showError;
                }
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            for (int i = 0; i < _snapshotEntries.Count; i++)
            {
                var entry = _snapshotEntries[i];

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(entry.Timestamp.ToString("HH:mm:ss"), BuilderStyles.LogTimestampStyle, GUILayout.Width(58));
                GUILayout.Label(entry.Message, GetStyleForLevel(entry.Level));
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
            BuilderStyles.DrawPanelToolbar("Console", () =>
            {
                var infoColor = _showInfo ? Color.white : Color.gray;
                var warnColor = _showWarning ? BuilderStyles.Warning : Color.gray;
                var errColor = _showError ? BuilderStyles.Error : Color.gray;

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
            }, 52f);
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

        private static GUIStyle GetStyleForLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.Warning: return BuilderStyles.LogWarnStyle;
                case LogLevel.Error: return BuilderStyles.LogErrorStyle;
                default: return BuilderStyles.LogInfoStyle;
            }
        }
    }
}

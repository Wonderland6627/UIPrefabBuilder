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
        private readonly List<float> _rowOffsets = new List<float>();
        private readonly List<float> _rowHeights = new List<float>();
        private int _lastSnapshotCount = -1;
        private bool _lastShowInfo, _lastShowWarning, _lastShowError;
        private float _layoutWidth = -1f;
        private float _messageWidth = 40f;
        private int _visibleFirst;
        private int _visibleLast;
        private bool _visibleValid;
        private const float TimestampWidth = 62f;
        private const float RowBuffer = 160f;

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
                RefreshSnapshot();
                float viewport = Mathf.Max(48f, rect.height - EditorStyles.toolbar.fixedHeight - 2f);
                float innerWidth = Mathf.Max(TimestampWidth + 40f, rect.width - 4f);
                float bar = GUI.skin.verticalScrollbar != null ? GUI.skin.verticalScrollbar.fixedWidth : 14f;
                float narrowed = Mathf.Max(TimestampWidth + 40f, innerWidth - bar);
                bool atFull = Mathf.Abs(_layoutWidth - innerWidth) < 0.5f;
                bool atNarrow = Mathf.Abs(_layoutWidth - narrowed) < 0.5f;
                float desired = innerWidth;
                if ((atNarrow && TotalHeight() > viewport) || (atFull && TotalHeight() > viewport))
                    desired = narrowed;
                EnsureRowLayout(desired);
                if (Mathf.Abs(desired - innerWidth) < 0.5f && TotalHeight() > viewport)
                    EnsureRowLayout(narrowed);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Layout)
                CaptureVisibleRows(Mathf.Max(48f, rect.height - EditorStyles.toolbar.fixedHeight - 2f));
            DrawVisibleRows();
            EditorGUILayout.EndScrollView();

            if (_autoScroll && _needsScroll)
            {
                _scroll.y = float.MaxValue;
                _needsScroll = false;
            }

            GUILayout.EndArea();
        }

        private void RefreshSnapshot()
        {
            var entries = ConsoleLogger.Entries;
            bool filtersChanged = _showInfo != _lastShowInfo || _showWarning != _lastShowWarning || _showError != _lastShowError;
            if (entries.Count == _lastSnapshotCount && !filtersChanged)
                return;

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
            _layoutWidth = -1f;
        }

        private void EnsureRowLayout(float width)
        {
            if (_rowOffsets.Count == _snapshotEntries.Count + 1 && Mathf.Abs(_layoutWidth - width) < 0.5f)
                return;

            _rowOffsets.Clear();
            _rowHeights.Clear();
            _rowOffsets.Add(0f);
            _messageWidth = Mathf.Max(40f, width - TimestampWidth);
            float offset = 0f;
            float minHeight = BuilderStyles.LogTimestampStyle.lineHeight;
            for (int i = 0; i < _snapshotEntries.Count; i++)
            {
                var entry = _snapshotEntries[i];
                var style = GetStyleForLevel(entry.Level);
                float height = Mathf.Max(minHeight, style.CalcHeight(new GUIContent(entry.Message ?? ""), _messageWidth));
                _rowHeights.Add(height);
                offset += height;
                _rowOffsets.Add(offset);
            }
            _layoutWidth = width;
        }

        private void CaptureVisibleRows(float viewport)
        {
            int count = _snapshotEntries.Count;
            if (count == 0 || _rowOffsets.Count != count + 1)
            {
                _visibleFirst = 0;
                _visibleLast = 0;
                _visibleValid = true;
                return;
            }

            float totalHeight = TotalHeight();
            float scrollY = _scroll.y;
            if (float.IsInfinity(scrollY) || scrollY >= totalHeight)
                scrollY = Mathf.Max(0f, totalHeight - viewport);
            float top = Mathf.Max(0f, scrollY - RowBuffer);
            float bottom = scrollY + viewport + RowBuffer;
            _visibleFirst = FindRowAt(top);
            _visibleLast = FindRowAfter(bottom);
            if (_visibleLast < _visibleFirst)
                _visibleLast = _visibleFirst;
            _visibleValid = true;
        }

        private void DrawVisibleRows()
        {
            if (!_visibleValid)
                return;

            int count = _snapshotEntries.Count;
            int first = _visibleFirst;
            int last = _visibleLast;
            if (count == 0 || _rowOffsets.Count != count + 1 || last > count)
                return;

            if (first > 0)
                GUILayout.Space(_rowOffsets[first]);

            for (int i = first; i < last; i++)
            {
                var entry = _snapshotEntries[i];
                float height = _rowHeights[i];
                EditorGUILayout.BeginHorizontal(GUILayout.Height(height));
                GUILayout.Label(TimeLabel(entry), BuilderStyles.LogTimestampStyle, GUILayout.Width(TimestampWidth), GUILayout.Height(height));
                GUILayout.Label(entry.Message ?? "", GetStyleForLevel(entry.Level), GUILayout.Width(_messageWidth), GUILayout.Height(height));
                EditorGUILayout.EndHorizontal();
            }

            if (last < count)
                GUILayout.Space(Mathf.Max(0f, TotalHeight() - _rowOffsets[last]));
        }

        private float TotalHeight()
        {
            return _rowOffsets.Count == 0 ? 0f : _rowOffsets[_rowOffsets.Count - 1];
        }

        private int FindRowAt(float offset)
        {
            int low = 0;
            int high = _snapshotEntries.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_rowOffsets[middle + 1] <= offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindRowAfter(float offset)
        {
            int low = 0;
            int high = _snapshotEntries.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_rowOffsets[middle] < offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private static string TimeLabel(LogEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.TimeLabel))
                return entry.TimeLabel;
            entry.TimeLabel = entry.Timestamp.ToString("HH:mm:ss");
            return entry.TimeLabel;
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

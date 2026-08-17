using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public enum SplitDirection { Horizontal, Vertical }

    public class SplitView
    {
        private readonly string _prefsKey;
        private readonly SplitDirection _direction;
        private readonly float _minSize;
        private readonly float _maxSize;
        private readonly float _splitterThickness;

        private float _splitPosition;
        private bool _isDragging;
        private Rect _splitterRect;

        public float SplitPosition
        {
            get => _splitPosition;
            set
            {
                _splitPosition = value;
                EditorPrefs.SetFloat(_prefsKey, _splitPosition);
            }
        }

        public bool IsDragging => _isDragging;

        /// <param name="prefsKey">EditorPrefs key for persisting position</param>
        /// <param name="direction">Horizontal = left|right, Vertical = top|bottom</param>
        /// <param name="defaultPosition">Default split position in pixels</param>
        /// <param name="minSize">Minimum size of the first panel</param>
        /// <param name="maxSize">Maximum size of the first panel (0 = no limit)</param>
        public SplitView(string prefsKey, SplitDirection direction, float defaultPosition,
            float minSize = 100f, float maxSize = 0f, float splitterThickness = 4f)
        {
            _prefsKey = prefsKey;
            _direction = direction;
            _minSize = minSize;
            _maxSize = maxSize;
            _splitterThickness = splitterThickness;
            _splitPosition = EditorPrefs.GetFloat(prefsKey, defaultPosition);
        }

        /// <summary>
        /// Call at the start of the split region. Returns the Rect for the first panel.
        /// </summary>
        public Rect BeginSplit(Rect availableRect)
        {
            ClampPosition(availableRect);
            HandleSplitterInput(availableRect);

            Rect firstPanel;
            if (_direction == SplitDirection.Horizontal)
            {
                firstPanel = new Rect(availableRect.x, availableRect.y, _splitPosition, availableRect.height);
                _splitterRect = new Rect(availableRect.x + _splitPosition, availableRect.y, _splitterThickness, availableRect.height);
            }
            else
            {
                firstPanel = new Rect(availableRect.x, availableRect.y, availableRect.width, _splitPosition);
                _splitterRect = new Rect(availableRect.x, availableRect.y + _splitPosition, availableRect.width, _splitterThickness);
            }

            DrawSplitter();
            return firstPanel;
        }

        /// <summary>
        /// Returns the Rect for the second panel.
        /// </summary>
        public Rect EndSplit(Rect availableRect)
        {
            if (_direction == SplitDirection.Horizontal)
            {
                var offset = _splitPosition + _splitterThickness;
                return new Rect(availableRect.x + offset, availableRect.y,
                    availableRect.width - offset, availableRect.height);
            }
            else
            {
                var offset = _splitPosition + _splitterThickness;
                return new Rect(availableRect.x, availableRect.y + offset,
                    availableRect.width, availableRect.height - offset);
            }
        }

        /// <summary>
        /// Draws a splitter that resizes from the opposite edge (e.g. right panel width).
        /// </summary>
        public void HandleReverseHorizontalSplitter(Rect splitterRect, Rect bodyRect, float otherPanelMin = 200f)
        {
            _splitterRect = splitterRect;
            var hitRect = new Rect(splitterRect.x - 2f, splitterRect.y, splitterRect.width + 4f, splitterRect.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (hitRect.Contains(e.mousePosition))
                    {
                        _isDragging = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (!_isDragging) break;
                    var newWidth = bodyRect.xMax - e.mousePosition.x;
                    var maxWidth = _maxSize > 0
                        ? Mathf.Min(_maxSize, bodyRect.width - otherPanelMin)
                        : bodyRect.width - otherPanelMin;
                    SplitPosition = Mathf.Clamp(newWidth, _minSize, Mathf.Max(_minSize, maxWidth));
                    e.Use();
                    break;

                case EventType.MouseUp:
                    if (!_isDragging) break;
                    _isDragging = false;
                    EditorPrefs.SetFloat(_prefsKey, _splitPosition);
                    e.Use();
                    break;
            }

            DrawSplitter();
        }

        private void ClampPosition(Rect available)
        {
            var totalSize = _direction == SplitDirection.Horizontal ? available.width : available.height;
            var effectiveMax = _maxSize > 0 ? Mathf.Min(_maxSize, totalSize - _minSize) : totalSize - _minSize;
            _splitPosition = Mathf.Clamp(_splitPosition, _minSize, Mathf.Max(_minSize, effectiveMax));
        }

        private void HandleSplitterInput(Rect available)
        {
            var e = Event.current;
            var cursorRect = _splitterRect;
            cursorRect.x -= 2;
            cursorRect.width += 4;
            if (_direction == SplitDirection.Vertical)
            {
                cursorRect.y -= 2;
                cursorRect.height += 4;
            }

            EditorGUIUtility.AddCursorRect(cursorRect,
                _direction == SplitDirection.Horizontal ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (cursorRect.Contains(e.mousePosition))
                    {
                        _isDragging = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDragging)
                    {
                        if (_direction == SplitDirection.Horizontal)
                            _splitPosition = e.mousePosition.x - available.x;
                        else
                            _splitPosition = e.mousePosition.y - available.y;

                        ClampPosition(available);
                        EditorPrefs.SetFloat(_prefsKey, _splitPosition);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDragging)
                    {
                        _isDragging = false;
                        EditorPrefs.SetFloat(_prefsKey, _splitPosition);
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawSplitter()
        {
            var color = _isDragging ? BuilderStyles.SplitterActive : BuilderStyles.Splitter;
            EditorGUI.DrawRect(_splitterRect, color);
        }
    }
}

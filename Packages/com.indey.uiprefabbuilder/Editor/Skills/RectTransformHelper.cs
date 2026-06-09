using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Skills
{
    public enum AnchorPreset
    {
        TopLeft, TopCenter, TopRight,
        MiddleLeft, MiddleCenter, MiddleRight,
        BottomLeft, BottomCenter, BottomRight,
        StretchAll, StretchTop, StretchBottom, StretchLeft, StretchRight
    }

    public static class RectTransformHelper
    {
        public static void SetAnchorPreset(GameObject go, AnchorPreset preset)
        {
            var r = go?.GetComponent<RectTransform>(); if (r == null) return;
            Undo.RecordObject(r, "Set Anchor");
            switch (preset)
            {
                case AnchorPreset.TopLeft: Set(r, new Vector2(0,1), new Vector2(0,1), new Vector2(0,1)); break;
                case AnchorPreset.TopCenter: Set(r, new Vector2(0.5f,1), new Vector2(0.5f,1), new Vector2(0.5f,1)); break;
                case AnchorPreset.TopRight: Set(r, new Vector2(1,1), new Vector2(1,1), new Vector2(1,1)); break;
                case AnchorPreset.MiddleLeft: Set(r, new Vector2(0,0.5f), new Vector2(0,0.5f), new Vector2(0,0.5f)); break;
                case AnchorPreset.MiddleCenter: Set(r, new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f), new Vector2(0.5f,0.5f)); break;
                case AnchorPreset.MiddleRight: Set(r, new Vector2(1,0.5f), new Vector2(1,0.5f), new Vector2(1,0.5f)); break;
                case AnchorPreset.BottomLeft: Set(r, Vector2.zero, Vector2.zero, Vector2.zero); break;
                case AnchorPreset.BottomCenter: Set(r, new Vector2(0.5f,0), new Vector2(0.5f,0), new Vector2(0.5f,0)); break;
                case AnchorPreset.BottomRight: Set(r, new Vector2(1,0), new Vector2(1,0), new Vector2(1,0)); break;
                case AnchorPreset.StretchAll: Set(r, Vector2.zero, Vector2.one, new Vector2(0.5f,0.5f)); r.offsetMin = r.offsetMax = Vector2.zero; break;
                case AnchorPreset.StretchTop: Set(r, new Vector2(0,1), new Vector2(1,1), new Vector2(0.5f,1)); break;
                case AnchorPreset.StretchBottom: Set(r, new Vector2(0,0), new Vector2(1,0), new Vector2(0.5f,0)); break;
                case AnchorPreset.StretchLeft: Set(r, new Vector2(0,0), new Vector2(0,1), new Vector2(0,0.5f)); break;
                case AnchorPreset.StretchRight: Set(r, new Vector2(1,0), new Vector2(1,1), new Vector2(1,0.5f)); break;
            }
        }

        public static void SetRawAnchors(GameObject go, float minX, float minY, float maxX, float maxY)
        {
            var r = go?.GetComponent<RectTransform>(); if (r == null) return;
            Undo.RecordObject(r, "Set Anchor");
            r.anchorMin = new Vector2(minX, minY);
            r.anchorMax = new Vector2(maxX, maxY);
        }

        public static void SetSize(GameObject go, float w, float h) { var r = go?.GetComponent<RectTransform>(); if (r == null) return; Undo.RecordObject(r, "Set Size"); r.sizeDelta = new Vector2(w, h); }
        public static void SetPosition(GameObject go, float x, float y) { var r = go?.GetComponent<RectTransform>(); if (r == null) return; Undo.RecordObject(r, "Set Pos"); r.anchoredPosition = new Vector2(x, y); }
        private static void Set(RectTransform r, Vector2 min, Vector2 max, Vector2 pivot) { r.anchorMin = min; r.anchorMax = max; r.pivot = pivot; }
    }
}

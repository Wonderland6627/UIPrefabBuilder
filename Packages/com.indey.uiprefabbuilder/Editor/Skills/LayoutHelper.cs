using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class LayoutHelper
    {
        public static VerticalLayoutGroup AddVerticalLayout(GameObject go, float spacing = 8, TextAnchor align = TextAnchor.UpperCenter, RectOffset pad = null,
            bool childControlWidth = true, bool childControlHeight = true, bool childForceExpandWidth = false, bool childForceExpandHeight = false)
        {
            var l = EnsureComponent<VerticalLayoutGroup>(go);
            l.spacing = spacing; l.childAlignment = align;
            l.childControlWidth = childControlWidth; l.childControlHeight = childControlHeight;
            l.childForceExpandWidth = childForceExpandWidth; l.childForceExpandHeight = childForceExpandHeight;
            if (pad != null) l.padding = pad;
            return l;
        }

        public static HorizontalLayoutGroup AddHorizontalLayout(GameObject go, float spacing = 8, TextAnchor align = TextAnchor.MiddleLeft, RectOffset pad = null,
            bool childControlWidth = true, bool childControlHeight = true, bool childForceExpandWidth = false, bool childForceExpandHeight = false)
        {
            var l = EnsureComponent<HorizontalLayoutGroup>(go);
            l.spacing = spacing; l.childAlignment = align;
            l.childControlWidth = childControlWidth; l.childControlHeight = childControlHeight;
            l.childForceExpandWidth = childForceExpandWidth; l.childForceExpandHeight = childForceExpandHeight;
            if (pad != null) l.padding = pad;
            return l;
        }

        public static GridLayoutGroup AddGridLayout(GameObject go, Vector2 cellSize, Vector2 spacing)
        {
            var l = EnsureComponent<GridLayoutGroup>(go);
            l.cellSize = cellSize; l.spacing = spacing;
            return l;
        }

        public static LayoutElement AddLayoutElement(GameObject go, float? minW = null, float? minH = null, float? prefW = null, float? prefH = null, float? flexW = null, float? flexH = null)
        {
            var e = EnsureComponent<LayoutElement>(go);
            if (minW.HasValue) e.minWidth = minW.Value;
            if (minH.HasValue) e.minHeight = minH.Value;
            if (prefW.HasValue) e.preferredWidth = prefW.Value;
            if (prefH.HasValue) e.preferredHeight = prefH.Value;
            if (flexW.HasValue) e.flexibleWidth = flexW.Value;
            if (flexH.HasValue) e.flexibleHeight = flexH.Value;
            return e;
        }

        private static T EnsureComponent<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c == null) { Undo.AddComponent<T>(go); c = go.GetComponent<T>(); }
            else Undo.RecordObject(c, "Layout");
            return c;
        }
    }
}

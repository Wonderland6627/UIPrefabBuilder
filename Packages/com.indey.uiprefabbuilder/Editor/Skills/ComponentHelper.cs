using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class ComponentHelper
    {
        public static void SetImageColor(GameObject go, Color color) { var i = go?.GetComponent<Image>(); if (i == null) return; Undo.RecordObject(i, "Color"); i.color = color; }
        public static void SetImageSprite(GameObject go, string path) { var i = go?.GetComponent<Image>(); if (i == null) return; Undo.RecordObject(i, "Sprite"); i.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); }
        public static void SetText(GameObject go, string text)
        {
            var legacy = go?.GetComponent<Text>();
            if (legacy != null) { Undo.RecordObject(legacy, "Text"); legacy.text = text; return; }
            foreach (var c in go.GetComponents<Component>())
            {
                var p = c?.GetType().GetProperty("text");
                if (p != null && p.PropertyType == typeof(string)) { Undo.RecordObject(c, "TMP"); p.SetValue(c, text); return; }
            }
        }
        public static CanvasGroup AddCanvasGroup(GameObject go, float alpha = 1, bool interact = true, bool block = true)
        {
            var g = go.GetComponent<CanvasGroup>();
            if (g == null) { Undo.AddComponent<CanvasGroup>(go); g = go.GetComponent<CanvasGroup>(); }
            Undo.RecordObject(g, "CanvasGroup"); g.alpha = alpha; g.interactable = interact; g.blocksRaycasts = block;
            return g;
        }
    }
}

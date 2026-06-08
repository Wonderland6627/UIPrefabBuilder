using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class UICreator
    {
        private static Type _tmpTextType;
        private static bool _tmpChecked, _tmpAvailable;

        public static GameObject CreateCanvas(string name = "Canvas", RenderMode mode = RenderMode.ScreenSpaceOverlay)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            var c = go.AddComponent<Canvas>(); c.renderMode = mode;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        public static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = CreateUI(name, parent);
            var img = go.AddComponent<Image>(); img.color = color;
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        public static GameObject CreateButton(string name, Transform parent, string text, Vector2 size)
        {
            var go = CreateUI(name, parent);
            var img = go.AddComponent<Image>(); img.color = new Color(0.2f, 0.45f, 0.85f);
            go.AddComponent<Button>().targetGraphic = img;
            SetSize(go, size);
            var label = CreateUI("Text", go.transform);
            Stretch(label.GetComponent<RectTransform>());
            AddText(label, text, 18, Color.white, TextAnchor.MiddleCenter);
            return go;
        }

        public static GameObject CreateText(string name, Transform parent, string content, int fontSize = 18, Color? color = null)
        {
            var go = CreateUI(name, parent);
            AddText(go, content, fontSize, color ?? Color.white, TextAnchor.MiddleLeft);
            return go;
        }

        public static GameObject CreateImage(string name, Transform parent, string spritePath, Vector2 size)
        {
            var go = CreateUI(name, parent);
            var img = go.AddComponent<Image>();
            if (!string.IsNullOrEmpty(spritePath))
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sp != null) img.sprite = sp;
            }
            SetSize(go, size);
            return go;
        }

        public static GameObject CreateInputField(string name, Transform parent, string placeholder, Vector2 size)
        {
            var go = CreateUI(name, parent);
            go.AddComponent<Image>().color = Color.white;
            go.AddComponent<InputField>();
            SetSize(go, size);
            return go;
        }

        public static GameObject CreateSlider(string name, Transform parent, float min, float max, float val)
        {
            var go = CreateUI(name, parent);
            var s = go.AddComponent<Slider>(); s.minValue = min; s.maxValue = max; s.value = val;
            return go;
        }

        public static GameObject CreateToggle(string name, Transform parent, string label, bool isOn)
        {
            var go = CreateUI(name, parent);
            go.AddComponent<Toggle>().isOn = isOn;
            CreateText("Label", go.transform, label, 14);
            return go;
        }

        public static GameObject CreateScrollView(string name, Transform parent, Vector2 size)
        {
            var go = CreateUI(name, parent);
            go.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
            SetSize(go, size);

            var viewport = CreateUI("Viewport", go.transform);
            var viewportImg = viewport.AddComponent<Image>();
            viewportImg.color = Color.white;
            viewport.AddComponent<Mask>().showMaskGraphic = false;
            var viewportRT = viewport.GetComponent<RectTransform>();
            Stretch(viewportRT);

            var content = CreateUI("Content", viewport.transform);
            var contentRT = content.GetComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.sizeDelta = new Vector2(0, size.y);
            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = go.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRT;
            scrollRect.content = contentRT;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            return go;
        }

        public static List<GameObject> CreateBatch(List<UIElementConfig> items)
        {
            var results = new List<GameObject>();
            if (items == null) return results;
            foreach (var item in items)
            {
                switch ((item.Type ?? "").ToLowerInvariant())
                {
                    case "canvas": results.Add(CreateCanvas(item.Name)); break;
                    case "panel": results.Add(CreatePanel(item.Name, item.Parent, item.Color ?? new Color(0, 0, 0, 0.7f))); break;
                    case "button": results.Add(CreateButton(item.Name, item.Parent, item.Text ?? "Button", item.Size ?? new Vector2(160, 30))); break;
                    case "text": results.Add(CreateText(item.Name, item.Parent, item.Text ?? "", item.FontSize)); break;
                    case "image": results.Add(CreateImage(item.Name, item.Parent, item.SpritePath, item.Size ?? new Vector2(100, 100))); break;
                }
            }
            return results;
        }

        private static GameObject CreateUI(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create UI");
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private static void AddText(GameObject go, string text, int size, Color color, TextAnchor align)
        {
            if (IsTMP())
            {
                var c = go.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("text")?.SetValue(c, text);
                _tmpTextType.GetProperty("fontSize")?.SetValue(c, (float)size);
                _tmpTextType.GetProperty("color")?.SetValue(c, color);
                return;
            }
            var t = go.AddComponent<Text>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static bool IsTMP()
        {
            if (_tmpChecked) return _tmpAvailable;
            _tmpChecked = true;
            _tmpTextType = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            _tmpAvailable = _tmpTextType != null;
            return _tmpAvailable;
        }

        private static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static void SetSize(GameObject go, Vector2 s) { go.GetComponent<RectTransform>().sizeDelta = s; }
    }

    public class UIElementConfig
    {
        public string Type, Name, Text, SpritePath;
        public Transform Parent;
        public int FontSize = 18;
        public Color? Color;
        public Vector2? Size;
    }
}

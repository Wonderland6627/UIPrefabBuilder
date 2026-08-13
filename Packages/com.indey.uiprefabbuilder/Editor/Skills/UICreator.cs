using System;
using System.Collections.Generic;
using System.Reflection;
using Indey.UIPrefabBuilder.Config;
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

            var config = ProjectConfig.Current;
            scaler.referenceResolution = new Vector2(config.basicInfo.designWidth, config.basicInfo.designHeight);
            switch (config.basicInfo.screenMatchMode)
            {
                case BasicInfo.CanvasMatchMode.MatchWidthOrHeight:
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                    scaler.matchWidthOrHeight = config.basicInfo.canvasMatchMode;
                    break;
                case BasicInfo.CanvasMatchMode.Expand:
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                    break;
                case BasicInfo.CanvasMatchMode.Shrink:
                    scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Shrink;
                    break;
            }

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        public static GameObject CreatePanel(string name, Transform parent, Color color)
        {
            var go = CreateUI(name, parent);
            var img = AddImageComponent(go);
            img.color = color;
            Stretch(go.GetComponent<RectTransform>());
            return go;
        }

        public static GameObject CreateButton(string name, Transform parent, string text, Vector2 size)
        {
            var go = CreateUI(name, parent);
            var img = AddImageComponent(go);
            // White, not a blue tint: Image.color multiplies the sprite, so a coloured default turns
            // every later sprite assignment into the wrong hue.
            img.color = Color.white;
            var btnComp = AddButtonComponent(go);
            var selectable = btnComp as UnityEngine.UI.Selectable;
            if (selectable != null) selectable.targetGraphic = img;
            SetSize(go, size);
            var label = CreateUI("Text", go.transform);
            Stretch(label.GetComponent<RectTransform>());
            AddText(label, text, 18, Color.white, TextAnchor.MiddleCenter);
            return go;
        }

        public static GameObject CreateText(string name, Transform parent, string content, int fontSize = 18, Color? color = null,
            TextAnchor align = TextAnchor.MiddleLeft)
        {
            var go = CreateUI(name, parent);
            AddText(go, content, fontSize, color ?? Color.white, align);
            return go;
        }

        public static GameObject CreateImage(string name, Transform parent, string spritePath, Vector2 size)
            => CreateImage(name, parent, spritePath, size, out _);

        public static GameObject CreateImage(string name, Transform parent, string spritePath, Vector2 size, out string spriteError)
        {
            var go = CreateUI(name, parent);
            var img = AddImageComponent(go);
            spriteError = ComponentHelper.LoadSpriteOrError(spritePath, out var sp);
            if (spriteError == null && sp != null) img.sprite = sp;
            SetSize(go, size);
            return go;
        }

        /// <summary>
        /// 创建 Unity 标准 InputField 层级（TMP 可用时使用 TMP_InputField）：
        /// Root(Image + InputField) → Text Area(RectMask2D) → Placeholder / Text，并完成组件接线。
        /// 只挂一个空的 InputField 组件会渲染成白色矩形，既没有占位符也没有可编辑文本区。
        /// </summary>
        public static GameObject CreateInputField(string name, Transform parent, string placeholder, Vector2 size,
            string text = "", int fontSize = 24, Color? textColor = null, Color? placeholderColor = null,
            string backgroundSpritePath = null, List<string> spriteErrors = null)
        {
            var go = CreateUI(name, parent);
            var img = AddImageComponent(go);
            img.color = Color.white;
            var bgSprite = LoadSpriteTracked(backgroundSpritePath, "backgroundSpritePath", spriteErrors);
            if (bgSprite != null)
            {
                img.sprite = bgSprite;
                img.type = Image.Type.Sliced;
            }
            SetSize(go, size);

            var input = AddInputFieldComponent(go, out var isTMPInput);

            Transform textHost = go.transform;
            RectTransform viewport = null;
            if (isTMPInput)
            {
                var textArea = CreateUI("Text Area", go.transform);
                viewport = textArea.GetComponent<RectTransform>();
                Stretch(viewport);
                viewport.offsetMin = new Vector2(12, 6);
                viewport.offsetMax = new Vector2(-12, -6);
                textArea.AddComponent<RectMask2D>();
                textHost = textArea.transform;
            }

            var placeholderGO = CreateUI("Placeholder", textHost);
            var placeholderRT = placeholderGO.GetComponent<RectTransform>();
            Stretch(placeholderRT);
            if (!isTMPInput) { placeholderRT.offsetMin = new Vector2(12, 6); placeholderRT.offsetMax = new Vector2(-12, -6); }
            AddText(placeholderGO, placeholder ?? "", fontSize,
                placeholderColor ?? new Color(0.5f, 0.5f, 0.5f, 0.75f), TextAnchor.MiddleLeft, !isTMPInput);

            var textGO = CreateUI("Text", textHost);
            var textRT = textGO.GetComponent<RectTransform>();
            Stretch(textRT);
            if (!isTMPInput) { textRT.offsetMin = new Vector2(12, 6); textRT.offsetMax = new Vector2(-12, -6); }
            AddText(textGO, text ?? "", fontSize,
                textColor ?? new Color(0.2f, 0.2f, 0.2f, 1f), TextAnchor.MiddleLeft, !isTMPInput);

            WireInputField(input, viewport, textGO, placeholderGO);
            // Must happen after wiring: the component's own `text` is the authoritative value and
            // its setter is what pushes the string into the (already assigned) label.
            SetInputFieldText(input, text);
            return go;
        }

        private static void SetInputFieldText(Component input, string text)
        {
            if (input == null || string.IsNullOrEmpty(text)) return;
            var prop = input.GetType().GetProperty("text");
            if (prop == null || prop.PropertyType != typeof(string) || !prop.CanWrite) return;
            prop.SetValue(input, text);
            EditorUtility.SetDirty(input);
        }

        /// <summary>
        /// InputField 的文本区与占位符必须通过属性接线，仅作为子对象存在不会被使用。
        /// </summary>
        private static void WireInputField(Component input, RectTransform viewport, GameObject textGO, GameObject placeholderGO)
        {
            if (input == null) return;
            var t = input.GetType();

            if (viewport != null)
            {
                var viewportProp = t.GetProperty("textViewport");
                if (viewportProp != null && viewportProp.CanWrite) viewportProp.SetValue(input, viewport);
            }

            AssignIfCompatible(input, t.GetProperty("textComponent"), FindTextGraphic(textGO));
            AssignIfCompatible(input, t.GetProperty("placeholder"), FindTextGraphic(placeholderGO));
            EditorUtility.SetDirty(input);
        }

        private static void AssignIfCompatible(Component target, PropertyInfo prop, Component value)
        {
            if (prop == null || !prop.CanWrite || value == null) return;
            if (!prop.PropertyType.IsInstanceOfType(value)) return;
            prop.SetValue(target, value);
        }

        /// <summary>
        /// 加载 sprite 并把失败原因收集给调用方。丢掉这个错误会让"路径写错"变成一个白色方块，
        /// 而工具仍然报告成功——这正是设计稿还原里最难排查的一类偏差。
        /// </summary>
        private static Sprite LoadSpriteTracked(string path, string role, List<string> errors)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var error = ComponentHelper.LoadSpriteOrError(path, out var sprite);
            if (error != null)
            {
                errors?.Add($"{role}: {error}");
                return null;
            }
            return sprite;
        }

        private static Component FindTextGraphic(GameObject go)
        {
            if (go == null) return null;
            var legacy = go.GetComponent<Text>();
            if (legacy != null) return legacy;
            foreach (var c in go.GetComponents<Component>())
            {
                if (!(c is Graphic)) continue;
                var p = c.GetType().GetProperty("text");
                if (p != null && p.PropertyType == typeof(string) && p.CanWrite) return c;
            }
            return null;
        }

        /// <summary>
        /// 创建一个 Tab 栏：ToggleGroup 下的 N 个互斥 Tab，每个 Tab 含未选中底图、选中态图形和标签。
        /// 选中态通过 Toggle.graphic 接线，因此"当前选中的页签高亮"是真实状态而不是一张静态贴图。
        /// </summary>
        public static GameObject CreateTabBar(string name, Transform parent, List<string> labels, int selectedIndex,
            Vector2 tabSize, float spacing, string unselectedSpritePath, string selectedSpritePath,
            int fontSize, Color selectedTextColor, Color unselectedTextColor, out List<GameObject> tabs,
            List<string> spriteErrors = null)
        {
            tabs = new List<GameObject>();
            var root = CreateUI(name, parent);
            var group = root.AddComponent<ToggleGroup>();
            group.allowSwitchOff = false;

            int count = labels != null ? labels.Count : 0;
            var rootRT = root.GetComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(count > 0 ? count * tabSize.x + (count - 1) * spacing : 0, tabSize.y);

            // allowSwitchOff=false plus an out-of-range index would leave every tab unselected.
            if (count > 0) selectedIndex = Mathf.Clamp(selectedIndex, 0, count - 1);

            var unselectedSprite = LoadSpriteTracked(unselectedSpritePath, "unselectedSpritePath", spriteErrors);
            var selectedSprite = LoadSpriteTracked(selectedSpritePath, "selectedSpritePath", spriteErrors);

            float step = tabSize.x + spacing;
            float startX = count > 0 ? -(count - 1) * step * 0.5f : 0f;

            for (int i = 0; i < count; i++)
            {
                bool isSelected = i == selectedIndex;
                var tab = CreateUI($"{name}_{i}", root.transform);
                var tabRT = tab.GetComponent<RectTransform>();
                tabRT.anchorMin = tabRT.anchorMax = new Vector2(0.5f, 0.5f);
                tabRT.pivot = new Vector2(0.5f, 0.5f);
                tabRT.sizeDelta = tabSize;
                tabRT.anchoredPosition = new Vector2(startX + i * step, 0);

                var bg = AddImageComponent(tab);
                bg.color = Color.white;
                if (unselectedSprite != null) { bg.sprite = unselectedSprite; bg.type = Image.Type.Sliced; }

                var selectedGO = CreateUI("Selected", tab.transform);
                Stretch(selectedGO.GetComponent<RectTransform>());
                var selectedImg = selectedGO.AddComponent<Image>();
                selectedImg.color = Color.white;
                if (selectedSprite != null) { selectedImg.sprite = selectedSprite; selectedImg.type = Image.Type.Sliced; }
                else selectedImg.color = new Color(1f, 1f, 1f, 0.35f);

                var label = CreateUI("Label", tab.transform);
                Stretch(label.GetComponent<RectTransform>());
                AddText(label, labels[i] ?? "", fontSize, isSelected ? selectedTextColor : unselectedTextColor, TextAnchor.MiddleCenter);

                var toggle = tab.AddComponent<Toggle>();
                toggle.group = group;
                toggle.targetGraphic = bg;
                toggle.graphic = selectedImg;
                toggle.isOn = isSelected;
                selectedGO.SetActive(isSelected);

                tabs.Add(tab);
            }

            return root;
        }

        public static GameObject CreateSlider(string name, Transform parent, float min, float max, float val)
        {
            var go = CreateUI(name, parent);
            var s = go.AddComponent<Slider>(); s.minValue = min; s.maxValue = max; s.value = val;
            return go;
        }

        /// <summary>
        /// 创建 Unity 标准 Toggle 层级：Root(Toggle) → Background(Image) → Checkmark(Image)，外加 Label。
        /// Background 接为 targetGraphic、Checkmark 接为 graphic，选中高亮才会真正跟随状态。
        /// </summary>
        public static GameObject CreateToggle(string name, Transform parent, string label, bool isOn,
            Vector2? size = null, string backgroundSpritePath = null, string checkmarkSpritePath = null,
            int fontSize = 20, Color? labelColor = null, List<string> spriteErrors = null)
        {
            var go = CreateUI(name, parent);
            var toggle = go.AddComponent<Toggle>();
            var boxSize = size ?? new Vector2(160, 160);
            SetSize(go, boxSize);

            var background = CreateUI("Background", go.transform);
            Stretch(background.GetComponent<RectTransform>());
            var bgImage = background.AddComponent<Image>();
            bgImage.color = Color.white;
            var bgSprite = LoadSpriteTracked(backgroundSpritePath, "backgroundSpritePath", spriteErrors);
            if (bgSprite != null) { bgImage.sprite = bgSprite; bgImage.type = Image.Type.Sliced; }

            var checkmark = CreateUI("Checkmark", background.transform);
            Stretch(checkmark.GetComponent<RectTransform>());
            var checkImage = checkmark.AddComponent<Image>();
            checkImage.color = Color.white;
            var checkSprite = LoadSpriteTracked(checkmarkSpritePath, "checkmarkSpritePath", spriteErrors);
            if (checkSprite != null) { checkImage.sprite = checkSprite; checkImage.type = Image.Type.Sliced; }
            else checkImage.color = new Color(1f, 0.85f, 0.2f, 0.5f);

            if (!string.IsNullOrEmpty(label))
            {
                var labelGO = CreateUI("Label", go.transform);
                Stretch(labelGO.GetComponent<RectTransform>());
                AddText(labelGO, label, fontSize, labelColor ?? Color.white, TextAnchor.MiddleCenter);
            }

            toggle.targetGraphic = bgImage;
            toggle.graphic = checkImage;
            toggle.isOn = isOn;
            checkmark.SetActive(isOn);
            return go;
        }

        public static GameObject CreateScrollView(string name, Transform parent, Vector2 size)
        {
            var go = CreateUI(name, parent);
            AddImageComponent(go).color = new Color(0.1f, 0.1f, 0.1f, 0.5f);
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

        public static GameObject CreateDropdown(string name, Transform parent, string options, Vector2 size)
        {
            var go = CreateUI(name, parent);
            var img = AddImageComponent(go);
            img.color = Color.white;
            var dd = go.AddComponent<Dropdown>();
            dd.options.Clear();
            if (!string.IsNullOrEmpty(options))
            {
                foreach (var opt in options.Split(','))
                    dd.options.Add(new Dropdown.OptionData(opt.Trim()));
            }
            var label = CreateUI("Label", go.transform);
            Stretch(label.GetComponent<RectTransform>());
            AddText(label, dd.options.Count > 0 ? dd.options[0].text : "", 14, Color.black, TextAnchor.MiddleLeft);
            SetSize(go, size);
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
                    case "text": results.Add(CreateText(item.Name, item.Parent, item.Text ?? "", item.FontSize, item.Color, item.Alignment)); break;
                    case "image": results.Add(CreateImage(item.Name, item.Parent, item.SpritePath, item.Size ?? new Vector2(100, 100))); break;
                    case "inputfield":
                        results.Add(CreateInputField(item.Name, item.Parent, item.Placeholder ?? "Enter text...",
                            item.Size ?? new Vector2(200, 30), item.Text ?? "", item.FontSize, item.Color, null, item.SpritePath));
                        break;
                    case "slider": results.Add(CreateSlider(item.Name, item.Parent, 0, 1, 0.5f)); break;
                    case "toggle":
                        results.Add(CreateToggle(item.Name, item.Parent, item.Text, item.IsOn,
                            item.Size, item.SpritePath, item.CheckmarkSpritePath, item.FontSize, item.Color));
                        break;
                    case "dropdown": results.Add(CreateDropdown(item.Name, item.Parent, item.Text ?? "Option A,Option B", item.Size ?? new Vector2(160, 30))); break;
                    case "scrollview": results.Add(CreateScrollView(item.Name, item.Parent, item.Size ?? new Vector2(300, 200))); break;
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

        private static Image AddImageComponent(GameObject go)
        {
            var overrideType = ProjectConfig.Current.componentOverrides.imageComponent;
            if (!string.IsNullOrEmpty(overrideType))
            {
                var t = ComponentHelper.FindComponentType(overrideType);
                if (t != null && typeof(Image).IsAssignableFrom(t))
                    return (Image)go.AddComponent(t);
            }
            return go.AddComponent<Image>();
        }

        private static Component AddButtonComponent(GameObject go)
        {
            var overrideType = ProjectConfig.Current.componentOverrides.buttonComponent;
            if (!string.IsNullOrEmpty(overrideType))
            {
                var t = ComponentHelper.FindComponentType(overrideType);
                if (t != null)
                    return go.AddComponent(t);
            }
            return go.AddComponent<Button>();
        }

        private static Component AddInputFieldComponent(GameObject go, out bool isTMPInput)
        {
            var overrideType = ProjectConfig.Current.componentOverrides.inputFieldComponent;
            if (!string.IsNullOrEmpty(overrideType))
            {
                var t = ComponentHelper.FindComponentType(overrideType);
                if (t != null)
                {
                    isTMPInput = IsTMPInputFieldType(t);
                    return go.AddComponent(t);
                }
            }

            // TMP_InputField only accepts TMP text children, so the component kind decides the
            // whole hierarchy below — pick it before any text child is created.
            var tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
            if (tmpInputType != null && IsTMP())
            {
                isTMPInput = true;
                return go.AddComponent(tmpInputType);
            }

            isTMPInput = false;
            return go.AddComponent<InputField>();
        }

        private static bool IsTMPInputFieldType(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.Name == "TMP_InputField") return true;
            }
            return false;
        }

        private static void AddText(GameObject go, string text, int size, Color color, TextAnchor align, bool forceLegacy = false)
        {
            if (forceLegacy)
            {
                AddLegacyText(go, text, size, color, align);
                return;
            }

            var overrideType = ProjectConfig.Current.componentOverrides.textComponent;
            if (!string.IsNullOrEmpty(overrideType))
            {
                var customType = ComponentHelper.FindComponentType(overrideType);
                if (customType != null)
                {
                    var c = go.AddComponent(customType);
                    customType.GetProperty("text")?.SetValue(c, text);
                    customType.GetProperty("fontSize")?.SetValue(c, (float)size);
                    customType.GetProperty("color")?.SetValue(c, color);
                    ApplyTMPAlignment(c, align);
                    EnsureTMPPersists(c, text);
                    return;
                }
            }

            if (IsTMP())
            {
                var c = go.AddComponent(_tmpTextType);
                _tmpTextType.GetProperty("text")?.SetValue(c, text);
                _tmpTextType.GetProperty("fontSize")?.SetValue(c, (float)size);
                _tmpTextType.GetProperty("color")?.SetValue(c, color);
                ApplyTMPAlignment(c, align);
                EnsureTMPPersists(c, text);
                return;
            }
            AddLegacyText(go, text, size, color, align);
        }

        private static void AddLegacyText(GameObject go, string text, int size, Color color, TextAnchor align)
        {
            var t = go.AddComponent<Text>();
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        /// <summary>
        /// 将 UGUI 的 <see cref="TextAnchor"/> 映射为 TMPro.TextAlignmentOptions 并通过反射写入。
        /// TMP 默认对齐为 TopLeft，若不显式设置会导致文本全部贴在左上角。
        /// </summary>
        private static void ApplyTMPAlignment(Component c, TextAnchor align)
        {
            if (c == null) return;
            var alignmentProp = c.GetType().GetProperty("alignment");
            if (alignmentProp == null || !alignmentProp.PropertyType.IsEnum) return;

            string name;
            switch (align)
            {
                case TextAnchor.UpperLeft: name = "TopLeft"; break;
                case TextAnchor.UpperCenter: name = "Top"; break;
                case TextAnchor.UpperRight: name = "TopRight"; break;
                case TextAnchor.MiddleLeft: name = "Left"; break;
                case TextAnchor.MiddleCenter: name = "Center"; break;
                case TextAnchor.MiddleRight: name = "Right"; break;
                case TextAnchor.LowerLeft: name = "BottomLeft"; break;
                case TextAnchor.LowerCenter: name = "Bottom"; break;
                case TextAnchor.LowerRight: name = "BottomRight"; break;
                default: name = "TopLeft"; break;
            }

            if (Enum.TryParse(alignmentProp.PropertyType, name, true, out var alignVal))
                alignmentProp.SetValue(c, alignVal);
        }

        /// <summary>
        /// TMP 组件 AddComponent 后立即设置 text 可能因内部状态未初始化而丢失。
        /// 调用 ForceMeshUpdate 确保内部缓存同步，并通过 EditorUtility.SetDirty 持久化。
        /// 若 ForceMeshUpdate 后 text 仍为空，则二次赋值兜底。
        /// </summary>
        private static void EnsureTMPPersists(Component c, string text)
        {
            if (c == null) return;
            try
            {
                var forceMesh = c.GetType().GetMethod("ForceMeshUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                forceMesh?.Invoke(c, null);

                // 验证 text 是否成功写入，若丢失则二次赋值
                var textProp = c.GetType().GetProperty("text");
                if (textProp != null)
                {
                    var current = textProp.GetValue(c) as string;
                    if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(text))
                        textProp.SetValue(c, text);
                }
            }
            catch { }
            EditorUtility.SetDirty(c);
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
        public string Type, Name, Text, SpritePath, Placeholder, CheckmarkSpritePath;
        public Transform Parent;
        public int FontSize = 18;
        public Color? Color;
        public Vector2? Size;
        public TextAnchor Alignment = TextAnchor.MiddleLeft;
        public bool IsOn = true;
    }
}

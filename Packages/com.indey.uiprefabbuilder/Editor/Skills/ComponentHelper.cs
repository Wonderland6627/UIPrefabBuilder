using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class ComponentHelper
    {
        public static string SetImageColor(GameObject go, Color color)
        {
            var i = go?.GetComponent<Image>();
            if (i == null) return $"'{(go != null ? go.name : "null")}' has no Image component.";
            Undo.RecordObject(i, "Color");
            i.color = color;
            return null;
        }

        /// <summary>
        /// 尝试按资源路径加载 Sprite，失败时给出可诊断的错误信息（区分"路径不存在"与
        /// "资源存在但 TextureType 不是 Sprite"两种常见情况），避免静默赋 null 变成白方块。
        /// 返回 null 表示成功（或 path 为空即无需加载），否则返回错误描述。
        /// </summary>
        public static string LoadSpriteOrError(string path, out Sprite sprite)
        {
            sprite = null;
            if (string.IsNullOrEmpty(path)) return null;

            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) return null;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null)
                return $"Asset at '{path}' exists but is NOT imported as a Sprite (its TextureImporter textureType is not 'Sprite'). Change the importer to Sprite, or choose a Sprite asset.";
            return $"No Sprite found at asset path '{path}'. Verify the path is correct and the asset exists/imported.";
        }

        public static string SetImageSprite(GameObject go, string path)
        {
            var i = go?.GetComponent<Image>();
            if (i == null) return "Target has no Image component.";
            var err = LoadSpriteOrError(path, out var sp);
            if (err != null) return err;
            Undo.RecordObject(i, "Sprite");
            i.sprite = sp;
            return null;
        }

        /// <summary>
        /// 写入 Image 属性。<paramref name="missingImage"/> 为 true 时表示目标根本没有 Image 组件
        /// （调用方应视为失败），否则返回值仅描述 sprite 加载失败这类可继续的问题。
        /// <paramref name="spritePath"/> 为 null 表示"不改动 sprite"，为空字符串表示"清除 sprite"。
        /// </summary>
        public static string SetImageProperties(GameObject go, string spritePath, string type, string fillMethod,
            float? fillAmount, bool? preserveAspect, Color? color, out bool missingImage, out bool spriteCleared)
        {
            missingImage = false;
            spriteCleared = false;
            var img = go?.GetComponent<Image>();
            if (img == null)
            {
                missingImage = true;
                return "Target has no Image component.";
            }
            Undo.RecordObject(img, "Image Props");

            string spriteErr = null;
            // An explicitly empty path is the only way a caller can say "drop this artwork and
            // render a flat colour". Treating it as "not provided" silently keeps the wrong
            // sprite and merely tints it, which reads as a wrong-asset bug in the screenshot.
            if (spritePath != null && spritePath.Trim().Length == 0)
            {
                if (img.sprite != null) spriteCleared = true;
                img.sprite = null;
            }
            else if (!string.IsNullOrEmpty(spritePath))
            {
                spriteErr = LoadSpriteOrError(spritePath, out var sp);
                if (spriteErr == null)
                {
                    img.sprite = sp;
                    // Image.color multiplies the sprite pixels. Buttons are created with a blue
                    // default tint, which turns an assigned yellow sprite green unless reset.
                    if (!color.HasValue) img.color = Color.white;
                }
            }

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<Image.Type>(type, true, out var imgType))
                img.type = imgType;

            if (!string.IsNullOrEmpty(fillMethod) && Enum.TryParse<Image.FillMethod>(fillMethod, true, out var fm))
                img.fillMethod = fm;

            if (fillAmount.HasValue)
                img.fillAmount = fillAmount.Value;

            if (preserveAspect.HasValue)
                img.preserveAspect = preserveAspect.Value;

            if (color.HasValue)
                img.color = color.Value;

            return spriteErr;
        }

        /// <summary>
        /// 文本样式参数集。全部可选，仅赋值的字段会被写入，其余保持现状。
        /// </summary>
        public class TextStyleArgs
        {
            public string Text;
            public float? FontSize;
            public string Alignment;
            public string FontStyle;
            public Color? Color;
            public string Overflow;
            public string FontAssetPath;
            public float? OutlineWidth;
            public Color? OutlineColor;
            public float? CharacterSpacing;
            public float? LineSpacing;
            public bool? EnableAutoSizing;
            public float? FontSizeMin;
            public float? FontSizeMax;
            public bool? EnableWordWrapping;

            public bool HasTMPOnlyRequest =>
                OutlineWidth.HasValue || OutlineColor.HasValue || CharacterSpacing.HasValue
                || EnableAutoSizing.HasValue || FontSizeMin.HasValue || FontSizeMax.HasValue;
        }

        public static bool TrySetText(GameObject go, string text, out string message)
            => TryApplyTextStyle(go, new TextStyleArgs { Text = text }, out message);

        /// <summary>
        /// 写入文本内容与样式。返回 false 表示目标（及其子节点）没有可写的文本组件，调用方必须
        /// 视为失败——静默成功会让模型以为字号/颜色已生效，是设计稿还原偏差的主要来源之一。
        /// 返回 true 时 <paramref name="message"/> 可能带有警告（例如样式被重定向到唯一的文本子节点）。
        /// </summary>
        public static bool TryApplyTextStyle(GameObject go, TextStyleArgs style, out string message)
        {
            message = null;
            if (go == null) { message = "Target GameObject is null."; return false; }
            if (style == null) style = new TextStyleArgs();

            var comp = ResolveTextComponent(go, out var redirectedPath, out var resolveError);
            if (comp == null)
            {
                message = resolveError;
                return false;
            }

            var notes = new List<string>();
            if (redirectedPath != null)
                notes.Add($"'{go.name}' has no Text/TMP component of its own — the style was applied to its text child '{redirectedPath}'. Target that child directly next time.");

            Undo.RecordObject(comp, "Text Props");
            var legacy = comp as Text;
            if (legacy != null) ApplyLegacyTextStyle(legacy, style, notes);
            else ApplyTMPTextStyle(comp, style, notes);

            SyncInputFieldText(comp, style, notes);

            EditorUtility.SetDirty(comp);
            if (notes.Count > 0) message = string.Join(" ", notes);
            return true;
        }

        /// <summary>
        /// InputField/TMP_InputField 的权威文本是组件自身的 `text`：UpdateLabel() 会用它覆盖 label
        /// 子节点。只写子节点的话内容会在下一次刷新时消失，而工具却报告成功——输入框"缺文本"的根因。
        /// 这里把内容同步到输入框组件，并在整个输入框（正文+占位符）都为空时给出警告。
        /// </summary>
        private static void SyncInputFieldText(Component textComp, TextStyleArgs style, List<string> notes)
        {
            var input = FindOwningInputField(textComp, out var isLabel, out var isPlaceholder);
            if (input == null) return;

            var inputType = input.GetType();
            var inputTextProp = inputType.GetProperty("text");
            if (inputTextProp == null || inputTextProp.PropertyType != typeof(string) || !inputTextProp.CanWrite) return;

            if (isLabel && style.Text != null)
            {
                Undo.RecordObject(input, "InputField Text");
                inputTextProp.SetValue(input, style.Text);
                EditorUtility.SetDirty(input);
                notes.Add($"'{textComp.gameObject.name}' is the label of InputField '{input.gameObject.name}', "
                    + "so the content was written to the InputField component (its own `text` overwrites the label). "
                    + $"Target '{GetHierarchyPath(input.gameObject)}' directly next time.");
            }

            if (!isLabel && !isPlaceholder) return;

            var currentValue = inputTextProp.GetValue(input) as string;
            if (!string.IsNullOrEmpty(currentValue)) return;

            var placeholderGraphic = inputType.GetProperty("placeholder")?.GetValue(input) as Component;
            var placeholderText = placeholderGraphic != null
                ? placeholderGraphic.GetType().GetProperty("text")?.GetValue(placeholderGraphic) as string
                : null;
            if (!string.IsNullOrEmpty(placeholderText)) return;

            notes.Add($"InputField '{input.gameObject.name}' now shows NOTHING: both its text and its placeholder are empty. "
                + "Pass the mockup's content (or a placeholder string) — an empty field reads as a blank coloured box.");
        }

        private static Component FindOwningInputField(Component textComp, out bool isLabel, out bool isPlaceholder)
        {
            isLabel = false;
            isPlaceholder = false;
            if (textComp == null) return null;

            for (var current = textComp.transform; current != null; current = current.parent)
            {
                foreach (var comp in current.GetComponents<Component>())
                {
                    if (comp == null || comp == textComp) continue;
                    var type = comp.GetType();
                    if (!IsInputFieldType(type)) continue;
                    var labelProp = type.GetProperty("textComponent");
                    if (labelProp == null) continue;

                    var label = labelProp.GetValue(comp) as Component;
                    var placeholder = type.GetProperty("placeholder")?.GetValue(comp) as Component;
                    if (label != textComp && placeholder != textComp) continue;

                    isLabel = label == textComp;
                    isPlaceholder = placeholder == textComp;
                    return comp;
                }
            }
            return null;
        }

        private static void ApplyLegacyTextStyle(Text legacy, TextStyleArgs style, List<string> notes)
        {
            if (style.Text != null) legacy.text = style.Text;
            if (style.FontSize.HasValue) legacy.fontSize = Mathf.RoundToInt(style.FontSize.Value);
            if (style.Color.HasValue) legacy.color = style.Color.Value;
            if (style.LineSpacing.HasValue) legacy.lineSpacing = style.LineSpacing.Value;

            if (!string.IsNullOrEmpty(style.Alignment))
            {
                switch (style.Alignment.ToLowerInvariant())
                {
                    case "left": legacy.alignment = TextAnchor.MiddleLeft; break;
                    case "center": legacy.alignment = TextAnchor.MiddleCenter; break;
                    case "right": legacy.alignment = TextAnchor.MiddleRight; break;
                    case "topleft": legacy.alignment = TextAnchor.UpperLeft; break;
                    case "top": legacy.alignment = TextAnchor.UpperCenter; break;
                    case "bottom": legacy.alignment = TextAnchor.LowerCenter; break;
                }
            }

            if (!string.IsNullOrEmpty(style.FontStyle) && Enum.TryParse<FontStyle>(style.FontStyle, true, out var fs))
                legacy.fontStyle = fs;

            if (!string.IsNullOrEmpty(style.Overflow))
            {
                switch (style.Overflow.ToLowerInvariant())
                {
                    case "overflow":
                        legacy.horizontalOverflow = HorizontalWrapMode.Overflow;
                        legacy.verticalOverflow = VerticalWrapMode.Overflow;
                        break;
                    case "truncate":
                    case "ellipsis":
                        legacy.horizontalOverflow = HorizontalWrapMode.Wrap;
                        legacy.verticalOverflow = VerticalWrapMode.Truncate;
                        break;
                }
            }

            if (style.EnableWordWrapping.HasValue)
                legacy.horizontalOverflow = style.EnableWordWrapping.Value ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;

            if (!string.IsNullOrEmpty(style.FontAssetPath))
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(style.FontAssetPath);
                if (font != null) legacy.font = font;
                else notes.Add($"No Font asset at '{style.FontAssetPath}' — the font was NOT changed.");
            }

            if (style.HasTMPOnlyRequest)
                notes.Add("This is a Legacy UI.Text component: outline/characterSpacing/auto-sizing were ignored. Use `add_outline` for an outline, or build the label as TextMeshPro.");
        }

        private static void ApplyTMPTextStyle(Component c, TextStyleArgs style, List<string> notes)
        {
            var t = c.GetType();
            var textProp = t.GetProperty("text");

            if (style.Text != null) textProp?.SetValue(c, style.Text);
            if (style.FontSize.HasValue) SetReflectionProp(c, "fontSize", style.FontSize.Value);
            if (style.Color.HasValue) SetReflectionProp(c, "color", style.Color.Value);
            if (style.CharacterSpacing.HasValue) SetReflectionProp(c, "characterSpacing", style.CharacterSpacing.Value);
            if (style.LineSpacing.HasValue) SetReflectionProp(c, "lineSpacing", style.LineSpacing.Value);
            if (style.EnableAutoSizing.HasValue) SetReflectionProp(c, "enableAutoSizing", style.EnableAutoSizing.Value);
            if (style.FontSizeMin.HasValue) SetReflectionProp(c, "fontSizeMin", style.FontSizeMin.Value);
            if (style.FontSizeMax.HasValue) SetReflectionProp(c, "fontSizeMax", style.FontSizeMax.Value);
            if (style.EnableWordWrapping.HasValue) SetReflectionProp(c, "enableWordWrapping", style.EnableWordWrapping.Value);

            if (!string.IsNullOrEmpty(style.Alignment))
            {
                var alignmentProp = t.GetProperty("alignment");
                if (alignmentProp != null && alignmentProp.PropertyType.IsEnum)
                {
                    var mapped = MapTMPAlignment(style.Alignment);
                    if (Enum.TryParse(alignmentProp.PropertyType, mapped, true, out var alignVal))
                        alignmentProp.SetValue(c, alignVal);
                    else
                        notes.Add($"Unknown alignment '{style.Alignment}' — alignment was NOT changed.");
                }
            }

            if (!string.IsNullOrEmpty(style.FontStyle))
            {
                var styleProp = t.GetProperty("fontStyle");
                if (styleProp != null && styleProp.PropertyType.IsEnum)
                {
                    // TMPro.FontStyles is a flags enum without a 'BoldAndItalic' member.
                    var mapped = style.FontStyle.Equals("BoldAndItalic", StringComparison.OrdinalIgnoreCase)
                        ? "Bold, Italic"
                        : style.FontStyle;
                    if (Enum.TryParse(styleProp.PropertyType, mapped, true, out var styleVal))
                        styleProp.SetValue(c, styleVal);
                    else
                        notes.Add($"Unknown fontStyle '{style.FontStyle}' — fontStyle was NOT changed.");
                }
            }

            if (!string.IsNullOrEmpty(style.Overflow))
            {
                var overflowProp = t.GetProperty("overflowMode");
                if (overflowProp != null && overflowProp.PropertyType.IsEnum
                    && Enum.TryParse(overflowProp.PropertyType, style.Overflow, true, out var ovVal))
                    overflowProp.SetValue(c, ovVal);
            }

            if (!string.IsNullOrEmpty(style.FontAssetPath))
                ApplyTMPFontAsset(c, style.FontAssetPath, notes);

            if (style.OutlineWidth.HasValue || style.OutlineColor.HasValue)
                ApplyTMPOutline(c, style.OutlineWidth, style.OutlineColor, notes);

            ForceTMPMeshUpdate(c);

            if (style.Text != null && textProp != null)
            {
                var current = textProp.GetValue(c) as string;
                if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(style.Text))
                    textProp.SetValue(c, style.Text);
            }
        }

        private static void ApplyTMPFontAsset(Component c, string path, List<string> notes)
        {
            var fontProp = c.GetType().GetProperty("font");
            if (fontProp == null || !fontProp.CanWrite)
            {
                notes.Add("This text component exposes no writable `font` property — the font asset was NOT changed.");
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath(path, fontProp.PropertyType);
            if (asset == null)
            {
                notes.Add($"No {fontProp.PropertyType.Name} asset at '{path}' — the font was NOT changed. Search for the project's font asset first.");
                return;
            }
            fontProp.SetValue(c, asset);
        }

        /// <summary>
        /// TMP 的描边写在材质上。先读取 `fontMaterial` 触发材质实例化，否则 TMP 会把描边写进
        /// 字体资源的共享材质，污染全工程所有使用该字体的文本。
        /// </summary>
        private static void ApplyTMPOutline(Component c, float? width, Color? color, List<string> notes)
        {
            var t = c.GetType();
            var fontMaterialProp = t.GetProperty("fontMaterial");
            if (fontMaterialProp == null)
            {
                notes.Add("This text component has no `fontMaterial` — outline was NOT applied. Use `add_outline` instead.");
                return;
            }

            var material = fontMaterialProp.GetValue(c) as Material;
            if (material == null)
            {
                notes.Add("Could not instantiate the text material — outline was NOT applied. Use `add_outline` instead.");
                return;
            }

            if (color.HasValue)
            {
                var colorProp = t.GetProperty("outlineColor");
                if (colorProp != null && colorProp.PropertyType == typeof(Color32))
                    colorProp.SetValue(c, (Color32)color.Value);
                else
                    material.SetColor("_OutlineColor", color.Value);
            }

            if (width.HasValue)
            {
                var widthProp = t.GetProperty("outlineWidth");
                if (widthProp != null && widthProp.CanWrite)
                    widthProp.SetValue(c, Mathf.Clamp01(width.Value));
                else
                    material.SetFloat("_OutlineWidth", Mathf.Clamp01(width.Value));

                if (width.Value > 0f) material.EnableKeyword("OUTLINE_ON");
            }
            else if (material.HasProperty("_OutlineWidth") && material.GetFloat("_OutlineWidth") <= 0f)
            {
                notes.Add("An outline colour was set but outlineWidth is still 0, so no outline is visible — pass outlineWidth (e.g. 0.2) as well.");
            }

            // The glyph mesh reserves padding for effects at generation time; without this the
            // outline is clipped even though the material is correct.
            var updatePadding = t.GetMethod("UpdateMeshPadding", BindingFlags.Instance | BindingFlags.Public);
            try { updatePadding?.Invoke(c, null); }
            catch { }

            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// 定位实际渲染文本的组件：优先目标自身，否则退回唯一的文本子节点。Button/Tab 这类容器把
        /// 标签放在子节点上，模型常把样式写到根节点；有多个候选时拒绝并列出路径，而不是随便挑一个。
        /// </summary>
        private static Component ResolveTextComponent(GameObject go, out string redirectedPath, out string error)
        {
            redirectedPath = null;
            error = null;

            var own = FindTextComponentOn(go);
            if (own != null) return own;

            // An InputField root has two text descendants (Placeholder + Text), so the generic
            // "single text child" rule would reject it. Its label is unambiguous, though.
            var inputLabel = FindInputFieldLabelOn(go);
            if (inputLabel != null)
            {
                redirectedPath = GetHierarchyPath(inputLabel.gameObject);
                return inputLabel;
            }

            var candidates = new List<Component>();
            foreach (var child in go.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child.gameObject == go) continue;
                var found = FindTextComponentOn(child.gameObject);
                if (found != null) candidates.Add(found);
            }

            if (candidates.Count == 1)
            {
                redirectedPath = GetHierarchyPath(candidates[0].gameObject);
                return candidates[0];
            }

            error = candidates.Count == 0
                ? $"'{go.name}' has no Text/TextMeshPro component (nor do its children). NOTHING was changed — target the object that actually renders the label."
                : $"'{go.name}' has no Text/TextMeshPro component of its own, and has {candidates.Count} text descendants: "
                    + string.Join(", ", candidates.Select(x => GetHierarchyPath(x.gameObject)))
                    + ". NOTHING was changed — target the specific child you meant.";
            return null;
        }

        private static Component FindInputFieldLabelOn(GameObject go)
        {
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null || !IsInputFieldType(comp.GetType())) continue;
                var labelProp = comp.GetType().GetProperty("textComponent");
                if (labelProp == null) continue;
                var label = labelProp.GetValue(comp) as Component;
                if (label != null) return label;
            }
            return null;
        }

        internal static bool IsInputFieldType(Type type)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.Name == "InputField" || t.Name == "TMP_InputField") return true;
            }
            return false;
        }

        /// <summary>
        /// 文本组件必须同时是 Graphic：InputField 之类也有可写的 string `text` 属性，
        /// 但它不渲染文字，不能被当作样式目标。
        /// </summary>
        private static Component FindTextComponentOn(GameObject go)
        {
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

        internal static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "";
            var path = go.name;
            var t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }

        public static CanvasGroup AddCanvasGroup(GameObject go, float alpha = 1, bool interact = true, bool block = true)
        {
            var g = go.GetComponent<CanvasGroup>();
            if (g == null) { Undo.AddComponent<CanvasGroup>(go); g = go.GetComponent<CanvasGroup>(); }
            Undo.RecordObject(g, "CanvasGroup");
            g.alpha = alpha; g.interactable = interact; g.blocksRaycasts = block;
            return g;
        }

        public static Component AddMask(GameObject go, string maskType, bool showGraphic)
        {
            if (maskType == "Mask" || string.IsNullOrEmpty(maskType))
            {
                var m = go.GetComponent<Mask>();
                if (m == null) { Undo.AddComponent<Mask>(go); m = go.GetComponent<Mask>(); }
                Undo.RecordObject(m, "Mask");
                m.showMaskGraphic = showGraphic;
                return m;
            }
            else
            {
                var m = go.GetComponent<RectMask2D>();
                if (m == null) { Undo.AddComponent<RectMask2D>(go); m = go.GetComponent<RectMask2D>(); }
                return m;
            }
        }

        public static Component AddOutline(GameObject go, string effectType, Color color, Vector2 distance)
        {
            if (effectType == "Shadow")
            {
                var s = Undo.AddComponent<Shadow>(go);
                s.effectColor = color;
                s.effectDistance = distance;
                return s;
            }
            var o = Undo.AddComponent<Outline>(go);
            o.effectColor = color;
            o.effectDistance = distance;
            return o;
        }

        public static string ConfigureSelectable(GameObject go, string transition, string navMode, bool interactable)
        {
            var sel = go?.GetComponent<Selectable>();
            if (sel == null)
                return $"'{(go != null ? go.name : "null")}' has no Selectable (Button/Toggle/Slider) component. Nothing was changed.";
            Undo.RecordObject(sel, "Selectable");
            sel.interactable = interactable;

            if (!string.IsNullOrEmpty(transition) && Enum.TryParse<Selectable.Transition>(transition, true, out var trans))
                sel.transition = trans;

            if (!string.IsNullOrEmpty(navMode) && Enum.TryParse<Navigation.Mode>(navMode, true, out var mode))
                sel.navigation = new Navigation { mode = mode };

            EditorUtility.SetDirty(sel);
            return null;
        }

        /// <summary>
        /// 每个视觉状态的 tint 颜色与 sprite。设计稿里的 Tab / 高亮选项靠的就是这些状态差异，
        /// 只配置 transition 类型是无法表达出来的。
        /// </summary>
        public class SelectableStateArgs
        {
            public string Transition;
            public Color? NormalColor, HighlightedColor, PressedColor, SelectedColor, DisabledColor;
            public float? ColorMultiplier, FadeDuration;
            public string HighlightedSpritePath, PressedSpritePath, SelectedSpritePath, DisabledSpritePath;
            public string TargetGraphicTarget;
            public bool? Interactable;

            public bool HasAnySprite =>
                !string.IsNullOrEmpty(HighlightedSpritePath) || !string.IsNullOrEmpty(PressedSpritePath)
                || !string.IsNullOrEmpty(SelectedSpritePath) || !string.IsNullOrEmpty(DisabledSpritePath);

            public bool HasAnyColor =>
                NormalColor.HasValue || HighlightedColor.HasValue || PressedColor.HasValue
                || SelectedColor.HasValue || DisabledColor.HasValue;
        }

        /// <summary>
        /// 写入 Selectable 的 ColorBlock / SpriteState。返回 false 表示目标没有 Selectable，
        /// <paramref name="message"/> 在成功时可能携带 sprite 加载警告。
        /// </summary>
        public static bool TryConfigureSelectableStates(GameObject go, SelectableStateArgs args, out string message)
        {
            message = null;
            if (go == null) { message = "Target GameObject is null."; return false; }
            if (args == null) args = new SelectableStateArgs();

            var sel = go.GetComponent<Selectable>();
            if (sel == null)
            {
                message = $"'{go.name}' has no Selectable (Button/Toggle/Slider) component — state colors/sprites cannot be applied. "
                    + "Create the element as a button/toggle/tab first, or target the child that carries the Selectable.";
                return false;
            }

            var notes = new List<string>();
            Undo.RecordObject(sel, "Selectable States");

            if (args.Interactable.HasValue) sel.interactable = args.Interactable.Value;

            if (!string.IsNullOrEmpty(args.TargetGraphicTarget))
            {
                var graphicGO = args.TargetGraphicTarget == "self"
                    ? go
                    : go.transform.Find(args.TargetGraphicTarget)?.gameObject;
                var graphic = graphicGO?.GetComponent<Graphic>();
                if (graphic != null) sel.targetGraphic = graphic;
                else notes.Add($"No Graphic found for targetGraphic '{args.TargetGraphicTarget}' — targetGraphic was NOT changed.");
            }
            else if (sel.targetGraphic == null)
            {
                var ownGraphic = go.GetComponent<Graphic>();
                if (ownGraphic != null) sel.targetGraphic = ownGraphic;
            }

            if (args.HasAnyColor || args.ColorMultiplier.HasValue || args.FadeDuration.HasValue)
            {
                var colors = sel.colors;
                if (args.NormalColor.HasValue) colors.normalColor = args.NormalColor.Value;
                if (args.HighlightedColor.HasValue) colors.highlightedColor = args.HighlightedColor.Value;
                if (args.PressedColor.HasValue) colors.pressedColor = args.PressedColor.Value;
                if (args.SelectedColor.HasValue) colors.selectedColor = args.SelectedColor.Value;
                if (args.DisabledColor.HasValue) colors.disabledColor = args.DisabledColor.Value;
                if (args.ColorMultiplier.HasValue) colors.colorMultiplier = args.ColorMultiplier.Value;
                if (args.FadeDuration.HasValue) colors.fadeDuration = args.FadeDuration.Value;
                sel.colors = colors;
            }

            if (args.HasAnySprite)
            {
                var state = sel.spriteState;
                state.highlightedSprite = ResolveStateSprite(args.HighlightedSpritePath, "highlighted", state.highlightedSprite, notes);
                state.pressedSprite = ResolveStateSprite(args.PressedSpritePath, "pressed", state.pressedSprite, notes);
                state.selectedSprite = ResolveStateSprite(args.SelectedSpritePath, "selected", state.selectedSprite, notes);
                state.disabledSprite = ResolveStateSprite(args.DisabledSpritePath, "disabled", state.disabledSprite, notes);
                sel.spriteState = state;
            }

            if (!string.IsNullOrEmpty(args.Transition) && Enum.TryParse<Selectable.Transition>(args.Transition, true, out var trans))
                sel.transition = trans;
            else if (args.HasAnySprite)
                sel.transition = Selectable.Transition.SpriteSwap;
            else if (args.HasAnyColor && sel.transition == Selectable.Transition.None)
                sel.transition = Selectable.Transition.ColorTint;

            if (sel.transition == Selectable.Transition.SpriteSwap && sel.targetGraphic == null)
                notes.Add("SpriteSwap needs a targetGraphic Image, but none is assigned — the state sprites will not show.");

            EditorUtility.SetDirty(sel);
            if (notes.Count > 0) message = string.Join(" ", notes);
            return true;
        }

        private static Sprite ResolveStateSprite(string path, string stateName, Sprite current, List<string> notes)
        {
            if (string.IsNullOrEmpty(path)) return current;
            var err = LoadSpriteOrError(path, out var sprite);
            if (err == null) return sprite;
            notes.Add($"{stateName}Sprite: {err}");
            return current;
        }

        /// <summary>
        /// Toggle 的 graphic（选中时显示的图形）不是普通子对象，必须显式接线，
        /// 否则设计稿里的"当前选中项高亮"在运行时不会随选中状态出现/消失。
        /// </summary>
        public static bool TryWireToggleGraphics(GameObject go, string backgroundTarget, string selectedGraphicTarget, out string message)
        {
            message = null;
            var toggle = go?.GetComponent<Toggle>();
            if (toggle == null)
            {
                message = $"'{(go != null ? go.name : "null")}' has no Toggle component.";
                return false;
            }

            var notes = new List<string>();
            Undo.RecordObject(toggle, "Toggle Graphics");

            if (!string.IsNullOrEmpty(backgroundTarget))
            {
                var bg = ResolveChildGraphic(go, backgroundTarget);
                if (bg != null) toggle.targetGraphic = bg;
                else notes.Add($"No Graphic at '{backgroundTarget}' — targetGraphic was NOT changed.");
            }

            if (!string.IsNullOrEmpty(selectedGraphicTarget))
            {
                var mark = ResolveChildGraphic(go, selectedGraphicTarget);
                if (mark != null) toggle.graphic = mark;
                else notes.Add($"No Graphic at '{selectedGraphicTarget}' — the selected-state graphic was NOT changed.");
            }

            EditorUtility.SetDirty(toggle);
            if (notes.Count > 0) message = string.Join(" ", notes);
            return true;
        }

        private static Graphic ResolveChildGraphic(GameObject root, string relativePath)
        {
            if (string.Equals(relativePath, "self", StringComparison.OrdinalIgnoreCase))
                return root.GetComponent<Graphic>();
            var t = root.transform.Find(relativePath);
            return t != null ? t.GetComponent<Graphic>() : null;
        }

        #region Generic Component Reflection

        private static readonly string[] NamespaceSearch = {
            "UnityEngine.UI.", "TMPro.", "UnityEngine.", "UnityEngine.EventSystems.", ""
        };

        public static Type FindComponentType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            foreach (var ns in NamespaceSearch)
            {
                var fullName = ns + typeName;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType(fullName);
                    if (t != null && typeof(Component).IsAssignableFrom(t))
                        return t;
                }
            }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var match = asm.GetTypes().FirstOrDefault(t =>
                        t.Name == typeName && typeof(Component).IsAssignableFrom(t));
                    if (match != null) return match;
                }
                catch { }
            }
            return null;
        }

        public static Component AddComponentByTypeName(GameObject go, string typeName)
        {
            var type = FindComponentType(typeName);
            if (type == null) return null;

            var existing = go.GetComponent(type);
            if (existing != null) return existing;

            return Undo.AddComponent(go, type);
        }

        public static bool SetComponentProperty(GameObject go, string typeName, string propertyName, string value, string assetPath)
        {
            var type = FindComponentType(typeName);
            if (type == null) return false;

            var comp = go.GetComponent(type);
            if (comp == null) return false;

            Undo.RecordObject(comp, "Set Property");

            var prop = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite)
            {
                var converted = ConvertValue(value, prop.PropertyType, assetPath);
                if (converted != null) { prop.SetValue(comp, converted); EditorUtility.SetDirty(comp); return true; }
            }

            var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
            {
                var converted = ConvertValue(value, field.FieldType, assetPath);
                if (converted != null) { field.SetValue(comp, converted); EditorUtility.SetDirty(comp); return true; }
            }

            return false;
        }

        private static object ConvertValue(string value, Type targetType, string assetPath)
        {
            if (!string.IsNullOrEmpty(assetPath))
                return AssetDatabase.LoadAssetAtPath(assetPath, targetType);

            if (targetType == typeof(bool))
                return bool.TryParse(value, out var b) && b;

            if (targetType == typeof(int))
                return int.TryParse(value, out var i) ? (object)i : null;

            if (targetType == typeof(float))
                return float.TryParse(value, out var f) ? (object)f : null;

            if (targetType == typeof(double))
                return double.TryParse(value, out var d) ? (object)d : null;

            if (targetType == typeof(string))
                return value;

            if (targetType.IsEnum)
                return Enum.TryParse(targetType, value, true, out var e) ? e : null;

            if (targetType == typeof(Color))
            {
                if (ColorUtility.TryParseHtmlString(value, out var c)) return c;
                var parts = value.Split(',');
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[0].Trim(), out var r);
                    float.TryParse(parts[1].Trim(), out var g);
                    float.TryParse(parts[2].Trim(), out var bv);
                    float a = parts.Length >= 4 && float.TryParse(parts[3].Trim(), out var av) ? av : 1f;
                    return new Color(r, g, bv, a);
                }
            }

            if (targetType == typeof(Vector2))
            {
                var parts = value.Split(',');
                if (parts.Length >= 2 && float.TryParse(parts[0].Trim(), out var x) && float.TryParse(parts[1].Trim(), out var y))
                    return new Vector2(x, y);
            }

            if (targetType == typeof(Vector3))
            {
                var parts = value.Split(',');
                if (parts.Length >= 3 && float.TryParse(parts[0].Trim(), out var x) && float.TryParse(parts[1].Trim(), out var y) && float.TryParse(parts[2].Trim(), out var z))
                    return new Vector3(x, y, z);
            }

            return null;
        }

        #endregion

        /// <summary>
        /// 调用 TMP 组件的 ForceMeshUpdate 以确保内部缓存同步（text 赋值后调用）。
        /// </summary>
        private static void ForceTMPMeshUpdate(Component c)
        {
            if (c == null) return;
            try
            {
                var m = c.GetType().GetMethod("ForceMeshUpdate", BindingFlags.Instance | BindingFlags.Public);
                m?.Invoke(c, null);
            }
            catch { }
        }

        private static void SetReflectionProp(object obj, string propName, object value)
        {
            if (value == null) return;
            var prop = obj.GetType().GetProperty(propName);
            if (prop != null && prop.CanWrite) { prop.SetValue(obj, value); return; }
            var field = obj.GetType().GetField(propName);
            if (field != null) field.SetValue(obj, value);
        }

        private static string MapTMPAlignment(string alignment)
        {
            switch (alignment.ToLowerInvariant())
            {
                case "left": return "Left";
                case "center": return "Center";
                case "right": return "Right";
                case "justified": return "Justified";
                default: return alignment;
            }
        }
    }
}

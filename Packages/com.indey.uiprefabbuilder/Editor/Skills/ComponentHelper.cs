using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class ComponentHelper
    {
        public static void SetImageColor(GameObject go, Color color)
        {
            var i = go?.GetComponent<Image>();
            if (i == null) return;
            Undo.RecordObject(i, "Color");
            i.color = color;
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
        /// </summary>
        public static string SetImageProperties(GameObject go, string spritePath, string type, string fillMethod,
            float? fillAmount, bool? preserveAspect, Color? color, out bool missingImage)
        {
            missingImage = false;
            var img = go?.GetComponent<Image>();
            if (img == null)
            {
                missingImage = true;
                return "Target has no Image component.";
            }
            Undo.RecordObject(img, "Image Props");

            string spriteErr = null;
            if (!string.IsNullOrEmpty(spritePath))
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

        public static void SetText(GameObject go, string text)
        {
            var legacy = go?.GetComponent<Text>();
            if (legacy != null) { Undo.RecordObject(legacy, "Text"); legacy.text = text; EditorUtility.SetDirty(legacy); return; }
            foreach (var c in go.GetComponents<Component>())
            {
                var p = c?.GetType().GetProperty("text");
                if (p != null && p.PropertyType == typeof(string))
                {
                    Undo.RecordObject(c, "TMP");
                    p.SetValue(c, text);
                    ForceTMPMeshUpdate(c);
                    EditorUtility.SetDirty(c);
                    return;
                }
            }
        }

        public static void SetTextProperties(GameObject go, string text, int? fontSize, string alignment,
            string fontStyle, Color? color, string overflow)
        {
            var legacy = go?.GetComponent<Text>();
            if (legacy != null)
            {
                Undo.RecordObject(legacy, "Text Props");
                if (text != null) legacy.text = text;
                if (fontSize.HasValue) legacy.fontSize = fontSize.Value;
                if (color.HasValue) legacy.color = color.Value;
                if (!string.IsNullOrEmpty(alignment))
                {
                    switch (alignment.ToLowerInvariant())
                    {
                        case "left": legacy.alignment = TextAnchor.MiddleLeft; break;
                        case "center": legacy.alignment = TextAnchor.MiddleCenter; break;
                        case "right": legacy.alignment = TextAnchor.MiddleRight; break;
                    }
                }
                if (!string.IsNullOrEmpty(fontStyle) && Enum.TryParse<FontStyle>(fontStyle, true, out var fs))
                    legacy.fontStyle = fs;
                if (!string.IsNullOrEmpty(overflow))
                {
                    switch (overflow.ToLowerInvariant())
                    {
                        case "overflow": legacy.horizontalOverflow = HorizontalWrapMode.Overflow; break;
                        case "truncate": case "ellipsis": legacy.horizontalOverflow = HorizontalWrapMode.Wrap; break;
                    }
                }
                return;
            }

            foreach (var c in go.GetComponents<Component>())
            {
                var t = c?.GetType();
                if (t == null) continue;
                var textProp = t.GetProperty("text");
                if (textProp == null || textProp.PropertyType != typeof(string)) continue;

                Undo.RecordObject(c, "TMP Props");
                if (text != null) textProp.SetValue(c, text);
                SetReflectionProp(c, "fontSize", fontSize.HasValue ? (object)(float)fontSize.Value : null);
                if (color.HasValue) SetReflectionProp(c, "color", color.Value);

                if (!string.IsNullOrEmpty(alignment))
                {
                    var alignmentProp = t.GetProperty("alignment");
                    if (alignmentProp != null && alignmentProp.PropertyType.IsEnum)
                    {
                        var mapped = MapTMPAlignment(alignment);
                        if (Enum.TryParse(alignmentProp.PropertyType, mapped, true, out var alignVal))
                            alignmentProp.SetValue(c, alignVal);
                    }
                }

                if (!string.IsNullOrEmpty(fontStyle))
                {
                    var styleProp = t.GetProperty("fontStyle");
                    if (styleProp != null && styleProp.PropertyType.IsEnum
                        && Enum.TryParse(styleProp.PropertyType, fontStyle, true, out var styleVal))
                        styleProp.SetValue(c, styleVal);
                }

                if (!string.IsNullOrEmpty(overflow))
                {
                    var overflowProp = t.GetProperty("overflowMode");
                    if (overflowProp != null && overflowProp.PropertyType.IsEnum
                        && Enum.TryParse(overflowProp.PropertyType, overflow, true, out var ovVal))
                        overflowProp.SetValue(c, ovVal);
                }

                ForceTMPMeshUpdate(c);

                // 验证 text 是否成功写入，若丢失则二次赋值兜底
                if (text != null)
                {
                    var current = textProp.GetValue(c) as string;
                    if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(text))
                        textProp.SetValue(c, text);
                }
                EditorUtility.SetDirty(c);
                return;
            }
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

        public static void ConfigureSelectable(GameObject go, string transition, string navMode, bool interactable)
        {
            var sel = go?.GetComponent<Selectable>();
            if (sel == null) return;
            Undo.RecordObject(sel, "Selectable");
            sel.interactable = interactable;

            if (!string.IsNullOrEmpty(transition) && Enum.TryParse<Selectable.Transition>(transition, true, out var trans))
                sel.transition = trans;

            if (!string.IsNullOrEmpty(navMode) && Enum.TryParse<Navigation.Mode>(navMode, true, out var mode))
                sel.navigation = new Navigation { mode = mode };
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

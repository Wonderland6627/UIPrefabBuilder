using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class HierarchyInspector
    {
        public static string Describe(GameObject root, int maxDepth = 4)
        {
            if (root == null) return "(null)";
            var sb = new StringBuilder();
            Walk(sb, root, 0, maxDepth);
            return sb.ToString();
        }

        public static string DescribeComponents(GameObject go)
        {
            if (go == null) return "(null)";
            var sb = new StringBuilder();
            sb.AppendLine($"GameObject: {go.name} (active={go.activeSelf})");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine($"  [{c.GetType().Name}]");
                var rt = c as RectTransform;
                if (rt != null)
                    sb.AppendLine($"    anchor={rt.anchorMin}-{rt.anchorMax} pivot={rt.pivot} size={rt.sizeDelta} pos={rt.anchoredPosition}");
                var img = c as Image;
                if (img != null)
                {
                    sb.AppendLine($"    color={FormatColor(img.color)} sprite={(img.sprite != null ? img.sprite.name : "NONE (flat colour block)")}" +
                        $" type={img.type} preserveAspect={img.preserveAspect}");
                }
                var txt = c as Text;
                if (txt != null)
                {
                    sb.AppendLine($"    text=\"{txt.text}\" fontSize={txt.fontSize} color={FormatColor(txt.color)}" +
                        $" alignment={txt.alignment} fontStyle={txt.fontStyle} font={(txt.font != null ? txt.font.name : "none")}");
                }

                // 通过反射读取 TMP 的样式字段：模型无法从缩略截图里判断字号/颜色是否真的生效，
                // 只能靠 inspect 输出核对，因此这里必须把样式全部暴露出来。
                if (txt == null && c is Graphic && HasStringTextProperty(c))
                    AppendTMPDetails(sb, c);

                var selectable = c as Selectable;
                if (selectable != null)
                {
                    var colors = selectable.colors;
                    var state = selectable.spriteState;
                    sb.AppendLine($"    transition={selectable.transition} interactable={selectable.interactable}" +
                        $" targetGraphic={(selectable.targetGraphic != null ? selectable.targetGraphic.gameObject.name : "NONE")}");
                    sb.AppendLine($"    stateColors: normal={FormatColor(colors.normalColor)} highlighted={FormatColor(colors.highlightedColor)}" +
                        $" pressed={FormatColor(colors.pressedColor)} selected={FormatColor(colors.selectedColor)}");
                    sb.AppendLine($"    stateSprites: highlighted={SpriteName(state.highlightedSprite)} pressed={SpriteName(state.pressedSprite)}" +
                        $" selected={SpriteName(state.selectedSprite)} disabled={SpriteName(state.disabledSprite)}");
                }

                var toggle = c as Toggle;
                if (toggle != null)
                {
                    sb.AppendLine($"    isOn={toggle.isOn} group={(toggle.group != null ? toggle.group.gameObject.name : "none")}" +
                        $" selectedGraphic={(toggle.graphic != null ? toggle.graphic.gameObject.name : "NONE (no selected-state highlight)")}");
                }

                AppendInputFieldDetails(sb, c);
            }
            return sb.ToString();
        }

        private static void AppendTMPDetails(StringBuilder sb, Component c)
        {
            var type = c.GetType();
            var text = type.GetProperty("text")?.GetValue(c) as string ?? "";
            sb.AppendLine($"    text=\"{text}\" fontSize={Read(c, "fontSize")} color={FormatColorValue(Read(c, "color"))}" +
                $" alignment={Read(c, "alignment")} fontStyle={Read(c, "fontStyle")}");

            var font = Read(c, "font");
            var fontName = font is UnityEngine.Object fontObj && fontObj != null ? fontObj.name : "none";
            sb.AppendLine($"    font={fontName} outlineWidth={Read(c, "outlineWidth")} outlineColor={FormatColorValue(Read(c, "outlineColor"))}" +
                $" characterSpacing={Read(c, "characterSpacing")} autoSize={Read(c, "enableAutoSizing")}");
        }

        private static void AppendInputFieldDetails(StringBuilder sb, Component c)
        {
            var type = c.GetType();
            bool isInputField = false;
            for (var t = type; t != null; t = t.BaseType)
            {
                if (t.Name == "InputField" || t.Name == "TMP_InputField") { isInputField = true; break; }
            }
            if (!isInputField) return;

            var textComponent = Read(c, "textComponent") as Component;
            var placeholder = Read(c, "placeholder") as Component;
            sb.AppendLine($"    textComponent={(textComponent != null ? textComponent.gameObject.name : "NOT WIRED")}" +
                $" placeholder={(placeholder != null ? placeholder.gameObject.name : "NOT WIRED")}");
        }

        private static bool HasStringTextProperty(Component c)
        {
            var p = c.GetType().GetProperty("text");
            return p != null && p.PropertyType == typeof(string);
        }

        private static object Read(Component c, string propertyName)
        {
            try { return c.GetType().GetProperty(propertyName)?.GetValue(c); }
            catch { return null; }
        }

        private static string SpriteName(Sprite sprite) => sprite != null ? sprite.name : "none";

        private static string FormatColor(Color c)
            => $"({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";

        private static string FormatColorValue(object value)
        {
            if (value is Color c) return FormatColor(c);
            if (value is Color32 c32) return FormatColor(c32);
            return "n/a";
        }

        private static void Walk(StringBuilder sb, GameObject go, int depth, int max)
        {
            if (depth > max) return;
            sb.AppendLine(new string(' ', depth * 2) + go.name + $" [{go.GetComponents<Component>().Length} comps]");
            for (int i = 0; i < go.transform.childCount; i++)
                Walk(sb, go.transform.GetChild(i).gameObject, depth + 1, max);
        }
    }
}

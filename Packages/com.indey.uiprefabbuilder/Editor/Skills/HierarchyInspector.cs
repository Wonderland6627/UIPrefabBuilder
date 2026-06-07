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
                    sb.AppendLine($"    color={img.color} sprite={(img.sprite != null ? img.sprite.name : "none")}");
                var txt = c as Text;
                if (txt != null)
                    sb.AppendLine($"    text=\"{txt.text}\" fontSize={txt.fontSize}");
            }
            return sb.ToString();
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

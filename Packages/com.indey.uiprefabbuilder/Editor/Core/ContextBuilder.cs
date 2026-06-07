using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Core
{
    public static class ContextBuilder
    {
        public static string BuildSelectionContext()
        {
            var go = Selection.activeGameObject;
            if (go == null) return BuildSceneOverview();

            var sb = new StringBuilder();
            sb.AppendLine("## Current Selection");
            AppendTree(sb, go, 0, 4);
            return sb.ToString();
        }

        public static string BuildSceneOverview()
        {
            var canvases = Object.FindObjectsOfType<Canvas>();
            var sb = new StringBuilder();
            sb.AppendLine("## Scene UI Overview");
            if (canvases.Length == 0) { sb.AppendLine("No Canvas found."); return sb.ToString(); }

            foreach (var c in canvases)
            {
                sb.AppendLine($"- Canvas: {GetPath(c.gameObject)} (mode={c.renderMode})");
                for (int i = 0; i < c.transform.childCount && i < 10; i++)
                    sb.AppendLine($"  - {c.transform.GetChild(i).name}");
            }
            return sb.ToString();
        }

        public static string BuildHierarchyContext(GameObject root)
        {
            if (root == null) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("## Hierarchy");
            AppendTree(sb, root, 0, 6);
            return sb.ToString();
        }

        private static void AppendTree(StringBuilder sb, GameObject go, int depth, int maxDepth)
        {
            if (go == null || depth > maxDepth) return;
            var indent = new string(' ', depth * 2);
            sb.Append($"{indent}- {go.name} (id={go.GetInstanceID()})");

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
                sb.Append($" rect[anchor={rect.anchorMin}-{rect.anchorMax}, size={rect.sizeDelta}, pos={rect.anchoredPosition}]");

            var comps = go.GetComponents<Component>();
            sb.Append(" [");
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                sb.Append(comps[i].GetType().Name);
                if (i < comps.Length - 1) sb.Append(", ");
            }
            sb.AppendLine("]");

            for (int i = 0; i < go.transform.childCount; i++)
                AppendTree(sb, go.transform.GetChild(i).gameObject, depth + 1, maxDepth);
        }

        public static string GetPath(GameObject go)
        {
            if (go == null) return string.Empty;
            var path = go.name;
            var t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }
    }
}

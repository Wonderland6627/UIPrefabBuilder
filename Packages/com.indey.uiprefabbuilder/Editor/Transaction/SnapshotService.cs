using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Transaction
{
    public static class SnapshotService
    {
        public static List<string> Capture(GameObject root, int maxDepth = 5)
        {
            var lines = new List<string>();
            if (root != null) Walk(root.transform, 0, maxDepth, lines);
            return lines;
        }

        public static string Diff(List<string> before, List<string> after)
        {
            var sb = new StringBuilder();
            var bSet = new HashSet<string>(before ?? new List<string>());
            var aSet = new HashSet<string>(after ?? new List<string>());
            foreach (var l in aSet) if (!bSet.Contains(l)) sb.AppendLine("+ " + l);
            foreach (var l in bSet) if (!aSet.Contains(l)) sb.AppendLine("- " + l);
            return sb.Length == 0 ? "(no change)" : sb.ToString();
        }

        private static void Walk(Transform t, int depth, int max, List<string> lines)
        {
            if (t == null || depth > max) return;
            lines.Add(new string(' ', depth * 2) + t.name + " (id=" + t.gameObject.GetInstanceID() + ")");
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, max, lines);
        }
    }
}

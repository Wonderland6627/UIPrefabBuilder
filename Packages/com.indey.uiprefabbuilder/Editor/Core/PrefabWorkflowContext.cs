using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Core
{
    public enum PrefabWorkflowMode
    {
        Auto = 0,
        New = 1,
        Modify = 2
    }

    /// <summary>
    /// Prefab workflow chosen for the task that is currently running.
    /// The target is captured when the user sends the message: an explicit Assets path wins,
    /// otherwise the selected prefab instance in the Hierarchy is used.
    /// </summary>
    public static class PrefabWorkflowContext
    {
        private static readonly Regex PrefabPathPattern = new Regex(
            @"Assets\/[^\s""'<>|]+?\.prefab",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static PrefabWorkflowMode Mode { get; private set; } = PrefabWorkflowMode.Auto;
        public static string ExplicitAssetPath { get; private set; }
        public static string SelectedAssetPath { get; private set; }
        public static string SelectedHierarchyPath { get; private set; }
        public static int SelectedInstanceId { get; private set; }

        /// <summary>The only existing prefab asset Modify mode may write. Null when the user must be asked.</summary>
        public static string ApplyTargetPath { get; private set; }

        public static bool HasApplyTarget => !string.IsNullOrEmpty(ApplyTargetPath);

        public static void Begin(PrefabWorkflowMode mode, string userText, GameObject selected)
        {
            Mode = mode;
            ExplicitAssetPath = ExtractPrefabPath(userText);
            SelectedAssetPath = null;
            SelectedHierarchyPath = null;
            SelectedInstanceId = 0;
            CaptureSelection(selected);
            ApplyTargetPath = !string.IsNullOrEmpty(ExplicitAssetPath) ? ExplicitAssetPath : SelectedAssetPath;
        }

        public static bool PathsMatch(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            return path.Trim().Replace('\\', '/');
        }

        private static void CaptureSelection(GameObject selected)
        {
            if (selected == null) return;

            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(selected);
            if (root == null || !PrefabUtility.IsAnyPrefabInstanceRoot(root)) return;

            SelectedInstanceId = root.GetInstanceID();
            SelectedHierarchyPath = BuildHierarchyPath(root);
            SelectedAssetPath = Normalize(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root));
        }

        private static string ExtractPrefabPath(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var match = PrefabPathPattern.Match(text);
            return match.Success ? Normalize(match.Value) : null;
        }

        private static string BuildHierarchyPath(GameObject go)
        {
            if (go == null) return null;
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}

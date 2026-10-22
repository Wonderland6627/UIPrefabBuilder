using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Indey.UIPrefabBuilder.Core
{
    public enum SessionAssetChangeKind
    {
        Added,
        Modified
    }

    public sealed class SessionAssetChange
    {
        public string Path;
        public SessionAssetChangeKind Kind;
        public long Ticks;
    }

    /// <summary>
    /// Project assets written during the current chat session. Scene-only edits stay out of this list
    /// because the Project window has nothing to reveal for them.
    /// </summary>
    public static class SessionAssetChanges
    {
        private static readonly List<SessionAssetChange> ItemsBacking = new List<SessionAssetChange>();
        private static int _recordingDepth;

        public static event Action Changed;

        public static IReadOnlyList<SessionAssetChange> Items => ItemsBacking;

        public static bool IsRecording => _recordingDepth > 0;

        public static void BeginRecording()
        {
            _recordingDepth++;
        }

        public static void EndRecording()
        {
            if (_recordingDepth > 0) _recordingDepth--;
        }

        public static void Record(string assetPath, SessionAssetChangeKind kind)
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (!ShouldTrack(assetPath)) return;

            for (int i = 0; i < ItemsBacking.Count; i++)
            {
                var existing = ItemsBacking[i];
                if (!string.Equals(existing.Path, assetPath, StringComparison.OrdinalIgnoreCase)) continue;
                existing.Ticks = DateTime.UtcNow.Ticks;
                if (existing.Kind != SessionAssetChangeKind.Added)
                    existing.Kind = kind;
                Changed?.Invoke();
                return;
            }

            ItemsBacking.Add(new SessionAssetChange
            {
                Path = assetPath,
                Kind = kind,
                Ticks = DateTime.UtcNow.Ticks
            });
            Changed?.Invoke();
        }

        public static void Clear()
        {
            if (ItemsBacking.Count == 0) return;
            ItemsBacking.Clear();
            Changed?.Invoke();
        }

        public static void Replace(IEnumerable<SessionAssetChange> items)
        {
            ItemsBacking.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item == null || string.IsNullOrEmpty(item.Path)) continue;
                    ItemsBacking.Add(new SessionAssetChange
                    {
                        Path = item.Path.Replace('\\', '/'),
                        Kind = item.Kind,
                        Ticks = item.Ticks
                    });
                }
            }
            Changed?.Invoke();
        }

        public static List<SessionAssetChange> Snapshot()
        {
            var copy = new List<SessionAssetChange>(ItemsBacking.Count);
            foreach (var item in ItemsBacking)
            {
                copy.Add(new SessionAssetChange
                {
                    Path = item.Path,
                    Kind = item.Kind,
                    Ticks = item.Ticks
                });
            }
            return copy;
        }

        public static bool ShouldTrack(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return false;
            if (assetPath.StartsWith("Assets/Screenshots/", StringComparison.OrdinalIgnoreCase)) return false;
            var ext = Path.GetExtension(assetPath);
            return !string.IsNullOrEmpty(ext) && !ext.Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            var path = assetPath.Trim().Replace('\\', '/');
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                path = path.Substring(0, path.Length - ".meta".Length);
            return path;
        }
    }

    /// <summary>
    /// Catches asset writes made from execute_code while a tool is running.
    /// Explicit prefab and scene saves also record themselves, and the tracker keeps a single row.
    /// </summary>
    public class SessionAssetChangeProcessor : AssetModificationProcessor
    {
        public static void OnWillCreateAsset(string assetName)
        {
            if (!SessionAssetChanges.IsRecording) return;
            SessionAssetChanges.Record(assetName, SessionAssetChangeKind.Added);
        }

        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (SessionAssetChanges.IsRecording && paths != null)
            {
                foreach (var path in paths)
                    SessionAssetChanges.Record(path, SessionAssetChangeKind.Modified);
            }
            return paths;
        }
    }
}

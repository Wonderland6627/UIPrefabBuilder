using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Indey.UIPrefabBuilder.Indexing
{
    [Serializable]
    public class IndexingConfig
    {
        public List<string> indexDirectories = new List<string> { "Assets" };
        public List<string> ignorePatterns = new List<string> { "*_bg.png", "*_seq_*" };
        public int topKDefault = 5;
        public float minConfidenceThreshold = 0.5f;

        public bool IsIgnored(string assetPath)
        {
            if (ignorePatterns == null || ignorePatterns.Count == 0)
                return false;

            var fileName = Path.GetFileName(assetPath);
            foreach (var pattern in ignorePatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                if (GlobMatch(pattern, fileName) || GlobMatch(pattern, assetPath))
                    return true;
            }
            return false;
        }

        public bool IsInIndexDirectory(string assetPath)
        {
            if (indexDirectories == null || indexDirectories.Count == 0)
                return true;

            return indexDirectories.Any(d =>
                assetPath.StartsWith(d, StringComparison.OrdinalIgnoreCase));
        }

        private static bool GlobMatch(string pattern, string input)
        {
            var escaped = Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");
            return Regex.IsMatch(input, "^" + escaped + "$", RegexOptions.IgnoreCase);
        }
    }
}

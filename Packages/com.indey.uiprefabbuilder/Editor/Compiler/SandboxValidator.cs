using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Indey.UIPrefabBuilder.Compiler
{
    public class ValidationResult
    {
        public bool IsValid;
        public List<string> Errors = new List<string>();
    }

    public static class SandboxValidator
    {
        private static readonly HashSet<string> AllowedNamespaces = new HashSet<string>
        {
            "UnityEngine", "UnityEngine.UI", "UnityEditor", "UnityEditor.SceneManagement",
            "TMPro", "System", "System.Linq", "System.Collections.Generic",
            "Indey.UIPrefabBuilder.Core", "Indey.UIPrefabBuilder.Skills"
        };

        private static readonly string[] BlockedPatterns =
        {
            @"System\.IO\.File\.Delete", @"System\.IO\.Directory\.Delete",
            @"System\.Diagnostics\.Process", @"Application\.Quit",
            @"EditorApplication\.Exit", @"AssetDatabase\.DeleteAsset",
            @"FileUtil\.DeleteFileOrDirectory", @"System\.Net\.",
            @"System\.Reflection\.Emit"
        };

        public static ValidationResult Validate(string code)
        {
            var r = new ValidationResult { IsValid = true };
            if (string.IsNullOrWhiteSpace(code)) { r.IsValid = false; r.Errors.Add("Empty code."); return r; }

            foreach (var pat in BlockedPatterns)
            {
                if (Regex.IsMatch(code, pat))
                {
                    r.IsValid = false;
                    r.Errors.Add($"Blocked API: {pat}");
                }
            }

            foreach (Match m in Regex.Matches(code, @"using\s+([\w\.]+)\s*;"))
            {
                var ns = m.Groups[1].Value;
                if (!AllowedNamespaces.Any(a => ns.StartsWith(a)))
                {
                    r.IsValid = false;
                    r.Errors.Add($"Disallowed namespace: {ns}");
                }
            }
            return r;
        }
    }
}

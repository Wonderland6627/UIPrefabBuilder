using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Indey.UIPrefabBuilder.Compiler
{
    public static class CodeExtractor
    {
        private static readonly Regex[] CodeBlockPatterns = new[]
        {
            new Regex(@"````*(?:csharp|cs|c#)?\s*\r?\n(.*?)````*", RegexOptions.Singleline | RegexOptions.IgnoreCase),
            new Regex(@"```(?:csharp|cs|c#)?[ \t]*\r?\n?(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase),
        };

        public static List<string> ExtractAll(string text)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return results;

            foreach (var rx in CodeBlockPatterns)
            {
                foreach (Match m in rx.Matches(text))
                {
                    var code = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(code) && code.Length > 20)
                        results.Add(code);
                }
                if (results.Count > 0) break;
            }
            return results;
        }

        public static string ExtractFirst(string text)
        {
            var all = ExtractAll(text);
            if (all.Count == 0) return string.Empty;

            // Prefer the block that contains IAgentAction implementation
            foreach (var code in all)
                if (code.Contains("IAgentAction") || code.Contains("Execute(ActionContext"))
                    return code;

            // Otherwise prefer the longest block (likely the full implementation)
            string best = all[0];
            for (int i = 1; i < all.Count; i++)
                if (all[i].Length > best.Length) best = all[i];
            return best;
        }
    }
}

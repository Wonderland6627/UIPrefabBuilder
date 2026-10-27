using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.UI
{
    /// <summary>
    /// Turns a finished tool call into chat text. Detail items stay separate until draw time,
    /// then flow on one line joined by "、" and wrap only when the bubble width is used up.
    /// execute_code is presented as its own code bubble instead of folding the source into that summary.
    /// </summary>
    public static class ToolCallPresenter
    {
        public const int MaxDetailLines = 3;
        public const char SegmentMark = '\u001f';
        const string Joiner = "\u3001";
        const string Ellipsis = "...";

        static readonly string[] ListKeys =
        {
            "assets", "names", "objects", "matches", "results", "items",
            "missingElements", "mismatches", "children", "paths", "files",
            "sprites", "elements"
        };

        static readonly string[] SnapshotKeys = { "info", "hierarchy", "components" };

        static readonly string[] FallbackTextKeys = { "structure", "overview", "summary", "description" };

        static readonly string[] ArgKeys =
        {
            "path", "spritePath", "backgroundSpritePath", "iconSpritePath",
            "fillSpritePath", "handleSpritePath", "checkmarkSpritePath",
            "unselectedSpritePath", "selectedSpritePath", "prefabPath", "assetPath",
            "directory", "filter", "pattern", "patterns", "name", "parent", "target",
            "targets", "text", "content", "label", "labels", "filename", "component",
            "type", "typeName", "uiType", "query", "queries", "reason",
            "completed", "remaining", "issues", "items"
        };

        static readonly HashSet<string> NoiseArgs = new HashSet<string>(StringComparer.Ordinal)
        {
            "code", "imageBase64", "success", "r", "g", "b", "a",
            "width", "height", "fontSize", "limit", "offset", "topK", "minConfidence",
            "x", "y", "min", "max", "value", "spacing", "iconWidth", "iconHeight",
            "handleWidth", "tabWidth", "tabHeight", "selectedIndex", "vertical", "force",
            "maxDepth"
        };

        public sealed class Presentation
        {
            public BubbleType Type;
            public string Title;
            public string Content;
            public string Note;
        }

        public static Presentation Present(string name, string argsJson, string resultJson)
        {
            if (TryCreateCodeBubble(name, argsJson, resultJson, out var title, out var code, out var note))
            {
                return new Presentation
                {
                    Type = BubbleType.Code,
                    Title = title,
                    Content = code,
                    Note = note
                };
            }

            return new Presentation
            {
                Type = BubbleType.ToolCall,
                Content = Format(name, argsJson, resultJson)
            };
        }

        public static string Format(string name, string argsJson, string resultJson)
        {
            var sb = new StringBuilder();
            sb.Append(StatusText(name, resultJson));
            var details = BuildDetails(argsJson, resultJson);
            if (!string.IsNullOrEmpty(details))
            {
                sb.Append('\n');
                sb.Append(details);
            }
            return sb.ToString();
        }

        public static bool TryCreateCodeBubble(string name, string argsJson, string resultJson, out string title, out string code, out string note)
        {
            title = null;
            code = null;
            note = null;
            if (!string.Equals(name, "execute_code", StringComparison.Ordinal))
                return false;

            var args = ParseObject(argsJson);
            code = args?["code"]?.Type == JTokenType.String ? args["code"].ToString() : null;
            if (string.IsNullOrWhiteSpace(code))
                return false;

            code = code.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();
            title = StatusText(name, resultJson);
            note = BuildDetails(argsJson, resultJson);
            return true;
        }

        static string StatusText(string name, string resultJson)
        {
            var label = string.IsNullOrEmpty(name) ? "tool" : name;
            var result = ParseObject(resultJson);
            if (result == null)
                return "\u2699 " + label;

            var success = result["success"];
            if (success != null && success.Type == JTokenType.Boolean)
                return "\u2699 " + label + ((bool)success ? " \u2714" : " \u2716");
            if (result["error"] != null && result["error"].Type != JTokenType.Null)
                return "\u2699 " + label + " \u2716";
            return "\u2699 " + label;
        }

        static string BuildDetails(string argsJson, string resultJson)
        {
            var lines = new List<string>(MaxDetailLines);
            var result = ParseObject(resultJson);
            var args = ParseObject(argsJson);
            if (result == null)
            {
                if (!string.IsNullOrWhiteSpace(resultJson))
                    lines.Add(OneLine(resultJson));
                else
                    AddArgumentHighlights(args, lines);
            }
            else if (IsFailed(result))
            {
                AddProse(TokenString(result["error"]), lines);
                AddArgumentHighlights(args, lines);
            }
            else
            {
                AddKeys(result, lines, ListKeys);
                AddKeys(result, lines, SnapshotKeys);
                var message = TokenString(result["message"]);
                if (!string.IsNullOrEmpty(message) && !AlreadyShown(lines, OneLine(message)))
                    AddProse(message, lines);
                if (lines.Count == 0)
                    AddIdentity(result, lines);
                AddArgumentHighlights(args, lines);
                if (lines.Count < MaxDetailLines)
                    AddKeys(result, lines, FallbackTextKeys);
                if (lines.Count < MaxDetailLines)
                    AddProse(TokenString(result["warning"]), lines);
            }

            if (lines.Count == 0)
                return "";
            return string.Join(SegmentMark.ToString(), lines.ToArray());
        }

        /// <summary>
        /// Joins stored detail segments with "、", wrapping to <see cref="MaxDetailLines"/> once
        /// <paramref name="width"/> is filled. Leftover items end the last line with "... +N".
        /// </summary>
        public static string Flow(string details, float width, Func<string, float> measure)
        {
            if (string.IsNullOrEmpty(details))
                return "";
            var parts = SplitSegments(details);
            if (parts.Count == 0)
                return "";
            if (measure == null || width < 8f)
                return string.Join(Joiner, parts.ToArray());

            var lines = new List<List<string>>();
            var line = new List<string>();
            int index = 0;
            bool cutInside = false;
            while (index < parts.Count && lines.Count < MaxDetailLines)
            {
                var item = parts[index];
                var trial = new List<string>(line);
                trial.Add(item);
                if (measure(Join(trial)) <= width)
                {
                    line.Add(item);
                    index++;
                    continue;
                }

                if (line.Count > 0)
                {
                    if (lines.Count >= MaxDetailLines - 1)
                        break;
                    lines.Add(line);
                    line = new List<string>();
                    continue;
                }

                if (!PlaceWide(item, width, measure, lines, ref line, out cutInside))
                    break;
                index++;
                if (cutInside)
                    break;
            }

            if (line.Count > 0 && lines.Count < MaxDetailLines)
                lines.Add(line);

            int hidden = parts.Count - index;
            if (hidden <= 0 && !cutInside)
                return JoinLines(lines);

            var suffix = hidden > 0 ? Ellipsis + " +" + hidden : Ellipsis;
            if (lines.Count == 0)
                return ClipToWidth("", width, measure, suffix);
            var last = lines[lines.Count - 1];
            while (last.Count > 1 && measure(Join(last) + suffix) > width)
            {
                last.RemoveAt(last.Count - 1);
                hidden++;
                suffix = Ellipsis + " +" + hidden;
            }
            var fitted = ClipToWidth(Join(last), width, measure, suffix);
            last.Clear();
            last.Add(fitted);
            return JoinLines(lines);
        }

        public static string ToPlain(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            return text.Replace(SegmentMark.ToString(), Joiner);
        }

        static bool IsFailed(JObject result)
        {
            var success = result["success"];
            if (success != null && success.Type == JTokenType.Boolean)
                return !(bool)success;
            return result["error"] != null && result["error"].Type != JTokenType.Null;
        }

        static void AddKeys(JObject result, List<string> lines, string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                var token = result[keys[i]];
                if (token == null || token.Type == JTokenType.Null)
                    continue;
                if (token.Type == JTokenType.Boolean || token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                    continue;
                int before = lines.Count;
                AddToken(token, lines);
                if (lines.Count > before)
                    return;
            }
        }

        static void AddToken(JToken token, List<string> lines)
        {
            if (token == null)
                return;
            if (token is JArray arr)
            {
                AddArray(arr, lines);
                return;
            }
            if (token is JObject obj)
            {
                AddMap(obj, lines);
                return;
            }
            if (token.Type == JTokenType.String)
                AddProse(token.ToString(), lines);
        }

        static void AddArray(JArray arr, List<string> lines)
        {
            if (arr == null || arr.Count == 0)
                return;
            for (int i = 0; i < arr.Count; i++)
            {
                var label = LabelOf(arr[i]);
                if (!string.IsNullOrEmpty(label))
                    lines.Add(label);
            }
        }

        static void AddMap(JObject map, List<string> lines)
        {
            if (map == null || map.Count == 0)
                return;
            foreach (var prop in map.Properties())
            {
                var summary = SummarizeMapValue(prop.Value);
                if (string.IsNullOrEmpty(summary) || summary == prop.Name)
                    lines.Add(prop.Name);
                else
                    lines.Add(prop.Name + "  " + summary);
            }
        }

        static string SummarizeMapValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return null;
            if (value is JArray arr)
            {
                if (arr.Count == 0)
                    return "(0)";
                var first = LabelOf(arr[0]);
                if (string.IsNullOrEmpty(first))
                    return arr.Count + " items";
                if (arr.Count == 1)
                    return first;
                return first + "  +" + (arr.Count - 1);
            }
            if (value.Type == JTokenType.String)
                return FirstProseLine(value.ToString());
            return LabelOf(value);
        }

        static void AddProse(string text, List<string> lines)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            text = text.Trim();
            if (text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0 && LooksSectioned(text))
            {
                AddSectioned(text, lines);
                return;
            }

            var raw = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < raw.Length; i++)
            {
                var part = raw[i].Trim();
                if (part.Length > 0)
                    lines.Add(OneLine(part));
            }
        }

        static bool LooksSectioned(string text)
        {
            return text.IndexOf(" | ", StringComparison.Ordinal) >= 0
                && (text.IndexOf('@') >= 0 || text.IndexOf('/') >= 0 || text.IndexOf('\\') >= 0);
        }

        static void AddSectioned(string text, List<string> lines)
        {
            var segs = text.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>();
            for (int i = 0; i < segs.Length; i++)
            {
                var part = segs[i].Trim();
                if (part.Length > 0)
                    parts.Add(part);
            }
            if (parts.Count == 0)
                return;
            for (int i = 0; i < parts.Count; i++)
                lines.Add(parts[i]);
        }

        static void AddIdentity(JObject result, List<string> lines)
        {
            var scene = FirstString(result, "sceneName");
            var path = FirstString(result, "path", "scenePath", "assetPath", "prefabPath", "screenshotPath");
            var name = FirstString(result, "name");
            string line = null;
            if (!string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(path))
                line = scene + "  " + path;
            else if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                line = path.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0 ? path : name + "  " + path;
            else if (!string.IsNullOrEmpty(path))
                line = path;
            else if (!string.IsNullOrEmpty(name))
                line = name;
            else if (!string.IsNullOrEmpty(scene))
                line = scene;
            else if (result["count"] != null && result["count"].Type != JTokenType.Null)
                line = "count: " + result["count"];

            if (string.IsNullOrEmpty(line) || AlreadyShown(lines, line))
                return;
            lines.Add(line);
        }

        static void AddArgumentHighlights(JObject args, List<string> lines)
        {
            if (args == null)
                return;

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < ArgKeys.Length; i++)
            {
                var key = ArgKeys[i];
                if (!used.Add(key))
                    continue;
                TryAddArg(key, args[key], lines);
            }

            foreach (var prop in args.Properties())
            {
                if (!used.Add(prop.Name) || IsNoiseArg(prop.Name))
                    continue;
                TryAddArg(prop.Name, prop.Value, lines);
            }
        }

        static void TryAddArg(string key, JToken token, List<string> lines)
        {
            if (token == null || token.Type == JTokenType.Null)
                return;
            if (key == "code" || key == "imageBase64")
                return;

            if (token.Type == JTokenType.String && LooksLikeJsonArray(token.ToString()))
            {
                try
                {
                    token = JArray.Parse(token.ToString());
                }
                catch
                {
                    return;
                }
            }

            if (token is JArray arr)
            {
                if (key == "items")
                    AddArray(arr, lines);
                else
                    AddJoinedArray(key, arr, lines);
                return;
            }

            if (token.Type != JTokenType.String)
                return;

            var value = OneLine(token.ToString());
            if (value.Length == 0 || value.Length > 180)
                return;
            if (AlreadyShown(lines, value))
                return;

            lines.Add(LooksLikePath(value) ? value : key + ": " + value);
        }

        static void AddJoinedArray(string key, JArray arr, List<string> lines)
        {
            var parts = new List<string>();
            for (int i = 0; i < arr.Count; i++)
            {
                var label = LabelOf(arr[i]);
                if (!string.IsNullOrEmpty(label))
                    parts.Add(label);
            }
            if (parts.Count == 0)
                return;
            if (!string.IsNullOrEmpty(key))
                lines.Add(key);
            for (int i = 0; i < parts.Count; i++)
                lines.Add(parts[i]);
        }

        static bool LooksLikeJsonArray(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                    continue;
                return text[i] == '[';
            }
            return false;
        }

        static string LabelOf(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            if (token.Type == JTokenType.String || token.Type == JTokenType.Integer || token.Type == JTokenType.Float || token.Type == JTokenType.Boolean)
                return OneLine(token.ToString());
            if (token is JArray arr)
            {
                if (arr.Count == 0)
                    return null;
                var first = LabelOf(arr[0]);
                return arr.Count == 1 ? first : first + "  +" + (arr.Count - 1);
            }

            var obj = token as JObject;
            if (obj == null)
                return OneLine(token.ToString());

            var name = FirstString(obj, "name", "target", "label", "query", "id");
            var path = FirstString(obj, "path", "assetPath", "spritePath", "prefabPath");
            var text = FirstString(obj, "text", "content", "message", "error");
            string core = null;
            if (!string.IsNullOrEmpty(path))
            {
                core = string.IsNullOrEmpty(name) || path.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    ? path
                    : name + "  " + path;
            }
            else if (!string.IsNullOrEmpty(name))
                core = name;
            else if (!string.IsNullOrEmpty(text))
                core = text;
            else
            {
                var rich = FirstString(obj, "hierarchy", "components", "info", "overview");
                core = string.IsNullOrEmpty(rich) ? FirstShortString(obj) : FirstProseLine(rich);
            }

            if (string.IsNullOrEmpty(core))
                return null;

            var success = obj["success"];
            if (success != null && success.Type == JTokenType.Boolean && !(bool)success)
                core += " \u2716";

            var score = obj["score"] ?? obj["confidence"];
            if (score != null && (score.Type == JTokenType.Float || score.Type == JTokenType.Integer))
                core += "  " + score;

            return OneLine(core);
        }

        static string FirstShortString(JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (prop.Value == null || prop.Value.Type != JTokenType.String)
                    continue;
                var text = FirstProseLine(prop.Value.ToString());
                if (!string.IsNullOrEmpty(text) && text.Length <= 180)
                    return text;
            }
            return null;
        }

        static string FirstString(JObject obj, params string[] keys)
        {
            if (obj == null)
                return null;
            for (int i = 0; i < keys.Length; i++)
            {
                var token = obj[keys[i]];
                if (token == null || token.Type != JTokenType.String)
                    continue;
                var text = OneLine(token.ToString());
                if (text.Length > 0)
                    return text;
            }
            return null;
        }

        static string FirstProseLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var raw = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < raw.Length; i++)
            {
                var part = raw[i].Trim();
                if (part.Length > 0)
                    return part;
            }
            return null;
        }

        static string TokenString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            return token.Type == JTokenType.String ? token.ToString() : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        static bool AlreadyShown(List<string> lines, string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == value)
                    return true;
                if (value.Length >= 4 && lines[i].IndexOf(value, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        static bool IsNoiseArg(string key)
        {
            if (string.IsNullOrEmpty(key) || NoiseArgs.Contains(key))
                return true;
            if (key.Length <= 1 || !char.IsLower(key[key.Length - 2]))
                return false;
            char tail = key[key.Length - 1];
            return tail == 'R' || tail == 'G' || tail == 'B' || tail == 'A';
        }

        static bool LooksLikePath(string value)
        {
            return value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0;
        }

        static bool PlaceWide(string item, float width, Func<string, float> measure, List<List<string>> lines, ref List<string> line, out bool cutInside)
        {
            cutInside = false;
            int room = MaxDetailLines - lines.Count;
            if (room <= 0 || string.IsNullOrEmpty(item))
                return false;

            int pos = 0;
            var wrapped = new List<string>();
            while (pos < item.Length && wrapped.Count < room)
            {
                int count = FitChars(item, pos, width, measure);
                if (count <= 0)
                    count = 1;
                if (pos + count > item.Length)
                    count = item.Length - pos;
                wrapped.Add(item.Substring(pos, count));
                pos += count;
            }
            if (wrapped.Count == 0)
                return false;

            if (pos < item.Length)
            {
                for (int i = 0; i < wrapped.Count; i++)
                    lines.Add(new List<string> { wrapped[i] });
                line = new List<string>();
                cutInside = true;
                return true;
            }

            for (int i = 0; i < wrapped.Count - 1; i++)
                lines.Add(new List<string> { wrapped[i] });
            line = new List<string> { wrapped[wrapped.Count - 1] };
            return true;
        }

        static int FitChars(string text, int start, float width, Func<string, float> measure)
        {
            int lo = 0;
            int hi = text.Length - start;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (measure(text.Substring(start, mid)) <= width)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return lo;
        }

        static string ClipToWidth(string text, float width, Func<string, float> measure, string suffix)
        {
            suffix = suffix ?? "";
            text = text ?? "";
            if (measure(text + suffix) <= width)
                return text + suffix;
            if (measure(suffix) > width)
                return suffix;
            int lo = 0;
            int hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (measure(text.Substring(0, mid) + suffix) <= width)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return (lo == 0 ? "" : text.Substring(0, lo)) + suffix;
        }

        static List<string> SplitSegments(string details)
        {
            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i <= details.Length; i++)
            {
                if (i < details.Length && details[i] != SegmentMark)
                    continue;
                if (i > start)
                {
                    var part = details.Substring(start, i - start).Trim();
                    if (part.Length > 0)
                        parts.Add(part);
                }
                start = i + 1;
            }
            return parts;
        }

        static string Join(List<string> parts)
        {
            if (parts == null || parts.Count == 0)
                return "";
            if (parts.Count == 1)
                return parts[0];
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                    sb.Append(Joiner);
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        static string JoinLines(List<List<string>> lines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    sb.Append('\n');
                sb.Append(Join(lines[i]));
            }
            return sb.ToString();
        }

        static string OneLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\t' || c == SegmentMark)
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        static JObject ParseObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                var token = JToken.Parse(json);
                return token as JObject;
            }
            catch
            {
                return null;
            }
        }
    }
}

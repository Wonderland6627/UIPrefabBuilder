using System;
using System.Collections.Generic;
using System.Text;

namespace Indey.UIPrefabBuilder.UI
{
    public enum MarkdownBlockKind
    {
        Text,
        Code
    }

    /// <summary>
    /// One display block. Text is IMGUI rich text; Code is the original source.
    /// </summary>
    public readonly struct MarkdownBlock
    {
        public readonly MarkdownBlockKind Kind;
        public readonly string Text;
        public readonly string Language;

        public MarkdownBlock(MarkdownBlockKind kind, string text, string language)
        {
            Kind = kind;
            Text = text ?? "";
            Language = language ?? "";
        }
    }

    /// <summary>
    /// Display-only Markdown subset for IMGUI chat bubbles.
    /// Stored messages stay Markdown; this runs at draw time.
    /// </summary>
    public static class MarkdownView
    {
        public readonly struct Theme
        {
            public readonly string InlineCodeColor;
            public readonly string LinkColor;
            public readonly string RuleColor;

            public Theme(string inlineCodeColor, string linkColor, string ruleColor)
            {
                InlineCodeColor = inlineCodeColor;
                LinkColor = linkColor;
                RuleColor = ruleColor;
            }

            public static Theme ForEditorSkin(bool proSkin)
            {
                if (proSkin)
                    return new Theme("#E6C07BFF", "#8AB4F8FF", "#888888FF");
                return new Theme("#8A5A00FF", "#1255CCFF", "#8A8A8AFF");
            }
        }

        public static List<MarkdownBlock> Parse(string markdown)
        {
            return Parse(markdown, Theme.ForEditorSkin(true));
        }

        public static List<MarkdownBlock> Parse(string markdown, Theme theme)
        {
            var blocks = new List<MarkdownBlock>();
            if (string.IsNullOrEmpty(markdown))
                return blocks;

            var lines = SplitLines(markdown);
            var text = new StringBuilder();
            var code = new StringBuilder();
            var inCode = false;
            var openTicks = 0;
            var language = "";

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!inCode && TryReadOpeningFence(line, out var ticks, out var info))
                {
                    FlushText(blocks, text, theme);
                    inCode = true;
                    openTicks = ticks;
                    language = NormalizeLanguage(info);
                    continue;
                }

                if (inCode)
                {
                    if (IsClosingFence(line, openTicks))
                    {
                        blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, code.ToString(), language));
                        code.Length = 0;
                        inCode = false;
                        openTicks = 0;
                        language = "";
                        continue;
                    }

                    if (code.Length > 0)
                        code.Append('\n');
                    code.Append(line);
                    continue;
                }

                if (text.Length > 0)
                    text.Append('\n');
                text.Append(line);
            }

            if (inCode)
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Code, code.ToString(), language));
            else
                FlushText(blocks, text, theme);

            return blocks;
        }

        private static void FlushText(List<MarkdownBlock> blocks, StringBuilder raw, Theme theme)
        {
            if (raw.Length == 0)
                return;

            var rendered = RenderText(raw.ToString(), theme).Trim('\r', '\n');
            raw.Length = 0;
            if (rendered.Length == 0)
                return;

            blocks.Add(new MarkdownBlock(MarkdownBlockKind.Text, rendered, ""));
        }

        private static string RenderText(string raw, Theme theme)
        {
            var lines = SplitLines(raw);
            var output = new StringBuilder();
            var paragraph = new StringBuilder();
            var pendingGap = false;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (IsBlank(line))
                {
                    FlushParagraph(output, paragraph, theme, ref pendingGap);
                    if (output.Length > 0)
                        pendingGap = true;
                    continue;
                }

                if (TryHeading(line, out var level, out var heading))
                {
                    FlushParagraph(output, paragraph, theme, ref pendingGap);
                    BreakBefore(output, pendingGap);
                    pendingGap = false;
                    var size = HeadingSize(level);
                    output.Append("<size=").Append(size).Append("><b>");
                    output.Append(RenderInline(heading, 0, heading.Length, theme));
                    output.Append("</b></size>");
                    continue;
                }

                if (IsHorizontalRule(line))
                {
                    FlushParagraph(output, paragraph, theme, ref pendingGap);
                    BreakBefore(output, pendingGap);
                    pendingGap = false;
                    output.Append("<color=").Append(theme.RuleColor).Append(">────────</color>");
                    continue;
                }

                if (TryListItem(line, out var bullet, out var item))
                {
                    FlushParagraph(output, paragraph, theme, ref pendingGap);
                    BreakBefore(output, pendingGap);
                    pendingGap = false;
                    output.Append(EscapeRichText(bullet));
                    output.Append(RenderInline(item, 0, item.Length, theme));
                    continue;
                }

                if (paragraph.Length > 0)
                    paragraph.Append('\n');
                paragraph.Append(line);
            }

            FlushParagraph(output, paragraph, theme, ref pendingGap);
            return output.ToString();
        }

        private static void FlushParagraph(StringBuilder output, StringBuilder paragraph, Theme theme, ref bool pendingGap)
        {
            if (paragraph.Length == 0)
                return;

            BreakBefore(output, pendingGap);
            pendingGap = false;
            output.Append(RenderInline(paragraph.ToString(), 0, paragraph.Length, theme));
            paragraph.Length = 0;
        }

        private static void BreakBefore(StringBuilder output, bool paragraphGap)
        {
            if (output.Length == 0)
                return;
            if (output[output.Length - 1] != '\n')
                output.Append('\n');
            if (paragraphGap)
                output.Append('\n');
        }

        private static int HeadingSize(int level)
        {
            if (level <= 1) return 16;
            if (level == 2) return 14;
            return 13;
        }

        private static string RenderInline(string src, int start, int end, Theme theme)
        {
            var sb = new StringBuilder();
            var i = start;
            while (i < end)
            {
                if (src[i] == '`')
                {
                    if (TryConsumeCodeSpan(src, i, end, theme, sb, out var next))
                    {
                        i = next;
                        continue;
                    }
                }

                if (IsDoubleMarker(src, i, end))
                {
                    if (TryConsumeBold(src, i, end, theme, sb, out var next))
                    {
                        i = next;
                        continue;
                    }
                }

                if (src[i] == '*' || src[i] == '_')
                {
                    if (TryConsumeItalic(src, i, end, theme, sb, out var next))
                    {
                        i = next;
                        continue;
                    }
                }

                if (src[i] == '[' || (src[i] == '!' && i + 1 < end && src[i + 1] == '['))
                {
                    if (TryConsumeLink(src, i, end, theme, sb, out var next))
                    {
                        i = next;
                        continue;
                    }
                }

                AppendLiteral(sb, src[i]);
                i++;
            }

            return sb.ToString();
        }

        private static bool TryConsumeCodeSpan(string src, int i, int end, Theme theme, StringBuilder sb, out int next)
        {
            next = i;
            var close = IndexOf(src, '`', i + 1, end);
            int contentEnd;
            if (close >= 0)
            {
                contentEnd = close;
                next = close + 1;
            }
            else
            {
                var lineEnd = IndexOf(src, '\n', i + 1, end);
                contentEnd = lineEnd < 0 ? end : lineEnd;
                next = contentEnd;
                if (contentEnd == i + 1)
                    return false;
            }

            var inner = src.Substring(i + 1, contentEnd - (i + 1));
            if (inner.Length == 0)
                return close >= 0;

            sb.Append("<color=").Append(theme.InlineCodeColor).Append('>');
            sb.Append(EscapeRichText(inner));
            sb.Append("</color>");
            return true;
        }

        private static bool TryConsumeBold(string src, int i, int end, Theme theme, StringBuilder sb, out int next)
        {
            next = i;
            var marker = src[i];
            if (marker == '_' && !CanOpenUnderscore(src, i))
                return false;
            if (i + 2 >= end || char.IsWhiteSpace(src[i + 2]))
                return false;

            var close = FindClosingDouble(src, i + 2, end, marker);
            if (close >= 0)
            {
                AppendWrapped(sb, "<b>", "</b>", RenderInline(src, i + 2, close, theme));
                next = close + 2;
                return true;
            }

            if (marker != '*')
                return false;

            var rest = RenderInline(src, i + 2, end, theme);
            if (rest.Length == 0)
                return false;

            AppendWrapped(sb, "<b>", "</b>", rest);
            next = end;
            return true;
        }

        private static bool TryConsumeItalic(string src, int i, int end, Theme theme, StringBuilder sb, out int next)
        {
            next = i;
            var marker = src[i];
            if (IsDoubleMarker(src, i, end))
                return false;
            if (marker == '_' && !CanOpenUnderscore(src, i))
                return false;
            if (marker == '*' && !CanOpenStar(src, i, end))
                return false;

            var close = FindClosingSingle(src, i + 1, end, marker);
            if (close < 0)
                return false;

            AppendWrapped(sb, "<i>", "</i>", RenderInline(src, i + 1, close, theme));
            next = close + 1;
            return true;
        }

        private static bool TryConsumeLink(string src, int i, int end, Theme theme, StringBuilder sb, out int next)
        {
            next = i;
            var searchEnd = Math.Min(end, i + 300);
            var labelOpen = src[i] == '!' ? i + 1 : i;
            if (labelOpen >= searchEnd || src[labelOpen] != '[')
                return false;

            var labelClose = IndexOf(src, ']', labelOpen + 1, searchEnd);
            if (labelClose < 0 || labelClose + 1 >= searchEnd || src[labelClose + 1] != '(')
                return false;

            var urlClose = IndexOf(src, ')', labelClose + 2, searchEnd);
            if (urlClose < 0)
                return false;
            if (labelClose == labelOpen + 1)
                return false;

            var rawLabel = src.Substring(labelOpen + 1, labelClose - labelOpen - 1);
            var url = src.Substring(labelClose + 2, urlClose - (labelClose + 2)).Trim();
            AppendWrapped(sb, "<color=" + theme.LinkColor + ">", "</color>", RenderInline(src, labelOpen + 1, labelClose, theme));
            if (url.Length > 0 && !string.Equals(url, rawLabel, StringComparison.Ordinal))
            {
                sb.Append(" (");
                sb.Append(EscapeRichText(url));
                sb.Append(')');
            }
            next = urlClose + 1;
            return true;
        }

        private static void AppendWrapped(StringBuilder sb, string open, string close, string inner)
        {
            if (string.IsNullOrEmpty(inner))
                return;
            sb.Append(open).Append(inner).Append(close);
        }

        private static bool IsDoubleMarker(string src, int i, int end)
        {
            if (i + 1 >= end)
                return false;
            var c = src[i];
            return (c == '*' || c == '_') && src[i + 1] == c;
        }

        private static bool CanOpenStar(string src, int i, int end)
        {
            if (i + 1 >= end || char.IsWhiteSpace(src[i + 1]))
                return false;
            if (i > 0 && char.IsDigit(src[i - 1]) && char.IsDigit(src[i + 1]))
                return false;
            return true;
        }

        private static bool CanOpenUnderscore(string src, int i)
        {
            if (i == 0)
                return true;
            return !IsWordChar(src[i - 1]);
        }

        private static bool IsWordChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        private static int FindClosingDouble(string src, int from, int end, char marker)
        {
            for (var i = from; i < end - 1; i++)
            {
                if (src[i] == '`')
                {
                    var codeClose = IndexOf(src, '`', i + 1, end);
                    if (codeClose < 0)
                        return -1;
                    i = codeClose;
                    continue;
                }

                if (src[i] != marker || src[i + 1] != marker)
                    continue;
                if (i > from && char.IsWhiteSpace(src[i - 1]))
                    continue;
                if (i > 0 && src[i - 1] == marker)
                    continue;
                return i;
            }

            return -1;
        }

        private static int FindClosingSingle(string src, int from, int end, char marker)
        {
            for (var i = from; i < end; i++)
            {
                if (src[i] == '`')
                {
                    var codeClose = IndexOf(src, '`', i + 1, end);
                    if (codeClose < 0)
                        return -1;
                    i = codeClose;
                    continue;
                }

                if (src[i] != marker)
                    continue;
                var prevSame = i > 0 && src[i - 1] == marker;
                var nextSame = i + 1 < end && src[i + 1] == marker;
                if (prevSame || nextSame)
                    continue;
                if (char.IsWhiteSpace(src[i - 1]))
                    continue;
                if (marker == '_' && i + 1 < end && IsWordChar(src[i + 1]))
                    continue;
                return i;
            }

            return -1;
        }

        private static bool TryReadOpeningFence(string line, out int ticks, out string info)
        {
            ticks = 0;
            info = "";
            if (!TryCountFenceTicks(line, out var indent, out var tickStart, out ticks))
                return false;
            if (ticks < 3)
                return false;

            info = line.Substring(tickStart + ticks).Trim();
            if (info.IndexOf('`') >= 0)
                return false;
            return indent <= 3;
        }

        private static bool IsClosingFence(string line, int openTicks)
        {
            if (!TryCountFenceTicks(line, out var indent, out var tickStart, out var ticks))
                return false;
            if (indent > 3 || ticks < openTicks)
                return false;
            return string.IsNullOrWhiteSpace(line.Substring(tickStart + ticks));
        }

        private static bool TryCountFenceTicks(string line, out int indent, out int tickStart, out int ticks)
        {
            indent = 0;
            tickStart = 0;
            ticks = 0;
            while (indent < line.Length && line[indent] == ' ')
                indent++;
            if (indent >= line.Length || line[indent] != '`')
                return false;

            tickStart = indent;
            var i = indent;
            while (i < line.Length && line[i] == '`')
                i++;
            ticks = i - indent;
            return ticks > 0;
        }

        private static string NormalizeLanguage(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                return "";
            var token = info.Trim();
            var space = token.IndexOf(' ');
            if (space > 0)
                token = token.Substring(0, space);
            if (token.Length > 24)
                token = token.Substring(0, 24);
            return token;
        }

        private static bool TryHeading(string line, out int level, out string text)
        {
            level = 0;
            text = "";
            var i = 0;
            while (i < line.Length && line[i] == ' ')
                i++;
            if (i > 3)
                return false;

            var hashStart = i;
            while (i < line.Length && line[i] == '#')
                i++;
            level = i - hashStart;
            if (level < 1 || level > 6)
                return false;
            if (i >= line.Length || line[i] != ' ')
                return false;

            text = TrimHeadingSuffix(line.Substring(i + 1).Trim());
            return true;
        }

        private static string TrimHeadingSuffix(string text)
        {
            var hash = text.Length;
            while (hash > 0 && text[hash - 1] == '#')
                hash--;
            if (hash == text.Length || hash == 0 || text[hash - 1] != ' ')
                return text;
            while (hash > 0 && text[hash - 1] == ' ')
                hash--;
            return text.Substring(0, hash);
        }

        private static bool IsHorizontalRule(string line)
        {
            var t = line.Trim();
            if (t.Length < 3)
                return false;
            var marker = t[0];
            if (marker != '-' && marker != '*' && marker != '_')
                return false;

            var count = 0;
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i] == marker)
                {
                    count++;
                    continue;
                }
                if (t[i] != ' ' && t[i] != '\t')
                    return false;
            }
            return count >= 3;
        }

        private static bool TryListItem(string line, out string bullet, out string item)
        {
            bullet = "";
            item = "";
            var i = 0;
            while (i < line.Length && line[i] == ' ')
                i++;
            var indent = i;
            if (i >= line.Length)
                return false;

            if (char.IsDigit(line[i]))
            {
                var j = i;
                while (j < line.Length && char.IsDigit(line[j]))
                    j++;
                if (j >= line.Length || (line[j] != '.' && line[j] != ')'))
                    return false;
                if (j + 1 >= line.Length || line[j + 1] != ' ')
                    return false;
                if (j - i > 4)
                    return false;

                bullet = new string(' ', indent) + line.Substring(i, j - i + 1) + " ";
                item = line.Substring(j + 2);
                return true;
            }

            var marker = line[i];
            if (marker != '-' && marker != '*' && marker != '+')
                return false;
            if (i + 1 >= line.Length || line[i + 1] != ' ')
                return false;

            bullet = new string(' ', indent) + "\u2022 ";
            item = line.Substring(i + 2);
            return true;
        }

        private static bool IsBlank(string line)
        {
            for (var i = 0; i < line.Length; i++)
            {
                if (!char.IsWhiteSpace(line[i]))
                    return false;
            }
            return true;
        }

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n')
                    continue;
                lines.Add(TakeLine(text, start, i));
                start = i + 1;
            }

            if (start <= text.Length)
                lines.Add(TakeLine(text, start, text.Length));
            return lines;
        }

        private static string TakeLine(string text, int start, int endExclusive)
        {
            if (endExclusive > start && text[endExclusive - 1] == '\r')
                endExclusive--;
            if (endExclusive <= start)
                return "";
            return text.Substring(start, endExclusive - start);
        }

        private static int IndexOf(string src, char value, int from, int end)
        {
            for (var i = from; i < end; i++)
            {
                if (src[i] == value)
                    return i;
            }
            return -1;
        }

        // IMGUI treats '<' as a rich-text tag. Model text must not be able to open one.
        private static void AppendLiteral(StringBuilder sb, char c)
        {
            if (c == '<')
            {
                sb.Append('\uFF1C');
                return;
            }
            sb.Append(c);
        }

        private static string EscapeRichText(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('<') < 0)
                return value ?? "";
            return value.Replace("<", "\uFF1C");
        }
    }
}

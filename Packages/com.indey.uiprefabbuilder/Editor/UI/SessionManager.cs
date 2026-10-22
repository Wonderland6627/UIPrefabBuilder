using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Indey.UIPrefabBuilder.Core;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    [Serializable]
    public class ChatSession
    {
        public string SessionId;
        public string Title;
        /// <summary>Shared file stem for Sessions/{name}.json and Logs/{name}.log.</summary>
        public string StorageName;
        /// <summary>When true, the title is the AI summary and later saves keep it.</summary>
        public bool TitleLocked;
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        public List<SerializedMessage> Messages = new List<SerializedMessage>();
        public List<SerializedBubble> Bubbles = new List<SerializedBubble>();
        public List<SerializedAssetChange> ChangedAssets = new List<SerializedAssetChange>();
    }

    [Serializable]
    public class SerializedMessage
    {
        public string Role;
        public string Content;
    }

    [Serializable]
    public class SerializedBubble
    {
        public string Type;
        public string Content;
    }

    public class SerializedAssetChange
    {
        public string Path;
        public string Kind;
        public long Ticks;
    }

    public class SessionManager
    {
        private string _currentId;

        public static string SessionDir
        {
            get
            {
                var baseDir = ConsoleLogger.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "UIPrefabBuilder");
                return Path.Combine(baseDir, "Sessions");
            }
        }

        public static string DataDirectory
        {
            get
            {
                var baseDir = ConsoleLogger.BaseDirectory;
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "UIPrefabBuilder");
                return baseDir;
            }
        }

        public const string PlaceholderTitle = "新会话";

        public const string TitleSummarySystemPrompt =
            "你是会话标题生成器。根据用户的第一条请求生成一个不超过20个字的标题，概括要做的界面或任务。使用与用户相同的语言。只输出标题本身，不要引号、序号、句号或解释。";

        public string CurrentSessionId => _currentId;

        public SessionManager()
        {
            Directory.CreateDirectory(SessionDir);
            var saved = EditorPrefs.GetString("UIPrefabBuilder_LastSession", "");
            _currentId = string.IsNullOrEmpty(saved) ? Guid.NewGuid().ToString("N") : saved;
            if (string.IsNullOrEmpty(saved))
                EditorPrefs.SetString("UIPrefabBuilder_LastSession", _currentId);
            EnsureReadableNames();
            ConsoleLogger.SetSession(_currentId, FindStorageName(_currentId));
        }

        public void NewSession()
        {
            _currentId = Guid.NewGuid().ToString("N");
            var session = new ChatSession
            {
                SessionId = _currentId,
                Title = PlaceholderTitle,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            WriteSession(session, null);
            EditorPrefs.SetString("UIPrefabBuilder_LastSession", _currentId);
            ConsoleLogger.SetSession(_currentId, session.StorageName);
        }

        public void SaveCurrentSession(MessageHistory history, List<ChatBubble> bubbles)
        {
            if (history == null || history.Count <= 1) return;
            var previous = ReadSession(_currentId, out var previousStem);
            var session = new ChatSession
            {
                SessionId = _currentId,
                Title = ResolveSavedTitle(previous),
                TitleLocked = previous != null && previous.TitleLocked,
                StorageName = previous?.StorageName,
                CreatedAt = previous?.CreatedAt ?? DateTime.Now,
                UpdatedAt = DateTime.Now,
                Messages = history.Messages.Select(m => new SerializedMessage { Role = m.Role.ToString(), Content = m.Content }).ToList(),
                Bubbles = bubbles.Select(b => new SerializedBubble { Type = b.Type.ToString(), Content = b.Content }).ToList(),
                ChangedAssets = SessionAssetChanges.Snapshot().Select(a => new SerializedAssetChange
                {
                    Path = a.Path,
                    Kind = a.Kind.ToString(),
                    Ticks = a.Ticks
                }).ToList()
            };
            WriteSession(session, previousStem);
            EditorPrefs.SetString("UIPrefabBuilder_LastSession", _currentId);
        }

        public void RestoreLastSession(MessageHistory history, List<ChatBubble> bubbles)
        {
            LoadSession(_currentId, history, bubbles);
        }

        public void LoadSession(string id, MessageHistory history, List<ChatBubble> bubbles)
        {
            var path = FindSessionPath(id);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var session = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(path));
                if (session == null) return;
                history.Clear();
                bubbles.Clear();
                if (session.Messages != null)
                {
                    foreach (var m in session.Messages)
                    {
                        if (Enum.TryParse<ChatRole>(m.Role, out var role))
                        {
                            switch (role)
                            {
                                case ChatRole.System: history.SetSystemPrompt(m.Content); break;
                                case ChatRole.User: history.AddUser(m.Content); break;
                                case ChatRole.Assistant: history.AddAssistant(m.Content); break;
                            }
                        }
                    }
                }
                if (session.Bubbles != null)
                {
                    foreach (var b in session.Bubbles)
                    {
                        if (Enum.TryParse<BubbleType>(b.Type, out var t))
                            bubbles.Add(new ChatBubble { Type = t, Content = b.Content });
                    }
                }
                SessionAssetChanges.Replace(ReadAssetChanges(session));
                _currentId = id;
                EditorPrefs.SetString("UIPrefabBuilder_LastSession", id);
                ConsoleLogger.SetSession(id, session.StorageName);
            }
            catch (Exception e) { Debug.LogWarning("[UIPrefabBuilder] Failed to load session: " + e.Message); }
        }

        public List<ChatSession> ListSessions()
        {
            var result = new List<ChatSession>();
            if (!Directory.Exists(SessionDir)) return result;
            foreach (var f in Directory.GetFiles(SessionDir, "*.json"))
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(f));
                    if (s != null) result.Add(s);
                }
                catch { }
            }
            return result.OrderByDescending(s => s.UpdatedAt).ToList();
        }

        public void DeleteSession(string id)
        {
            var path = FindSessionPath(id);
            string stem = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                stem = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var session = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(path));
                    if (!string.IsNullOrEmpty(session?.StorageName))
                        stem = session.StorageName;
                }
                catch { }
                File.Delete(path);
            }
            DeleteFileIfExists(LogPath(stem));
            DeleteFileIfExists(LogPath(id));
            if (id == _currentId)
                ConsoleLogger.RetargetLog(id);
        }

        private static List<SessionAssetChange> ReadAssetChanges(ChatSession session)
        {
            var restored = new List<SessionAssetChange>();
            if (session?.ChangedAssets == null) return restored;
            foreach (var change in session.ChangedAssets)
            {
                if (change == null || string.IsNullOrEmpty(change.Path)) continue;
                var kind = string.Equals(change.Kind, nameof(SessionAssetChangeKind.Added), StringComparison.OrdinalIgnoreCase)
                    ? SessionAssetChangeKind.Added
                    : SessionAssetChangeKind.Modified;
                restored.Add(new SessionAssetChange
                {
                    Path = change.Path,
                    Kind = kind,
                    Ticks = change.Ticks
                });
            }
            return restored;
        }

        public static void OpenDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);
#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", "\"" + directory.Replace('/', '\\') + "\"");
#elif UNITY_EDITOR_OSX
            System.Diagnostics.Process.Start("open", directory);
#else
            System.Diagnostics.Process.Start("xdg-open", directory);
#endif
        }

        public bool NeedsAutoTitle()
        {
            var session = ReadSession(_currentId, out _);
            if (session == null) return true;
            if (session.TitleLocked) return false;
            return IsPlaceholderTitle(session.Title);
        }

        public bool TryApplySummaryTitle(string sessionId, string rawTitle, out string error)
        {
            var title = CleanTitle(rawTitle);
            if (string.IsNullOrEmpty(title) || IsPlaceholderTitle(title))
            {
                error = "模型没有返回可用标题。";
                return false;
            }

            var session = ReadSession(sessionId, out var fileStem);
            if (session == null)
            {
                error = "找不到这条会话。";
                return false;
            }
            if (session.TitleLocked)
            {
                error = null;
                return true;
            }

            session.Title = title;
            session.TitleLocked = true;
            WriteSession(session, fileStem);
            error = null;
            return true;
        }

        public static string ExportToMarkdown(List<ChatBubble> bubbles, string modelName = "")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# UIPrefabBuilder Chat Export");
            sb.AppendLine($"- **Date**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (!string.IsNullOrEmpty(modelName))
                sb.AppendLine($"- **Model**: {modelName}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var b in bubbles)
            {
                switch (b.Type)
                {
                    case BubbleType.User:
                        sb.AppendLine("## User");
                        sb.AppendLine(b.Content);
                        sb.AppendLine();
                        break;
                    case BubbleType.Thinking:
                        sb.AppendLine("## Thinking");
                        sb.AppendLine("<details><summary>Thinking process</summary>");
                        sb.AppendLine();
                        sb.AppendLine(b.Content);
                        sb.AppendLine();
                        sb.AppendLine("</details>");
                        sb.AppendLine();
                        break;
                    case BubbleType.AI: case BubbleType.AIStream:
                        sb.AppendLine("## Assistant");
                        sb.AppendLine(b.Content);
                        sb.AppendLine();
                        break;
                    case BubbleType.Code:
                        sb.AppendLine("## Generated Code");
                        sb.AppendLine("```csharp");
                        sb.AppendLine(b.Content);
                        sb.AppendLine("```");
                        sb.AppendLine();
                        break;
                    case BubbleType.ToolCall:
                        sb.AppendLine("## Tool Call");
                        sb.AppendLine($"> {b.Content}");
                        sb.AppendLine();
                        break;
                    case BubbleType.Result:
                        sb.AppendLine("## Result");
                        sb.AppendLine($"> {b.Content}");
                        sb.AppendLine();
                        break;
                    case BubbleType.Error:
                        sb.AppendLine("## Error");
                        sb.AppendLine($"> **ERROR**: {b.Content}");
                        sb.AppendLine();
                        break;
                }
                sb.AppendLine("---");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string ResolveSavedTitle(ChatSession previous)
        {
            if (previous != null && previous.TitleLocked && !string.IsNullOrWhiteSpace(previous.Title))
                return previous.Title;
            if (previous != null && !IsPlaceholderTitle(previous.Title))
                return previous.Title;
            return PlaceholderTitle;
        }

        private static bool IsPlaceholderTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return true;
            return title == PlaceholderTitle || title == "New Chat";
        }

        private void EnsureReadableNames()
        {
            if (!Directory.Exists(SessionDir)) return;
            foreach (var file in Directory.GetFiles(SessionDir, "*.json"))
            {
                ChatSession session;
                try { session = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(file)); }
                catch { continue; }
                if (session == null || string.IsNullOrEmpty(session.SessionId)) continue;

                var fileStem = Path.GetFileNameWithoutExtension(file);
                var desired = ResolveUniqueStem(MakeStorageName(session.Title, session.SessionId), session.SessionId);
                if (string.Equals(session.StorageName, desired, StringComparison.Ordinal)
                    && string.Equals(fileStem, desired, StringComparison.OrdinalIgnoreCase))
                {
                    var legacyLog = FindLogStem(null, null, session.SessionId);
                    if (!string.IsNullOrEmpty(legacyLog)
                        && !string.Equals(legacyLog, desired, StringComparison.OrdinalIgnoreCase))
                        MoveLog(legacyLog, desired);
                    continue;
                }
                try { WriteSession(session, fileStem); }
                catch (Exception e) { Debug.LogWarning("[UIPrefabBuilder] Failed to rename session: " + e.Message); }
            }
        }

        private void WriteSession(ChatSession session, string previousFileStem)
        {
            if (session == null || string.IsNullOrEmpty(session.SessionId)) return;
            Directory.CreateDirectory(SessionDir);
            var desired = ResolveUniqueStem(MakeStorageName(session.Title, session.SessionId), session.SessionId);
            var previousLogStem = FindLogStem(session.StorageName, previousFileStem, session.SessionId);
            session.StorageName = desired;
            var path = Path.Combine(SessionDir, desired + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(session, Formatting.Indented));
            if (!string.IsNullOrEmpty(previousFileStem)
                && !string.Equals(previousFileStem, desired, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = Path.Combine(SessionDir, previousFileStem + ".json");
                if (File.Exists(oldPath)
                    && !string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    File.Delete(oldPath);
            }
            if (!string.IsNullOrEmpty(previousLogStem)
                && !string.Equals(previousLogStem, desired, StringComparison.OrdinalIgnoreCase))
                MoveLog(previousLogStem, desired);
            if (session.SessionId == _currentId)
                ConsoleLogger.RetargetLog(desired);
        }

        private string ResolveUniqueStem(string desired, string sessionId)
        {
            var path = Path.Combine(SessionDir, desired + ".json");
            if (!File.Exists(path)) return desired;
            var existing = ReadFile(path);
            if (existing != null && existing.SessionId == sessionId) return desired;
            return sessionId;
        }

        private static string MakeStorageName(string title, string sessionId)
        {
            var slug = SanitizeFileStem(string.IsNullOrWhiteSpace(title) ? "chat" : title);
            var shortId = string.IsNullOrEmpty(sessionId)
                ? "session"
                : (sessionId.Length <= 8 ? sessionId : sessionId.Substring(0, 8));
            return slug + "_" + shortId;
        }

        private static string SanitizeFileStem(string title)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (var c in title.Trim())
            {
                if (char.IsWhiteSpace(c) || c == '.' || c == ',' || c == '。' || c == '，')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
                    continue;
                }
                if (Array.IndexOf(invalid, c) >= 0) continue;
                sb.Append(c);
                if (sb.Length >= 40) break;
            }
            var stem = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(stem) ? "chat" : stem;
        }

        private string FindStorageName(string sessionId)
        {
            var session = ReadSession(sessionId, out _);
            return string.IsNullOrEmpty(session?.StorageName) ? null : session.StorageName;
        }

        private ChatSession ReadSession(string sessionId, out string fileStem)
        {
            fileStem = null;
            var path = FindSessionPath(sessionId);
            if (string.IsNullOrEmpty(path)) return null;
            fileStem = Path.GetFileNameWithoutExtension(path);
            return ReadFile(path);
        }

        private static ChatSession ReadFile(string path)
        {
            try { return JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(path)); }
            catch { return null; }
        }

        private string FindSessionPath(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId) || !Directory.Exists(SessionDir)) return null;
            var direct = Path.Combine(SessionDir, sessionId + ".json");
            if (File.Exists(direct)) return direct;
            foreach (var file in Directory.GetFiles(SessionDir, "*.json"))
            {
                var session = ReadFile(file);
                if (session != null && session.SessionId == sessionId) return file;
            }
            return null;
        }

        private static string FindLogStem(string storageName, string fileStem, string sessionId)
        {
            if (LogFileExists(storageName)) return storageName;
            if (LogFileExists(fileStem)) return fileStem;
            if (LogFileExists(sessionId)) return sessionId;
            return null;
        }

        private static bool LogFileExists(string stem)
        {
            var path = LogPath(stem);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        private static string LogPath(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return null;
            return Path.Combine(DataDirectory, "Logs", stem + ".log");
        }

        private static void MoveLog(string oldStem, string newStem)
        {
            try
            {
                var oldPath = LogPath(oldStem);
                var newPath = LogPath(newStem);
                if (string.IsNullOrEmpty(oldPath) || string.IsNullOrEmpty(newPath) || !File.Exists(oldPath)) return;
                if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(newPath));
                if (File.Exists(newPath))
                {
                    File.AppendAllText(newPath, File.ReadAllText(oldPath));
                    File.Delete(oldPath);
                    return;
                }
                File.Move(oldPath, newPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UIPrefabBuilder] Failed to rename log: " + e.Message);
            }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }

        private static string CollapseWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
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
            return sb.ToString();
        }

        private static string CleanTitle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var line = raw.Trim();
            var breakAt = line.IndexOfAny(new[] { '\r', '\n' });
            if (breakAt >= 0) line = line.Substring(0, breakAt).Trim();
            line = line.Trim('"', '\'', '`', '*', '#', '“', '”', '‘', '’', ' ', '。', '.', '：', ':');
            if (line.StartsWith("标题"))
                line = line.Substring("标题".Length).Trim(' ', '：', ':');
            line = CollapseWhitespace(line);
            if (line.Length > 40) line = line.Substring(0, 40).Trim();
            return string.IsNullOrWhiteSpace(line) ? null : line;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public DateTime CreatedAt;
        public DateTime UpdatedAt;
        public List<SerializedMessage> Messages = new List<SerializedMessage>();
        public List<SerializedBubble> Bubbles = new List<SerializedBubble>();
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

        public string CurrentSessionId => _currentId;

        public SessionManager()
        {
            Directory.CreateDirectory(SessionDir);
            _currentId = EditorPrefs.GetString("UIPrefabBuilder_LastSession", Guid.NewGuid().ToString("N"));
            ConsoleLogger.SetSession(_currentId);
        }

        public void NewSession()
        {
            _currentId = Guid.NewGuid().ToString("N");
            EditorPrefs.SetString("UIPrefabBuilder_LastSession", _currentId);
            ConsoleLogger.SetSession(_currentId);

            var session = new ChatSession
            {
                SessionId = _currentId,
                Title = "New Chat",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            Directory.CreateDirectory(SessionDir);
            var path = Path.Combine(SessionDir, _currentId + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(session, Formatting.Indented));
        }

        public void SaveCurrentSession(MessageHistory history, List<ChatBubble> bubbles)
        {
            if (history == null || history.Count <= 1) return;
            var session = new ChatSession
            {
                SessionId = _currentId,
                Title = GetTitle(bubbles),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Messages = history.Messages.Select(m => new SerializedMessage { Role = m.Role.ToString(), Content = m.Content }).ToList(),
                Bubbles = bubbles.Select(b => new SerializedBubble { Type = b.Type.ToString(), Content = b.Content }).ToList()
            };
            Directory.CreateDirectory(SessionDir);
            var path = Path.Combine(SessionDir, _currentId + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(session, Formatting.Indented));
            EditorPrefs.SetString("UIPrefabBuilder_LastSession", _currentId);
        }

        public void RestoreLastSession(MessageHistory history, List<ChatBubble> bubbles)
        {
            LoadSession(_currentId, history, bubbles);
        }

        public void LoadSession(string id, MessageHistory history, List<ChatBubble> bubbles)
        {
            var path = Path.Combine(SessionDir, id + ".json");
            if (!File.Exists(path)) return;
            try
            {
                var session = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(path));
                history.Clear();
                bubbles.Clear();
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
                foreach (var b in session.Bubbles)
                {
                    if (Enum.TryParse<BubbleType>(b.Type, out var t))
                        bubbles.Add(new ChatBubble { Type = t, Content = b.Content });
                }
                _currentId = id;
                EditorPrefs.SetString("UIPrefabBuilder_LastSession", id);
                ConsoleLogger.SetSession(id);
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
            var path = Path.Combine(SessionDir, id + ".json");
            if (File.Exists(path)) File.Delete(path);
            var logPath = Path.Combine(DataDirectory, "Logs", id + ".log");
            if (File.Exists(logPath)) File.Delete(logPath);
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

        private string GetTitle(List<ChatBubble> bubbles)
        {
            var first = bubbles.FirstOrDefault(b => b.Type == BubbleType.User);
            if (first == null) return "New Chat";
            return first.Content.Length > 40 ? first.Content.Substring(0, 40) + "..." : first.Content;
        }
    }
}

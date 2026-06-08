using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Logging
{
    public enum LogLevel { Info, Warning, Error }

    [Serializable]
    public class LogEntry
    {
        public DateTime Timestamp;
        public LogLevel Level;
        public string Message;
    }

    public static class ConsoleLogger
    {
        private static readonly List<LogEntry> _entries = new List<LogEntry>();
        private static readonly object _lock = new object();
        private static string _logFilePath;
        private static string _baseDirectory;
        private static string _currentSessionId;
        private static bool _initialized;

        public static string BaseDirectory => _baseDirectory;

        public static IReadOnlyList<LogEntry> Entries
        {
            get { lock (_lock) return _entries; }
        }

        public static event Action<LogEntry> OnLogAdded;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            _baseDirectory = Path.Combine(projectRoot, "Library", "UIPrefabBuilder");
            Directory.CreateDirectory(Path.Combine(_baseDirectory, "Sessions"));
            Directory.CreateDirectory(Path.Combine(_baseDirectory, "Logs"));

            Log($"Logger initialized. BaseDir: {_baseDirectory}");
        }

        public static void SetSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            _currentSessionId = sessionId;

            var logsDir = Path.Combine(_baseDirectory, "Logs");
            Directory.CreateDirectory(logsDir);
            _logFilePath = Path.Combine(logsDir, sessionId + ".log");

            Log($"--- Session started: {sessionId} ---");
        }

        public static void Log(string message) => AddEntry(LogLevel.Info, message);
        public static void Warning(string message) => AddEntry(LogLevel.Warning, message);
        public static void Error(string message) => AddEntry(LogLevel.Error, message);

        public static void LogBlock(string tag, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            var lines = content.Split('\n');
            var header = lines.Length > 1 ? $"[{tag}] ({lines.Length} lines, {content.Length} chars)" : $"[{tag}] {content}";
            AddEntry(LogLevel.Info, header);
            WriteRawToFile($"--- {tag} START ---\n{content}\n--- {tag} END ---");
        }

        /// <summary>
        /// Compact log for tool calls: single line with name + truncated args/result.
        /// </summary>
        public static void LogToolCall(string toolName, string args, string result)
        {
            var argPreview = TruncateForLog(args, 150);
            var line = $"[Tool] {toolName}({argPreview})";
            AddEntry(LogLevel.Info, line);

            if (!string.IsNullOrEmpty(result))
            {
                var resultPreview = TruncateForLog(result, 200);
                AddEntry(LogLevel.Info, $"[Tool] {toolName} => {resultPreview}");
            }
        }

        private static string TruncateForLog(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r\n", " ").Replace("\n", " ");
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }

        public static void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        public static void OpenLogDirectory()
        {
            var dir = Path.Combine(_baseDirectory ?? "", "Logs");
            if (!Directory.Exists(dir))
            {
                dir = _baseDirectory;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            }
#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", dir.Replace('/', '\\'));
#elif UNITY_EDITOR_OSX
            System.Diagnostics.Process.Start("open", dir);
#else
            System.Diagnostics.Process.Start("xdg-open", dir);
#endif
        }

        private static void AddEntry(LogLevel level, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            lock (_lock)
            {
                _entries.Add(entry);
                if (_entries.Count > 2000)
                    _entries.RemoveRange(0, _entries.Count - 1500);
            }

            WriteToFile(entry);
            OnLogAdded?.Invoke(entry);
        }

        private static void WriteToFile(LogEntry entry)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                var line = $"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.Level.ToString().ToUpperInvariant()}] {entry.Message}";
                File.AppendAllText(_logFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        private static void WriteRawToFile(string text)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                File.AppendAllText(_logFilePath, text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }
    }
}

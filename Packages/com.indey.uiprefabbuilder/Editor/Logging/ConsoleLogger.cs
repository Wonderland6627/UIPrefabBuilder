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

            LogVolatile($"Logger initialized. BaseDir: {_baseDirectory}");
        }

        public static void SetSession(string sessionId, string storageName = null)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (string.IsNullOrEmpty(_baseDirectory))
                Initialize();

            var stem = string.IsNullOrEmpty(storageName) ? sessionId : storageName;
            var newPath = Path.Combine(_baseDirectory, "Logs", stem + ".log");
            bool same = string.Equals(_currentSessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(_logFilePath, newPath, StringComparison.OrdinalIgnoreCase);
            _currentSessionId = sessionId;
            Directory.CreateDirectory(Path.GetDirectoryName(newPath));
            _logFilePath = newPath;
            if (!same)
                LogVolatile($"--- Session started: {sessionId} ({stem}) ---");
        }

        /// <summary>
        /// Point later file writes at Logs/{storageName}.log without creating the file.
        /// </summary>
        public static void RetargetLog(string storageName)
        {
            if (string.IsNullOrEmpty(storageName) || string.IsNullOrEmpty(_baseDirectory)) return;
            var logsDir = Path.Combine(_baseDirectory, "Logs");
            Directory.CreateDirectory(logsDir);
            _logFilePath = Path.Combine(logsDir, storageName + ".log");
        }

        public static void Log(string message) => AddEntry(LogLevel.Info, message);

        /// <summary>
        /// Visible in the console panel, but not written to the session log file.
        /// Window startup uses this so merely opening the window does not create a chat log.
        /// </summary>
        public static void LogVolatile(string message) => AddEntry(LogLevel.Info, message, false);
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
        /// Writes the complete tool arguments and result to the log file.
        /// The console panel keeps a short index line so a large payload does not have to be drawn.
        /// </summary>
        public static void LogToolCall(string toolName, string args, string result, string taskId, string stepId, string messageId, string toolCallId)
        {
            AddEntry(LogLevel.Info,
                $"[Tool] {toolName} task={taskId} step={stepId} message={messageId} tool={toolCallId} args={args?.Length ?? 0}c result={result?.Length ?? 0}c");
            WriteRawToFile(
                "--- TOOL " + toolName + " task=" + taskId + " step=" + stepId + " message=" + messageId + " tool=" + toolCallId + " START ---\n"
                + "ARGUMENTS\n" + (args ?? "") + "\nRESULT\n" + (result ?? "")
                + "\n--- TOOL " + toolName + " END ---");
        }

        public static void LogAudit(string kind, string taskId, string stepId, string messageId, string toolCallId, string body)
        {
            if (string.IsNullOrEmpty(body)) return;
            AddEntry(LogLevel.Info,
                $"[Audit] {kind} task={taskId} step={stepId} message={messageId} tool={toolCallId} ({body.Length} chars)");
            WriteRawToFile(
                "--- AUDIT " + kind + " task=" + taskId + " step=" + stepId + " message=" + messageId + " tool=" + toolCallId + " START ---\n"
                + body
                + "\n--- AUDIT " + kind + " END ---");
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

        private static void AddEntry(LogLevel level, string message, bool persist = true)
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

            if (persist)
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

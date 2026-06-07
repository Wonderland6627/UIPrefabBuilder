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
        private static string _logDirectory;
        private static bool _initialized;

        public static IReadOnlyList<LogEntry> Entries
        {
            get { lock (_lock) return _entries; }
        }

        public static event Action<LogEntry> OnLogAdded;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            var projectName = SanitizeFileName(GetProjectName());
            var date = DateTime.Now.ToString("yyyy-MM-dd");
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logDirectory = Path.Combine(appData, "UIPrefabBuilder", "Logs", projectName);
            Directory.CreateDirectory(_logDirectory);
            _logFilePath = Path.Combine(_logDirectory, date + ".log");

            Log($"Logger initialized. Project: {projectName}");
        }

        public static void Log(string message) => AddEntry(LogLevel.Info, message);
        public static void Warning(string message) => AddEntry(LogLevel.Warning, message);
        public static void Error(string message) => AddEntry(LogLevel.Error, message);

        public static void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        public static void OpenLogDirectory()
        {
            if (string.IsNullOrEmpty(_logDirectory)) return;
            Directory.CreateDirectory(_logDirectory);
#if UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", _logDirectory.Replace('/', '\\'));
#elif UNITY_EDITOR_OSX
            System.Diagnostics.Process.Start("open", _logDirectory);
#else
            System.Diagnostics.Process.Start("xdg-open", _logDirectory);
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
            catch
            {
                // Silently ignore file write errors to avoid infinite recursion
            }
        }

        private static string GetProjectName()
        {
            var productName = Application.productName;
            if (!string.IsNullOrWhiteSpace(productName) && productName != "DefaultCompany")
                return productName;

            var dataPath = Application.dataPath;
            var projectDir = Path.GetDirectoryName(dataPath);
            return string.IsNullOrEmpty(projectDir) ? "UnknownProject" : Path.GetFileName(projectDir);
        }

        private static string SanitizeFileName(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (Path.GetInvalidFileNameChars().Length > 0 && Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}

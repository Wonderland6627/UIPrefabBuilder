using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Core
{
    /// <summary>
    /// Copies images that are about to be sent to the model into
    /// Library/UIPrefabBuilder/Attachments/{session}/ so a later turn can still
    /// read them after the user deletes the original file.
    /// </summary>
    public static class SessionImageArchive
    {
        public const string FolderName = "Attachments";
        const string IndexFileName = "index.json";

        static readonly object Gate = new object();

        class ArchiveIndex
        {
            public List<ArchiveEntry> files = new List<ArchiveEntry>();
        }

        class ArchiveEntry
        {
            public string role;
            public string source;
            public string archive;
            public string sha256;
        }

        /// <summary>
        /// Writes <paramref name="bytes"/> into the current session folder.
        /// Identical bytes are stored once. A new source path for those bytes is
        /// recorded so either path still resolves after the original is deleted.
        /// Returns a path relative to the UIPrefabBuilder data directory, or null.
        /// </summary>
        public static string Backup(string role, string sourcePath, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var stem = CurrentStem();
            if (string.IsNullOrEmpty(stem)) return null;

            try
            {
                lock (Gate)
                {
                    var sha = HashBytes(bytes);
                    var index = LoadIndex(stem);
                    var existing = FindByHash(index, sha);
                    string archiveName;
                    var wrote = false;
                    if (existing != null && File.Exists(Path.Combine(SessionDirectory(stem), existing.archive)))
                    {
                        archiveName = existing.archive;
                    }
                    else
                    {
                        archiveName = NextFileName(stem, index, role, sourcePath, bytes);
                        Directory.CreateDirectory(SessionDirectory(stem));
                        File.WriteAllBytes(Path.Combine(SessionDirectory(stem), archiveName), bytes);
                        wrote = true;
                    }

                    if (!HasSource(index, archiveName, sourcePath))
                    {
                        index.files.Add(new ArchiveEntry
                        {
                            role = string.IsNullOrEmpty(role) ? "image" : role,
                            source = Normalize(sourcePath),
                            archive = archiveName,
                            sha256 = sha
                        });
                        SaveIndex(stem, index);
                    }

                    var relative = RelativePath(stem, archiveName);
                    if (wrote)
                        ConsoleLogger.Log($"[Archive] {role} -> {relative} ({bytes.Length / 1024}KB)");
                    return relative;
                }
            }
            catch (Exception e)
            {
                ConsoleLogger.Error("[Archive] Failed to backup image: " + e.Message);
                return null;
            }
        }

        /// <summary>Backup the bytes of a data URL, or the file at <paramref name="sourcePath"/> when the URL is empty.</summary>
        public static string BackupDataUrl(string role, string sourcePath, string dataUrl)
        {
            var bytes = DecodeDataUrl(dataUrl);
            if (bytes == null && !string.IsNullOrEmpty(sourcePath))
            {
                var full = Resolve(sourcePath);
                if (!string.IsNullOrEmpty(full))
                {
                    try { bytes = File.ReadAllBytes(full); }
                    catch { bytes = null; }
                }
            }
            if (bytes == null) return null;
            return Backup(role, sourcePath, bytes);
        }

        /// <summary>
        /// Absolute path for a stored image. An existing original wins. A missing
        /// original falls back to this session's archive index.
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (Path.IsPathRooted(path) && File.Exists(path))
                return path;

            var normalized = Normalize(path);
            var root = DataRoot();
            if (!string.IsNullOrEmpty(root))
            {
                var underData = Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(underData)) return underData;
            }

            var projectRoot = ProjectRoot();
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var underProject = Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(underProject)) return underProject;
            }

            if (File.Exists(path))
                return Path.GetFullPath(path);

            return FindArchived(normalized);
        }

        /// <summary>Relative path of the newest user attachment in the session, or null.</summary>
        public static string LatestUserAttachment(string stem = null)
        {
            stem = string.IsNullOrEmpty(stem) ? CurrentStem() : stem;
            if (string.IsNullOrEmpty(stem)) return null;
            lock (Gate)
            {
                var index = LoadIndex(stem);
                for (int i = index.files.Count - 1; i >= 0; i--)
                {
                    var entry = index.files[i];
                    if (entry == null || !string.Equals(entry.role, "user", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (string.IsNullOrEmpty(entry.archive)) continue;
                    var full = Path.Combine(SessionDirectory(stem), entry.archive);
                    if (!File.Exists(full)) continue;
                    return RelativePath(stem, entry.archive);
                }
            }
            return null;
        }

        /// <summary>
        /// Puts the session's archived design image back into <see cref="DesignImageContext"/>
        /// when the in-memory path is empty or the file it names is gone.
        /// </summary>
        public static void RestoreDesignContext(string stem = null)
        {
            var current = DesignImageContext.CurrentAssetPath;
            if (!string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(Resolve(current)))
            {
                FillDesignSize(Resolve(current));
                return;
            }

            var archived = LatestUserAttachment(stem);
            if (string.IsNullOrEmpty(archived)) return;
            DesignImageContext.CurrentAssetPath = archived;
            FillDesignSize(Resolve(archived));
        }

        public static void Move(string oldStem, string newStem)
        {
            if (string.IsNullOrEmpty(oldStem) || string.IsNullOrEmpty(newStem)) return;
            if (string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase)) return;

            lock (Gate)
            {
                var from = SessionDirectory(oldStem);
                var to = SessionDirectory(newStem);
                if (!Directory.Exists(from)) return;
                try
                {
                    Directory.CreateDirectory(DataRoot() == null
                        ? Path.GetDirectoryName(to)
                        : Path.Combine(DataRoot(), FolderName));
                    if (!Directory.Exists(to))
                    {
                        Directory.Move(from, to);
                        return;
                    }

                    var leftover = false;
                    foreach (var file in Directory.GetFiles(from))
                    {
                        var name = Path.GetFileName(file);
                        if (string.Equals(name, IndexFileName, StringComparison.OrdinalIgnoreCase)) continue;
                        var dest = Path.Combine(to, name);
                        if (File.Exists(dest))
                        {
                            leftover = true;
                            continue;
                        }
                        File.Move(file, dest);
                    }
                    MergeIndex(oldStem, newStem);
                    if (!leftover)
                        Directory.Delete(from, true);
                }
                catch (Exception e)
                {
                    ConsoleLogger.Error("[Archive] Failed to move attachments: " + e.Message);
                }
            }
        }

        public static void Delete(string stem)
        {
            if (string.IsNullOrEmpty(stem)) return;
            lock (Gate)
            {
                var dir = SessionDirectory(stem);
                if (!Directory.Exists(dir)) return;
                try { Directory.Delete(dir, true); }
                catch (Exception e)
                {
                    ConsoleLogger.Error("[Archive] Failed to delete attachments: " + e.Message);
                }
            }
        }

        public static string RewritePrefix(string path, string oldStem, string newStem)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(oldStem) || string.IsNullOrEmpty(newStem))
                return path;
            if (string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase))
                return path;

            var normalized = Normalize(path);
            var oldPrefix = FolderName + "/" + oldStem + "/";
            var index = normalized.IndexOf(oldPrefix, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return path;
            return normalized.Substring(0, index)
                + FolderName + "/" + newStem + "/"
                + normalized.Substring(index + oldPrefix.Length);
        }

        public static void RewriteHistory(MessageHistory history, string oldStem, string newStem)
        {
            if (history == null) return;
            if (string.IsNullOrEmpty(oldStem) || string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var message in history.Messages)
            {
                if (message == null) continue;
                if (message.ContentParts != null)
                {
                    foreach (var part in message.ContentParts)
                    {
                        if (part == null) continue;
                        part.SourcePath = RewritePrefix(part.SourcePath, oldStem, newStem);
                    }
                }
                if (message.ImageArchives == null) continue;
                foreach (var image in message.ImageArchives)
                {
                    if (image == null) continue;
                    image.SourcePath = RewritePrefix(image.SourcePath, oldStem, newStem);
                }
            }
        }

        static void FillDesignSize(string fullPath)
        {
            if (DesignImageContext.HasSize || string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                return;
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                if (!TryGetPngSize(bytes, out var width, out var height)) return;
                DesignImageContext.Width = width;
                DesignImageContext.Height = height;
            }
            catch { }
        }

        static string FindArchived(string normalizedPath)
        {
            var stem = StemFromArchivePath(normalizedPath) ?? CurrentStem();
            if (string.IsNullOrEmpty(stem)) return null;
            lock (Gate)
            {
                var index = LoadIndex(stem);
                var fileName = Path.GetFileName(normalizedPath);
                foreach (var entry in index.files)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.archive)) continue;
                    var sameSource = SameSource(entry.source, normalizedPath);
                    var underArchive = normalizedPath.IndexOf(FolderName + "/", StringComparison.OrdinalIgnoreCase) >= 0;
                    var sameArchive = underArchive
                        && string.Equals(entry.archive, fileName, StringComparison.OrdinalIgnoreCase);
                    if (!sameSource && !sameArchive) continue;
                    var full = Path.Combine(SessionDirectory(stem), entry.archive);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }

        static string StemFromArchivePath(string normalizedPath)
        {
            var marker = FolderName + "/";
            var index = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            var rest = normalizedPath.Substring(index + marker.Length);
            var slash = rest.IndexOf('/');
            if (slash <= 0) return null;
            return rest.Substring(0, slash);
        }

        static bool HasSource(ArchiveIndex index, string archiveName, string sourcePath)
        {
            foreach (var entry in index.files)
            {
                if (entry == null) continue;
                if (!string.Equals(entry.archive, archiveName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.IsNullOrEmpty(sourcePath))
                    return string.IsNullOrEmpty(entry.source);
                if (SameSource(entry.source, sourcePath)) return true;
            }
            return false;
        }

        static ArchiveEntry FindByHash(ArchiveIndex index, string sha)
        {
            foreach (var entry in index.files)
            {
                if (entry != null && string.Equals(entry.sha256, sha, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        static bool SameSource(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return false;
            left = Normalize(left);
            right = Normalize(right);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase)) return true;
            return left.EndsWith("/" + right, StringComparison.OrdinalIgnoreCase)
                || right.EndsWith("/" + left, StringComparison.OrdinalIgnoreCase);
        }

        static string NextFileName(string stem, ArchiveIndex index, string role, string sourcePath, byte[] bytes)
        {
            var seq = NextSequence(stem, index);
            var safeRole = SanitizeToken(string.IsNullOrEmpty(role) ? "image" : role);
            var ext = ExtensionFor(sourcePath, bytes);
            var baseName = string.IsNullOrEmpty(sourcePath)
                ? null
                : SanitizeToken(Path.GetFileNameWithoutExtension(sourcePath));
            if (string.IsNullOrEmpty(baseName) || string.Equals(baseName, safeRole, StringComparison.OrdinalIgnoreCase))
                return seq.ToString("D2") + "_" + safeRole + ext;
            return seq.ToString("D2") + "_" + safeRole + "_" + baseName + ext;
        }

        static int NextSequence(string stem, ArchiveIndex index)
        {
            int max = 0;
            foreach (var entry in index.files)
                ConsiderSequence(entry?.archive, ref max);
            var dir = SessionDirectory(stem);
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir))
                    ConsiderSequence(Path.GetFileName(file), ref max);
            }
            return max + 1;
        }

        static void ConsiderSequence(string name, ref int max)
        {
            if (string.IsNullOrEmpty(name)) return;
            int i = 0;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            if (i == 0 || i >= name.Length || name[i] != '_') return;
            if (int.TryParse(name.Substring(0, i), out var n) && n > max)
                max = n;
        }

        static string ExtensionFor(string sourcePath, byte[] bytes)
        {
            var ext = string.IsNullOrEmpty(sourcePath) ? "" : Path.GetExtension(sourcePath);
            if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                return ext.ToLowerInvariant();
            if (bytes != null && bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return ".png";
            if (bytes != null && bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
                return ".jpg";
            return ".png";
        }

        static string SanitizeToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == '/' || c == '\\' || Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            var text = sb.ToString().Trim(' ', '.', '_');
            if (text.Length > 48) text = text.Substring(0, 48).Trim(' ', '.', '_');
            return text;
        }

        static string RelativePath(string stem, string archiveName)
        {
            return FolderName + "/" + stem + "/" + archiveName;
        }

        static string CurrentStem()
        {
            var stem = ConsoleLogger.SessionStem;
            if (!string.IsNullOrEmpty(stem)) return stem;
            ConsoleLogger.Initialize();
            return ConsoleLogger.SessionStem;
        }

        static string DataRoot()
        {
            var root = ConsoleLogger.BaseDirectory;
            if (!string.IsNullOrEmpty(root)) return root;
            ConsoleLogger.Initialize();
            return ConsoleLogger.BaseDirectory;
        }

        static string ProjectRoot()
        {
            try { return Path.GetDirectoryName(Application.dataPath); }
            catch { return null; }
        }

        static string SessionDirectory(string stem)
        {
            var root = DataRoot();
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(stem)) return "";
            return Path.Combine(root, FolderName, stem);
        }

        static ArchiveIndex LoadIndex(string stem)
        {
            var path = IndexPath(stem);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return new ArchiveIndex();
            try
            {
                var index = JsonConvert.DeserializeObject<ArchiveIndex>(File.ReadAllText(path));
                if (index == null) return new ArchiveIndex();
                if (index.files == null) index.files = new List<ArchiveEntry>();
                return index;
            }
            catch
            {
                return new ArchiveIndex();
            }
        }

        static void SaveIndex(string stem, ArchiveIndex index)
        {
            var dir = SessionDirectory(stem);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(IndexPath(stem), JsonConvert.SerializeObject(index, Formatting.Indented), Encoding.UTF8);
        }

        static void MergeIndex(string oldStem, string newStem)
        {
            var incoming = LoadIndex(oldStem);
            var dest = LoadIndex(newStem);
            foreach (var entry in incoming.files)
            {
                if (entry == null) continue;
                if (HasSource(dest, entry.archive, entry.source)) continue;
                dest.files.Add(entry);
            }
            SaveIndex(newStem, dest);
        }

        static string IndexPath(string stem)
        {
            var dir = SessionDirectory(stem);
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, IndexFileName);
        }

        static byte[] DecodeDataUrl(string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return null;
            var comma = dataUrl.IndexOf(',');
            if (comma < 0 || comma + 1 >= dataUrl.Length) return null;
            try { return Convert.FromBase64String(dataUrl.Substring(comma + 1)); }
            catch { return null; }
        }

        static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? "" : path.Replace('\\', '/');
        }

        static bool TryGetPngSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null || bytes.Length < 24) return false;
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;
            width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return width > 0 && height > 0;
        }
    }
}

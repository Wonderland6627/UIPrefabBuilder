using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        public int SchemaVersion = 2;
        /// <summary>Short index of the conversation. The full text stays in Messages.</summary>
        public List<SerializedOutline> Outline = new List<SerializedOutline>();
        public List<SerializedMessage> Messages = new List<SerializedMessage>();
        public List<SerializedBubble> Bubbles = new List<SerializedBubble>();
        public List<SerializedAssetChange> ChangedAssets = new List<SerializedAssetChange>();
        /// <summary>Measured mockup regions, so a resumed task can still check them.</summary>
        public SerializedDesignMeasurements DesignMeasurements;
    }

    [Serializable]
    public class SerializedDesignMeasurements
    {
        public float Scale;
        public float CanvasWidth;
        public float CanvasHeight;
        public float ImageWidth;
        public float ImageHeight;
        public List<SerializedMappedRect> Rects = new List<SerializedMappedRect>();
    }

    [Serializable]
    public class SerializedMappedRect
    {
        public string Label;
        public float X;
        public float Y;
        public float W;
        public float H;
        public float AnchoredX;
        public float AnchoredY;
        public float SizeW;
        public float SizeH;
    }

    [Serializable]
    public class SerializedOutline
    {
        public int Index;
        public string MessageId;
        public string StepId;
        public string Role;
        public string ToolName;
        public List<string> ToolCalls;
        public int ContentChars;
        public int ThinkingChars;
        public int ModelChars;
    }

    [Serializable]
    public class SerializedMessage
    {
        public string MessageId;
        public string TaskId;
        public string StepId;
        public string Role;
        public string Content;
        public string ModelContent;
        public string ToolCallId;
        public string ToolName;
        public string Thinking;
        public bool ReasoningFieldPresent;
        public string ContinuityJson;
        public int PromptTokens;
        public int CompletionTokens;
        public int ReasoningTokens;
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float ThinkingSeconds = -1f;
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float ContentSeconds = -1f;
        [DefaultValue(-1f)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public float ToolSeconds = -1f;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool Injected;
        public List<SerializedToolCall> ToolCalls;
        public List<SerializedImageRef> ImageRefs;
    }

    public class SerializedToolCall
    {
        public string Id;
        public string Name;
        public string Arguments;
        public string ContinuityJson;
    }

    public class SerializedImageRef
    {
        public string SourcePath;
        public long ByteLength;
        public string Sha256;
        public string Detail;
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
        private List<ChatSession> _sessionListCache;
        private bool _sessionListDirty = true;

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
            var messages = history.Messages.Select(ToSerializedMessage).ToList();
            var session = new ChatSession
            {
                SessionId = _currentId,
                Title = ResolveSavedTitle(previous),
                TitleLocked = previous != null && previous.TitleLocked,
                StorageName = previous?.StorageName,
                CreatedAt = previous?.CreatedAt ?? DateTime.Now,
                UpdatedAt = DateTime.Now,
                SchemaVersion = 2,
                Outline = BuildOutline(messages),
                Messages = messages,
                Bubbles = SelectUnrebuiltBubbles(bubbles),
                ChangedAssets = SessionAssetChanges.Snapshot().Select(a => new SerializedAssetChange
                {
                    Path = a.Path,
                    Kind = a.Kind.ToString(),
                    Ticks = a.Ticks
                }).ToList(),
                DesignMeasurements = CaptureDesignMeasurements()
            };
            WriteSession(session, previousStem);
            var desired = session.StorageName;
            SessionImageArchive.RewriteHistory(history, previous?.StorageName, desired);
            SessionImageArchive.RewriteHistory(history, previousStem, desired);
            SessionImageArchive.RewriteHistory(history, _currentId, desired);
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
                var restored = new List<ChatMessage>();
                if (session.Messages != null)
                {
                    foreach (var m in session.Messages)
                        restored.Add(FromSerializedMessage(m));
                }
                history.RestoreMessages(restored);
                if (restored.Exists(m => !string.IsNullOrEmpty(m.MessageId)))
                {
                    RebuildBubbles(restored, bubbles);
                    AppendStoredBubbles(session.Bubbles, bubbles);
                }
                else if (session.Bubbles != null)
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
                SessionImageArchive.RestoreDesignContext(session.StorageName);
                ApplyDesignMeasurements(session.DesignMeasurements);
            }
            catch (Exception e) { Debug.LogWarning("[UIPrefabBuilder] Failed to load session: " + e.Message); }
        }

        public List<ChatSession> ListSessions()
        {
            if (!_sessionListDirty && _sessionListCache != null)
                return _sessionListCache;

            var result = new List<ChatSession>();
            if (!Directory.Exists(SessionDir))
            {
                _sessionListCache = result;
                _sessionListDirty = false;
                return _sessionListCache;
            }
            foreach (var f in Directory.GetFiles(SessionDir, "*.json"))
            {
                try
                {
                    var s = JsonConvert.DeserializeObject<ChatSession>(File.ReadAllText(f));
                    if (s != null) result.Add(s);
                }
                catch { }
            }
            _sessionListCache = result.OrderByDescending(s => s.UpdatedAt).ToList();
            _sessionListDirty = false;
            return _sessionListCache;
        }

        public void DeleteSession(string id)
        {
            _sessionListDirty = true;
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
            SessionImageArchive.Delete(stem);
            if (!string.Equals(stem, id, StringComparison.OrdinalIgnoreCase))
                SessionImageArchive.Delete(id);
            if (id == _currentId)
                ConsoleLogger.RetargetLog(id);
        }

        private static SerializedDesignMeasurements CaptureDesignMeasurements()
        {
            var rects = DesignImageContext.CopyMappedRects();
            if (rects.Count == 0) return null;
            return new SerializedDesignMeasurements
            {
                Scale = DesignImageContext.LastScale,
                CanvasWidth = DesignImageContext.LastCanvasWidth,
                CanvasHeight = DesignImageContext.LastCanvasHeight,
                ImageWidth = DesignImageContext.LastImageWidth,
                ImageHeight = DesignImageContext.LastImageHeight,
                Rects = rects.Select(rect => new SerializedMappedRect
                {
                    Label = rect.Label,
                    X = rect.X,
                    Y = rect.Y,
                    W = rect.W,
                    H = rect.H,
                    AnchoredX = rect.AnchoredX,
                    AnchoredY = rect.AnchoredY,
                    SizeW = rect.SizeW,
                    SizeH = rect.SizeH
                }).ToList()
            };
        }

        private static void ApplyDesignMeasurements(SerializedDesignMeasurements saved)
        {
            if (saved?.Rects == null || saved.Rects.Count == 0)
            {
                DesignImageContext.ReplaceMeasurements(0, 0, 0, 0, 0, null);
                return;
            }

            var rects = saved.Rects.Select(rect => new DesignImageContext.MappedRect
            {
                Label = rect.Label,
                X = rect.X,
                Y = rect.Y,
                W = rect.W,
                H = rect.H,
                AnchoredX = rect.AnchoredX,
                AnchoredY = rect.AnchoredY,
                SizeW = rect.SizeW,
                SizeH = rect.SizeH
            }).ToList();
            DesignImageContext.ReplaceMeasurements(
                saved.Scale, saved.CanvasWidth, saved.CanvasHeight, saved.ImageWidth, saved.ImageHeight, rects);
            ConsoleLogger.Log($"[DesignImage] Restored {rects.Count} measured region(s).");
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

        static List<SerializedOutline> BuildOutline(List<SerializedMessage> messages)
        {
            var outline = new List<SerializedOutline>();
            if (messages == null) return outline;
            for (int i = 0; i < messages.Count; i++)
            {
                var message = messages[i];
                if (message == null) continue;
                outline.Add(new SerializedOutline
                {
                    Index = i,
                    MessageId = message.MessageId,
                    StepId = message.StepId,
                    Role = message.Role,
                    ToolName = message.ToolName,
                    ToolCalls = message.ToolCalls?.Select(call => call.Name).ToList(),
                    ContentChars = message.Content?.Length ?? 0,
                    ThinkingChars = message.Thinking?.Length ?? 0,
                    ModelChars = message.ModelContent?.Length ?? 0
                });
            }
            return outline;
        }

        static List<SerializedBubble> SelectUnrebuiltBubbles(List<ChatBubble> bubbles)
        {
            if (bubbles == null) return new List<SerializedBubble>();
            return bubbles
                .Where(b => b != null && b.Type == BubbleType.Error)
                .Select(b => new SerializedBubble { Type = b.Type.ToString(), Content = b.Content })
                .ToList();
        }

        static void RebuildBubbles(List<ChatMessage> messages, List<ChatBubble> bubbles)
        {
            var results = new Dictionary<string, ChatMessage>();
            foreach (var message in messages)
            {
                if (message == null || message.Role != ChatRole.Tool || string.IsNullOrEmpty(message.ToolCallId)) continue;
                results[message.ToolCallId] = message;
            }

            foreach (var message in messages)
            {
                if (message == null || message.Role == ChatRole.System || message.Role == ChatRole.Tool) continue;
                if (message.Role == ChatRole.User)
                {
                    var text = message.ArchiveContent ?? message.Content ?? "";
                    var files = message.ImageArchives?
                        .Select(image => image.SourcePath)
                        .Where(path => !string.IsNullOrEmpty(path))
                        .ToList();
                    if (string.IsNullOrWhiteSpace(text) && (files == null || files.Count == 0)) continue;
                    AppendUserTurn(bubbles, text, files, message.Injected, message.ContentSeconds);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(message.Thinking))
                    bubbles.Add(new ChatBubble { Type = BubbleType.Thinking, Content = message.Thinking, DurationSeconds = message.ThinkingSeconds });
                if (message.ToolCalls != null)
                {
                    foreach (var call in message.ToolCalls)
                    {
                        results.TryGetValue(call.Id ?? "", out var result);
                        var body = result?.ArchiveContent ?? result?.Content;
                        var presented = ToolCallPresenter.Present(call.Name, call.Arguments, body);
                        bubbles.Add(new ChatBubble
                        {
                            Type = presented.Type,
                            Title = presented.Title,
                            Content = presented.Content,
                            Note = presented.Note,
                            DurationSeconds = result?.ToolSeconds ?? -1f
                        });
                    }
                }
                if (!string.IsNullOrWhiteSpace(message.Content))
                    bubbles.Add(new ChatBubble { Type = BubbleType.AI, Content = message.ArchiveContent ?? message.Content, DurationSeconds = message.ContentSeconds });
            }
        }

        static void AppendStoredBubbles(List<SerializedBubble> stored, List<ChatBubble> bubbles)
        {
            if (stored == null) return;
            foreach (var bubble in stored)
            {
                if (bubble == null || !Enum.TryParse<BubbleType>(bubble.Type, out var type) || type != BubbleType.Error) continue;
                bubbles.Add(new ChatBubble { Type = type, Content = bubble.Content });
            }
        }

        static void AppendUserTurn(List<ChatBubble> bubbles, string text, List<string> files, bool injected, float durationSeconds)
        {
            if (TrySplitDesignMeta(text, out var userText, out var meta))
            {
                if (!string.IsNullOrWhiteSpace(userText) || (files != null && files.Count > 0))
                    bubbles.Add(new ChatBubble { Type = BubbleType.User, Content = userText, AttachedFilePaths = files });
                if (!string.IsNullOrWhiteSpace(meta))
                    bubbles.Add(new ChatBubble { Type = BubbleType.Directive, Content = meta, DurationSeconds = durationSeconds });
                return;
            }

            var type = injected || IsStandaloneDirective(text) ? BubbleType.Directive : BubbleType.User;
            var bubble = new ChatBubble { Type = type, Content = text, AttachedFilePaths = files };
            if (type == BubbleType.Directive)
                bubble.DurationSeconds = durationSeconds;
            bubbles.Add(bubble);
        }

        static bool TrySplitDesignMeta(string text, out string userText, out string meta)
        {
            userText = text;
            meta = null;
            if (string.IsNullOrEmpty(text)) return false;
            const string marker = "[DesignImageMeta]";
            var index = text.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0) return false;
            userText = text.Substring(0, index).Trim();
            meta = text.Substring(index).Trim();
            return meta.Length > 0;
        }

        static bool IsStandaloneDirective(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var trimmed = text.TrimStart();
            string[] prefixes =
            {
                "You are making many individual search calls.",
                "You have been searching for a while",
                "You have spent many steps searching.",
                "You have enough information to start building.",
                "STOP using execute_code",
                "Your last message was EMPTY",
                "VISUAL VERIFICATION REQUIRED",
                "VISUAL VERIFICATION INCOMPLETE",
                "Your own verdict says",
                "Stop building:",
                "The user confirmed.",
                "The user refused editing",
                "Please analyze the provided screenshot",
                "Compare the current UI build",
                "Analyze this UI screenshot",
                "Cropped region (",
                "[Execution Result]"
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (trimmed.StartsWith(prefixes[i], StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static SerializedMessage ToSerializedMessage(ChatMessage message)
        {
            var full = message.ArchiveContent ?? message.Content ?? "";
            var model = message.Content ?? "";
            var serialized = new SerializedMessage
            {
                MessageId = message.MessageId,
                TaskId = message.TaskId,
                StepId = message.StepId,
                Role = message.Role.ToString(),
                Content = full,
                ModelContent = model == full ? null : model,
                ToolCallId = message.ToolCallId,
                ToolName = message.ToolName,
                Thinking = message.Thinking,
                ReasoningFieldPresent = message.ReasoningFieldPresent,
                ContinuityJson = message.ContinuityJson,
                PromptTokens = message.PromptTokens,
                CompletionTokens = message.CompletionTokens,
                ReasoningTokens = message.ReasoningTokens,
                ThinkingSeconds = message.ThinkingSeconds,
                ContentSeconds = message.ContentSeconds,
                ToolSeconds = message.ToolSeconds,
                Injected = message.Injected,
                ImageRefs = CollectImageRefs(message)
            };
            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                serialized.ToolCalls = message.ToolCalls.Select(call => new SerializedToolCall
                {
                    Id = call.Id,
                    Name = call.Name,
                    Arguments = call.Arguments,
                    ContinuityJson = call.ContinuityJson
                }).ToList();
            }
            return serialized;
        }

        static ChatMessage FromSerializedMessage(SerializedMessage message)
        {
            var role = ChatRole.User;
            if (!string.IsNullOrEmpty(message.Role))
                Enum.TryParse(message.Role, out role);
            var full = message.Content ?? "";
            var model = string.IsNullOrEmpty(message.ModelContent) ? full : message.ModelContent;
            var restored = new ChatMessage
            {
                Role = role,
                Content = model,
                ArchiveContent = full,
                MessageId = message.MessageId,
                TaskId = message.TaskId,
                StepId = message.StepId,
                ToolCallId = message.ToolCallId,
                ToolName = message.ToolName,
                Thinking = message.Thinking,
                ReasoningFieldPresent = message.ReasoningFieldPresent,
                ContinuityJson = message.ContinuityJson,
                PromptTokens = message.PromptTokens,
                CompletionTokens = message.CompletionTokens,
                ReasoningTokens = message.ReasoningTokens,
                ThinkingSeconds = message.ThinkingSeconds,
                ContentSeconds = message.ContentSeconds,
                ToolSeconds = message.ToolSeconds,
                Injected = message.Injected,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            if (message.ToolCalls != null)
            {
                restored.ToolCalls = message.ToolCalls.Select(call => new ToolCallInfo
                {
                    Id = call.Id,
                    Name = call.Name,
                    Arguments = call.Arguments,
                    ContinuityJson = call.ContinuityJson
                }).ToList();
            }
            restored.ImageArchives = message.ImageRefs?.Select(image => new ImageArchiveRef
            {
                SourcePath = image.SourcePath,
                ByteLength = image.ByteLength,
                Sha256 = image.Sha256,
                Detail = image.Detail
            }).ToList();
            restored.ContentParts = RehydrateImages(message, model);
            return restored;
        }

        static List<SerializedImageRef> CollectImageRefs(ChatMessage message)
        {
            var refs = new List<SerializedImageRef>();
            if (message.ContentParts != null)
            {
                foreach (var part in message.ContentParts)
                {
                    if (part == null || part.Type != "image_url") continue;
                    refs.Add(DescribeImage(part));
                }
            }
            if (refs.Count == 0 && message.ImageArchives != null)
            {
                foreach (var image in message.ImageArchives)
                {
                    refs.Add(new SerializedImageRef
                    {
                        SourcePath = image.SourcePath,
                        ByteLength = image.ByteLength,
                        Sha256 = image.Sha256,
                        Detail = image.Detail
                    });
                }
            }
            return refs.Count == 0 ? null : refs;
        }

        static SerializedImageRef DescribeImage(ContentPart part)
        {
            var described = new SerializedImageRef
            {
                SourcePath = part.SourcePath,
                Detail = part.ImageUrl?.Detail
            };
            var url = part.ImageUrl?.Url;
            if (!string.IsNullOrEmpty(url) && url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = url.IndexOf(',');
                var payload = comma >= 0 ? url.Substring(comma + 1) : "";
                try
                {
                    var bytes = Convert.FromBase64String(payload);
                    described.ByteLength = bytes.Length;
                    described.Sha256 = HashBytes(bytes);
                }
                catch { }
            }
            else
            {
                var fullPath = SessionImageArchive.Resolve(part.SourcePath);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    var bytes = File.ReadAllBytes(fullPath);
                    described.ByteLength = bytes.Length;
                    described.Sha256 = HashBytes(bytes);
                }
            }
            return described;
        }

        static List<ContentPart> RehydrateImages(SerializedMessage message, string text)
        {
            if (message.ImageRefs == null || message.ImageRefs.Count == 0) return null;
            var parts = new List<ContentPart>();
            if (!string.IsNullOrEmpty(text))
                parts.Add(new ContentPart { Type = "text", Text = text });
            foreach (var image in message.ImageRefs)
            {
                var part = new ContentPart
                {
                    Type = "image_url",
                    SourcePath = image.SourcePath,
                    ImageUrl = new ImageUrl { Detail = image.Detail ?? "low" }
                };
                var fullPath = SessionImageArchive.Resolve(image.SourcePath);
                if (!string.IsNullOrEmpty(fullPath))
                {
                    var bytes = File.ReadAllBytes(fullPath);
                    var mime = fullPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                        || fullPath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                        ? "image/jpeg" : "image/png";
                    part.ImageUrl.Url = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
                    parts.Add(part);
                    continue;
                }
                parts.Add(new ContentPart
                {
                    Type = "text",
                    Text = "[Image unavailable: " + (image.SourcePath ?? "unknown") + " sha256=" + (image.Sha256 ?? "") + "]"
                });
            }
            return parts;
        }

        static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
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
                    case BubbleType.Directive:
                        sb.AppendLine("## Agent Directive");
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
                        sb.AppendLine("## " + (string.IsNullOrEmpty(b.Title) ? "Code" : b.Title));
                        sb.AppendLine("```csharp");
                        sb.AppendLine(b.Content);
                        sb.AppendLine("```");
                        if (!string.IsNullOrEmpty(b.Note))
                        {
                            sb.AppendLine();
                            sb.AppendLine(ToolCallPresenter.ToPlain(b.Note));
                        }
                        sb.AppendLine();
                        break;
                    case BubbleType.ToolCall:
                        sb.AppendLine("## Tool Call");
                        var toolLines = ToolCallPresenter.ToPlain(b.Content).Split('\n');
                        for (int li = 0; li < toolLines.Length; li++)
                            sb.AppendLine("> " + toolLines[li]);
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
            _sessionListDirty = true;
            if (session == null || string.IsNullOrEmpty(session.SessionId)) return;
            Directory.CreateDirectory(SessionDir);
            var desired = ResolveUniqueStem(MakeStorageName(session.Title, session.SessionId), session.SessionId);
            var previousLogStem = FindLogStem(session.StorageName, previousFileStem, session.SessionId);
            var previousAttachmentStem = session.StorageName;
            session.StorageName = desired;
            // Only this session's own folder names. The logger stem can still be the
            // previously open chat when a new session is created, and moving that
            // folder would merge two chats into one attachment directory.
            SessionImageArchive.Move(previousAttachmentStem, desired);
            SessionImageArchive.Move(previousFileStem, desired);
            SessionImageArchive.Move(session.SessionId, desired);
            RewriteImageRefs(session, previousAttachmentStem, desired);
            RewriteImageRefs(session, previousFileStem, desired);
            RewriteImageRefs(session, session.SessionId, desired);
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

        static void RewriteImageRefs(ChatSession session, string oldStem, string newStem)
        {
            if (session?.Messages == null) return;
            if (string.IsNullOrEmpty(oldStem) || string.Equals(oldStem, newStem, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var message in session.Messages)
            {
                if (message?.ImageRefs == null) continue;
                foreach (var image in message.ImageRefs)
                {
                    if (image == null) continue;
                    image.SourcePath = SessionImageArchive.RewritePrefix(image.SourcePath, oldStem, newStem);
                }
            }
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

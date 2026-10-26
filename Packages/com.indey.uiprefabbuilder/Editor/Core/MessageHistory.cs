using System;
using System.Collections.Generic;
using System.Linq;

namespace Indey.UIPrefabBuilder.Core
{
    public enum ChatRole { System, User, Assistant, Tool }

    [Serializable]
    public class ContentPart
    {
        public string Type;      // "text" or "image_url"
        public string Text;      // when Type == "text"
        public ImageUrl ImageUrl; // when Type == "image_url"
        public string SourcePath;
    }

    public class ImageArchiveRef
    {
        public string SourcePath;
        public long ByteLength;
        public string Sha256;
        public string Detail;
    }

    [Serializable]
    public class ImageUrl
    {
        public string Url;    // "data:image/png;base64,..." or http URL
        public string Detail;  // "low" | "high" | "auto"
    }

    [Serializable]
    public class ChatMessage
    {
        public ChatRole Role;
        public string Content;
        public List<ContentPart> ContentParts;
        public long Timestamp;
        public string MessageId;
        public string TaskId;
        public string StepId;
        public string ToolCallId;
        public string ToolName;
        public List<ToolCallInfo> ToolCalls;
        public string Thinking;
        public bool ReasoningFieldPresent;
        public string ContinuityJson;
        /// <summary>Full tool result or message text kept for the session file and the audit log.</summary>
        public string ArchiveContent;
        public int PromptTokens;
        public int CompletionTokens;
        public int ReasoningTokens;
        public List<ImageArchiveRef> ImageArchives;
        /// <summary>
        /// When true, images in this message are degraded to low-resolution
        /// instead of being replaced with text placeholders.
        /// Used for design mockup images that need to remain visible throughout the session.
        /// </summary>
        public bool PinImage;

        public bool HasMultimodalContent => ContentParts != null && ContentParts.Count > 0;

        public ChatMessage() { }

        public ChatMessage(ChatRole role, string content)
        {
            Role = role;
            Content = content;
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
    }

    public class MessageHistory
    {
        private readonly List<ChatMessage> _messages = new List<ChatMessage>();
        private string _taskId;
        private string _stepId;
        private const int MaxMessages = 80;
        private const int SoftLimit = 50;
        private const int MaxToolResultChars = 2000;
        private const int ProtectRecentMessages = 18;
        private const int ImageDegradeAfterMessages = 16;
        private bool _imagesDegraded;

        public IReadOnlyList<ChatMessage> Messages => _messages;
        public int Count => _messages.Count;
        public string CurrentTaskId => _taskId;
        public string CurrentStepId => _stepId;
        public string ResumeBlockedReason { get; private set; }

        public void BeginTask(string taskId) => _taskId = taskId;

        public void SetStep(string stepId) => _stepId = stepId;

        public void Clear()
        {
            _messages.Clear();
            _imagesDegraded = false;
            ResumeBlockedReason = null;
            _taskId = null;
            _stepId = null;
        }

        public void RestoreMessages(List<ChatMessage> messages)
        {
            _messages.Clear();
            _imagesDegraded = false;
            if (messages != null)
                _messages.AddRange(messages);
            ResumeBlockedReason = EvaluateResume(_messages);
            var lastTask = _messages.LastOrDefault(m => !string.IsNullOrEmpty(m.TaskId));
            _taskId = lastTask?.TaskId;
        }

        public static string EvaluateResume(IReadOnlyList<ChatMessage> messages)
        {
            if (messages == null) return null;
            var ids = new HashSet<string>();
            foreach (var message in messages)
            {
                if (message.ToolCalls == null) continue;
                foreach (var call in message.ToolCalls)
                {
                    if (!string.IsNullOrEmpty(call.Id))
                        ids.Add(call.Id);
                }
            }

            foreach (var message in messages)
            {
                if (message.Role != ChatRole.Tool) continue;
                if (string.IsNullOrEmpty(message.ToolCallId) || !ids.Contains(message.ToolCallId))
                    return "这份会话缺少与工具结果对应的调用参数，只能查看，不能继续任务。";
            }
            return null;
        }

        public void SetSystemPrompt(string prompt)
        {
            if (_messages.Count > 0 && _messages[0].Role == ChatRole.System)
            {
                _messages[0].Content = prompt;
                _messages[0].ArchiveContent = prompt;
                Stamp(_messages[0]);
                return;
            }
            var msg = new ChatMessage(ChatRole.System, prompt) { ArchiveContent = prompt };
            Stamp(msg);
            _messages.Insert(0, msg);
        }

        public ChatMessage AddUser(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return null;
            var msg = new ChatMessage(ChatRole.User, content);
            msg.ArchiveContent = content;
            Stamp(msg);
            _messages.Add(msg);
            Trim();
            return msg;
        }

        public void AddUserMultimodal(List<ContentPart> parts, bool pinImage = false)
        {
            if (parts == null || parts.Count == 0) return;
            var textContent = parts.FirstOrDefault(p => p.Type == "text")?.Text ?? "";
            _messages.Add(new ChatMessage
            {
                Role = ChatRole.User,
                Content = textContent,
                ContentParts = parts,
                PinImage = pinImage,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Stamp(_messages[_messages.Count - 1]);
            _messages[_messages.Count - 1].ArchiveContent = textContent;
            Trim();
        }

        public ChatMessage AddAssistant(string content)
        {
            return AddAssistant(content, null);
        }

        public ChatMessage AddAssistant(string content, LLMResponse response)
        {
            if (response == null && string.IsNullOrWhiteSpace(content))
                return null;
            var msg = new ChatMessage(ChatRole.Assistant, content ?? "");
            msg.ArchiveContent = content ?? "";
            CopyTrace(msg, response);
            Stamp(msg);
            _messages.Add(msg);
            Trim();
            return msg;
        }

        public ChatMessage AddAssistantWithToolCalls(LLMResponse response)
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = response.Content ?? "",
                ArchiveContent = response.Content ?? "",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ToolCalls = response.ToolCalls
            };
            CopyTrace(msg, response);
            Stamp(msg);
            _messages.Add(msg);
            Trim();
            return msg;
        }

        public ChatMessage AddToolResult(string toolCallId, string toolName, string result)
        {
            var full = result ?? "";
            var truncated = TruncateToolResult(full, MaxToolResultChars);
            var msg = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = truncated,
                ArchiveContent = full,
                ToolCallId = toolCallId,
                ToolName = toolName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            Stamp(msg);
            _messages.Add(msg);
            CompressIfNeeded();
            Trim();
            return msg;
        }

        public void AddToolResultMultimodal(string toolCallId, string toolName, string textContent, List<ContentPart> parts)
        {
            _messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = textContent ?? "",
                ContentParts = parts,
                ToolCallId = toolCallId,
                ToolName = toolName,
                ArchiveContent = textContent ?? "",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Stamp(_messages[_messages.Count - 1]);
            CompressIfNeeded();
            Trim();
        }

        public void AddToolResult(string content)
        {
            var text = "[Execution Result]\n" + content;
            var msg = new ChatMessage(ChatRole.User, text) { ArchiveContent = text };
            Stamp(msg);
            _messages.Add(msg);
            Trim();
        }

        private void Stamp(ChatMessage msg)
        {
            if (msg == null) return;
            if (string.IsNullOrEmpty(msg.MessageId))
                msg.MessageId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(msg.TaskId))
                msg.TaskId = _taskId;
            if (string.IsNullOrEmpty(msg.StepId))
                msg.StepId = _stepId;
        }

        private static void CopyTrace(ChatMessage msg, LLMResponse response)
        {
            if (response == null) return;
            msg.Thinking = response.Thinking;
            msg.ReasoningFieldPresent = response.ReasoningFieldPresent;
            msg.ContinuityJson = response.ContinuityJson;
            msg.PromptTokens = response.PromptTokens;
            msg.CompletionTokens = response.CompletionTokens;
            msg.ReasoningTokens = response.ReasoningTokens;
        }

        /// <summary>
        /// Replace base64 image data with a text placeholder in old messages
        /// to prevent unbounded payload growth. Called automatically when
        /// message count exceeds ImageDegradeAfterMessages.
        /// </summary>
        private void DegradeOldImages()
        {
            if (_imagesDegraded) return;
            if (_messages.Count < ImageDegradeAfterMessages) return;

            _imagesDegraded = true;
            int degraded = 0;
            int downscaled = 0;

            int protectFrom = Math.Max(0, _messages.Count - ProtectRecentMessages);
            for (int i = 0; i < protectFrom; i++)
            {
                var msg = _messages[i];
                if (!msg.HasMultimodalContent) continue;

                if (msg.PinImage)
                {
                    foreach (var part in msg.ContentParts)
                    {
                        if (part.Type != "image_url" || part.ImageUrl?.Url == null
                            || !part.ImageUrl.Url.StartsWith("data:"))
                            continue;
                        if (part.ImageUrl.Detail == "low") continue;
                        part.ImageUrl.Detail = "low";
                        downscaled++;
                    }
                    continue;
                }

                var newParts = new List<ContentPart>();
                foreach (var part in msg.ContentParts)
                {
                    if (part.Type == "image_url" && part.ImageUrl?.Url != null
                        && part.ImageUrl.Url.StartsWith("data:"))
                    {
                        newParts.Add(new ContentPart
                        {
                            Type = "text",
                            Text = "[Image was provided earlier and has been analyzed. Refer to your earlier analysis for design details.]"
                        });
                        degraded++;
                    }
                    else
                    {
                        newParts.Add(part);
                    }
                }
                msg.ContentParts = newParts;
            }

            if (degraded > 0 || downscaled > 0)
                Logging.ConsoleLogger.Log($"[MessageHistory] Degraded {degraded} image(s) to text, downscaled {downscaled} pinned image(s) to low detail.");
        }

        #region Context Compression

        private static string TruncateToolResult(string result, int maxChars)
        {
            if (result.Length <= maxChars) return result;

            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(result);
                StripLargeStringFields(obj);

                var compactVisualResult = CompactVisualSearchResult(obj, maxChars);
                if (compactVisualResult != null)
                    return compactVisualResult;

                TrimJsonArray(obj, "assets", 20);
                TrimJsonArray(obj, "elements", 20);
                TrimJsonArray(obj, "names", 200);

                var hierarchy = obj["hierarchy"]?.ToString();
                if (hierarchy != null && hierarchy.Length > maxChars / 2)
                    obj["hierarchy"] = hierarchy.Substring(0, maxChars / 2) + "\n... (truncated)";

                var json = obj.ToString(Newtonsoft.Json.Formatting.None);
                if (json.Length <= maxChars) return json;

                var preserved = PreserveStructuredPayload(obj);
                if (preserved != null) return preserved;

                return SummarizeOversized(obj);
            }
            catch
            {
                return "{\"_truncated\":true,\"message\":\"Tool result was not valid JSON and was too long to keep.\"}";
            }
        }

        /// <summary>
        /// Drops base64 and other oversized string fields so a later compact step still emits valid JSON.
        /// A raw Substring cut used to hide filePath and every region after the first few.
        /// </summary>
        private static void StripLargeStringFields(Newtonsoft.Json.Linq.JObject obj)
        {
            var drop = new List<string>();
            foreach (var prop in obj.Properties())
            {
                if (prop.Value.Type != Newtonsoft.Json.Linq.JTokenType.String) continue;
                var text = prop.Value.ToString();
                if (text.Length < 400) continue;
                var name = prop.Name;
                var looksEncoded = name.IndexOf("base64", StringComparison.OrdinalIgnoreCase) >= 0
                    || name == "_images"
                    || name == "imageBase64"
                    || (text.Length > 1500 && text.IndexOf(' ') < 0 && text.IndexOf('\n') < 0);
                if (looksEncoded) drop.Add(name);
            }
            if (drop.Count == 0) return;
            foreach (var name in drop) obj.Remove(name);
            obj["_stripped"] = "Removed oversized field(s): " + string.Join(", ", drop);
        }

        private static void TrimJsonArray(Newtonsoft.Json.Linq.JObject obj, string name, int keep)
        {
            var arr = obj[name] as Newtonsoft.Json.Linq.JArray;
            if (arr == null || arr.Count <= keep) return;
            int totalCount = arr.Count;
            var trimmed = new Newtonsoft.Json.Linq.JArray();
            for (int i = 0; i < keep; i++) trimmed.Add(arr[i]);
            obj[name] = trimmed;
            obj["_truncated"] = $"Showing {keep} of {totalCount} {name}";
            if (obj["count"] != null && name != "names") obj["count"] = totalCount;
            if (obj["total"] == null) obj["total"] = totalCount;
        }

        /// <summary>
        /// Directory name pages and mapped rects are the payload. Dropping them left the model
        /// with a summary and no coordinates, which made it call the same tool again.
        /// </summary>
        private static string PreserveStructuredPayload(Newtonsoft.Json.Linq.JObject obj)
        {
            if (obj["names"] is Newtonsoft.Json.Linq.JArray names && names.Count > 0)
            {
                var listing = new Newtonsoft.Json.Linq.JObject();
                CopyJsonField(listing, obj, "success");
                CopyJsonField(listing, obj, "count");
                CopyJsonField(listing, obj, "total");
                CopyJsonField(listing, obj, "offset");
                CopyJsonField(listing, obj, "nextOffset");
                CopyJsonField(listing, obj, "directory");
                CopyJsonField(listing, obj, "_hint");
                listing["names"] = names;
                return listing.ToString(Newtonsoft.Json.Formatting.None);
            }

            if (obj["results"] is Newtonsoft.Json.Linq.JArray results && HasMappedRects(results))
            {
                var mapped = new Newtonsoft.Json.Linq.JObject();
                CopyJsonField(mapped, obj, "success");
                CopyJsonField(mapped, obj, "total");
                CopyJsonField(mapped, obj, "mapped");
                CopyJsonField(mapped, obj, "failed");
                CopyJsonField(mapped, obj, "scale");
                CopyJsonField(mapped, obj, "imagePixelSize");
                CopyJsonField(mapped, obj, "canvasDesignResolution");
                CopyJsonField(mapped, obj, "hint");
                CopyJsonField(mapped, obj, "_hint");
                mapped["results"] = results;
                return mapped.ToString(Newtonsoft.Json.Formatting.None);
            }

            return null;
        }

        private static bool HasMappedRects(Newtonsoft.Json.Linq.JArray results)
        {
            foreach (var token in results)
            {
                if (token is Newtonsoft.Json.Linq.JObject item && item["anchoredPosition"] != null)
                    return true;
            }
            return false;
        }

        private static string SummarizeOversized(Newtonsoft.Json.Linq.JObject obj)
        {
            var summary = new Newtonsoft.Json.Linq.JObject();
            CopyJsonField(summary, obj, "success");
            CopyJsonField(summary, obj, "error");
            CopyJsonField(summary, obj, "message");
            CopyJsonField(summary, obj, "filePath");
            CopyJsonField(summary, obj, "width");
            CopyJsonField(summary, obj, "height");
            CopyJsonField(summary, obj, "captureId");
            CopyJsonField(summary, obj, "validCapture");
            CopyJsonField(summary, obj, "count");
            CopyJsonField(summary, obj, "total");
            CopyJsonField(summary, obj, "offset");
            CopyJsonField(summary, obj, "nextOffset");
            CopyJsonField(summary, obj, "_hint");
            CopyJsonField(summary, obj, "_stripped");
            summary["_truncated"] = "Result exceeded the context limit. Large fields were removed; the fields above are intact.";
            var message = summary["message"]?.ToString();
            if (message != null && message.Length > 500)
                summary["message"] = message.Substring(0, 500);
            return summary.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static void CopyJsonField(Newtonsoft.Json.Linq.JObject dest, Newtonsoft.Json.Linq.JObject source, string name)
        {
            if (source[name] != null) dest[name] = source[name].DeepClone();
        }

        /// <summary>
        /// Keeps every visual-search query/region while retaining only the most useful matches.
        /// This avoids cutting the JSON in the middle and hiding later regions from the model.
        /// </summary>
        private static string CompactVisualSearchResult(Newtonsoft.Json.Linq.JObject source, int maxChars)
        {
            if (!(source["results"] is Newtonsoft.Json.Linq.JArray results)) return null;
            if (!results.Any(IsVisualSearchResultItem)) return null;

            for (int matchesPerItem = 3; matchesPerItem >= 1; matchesPerItem--)
            {
                var compact = new Newtonsoft.Json.Linq.JObject
                {
                    ["success"] = source["success"] ?? true
                };
                if (source["totalRegions"] != null) compact["totalRegions"] = source["totalRegions"];
                if (source["totalQueries"] != null) compact["totalQueries"] = source["totalQueries"];

                var compactResults = new Newtonsoft.Json.Linq.JArray();
                foreach (var token in results)
                {
                    if (!(token is Newtonsoft.Json.Linq.JObject item)) continue;
                    compactResults.Add(CompactVisualSearchItem(item, matchesPerItem));
                }

                compact["results"] = compactResults;
                if (results.OfType<Newtonsoft.Json.Linq.JObject>().Any(
                    item => item["lowConfidence"] != null && (bool)item["lowConfidence"]))
                    compact["_hint"] = "One or more best matches have low confidence; do not apply them as reliable visual matches.";
                compact["_compacted"] = $"Preserved all {compactResults.Count} results with top {matchesPerItem} match(es) each.";
                var json = compact.ToString(Newtonsoft.Json.Formatting.None);
                if (json.Length <= maxChars) return json;
            }

            return null;
        }

        private static bool IsVisualSearchResultItem(Newtonsoft.Json.Linq.JToken token)
        {
            if (!(token is Newtonsoft.Json.Linq.JObject item)) return false;
            return item["matches"] is Newtonsoft.Json.Linq.JArray
                && (item["label"] != null || item["query"] != null);
        }

        private static Newtonsoft.Json.Linq.JObject CompactVisualSearchItem(
            Newtonsoft.Json.Linq.JObject item, int matchesPerItem)
        {
            var compact = new Newtonsoft.Json.Linq.JObject();
            if (item["label"] != null) compact["label"] = item["label"];
            if (item["query"] != null) compact["query"] = item["query"];
            if (item["error"] != null) compact["error"] = item["error"];
            if (item["lowConfidence"] != null) compact["lowConfidence"] = item["lowConfidence"];

            var matches = new Newtonsoft.Json.Linq.JArray();
            if (item["matches"] is Newtonsoft.Json.Linq.JArray sourceMatches)
            {
                foreach (var matchToken in sourceMatches.Take(matchesPerItem))
                {
                    if (!(matchToken is Newtonsoft.Json.Linq.JObject match)) continue;
                    var compactMatch = new Newtonsoft.Json.Linq.JObject();
                    if (match["assetPath"] != null) compactMatch["assetPath"] = match["assetPath"];
                    if (match["score"] != null) compactMatch["score"] = match["score"];
                    if (match["confidence"] != null) compactMatch["confidence"] = match["confidence"];
                    if (match["lowConfidence"] != null) compactMatch["lowConfidence"] = match["lowConfidence"];
                    matches.Add(compactMatch);
                }
            }

            compact["count"] = matches.Count;
            compact["matches"] = matches;
            return compact;
        }

        private void CompressIfNeeded()
        {
            DegradeOldImages();

            if (_messages.Count <= SoftLimit) return;

            int protectFrom = _messages.Count - ProtectRecentMessages;
            for (int i = 2; i < protectFrom; i++)
            {
                if (_messages[i].Role != ChatRole.Tool) continue;
                if (_messages[i].Content.Length <= 200) continue;
                if (_messages[i].HasMultimodalContent) continue;
                if (IsCompactVisualSearchResult(_messages[i].Content)) continue;

                _messages[i].Content = SummarizeToolResult(_messages[i].Content);
                _messages[i].ContentParts = null;

                if (_messages.Count <= SoftLimit) return;
            }
        }

        private static bool IsCompactVisualSearchResult(string result)
        {
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(result);
                if (!(obj["results"] is Newtonsoft.Json.Linq.JArray results)) return false;
                return results.Any(IsVisualSearchResultItem);
            }
            catch
            {
                return false;
            }
        }

        private static string SummarizeToolResult(string result)
        {
            try
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(result);
                var summary = new Newtonsoft.Json.Linq.JObject();
                summary["success"] = obj["success"] ?? true;

                if (obj["count"] != null)
                    summary["summary"] = $"Returned {obj["count"]} items";
                else if (obj["message"] != null)
                    summary["summary"] = obj["message"].ToString().Length > 100
                        ? obj["message"].ToString().Substring(0, 100) + "..."
                        : obj["message"];
                else if (obj["error"] != null)
                    summary["summary"] = "Error: " + obj["error"];
                else
                    summary["summary"] = "(compressed)";

                return summary.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch
            {
                if (result.Length > 100)
                    return "{\"summary\":\"" + result.Substring(0, 80).Replace("\"", "'") + "...\"}";
                return result;
            }
        }

        #endregion

        private void Trim()
        {
            while (_messages.Count > MaxMessages && _messages.Count > 1)
            {
                int removeIdx = FindRemovableGroupStart();
                if (removeIdx < 0) break;
                RemoveMessageGroup(removeIdx);
            }
        }

        /// <summary>
        /// Finds the start index of the earliest removable message group.
        /// Never removes index 0 (system prompt) or index 1 (first user message).
        /// </summary>
        private int FindRemovableGroupStart()
        {
            for (int i = 2; i < _messages.Count; i++)
            {
                var msg = _messages[i];
                if (msg.Role == ChatRole.Tool) continue;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Removes a message at the given index. If it's an assistant message with tool_calls,
        /// also removes all immediately following tool result messages that belong to it.
        /// </summary>
        private void RemoveMessageGroup(int startIdx)
        {
            var msg = _messages[startIdx];
            _messages.RemoveAt(startIdx);

            if (msg.Role != ChatRole.Assistant || msg.ToolCalls == null || msg.ToolCalls.Count == 0)
                return;

            var toolCallIds = new HashSet<string>();
            foreach (var tc in msg.ToolCalls)
            {
                if (!string.IsNullOrEmpty(tc.Id))
                    toolCallIds.Add(tc.Id);
            }

            while (startIdx < _messages.Count
                   && _messages[startIdx].Role == ChatRole.Tool
                   && toolCallIds.Contains(_messages[startIdx].ToolCallId))
            {
                _messages.RemoveAt(startIdx);
            }
        }
    }
}

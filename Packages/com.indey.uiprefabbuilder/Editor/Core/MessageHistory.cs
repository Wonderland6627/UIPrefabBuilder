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
        public string ToolCallId;
        public List<ToolCallInfo> ToolCalls;
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
        private const int MaxMessages = 80;
        private const int SoftLimit = 50;
        private const int MaxToolResultChars = 2000;
        private const int ProtectRecentMessages = 18;
        private const int ImageDegradeAfterMessages = 16;
        private bool _imagesDegraded;

        public IReadOnlyList<ChatMessage> Messages => _messages;
        public int Count => _messages.Count;

        public void Clear()
        {
            _messages.Clear();
            _imagesDegraded = false;
        }

        public void SetSystemPrompt(string prompt)
        {
            if (_messages.Count > 0 && _messages[0].Role == ChatRole.System)
                _messages[0].Content = prompt;
            else
                _messages.Insert(0, new ChatMessage(ChatRole.System, prompt));
        }

        public void AddUser(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            _messages.Add(new ChatMessage(ChatRole.User, content));
            Trim();
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
            Trim();
        }

        public void AddAssistant(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            _messages.Add(new ChatMessage(ChatRole.Assistant, content));
            Trim();
        }

        public void AddAssistantWithToolCalls(LLMResponse response)
        {
            var msg = new ChatMessage
            {
                Role = ChatRole.Assistant,
                Content = response.Content ?? "",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ToolCalls = response.ToolCalls
            };
            _messages.Add(msg);
            Trim();
        }

        public void AddToolResult(string toolCallId, string toolName, string result)
        {
            var truncated = TruncateToolResult(result ?? "", MaxToolResultChars);
            _messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = truncated,
                ToolCallId = toolCallId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            CompressIfNeeded();
            Trim();
        }

        public void AddToolResultMultimodal(string toolCallId, string toolName, string textContent, List<ContentPart> parts)
        {
            _messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = textContent ?? "",
                ContentParts = parts,
                ToolCallId = toolCallId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            CompressIfNeeded();
            Trim();
        }

        public void AddToolResult(string content)
        {
            _messages.Add(new ChatMessage(ChatRole.User, "[Execution Result]\n" + content));
            Trim();
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

                var assetsArr = obj["assets"] as Newtonsoft.Json.Linq.JArray;
                if (assetsArr != null && assetsArr.Count > 20)
                {
                    int totalCount = assetsArr.Count;
                    var trimmed = new Newtonsoft.Json.Linq.JArray();
                    for (int i = 0; i < 20; i++) trimmed.Add(assetsArr[i]);
                    obj["assets"] = trimmed;
                    obj["_truncated"] = $"Showing 20 of {totalCount} results";
                    if (obj["count"] != null) obj["count"] = totalCount;
                    return obj.ToString(Newtonsoft.Json.Formatting.None);
                }

                var elementsArr = obj["elements"] as Newtonsoft.Json.Linq.JArray;
                if (elementsArr != null && elementsArr.Count > 20)
                {
                    int totalCount = elementsArr.Count;
                    var trimmed = new Newtonsoft.Json.Linq.JArray();
                    for (int i = 0; i < 20; i++) trimmed.Add(elementsArr[i]);
                    obj["elements"] = trimmed;
                    obj["_truncated"] = $"Showing 20 of {totalCount} results";
                    return obj.ToString(Newtonsoft.Json.Formatting.None);
                }

                var hierarchy = obj["hierarchy"]?.ToString();
                if (hierarchy != null && hierarchy.Length > maxChars / 2)
                {
                    obj["hierarchy"] = hierarchy.Substring(0, maxChars / 2) + "\n... (truncated)";
                    return obj.ToString(Newtonsoft.Json.Formatting.None);
                }
            }
            catch { }

            return result.Substring(0, maxChars) + "... (truncated)";
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

                _messages[i].Content = SummarizeToolResult(_messages[i].Content);
                _messages[i].ContentParts = null;

                if (_messages.Count <= SoftLimit) return;
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

using System;
using System.Collections.Generic;

namespace Indey.UIPrefabBuilder.Core
{
    public enum ChatRole { System, User, Assistant, Tool }

    [Serializable]
    public class ChatMessage
    {
        public ChatRole Role;
        public string Content;
        public long Timestamp;
        public string ToolCallId;
        public List<ToolCallInfo> ToolCalls;

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

        public IReadOnlyList<ChatMessage> Messages => _messages;
        public int Count => _messages.Count;

        public void Clear() => _messages.Clear();

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
            _messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = result ?? "",
                ToolCallId = toolCallId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
            Trim();
        }

        public void AddToolResult(string content)
        {
            _messages.Add(new ChatMessage(ChatRole.User, "[Execution Result]\n" + content));
            Trim();
        }

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
        /// A "group" is either: a standalone message, or an assistant+tool_results block.
        /// Never removes index 0 (system prompt).
        /// </summary>
        private int FindRemovableGroupStart()
        {
            for (int i = 1; i < _messages.Count; i++)
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

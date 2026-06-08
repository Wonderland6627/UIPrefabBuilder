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
                _messages.RemoveAt(1);
        }
    }
}

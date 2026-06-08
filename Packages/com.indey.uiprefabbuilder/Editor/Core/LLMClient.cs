using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Indey.UIPrefabBuilder.Async;
using Indey.UIPrefabBuilder.Config;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Core
{
    public class ToolCallInfo
    {
        public string Id;
        public string Name;
        public string Arguments;
    }

    public class LLMResponse
    {
        public string Content;
        public string Thinking;
        public List<ToolCallInfo> ToolCalls;
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }

    public class LLMClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly BuilderSettings _settings;
        private string _cachedApiKey;

        public LLMClient(BuilderSettings settings)
        {
            _settings = settings;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds) };
            RefreshApiKey();
        }

        public void RefreshApiKey() => _cachedApiKey = SecureKeyStore.LoadApiKey();

        public void ChatWithToolsAsync(
            IReadOnlyList<ChatMessage> messages,
            JArray tools,
            Action<string> onToken,
            Action<string> onThinkingToken,
            Action<LLMResponse> onComplete,
            Action<Exception> onError,
            CancellationToken ct)
        {
            Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var apiKey = _cachedApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                        throw new InvalidOperationException("API Key not configured. Open Settings to set it.");

                    var body = BuildRequestBody(messages, tools);
                    var url = NormalizeUrl(_settings.BaseUrl);

                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        var err = await resp.Content.ReadAsStringAsync();
                        throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {err}");
                    }

                    var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                    LLMResponse result;
                    if (contentType.Contains("text/event-stream"))
                        result = await ReadSSEWithTools(resp, onToken, onThinkingToken, ct);
                    else
                        result = ParseNonStreamResponse(await resp.Content.ReadAsStringAsync());

                    MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(result));
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { MainThreadDispatcher.Enqueue(() => onError?.Invoke(e)); }
            }, ct);
        }

        #region Legacy streaming (kept for backward compatibility)

        public void StreamChatAsync(
            IReadOnlyList<ChatMessage> messages,
            Action<string> onToken,
            Action<string> onThinkingToken,
            Action<string> onThinkingComplete,
            Action<string> onComplete,
            Action<Exception> onError,
            CancellationToken ct)
        {
            ChatWithToolsAsync(messages, null, onToken, onThinkingToken,
                response =>
                {
                    if (!string.IsNullOrEmpty(response.Thinking))
                        onThinkingComplete?.Invoke(response.Thinking);
                    onComplete?.Invoke(response.Content ?? "");
                },
                onError, ct);
        }

        #endregion

        private string BuildRequestBody(IReadOnlyList<ChatMessage> messages, JArray tools)
        {
            var arr = new JArray();
            foreach (var m in messages)
            {
                var msg = new JObject { ["role"] = m.Role.ToString().ToLowerInvariant() };

                if (!string.IsNullOrEmpty(m.Content))
                    msg["content"] = m.Content;

                if (m.ToolCallId != null)
                {
                    msg["role"] = "tool";
                    msg["tool_call_id"] = m.ToolCallId;
                    msg["content"] = m.Content ?? "";
                }

                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    var calls = new JArray();
                    foreach (var tc in m.ToolCalls)
                    {
                        calls.Add(new JObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = tc.Name,
                                ["arguments"] = tc.Arguments
                            }
                        });
                    }
                    msg["tool_calls"] = calls;
                    if (msg["content"] == null)
                        msg["content"] = "";
                }

                arr.Add(msg);
            }

            var obj = new JObject
            {
                ["model"] = _settings.ModelName,
                ["stream"] = true,
                ["messages"] = arr
            };

            if (tools != null && tools.Count > 0)
                obj["tools"] = tools;

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static async Task<LLMResponse> ReadSSEWithTools(
            HttpResponseMessage resp,
            Action<string> onToken,
            Action<string> onThinkingToken,
            CancellationToken ct)
        {
            var contentBuf = new StringBuilder();
            var thinkingBuf = new StringBuilder();
            var toolCalls = new Dictionary<int, ToolCallInfo>();

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line) || !line.StartsWith("data:")) continue;
                var data = line.Substring(line.IndexOf(':') + 1).Trim();
                if (data == "[DONE]") break;
                try
                {
                    var json = JObject.Parse(data);
                    var choice = json["choices"]?[0];
                    if (choice == null) continue;

                    var delta = choice["delta"];
                    if (delta == null) continue;

                    var content = delta["content"]?.ToString();
                    var reasoning = delta["reasoning_content"]?.ToString();

                    if (!string.IsNullOrEmpty(reasoning))
                    {
                        thinkingBuf.Append(reasoning);
                        var captured = reasoning;
                        MainThreadDispatcher.Enqueue(() => onThinkingToken?.Invoke(captured));
                    }

                    if (!string.IsNullOrEmpty(content))
                    {
                        contentBuf.Append(content);
                        var captured = content;
                        MainThreadDispatcher.Enqueue(() => onToken?.Invoke(captured));
                    }

                    var deltaToolCalls = delta["tool_calls"] as JArray;
                    if (deltaToolCalls != null)
                    {
                        foreach (JObject tc in deltaToolCalls)
                        {
                            var index = (int)(tc["index"] ?? 0);
                            if (!toolCalls.TryGetValue(index, out var info))
                            {
                                info = new ToolCallInfo { Arguments = "" };
                                toolCalls[index] = info;
                            }

                            var id = tc["id"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                                info.Id = id;

                            var fn = tc["function"];
                            if (fn != null)
                            {
                                var fnName = fn["name"]?.ToString();
                                if (!string.IsNullOrEmpty(fnName))
                                    info.Name = fnName;

                                var fnArgs = fn["arguments"]?.ToString();
                                if (!string.IsNullOrEmpty(fnArgs))
                                    info.Arguments += fnArgs;
                            }
                        }
                    }
                }
                catch { }
            }

            var thinkingStr = thinkingBuf.ToString();
            var contentStr = contentBuf.ToString();

            if (string.IsNullOrEmpty(thinkingStr))
            {
                var fallback = ExtractThinking(contentStr);
                if (!string.IsNullOrEmpty(fallback))
                    thinkingStr = fallback;
            }

            var result = new LLMResponse
            {
                Content = contentStr,
                Thinking = thinkingStr
            };

            if (toolCalls.Count > 0)
            {
                result.ToolCalls = new List<ToolCallInfo>();
                foreach (var kvp in toolCalls)
                    result.ToolCalls.Add(kvp.Value);
            }

            return result;
        }

        private static LLMResponse ParseNonStreamResponse(string json)
        {
            try
            {
                var obj = JObject.Parse(json);
                var message = obj["choices"]?[0]?["message"];
                var content = message?["content"]?.ToString() ?? "";
                var result = new LLMResponse { Content = content };

                var msgToolCalls = message?["tool_calls"] as JArray;
                if (msgToolCalls != null && msgToolCalls.Count > 0)
                {
                    result.ToolCalls = new List<ToolCallInfo>();
                    foreach (JObject tc in msgToolCalls)
                    {
                        result.ToolCalls.Add(new ToolCallInfo
                        {
                            Id = tc["id"]?.ToString(),
                            Name = tc["function"]?["name"]?.ToString(),
                            Arguments = tc["function"]?["arguments"]?.ToString() ?? "{}"
                        });
                    }
                }

                return result;
            }
            catch
            {
                return new LLMResponse { Content = json };
            }
        }

        private static string NormalizeUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return "https://api.openai.com/v1/chat/completions";
            var u = baseUrl.TrimEnd('/');
            if (u.EndsWith("/chat/completions")) return u;
            if (u.EndsWith("/v1")) return u + "/chat/completions";
            return u + "/v1/chat/completions";
        }

        private static string ExtractThinking(string text)
        {
            var s = text.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase);
            var e = text.IndexOf("</thinking>", StringComparison.OrdinalIgnoreCase);
            if (s < 0 || e < 0 || e <= s) return string.Empty;
            return text.Substring(s + 10, e - s - 10).Trim();
        }

        public void Dispose() => _http?.Dispose();
    }
}

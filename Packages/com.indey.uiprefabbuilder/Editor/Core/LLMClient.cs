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

        /// <summary>Must be called from main thread before each request cycle.</summary>
        public void RefreshApiKey() => _cachedApiKey = SecureKeyStore.LoadApiKey();

        public void StreamChatAsync(
            IReadOnlyList<ChatMessage> messages,
            Action<string> onToken,
            Action<string> onThinking,
            Action<string> onComplete,
            Action<Exception> onError,
            CancellationToken ct)
        {
            Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var apiKey = _cachedApiKey;
                    if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("API Key not configured. Open Settings to set it.");

                    var body = BuildRequestBody(messages);
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
                    if (contentType.Contains("text/event-stream"))
                    {
                        var full = await ReadSSE(resp, onToken, ct);
                        var thinking = ExtractThinking(full);
                        if (!string.IsNullOrEmpty(thinking))
                            MainThreadDispatcher.Enqueue(() => onThinking?.Invoke(thinking));
                        MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(full));
                    }
                    else
                    {
                        var text = await resp.Content.ReadAsStringAsync();
                        var content = ExtractContent(text);
                        MainThreadDispatcher.Enqueue(() => onToken?.Invoke(content));
                        MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(content));
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { MainThreadDispatcher.Enqueue(() => onError?.Invoke(e)); }
            }, ct);
        }

        private string BuildRequestBody(IReadOnlyList<ChatMessage> messages)
        {
            var arr = new JArray();
            foreach (var m in messages)
                arr.Add(new JObject { ["role"] = m.Role.ToString().ToLowerInvariant(), ["content"] = m.Content });

            var obj = new JObject { ["model"] = _settings.ModelName, ["stream"] = true, ["messages"] = arr };
            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string NormalizeUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return "https://api.openai.com/v1/chat/completions";
            var u = baseUrl.TrimEnd('/');
            if (u.EndsWith("/chat/completions")) return u;
            if (u.EndsWith("/v1")) return u + "/chat/completions";
            return u + "/v1/chat/completions";
        }

        private static async Task<string> ReadSSE(HttpResponseMessage resp, Action<string> onToken, CancellationToken ct)
        {
            var full = new StringBuilder();
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
                    var delta = json["choices"]?[0]?["delta"];
                    if (delta == null) continue;

                    // Support both content and reasoning_content (DeepSeek)
                    var content = delta["content"]?.ToString();
                    var reasoning = delta["reasoning_content"]?.ToString();

                    var text = content ?? reasoning ?? "";
                    if (string.IsNullOrEmpty(text)) continue;

                    full.Append(text);
                    var captured = text;
                    MainThreadDispatcher.Enqueue(() => onToken?.Invoke(captured));
                }
                catch { }
            }
            return full.ToString();
        }

        private static string ExtractContent(string json)
        {
            try { return JObject.Parse(json)["choices"]?[0]?["message"]?["content"]?.ToString() ?? json; }
            catch { return json; }
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

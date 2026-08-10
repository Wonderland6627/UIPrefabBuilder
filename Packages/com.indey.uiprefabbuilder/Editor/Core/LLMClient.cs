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

        /// <summary>
        /// Non-streaming multimodal probe: sends a 1x1 PNG and asks the model to reply VISION_OK.
        /// Does not use tools or enter MessageHistory.
        /// Must be called from the main thread (EditorPrefs / settings are snapshotted before Task.Run).
        /// </summary>
        public void ProbeVisionSupportAsync(
            Action<VisionProbeResult> onComplete,
            Action<Exception> onError,
            CancellationToken ct)
        {
            // Snapshot Unity-thread-only state on the caller (main) thread.
            RefreshApiKey();
            var apiKey = _cachedApiKey;
            var modelName = _settings.ModelName;
            var baseUrl = _settings.BaseUrl;
            var temperature = _settings.Temperature;
            var requestTimeoutSeconds = _settings.RequestTimeoutSeconds;

            Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(new VisionProbeResult
                        {
                            Supported = false,
                            IsAuthOrNetworkError = true,
                            Reason = "API Key not configured. Open Settings to set it."
                        }));
                        return;
                    }

                    // 1x1 red PNG
                    const string TinyPngBase64 =
                        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

                    var messages = new JArray
                    {
                        new JObject
                        {
                            ["role"] = "user",
                            ["content"] = new JArray
                            {
                                new JObject
                                {
                                    ["type"] = "text",
                                    ["text"] = "Is there an image attached to this message? Answer with a single word: YES or NO. No explanation."
                                },
                                new JObject
                                {
                                    ["type"] = "image_url",
                                    ["image_url"] = new JObject
                                    {
                                        ["url"] = "data:image/png;base64," + TinyPngBase64,
                                        ["detail"] = "low"
                                    }
                                }
                            }
                        }
                    };

                    // Budget must cover reasoning tokens: thinking models (Gemini, o-series, ...)
                    // spend the allowance internally and would otherwise return a truncated answer.
                    var bodyObj = new JObject
                    {
                        ["model"] = modelName,
                        ["stream"] = false,
                        ["messages"] = messages,
                        ["max_tokens"] = 2048
                    };
                    if (temperature >= 0)
                        bodyObj["temperature"] = 0;

                    var url = NormalizeUrl(baseUrl);
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    req.Content = new StringContent(bodyObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Min(30, Math.Max(10, requestTimeoutSeconds))));

                    HttpResponseMessage resp;
                    try
                    {
                        resp = await _http.SendAsync(req, timeoutCts.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(new VisionProbeResult
                        {
                            Supported = false,
                            IsAuthOrNetworkError = true,
                            Reason = "Vision probe timed out contacting the API."
                        }));
                        return;
                    }

                    using (resp)
                    {
                        var errBody = await resp.Content.ReadAsStringAsync();
                        var statusCode = (int)resp.StatusCode;

                        if (!resp.IsSuccessStatusCode)
                        {
                            var result = ClassifyProbeHttpFailure(statusCode, errBody);
                            MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(result));
                            return;
                        }

                        var parsed = ParseNonStreamResponse(errBody);
                        var content = parsed.Content ?? "";
                        var verdict = ClassifyProbeContent(content, ReadFinishReason(errBody));
                        MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(verdict));
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(e));
                }
            }, ct);
        }

        private static VisionProbeResult ClassifyProbeHttpFailure(int statusCode, string errBody)
        {
            var body = errBody ?? "";
            var lower = body.ToLowerInvariant();

            if (statusCode == 401 || statusCode == 403)
            {
                return new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = true,
                    Reason = $"Authentication failed (HTTP {statusCode}). Check API Key."
                };
            }

            if (statusCode == 404)
            {
                return new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = true,
                    Reason = $"Endpoint not found (HTTP 404). Check Base URL.\n{Truncate(body, 300)}"
                };
            }

            bool looksLikeNoVision =
                lower.Contains("image") || lower.Contains("vision") || lower.Contains("multimodal")
                || lower.Contains("does not support") || lower.Contains("unsupported")
                || lower.Contains("not support") || lower.Contains("invalid content");

            if (statusCode == 400 && looksLikeNoVision)
            {
                return new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = false,
                    Reason = $"API rejected the image — this model/endpoint likely does not support vision.\n{Truncate(body, 400)}"
                };
            }

            return new VisionProbeResult
            {
                Supported = false,
                IsAuthOrNetworkError = statusCode >= 500 || statusCode == 429,
                Reason = $"Vision probe HTTP {statusCode}: {Truncate(body, 400)}"
            };
        }

        private static string ReadFinishReason(string json)
        {
            try
            {
                return JObject.Parse(json)["choices"]?[0]?["finish_reason"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static VisionProbeResult ClassifyProbeContent(string content, string finishReason)
        {
            var text = (content ?? "").Trim();
            var upper = text.ToUpperInvariant();

            bool saysNo =
                HasWord(upper, "NO")
                || upper.Contains("VISION_UNSUPPORTED")
                || text.IndexOf("cannot see", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("can't see", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("unable to see", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("no image", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("don't support", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("do not support", StringComparison.OrdinalIgnoreCase) >= 0;

            if (saysNo)
            {
                return new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = false,
                    Reason = $"Model reported it cannot see images: \"{Truncate(text, 200)}\""
                };
            }

            if (HasWord(upper, "YES") || upper.Contains("VISION_OK"))
            {
                return new VisionProbeResult
                {
                    Supported = true,
                    Reason = "Model acknowledged the probe image."
                };
            }

            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                return new VisionProbeResult
                {
                    Supported = false,
                    IsAuthOrNetworkError = true,
                    Reason = "The model ran out of output tokens before answering (likely spent on internal reasoning). " +
                             "Try again, or use a model with a smaller reasoning budget."
                };
            }

            // Ambiguous but HTTP succeeded — treat as unsupported to avoid false confidence
            return new VisionProbeResult
            {
                Supported = false,
                IsAuthOrNetworkError = false,
                Reason = $"Vision probe reply was ambiguous (expected YES or NO): \"{Truncate(text, 200)}\""
            };
        }

        /// <summary>Whole-word match so "NORMAL" does not count as "NO".</summary>
        private static bool HasWord(string haystack, string word)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            int idx = 0;
            while ((idx = haystack.IndexOf(word, idx, StringComparison.Ordinal)) >= 0)
            {
                bool leftOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
                int end = idx + word.Length;
                bool rightOk = end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]);
                if (leftOk && rightOk) return true;
                idx = end;
            }
            return false;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

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

                    var maxRetries = _settings.MaxRetryCount > 0 ? _settings.MaxRetryCount : 3;
                    Exception lastEx = null;
                    LLMResponse result = null;

                    for (int attempt = 0; attempt < maxRetries; attempt++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (attempt > 0)
                            await Task.Delay(1000 * attempt, ct);

                        HttpResponseMessage resp = null;
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Post, url);
                            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

                            resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                            if (!resp.IsSuccessStatusCode)
                            {
                                var errBody = await resp.Content.ReadAsStringAsync();
                                var statusCode = (int)resp.StatusCode;

                                if (statusCode == 400 || statusCode == 401 || statusCode == 403 || statusCode == 404)
                                    throw new InvalidOperationException($"HTTP {statusCode}: {errBody}");

                                lastEx = new InvalidOperationException($"HTTP {statusCode}: {errBody}");
                                resp.Dispose();
                                continue;
                            }

                            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                            if (contentType.Contains("text/event-stream"))
                                result = await ReadSSEWithTools(resp, onToken, onThinkingToken, ct);
                            else
                                result = ParseNonStreamResponse(await resp.Content.ReadAsStringAsync());

                            lastEx = null;
                            break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (InvalidOperationException) { throw; }
                        catch (Exception e)
                        {
                            lastEx = e;
                        }
                        finally
                        {
                            resp?.Dispose();
                        }
                    }

                    if (lastEx != null)
                        throw lastEx;
                    if (result == null)
                        throw new InvalidOperationException("No response after retries.");

                    MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(result));
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    var detail = e.InnerException != null
                        ? $"{e.Message} -> {e.InnerException.GetType().Name}: {e.InnerException.Message}"
                        : e.Message;
                    var wrapped = new InvalidOperationException(detail, e);
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(wrapped));
                }
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

                if (m.ToolCallId != null)
                {
                    msg["role"] = "tool";
                    msg["tool_call_id"] = m.ToolCallId;
                    msg["content"] = m.Content ?? "";
                }
                else if (m.HasMultimodalContent)
                {
                    msg["content"] = BuildMultimodalContent(m);
                }
                else if (!string.IsNullOrEmpty(m.Content))
                {
                    msg["content"] = m.Content;
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

            if (_settings.EnableExtendedThinking)
            {
                if (_settings.ModelName.IndexOf("claude", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    obj["thinking"] = new JObject
                    {
                        ["type"] = "enabled",
                        ["budget_tokens"] = _settings.ThinkingBudgetTokens > 0
                            ? _settings.ThinkingBudgetTokens : 4096
                    };
                }
            }

            if (_settings.Temperature >= 0)
                obj["temperature"] = _settings.Temperature;

            return obj.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static JArray BuildMultimodalContent(ChatMessage m)
        {
            var contentArr = new JArray();
            foreach (var part in m.ContentParts)
            {
                if (part.Type == "text")
                {
                    contentArr.Add(new JObject { ["type"] = "text", ["text"] = part.Text });
                }
                else if (part.Type == "image_url")
                {
                    contentArr.Add(new JObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JObject
                        {
                            ["url"] = part.ImageUrl.Url,
                            ["detail"] = part.ImageUrl.Detail ?? "low"
                        }
                    });
                }
            }
            return contentArr;
        }

        private const int SSEReadTimeoutSeconds = 90;

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
                var lineTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(SSEReadTimeoutSeconds), ct);
                var completed = await Task.WhenAny(lineTask, timeoutTask);
                if (completed == timeoutTask)
                {
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException($"SSE stream read timed out: no data received for {SSEReadTimeoutSeconds} seconds.");
                }
                var line = await lineTask;
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

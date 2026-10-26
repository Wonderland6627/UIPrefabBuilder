using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Indey.UIPrefabBuilder.Async;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Core
{
    public class ToolCallInfo
    {
        public string Id;
        public string Name;
        public string Arguments;
        /// <summary>Vendor continuity object, such as a thought signature. Sent back unchanged.</summary>
        public string ContinuityJson;
    }

    public class LLMResponse
    {
        public string Content;
        public string Thinking;
        public bool ReasoningFieldPresent;
        public string ContinuityJson;
        public string Provider;
        public int PromptTokens;
        public int CompletionTokens;
        public int ReasoningTokens;
        public List<ToolCallInfo> ToolCalls;
        public bool HasToolCalls => ToolCalls != null && ToolCalls.Count > 0;
    }

    public class LLMClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly BuilderSettings _settings;
        private string _cachedApiKey;
        private string _capabilitySignature;
        private bool _reasoningSuppressed;
        private bool _usageSuppressed;
        private string _outlineTaskId;
        private int _outlinedMessages;

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

        /// <summary>
        /// One-shot completion with no tools and no streaming. Used for short side requests
        /// such as a chat title. Must be called from the main thread so settings can be snapshotted.
        /// </summary>
        public void CompleteTextAsync(
            string systemPrompt,
            string userPrompt,
            Action<string> onComplete,
            Action<Exception> onError)
        {
            RefreshApiKey();
            var apiKey = _cachedApiKey;
            var modelName = _settings.ModelName;
            var baseUrl = _settings.BaseUrl;
            var requestTimeoutSeconds = _settings.RequestTimeoutSeconds;

            Task.Run(async () =>
            {
                try
                {
                    if (string.IsNullOrEmpty(apiKey))
                        throw new InvalidOperationException("API Key not configured. Open Settings to set it.");

                    var messages = new JArray
                    {
                        new JObject { ["role"] = "system", ["content"] = systemPrompt ?? "" },
                        new JObject { ["role"] = "user", ["content"] = userPrompt ?? "" }
                    };
                    var bodyObj = new JObject
                    {
                        ["model"] = modelName,
                        ["stream"] = false,
                        ["max_tokens"] = 512,
                        ["messages"] = messages
                    };

                    var url = NormalizeUrl(baseUrl);
                    using var req = new HttpRequestMessage(HttpMethod.Post, url);
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);
                    req.Content = new StringContent(bodyObj.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");

                    using var timeoutCts = new CancellationTokenSource();
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, Math.Min(45, requestTimeoutSeconds))));

                    using var resp = await _http.SendAsync(req, timeoutCts.Token);
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"HTTP {(int)resp.StatusCode}: {Truncate(body, 300)}");

                    var content = StripThinkingTags(ParseNonStreamResponse(body).Content);
                    if (string.IsNullOrWhiteSpace(content))
                        throw new InvalidOperationException("模型没有返回标题文本。");

                    MainThreadDispatcher.Enqueue(() => onComplete?.Invoke(content.Trim()));
                }
                catch (OperationCanceledException)
                {
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(new InvalidOperationException("总结请求超时。")));
                }
                catch (Exception e)
                {
                    var detail = e.InnerException != null
                        ? $"{e.Message} -> {e.InnerException.Message}"
                        : e.Message;
                    MainThreadDispatcher.Enqueue(() => onError?.Invoke(new InvalidOperationException(detail, e)));
                }
            });
        }

        private static string StripThinkingTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            while (true)
            {
                var start = text.IndexOf("<thinking>", StringComparison.OrdinalIgnoreCase);
                var end = text.IndexOf("</thinking>", StringComparison.OrdinalIgnoreCase);
                if (start < 0 || end < 0 || end <= start) break;
                text = text.Remove(start, end + "</thinking>".Length - start);
            }
            return text.Trim();
        }

        public void ChatWithToolsAsync(
            IReadOnlyList<ChatMessage> messages,
            JArray tools,
            Action<string> onToken,
            Action<string> onThinkingToken,
            Action<LLMResponse> onComplete,
            Action<Exception> onError,
            CancellationToken ct,
            string taskId = null,
            string stepId = null)
        {
            Task.Run(async () =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var apiKey = _cachedApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                        throw new InvalidOperationException("API Key not configured. Open Settings to set it.");

                    var baseUrl = _settings.BaseUrl;
                    var model = _settings.ModelName;
                    var reasoningEnabled = _settings.EnableReasoning;
                    var signature = baseUrl + "|" + model + "|" + reasoningEnabled;
                    if (!string.Equals(signature, _capabilitySignature, StringComparison.Ordinal))
                    {
                        _capabilitySignature = signature;
                        _reasoningSuppressed = false;
                        _usageSuppressed = false;
                    }

                    var hasTools = tools != null && tools.Count > 0;
                    var rejection = ReasoningRequestAdapter.RejectionReason(baseUrl, model, reasoningEnabled && !_reasoningSuppressed, hasTools);
                    if (rejection != null)
                        throw new InvalidOperationException(rejection);

                    var request = BuildRequestBody(messages, tools, reasoningEnabled && !_reasoningSuppressed, !_usageSuppressed);
                    var body = request.ToString(Formatting.None);
                    LogRequestOutline(taskId, stepId, request);
                    var url = NormalizeUrl(baseUrl);

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

                                if (statusCode == 400 && reasoningEnabled && !_reasoningSuppressed
                                    && ReasoningRequestAdapter.IsUnsupportedReasoningField(errBody))
                                {
                                    _reasoningSuppressed = true;
                                    request = BuildRequestBody(messages, tools, false, !_usageSuppressed);
                                    body = request.ToString(Formatting.None);
                                    ConsoleLogger.Warning("[Reasoning] Endpoint rejected the reasoning fields. This session continues without them. " + TrimError(errBody));
                                    ConsoleLogger.LogAudit("reasoning_fallback", taskId, stepId, null, null, TrimError(errBody));
                                    resp.Dispose();
                                    attempt--;
                                    continue;
                                }

                                if (statusCode == 400 && !_usageSuppressed
                                    && ReasoningRequestAdapter.IsUnsupportedUsageField(errBody))
                                {
                                    _usageSuppressed = true;
                                    request = BuildRequestBody(messages, tools, reasoningEnabled && !_reasoningSuppressed, false);
                                    body = request.ToString(Formatting.None);
                                    ConsoleLogger.Warning("[Audit] Endpoint rejected stream usage. Token counts will be omitted. " + TrimError(errBody));
                                    resp.Dispose();
                                    attempt--;
                                    continue;
                                }

                                if (statusCode == 400 || statusCode == 401 || statusCode == 403 || statusCode == 404)
                                    throw new InvalidOperationException($"HTTP {statusCode}: {TrimError(errBody)}");

                                lastEx = new InvalidOperationException($"HTTP {statusCode}: {TrimError(errBody)}");
                                resp.Dispose();
                                continue;
                            }

                            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
                            var budget = TimeSpan.FromSeconds(Math.Max(15, _settings.RequestTimeoutSeconds));
                            if (contentType.Contains("text/event-stream"))
                                result = await ReadSSEWithTools(resp, onToken, onThinkingToken, ct, budget);
                            else
                                result = await ReadBodyWithin(resp, ct, budget);

                            result.Provider = ReasoningRequestAdapter.Detect(baseUrl, model).ToString();
                            lastEx = null;
                            break;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (OperationCanceledException)
                        {
                            lastEx = new TimeoutException($"Model request timed out after {Math.Max(15, _settings.RequestTimeoutSeconds)}s.");
                            ConsoleLogger.Warning("[Agent] " + lastEx.Message);
                        }
                        catch (TimeoutException e)
                        {
                            lastEx = e;
                            ConsoleLogger.Warning("[Agent] " + e.Message);
                        }
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
                onError, ct, null, null);
        }

        #endregion

        private JObject BuildRequestBody(IReadOnlyList<ChatMessage> messages, JArray tools, bool reasoningEnabled, bool includeUsage)
        {
            var provider = ReasoningRequestAdapter.Detect(_settings.BaseUrl, _settings.ModelName);
            var echoReasoning = reasoningEnabled && ReasoningRequestAdapter.EchoReasoningContent(provider);
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

                if (m.Role == ChatRole.Assistant && echoReasoning && m.ReasoningFieldPresent)
                    msg["reasoning_content"] = m.Thinking ?? "";

                if (!string.IsNullOrEmpty(m.ContinuityJson))
                {
                    try { msg["extra_content"] = JObject.Parse(m.ContinuityJson); }
                    catch { }
                }

                if (m.ToolCalls != null && m.ToolCalls.Count > 0)
                {
                    var calls = new JArray();
                    foreach (var tc in m.ToolCalls)
                    {
                        var call = new JObject
                        {
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JObject
                            {
                                ["name"] = tc.Name,
                                ["arguments"] = tc.Arguments
                            }
                        };
                        if (!string.IsNullOrEmpty(tc.ContinuityJson))
                        {
                            try { call["extra_content"] = JObject.Parse(tc.ContinuityJson); }
                            catch { }
                        }
                        calls.Add(call);
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

            if (includeUsage)
            {
                obj["stream_options"] = new JObject { ["include_usage"] = true };
            }

            ReasoningRequestAdapter.Apply(obj, _settings.BaseUrl, _settings.ModelName, reasoningEnabled);

            if (_settings.Temperature >= 0)
                obj["temperature"] = _settings.Temperature;

            return obj;
        }

        /// <summary>
        /// Writes the request envelope and only the messages added since the previous step.
        /// The message bodies stay in the session file and in the tool log.
        /// </summary>
        private void LogRequestOutline(string taskId, string stepId, JObject request)
        {
            if (!string.Equals(taskId, _outlineTaskId, StringComparison.Ordinal))
            {
                _outlineTaskId = taskId;
                _outlinedMessages = 0;
            }

            var messages = request?["messages"] as JArray;
            int count = messages?.Count ?? 0;
            int from = Math.Min(_outlinedMessages, count);
            var tools = request?["tools"] as JArray;
            ConsoleLogger.Log(
                "[Request] step=" + stepId
                + " model=" + request?["model"]
                + " tools=" + (tools?.Count ?? 0)
                + " messages=" + count
                + " new=" + (count - from)
                + " " + DescribeEnvelope(request));

            if (messages != null && count > from)
            {
                var outline = new StringBuilder();
                for (int i = from; i < count; i++)
                {
                    if (i > from) outline.Append('\n');
                    outline.Append(DescribeRequestMessage(i, messages[i] as JObject));
                }
                ConsoleLogger.LogBlock("REQUEST_NEW", outline.ToString());
            }

            _outlinedMessages = count;
        }

        static string DescribeEnvelope(JObject request)
        {
            if (request == null) return "";
            var parts = new List<string>();
            if (request["stream_options"] != null) parts.Add("usage=included");
            if (request["temperature"] != null) parts.Add("temperature=" + request["temperature"]);
            if (request["extra_body"] != null) parts.Add("extra_body=" + request["extra_body"].ToString(Formatting.None));
            if (request["thinking"] != null) parts.Add("thinking=" + request["thinking"].ToString(Formatting.None));
            if (request["reasoning_effort"] != null) parts.Add("reasoning_effort=" + request["reasoning_effort"]);
            return string.Join(" ", parts);
        }

        static string DescribeRequestMessage(int index, JObject message)
        {
            if (message == null) return "[" + index + "] (empty)";
            int textChars = 0;
            int images = 0;
            var content = message["content"];
            if (content is JArray parts)
            {
                foreach (var part in parts)
                {
                    var type = part?["type"]?.ToString();
                    if (type == "text") textChars += part["text"]?.ToString().Length ?? 0;
                    else if (type == "image_url") images++;
                }
            }
            else if (content != null && content.Type == JTokenType.String)
            {
                textChars = content.ToString().Length;
            }

            var line = "[" + index + "] " + message["role"] + " content=" + textChars + "c";
            var toolCallId = message["tool_call_id"]?.ToString();
            if (!string.IsNullOrEmpty(toolCallId)) line += " tool=" + toolCallId;
            if (images > 0) line += " images=" + images;
            var calls = message["tool_calls"] as JArray;
            if (calls != null && calls.Count > 0)
            {
                var names = new StringBuilder();
                for (int i = 0; i < calls.Count; i++)
                {
                    if (i > 0) names.Append(", ");
                    names.Append(calls[i]?["function"]?["name"]?.ToString());
                }
                line += " calls=[" + names + "]";
            }
            if (message["reasoning_content"] != null)
                line += " reasoning=" + message["reasoning_content"].ToString().Length + "c";
            if (message["extra_content"] != null)
                line += " continuity";
            return line;
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

        private static async Task<LLMResponse> ReadBodyWithin(HttpResponseMessage resp, CancellationToken ct, TimeSpan budget)
        {
            var read = resp.Content.ReadAsStringAsync();
            var finished = await Task.WhenAny(read, Task.Delay(budget, ct));
            if (finished != read)
            {
                ct.ThrowIfCancellationRequested();
                try { resp.Content.Dispose(); } catch { }
                ObserveAbandoned(read);
                throw new TimeoutException($"Model response exceeded {budget.TotalSeconds:0}s.");
            }
            return ParseNonStreamResponse(await read);
        }

        private static async Task<LLMResponse> ReadSSEWithTools(
            HttpResponseMessage resp,
            Action<string> onToken,
            Action<string> onThinkingToken,
            CancellationToken ct,
            TimeSpan budget)
        {
            var contentBuf = new StringBuilder();
            var thinkingBuf = new StringBuilder();
            var toolCalls = new Dictionary<int, ToolCallInfo>();
            string continuityJson = null;
            bool reasoningFieldPresent = false;
            int promptTokens = 0, completionTokens = 0, reasoningTokens = 0;
            var deadline = DateTime.UtcNow + budget;

            using var stream = await resp.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw new TimeoutException($"Model response exceeded {budget.TotalSeconds:0}s.");

                var lineTask = reader.ReadLineAsync();
                var timeoutTask = Task.Delay(remaining, ct);
                var completed = await Task.WhenAny(lineTask, timeoutTask);
                if (completed != lineTask)
                {
                    try { stream.Close(); } catch { }
                    ObserveAbandoned(lineTask);
                    ct.ThrowIfCancellationRequested();
                    throw new TimeoutException($"Model response exceeded {budget.TotalSeconds:0}s without completing.");
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
                    if (delta["reasoning_content"] != null)
                        reasoningFieldPresent = true;
                    var reasoning = delta["reasoning_content"]?.ToString() ?? delta["reasoning"]?.ToString();
                    var deltaExtra = delta["extra_content"] as JObject;
                    if (deltaExtra != null)
                        continuityJson = deltaExtra.ToString(Formatting.None);

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

                            var callExtra = tc["extra_content"] as JObject;
                            if (callExtra != null)
                                info.ContinuityJson = callExtra.ToString(Formatting.None);
                        }
                    }

                    ReadUsage(json["usage"] as JObject, ref promptTokens, ref completionTokens, ref reasoningTokens);
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
                Thinking = thinkingStr,
                ReasoningFieldPresent = reasoningFieldPresent || !string.IsNullOrEmpty(thinkingStr),
                ContinuityJson = continuityJson,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                ReasoningTokens = reasoningTokens
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
                var message = obj["choices"]?[0]?["message"] as JObject;
                var content = message?["content"]?.ToString() ?? "";
                var reasoningToken = message?["reasoning_content"];
                int promptTokens = 0, completionTokens = 0, reasoningTokens = 0;
                ReadUsage(obj["usage"] as JObject, ref promptTokens, ref completionTokens, ref reasoningTokens);
                var result = new LLMResponse
                {
                    Content = content,
                    Thinking = reasoningToken?.ToString() ?? "",
                    ReasoningFieldPresent = reasoningToken != null,
                    ContinuityJson = (message?["extra_content"] as JObject)?.ToString(Formatting.None),
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    ReasoningTokens = reasoningTokens
                };

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
                            Arguments = tc["function"]?["arguments"]?.ToString() ?? "{}",
                            ContinuityJson = (tc["extra_content"] as JObject)?.ToString(Formatting.None)
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

        static void ObserveAbandoned(Task task)
        {
            if (task == null) return;
            task.ContinueWith(finished =>
            {
                try { var _ = finished.Exception; } catch { }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        static void ReadUsage(JObject usage, ref int promptTokens, ref int completionTokens, ref int reasoningTokens)
        {
            if (usage == null) return;
            promptTokens = usage["prompt_tokens"]?.Value<int>() ?? promptTokens;
            completionTokens = usage["completion_tokens"]?.Value<int>() ?? completionTokens;
            var details = usage["completion_tokens_details"] as JObject;
            reasoningTokens = details?["reasoning_tokens"]?.Value<int>()
                ?? usage["reasoning_tokens"]?.Value<int>()
                ?? reasoningTokens;
        }

        static string TrimError(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            return RedactForLog(body);
        }

        static string RedactForLog(string body)
        {
            if (string.IsNullOrEmpty(body)) return "";
            body = Regex.Replace(body, @"Bearer\s+[A-Za-z0-9\-._~+/]+=*", "Bearer [redacted]");
            return Regex.Replace(body, @"data:[^""\\]*;base64,[A-Za-z0-9+/=]+", match =>
            {
                var value = match.Value;
                var comma = value.IndexOf(',');
                var payload = comma >= 0 ? value.Substring(comma + 1) : value;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(payload); }
                catch { bytes = Encoding.UTF8.GetBytes(payload); }
                string hash;
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                return "[image byteLength=" + bytes.Length + " sha256=" + hash + "]";
            });
        }
    }
}

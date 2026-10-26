using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Core
{
    public enum ReasoningProviderKind
    {
        Unknown,
        Gemini,
        Anthropic,
        OpenAI,
        DeepSeek,
        Xai
    }

    /// <summary>
    /// Maps the single "enable reasoning" setting onto each vendor's chat-completions fields.
    /// Effort stays at the model's own default. The user does not pick a level.
    /// </summary>
    public static class ReasoningRequestAdapter
    {
        const int LegacyClaudeBudgetTokens = 8192;

        public static ReasoningProviderKind Detect(string baseUrl, string model)
        {
            var m = model ?? "";
            var u = baseUrl ?? "";
            if (Contains(m, "gemini") || Contains(u, "generativelanguage.googleapis.com"))
                return ReasoningProviderKind.Gemini;
            if (Contains(m, "claude") || Contains(u, "api.anthropic.com"))
                return ReasoningProviderKind.Anthropic;
            if (Contains(m, "deepseek") || Contains(u, "api.deepseek.com"))
                return ReasoningProviderKind.DeepSeek;
            if (Contains(m, "grok") || Contains(u, "api.x.ai"))
                return ReasoningProviderKind.Xai;
            if (Contains(u, "api.openai.com") || StartsOpenAIModel(m))
                return ReasoningProviderKind.OpenAI;
            return ReasoningProviderKind.Unknown;
        }

        /// <summary>
        /// Returns a user-facing reason when this client cannot safely enable reasoning, otherwise null.
        /// </summary>
        public static string RejectionReason(string baseUrl, string model, bool enabled, bool hasTools)
        {
            if (!enabled) return null;
            if (Contains(baseUrl, "api.anthropic.com"))
                return "深度思考无法在 Anthropic 官方 Messages API 上启用：当前客户端只发送 Chat Completions 请求。请改用兼容该接口的网关。";
            if (!hasTools) return null;
            if (!Contains(baseUrl, "api.openai.com")) return null;
            if (!RequiresResponsesApi(model)) return null;
            return "该 OpenAI 模型在 Chat Completions 上不能同时使用工具和深度思考。请更换支持该组合的端点，或关闭深度思考。";
        }

        public static void Apply(JObject body, string baseUrl, string model, bool enabled)
        {
            if (body == null || !enabled) return;
            switch (Detect(baseUrl, model))
            {
                case ReasoningProviderKind.Gemini:
                    body["extra_body"] = new JObject
                    {
                        ["google"] = new JObject
                        {
                            ["thinking_config"] = new JObject
                            {
                                ["include_thoughts"] = true
                            }
                        }
                    };
                    break;
                case ReasoningProviderKind.Anthropic:
                    body["thinking"] = IsLegacyClaude(model)
                        ? new JObject { ["type"] = "enabled", ["budget_tokens"] = LegacyClaudeBudgetTokens }
                        : new JObject { ["type"] = "adaptive" };
                    break;
                case ReasoningProviderKind.DeepSeek:
                    body["thinking"] = new JObject { ["type"] = "enabled" };
                    break;
                case ReasoningProviderKind.OpenAI:
                case ReasoningProviderKind.Xai:
                case ReasoningProviderKind.Unknown:
                    break;
            }
        }

        public static bool IsUnsupportedReasoningField(string errorBody)
        {
            if (string.IsNullOrEmpty(errorBody)) return false;
            var text = errorBody.ToLowerInvariant();
            return text.Contains("thinking_config")
                || text.Contains("include_thoughts")
                || text.Contains("extra_body")
                || text.Contains("budget_tokens")
                || text.Contains("reasoning_effort")
                || text.Contains("\"thinking\"")
                || text.Contains("thinking.type")
                || (text.Contains("adaptive") && text.Contains("not supported"))
                || (text.Contains("enabled") && text.Contains("not supported") && text.Contains("thinking"));
        }

        public static bool IsUnsupportedUsageField(string errorBody)
        {
            if (string.IsNullOrEmpty(errorBody)) return false;
            var text = errorBody.ToLowerInvariant();
            return text.Contains("stream_options") || text.Contains("include_usage");
        }

        public static bool EchoReasoningContent(ReasoningProviderKind kind)
        {
            return kind == ReasoningProviderKind.DeepSeek
                || kind == ReasoningProviderKind.Xai
                || kind == ReasoningProviderKind.Gemini
                || kind == ReasoningProviderKind.OpenAI
                || kind == ReasoningProviderKind.Unknown;
        }

        static bool IsLegacyClaude(string model)
        {
            var m = (model ?? "").ToLowerInvariant();
            if (!m.Contains("claude")) return false;
            return !Regex.IsMatch(m, @"claude-(opus|sonnet|haiku|fable|mythos)-([5-9]|4[.-][7-9])");
        }

        static bool RequiresResponsesApi(string model)
        {
            var m = (model ?? "").ToLowerInvariant();
            return Regex.IsMatch(m, @"gpt-(5[.-][4-9]|5[.-]\d{2,}|6)");
        }

        static bool StartsOpenAIModel(string model)
        {
            var m = (model ?? "").ToLowerInvariant();
            return m.StartsWith("gpt-") || m.StartsWith("o1") || m.StartsWith("o3") || m.StartsWith("o4") || m.StartsWith("chatgpt");
        }

        static bool Contains(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using Indey.UIPrefabBuilder.Skills;
using Indey.UIPrefabBuilder.Transaction;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Core
{
    public class AgentEngine : IDisposable
    {
        private readonly BuilderSettings _settings;
        private readonly MessageHistory _history = new MessageHistory();
        private readonly SkillRegistry _skillRegistry = new SkillRegistry();
        private readonly ToolRegistry _toolRegistry = new ToolRegistry();
        private readonly TransactionManager _txManager = new TransactionManager();
        private LLMClient _llm;

        private AgentState _state = AgentState.Idle;
        private CancellationTokenSource _cts;
        private string _pendingMessage;
        private int _currentStep;
        private const int HardCap = 500;

        public AgentState State => _state;
        public MessageHistory History => _history;
        public SkillRegistry Skills => _skillRegistry;
        public ToolRegistry Tools => _toolRegistry;
        public string LatestCode { get; private set; } = "";
        public string LatestThinking { get; private set; } = "";

        public event Action<AgentState> OnStateChanged;
        public event Action<string> OnStreamToken;
        public event Action<string> OnThinkingToken;
        public event Action<string> OnThinking;
        public event Action<string> OnComplete;
        public event Action<string> OnError;
        public event Action<ActionResult> OnExecuted;
        public event Action<string, string, string> OnToolCall;

        public AgentEngine(BuilderSettings settings)
        {
            _settings = settings;
            _llm = new LLMClient(settings);
            _skillRegistry.DiscoverSkills();
            _toolRegistry.Rebuild();
            RebuildSystemPrompt();
        }

        public void RefreshSkills()
        {
            _skillRegistry.DiscoverSkills();
            _toolRegistry.Rebuild();
            RebuildSystemPrompt();
        }

        public void StartTask(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            if (_state != AgentState.Idle && _state != AgentState.Error
                && _state != AgentState.WaitingConfirmation && _state != AgentState.Completed)
                return;

            var effectiveMax = EffectiveMaxSteps;
            ConsoleLogger.Log($"=== NEW TASK ===");
            ConsoleLogger.Log($"[Config] model={_settings.ModelName}, baseUrl={_settings.BaseUrl}, timeout={_settings.RequestTimeoutSeconds}s, maxSteps={(_settings.MaxAgentSteps <= 0 ? "unlimited" : _settings.MaxAgentSteps.ToString())} (effective={effectiveMax})");
            ConsoleLogger.Log($"[Config] tools={_toolRegistry.ToolNames.Count}");
            ConsoleLogger.LogBlock("SYSTEM_PROMPT", _history.Messages.Count > 0 && _history.Messages[0].Role == ChatRole.System ? _history.Messages[0].Content : "(none)");
            ConsoleLogger.LogBlock("USER_REQUEST", msg);

            _pendingMessage = msg;
            _currentStep = 0;
            LatestCode = "";
            LatestThinking = "";
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _history.AddUser(_pendingMessage);
            _txManager.Begin("AI Agent Task");
            TransitionTo(AgentState.Thinking);
        }

        public void Cancel()
        {
            _cts?.Cancel();
            TransitionTo(AgentState.Idle);
        }

        public void Confirm() => TransitionTo(AgentState.Idle);

        public void Rollback()
        {
            _txManager.RollbackLast();
            TransitionTo(AgentState.Idle);
        }

        private void RebuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are **UIPrefabBuilder**, a Unity Editor AI agent specialized in UGUI and UI Prefab workflows.");
            sb.AppendLine();
            sb.AppendLine("## How You Work");
            sb.AppendLine("You have tools to inspect the project, search assets, create and modify UI elements, and manage prefabs.");
            sb.AppendLine("Plan your approach first, then execute efficiently.");
            sb.AppendLine();
            sb.AppendLine("## Efficiency Rules (IMPORTANT)");
            sb.AppendLine("- **Batch tool calls**: Always call multiple tools in parallel when they are independent. For example, create an element AND set its anchor/size/position in the same step, or search assets and inspect scene overview simultaneously.");
            sb.AppendLine("- **Minimize verification**: Only use `inspect_hierarchy` / `inspect_components` when something may have gone wrong or before a complex next step. Do NOT inspect after every single tool call.");
            sb.AppendLine("- **Use `execute_code` for batch creation**: When creating many similar elements (e.g. 10+ item slots, grid cells), write a single `execute_code` call instead of creating them one by one with individual tool calls.");
            sb.AppendLine("- **Combine create + configure**: After creating a UI element, immediately set its anchor, size, position, and sprite in the same step rather than spreading across multiple steps.");
            sb.AppendLine();
            sb.AppendLine("## Guidelines");
            sb.AppendLine("- **Explore first**: Use `search_assets` and `get_scene_overview` together before creating anything.");
            sb.AppendLine("- **Choose sprites wisely**: Pick by naming convention (e.g. `btn_green` for confirm, `dialog_*` for backgrounds).");
            sb.AppendLine("- **Anchor patterns**: StretchAll for overlays/masks, MiddleCenter for dialogs, top/bottom presets for HUD.");
            sb.AppendLine("- **Layout groups**: Prefer `add_horizontal_layout` / `add_vertical_layout` / `add_grid_layout` for automatic arrangement.");
            sb.AppendLine();

            var skillsDocs = _skillRegistry.LoadRelevantDocs("", 6000);
            if (!string.IsNullOrWhiteSpace(skillsDocs))
            {
                sb.AppendLine("## Reference Knowledge");
                sb.AppendLine(skillsDocs);
                sb.AppendLine();
            }

            sb.AppendLine("## execute_code Usage");
            sb.AppendLine("The `execute_code` tool compiles and runs C# code at runtime. Your code must implement `IAgentAction`.");
            sb.AppendLine("If your code does NOT contain `class` and `IAgentAction`, it will be auto-wrapped (just write the body).");
            sb.AppendLine("");
            sb.AppendLine("### Auto-wrapped mode (preferred for simple tasks):");
            sb.AppendLine("```csharp");
            sb.AppendLine("// Just write statements - they run inside Execute(ActionContext context)");
            sb.AppendLine("var canvas = GameObject.Find(\"MyCanvas\");");
            sb.AppendLine("var scaler = canvas.GetComponent<CanvasScaler>();");
            sb.AppendLine("scaler.referenceResolution = new Vector2(1080, 2340);");
            sb.AppendLine("// Auto-returns ActionResult.Ok(\"Done.\") at the end");
            sb.AppendLine("```");
            sb.AppendLine("");
            sb.AppendLine("### Full class mode (for complex logic):");
            sb.AppendLine("```csharp");
            sb.AppendLine("public class MyAction : IAgentAction");
            sb.AppendLine("{");
            sb.AppendLine("    public string ActionName => \"MyAction\";");
            sb.AppendLine("    public string Description => \"Does something\";");
            sb.AppendLine("    public ActionResult Execute(ActionContext context)");
            sb.AppendLine("    {");
            sb.AppendLine("        var go = GameObject.Find(\"TargetName\");");
            sb.AppendLine("        if (go == null) return ActionResult.Fail(\"Not found\");");
            sb.AppendLine("        // ... do work ...");
            sb.AppendLine("        return ActionResult.Ok(\"Success\");");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("Available in context: `context.CanvasRoot` (first Canvas), `context.TargetObject` (selected object).");
            sb.AppendLine("Available APIs: UnityEngine, UnityEngine.UI, UnityEditor, System.Linq, Indey.UIPrefabBuilder.Core/Skills.");
            sb.AppendLine("");
            sb.AppendLine("## Safety");
            sb.AppendLine("- Never delete files or assets. Never quit Unity.");
            sb.AppendLine("- If `execute_code` fails more than 2 times, stop trying and use other available tools instead.");
            sb.AppendLine();
            sb.AppendLine("When you finish the task, respond with a brief summary of what you created.");
            _history.SetSystemPrompt(sb.ToString());
        }

        private void TransitionTo(AgentState s)
        {
            _state = s;
            OnStateChanged?.Invoke(s);
            if (s == AgentState.Thinking) DoThink();
        }

        private int EffectiveMaxSteps => _settings.MaxAgentSteps <= 0 ? HardCap : _settings.MaxAgentSteps;

        private void DoThink()
        {
            var max = EffectiveMaxSteps;
            if (_currentStep >= max)
            {
                HandleError($"Agent stopped: reached maximum {max} steps.");
                return;
            }
            _currentStep++;

            _llm.RefreshApiKey();
            ConsoleLogger.Log($"[Agent] Step {_currentStep}/{(_settings.MaxAgentSteps <= 0 ? "∞" : max.ToString())}, messages={_history.Count}");

            _llm.ChatWithToolsAsync(
                _history.Messages,
                _toolRegistry.Definitions,
                t => OnStreamToken?.Invoke(t),
                t => { OnThinkingToken?.Invoke(t); },
                response => HandleLLMResponse(response),
                e => HandleError(e.Message),
                _cts.Token);
        }

        private void HandleLLMResponse(LLMResponse response)
        {
            var toolCount = response.ToolCalls?.Count ?? 0;
            var toolNames = toolCount > 0
                ? string.Join(", ", response.ToolCalls.ConvertAll(tc => tc.Name))
                : "none";
            ConsoleLogger.Log($"[Agent] Step {_currentStep} response: tools=[{toolNames}], content={response.Content?.Length ?? 0}c, thinking={response.Thinking?.Length ?? 0}c");

            if (!string.IsNullOrEmpty(response.Thinking))
                ConsoleLogger.LogBlock($"LLM_THINKING_STEP_{_currentStep}", response.Thinking);
            if (!string.IsNullOrEmpty(response.Content) && toolCount == 0)
                ConsoleLogger.LogBlock($"LLM_CONTENT_STEP_{_currentStep}", response.Content);

            if (!string.IsNullOrEmpty(response.Thinking))
            {
                LatestThinking = response.Thinking;
                OnThinking?.Invoke(response.Thinking);
            }

            if (response.HasToolCalls)
            {
                _history.AddAssistantWithToolCalls(response);

                if (!string.IsNullOrEmpty(response.Content))
                    OnStreamToken?.Invoke(response.Content);

                TransitionTo(AgentState.CallingTool);
                ExecuteToolCalls(response.ToolCalls);
            }
            else
            {
                _history.AddAssistant(response.Content);
                OnComplete?.Invoke(response.Content);
                ConsoleLogger.Log($"[Agent] Task completed in {_currentStep} steps.");
                TransitionTo(AgentState.Completed);
            }
        }

        private void ExecuteToolCalls(List<ToolCallInfo> toolCalls)
        {
            foreach (var call in toolCalls)
            {
                if (_cts.IsCancellationRequested) return;

                OnToolCall?.Invoke(call.Name, call.Arguments, null);

                string result;
                try
                {
                    result = _toolRegistry.Execute(call.Name, call.Arguments);
                }
                catch (Exception e)
                {
                    result = new JObject { ["success"] = false, ["error"] = e.Message }.ToString();
                    ConsoleLogger.Error($"[Agent] Tool exception: {call.Name} -> {e.Message}");
                }

                ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                _history.AddToolResult(call.Id, call.Name, result);
                OnToolCall?.Invoke(call.Name, call.Arguments, result);

                ReportToolResult(call.Name, result);
            }

            if (!_cts.IsCancellationRequested)
                TransitionTo(AgentState.Thinking);
        }

        private void ReportToolResult(string toolName, string resultJson)
        {
            try
            {
                var obj = JObject.Parse(resultJson);
                var success = (bool)(obj["success"] ?? false);
                var message = obj["message"]?.ToString() ?? obj["error"]?.ToString() ?? resultJson;
                OnExecuted?.Invoke(success ? ActionResult.Ok($"[{toolName}] {message}") : ActionResult.Fail($"[{toolName}] {message}"));
            }
            catch
            {
                OnExecuted?.Invoke(ActionResult.Ok($"[{toolName}] done"));
            }
        }

        private void HandleError(string msg)
        {
            ConsoleLogger.Error(msg);
            DumpRecentContext();
            _state = AgentState.Error;
            OnStateChanged?.Invoke(_state);
            OnError?.Invoke(msg);
        }

        private void DumpRecentContext()
        {
            var sb = new StringBuilder();
            var messages = _history.Messages;
            int start = Math.Max(1, messages.Count - 6);
            for (int i = start; i < messages.Count; i++)
            {
                var m = messages[i];
                var preview = m.Content ?? "";
                if (preview.Length > 300) preview = preview.Substring(0, 300) + "...";
                sb.AppendLine($"  [{m.Role}] {preview}");
                if (m.ToolCalls != null)
                    foreach (var tc in m.ToolCalls)
                        sb.AppendLine($"    -> tool_call: {tc.Name}({(tc.Arguments?.Length > 120 ? tc.Arguments.Substring(0, 120) + "..." : tc.Arguments)})");
            }
            ConsoleLogger.LogBlock("ERROR_CONTEXT_LAST_MESSAGES", sb.ToString());
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _llm?.Dispose();
        }
    }
}

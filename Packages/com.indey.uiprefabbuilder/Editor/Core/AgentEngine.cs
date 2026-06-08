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
        private int _maxSteps = 30;

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

            ConsoleLogger.Log($"StartTask: {(msg.Length > 80 ? msg.Substring(0, 80) + "..." : msg)}");
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
            sb.AppendLine("Use tools step by step to accomplish the user's request. Observe results before proceeding.");
            sb.AppendLine();
            sb.AppendLine("## Guidelines");
            sb.AppendLine("- **Explore first**: If the task references project assets (sprites, prefabs, textures), use `search_assets` to discover what's available before creating anything.");
            sb.AppendLine("- **Verify the scene**: Use `get_scene_overview` to see existing Canvas/UI before creating duplicates.");
            sb.AppendLine("- **Step by step**: Create elements one at a time or in logical groups, then set their properties (anchor, size, position, sprite).");
            sb.AppendLine("- **Verify your work**: After creating UI, use `inspect_hierarchy` to confirm the structure is correct.");
            sb.AppendLine("- **Choose sprites wisely**: When multiple sprites are available, pick by naming convention (e.g. `btn_green` for confirm, `dialog_*` for backgrounds).");
            sb.AppendLine("- **Anchor patterns**: Use StretchAll for overlays/masks, MiddleCenter for dialogs, top/bottom presets for HUD elements.");
            sb.AppendLine("- **Layout groups**: Use `add_horizontal_layout` / `add_vertical_layout` for automatic child arrangement instead of manual positioning.");
            sb.AppendLine();
            sb.AppendLine("## Safety");
            sb.AppendLine("- Never delete files or assets.");
            sb.AppendLine("- Never quit Unity.");
            sb.AppendLine("- Use `execute_code` only as a last resort when other tools cannot achieve the desired result.");
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

        private void DoThink()
        {
            if (_currentStep >= _maxSteps)
            {
                HandleError($"Agent stopped: reached maximum {_maxSteps} steps.");
                return;
            }
            _currentStep++;

            _llm.RefreshApiKey();
            ConsoleLogger.Log($"[Agent] Step {_currentStep}/{_maxSteps}, messages={_history.Count}");

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

                ConsoleLogger.Log($"[Agent] Tool call: {call.Name}");
                OnToolCall?.Invoke(call.Name, call.Arguments, null);

                string result;
                try
                {
                    result = _toolRegistry.Execute(call.Name, call.Arguments);
                }
                catch (Exception e)
                {
                    result = new JObject { ["success"] = false, ["error"] = e.Message }.ToString();
                }

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
            _state = AgentState.Error;
            OnStateChanged?.Invoke(_state);
            OnError?.Invoke(msg);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _llm?.Dispose();
        }
    }
}

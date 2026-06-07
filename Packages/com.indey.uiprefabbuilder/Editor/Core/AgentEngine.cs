using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Indey.UIPrefabBuilder.Compiler;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Logging;
using Indey.UIPrefabBuilder.Skills;
using Indey.UIPrefabBuilder.Transaction;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.Core
{
    public class AgentEngine : IDisposable
    {
        private readonly BuilderSettings _settings;
        private readonly MessageHistory _history = new MessageHistory();
        private readonly SkillRegistry _skillRegistry = new SkillRegistry();
        private readonly DynamicCompiler _compiler = new DynamicCompiler();
        private readonly TransactionManager _txManager = new TransactionManager();
        private LLMClient _llm;

        private AgentState _state = AgentState.Idle;
        private CancellationTokenSource _cts;
        private string _pendingMessage;
        private string _latestResponse = "";
        private string _latestCode = "";
        private Assembly _compiledAssembly;
        private List<string> _beforeSnapshot;
        private int _retryCount;

        public AgentState State => _state;
        public MessageHistory History => _history;
        public SkillRegistry Skills => _skillRegistry;
        public string LatestCode => _latestCode;
        public string LatestThinking { get; private set; } = "";

        public event Action<AgentState> OnStateChanged;
        public event Action<string> OnStreamToken;
        public event Action<string> OnThinkingToken;
        public event Action<string> OnThinking;
        public event Action<string> OnComplete;
        public event Action<string> OnError;
        public event Action<ActionResult> OnExecuted;

        public AgentEngine(BuilderSettings settings)
        {
            _settings = settings;
            _llm = new LLMClient(settings);
            _skillRegistry.DiscoverSkills();
            RebuildSystemPrompt();
        }

        public void RefreshSkills() { _skillRegistry.DiscoverSkills(); RebuildSystemPrompt(); }

        public void StartTask(string msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) return;
            if (_state != AgentState.Idle && _state != AgentState.Error && _state != AgentState.WaitingConfirmation) return;

            ConsoleLogger.Log($"StartTask: {(msg.Length > 80 ? msg.Substring(0, 80) + "..." : msg)}");
            _pendingMessage = msg;
            _retryCount = 0;
            _latestResponse = _latestCode = "";
            LatestThinking = "";
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            TransitionTo(AgentState.BuildingContext);
        }

        public void Cancel() { _cts?.Cancel(); TransitionTo(AgentState.Idle); }
        public void Confirm() => TransitionTo(AgentState.Idle);
        public void Rollback() { _txManager.RollbackLast(); TransitionTo(AgentState.Idle); }

        private void RebuildSystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine(_skillRegistry.LoadSystemPrompt());
            sb.AppendLine();
            sb.AppendLine(_skillRegistry.GenerateSkillsSummary());
            sb.AppendLine();
            sb.AppendLine("## Output Rules");
            sb.AppendLine("- Optionally think in <thinking>...</thinking>.");
            sb.AppendLine("- Always output exactly one ```csharp block implementing IAgentAction.");
            sb.AppendLine("- Use helpers: UICreator, RectTransformHelper, LayoutHelper, PrefabHelper, AssetFinder, ComponentHelper, SceneHelper, HierarchyInspector.");
            sb.AppendLine("- Never delete files, quit Unity, or access network.");
            _history.SetSystemPrompt(sb.ToString());
        }

        private void TransitionTo(AgentState s)
        {
            _state = s;
            OnStateChanged?.Invoke(s);
            switch (s)
            {
                case AgentState.BuildingContext: DoBuildContext(); break;
                case AgentState.WaitingForLLM: DoCallLLM(); break;
                case AgentState.ExtractingCode: DoExtract(); break;
                case AgentState.Compiling: DoCompile(); break;
                case AgentState.Executing: DoExecute(); break;
                case AgentState.ObservingResult: DoObserve(); break;
            }
        }

        private void DoBuildContext()
        {
            var ctx = ContextBuilder.BuildSelectionContext();
            var docs = _skillRegistry.LoadRelevantDocs(_pendingMessage);
            var prompt = $"## Context\n{ctx}\n\n## Skills\n{docs}\n\n## Request\n{_pendingMessage}";
            _history.AddUser(prompt);
            ConsoleLogger.Log($"[Prompt] Context length={ctx.Length}, Skills docs length={docs.Length}");
            ConsoleLogger.Log($"[Prompt] Full user prompt:\n{prompt}");
            TransitionTo(AgentState.WaitingForLLM);
        }

        private void DoCallLLM()
        {
            _llm.RefreshApiKey();
            ConsoleLogger.Log($"[LLM] Calling model={_settings.ModelName}, baseUrl={_settings.BaseUrl}, timeout={_settings.RequestTimeoutSeconds}s, messages={_history.Count}");
            var buf = new StringBuilder();
            _llm.StreamChatAsync(_history.Messages,
                t => { buf.Append(t); OnStreamToken?.Invoke(t); },
                t => { OnThinkingToken?.Invoke(t); },
                t => { LatestThinking = t; OnThinking?.Invoke(t); ConsoleLogger.Log($"Thinking complete ({t.Length} chars)"); },
                full =>
                {
                    _latestResponse = string.IsNullOrEmpty(full) ? buf.ToString() : full;
                    _history.AddAssistant(_latestResponse);
                    OnComplete?.Invoke(_latestResponse);
                    ConsoleLogger.Log($"LLM response complete ({_latestResponse.Length} chars)");
                    TransitionTo(AgentState.ExtractingCode);
                },
                e => HandleError(e.Message),
                _cts.Token);
        }

        private void DoExtract()
        {
            ConsoleLogger.Log("Extracting code from response...");
            var extracted = CodeExtractor.ExtractFirst(_latestResponse);
            if (!string.IsNullOrWhiteSpace(extracted))
                _latestCode = extracted;

            if (string.IsNullOrWhiteSpace(_latestCode))
            {
                ConsoleLogger.Warning($"Code extraction failed. Response length={_latestResponse?.Length ?? 0}");
                HandleError("No code block in response.");
                return;
            }
            ConsoleLogger.Log($"[Code] Extracted code ({_latestCode.Length} chars):\n{_latestCode}");
            var v = SandboxValidator.Validate(_latestCode);
            if (!v.IsValid) { ConsoleLogger.Warning("Sandbox validation failed: " + string.Join("; ", v.Errors)); Retry("Sandbox: " + string.Join("; ", v.Errors)); return; }
            ConsoleLogger.Log("Code extracted and validated successfully");
            TransitionTo(AgentState.Compiling);
        }

        private void DoCompile()
        {
            ConsoleLogger.Log("Compiling generated code...");
            _compiler.CompileAsync(_latestCode,
                r =>
                {
                    if (!r.Success)
                    {
                        if (r.IsConfigError)
                        {
                            ConsoleLogger.Error("Compiler config error: " + string.Join("; ", r.Errors));
                            HandleError(string.Join("\n", r.Errors) + "\n\nThis is a configuration issue (C# compiler not found). Please check your Unity installation.");
                            return;
                        }
                        ConsoleLogger.Warning("Compile failed: " + string.Join("; ", r.Errors));
                        Retry("Compile: " + string.Join("\n", r.Errors));
                        return;
                    }
                    ConsoleLogger.Log("Compilation succeeded");
                    _compiledAssembly = r.Assembly;
                    TransitionTo(AgentState.Executing);
                },
                e => { ConsoleLogger.Error("Compile exception: " + e.Message); HandleError(e.Message); });
        }

        private void DoExecute()
        {
            ConsoleLogger.Log("Executing compiled action...");
            var actionType = DynamicCompiler.FindActionType(_compiledAssembly);
            if (actionType == null) { ConsoleLogger.Warning("No IAgentAction found in assembly"); Retry("No IAgentAction found in compiled assembly."); return; }

            _beforeSnapshot = SnapshotService.Capture(Selection.activeGameObject);
            _txManager.Begin("AI Action");
            try
            {
                using (new UndoScope("AI Generated"))
                {
                    var action = (IAgentAction)Activator.CreateInstance(actionType);
                    var ctx = new ActionContext
                    {
                        TargetObject = Selection.activeGameObject,
                        CanvasRoot = UnityEngine.Object.FindObjectOfType<Canvas>()?.gameObject
                    };
                    var result = action.Execute(ctx);
                    OnExecuted?.Invoke(result);
                    if (!result.Success) { ConsoleLogger.Warning("Action failed: " + result.Message); _txManager.RollbackLast(); Retry("Runtime: " + result.Message); return; }
                    ConsoleLogger.Log("Action executed successfully: " + result.Message);
                    _history.AddToolResult(result.Message);
                    TransitionTo(AgentState.ObservingResult);
                }
            }
            catch (Exception e) { ConsoleLogger.Error("Execution exception: " + e.Message); _txManager.RollbackLast(); Retry("Exception: " + e.Message); }
        }

        private void DoObserve()
        {
            var after = SnapshotService.Capture(Selection.activeGameObject);
            _history.AddToolResult(SnapshotService.Diff(_beforeSnapshot, after));
            TransitionTo(AgentState.WaitingConfirmation);
        }

        private void Retry(string feedback)
        {
            if (_retryCount >= _settings.MaxRetryCount) { ConsoleLogger.Error($"Max retries reached: {feedback}"); HandleError(feedback); return; }
            _retryCount++;
            ConsoleLogger.Warning($"Retry {_retryCount}/{_settings.MaxRetryCount}: {feedback}");
            _history.AddToolResult($"[ERROR] {feedback}\n\n[Retry {_retryCount}/{_settings.MaxRetryCount}] Fix the error above and output a corrected ```csharp code block implementing IAgentAction. Do NOT explain, just output the fixed code.");
            TransitionTo(AgentState.WaitingForLLM);
        }

        private void HandleError(string msg)
        {
            ConsoleLogger.Error(msg);
            _state = AgentState.Error;
            OnStateChanged?.Invoke(_state);
            OnError?.Invoke(msg);
        }

        public void Dispose() { _cts?.Cancel(); _llm?.Dispose(); }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using Indey.UIPrefabBuilder.Compiler;
using Indey.UIPrefabBuilder.Config;
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
            TransitionTo(AgentState.WaitingForLLM);
        }

        private void DoCallLLM()
        {
            _llm.RefreshApiKey();
            var buf = new StringBuilder();
            _llm.StreamChatAsync(_history.Messages,
                t => { buf.Append(t); OnStreamToken?.Invoke(t); },
                t => { LatestThinking = t; OnThinking?.Invoke(t); },
                full =>
                {
                    _latestResponse = string.IsNullOrEmpty(full) ? buf.ToString() : full;
                    _history.AddAssistant(_latestResponse);
                    OnComplete?.Invoke(_latestResponse);
                    TransitionTo(AgentState.ExtractingCode);
                },
                e => HandleError(e.Message),
                _cts.Token);
        }

        private void DoExtract()
        {
            var extracted = CodeExtractor.ExtractFirst(_latestResponse);
            if (!string.IsNullOrWhiteSpace(extracted))
                _latestCode = extracted;

            if (string.IsNullOrWhiteSpace(_latestCode))
            {
                Debug.LogWarning($"[UIPrefabBuilder] Code extraction failed. Response length={_latestResponse?.Length ?? 0}");
                HandleError("No code block in response.");
                return;
            }
            var v = SandboxValidator.Validate(_latestCode);
            if (!v.IsValid) { Retry("Sandbox: " + string.Join("; ", v.Errors)); return; }
            TransitionTo(AgentState.Compiling);
        }

        private void DoCompile()
        {
            _compiler.CompileAsync(_latestCode,
                r =>
                {
                    if (!r.Success)
                    {
                        if (r.IsConfigError)
                        {
                            HandleError(string.Join("\n", r.Errors) + "\n\nThis is a configuration issue (C# compiler not found). Please check your Unity installation.");
                            return;
                        }
                        Retry("Compile: " + string.Join("\n", r.Errors));
                        return;
                    }
                    _compiledAssembly = r.Assembly;
                    TransitionTo(AgentState.Executing);
                },
                e => HandleError(e.Message));
        }

        private void DoExecute()
        {
            var actionType = DynamicCompiler.FindActionType(_compiledAssembly);
            if (actionType == null) { Retry("No IAgentAction found in compiled assembly."); return; }

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
                    if (!result.Success) { _txManager.RollbackLast(); Retry("Runtime: " + result.Message); return; }
                    _history.AddToolResult(result.Message);
                    TransitionTo(AgentState.ObservingResult);
                }
            }
            catch (Exception e) { _txManager.RollbackLast(); Retry("Exception: " + e.Message); }
        }

        private void DoObserve()
        {
            var after = SnapshotService.Capture(Selection.activeGameObject);
            _history.AddToolResult(SnapshotService.Diff(_beforeSnapshot, after));
            TransitionTo(AgentState.WaitingConfirmation);
        }

        private void Retry(string feedback)
        {
            if (_retryCount >= _settings.MaxRetryCount) { HandleError(feedback); return; }
            _retryCount++;
            _history.AddToolResult($"[ERROR] {feedback}\n\n[Retry {_retryCount}/{_settings.MaxRetryCount}] Fix the error above and output a corrected ```csharp code block implementing IAgentAction. Do NOT explain, just output the fixed code.");
            TransitionTo(AgentState.WaitingForLLM);
        }

        private void HandleError(string msg)
        {
            _state = AgentState.Error;
            OnStateChanged?.Invoke(_state);
            OnError?.Invoke(msg);
        }

        public void Dispose() { _cts?.Cancel(); _llm?.Dispose(); }
    }
}

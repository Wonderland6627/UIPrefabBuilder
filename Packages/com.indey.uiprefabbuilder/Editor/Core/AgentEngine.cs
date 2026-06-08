using System;
using System.Collections.Generic;
using System.Linq;
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
            sb.AppendLine($"- Generated code MUST use C# {GetMaxLangVersion()} syntax ONLY. No features from higher C# versions.");
            sb.AppendLine("- FORBIDDEN: global using, file-scoped namespaces, record struct, raw string literals, required members, primary constructors, collection expressions.");
            sb.AppendLine("- Use helpers: UICreator, RectTransformHelper, LayoutHelper, PrefabHelper, AssetFinder, ComponentHelper, SceneHelper, HierarchyInspector.");
            sb.AppendLine("- For RectTransform operations not covered by RectTransformHelper, use the Unity RectTransform API directly (anchorMin, anchorMax, pivot, sizeDelta, anchoredPosition).");
            sb.AppendLine("- For component property changes not covered by ComponentHelper, use the Unity component API directly (GetComponent<T>(), AddComponent<T>()).");
            sb.AppendLine("- Never delete files, quit Unity, or access network.");
            sb.AppendLine();
            sb.AppendLine(IAgentActionContract);
            sb.AppendLine();
            sb.AppendLine(ApiQuickReference);
            _history.SetSystemPrompt(sb.ToString());
        }

        private const string IAgentActionContract = @"## IAgentAction Contract
```csharp
public interface IAgentAction {
    string ActionName { get; }
    string Description { get; }
    ActionResult Execute(ActionContext context);
}
public class ActionContext {
    public GameObject TargetObject { get; set; }
    public GameObject CanvasRoot { get; set; }
}
public class ActionResult {
    public bool Success { get; set; }
    public string Message { get; set; }
    public static ActionResult Ok(string message);   // use for success
    public static ActionResult Fail(string message);  // use for failure
}
```";

        private const string ApiQuickReference = @"## API Quick Reference
### UICreator
- `CreateCanvas(string name, RenderMode mode = ScreenSpaceOverlay)` → GameObject
- `CreatePanel(string name, Transform parent, Color color)` → GameObject (Image)
- `CreateButton(string name, Transform parent, string text, Vector2 size)` → GameObject (Button+Text)
- `CreateText(string name, Transform parent, string content, int fontSize = 18, Color? color = null)` → GameObject (Text)
- `CreateImage(string name, Transform parent, string spritePath, Vector2 size)` → GameObject (Image)
- `CreateInputField(string name, Transform parent, string placeholder, Vector2 size)` → GameObject
- `CreateSlider(string name, Transform parent, float min, float max, float val)` → GameObject
- `CreateToggle(string name, Transform parent, string label, bool isOn)` → GameObject
- `CreateScrollView(string name, Transform parent, Vector2 size)` → GameObject

### AssetFinder
- `Find(string filter, int limit = 50)` → List<string> of asset paths (e.g. `Find(""t:Sprite icon"")`)
- `GetInfo(string path)` → string description

### RectTransformHelper
- `SetAnchorPreset(GameObject go, AnchorPreset preset)` — presets: TopLeft/TopCenter/TopRight/MiddleLeft/MiddleCenter/MiddleRight/BottomLeft/BottomCenter/BottomRight/StretchAll
- `SetSize(GameObject go, float w, float h)`
- `SetPosition(GameObject go, float x, float y)`

### ComponentHelper
- `SetImageColor(GameObject go, Color color)`
- `SetImageSprite(GameObject go, string path)`
- `SetText(GameObject go, string text)`
- `AddCanvasGroup(GameObject go, float alpha = 1, bool interact = true, bool block = true)`

### LayoutHelper
- `AddVerticalLayout(GameObject go, float spacing = 8, TextAnchor align = UpperCenter, RectOffset pad = null)` → VerticalLayoutGroup
- `AddHorizontalLayout(GameObject go, float spacing = 8, TextAnchor align = MiddleLeft, RectOffset pad = null)` → HorizontalLayoutGroup
- `AddGridLayout(GameObject go, Vector2 cellSize, Vector2 spacing)` → GridLayoutGroup
- `AddLayoutElement(GameObject go, float? minW = null, float? minH = null, float? prefW = null, float? prefH = null)` → LayoutElement

### PrefabHelper
- `Create(GameObject source, string path)` → string
- `Instantiate(string path, Transform parent = null, string name = null)` → GameObject
- `Apply(GameObject instance)` → bool

### SceneHelper
- `GetCurrentSceneName()` / `GetCurrentScenePath()` → string
- `SaveScene()` → bool / `MarkDirty()`
- `FindByTag(string tag)` / `FindByName(string name)` → GameObject[]

### HierarchyInspector
- `Describe(GameObject root, int maxDepth = 4)` → string
- `DescribeComponents(GameObject go)` → string";

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
            catch (Exception e)
            {
                var inner = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : e;
                var firstFrame = inner.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("Agent_"))?.Trim() ?? "";
                var detail = string.IsNullOrEmpty(firstFrame)
                    ? $"Exception: {inner.GetType().Name}: {inner.Message}"
                    : $"Exception: {inner.GetType().Name}: {inner.Message}\n  at {firstFrame}";
                ConsoleLogger.Error("Execution exception: " + detail);
                _txManager.RollbackLast();
                Retry(detail);
            }
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
            var hint = BuildErrorHint(feedback);
            var retryMsg = $"[ERROR] {feedback}\n\n{hint}[Retry {_retryCount}/{_settings.MaxRetryCount}] Fix the error above and output a corrected ```csharp code block implementing IAgentAction. Do NOT explain, just output the fixed code.";
            _history.AddToolResult(retryMsg);
            TransitionTo(AgentState.WaitingForLLM);
        }

        private static string BuildErrorHint(string feedback)
        {
            var sb = new StringBuilder();
            if (feedback.Contains("CS0738") || feedback.Contains("CS0535") || feedback.Contains("IAgentAction"))
            {
                sb.AppendLine("[HINT] IAgentAction requires: string ActionName { get; }, string Description { get; }, ActionResult Execute(ActionContext context).");
                sb.AppendLine("Return ActionResult.Ok(\"msg\") for success or ActionResult.Fail(\"msg\") for failure.");
                sb.AppendLine();
            }
            if (feedback.Contains("CS0117"))
            {
                if (feedback.Contains("AssetFinder"))
                    sb.AppendLine("[HINT] AssetFinder API: Find(string filter, int limit = 50) → List<string> paths. Example: AssetFinder.Find(\"t:Sprite\", 50)");
                if (feedback.Contains("RectTransformHelper"))
                    sb.AppendLine("[HINT] RectTransformHelper only has: SetAnchorPreset(go, AnchorPreset), SetSize(go, w, h), SetPosition(go, x, y). For anchors/pivot, use RectTransform directly.");
                if (feedback.Contains("ComponentHelper"))
                    sb.AppendLine("[HINT] ComponentHelper only has: SetImageColor(go, color), SetImageSprite(go, path), SetText(go, text), AddCanvasGroup(go, ...). For other properties, use GetComponent<T>() directly.");
                if (feedback.Contains("LayoutHelper"))
                    sb.AppendLine("[HINT] LayoutHelper: AddVerticalLayout(go, spacing, align, pad), AddHorizontalLayout(go, spacing, align, pad), AddGridLayout(go, cellSize, spacing), AddLayoutElement(go, minW, minH, prefW, prefH).");
                sb.AppendLine();
            }
            if (feedback.Contains("CS7036"))
            {
                if (feedback.Contains("CreatePanel"))
                    sb.AppendLine("[HINT] UICreator.CreatePanel(string name, Transform parent, Color color) — color is required.");
                if (feedback.Contains("CreateButton"))
                    sb.AppendLine("[HINT] UICreator.CreateButton(string name, Transform parent, string text, Vector2 size) — size is required.");
                if (feedback.Contains("CreateImage"))
                    sb.AppendLine("[HINT] UICreator.CreateImage(string name, Transform parent, string spritePath, Vector2 size) — spritePath and size are required.");
                sb.AppendLine();
            }
            if (feedback.Contains("CS1955") && feedback.Contains("ActionResult"))
            {
                sb.AppendLine("[HINT] Use ActionResult.Ok(\"message\") or ActionResult.Fail(\"message\"), not ActionResult.Success(...).");
                sb.AppendLine();
            }
            if (feedback.Contains("CS0120") && feedback.Contains("ActionResult"))
            {
                sb.AppendLine("[HINT] ActionResult.Success is an instance property. Use the static factory: ActionResult.Ok(\"message\") or ActionResult.Fail(\"message\").");
                sb.AppendLine();
            }
            if (feedback.Contains("CS8652") || feedback.Contains("CS8400") || feedback.Contains("CS8773")
                || feedback.Contains("CS8936") || feedback.Contains("feature"))
            {
                sb.AppendLine($"[HINT] This project targets C# {GetMaxLangVersion()}. Do NOT use features from higher C# versions.");
                sb.AppendLine("Forbidden: global using, file-scoped namespaces (namespace X;), record struct, raw string literals, required members, primary constructors, collection expressions.");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private void HandleError(string msg)
        {
            ConsoleLogger.Error(msg);
            _state = AgentState.Error;
            OnStateChanged?.Invoke(_state);
            OnError?.Invoke(msg);
        }

        internal static string GetMaxLangVersion()
        {
#if UNITY_2022_2_OR_NEWER
            return "9.0";
#elif UNITY_2021_2_OR_NEWER
            return "9.0";
#else
            return "8.0";
#endif
        }

        public void Dispose() { _cts?.Cancel(); _llm?.Dispose(); }
    }
}

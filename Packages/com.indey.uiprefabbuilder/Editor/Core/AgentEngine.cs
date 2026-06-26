using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Indexing;
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
            _toolRegistry.Rebuild();
            RebuildSystemPrompt();
        }

        public void Refresh()
        {
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

            // ── Identity ──
            sb.AppendLine("You are **UIPrefabBuilder**, a Unity Editor AI agent specialized in UGUI and UI Prefab workflows.");
            sb.AppendLine("You think step-by-step, verify your work visually, and self-correct when issues are found.");
            sb.AppendLine();

            // ── Thinking Protocol ──
            sb.AppendLine("## THINKING PROTOCOL");
            sb.AppendLine("Before each action, briefly plan what you will do and why.");
            sb.AppendLine("After each tool result, evaluate: did it succeed? do I need to adjust?");
            sb.AppendLine("Use `update_progress` to track completed steps and remaining work in complex tasks.");
            sb.AppendLine();

            // ── Phase-Based Workflow ──
            sb.AppendLine("## PHASE-BASED WORKFLOW");
            sb.AppendLine();
            sb.AppendLine("### Phase 1: Understand (MANDATORY)");
            sb.AppendLine("- `search_assets` to discover available sprites/assets (use `search_assets_glob` for filename patterns like `*popup*`)");
            sb.AppendLine("- `get_scene_overview` to understand current state");
            sb.AppendLine("- Plan the UI structure mentally before creating anything");
            sb.AppendLine();
            sb.AppendLine("### Phase 2: Build (BATCH-FIRST)");
            sb.AppendLine("- `create_batch` for ALL elements in one call (canvas, panels, images, texts, buttons)");
            sb.AppendLine("- `set_rect_transform_batch` to position/size/anchor ALL elements in one call");
            sb.AppendLine("- `set_image` for sprite assignment (use type=Sliced for 9-slice backgrounds)");
            sb.AppendLine("- `set_text_properties` for text styling (alignment, fontStyle, color, overflow). IMPORTANT: always include the `text` parameter to ensure text content is written.");
            sb.AppendLine("- `add_horizontal_layout` / `add_vertical_layout` for automatic child arrangement");
            sb.AppendLine("- Call multiple independent tools in the same step (parallel)");
            sb.AppendLine();
            sb.AppendLine("### Phase 3: Verify (MANDATORY)");
            if (_settings.SupportsVision)
            {
                sb.AppendLine("- Do NOT screenshot after every step. Only take screenshots when:");
                sb.AppendLine("  1) You suspect a layout problem, or 2) You are nearly done and want a check before delivery");
                sb.AppendLine("- `take_screenshot` → `analyze_screenshot` to visually inspect the Game View");
                sb.AppendLine("- Fix issues found, then take one more screenshot to confirm");
            }
            else
            {
                sb.AppendLine("- Visual screenshot analysis is NOT available with the current model.");
                sb.AppendLine("- `take_screenshot` only saves an image for the user to review — you cannot see it.");
                sb.AppendLine("- Rely on **structural self-inspection** instead:");
                sb.AppendLine("  - `inspect_hierarchy` with maxDepth to review the full tree");
                sb.AppendLine("  - `inspect_components` on key elements to verify RectTransform, Mask, ScrollRect, LayoutGroup, Image, Text. IMPORTANT: check that Text `text` property is NOT empty — if it is, call `set_text` to fix it.");
                sb.AppendLine("- Think carefully: are element sizes reasonable relative to their parents? Are anchors/pivots correct? Could a Mask be clipping content unexpectedly? Is LayoutGroup child sizing configured properly?");
                sb.AppendLine("- Fix structural issues, then re-inspect to confirm");
                sb.AppendLine("- Take ONE screenshot at the very end (saved for user review)");
            }
            sb.AppendLine();
            sb.AppendLine("### Phase 4: Finalize");
            sb.AppendLine("- `inspect_hierarchy` for structure verification");
            sb.AppendLine("- `save_as_prefab` if requested");
            sb.AppendLine("- Summarize what was created");
            sb.AppendLine();

            // ── Batch-First Rules ──
            sb.AppendLine("## BATCH-FIRST PRINCIPLE (CRITICAL)");
            sb.AppendLine("1. Creating 2+ elements → `create_batch`");
            sb.AppendLine("2. Positioning 2+ elements → `set_rect_transform_batch`");
            sb.AppendLine("3. Single element positioning → `set_rect_transform` (NOT separate set_anchor/set_size/set_position)");
            sb.AppendLine("4. Image config → `set_image` (NOT set_image_sprite)");
            sb.AppendLine("5. Text styling → `set_text_properties` (all in one call, ALWAYS pass `text` content to ensure TextMeshPro text is written)");
            sb.AppendLine("6. Generic property → `set_component_property` via reflection");
            sb.AppendLine();

            // ── Asset Search Guide ──
            sb.AppendLine("## ASSET SEARCH");
            sb.AppendLine("- `search_assets`: Unity type filter (e.g. `t:Sprite`, `t:Prefab`)");
            sb.AppendLine("- `search_assets_glob`: filename pattern matching (e.g. `*popup*`, `btn_*_lg*`, `*dialog*.png`)");
            sb.AppendLine("- Sprite naming conventions: `btn_*` for buttons, `dialog_*`/`panel_*` for backgrounds, `icon_*` for icons");

            // ── Asset Indexing Status ──
            if (_settings.EnableAssetIndexing)
            {
                var indexer = AssetIndexer.Instance;
                sb.AppendLine();
                sb.AppendLine("## VISUAL ASSET INDEX");
                if (indexer.IsReady && indexer.IndexedCount > 0)
                {
                    sb.AppendLine($"- Visual asset index is ACTIVE with {indexer.IndexedCount} indexed sprites.");
                    sb.AppendLine("- Use `match_sprite_by_image` to find sprites by visual similarity from a design mockup crop (base64 image).");
                    sb.AppendLine("- Use `search_sprites_by_text` to find sprites by text description (e.g. 'treasure chest', 'red button', 'gold coin'). This understands visual semantics, not just filenames.");
                    sb.AppendLine("- Text search is more powerful than filename search when asset names don't match the visual content.");
                    sb.AppendLine("- Use `get_index_status` to check index health.");
                }
                else
                {
                    sb.AppendLine("- Visual asset indexing is enabled but the index is not yet built.");
                    sb.AppendLine("- Use `rebuild_asset_index` to build the visual index before using `match_sprite_by_image` or `search_sprites_by_text`.");
                }
            }
            sb.AppendLine();

            // ── Error Recovery ──
            sb.AppendLine("## ERROR RECOVERY");
            sb.AppendLine("- Tool failure → read the error, try alternative approach");
            sb.AppendLine("- Target not found → `inspect_hierarchy` to find correct name/path");
            sb.AppendLine("- `create_batch` partial failure → handle failed items individually");
            sb.AppendLine("- Screenshot shows layout issues → identify and fix specific elements");
            sb.AppendLine("- `execute_code` fails 2+ times → switch to other tools");
            sb.AppendLine();

            // ── Target Resolution ──
            sb.AppendLine("## TARGET RESOLUTION");
            sb.AppendLine("- By name: `\"Title\"`");
            sb.AppendLine("- By path: `\"Canvas/Panel/Title\"`");
            sb.AppendLine("- By instanceId: `\"#12345\"`");
            sb.AppendLine();

            // ── execute_code ──
            sb.AppendLine("## execute_code USAGE");
            sb.AppendLine("Compiles and runs C# at runtime. Code must implement `IAgentAction`.");
            sb.AppendLine("If code does NOT contain `class` and `IAgentAction`, it is auto-wrapped.");
            sb.AppendLine("Auto-wrapped example: `var canvas = GameObject.Find(\"Canvas\"); ...`");
            sb.AppendLine("Full class: implement `IAgentAction` with `Execute(ActionContext context)` returning `ActionResult.Ok(msg)` or `ActionResult.Fail(msg)`.");
            sb.AppendLine("APIs: UnityEngine, UnityEngine.UI, UnityEditor, System.Linq.");
            sb.AppendLine("Use for 10+ repetitive items (grid cells, slot arrays).");
            sb.AppendLine();

            // ── Safety ──
            sb.AppendLine("## SAFETY");
            sb.AppendLine("- Never delete files or assets. Never quit Unity.");
            sb.AppendLine();
            sb.AppendLine("When finished, respond with a brief summary of what you created.");

            // ── Project Config ──
            var projectConfig = Config.ProjectConfig.Current;
            if (projectConfig != null && projectConfig.HasAnyConfig())
            {
                sb.AppendLine();
                sb.Append(projectConfig.BuildPromptSection());
            }

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
            int consecutiveFailures = 0;
            var pendingImageParts = new List<ContentPart>();
            string pendingAnalysisPrompt = null;

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

                try
                {
                    var resultObj = JObject.Parse(result);
                    var success = resultObj["success"]?.Value<bool>() ?? true;

                    if (!success)
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures >= 3)
                        {
                            resultObj["_hint"] = "Multiple consecutive failures. Consider: " +
                                "1) Use inspect_hierarchy to verify element names/paths. " +
                                "2) Try a different approach. " +
                                "3) Use execute_code for complex operations.";
                            result = resultObj.ToString();
                        }
                    }
                    else
                    {
                        consecutiveFailures = 0;
                    }

                    if (resultObj["_multimodal"]?.Value<bool>() == true)
                    {
                        var images = resultObj["_images"] as JArray;
                        if (images != null)
                        {
                            foreach (var img in images)
                            {
                                pendingImageParts.Add(new ContentPart
                                {
                                    Type = "image_url",
                                    ImageUrl = new ImageUrl
                                    {
                                        Url = img.ToString(),
                                        Detail = "low"
                                    }
                                });
                            }
                        }
                        pendingAnalysisPrompt = resultObj["message"]?.ToString();

                        resultObj.Remove("_images");
                        resultObj.Remove("_multimodal");
                        result = resultObj.ToString();
                    }
                }
                catch { }

                ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                _history.AddToolResult(call.Id, call.Name, result);
                OnToolCall?.Invoke(call.Name, call.Arguments, result);
                ReportToolResult(call.Name, result);
            }

            if (pendingImageParts.Count > 0 && _settings.SupportsVision)
            {
                var parts = new List<ContentPart>();
                parts.Add(new ContentPart
                {
                    Type = "text",
                    Text = pendingAnalysisPrompt ?? "Please analyze the provided screenshot(s)."
                });
                parts.AddRange(pendingImageParts);
                _history.AddUserMultimodal(parts);
                ConsoleLogger.Log($"[Agent] Injected vision user message with {pendingImageParts.Count} image(s).");
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

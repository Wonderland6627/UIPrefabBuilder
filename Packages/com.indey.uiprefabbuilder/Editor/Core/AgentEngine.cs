using System;
using System.Collections.Generic;
using System.IO;
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
        private int _consecutiveEmptySearches;
        private int _cumulativeEmptySearches;
        private int _totalSearchSteps;
        private int _totalBuildSteps;
        private bool _searchBudgetWarningInjected;
        private int _postBuildSearchSteps;
        private bool _postBuildNudgeInjected;
        private readonly Dictionary<string, string> _searchCache = new Dictionary<string, string>();
        private const int HardCap = 500;
        private const int MaxConsecutiveEmptySearches = 6;
        private const int MaxCumulativeEmptySearches = 10;
        private const int SearchBudgetSoft = 10;
        private const int SearchBudgetHard = 25;
        private const int PostBuildSearchBudget = 3;
        private string _designImagePath;
        private string _designImageAssetPath;

        // Camera manipulation guard: block execute_code calls that try to modify camera settings
        private int _consecutiveCameraAttempts;
        private static readonly string[] CameraBlockedKeywords = {
            "Camera.main", "clearFlags", "cullingMask",
            "Camera.orthographic", "Camera.depth", "Camera.orthographicSize",
            "camera.clearFlags", "camera.cullingMask", "camera.orthographic",
            "camera.depth", "camera.orthographicSize"
        };

        // Consecutive single-search detection: nudge agent to use batch tools
        private int _consecutiveSingleSearchCalls;
        private const int MaxConsecutiveSingleSearchBeforeNudge = 3;
        private bool _batchSearchNudgeInjected;

        // execute_code dedup: detect repeated similar code submissions
        private readonly List<string> _recentExecuteCodes = new List<string>();
        private int _consecutiveSimilarCodeCount;
        private const int MaxConsecutiveSimilarCode = 3;

        // Post-analyze_screenshot guardrail: detect execute_code spirals after screenshot analysis
        private bool _lastStepWasAnalyzeScreenshot;
        private int _postAnalyzeExecuteCodeCount;
        private const int MaxPostAnalyzeExecuteCode = 5;

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

            _pendingMessage = msg;
            ResetStepCounters();
            RebuildSystemPrompt();

            var effectiveMax = EffectiveMaxSteps;
            ConsoleLogger.Log($"=== NEW TASK ===");
            ConsoleLogger.Log($"[Config] model={_settings.ModelName}, baseUrl={_settings.BaseUrl}, timeout={_settings.RequestTimeoutSeconds}s, maxSteps={(_settings.MaxAgentSteps <= 0 ? "unlimited" : _settings.MaxAgentSteps.ToString())} (effective={effectiveMax})");
            ConsoleLogger.Log($"[Config] tools={_toolRegistry.Definitions.Count}");
            ConsoleLogger.LogBlock("SYSTEM_PROMPT", _history.Messages.Count > 0 && _history.Messages[0].Role == ChatRole.System ? _history.Messages[0].Content : "(none)");
            ConsoleLogger.LogBlock("USER_REQUEST", msg);

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _history.AddUser(_pendingMessage);
            _txManager.Begin("AI Agent Task");
            TransitionTo(AgentState.Thinking);
        }

        public void StartTask(string msg, List<string> imagePaths)
        {
            if (imagePaths == null || imagePaths.Count == 0)
            {
                StartTask(msg);
                return;
            }

            if (_state != AgentState.Idle && _state != AgentState.Error
                && _state != AgentState.WaitingConfirmation && _state != AgentState.Completed)
                return;

            var textContent = string.IsNullOrWhiteSpace(msg)
                ? "Please analyze the attached design mockup(s) and build the UI."
                : msg;

            _pendingMessage = textContent;
            ResetStepCounters();
            RebuildSystemPrompt();

            var effectiveMax = EffectiveMaxSteps;
            ConsoleLogger.Log($"=== NEW TASK (with {imagePaths.Count} image(s)) ===");
            ConsoleLogger.Log($"[Config] model={_settings.ModelName}, baseUrl={_settings.BaseUrl}, timeout={_settings.RequestTimeoutSeconds}s, maxSteps={(_settings.MaxAgentSteps <= 0 ? "unlimited" : _settings.MaxAgentSteps.ToString())} (effective={effectiveMax})");
            ConsoleLogger.Log($"[Config] tools={_toolRegistry.Definitions.Count}");
            ConsoleLogger.LogBlock("SYSTEM_PROMPT", _history.Messages.Count > 0 && _history.Messages[0].Role == ChatRole.System ? _history.Messages[0].Content : "(none)");
            ConsoleLogger.LogBlock("USER_REQUEST", textContent + $"\n[Attached: {imagePaths.Count} image(s)]");

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var parts = new List<ContentPart>
            {
                new ContentPart { Type = "text", Text = textContent }
            };
            foreach (var path in imagePaths)
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var b64 = Convert.ToBase64String(bytes);
                    var mime = GetMimeType(Path.GetExtension(path));
                    parts.Add(new ContentPart
                    {
                        Type = "image_url",
                        ImageUrl = new ImageUrl
                        {
                            Url = $"data:{mime};base64,{b64}",
                            Detail = "high"
                        }
                    });
                    ConsoleLogger.Log($"[Agent] Attached image: {Path.GetFileName(path)} ({bytes.Length / 1024}KB)");
                }
                catch (Exception e)
                {
                    ConsoleLogger.Error($"[Agent] Failed to read image {path}: {e.Message}");
                }
            }

            _designImagePath = imagePaths.Count > 0 ? imagePaths[0] : null;
            _designImageAssetPath = EnsureDesignImageAssetPath(_designImagePath);
            DesignImageContext.CurrentAssetPath = _designImageAssetPath;
            _history.AddUserMultimodal(parts, pinImage: true);
            _txManager.Begin("AI Agent Task");
            TransitionTo(AgentState.Thinking);
        }

        /// <summary>
        /// Ensures the raw design mockup image (which may live outside Assets/, e.g. a temp
        /// clipboard/attachment path) is copied into Assets/Screenshots/ so it has a stable
        /// Assets-relative path that both `analyze_screenshot` (referenceImagePath) and the
        /// new region-based visual matching tools (crop_design_image, match_sprite_by_region)
        /// can read from. Returns the Assets-relative path, or null if there is no image.
        /// </summary>
        private string EnsureDesignImageAssetPath(string rawImagePath)
        {
            if (string.IsNullOrEmpty(rawImagePath)) return null;

            try
            {
                var projectRoot = Path.GetDirectoryName(Application.dataPath);
                var normalized = rawImagePath.Replace("\\", "/");
                var normalizedRoot = projectRoot?.Replace("\\", "/") ?? "";

                if (normalized.StartsWith(normalizedRoot) && normalized.Contains("/Assets/"))
                {
                    var assetsIdx = normalized.IndexOf("/Assets/", StringComparison.Ordinal);
                    return normalized.Substring(assetsIdx + 1);
                }

                var screenshotDir = "Assets/Screenshots";
                var destName = "design_reference_" + Path.GetFileName(rawImagePath);
                var destPath = Path.Combine(projectRoot, screenshotDir, destName);
                if (!File.Exists(destPath) && File.Exists(rawImagePath))
                {
                    var dir = Path.GetDirectoryName(destPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.Copy(rawImagePath, destPath, true);
                    AssetDatabase.Refresh();
                }
                return screenshotDir + "/" + destName;
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"[Agent] Failed to stage design reference image: {e.Message}");
                return null;
            }
        }

        private static string GetMimeType(string ext)
        {
            switch (ext?.ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                case ".bmp": return "image/bmp";
                default: return "image/png";
            }
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

            {
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
                sb.AppendLine("- `search_assets` to discover available sprites/assets (use `search_assets_glob` / `search_assets_glob_batch` for filename patterns like `*popup*`)");
                if (_settings.EnableAssetIndexing)
                {
                    var indexerForPhase = AssetIndexer.Instance;
                    if (indexerForPhase.IsReady && indexerForPhase.IndexedCount > 0)
                    {
                        sb.AppendLine("- `search_sprites_by_text_batch` to find ALL needed sprites in one call (e.g. coin icon, button bg, lock icon — batch them together)");
                        sb.AppendLine("- `search_sprites_by_text` for a single sprite lookup only");
                    }
                }
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
                    sb.AppendLine("- When the user provides a design mockup, use `analyze_screenshot` with `referenceImagePath` to compare side-by-side");
                    sb.AppendLine("- Fix issues found, then take one more screenshot to confirm");
                    sb.AppendLine("- `take_screenshot` automatically ensures a Camera and EventSystem exist in the scene — NEVER write `execute_code` to create/fix cameras, Canvas render mode, clearFlags, or cullingMask. If a screenshot still looks wrong, the cause is UI layout (RectTransform/anchors/Canvas Scaler design resolution) or missing sprites, NOT the render pipeline — use `inspect_hierarchy`/`inspect_components` to debug instead of repeated camera experiments.");
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
                sb.AppendLine("0a. Searching 2+ sprites by text → `search_sprites_by_text_batch` (NOT multiple search_sprites_by_text calls)");
                sb.AppendLine("0b. Searching 2+ filename patterns → `search_assets_glob_batch` (NOT multiple search_assets_glob calls)");
                sb.AppendLine("1. Creating 2+ elements → `create_batch`");
                sb.AppendLine("2. Positioning 2+ elements → `set_rect_transform_batch`");
                sb.AppendLine("3. Single element positioning → `set_rect_transform` (NOT separate set_anchor/set_size/set_position)");
                sb.AppendLine("4. Styling 2+ Images → `set_image_batch` (sprite, type, color in one call)");
                sb.AppendLine("5. Styling 1 Image → `set_image` (NOT set_image_sprite)");
                sb.AppendLine("6. Styling 2+ Texts → `set_text_properties_batch` (text, fontSize, color in one call. ALWAYS pass `text` content)");
                sb.AppendLine("7. Styling 1 Text → `set_text_properties` (ALWAYS pass `text` content)");
                sb.AppendLine("8. Adding 2+ Outline/Shadow → `add_outline_batch`");
                sb.AppendLine("9. Configuring 2+ LayoutElements → `add_layout_element_batch`");
                sb.AppendLine("10. Setting 2+ component properties → `set_component_property_batch`");
                sb.AppendLine("11. Generic single property → `set_component_property` via reflection");
                sb.AppendLine("12. Destroying 2+ objects → `destroy_object_batch` (NOT multiple destroy_object calls)");
                sb.AppendLine("13. Inspecting hierarchy of 2+ roots → `inspect_hierarchy_batch` (NOT multiple inspect_hierarchy calls)");
                sb.AppendLine("14. Inspecting components of 2+ objects → `inspect_components_batch` (NOT multiple inspect_components calls)");
                sb.AppendLine();

                // ── Asset Search Guide ──
                sb.AppendLine("## ASSET SEARCH");
                sb.AppendLine("- `search_assets`: Unity type filter (e.g. `t:Sprite`, `t:Prefab`)");
                sb.AppendLine("- `search_assets_glob`: filename pattern matching (e.g. `*popup*`, `btn_*_lg*`, `*dialog*.png`)");
                sb.AppendLine("- `search_assets_glob_batch`: search multiple filename patterns at once (e.g. `['*popup*', '*chest*', '*arrow*']`)");
                sb.AppendLine("- Sprite naming conventions: `btn_*` for buttons, `dialog_*`/`panel_*` for backgrounds, `icon_*` for icons");
                sb.AppendLine("- **CRITICAL SEARCH RULES**:");
                sb.AppendLine("  1. If a search returns 0 results, the asset does NOT exist. Do NOT try alternative keywords. Use a plain Image with color tint as fallback and move on IMMEDIATELY.");
                sb.AppendLine("  2. NEVER search the same pattern twice. Results are cached — repeated searches waste steps.");
                sb.AppendLine("  3. After you start building (create_batch), do NOT go back to searching. Use the assets you already found.");
                sb.AppendLine("  4. Searches are HARD-LIMITED. Excessive searching will be blocked by the system.");

                // ── Asset Indexing Status ──
                if (_settings.EnableAssetIndexing)
                {
                    var indexer = AssetIndexer.Instance;
                    sb.AppendLine();
                    sb.AppendLine("## VISUAL ASSET INDEX");
                    if (indexer.IsReady && indexer.IndexedCount > 0)
                    {
                        sb.AppendLine($"- Visual asset index is ACTIVE with {indexer.IndexedCount} indexed sprites.");
                        sb.AppendLine("- **PREFER** visual index tools over filename-based search when:");
                        sb.AppendLine("  - User provides a design mockup/screenshot → use `match_sprite_by_region_batch` (or `match_sprite_by_region` for a single element)");
                        sb.AppendLine("  - You need to find sprites by visual appearance from a text description → use `search_sprites_by_text`");
                        sb.AppendLine("  - You want to understand what sprites look like → use `search_sprites_by_text`");
                        sb.AppendLine("- `match_sprite_by_region_batch`: give normalized bounding boxes (x, y, width, height in 0-1, top-left origin) directly on the ATTACHED design mockup image — no need to crop or encode images yourself. The tool crops the region server-side and matches it by visual similarity (CLIP embedding) against the indexed sprites. **ALWAYS prefer this over multiple single calls** when matching 2+ regions.");
                        sb.AppendLine("- **When a design mockup is attached/available in this session, call `match_sprite_by_region_batch` FIRST for every visually distinct element (backgrounds, buttons, checkboxes, icons) BEFORE falling back to `search_sprites_by_text_batch`.** Text-only search often returns a sprite that merely matches the description in words but looks visually wrong (e.g. a round checkbox instead of a square one) — actual pixel/CLIP matching against the mockup avoids that.");
                        sb.AppendLine("- `match_sprite_by_region`: same as above for a single bounding box.");
                        sb.AppendLine("- `crop_design_image`: optional — crop and preview a region of the design mockup (returns the cropped image to you) if you want to double-check a bounding box before matching.");
                        sb.AppendLine("- `search_sprites_by_text_batch`: find multiple sprites at once by text descriptions. **ALWAYS prefer this over multiple single calls.** Example: `{\"queries\":[{\"query\":\"gold coin\"},{\"query\":\"red button\"},{\"query\":\"lock icon\"}]}`");
                        sb.AppendLine("- `search_sprites_by_text`: find a single sprite by text description. Use only when searching for exactly one sprite.");
                        sb.AppendLine("- Fall back to `search_assets`/`search_assets_glob` only for exact filename-based lookups.");
                        sb.AppendLine("- Use `get_index_status` to check index health.");
                        sb.AppendLine("- **`lowConfidence: true` / `_hint` on a match result means the best candidate probably does NOT visually resemble the target** (it only passed the minimum filter, not a real similarity bar). Do NOT silently apply it as if it were correct — use a plain color-tinted Image instead and explicitly say in your final summary that no confident sprite was found for that element, so the user knows to review/supply one.");
                    }
                    else
                    {
                        sb.AppendLine("- Visual asset indexing is enabled but the index is not yet built.");
                        sb.AppendLine("- Use `rebuild_asset_index` to build the visual index before using `match_sprite_by_image` or `search_sprites_by_text`.");
                    }
                }
                // ── Design Mockup Analysis ──
                if (_settings.SupportsVision)
                {
                    sb.AppendLine();
                    sb.AppendLine("## DESIGN MOCKUP ANALYSIS");
                    sb.AppendLine("When the user provides a design mockup image:");
                    sb.AppendLine("1. **Analyze Structure**: Identify the UI hierarchy — panels, buttons, text labels, icons, backgrounds, decorations.");
                    sb.AppendLine("2. **Describe Elements**: For each UI element, describe its visual appearance (color, shape, size), approximate position relative to the canvas, and functional role.");
                    sb.AppendLine("3. **Match Assets**: Use the visual asset index to find matching local sprites:");
                    if (_settings.EnableAssetIndexing)
                    {
                        var idxForMockup = AssetIndexer.Instance;
                        if (idxForMockup.IsReady && idxForMockup.IndexedCount > 0)
                        {
                            sb.AppendLine("   - **PREFERRED for precise matching**: `match_sprite_by_region_batch` with the normalized bounding box (0-1, top-left origin) of EACH element on the mockup — this compares actual pixels via CLIP image similarity instead of guessing from a text description.");
                            sb.AppendLine("   - **Alternative**: `search_sprites_by_text_batch` with ALL visual descriptions at once (e.g. 'green ribbon header', 'gray stone button', 'gold coin icon' — batch them in one call) when a bounding box is impractical.");
                        }
                        else
                        {
                            sb.AppendLine("   - Visual index is not ready. Use `search_assets_glob` with filename patterns instead.");
                        }
                    }
                    else
                    {
                        sb.AppendLine("   - Use `search_assets` / `search_assets_glob` to find sprites by filename patterns.");
                    }
                    sb.AppendLine("   - Fall back to `search_assets_glob` if no good match is found in the visual index.");
                    sb.AppendLine("4. **Build UI**: Use `create_batch` and positioning tools to reconstruct the design as a Unity UGUI hierarchy.");
                    sb.AppendLine("5. **Verify**: `take_screenshot` → `analyze_screenshot` (with `referenceImagePath` pointing to the user's mockup) to compare side-by-side. Fix discrepancies.");
                    sb.AppendLine();

                    sb.AppendLine("## VISUAL FIDELITY CHECKLIST");
                    sb.AppendLine("When reproducing a design mockup, carefully observe the design and verify the following before delivery:");
                    sb.AppendLine("1. **Background layers**: Observe whether the design has distinct background layers (popup frame, header banner, tab bar, list area). If so, each must be a separate Image with the correct sprite — do NOT skip any visible background.");
                    sb.AppendLine("2. **Title/Header bar**: If the design shows a colored banner or decorative bar behind the title text, create a separate Image element for it — do not render it as plain text only.");
                    sb.AppendLine("3. **Tab/Button states**: If the design has tabs or toggle buttons, observe the visual difference between selected and unselected states (color, shape, brightness) and reproduce them with DIFFERENT sprites or tint colors.");
                    sb.AppendLine("4. **Text styling**: Observe the design's text effects (outline, shadow, glow) and reproduce them using TMPro settings. Match font colors from the mockup.");
                    sb.AppendLine("5. **Spacing & proportions**: Estimate element sizes relative to the popup width. Verify that padding, margins, and gaps between items visually match the design.");
                    sb.AppendLine("6. **Icon sizes**: If the design shows a list with icons, ensure uniform icon size and vertical centering with labels.");
                    sb.AppendLine("7. **Close button**: Place the close button in the same relative position as the design (inside/outside/below the popup).");
                    sb.AppendLine("8. **Overlay/Dimming**: If the design shows a semi-transparent dark overlay behind the popup, add one. If not, skip it.");
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
            sb.AppendLine("Compiles and runs C# at runtime. Two modes:");
            sb.AppendLine("### Mode 1: Auto-wrapped (PREFERRED for simple scripts)");
            sb.AppendLine("Just write plain C# statements. Do NOT use bare `return;` — it will fail. Simply let the code run to the end.");
            sb.AppendLine("Example: `var canvas = GameObject.Find(\"Canvas\"); canvas.SetActive(true);`");
            sb.AppendLine("### Mode 2: Full class (for complex logic)");
            sb.AppendLine("Implement `IAgentAction` with ALL 3 required members:");
            sb.AppendLine("```");
            sb.AppendLine("public class MyAction : IAgentAction {");
            sb.AppendLine("  public string ActionName => \"MyAction\";");
            sb.AppendLine("  public string Description => \"What it does\";");
            sb.AppendLine("  public ActionResult Execute(ActionContext context) {");
            sb.AppendLine("    // your code here");
            sb.AppendLine("    return ActionResult.Ok(\"Done.\");");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine("```");
            sb.AppendLine("**IMPORTANT**: If you use Mode 2, you MUST include `ActionName` and `Description` properties, or it will fail to compile.");
            sb.AppendLine("**ActionResult API**: `ActionResult.Ok(\"message\")` for success, `ActionResult.Fail(\"message\")` for failure. Do NOT use `ActionResult.Error()` — it does not exist.");
            sb.AppendLine("APIs: UnityEngine, UnityEngine.UI, UnityEditor, System.Linq, TMPro.");
            sb.AppendLine("Use for 10+ repetitive items (grid cells, slot arrays).");
            sb.AppendLine("**Common compile pitfalls**: a bare `Object` identifier is ambiguous (`CS0104`) once both `System` and `UnityEngine` are in scope — use `GameObject`, `Component`, or the full `UnityEngine.Object` instead. " +
                "For text/graphic outline or shadow effects, prefer the `add_outline`/`add_outline_batch` tools instead of hand-writing `Outline`/`Shadow` field access.");
            sb.AppendLine();

            // ── Safety ──
            sb.AppendLine("## SAFETY");
            sb.AppendLine("- Never delete files or assets. Never quit Unity.");
            sb.AppendLine("- **Prefab protection (CRITICAL)**: you may only modify GameObject instances that live in the scene Hierarchy. You must NEVER overwrite an existing prefab asset or push instance overrides back into a prefab asset in the Project:");
            sb.AppendLine("  - `save_as_prefab` only creates a brand-new prefab and will refuse if the path already exists — never try to work around this by deleting the existing asset first.");
            sb.AppendLine("  - Do NOT use `apply_prefab` or any `execute_code` calling `PrefabUtility.ApplyPrefabInstance` / `PrefabUtility.SaveAsPrefabAssetAndConnect` on an existing path — these write to the Project's prefab asset, not just the scene instance, and are forbidden.");
            sb.AppendLine("  - If a task is read-only/analysis-only (e.g. \"分析这个设计稿\", \"这个UI是什么结构\"), do NOT create/build/save/modify any GameObject or asset — just answer with your analysis and stop. " +
                "This does NOT mean skip tool calls entirely: you SHOULD still use non-destructive search-only tools (`match_sprite_by_region_batch`, `search_sprites_by_text_batch`, `get_project_config`, `search_assets_glob`, etc.) as part of a thorough analysis — identifying the ACTUAL matching sprite asset paths for each element (not just describing them in words) makes your analysis far more useful and lets a later \"now build it\" turn reuse those exact matches instead of re-guessing.");
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

        private void ResetStepCounters()
        {
            _currentStep = 0;
            _consecutiveEmptySearches = 0;
            _cumulativeEmptySearches = 0;
            _totalSearchSteps = 0;
            _totalBuildSteps = 0;
            _searchBudgetWarningInjected = false;
            _postBuildSearchSteps = 0;
            _postBuildNudgeInjected = false;
            _searchCache.Clear();
            _consecutiveCameraAttempts = 0;
            _consecutiveSingleSearchCalls = 0;
            _batchSearchNudgeInjected = false;
            _recentExecuteCodes.Clear();
            _consecutiveSimilarCodeCount = 0;
            _lastStepWasAnalyzeScreenshot = false;
            _postAnalyzeExecuteCodeCount = 0;
            // Design mockup reference is scoped to a single task/turn: it is set (if images are
            // attached) in StartTask(msg, imagePaths) right after this reset, so clearing it here
            // means a follow-up turn that doesn't re-attach an image simply has no reference image.
            _designImagePath = null;
            _designImageAssetPath = null;
            DesignImageContext.Clear();
            LatestCode = "";
            LatestThinking = "";
        }

        private static bool IsCameraManipulation(string code)
        {
            if (string.IsNullOrEmpty(code)) return false;
            foreach (var kw in CameraBlockedKeywords)
                if (code.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static bool IsSingleSearchTool(string name)
        {
            return name == "search_assets" || name == "search_assets_glob"
                || name == "search_sprites_by_text" || name == "get_asset_info";
        }

        /// <summary>
        /// Rough similarity check: ratio of shared characters (order-independent) to max length.
        /// Good enough to detect repeated camera/debug snippets without pulling in a full edit-distance lib.
        /// </summary>
        private static float RoughSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0f;
            int matched = 0;
            var pool = new List<char>(b);
            foreach (var c in a)
            {
                int idx = pool.IndexOf(c);
                if (idx >= 0) { matched++; pool.RemoveAt(idx); }
            }
            return (float)matched / Math.Max(a.Length, b.Length);
        }

        private static bool IsSearchOnlyTool(string name)
        {
            return name == "search_assets" || name == "search_assets_glob" || name == "search_assets_glob_batch"
                || name == "search_sprites_by_text" || name == "search_sprites_by_text_batch"
                || name == "match_sprite_by_image" || name == "match_sprite_by_region" || name == "match_sprite_by_region_batch"
                || name == "crop_design_image"
                || name == "get_project_config" || name == "get_scene_overview"
                || name == "get_index_status" || name == "inspect_hierarchy" || name == "inspect_hierarchy_batch"
                || name == "inspect_components" || name == "inspect_components_batch";
        }

        private static bool IsBuildTool(string name)
        {
            return name == "create_batch" || name == "create_element"
                || name == "set_rect_transform" || name == "set_rect_transform_batch"
                || name == "set_image" || name == "set_image_batch"
                || name == "set_text_properties" || name == "set_text_properties_batch"
                || name == "set_text" || name == "add_horizontal_layout"
                || name == "add_vertical_layout" || name == "instantiate_prefab"
                || name == "set_component_property" || name == "set_component_property_batch"
                || name == "add_outline" || name == "add_outline_batch"
                || name == "add_layout_element" || name == "add_layout_element_batch"
                || name == "execute_code" || name == "save_as_prefab";
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

            var toolDefs = _toolRegistry.Definitions;
            _llm.ChatWithToolsAsync(
                _history.Messages,
                toolDefs,
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

                // NOTE: We intentionally do NOT force the model to start building just because it
                // searched first and then replied with text only. Not every task is a "build" task —
                // e.g. "分析此UI设计稿的结构" is a read-only analysis request, and forcing a build in
                // that case caused the agent to fabricate unwanted scene changes and even overwrite an
                // existing prefab asset. Trust the model's own judgement about whether the task is done.
                OnComplete?.Invoke(response.Content);
                ConsoleLogger.Log($"[Agent] Task completed in {_currentStep} steps. (search={_totalSearchSteps}, build={_totalBuildSteps})");
                TransitionTo(AgentState.Completed);
            }
        }

        private void ExecuteToolCalls(List<ToolCallInfo> toolCalls)
        {
            int consecutiveFailures = 0;
            var pendingImageParts = new List<ContentPart>();
            string pendingAnalysisPrompt = null;

            bool stepHasBuildTool = false;
            bool stepHasSearchOnly = true;

            foreach (var call in toolCalls)
            {
                if (_cts.IsCancellationRequested) return;

                if (IsBuildTool(call.Name))
                {
                    stepHasBuildTool = true;
                    stepHasSearchOnly = false;
                }
                else if (!IsSearchOnlyTool(call.Name))
                {
                    stepHasSearchOnly = false;
                }

                OnToolCall?.Invoke(call.Name, call.Arguments, null);

                string result;
                bool isSearchTool = call.Name == "search_assets" || call.Name == "search_assets_glob" || call.Name == "search_assets_glob_batch"
                    || call.Name == "search_sprites_by_text" || call.Name == "search_sprites_by_text_batch";

                if (isSearchTool)
                {
                    var cacheKey = $"{call.Name}:{call.Arguments}";
                    if (_searchCache.TryGetValue(cacheKey, out var cached))
                    {
                        result = cached;
                        try
                        {
                            var cachedObj = JObject.Parse(result);
                            cachedObj["_hint"] = "DUPLICATE SEARCH: You already searched this exact query earlier and got the same results. Do NOT search again. Use the results you have or move on.";
                            result = cachedObj.ToString();
                        }
                        catch { }
                        ConsoleLogger.Log($"[Agent] Search cache hit: {call.Name}({call.Arguments?.Substring(0, Math.Min(call.Arguments?.Length ?? 0, 60))})");
                        ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                        _history.AddToolResult(call.Id, call.Name, result);
                        OnToolCall?.Invoke(call.Name, call.Arguments, result);
                        ReportToolResult(call.Name, result);
                        continue;
                    }

                    if (_totalBuildSteps > 0 && _postBuildSearchSteps >= PostBuildSearchBudget)
                    {
                        result = new JObject
                        {
                            ["success"] = false,
                            ["error"] = "SEARCH BLOCKED: Post-build search budget exhausted. You have already built UI elements. " +
                                "Stop searching and continue styling with set_image_batch / set_text_properties_batch. " +
                                "For missing assets, use a plain Image with color tint as fallback."
                        }.ToString();
                        ConsoleLogger.Log($"[Agent] Search hard-blocked (post-build budget exceeded): {call.Name}");
                        ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                        _history.AddToolResult(call.Id, call.Name, result);
                        OnToolCall?.Invoke(call.Name, call.Arguments, result);
                        ReportToolResult(call.Name, result);
                        continue;
                    }

                    if (_cumulativeEmptySearches >= MaxCumulativeEmptySearches)
                    {
                        result = new JObject
                        {
                            ["success"] = false,
                            ["error"] = "SEARCH BLOCKED: Too many empty searches (" + _cumulativeEmptySearches + " total). " +
                                "The assets you are looking for do not exist. Use fallback colors and move on to building/styling."
                        }.ToString();
                        ConsoleLogger.Log($"[Agent] Search hard-blocked (cumulative empty limit): {call.Name}");
                        ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                        _history.AddToolResult(call.Id, call.Name, result);
                        OnToolCall?.Invoke(call.Name, call.Arguments, result);
                        ReportToolResult(call.Name, result);
                        continue;
                    }
                }

                // --- Camera manipulation guard for execute_code ---
                if (call.Name == "execute_code")
                {
                    string codeContent = null;
                    try { codeContent = JObject.Parse(call.Arguments ?? "{}")["code"]?.ToString(); }
                    catch { }

                    if (IsCameraManipulation(codeContent))
                    {
                        _consecutiveCameraAttempts++;
                        var blockMsg = "BLOCKED: Modifying Camera settings via execute_code is not allowed. " +
                            "The take_screenshot tool already manages the camera automatically. " +
                            "If the screenshot looks wrong, debug the UI layout instead — use inspect_hierarchy / inspect_components " +
                            "to check RectTransform anchors, sizes, and Canvas Scaler settings.";
                        if (_consecutiveCameraAttempts >= 2)
                            blockMsg += " You have attempted camera manipulation " + _consecutiveCameraAttempts +
                                " times. STOP trying to fix the camera and focus on UI layout issues.";
                        result = new JObject { ["success"] = false, ["error"] = blockMsg }.ToString();
                        ConsoleLogger.Log($"[Agent] Camera manipulation blocked (attempt {_consecutiveCameraAttempts}): {call.Name}");
                        ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                        _history.AddToolResult(call.Id, call.Name, result);
                        OnToolCall?.Invoke(call.Name, call.Arguments, result);
                        ReportToolResult(call.Name, result);
                        continue;
                    }

                    // --- execute_code dedup: detect repeated similar code ---
                    if (!string.IsNullOrEmpty(codeContent) && _recentExecuteCodes.Count > 0)
                    {
                        var lastCode = _recentExecuteCodes[_recentExecuteCodes.Count - 1];
                        if (RoughSimilarity(codeContent, lastCode) > 0.8f)
                        {
                            _consecutiveSimilarCodeCount++;
                            if (_consecutiveSimilarCodeCount >= MaxConsecutiveSimilarCode)
                            {
                                result = new JObject
                                {
                                    ["success"] = false,
                                    ["error"] = "BLOCKED: You have submitted " + _consecutiveSimilarCodeCount +
                                        " very similar execute_code calls in a row. This approach is not working. " +
                                        "Try a completely different strategy — use set_rect_transform_batch, set_image_batch, " +
                                        "inspect_hierarchy, or other specialized tools instead."
                                }.ToString();
                                ConsoleLogger.Log($"[Agent] Repeated execute_code blocked (similarity={_consecutiveSimilarCodeCount})");
                                ConsoleLogger.LogToolCall(call.Name, call.Arguments, result);
                                _history.AddToolResult(call.Id, call.Name, result);
                                OnToolCall?.Invoke(call.Name, call.Arguments, result);
                                ReportToolResult(call.Name, result);
                                continue;
                            }
                        }
                        else
                        {
                            _consecutiveSimilarCodeCount = 0;
                        }
                    }
                    if (!string.IsNullOrEmpty(codeContent))
                        _recentExecuteCodes.Add(codeContent);
                }
                else
                {
                    _consecutiveCameraAttempts = 0;
                    _consecutiveSimilarCodeCount = 0;
                }

                // --- Track consecutive single-search calls (non-batch) ---
                if (IsSingleSearchTool(call.Name))
                {
                    _consecutiveSingleSearchCalls++;
                }
                else
                {
                    _consecutiveSingleSearchCalls = 0;
                }

                var execArgs = call.Arguments;
                if (call.Name == "analyze_screenshot" && !string.IsNullOrEmpty(_designImageAssetPath))
                {
                    try
                    {
                        var argsObj = JObject.Parse(execArgs ?? "{}");
                        if (string.IsNullOrEmpty(argsObj["referenceImagePath"]?.ToString()))
                        {
                            argsObj["referenceImagePath"] = _designImageAssetPath;
                            execArgs = argsObj.ToString();
                            ConsoleLogger.Log($"[Agent] Auto-injected design reference image: {_designImageAssetPath}");
                        }
                    }
                    catch { }
                }

                try
                {
                    result = _toolRegistry.Execute(call.Name, execArgs);
                }
                catch (Exception e)
                {
                    result = new JObject { ["success"] = false, ["error"] = e.Message }.ToString();
                    ConsoleLogger.Error($"[Agent] Tool exception: {call.Name} -> {e.Message}");
                }

                if (isSearchTool)
                {
                    var cacheKey = $"{call.Name}:{call.Arguments}";
                    _searchCache[cacheKey] = result;
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

                    bool isSearchToolForHint = call.Name == "search_assets" || call.Name == "search_assets_glob" || call.Name == "search_assets_glob_batch"
                        || call.Name == "search_sprites_by_text" || call.Name == "search_sprites_by_text_batch";
                    if (isSearchToolForHint)
                    {
                        int count = resultObj["count"]?.Value<int>() ?? -1;
                        if (count == 0)
                        {
                            _consecutiveEmptySearches++;
                            _cumulativeEmptySearches++;
                            if (_consecutiveEmptySearches >= MaxConsecutiveEmptySearches)
                            {
                                resultObj["_hint"] = "STOP searching. You have made " + _consecutiveEmptySearches +
                                    " consecutive searches with 0 results (total empty: " + _cumulativeEmptySearches + "). " +
                                    "The asset does not exist in this project. " +
                                    "Use a plain Image with color tint as fallback, or skip this element and move on to building the UI.";
                                result = resultObj.ToString();
                            }
                        }
                        else if (count > 0)
                        {
                            _consecutiveEmptySearches = 0;
                        }
                    }
                    else
                    {
                        _consecutiveEmptySearches = 0;
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

            if (stepHasBuildTool)
            {
                _totalBuildSteps++;
                _postBuildSearchSteps = 0;
                _postBuildNudgeInjected = false;
            }
            if (stepHasSearchOnly)
            {
                _totalSearchSteps++;
                if (_totalBuildSteps > 0)
                    _postBuildSearchSteps++;
            }

            // --- Post-analyze_screenshot guardrail ---
            bool stepHasAnalyze = false;
            bool stepHasExecuteCode = false;
            foreach (var call in toolCalls)
            {
                if (call.Name == "analyze_screenshot") stepHasAnalyze = true;
                if (call.Name == "execute_code") stepHasExecuteCode = true;
            }
            if (stepHasAnalyze)
            {
                _lastStepWasAnalyzeScreenshot = true;
                _postAnalyzeExecuteCodeCount = 0;
            }
            else if (_lastStepWasAnalyzeScreenshot)
            {
                if (stepHasExecuteCode && !stepHasBuildTool)
                    _postAnalyzeExecuteCodeCount++;
                else
                    _lastStepWasAnalyzeScreenshot = false;
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
            {
                InjectSearchBudgetNudgeIfNeeded();
                TransitionTo(AgentState.Thinking);
            }
        }

        private void InjectSearchBudgetNudgeIfNeeded()
        {
            if (_totalBuildSteps > 0 && _postBuildSearchSteps >= PostBuildSearchBudget && !_postBuildNudgeInjected)
            {
                _postBuildNudgeInjected = true;
                _history.AddUser(
                    "You have been searching for a while after already creating elements. " +
                    "Please continue styling with the assets you have. Use color tints as fallback for missing assets.");
                ConsoleLogger.Log($"[Agent] Post-build search nudge ({_postBuildSearchSteps} search steps after build).");
                return;
            }

            // Batch search nudge: remind agent to use batch tools when making consecutive single searches
            if (_consecutiveSingleSearchCalls >= MaxConsecutiveSingleSearchBeforeNudge && !_batchSearchNudgeInjected)
            {
                _batchSearchNudgeInjected = true;
                _history.AddUser(
                    "You are making many individual search calls. Use batch tools instead: " +
                    "`search_assets_glob_batch` for multiple filename patterns, " +
                    "`search_sprites_by_text_batch` for multiple text queries. " +
                    "Batch all remaining searches into ONE call.");
                ConsoleLogger.Log($"[Agent] Batch search nudge injected ({_consecutiveSingleSearchCalls} consecutive single searches).");
                return;
            }

            // Post-analyze_screenshot guardrail: if agent keeps using execute_code after screenshot analysis
            if (_lastStepWasAnalyzeScreenshot && _postAnalyzeExecuteCodeCount >= MaxPostAnalyzeExecuteCode)
            {
                _lastStepWasAnalyzeScreenshot = false;
                _history.AddUser(
                    "STOP using execute_code to debug rendering. You have used execute_code " + _postAnalyzeExecuteCodeCount +
                    " times after analyze_screenshot without fixing UI layout. " +
                    "The issue is in UI layout, NOT the camera or render pipeline. " +
                    "Use set_rect_transform_batch to fix positions/sizes, set_image_batch to fix sprites, " +
                    "and inspect_hierarchy/inspect_components to debug element structure.");
                ConsoleLogger.Log($"[Agent] Post-analyze execute_code guardrail triggered ({_postAnalyzeExecuteCodeCount} execute_code calls after analyze_screenshot).");
                return;
            }

            if (_totalBuildSteps > 0 || _searchBudgetWarningInjected) return;

            if (_totalSearchSteps >= SearchBudgetHard)
            {
                _searchBudgetWarningInjected = true;
                _history.AddUser(
                    "You have spent many steps searching. Please start building now with the assets you found. " +
                    "Use plain Images with color tint for anything missing.");
                ConsoleLogger.Log($"[Agent] Search budget hard limit reached ({_totalSearchSteps} steps).");
            }
            else if (_totalSearchSteps >= SearchBudgetSoft)
            {
                _searchBudgetWarningInjected = true;
                _history.AddUser(
                    "You have enough information to start building. " +
                    "Please begin creating UI elements now. Use fallback colors for assets you couldn't find.");
                ConsoleLogger.Log($"[Agent] Search budget soft limit reached ({_totalSearchSteps} steps).");
            }
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

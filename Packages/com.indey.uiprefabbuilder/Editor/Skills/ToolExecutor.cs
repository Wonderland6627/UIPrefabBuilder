using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Indey.UIPrefabBuilder.Compiler;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Core;
using Indey.UIPrefabBuilder.Indexing;
using Indey.UIPrefabBuilder.Logging;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.Skills
{
    public class ToolExecutor
    {
        private readonly DynamicCompiler _compiler = new DynamicCompiler();

        // Every scene edit bumps the revision; each capture remembers the revision it was taken at.
        // Without this a verdict can be recorded against a screenshot that predates the last edit —
        // the pixel claims come from the stale image while the scene checks read the new scene.
        private static int _sceneRevision;
        private static readonly Dictionary<string, int> _captureRevisions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public string Execute(string toolName, string argumentsJson)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson)
                    ? new JObject()
                    : JObject.Parse(argumentsJson);
                NormalizeArgumentKeys(args);
                if (IsSceneMutatingTool(toolName)) _sceneRevision++;
                return ExecuteInternal(toolName, args);
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"Tool '{toolName}' error: {e.Message}");
                return Error($"Tool execution failed: {e.Message}");
            }
        }

        /// <summary>
        /// True for tools that can change what the UI looks like. Inspect/search/screenshot tools are
        /// read-only, so they never invalidate a capture.
        /// </summary>
        public static bool IsSceneMutatingTool(string toolName)
        {
            if (string.IsNullOrEmpty(toolName)) return false;
            if (toolName == "execute_code" || toolName == "instantiate_prefab") return true;

            return toolName.StartsWith("create_", StringComparison.Ordinal)
                || toolName.StartsWith("set_", StringComparison.Ordinal)
                || toolName.StartsWith("add_", StringComparison.Ordinal)
                || toolName.StartsWith("configure_", StringComparison.Ordinal)
                || toolName.StartsWith("wire_", StringComparison.Ordinal)
                || toolName.StartsWith("destroy_", StringComparison.Ordinal)
                || toolName.StartsWith("remove_", StringComparison.Ordinal)
                || toolName.StartsWith("rename_", StringComparison.Ordinal);
        }

        /// <summary>
        /// Rejects reasoning about a capture that no longer shows the current scene. Captures we have
        /// no record of (e.g. a user-supplied image) are left alone.
        /// </summary>
        private static string CheckCaptureFreshness(string screenshotPath, string action)
        {
            if (string.IsNullOrEmpty(screenshotPath)) return null;
            if (!_captureRevisions.TryGetValue(screenshotPath, out var captureRevision)) return null;
            if (captureRevision >= _sceneRevision) return null;

            var edits = _sceneRevision - captureRevision;
            return $"The scene has been modified {edits} time(s) since '{screenshotPath}' was captured, so that image no longer shows the current UI. " +
                $"Call `take_screenshot` again and {action} the NEW file. " +
                "Never judge the build from a capture taken before your last edit.";
        }

        /// <summary>
        /// Models occasionally emit an over-escaped payload where the key itself carries quotes
        /// (`{"\"target\"": "Foo"}`). Every lookup then misses and the tool answers
        /// "GameObject '' not found", which reads as a scene problem and gets retried verbatim.
        /// Stripping the stray quotes recovers the intended call instead.
        /// </summary>
        private static void NormalizeArgumentKeys(JObject args)
        {
            if (args == null) return;

            var renames = new List<KeyValuePair<string, string>>();
            foreach (var prop in args.Properties())
            {
                var cleaned = prop.Name.Trim().Trim('"', '\'').Trim();
                if (cleaned.Length == 0 || cleaned == prop.Name) continue;
                if (args[cleaned] != null) continue;
                renames.Add(new KeyValuePair<string, string>(prop.Name, cleaned));
            }

            foreach (var rename in renames)
            {
                var value = args[rename.Key];
                args.Remove(rename.Key);
                args[rename.Value] = value;
            }
        }

        private string ExecuteInternal(string toolName, JObject args)
        {
            switch (toolName)
            {
                // Asset
                case "search_assets": return DoSearchAssets(args);
                case "search_assets_glob": return DoSearchAssetsGlob(args);
                case "search_assets_glob_batch": return DoSearchAssetsGlobBatch(args);
                case "get_asset_info": return DoGetAssetInfo(args);
                // Create single
                case "create_canvas": return DoCreateCanvas(args);
                case "create_panel": return DoCreatePanel(args);
                case "create_button": return DoCreateButton(args);
                case "create_text": return DoCreateText(args);
                case "create_image": return DoCreateImage(args);
                case "create_inputfield": return DoCreateInputField(args);
                case "create_slider": return DoCreateSlider(args);
                case "create_toggle": return DoCreateToggle(args);
                case "create_scrollview": return DoCreateScrollView(args);
                case "create_dropdown": return DoCreateDropdown(args);
                case "create_tab_bar": return DoCreateTabBar(args);
                // Create batch
                case "create_batch": return DoCreateBatch(args);
                // RectTransform combined
                case "set_rect_transform": return DoSetRectTransform(args);
                case "set_rect_transform_batch": return DoSetRectTransformBatch(args);
                // Legacy rect
                case "set_anchor": return DoSetAnchor(args);
                case "set_size": return DoSetSize(args);
                case "set_position": return DoSetPosition(args);
                // Image
                case "set_image": return DoSetImage(args);
                case "set_image_batch": return DoSetImageBatch(args);
                case "set_image_sprite": return DoSetImageSprite(args);
                case "set_image_color": return DoSetImageColor(args);
                // Text
                case "set_text_properties": return DoSetTextProperties(args);
                case "set_text_properties_batch": return DoSetTextPropertiesBatch(args);
                case "set_text": return DoSetText(args);
                // Layout
                case "add_canvas_group": return DoAddCanvasGroup(args);
                case "add_vertical_layout": return DoAddVerticalLayout(args);
                case "add_horizontal_layout": return DoAddHorizontalLayout(args);
                case "add_grid_layout": return DoAddGridLayout(args);
                case "add_layout_element": return DoAddLayoutElement(args);
                case "add_layout_element_batch": return DoAddLayoutElementBatch(args);
                // Effects
                case "add_mask": return DoAddMask(args);
                case "add_outline": return DoAddOutline(args);
                case "add_outline_batch": return DoAddOutlineBatch(args);
                case "configure_selectable": return DoConfigureSelectable(args);
                case "configure_selectable_states": return DoConfigureSelectableStates(args);
                case "configure_selectable_states_batch": return DoConfigureSelectableStatesBatch(args);
                case "wire_toggle_graphics": return DoWireToggleGraphics(args);
                // Generic component
                case "add_component": return DoAddComponent(args);
                case "set_component_property": return DoSetComponentProperty(args);
                case "set_component_property_batch": return DoSetComponentPropertyBatch(args);
                // Sibling order
                case "set_sibling_index": return DoSetSiblingIndex(args);
                // GameObject management
                case "destroy_object": return DoDestroyObject(args);
                case "destroy_object_batch": return DoDestroyObjectBatch(args);
                case "set_parent": return DoSetParent(args);
                case "rename_object": return DoRenameObject(args);
                // Query
                case "inspect_hierarchy": return DoInspectHierarchy(args);
                case "inspect_hierarchy_batch": return DoInspectHierarchyBatch(args);
                case "inspect_components": return DoInspectComponents(args);
                case "inspect_components_batch": return DoInspectComponentsBatch(args);
                case "get_scene_overview": return DoGetSceneOverview();
                case "get_scene_info": return DoGetSceneInfo();
                case "find_by_name": return DoFindByName(args);
                case "find_by_tag": return DoFindByTag(args);
                case "find_ui_elements": return DoFindUIElements(args);
                // Scene & Prefab
                case "save_scene": return DoSaveScene();
                case "save_as_prefab": return DoSaveAsPrefab(args);
                case "instantiate_prefab": return DoInstantiatePrefab(args);
                case "apply_prefab": return DoApplyPrefab(args);
                case "find_prefab_instances": return DoFindPrefabInstances(args);
                // Screenshot & Visual Analysis
                case "take_screenshot": return DoTakeScreenshot(args);
                case "analyze_screenshot": return DoAnalyzeScreenshot(args);
                case "report_visual_verdict": return DoReportVisualVerdict(args);
                // Project Config
                case "get_project_config": return DoGetProjectConfig();
                // Progress
                case "update_progress": return DoUpdateProgress(args);
                // Code
                case "execute_code": return DoExecuteCode(args);
                // Asset Indexing
                case "match_sprite_by_image": return DoMatchSpriteByImage(args);
                case "crop_design_image": return DoCropDesignImage(args);
                case "map_design_rect": return DoMapDesignRect(args);
                case "map_design_rect_batch": return DoMapDesignRectBatch(args);
                case "match_sprite_by_region": return DoMatchSpriteByRegion(args);
                case "match_sprite_by_region_batch": return DoMatchSpriteByRegionBatch(args);
                case "search_sprites_by_text": return DoSearchSpritesByText(args);
                case "search_sprites_by_text_batch": return DoSearchSpritesByTextBatch(args);
                case "rebuild_asset_index": return DoRebuildAssetIndex(args);
                case "get_index_status": return DoGetIndexStatus(args);
                default: return Error($"Unknown tool: {toolName}");
            }
        }

        #region Asset

        private string DoSearchAssets(JObject args)
        {
            var filter = Str(args, "filter");
            var limit = Int(args, "limit", 50);
            var results = AssetFinder.Find(filter, limit);
            var arr = new JArray(results.Select(p => (JToken)p));
            return new JObject { ["success"] = true, ["count"] = results.Count, ["assets"] = arr }.ToString();
        }

        private string DoSearchAssetsGlob(JObject args)
        {
            var pattern = Str(args, "pattern");
            var directory = Str(args, "directory", "Assets");
            var extensions = Str(args, "extensions", null);
            var limit = Int(args, "limit", 50);
            var results = AssetFinder.FindByGlob(pattern, directory, extensions, limit);
            var arr = new JArray(results.Select(p => (JToken)p));
            return new JObject { ["success"] = true, ["count"] = results.Count, ["assets"] = arr }.ToString();
        }

        private string DoSearchAssetsGlobBatch(JObject args)
        {
            var patternsToken = args["patterns"] as JArray;
            if (patternsToken == null || patternsToken.Count == 0)
                return Error("Missing or empty 'patterns' array.");

            var directory = Str(args, "directory", "Assets");
            var extensions = Str(args, "extensions", null);
            var limit = Int(args, "limit", 20);

            var resultsObj = new JObject();
            int totalCount = 0;

            foreach (var token in patternsToken)
            {
                var pattern = token.ToString();
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                var results = AssetFinder.FindByGlob(pattern, directory, extensions, limit);
                resultsObj[pattern] = new JArray(results.Select(p => (JToken)p));
                totalCount += results.Count;
            }

            return new JObject { ["success"] = true, ["count"] = totalCount, ["results"] = resultsObj }.ToString();
        }

        private string DoGetAssetInfo(JObject args)
        {
            var info = AssetFinder.GetInfo(Str(args, "path"));
            return new JObject { ["success"] = true, ["info"] = info }.ToString();
        }

        #endregion

        #region Create Single

        private string DoCreateCanvas(JObject args)
        {
            var name = Str(args, "name", "Canvas");
            var modeStr = Str(args, "renderMode", "ScreenSpaceOverlay");
            if (!TryParseEnum(modeStr, out RenderMode mode))
                return Error($"Invalid renderMode: {modeStr}.");
            return Created(UICreator.CreateCanvas(name, mode));
        }

        private string DoCreatePanel(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreatePanel(Str(args, "name"), parent.transform, ReadColor(args, 1, 1, 1, 1)));
        }

        private string DoCreateButton(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));

            var height = Float(args, "height", 40);
            var spriteErrors = new List<string>();
            var go = UICreator.CreateButton(Str(args, "name"), parent.transform, Str(args, "text", "Button"),
                new Vector2(Float(args, "width", 160), height),
                Str(args, "backgroundSpritePath", null),
                Str(args, "iconSpritePath", null),
                new Vector2(Float(args, "iconWidth", height * 0.5f), Float(args, "iconHeight", height * 0.5f)),
                Str(args, "iconPosition", "Left"),
                Int(args, "fontSize", 18),
                spriteErrors);
            if (go == null) return Error("Failed to create.");

            var result = new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) };
            AttachSpriteWarnings(result, spriteErrors);
            return result.ToString();
        }

        private string DoCreateText(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var color = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));
            return Created(UICreator.CreateText(Str(args, "name"), parent.transform, Str(args, "content"), Int(args, "fontSize", 18), color));
        }

        private string DoCreateImage(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var go = UICreator.CreateImage(Str(args, "name"), parent.transform, Str(args, "spritePath", ""),
                new Vector2(Float(args, "width", 100), Float(args, "height", 100)), out var spriteError);
            if (go == null) return Error("Failed to create.");
            var result = new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) };
            if (!string.IsNullOrEmpty(spriteError)) result["warning"] = spriteError;
            return result.ToString();
        }

        private string DoCreateInputField(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));

            Color? textColor = null;
            if (args["r"] != null || args["g"] != null || args["b"] != null)
                textColor = new Color(Float(args, "r", 0.2f), Float(args, "g", 0.2f), Float(args, "b", 0.2f), Float(args, "a", 1));

            var spriteErrors = new List<string>();
            var go = UICreator.CreateInputField(Str(args, "name"), parent.transform,
                Str(args, "placeholder", "Enter text..."),
                new Vector2(Float(args, "width", 200), Float(args, "height", 30)),
                Str(args, "text", ""),
                Int(args, "fontSize", 24),
                textColor, null,
                Str(args, "backgroundSpritePath", null),
                spriteErrors);
            if (go == null) return Error("Failed to create.");

            var result = new JObject
            {
                ["success"] = true,
                ["name"] = go.name,
                ["path"] = GetHierarchyPath(go),
                ["structure"] = "Standard InputField hierarchy created and wired (Text Area → Placeholder / Text). " +
                    "Style the visible label via set_text_properties on the 'Text' child and the hint on 'Placeholder'."
            };
            AttachSpriteWarnings(result, spriteErrors);
            return result.ToString();
        }

        private string DoCreateSlider(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));

            var spriteErrors = new List<string>();
            var go = UICreator.CreateSlider(Str(args, "name"), parent.transform,
                Float(args, "min", 0), Float(args, "max", 1), Float(args, "value", 0.5f),
                new Vector2(Float(args, "width", 320), Float(args, "height", 40)),
                Str(args, "backgroundSpritePath", null),
                Str(args, "fillSpritePath", null),
                Str(args, "handleSpritePath", null),
                ReadOptionalColor(args, "background"),
                ReadOptionalColor(args, "fill"),
                ReadOptionalColor(args, "handle"),
                Float(args, "handleWidth", 0),
                Bool(args, "vertical", false),
                spriteErrors);
            if (go == null) return Error("Failed to create.");

            var result = new JObject
            {
                ["success"] = true,
                ["name"] = go.name,
                ["path"] = GetHierarchyPath(go),
                ["structure"] = "Slider hierarchy created and wired: 'Background' is the track, 'Fill Area/Fill' is Slider.fillRect " +
                    "(the filled portion) and 'Handle Slide Area/Handle' is Slider.handleRect (the knob). " +
                    "Give all three real sprites or tints — a slider whose parts are untextured white reads as empty space."
            };
            AttachSpriteWarnings(result, spriteErrors);
            return result.ToString();
        }

        /// <summary>
        /// Reads a "&lt;role&gt;R/G/B/A" colour group, returning null when the caller did not specify it
        /// so the creator can keep its own default instead of silently tinting to white.
        /// </summary>
        private static Color? ReadOptionalColor(JObject args, string role)
        {
            var r = role + "R";
            var g = role + "G";
            var b = role + "B";
            var a = role + "A";
            if (args[r] == null && args[g] == null && args[b] == null && args[a] == null) return null;
            return new Color(Float(args, r, 1), Float(args, g, 1), Float(args, b, 1), Float(args, a, 1));
        }

        private string DoCreateToggle(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));

            Color? labelColor = null;
            if (args["r"] != null || args["g"] != null || args["b"] != null)
                labelColor = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));

            var spriteErrors = new List<string>();
            var go = UICreator.CreateToggle(Str(args, "name"), parent.transform, Str(args, "label", null),
                Bool(args, "isOn", true),
                new Vector2(Float(args, "width", 160), Float(args, "height", 160)),
                Str(args, "backgroundSpritePath", null),
                Str(args, "checkmarkSpritePath", null),
                Int(args, "fontSize", 20),
                labelColor, spriteErrors);
            if (go == null) return Error("Failed to create.");

            var result = new JObject
            {
                ["success"] = true,
                ["name"] = go.name,
                ["path"] = GetHierarchyPath(go),
                ["structure"] = "Toggle hierarchy created and wired: 'Background' is targetGraphic, 'Background/Checkmark' is " +
                    "Toggle.graphic (the selected-state highlight). Assign real sprites to both with set_image_batch."
            };
            AttachSpriteWarnings(result, spriteErrors);
            return result.ToString();
        }

        private string DoCreateScrollView(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreateScrollView(Str(args, "name"), parent.transform,
                new Vector2(Float(args, "width", 300), Float(args, "height", 200))));
        }

        private string DoCreateDropdown(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreateDropdown(Str(args, "name"), parent.transform, Str(args, "options", "Option A,Option B,Option C"),
                new Vector2(Float(args, "width", 160), Float(args, "height", 30))));
        }

        #endregion

        #region Create Batch

        private string DoCreateBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray itemsArr;
            try { itemsArr = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            var measurementGate = CheckDesignMeasurementGate();
            if (measurementGate != null) return measurementGate;

            int total = itemsArr.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in itemsArr)
            {
                try
                {
                    var type = (item["type"]?.ToString() ?? "").ToLowerInvariant();
                    var name = item["name"]?.ToString() ?? "Element";
                    var parentName = item["parent"]?.ToString();
                    var parentGO = string.IsNullOrEmpty(parentName) ? null : FindGO(parentName);
                    Transform parentT = parentGO?.transform;

                    var w = item["width"] != null ? (float)item["width"] : 100f;
                    var h = item["height"] != null ? (float)item["height"] : 100f;
                    var color = new Color(
                        item["r"] != null ? (float)item["r"] : 1f,
                        item["g"] != null ? (float)item["g"] : 1f,
                        item["b"] != null ? (float)item["b"] : 1f,
                        item["a"] != null ? (float)item["a"] : 1f);
                    var text = item["text"]?.ToString();
                    var spritePath = item["spritePath"]?.ToString() ?? "";
                    var fontSize = item["fontSize"] != null ? (int)item["fontSize"] : 18;
                    var alignment = ParseCreateAlignment(item["alignment"]?.ToString());
                    var isOn = item["isOn"] == null || (bool)item["isOn"];
                    var placeholder = item["placeholder"]?.ToString();
                    var checkmarkSpritePath = item["checkmarkSpritePath"]?.ToString();
                    var iconSpritePath = item["iconSpritePath"]?.ToString();
                    var fillSpritePath = item["fillSpritePath"]?.ToString();
                    var handleSpritePath = item["handleSpritePath"]?.ToString();
                    // A colour is only meaningful when the caller actually asked for one: the white
                    // default would otherwise overwrite each creator's own sensible tint.
                    var hasColor = item["r"] != null || item["g"] != null || item["b"] != null || item["a"] != null;

                    GameObject go = null;
                    string itemSpriteError = null;
                    var itemSpriteErrors = new List<string>();
                    switch (type)
                    {
                        case "canvas": go = UICreator.CreateCanvas(name); break;
                        case "panel":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreatePanel(name, parentT, color); break;
                        case "button":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateButton(name, parentT, text ?? "Button", new Vector2(w, h),
                                spritePath, iconSpritePath,
                                new Vector2(item["iconWidth"] != null ? (float)item["iconWidth"] : h * 0.5f,
                                    item["iconHeight"] != null ? (float)item["iconHeight"] : h * 0.5f),
                                item["iconPosition"]?.ToString() ?? "Left", fontSize, itemSpriteErrors); break;
                        case "text":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateText(name, parentT, text ?? "", fontSize, color, alignment); break;
                        case "image":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateImage(name, parentT, spritePath, new Vector2(w, h), out itemSpriteError); break;
                        case "inputfield":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateInputField(name, parentT, placeholder ?? "Enter text...", new Vector2(w, h),
                                text ?? "", fontSize, hasColor ? color : (Color?)null, null, spritePath, itemSpriteErrors); break;
                        case "slider":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateSlider(name, parentT,
                                item["min"] != null ? (float)item["min"] : 0f,
                                item["max"] != null ? (float)item["max"] : 1f,
                                item["value"] != null ? (float)item["value"] : 0.5f,
                                new Vector2(w, h), spritePath, fillSpritePath, handleSpritePath,
                                null, null, null,
                                item["handleWidth"] != null ? (float)item["handleWidth"] : 0f,
                                item["vertical"] != null && (bool)item["vertical"], itemSpriteErrors); break;
                        case "toggle":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateToggle(name, parentT, text, isOn, new Vector2(w, h),
                                spritePath, checkmarkSpritePath, fontSize, color, itemSpriteErrors); break;
                        case "dropdown":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateDropdown(name, parentT, text ?? "Option A,Option B", new Vector2(w, h)); break;
                        case "scrollview":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateScrollView(name, parentT, new Vector2(w, h)); break;
                        default:
                            results.Add(ItemFail(name, $"Unknown type: {type}"));
                            fail++; continue;
                    }
                    // 对于 text 类型，创建后立即验证 text 内容是否写入成功
                    if (type == "text" && go != null && !string.IsNullOrEmpty(text))
                    {
                        ComponentHelper.TrySetText(go, text, out _);
                    }

                    var itemResult = new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) };
                    if (!string.IsNullOrEmpty(itemSpriteError)) itemSpriteErrors.Insert(0, itemSpriteError);
                    AttachSpriteWarnings(itemResult, itemSpriteErrors);
                    results.Add(itemResult);
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(item["name"]?.ToString() ?? "?", e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        private static TextAnchor ParseCreateAlignment(string value)
        {
            if (string.IsNullOrEmpty(value)) return TextAnchor.MiddleLeft;
            switch (value.ToLowerInvariant())
            {
                case "center": return TextAnchor.MiddleCenter;
                case "left": return TextAnchor.MiddleLeft;
                case "right": return TextAnchor.MiddleRight;
                case "top": return TextAnchor.UpperCenter;
                case "bottom": return TextAnchor.LowerCenter;
                default: return Enum.TryParse<TextAnchor>(value, true, out var anchor) ? anchor : TextAnchor.MiddleLeft;
            }
        }

        /// <summary>
        /// 有设计稿时，禁止在未做任何区域测量的情况下开始搭建。凭肉眼估算的尺寸是比例失真的根源，
        /// 而 map_design_rect 已经能把归一化 bbox 换算成 Canvas 数值。
        /// </summary>
        private static string CheckDesignMeasurementGate()
        {
            if (!DesignImageContext.RequiresMeasurement) return null;
            if (string.IsNullOrEmpty(DesignImageContext.CurrentAssetPath)) return null;
            if (DesignImageContext.MappedRegionCount > 0) return null;

            return Error("A design mockup is attached but you have not measured a single region yet. " +
                "Call `map_design_rect_batch` first with the normalized bbox (0-1, top-left origin) of EVERY element you are about to create " +
                "— including each background layer, tab, input box, icon cell and label — then build with the returned " +
                "anchoredPosition/sizeDelta values. Eyeballed sizes are the main cause of wrong proportions and font scale.");
        }

        #endregion

        #region RectTransform

        private string DoSetRectTransform(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            return ApplyRectTransform(go, args);
        }

        private string DoSetRectTransformBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    ApplyRectTransformInternal(go, item);
                    results.Add(new JObject { ["success"] = true, ["name"] = go.name });
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        private string ApplyRectTransform(GameObject go, JObject args)
        {
            ApplyRectTransformInternal(go, args);
            return Ok($"RectTransform updated on '{go.name}'.");
        }

        private void ApplyRectTransformInternal(GameObject go, JObject args)
        {
            bool hasRawAnchors = args["anchorMinX"] != null || args["anchorMinY"] != null
                              || args["anchorMaxX"] != null || args["anchorMaxY"] != null;

            if (hasRawAnchors)
            {
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Undo.RecordObject(rt, "Set Anchor");
                    float minX = args["anchorMinX"] != null ? (float)args["anchorMinX"] : rt.anchorMin.x;
                    float minY = args["anchorMinY"] != null ? (float)args["anchorMinY"] : rt.anchorMin.y;
                    float maxX = args["anchorMaxX"] != null ? (float)args["anchorMaxX"] : rt.anchorMax.x;
                    float maxY = args["anchorMaxY"] != null ? (float)args["anchorMaxY"] : rt.anchorMax.y;
                    rt.anchorMin = new Vector2(minX, minY);
                    rt.anchorMax = new Vector2(maxX, maxY);
                }
            }
            else
            {
                var anchorStr = args["anchor"]?.ToString();
                if (!string.IsNullOrEmpty(anchorStr) && TryParseEnum(anchorStr, out AnchorPreset preset))
                    RectTransformHelper.SetAnchorPreset(go, preset);
            }

            var rect = go.GetComponent<RectTransform>();
            if (rect == null) return;
            Undo.RecordObject(rect, "Set RT");

            if (args["pivotX"] != null || args["pivotY"] != null)
                rect.pivot = new Vector2(args["pivotX"] != null ? (float)args["pivotX"] : rect.pivot.x, args["pivotY"] != null ? (float)args["pivotY"] : rect.pivot.y);

            if (args["width"] != null || args["height"] != null)
                rect.sizeDelta = new Vector2(args["width"] != null ? (float)args["width"] : rect.sizeDelta.x, args["height"] != null ? (float)args["height"] : rect.sizeDelta.y);

            if (args["x"] != null || args["y"] != null)
                rect.anchoredPosition = new Vector2(args["x"] != null ? (float)args["x"] : rect.anchoredPosition.x, args["y"] != null ? (float)args["y"] : rect.anchoredPosition.y);
        }

        private string DoSetAnchor(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            if (!TryParseEnum(Str(args, "preset"), out AnchorPreset preset))
                return Error($"Invalid preset: {Str(args, "preset")}.");
            RectTransformHelper.SetAnchorPreset(go, preset);
            return Ok($"Anchor set to {preset} on '{go.name}'.");
        }

        private string DoSetSize(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            RectTransformHelper.SetSize(go, Float(args, "width"), Float(args, "height"));
            return Ok($"Size set on '{go.name}'.");
        }

        private string DoSetPosition(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            RectTransformHelper.SetPosition(go, Float(args, "x"), Float(args, "y"));
            return Ok($"Position set on '{go.name}'.");
        }

        #endregion

        #region Image & Text

        private string DoSetImage(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            Color? color = null;
            if (args["r"] != null || args["g"] != null || args["b"] != null)
                color = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));
            var spriteError = ComponentHelper.SetImageProperties(go, ReadSpritePathArg(args["spritePath"]), Str(args, "type", null),
                Str(args, "fillMethod", null), NullFloat(args, "fillAmount"), NullBool(args, "preserveAspect"), color,
                out var missingImage, out var spriteCleared);
            if (missingImage) return Error(NoImageComponentError(go));
            var message = spriteCleared
                ? $"Image properties updated on '{go.name}'. The previous sprite was CLEARED (empty spritePath) — it now renders as a flat colour."
                : $"Image properties updated on '{go.name}'.";
            if (!string.IsNullOrEmpty(spriteError))
                return new JObject { ["success"] = true, ["message"] = message, ["warning"] = spriteError }.ToString();
            return Ok(message);
        }

        private string DoSetImageBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    Color? color = null;
                    if (item["r"] != null || item["g"] != null || item["b"] != null)
                        color = new Color(
                            item["r"] != null ? (float)item["r"] : 1f,
                            item["g"] != null ? (float)item["g"] : 1f,
                            item["b"] != null ? (float)item["b"] : 1f,
                            item["a"] != null ? (float)item["a"] : 1f);
                    var spritePath = ReadSpritePathArg(item["spritePath"]);
                    var type = item["type"]?.ToString();
                    var fillMethod = item["fillMethod"]?.ToString();
                    float? fillAmount = item["fillAmount"] != null ? (float?)item["fillAmount"] : null;
                    bool? preserveAspect = item["preserveAspect"] != null ? (bool?)item["preserveAspect"] : null;
                    var spriteError = ComponentHelper.SetImageProperties(go, spritePath, type, fillMethod, fillAmount,
                        preserveAspect, color, out var missingImage, out var spriteCleared);
                    if (missingImage)
                    {
                        results.Add(ItemFail(target ?? go.name, NoImageComponentError(go)));
                        fail++;
                        continue;
                    }
                    var imgResult = new JObject { ["success"] = true, ["name"] = go.name };
                    if (spriteCleared) imgResult["message"] = "Previous sprite CLEARED (empty spritePath) — renders as a flat colour now.";
                    if (!string.IsNullOrEmpty(spriteError)) imgResult["warning"] = spriteError;
                    results.Add(imgResult);
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        /// <summary>
        /// null 与 JSON null 都表示"不改动 sprite"，只有真正传入的空字符串才表示清除。
        /// </summary>
        private static string ReadSpritePathArg(JToken token)
            => token == null || token.Type == JTokenType.Null ? null : token.ToString();

        /// <summary>
        /// Toggle/Button 这类容器自身常常没有 Image（图形在 Background 子节点上）。只回一句
        /// "has no Image component" 会让模型靠 inspect_hierarchy 猜路径，所以这里直接把候选节点报出来。
        /// </summary>
        private static string NoImageComponentError(GameObject go)
        {
            var hint = $"'{go.name}' has no Image component, so NOTHING was changed.";

            var selectable = go.GetComponent<Selectable>();
            var targetGraphic = selectable != null ? selectable.targetGraphic : null;
            if (targetGraphic != null && targetGraphic.gameObject != go)
                return hint + $" This is a {selectable.GetType().Name} whose graphic lives on '{GetHierarchyPath(targetGraphic.gameObject)}' — retarget that path.";

            var children = new List<string>();
            foreach (var image in go.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.gameObject == go) continue;
                children.Add(GetHierarchyPath(image.gameObject));
                if (children.Count >= 6) break;
            }
            if (children.Count > 0)
                return hint + $" Image children you can target instead: {string.Join(", ", children)}.";

            return hint + " Add one with add_component, or target the child that renders the graphic.";
        }

        private string DoSetImageSprite(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var spriteError = ComponentHelper.SetImageSprite(go, Str(args, "spritePath"));
            if (!string.IsNullOrEmpty(spriteError)) return Error(spriteError);
            return Ok($"Sprite set on '{go.name}'.");
        }

        private string DoSetImageColor(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var colorError = ComponentHelper.SetImageColor(go, new Color(Float(args, "r"), Float(args, "g"), Float(args, "b"), Float(args, "a", 1)));
            if (colorError != null) return Error(colorError);
            return Ok($"Image color set on '{go.name}'.");
        }

        private string DoSetTextProperties(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));

            var style = ReadTextStyle(args);
            if (!ComponentHelper.TryApplyTextStyle(go, style, out var message))
                return Error(message);

            if (!string.IsNullOrEmpty(message))
                return new JObject { ["success"] = true, ["message"] = $"Text properties updated on '{go.name}'.", ["warning"] = message }.ToString();
            return Ok($"Text properties updated on '{go.name}'.");
        }

        private static ComponentHelper.TextStyleArgs ReadTextStyle(JObject args)
        {
            Color? color = null;
            if (args["r"] != null || args["g"] != null || args["b"] != null)
                color = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));

            Color? outlineColor = null;
            if (args["outlineR"] != null || args["outlineG"] != null || args["outlineB"] != null)
                outlineColor = new Color(Float(args, "outlineR", 0), Float(args, "outlineG", 0), Float(args, "outlineB", 0), Float(args, "outlineA", 1));

            return new ComponentHelper.TextStyleArgs
            {
                Text = args["text"]?.ToString(),
                FontSize = NullFloat(args, "fontSize"),
                Alignment = args["alignment"]?.ToString(),
                FontStyle = args["fontStyle"]?.ToString(),
                Color = color,
                Overflow = args["overflow"]?.ToString(),
                FontAssetPath = args["fontAssetPath"]?.ToString(),
                OutlineWidth = NullFloat(args, "outlineWidth"),
                OutlineColor = outlineColor,
                CharacterSpacing = NullFloat(args, "characterSpacing"),
                LineSpacing = NullFloat(args, "lineSpacing"),
                EnableAutoSizing = NullBool(args, "enableAutoSizing"),
                FontSizeMin = NullFloat(args, "fontSizeMin"),
                FontSizeMax = NullFloat(args, "fontSizeMax"),
                EnableWordWrapping = NullBool(args, "enableWordWrapping")
            };
        }

        private string DoSetTextPropertiesBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    if (!ComponentHelper.TryApplyTextStyle(go, ReadTextStyle(item), out var message))
                    {
                        results.Add(ItemFail(target ?? go.name, message));
                        fail++;
                        continue;
                    }
                    var textResult = new JObject { ["success"] = true, ["name"] = go.name };
                    if (!string.IsNullOrEmpty(message)) textResult["warning"] = message;
                    results.Add(textResult);
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        private string DoSetText(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            if (!ComponentHelper.TrySetText(go, Str(args, "text"), out var message))
                return Error(message);
            if (!string.IsNullOrEmpty(message))
                return new JObject { ["success"] = true, ["message"] = $"Text set on '{go.name}'.", ["warning"] = message }.ToString();
            return Ok($"Text set on '{go.name}'.");
        }

        #endregion

        #region Layout

        private string DoAddCanvasGroup(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.AddCanvasGroup(go, Float(args, "alpha", 1), Bool(args, "interactable", true), Bool(args, "blocksRaycasts", true));
            return Ok($"CanvasGroup added to '{go.name}'.");
        }

        private string DoAddVerticalLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddVerticalLayout(go, Float(args, "spacing", 8), ParseTextAnchor(Str(args, "childAlignment", "UpperCenter")), BuildPadding(args),
                Bool(args, "childControlWidth", true), Bool(args, "childControlHeight", true),
                Bool(args, "childForceExpandWidth", false), Bool(args, "childForceExpandHeight", false));
            return Ok($"VerticalLayoutGroup added to '{go.name}'.");
        }

        private string DoAddHorizontalLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddHorizontalLayout(go, Float(args, "spacing", 8), ParseTextAnchor(Str(args, "childAlignment", "MiddleCenter")), BuildPadding(args),
                Bool(args, "childControlWidth", true), Bool(args, "childControlHeight", true),
                Bool(args, "childForceExpandWidth", false), Bool(args, "childForceExpandHeight", false));
            return Ok($"HorizontalLayoutGroup added to '{go.name}'.");
        }

        private string DoAddGridLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddGridLayout(go, new Vector2(Float(args, "cellWidth"), Float(args, "cellHeight")),
                new Vector2(Float(args, "spacingX", 0), Float(args, "spacingY", 0)));
            return Ok($"GridLayoutGroup added to '{go.name}'.");
        }

        private string DoAddLayoutElement(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddLayoutElement(go, NullFloat(args, "minWidth"), NullFloat(args, "minHeight"),
                NullFloat(args, "preferredWidth"), NullFloat(args, "preferredHeight"),
                NullFloat(args, "flexibleWidth"), NullFloat(args, "flexibleHeight"));
            return Ok($"LayoutElement added to '{go.name}'.");
        }

        private string DoAddLayoutElementBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    LayoutHelper.AddLayoutElement(go,
                        item["minWidth"] != null ? (float?)item["minWidth"] : null,
                        item["minHeight"] != null ? (float?)item["minHeight"] : null,
                        item["preferredWidth"] != null ? (float?)item["preferredWidth"] : null,
                        item["preferredHeight"] != null ? (float?)item["preferredHeight"] : null,
                        item["flexibleWidth"] != null ? (float?)item["flexibleWidth"] : null,
                        item["flexibleHeight"] != null ? (float?)item["flexibleHeight"] : null);
                    results.Add(new JObject { ["success"] = true, ["name"] = go.name });
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        #endregion

        #region Effects

        private string DoAddMask(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.AddMask(go, Str(args, "maskType", "RectMask2D"), Bool(args, "showMaskGraphic", false));
            return Ok($"Mask added to '{go.name}'.");
        }

        private string DoAddOutline(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var color = new Color(Float(args, "r", 0), Float(args, "g", 0), Float(args, "b", 0), Float(args, "a", 0.5f));
            ComponentHelper.AddOutline(go, Str(args, "effectType", "Outline"), color,
                new Vector2(Float(args, "distanceX", 1), Float(args, "distanceY", -1)));
            return Ok($"Effect added to '{go.name}'.");
        }

        private string DoAddOutlineBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    var color = new Color(
                        item["r"] != null ? (float)item["r"] : 0f,
                        item["g"] != null ? (float)item["g"] : 0f,
                        item["b"] != null ? (float)item["b"] : 0f,
                        item["a"] != null ? (float)item["a"] : 0.5f);
                    var effectType = item["effectType"]?.ToString() ?? "Outline";
                    var dist = new Vector2(
                        item["distanceX"] != null ? (float)item["distanceX"] : 1f,
                        item["distanceY"] != null ? (float)item["distanceY"] : -1f);
                    ComponentHelper.AddOutline(go, effectType, color, dist);
                    results.Add(new JObject { ["success"] = true, ["name"] = go.name });
                    success++;
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        private string DoConfigureSelectable(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var error = ComponentHelper.ConfigureSelectable(go, Str(args, "transition", "ColorTint"),
                Str(args, "navigationMode", "Automatic"), Bool(args, "interactable", true));
            if (error != null) return Error(error);
            return Ok($"Selectable configured on '{go.name}'.");
        }

        private string DoConfigureSelectableStates(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));

            if (!ComponentHelper.TryConfigureSelectableStates(go, ReadSelectableStates(args), out var message))
                return Error(message);

            if (!string.IsNullOrEmpty(message))
                return new JObject { ["success"] = true, ["message"] = $"Selectable states configured on '{go.name}'.", ["warning"] = message }.ToString();
            return Ok($"Selectable states configured on '{go.name}'.");
        }

        private string DoConfigureSelectableStatesBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }

                if (!ComponentHelper.TryConfigureSelectableStates(go, ReadSelectableStates(item), out var message))
                {
                    results.Add(ItemFail(target ?? go.name, message));
                    fail++;
                    continue;
                }

                var itemResult = new JObject { ["success"] = true, ["name"] = go.name };
                if (!string.IsNullOrEmpty(message)) itemResult["warning"] = message;
                results.Add(itemResult);
                success++;
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        private static ComponentHelper.SelectableStateArgs ReadSelectableStates(JObject args)
        {
            return new ComponentHelper.SelectableStateArgs
            {
                Transition = args["transition"]?.ToString(),
                NormalColor = ReadPrefixedColor(args, "normal"),
                HighlightedColor = ReadPrefixedColor(args, "highlighted"),
                PressedColor = ReadPrefixedColor(args, "pressed"),
                SelectedColor = ReadPrefixedColor(args, "selected"),
                DisabledColor = ReadPrefixedColor(args, "disabled"),
                ColorMultiplier = NullFloat(args, "colorMultiplier"),
                FadeDuration = NullFloat(args, "fadeDuration"),
                HighlightedSpritePath = args["highlightedSpritePath"]?.ToString(),
                PressedSpritePath = args["pressedSpritePath"]?.ToString(),
                SelectedSpritePath = args["selectedSpritePath"]?.ToString(),
                DisabledSpritePath = args["disabledSpritePath"]?.ToString(),
                TargetGraphicTarget = args["targetGraphic"]?.ToString(),
                Interactable = NullBool(args, "interactable")
            };
        }

        /// <summary>Reads e.g. selectedR/selectedG/selectedB/selectedA into a Color, or null when unset.</summary>
        private static Color? ReadPrefixedColor(JObject args, string prefix)
        {
            var r = args[prefix + "R"];
            var g = args[prefix + "G"];
            var b = args[prefix + "B"];
            var a = args[prefix + "A"];
            if (r == null && g == null && b == null && a == null) return null;
            return new Color(
                r != null ? (float)r : 1f,
                g != null ? (float)g : 1f,
                b != null ? (float)b : 1f,
                a != null ? (float)a : 1f);
        }

        private string DoWireToggleGraphics(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            if (!ComponentHelper.TryWireToggleGraphics(go, Str(args, "backgroundTarget", null), Str(args, "selectedGraphicTarget", null), out var message))
                return Error(message);
            if (!string.IsNullOrEmpty(message))
                return new JObject { ["success"] = true, ["message"] = $"Toggle graphics wired on '{go.name}'.", ["warning"] = message }.ToString();
            return Ok($"Toggle graphics wired on '{go.name}'.");
        }

        private string DoCreateTabBar(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));

            var labels = StringList(args, "labels");
            if (labels.Count == 0) return Error("Missing 'labels' — give one label per tab, e.g. [\"Bookmark\", \"Alliance\"].");

            var selectedTextColor = new Color(Float(args, "selectedTextR", 0.35f), Float(args, "selectedTextG", 0.2f),
                Float(args, "selectedTextB", 0.15f), Float(args, "selectedTextA", 1f));
            var unselectedTextColor = new Color(Float(args, "unselectedTextR", 0.55f), Float(args, "unselectedTextG", 0.42f),
                Float(args, "unselectedTextB", 0.38f), Float(args, "unselectedTextA", 1f));

            var spriteErrors = new List<string>();
            var root = UICreator.CreateTabBar(
                Str(args, "name", "TabBar"), parent.transform, labels,
                Int(args, "selectedIndex", 0),
                new Vector2(Float(args, "tabWidth", 240), Float(args, "tabHeight", 80)),
                Float(args, "spacing", 0),
                Str(args, "unselectedSpritePath", null),
                Str(args, "selectedSpritePath", null),
                Int(args, "fontSize", 30),
                selectedTextColor, unselectedTextColor,
                out var tabs, spriteErrors);

            if (root == null) return Error("Failed to create the tab bar.");

            var result = new JObject
            {
                ["success"] = true,
                ["name"] = root.name,
                ["path"] = GetHierarchyPath(root),
                ["tabs"] = new JArray(tabs.Select(t => (JToken)GetHierarchyPath(t))),
                ["structure"] = "Each tab is a Toggle in a shared ToggleGroup: the tab Image is targetGraphic (unselected look) " +
                    "and the 'Selected' child Image is Toggle.graphic (selected look). Style them with set_image_batch, " +
                    "and the labels via set_text_properties_batch on the 'Label' children."
            };

            var missing = new List<string>();
            if (string.IsNullOrEmpty(Str(args, "unselectedSpritePath", null))) missing.Add("unselectedSpritePath");
            if (string.IsNullOrEmpty(Str(args, "selectedSpritePath", null))) missing.Add("selectedSpritePath");

            var warnings = new List<string>(spriteErrors);
            if (missing.Count > 0)
            {
                warnings.Add("No sprite given for: " + string.Join(", ", missing)
                    + ". Those states are flat colour placeholders until you assign real sprites — a design mockup's "
                    + "selected/unselected tabs almost always use different artwork.");
            }
            AttachSpriteWarnings(result, warnings);

            return result.ToString();
        }

        /// <summary>
        /// Sprite paths that failed to load must reach the model: the element is now a blank box,
        /// and a silent success is what makes the wrong artwork ship.
        /// </summary>
        private static void AttachSpriteWarnings(JObject result, List<string> warnings)
        {
            if (result == null || warnings == null || warnings.Count == 0) return;
            result["warning"] = string.Join(" ", warnings);
        }

        #endregion

        #region Generic Component

        private string DoAddComponent(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var typeName = Str(args, "componentType");
            var comp = ComponentHelper.AddComponentByTypeName(go, typeName);
            if (comp == null) return Error($"Component type '{typeName}' not found.");
            return Ok($"Component '{comp.GetType().Name}' added to '{go.name}'.");
        }

        private string DoSetComponentProperty(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var ok = ComponentHelper.SetComponentProperty(go, Str(args, "componentType"), Str(args, "propertyName"),
                Str(args, "value"), Str(args, "assetPath", null));
            return ok ? Ok($"Property set on '{go.name}'.") : Error("Property not found or type mismatch.");
        }

        private string DoSetComponentPropertyBatch(JObject args)
        {
            var itemsJson = Str(args, "items");
            if (string.IsNullOrWhiteSpace(itemsJson)) return Error("Missing 'items' parameter.");

            JArray items;
            try { items = JArray.Parse(itemsJson); }
            catch { return Error("Invalid JSON in 'items' parameter."); }

            int total = items.Count, success = 0, fail = 0;
            var results = new JArray();

            foreach (JObject item in items)
            {
                var target = item["target"]?.ToString();
                var go = FindGO(target);
                if (go == null) { results.Add(ItemFail(target ?? "?", "Not found")); fail++; continue; }
                try
                {
                    var ok = ComponentHelper.SetComponentProperty(go,
                        item["componentType"]?.ToString(),
                        item["propertyName"]?.ToString(),
                        item["value"]?.ToString(),
                        item["assetPath"]?.ToString());
                    if (ok)
                    {
                        results.Add(new JObject { ["success"] = true, ["name"] = go.name });
                        success++;
                    }
                    else
                    {
                        results.Add(ItemFail(target, "Property not found or type mismatch."));
                        fail++;
                    }
                }
                catch (Exception e)
                {
                    results.Add(ItemFail(target, e.Message));
                    fail++;
                }
            }

            return new JObject { ["success"] = fail == 0, ["totalItems"] = total, ["successCount"] = success, ["failCount"] = fail, ["results"] = results }.ToString();
        }

        #endregion

        #region Sibling Order

        private string DoSetSiblingIndex(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var index = Int(args, "index");
            Undo.RecordObject(go.transform, "Set Sibling Index");
            go.transform.SetSiblingIndex(index);
            return Ok($"'{go.name}' sibling index set to {go.transform.GetSiblingIndex()}.");
        }

        #endregion

        #region GameObject Management

        private string DoDestroyObject(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));

            var block = DescribeDestroyBlock(go, Bool(args, "force"));
            if (block != null) return Error(block);

            var name = go.name;
            Undo.DestroyObjectImmediate(go);
            return Ok($"Destroyed '{name}'.");
        }

        /// <summary>
        /// Tearing down an instantiated prefab (or the whole Canvas) to rebuild from scratch throws
        /// away hand-authored art the agent cannot reproduce. Returns a refusal message, or null when
        /// the destroy is allowed.
        /// </summary>
        private static string DescribeDestroyBlock(GameObject go, bool force)
        {
            if (force) return null;

            if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            {
                var source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                return $"'{go.name}' is the root of a prefab instance ({source}). " +
                    "Do NOT delete it to rebuild from scratch — adjust the instance in place " +
                    "(set_rect_transform_batch / set_image_batch / set_text_properties_batch), " +
                    "or hide the children you do not need. Pass force=true only if the user explicitly asked for removal.";
            }

            if (go.GetComponent<Canvas>() != null && go.transform.parent == null && go.transform.childCount > 0)
            {
                return $"'{go.name}' is a root Canvas with {go.transform.childCount} child element(s). " +
                    "Deleting it discards the whole UI. Fix the elements in place instead, " +
                    "or pass force=true if a full rebuild is really intended.";
            }

            return null;
        }

        private string DoDestroyObjectBatch(JObject args)
        {
            var targets = args["targets"] as JArray;
            if (targets == null || targets.Count == 0)
                return Error("Missing or empty 'targets' array.");

            int total = targets.Count, success = 0, fail = 0;
            var results = new JArray();
            var force = Bool(args, "force");

            foreach (var token in targets)
            {
                var name = token.ToString();
                var go = FindGO(name);
                if (go == null) { results.Add(ItemFail(name ?? "?", "Not found")); fail++; continue; }

                var block = DescribeDestroyBlock(go, force);
                if (block != null) { results.Add(ItemFail(name ?? "?", block)); fail++; continue; }

                var actualName = go.name;
                Undo.DestroyObjectImmediate(go);
                results.Add(new JObject { ["success"] = true, ["name"] = actualName });
                success++;
            }

            return new JObject
            {
                ["success"] = fail == 0,
                ["total"] = total,
                ["succeeded"] = success,
                ["failed"] = fail,
                ["results"] = results,
                ["message"] = $"Destroyed {success}/{total} objects."
            }.ToString();
        }

        private string DoSetParent(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            Undo.SetTransformParent(go.transform, parent.transform, $"Reparent {go.name}");
            go.transform.SetParent(parent.transform, Bool(args, "worldPositionStays", false));
            return Ok($"'{go.name}' parented to '{parent.name}'.");
        }

        private string DoRenameObject(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var newName = Str(args, "newName");
            Undo.RecordObject(go, "Rename");
            go.name = newName;
            return Ok($"Renamed to '{newName}'.");
        }

        #endregion

        #region Query

        private string DoInspectHierarchy(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            return new JObject { ["success"] = true, ["hierarchy"] = HierarchyInspector.Describe(go, Int(args, "maxDepth", 4)) }.ToString();
        }

        private string DoInspectHierarchyBatch(JObject args)
        {
            var targets = args["targets"] as JArray;
            if (targets == null || targets.Count == 0)
                return Error("Missing or empty 'targets' array.");

            var maxDepth = Int(args, "maxDepth", 4);
            var resultsObj = new JObject();

            foreach (var token in targets)
            {
                var name = token.ToString();
                var go = FindGO(name);
                resultsObj[name] = go == null
                    ? (JToken)new JObject { ["success"] = false, ["error"] = "Not found" }
                    : new JObject { ["success"] = true, ["hierarchy"] = HierarchyInspector.Describe(go, maxDepth) };
            }

            return new JObject { ["success"] = true, ["results"] = resultsObj }.ToString();
        }

        private string DoInspectComponents(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            return new JObject { ["success"] = true, ["components"] = HierarchyInspector.DescribeComponents(go) }.ToString();
        }

        private string DoInspectComponentsBatch(JObject args)
        {
            var targets = args["targets"] as JArray;
            if (targets == null || targets.Count == 0)
                return Error("Missing or empty 'targets' array.");

            var resultsObj = new JObject();

            foreach (var token in targets)
            {
                var name = token.ToString();
                var go = FindGO(name);
                resultsObj[name] = go == null
                    ? (JToken)new JObject { ["success"] = false, ["error"] = "Not found" }
                    : new JObject { ["success"] = true, ["components"] = HierarchyInspector.DescribeComponents(go) };
            }

            return new JObject { ["success"] = true, ["results"] = resultsObj }.ToString();
        }

        private string DoGetSceneOverview()
        {
            return new JObject { ["success"] = true, ["overview"] = ContextBuilder.BuildSceneOverview() }.ToString();
        }

        private string DoGetSceneInfo()
        {
            return new JObject
            {
                ["success"] = true,
                ["sceneName"] = SceneHelper.GetCurrentSceneName(),
                ["scenePath"] = SceneHelper.GetCurrentScenePath()
            }.ToString();
        }

        private string DoFindByName(JObject args)
        {
            var found = SceneHelper.FindByName(Str(args, "name"));
            var arr = new JArray();
            foreach (var go in found) arr.Add(new JObject { ["name"] = go.name, ["path"] = GetHierarchyPath(go) });
            return new JObject { ["success"] = true, ["count"] = found.Length, ["objects"] = arr }.ToString();
        }

        private string DoFindByTag(JObject args)
        {
            var found = SceneHelper.FindByTag(Str(args, "tag"));
            var arr = new JArray();
            foreach (var go in found) arr.Add(new JObject { ["name"] = go.name, ["path"] = GetHierarchyPath(go) });
            return new JObject { ["success"] = true, ["count"] = found.Length, ["objects"] = arr }.ToString();
        }

        private string DoFindUIElements(JObject args)
        {
            var uiType = Str(args, "uiType", "").ToLowerInvariant();
            var limit = Int(args, "limit", 50);
            var all = UnityEngine.Object.FindObjectsOfType<RectTransform>();
            var arr = new JArray();
            int count = 0;

            foreach (var rt in all)
            {
                if (count >= limit) break;
                var go = rt.gameObject;

                if (!string.IsNullOrEmpty(uiType))
                {
                    bool match = false;
                    switch (uiType)
                    {
                        case "button": match = go.GetComponent<Button>() != null; break;
                        case "text": match = go.GetComponent<Text>() != null || HasTMP(go); break;
                        case "image": match = go.GetComponent<Image>() != null; break;
                        case "toggle": match = go.GetComponent<Toggle>() != null; break;
                        case "slider": match = go.GetComponent<Slider>() != null; break;
                        case "inputfield": match = go.GetComponent<InputField>() != null; break;
                        case "scrollrect": match = go.GetComponent<ScrollRect>() != null; break;
                        case "dropdown": match = go.GetComponent<Dropdown>() != null; break;
                        default: match = true; break;
                    }
                    if (!match) continue;
                }

                arr.Add(new JObject { ["name"] = go.name, ["path"] = GetHierarchyPath(go), ["active"] = go.activeInHierarchy });
                count++;
            }

            return new JObject { ["success"] = true, ["count"] = count, ["elements"] = arr }.ToString();
        }

        #endregion

        #region Scene & Prefab

        private string DoSaveScene()
        {
            var ok = SceneHelper.SaveScene();
            return ok ? Ok("Scene saved.") : Error("Failed to save scene.");
        }

        private string DoSaveAsPrefab(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var path = Str(args, "path");
            if (PrefabHelper.ExistsAtPath(path))
            {
                return Error($"Refused: a prefab asset already exists at '{path}'. " +
                    "Overwriting existing project prefabs is not allowed — choose a different path for a new prefab, " +
                    "or if you only needed to update objects already placed in the scene, that's done (no need to save/overwrite the source prefab).");
            }
            var result = PrefabHelper.Create(go, path);
            return result != null ? Ok($"Prefab saved to '{result}'.") : Error("Failed to save prefab.");
        }

        private string DoInstantiatePrefab(JObject args)
        {
            var path = Str(args, "path");
            var parentName = Str(args, "parent", null);
            Transform parent = null;
            if (!string.IsNullOrEmpty(parentName))
            {
                var p = FindGO(parentName);
                if (p == null) return TargetNotFound(parentName);
                parent = p.transform;
            }
            var go = PrefabHelper.Instantiate(path, parent, Str(args, "name", null));
            return go != null ? Created(go) : Error($"Failed to instantiate prefab at '{path}'.");
        }

        private string DoApplyPrefab(JObject args)
        {
            return Error("Disabled for safety: applying instance overrides back to the source prefab asset " +
                "(PrefabUtility.ApplyPrefabInstance) modifies the project's prefab file directly, which is not allowed. " +
                "You may only modify the GameObject instance in the scene Hierarchy. If the user wants the changes " +
                "persisted as a prefab, save a NEW prefab via `save_as_prefab` at a different path instead.");
        }

        private string DoFindPrefabInstances(JObject args)
        {
            var instances = PrefabHelper.FindInstances(Str(args, "path"));
            var arr = new JArray();
            foreach (var go in instances) arr.Add(new JObject { ["name"] = go.name, ["path"] = GetHierarchyPath(go) });
            return new JObject { ["success"] = true, ["count"] = instances.Count, ["instances"] = arr }.ToString();
        }

        #endregion

        #region Screenshot

        private static readonly string[] _gameViewRTFields =
            { "m_RenderTexture", "m_TargetTexture", "m_BackBuffer" };

        private static byte[] TryCaptureGameViewRT(out int w, out int h)
        {
            w = h = 0;
            byte[] uniformFallback = null;
            try
            {
                var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
                if (gameViewType == null) return null;

                var gameView = EditorWindow.GetWindow(gameViewType, false, "Game", false);

                var repaintNow = typeof(EditorWindow).GetMethod(
                    "RepaintImmediately", BindingFlags.NonPublic | BindingFlags.Instance);
                if (repaintNow != null)
                    repaintNow.Invoke(gameView, null);
                else
                    gameView.Repaint();

                var type = gameViewType;
                while (type != null && type != typeof(object))
                {
                    foreach (var fieldName in _gameViewRTFields)
                    {
                        var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                        var rt = field?.GetValue(gameView) as RenderTexture;
                        if (rt != null && rt.width > 1 && rt.height > 1)
                        {
                            w = rt.width;
                            h = rt.height;
                            var prev = RenderTexture.active;
                            RenderTexture.active = rt;
                            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                            tex.Apply();
                            RenderTexture.active = prev;
                            FlipTextureVertically(tex);
                            var bytes = tex.EncodeToPNG();
                            var hasVariance = TextureHasVisualVariance(tex);
                            UnityEngine.Object.DestroyImmediate(tex);
                            if (hasVariance) return bytes;
                            if (uniformFallback == null) uniformFallback = bytes;
                        }
                    }
                    type = type.BaseType;
                }
            }
            catch { }
            return uniformFallback;
        }

        private struct CanvasCaptureState
        {
            public Canvas Canvas;
            public RenderMode Mode;
            public Camera WorldCamera;
        }

        /// <summary>
        /// Authored popup prefabs usually ship hidden: CanvasGroup.alpha=0, an intro Animator that
        /// has not run, or a zeroed localScale. Forcing them visible for the duration of the capture
        /// keeps the agent from "fixing" the prefab just to make screenshots work.
        /// </summary>
        private static void ForceVisibleForCapture(Canvas canvas, List<Action> restores)
        {
            foreach (var group in canvas.GetComponentsInChildren<CanvasGroup>(true))
            {
                if (group == null) continue;
                if (group.alpha >= 0.99f && group.gameObject.activeSelf) continue;
                var g = group;
                var alpha = g.alpha;
                var wasActive = g.gameObject.activeSelf;
                restores.Add(() =>
                {
                    if (g == null) return;
                    g.alpha = alpha;
                    g.gameObject.SetActive(wasActive);
                });
                g.alpha = 1f;
                if (!wasActive) g.gameObject.SetActive(true);
            }

            foreach (var animator in canvas.GetComponentsInChildren<Animator>(true))
            {
                if (animator == null || !animator.enabled) continue;
                var a = animator;
                restores.Add(() => { if (a != null) a.enabled = true; });
                a.enabled = false;
            }

            foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                if (rect == null) continue;
                var scale = rect.localScale;
                if (scale.x > 0.01f && scale.y > 0.01f) continue;
                var r = rect;
                restores.Add(() => { if (r != null) r.localScale = scale; });
                r.localScale = new Vector3(scale.x <= 0.01f ? 1f : scale.x, scale.y <= 0.01f ? 1f : scale.y, 1f);
            }
        }

        /// <summary>
        /// ScreenSpaceOverlay UI is invisible to Camera.Render(). Temporarily switch active
        /// Overlay canvases to ScreenSpaceCamera, render, then restore — this is the reliable
        /// path for agent visual verification on Unity 2021.3.
        /// </summary>
        private static byte[] CaptureUiViaCamera(int width, int height, out int actualW, out int actualH)
        {
            actualW = width;
            actualH = height;
            var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (camera == null) return null;

            var restored = new List<CanvasCaptureState>();
            var restores = new List<Action>();
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (canvas == null || !canvas.isActiveAndEnabled) continue;

                    // A popup hidden behind alpha=0 / a zeroed scale is invisible in every render mode,
                    // so visibility is forced on every canvas, not just the ones being re-pointed.
                    ForceVisibleForCapture(canvas, restores);

                    // ScreenSpaceCamera without a camera assigned falls back to Overlay rendering,
                    // which Camera.Render() also cannot see.
                    var needsCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
                        || (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null);
                    if (!needsCamera) continue;

                    restored.Add(new CanvasCaptureState
                    {
                        Canvas = canvas,
                        Mode = canvas.renderMode,
                        WorldCamera = canvas.worldCamera
                    });
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                }

                Canvas.ForceUpdateCanvases();

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                var prevTarget = camera.targetTexture;
                var prevActive = RenderTexture.active;
                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                camera.targetTexture = prevTarget;
                RenderTexture.active = prevActive;

                // Camera.Render into an RT is already upright — do not flip.
                if (!TextureHasVisualVariance(tex))
                    return null;

                actualW = width;
                actualH = height;
                return tex.EncodeToPNG();
            }
            catch
            {
                return null;
            }
            finally
            {
                for (int i = restores.Count - 1; i >= 0; i--)
                {
                    try { restores[i]?.Invoke(); } catch { }
                }
                foreach (var state in restored)
                {
                    if (state.Canvas == null) continue;
                    state.Canvas.renderMode = state.Mode;
                    state.Canvas.worldCamera = state.WorldCamera;
                }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void FlipTextureVertically(Texture2D tex)
        {
            var pixels = tex.GetPixels();
            int w = tex.width, h = tex.height;
            for (int y = 0; y < h / 2; y++)
            {
                int topRow = y * w;
                int bottomRow = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    var tmp = pixels[topRow + x];
                    pixels[topRow + x] = pixels[bottomRow + x];
                    pixels[bottomRow + x] = tmp;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
        }

        private static bool TextureHasVisualVariance(Texture2D tex)
        {
            if (tex == null || tex.width < 2 || tex.height < 2) return false;
            float min = 1f, max = 0f;
            const int samples = 12;
            for (int y = 0; y < samples; y++)
            {
                for (int x = 0; x < samples; x++)
                {
                    var c = tex.GetPixel(
                        Mathf.RoundToInt((tex.width - 1) * x / (float)(samples - 1)),
                        Mathf.RoundToInt((tex.height - 1) * y / (float)(samples - 1)));
                    var luminance = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    min = Mathf.Min(min, luminance);
                    max = Mathf.Max(max, luminance);
                }
            }
            return max - min > 0.02f;
        }

        private static bool PngHasVisualVariance(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                return ImageConversion.LoadImage(tex, bytes, false) && TextureHasVisualVariance(tex);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        private static bool SceneHasVisibleUi()
        {
            return UnityEngine.Object.FindObjectsOfType<Canvas>()
                .Any(canvas => canvas != null && canvas.isActiveAndEnabled
                    && canvas.GetComponentsInChildren<Graphic>(false)
                        .Any(graphic => graphic != null && graphic.isActiveAndEnabled && graphic.color.a > 0.01f));
        }

        private static void EnsurePreviewCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
                {
                    if (cam.isActiveAndEnabled) { camera = cam; break; }
                }
            }

            if (camera == null)
            {
                var camGO = new GameObject("Preview Camera");
                Undo.RegisterCreatedObjectUndo(camGO, "Create Preview Camera");
                camGO.tag = "MainCamera";
                camera = camGO.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                camera.cullingMask = -1;
                camera.orthographic = false;
                camera.depth = 0;
                camera.transform.position = new Vector3(0, 0, -10);
            }

            var eventSystem = UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                var esGO = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }
        }

        private static void ResolveScreenshotSize(JObject args, out int width, out int height)
        {
            var cfg = ProjectConfig.Current;
            int designW = cfg?.basicInfo?.designWidth > 0 ? cfg.basicInfo.designWidth : 1080;
            int designH = cfg?.basicInfo?.designHeight > 0 ? cfg.basicInfo.designHeight : 1920;

            // A portrait design (1080x2340) capped at 1080 left the popup itself around 500x370 px,
            // where a brush-stroke banner and a flat colour block are indistinguishable to the
            // model. 1600 keeps the payload reasonable while making sprite shapes readable.
            const int maxLong = 1600;
            float scale = 1f;
            int longSide = Math.Max(designW, designH);
            if (longSide > maxLong) scale = maxLong / (float)longSide;

            int defaultW = Mathf.Max(64, Mathf.RoundToInt(designW * scale));
            int defaultH = Mathf.Max(64, Mathf.RoundToInt(designH * scale));

            width = args["width"] != null ? Int(args, "width", defaultW) : defaultW;
            height = args["height"] != null ? Int(args, "height", defaultH) : defaultH;
            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);
        }

        private static void DeleteScreenshotIfExists(string absolutePath)
        {
            try
            {
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                    var meta = absolutePath + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
            }
            catch { }
        }

        private string DoTakeScreenshot(JObject args)
        {
            var filename = Str(args, "filename", "screenshot");
            ResolveScreenshotSize(args, out var width, out var height);

            try
            {
                EnsurePreviewCamera();

                int actualW = width, actualH = height;
                string capturePath = null;

                // Primary: temporary ScreenSpaceCamera render (captures Overlay UI reliably).
                var bytes = CaptureUiViaCamera(width, height, out actualW, out actualH);
                if (bytes != null) capturePath = "camera";

                // Secondary: GameView RT (may be stale on Unity 2021.3 — only accept if non-uniform).
                if (bytes == null)
                {
                    var gvBytes = TryCaptureGameViewRT(out actualW, out actualH);
                    if (gvBytes != null && PngHasVisualVariance(gvBytes))
                    {
                        bytes = gvBytes;
                        capturePath = "gameview";
                    }
                }

                var screenshotDir = Path.Combine(Application.dataPath, "Screenshots");
                if (!Directory.Exists(screenshotDir)) Directory.CreateDirectory(screenshotDir);
                var filePath = Path.Combine(screenshotDir, filename + ".png");
                var assetPath = $"Assets/Screenshots/{filename}.png";

                if (bytes == null || !PngHasVisualVariance(bytes))
                {
                    DeleteScreenshotIfExists(filePath);
                    AssetDatabase.Refresh();
                    var reason = SceneHasVisibleUi()
                        ? "Screenshot capture is nearly uniform even though visible UI Graphics exist. " +
                          "Do not analyze any previous file at this path — retake after fixing capture, or inspect hierarchy."
                        : "Screenshot capture failed (no non-uniform image). Ensure a Canvas with visible UI exists.";
                    return new JObject
                    {
                        ["success"] = false,
                        ["error"] = reason,
                        ["validCapture"] = false
                    }.ToString();
                }

                File.WriteAllBytes(filePath, bytes);
                AssetDatabase.Refresh();

                var captureId = Guid.NewGuid().ToString("N");
                _captureRevisions[assetPath] = _sceneRevision;
                var result = new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Screenshot saved to {assetPath} ({actualW}x{actualH}, via {capturePath})",
                    ["filePath"] = assetPath,
                    ["width"] = actualW,
                    ["height"] = actualH,
                    ["captureId"] = captureId,
                    ["validCapture"] = true,
                    ["capturePath"] = capturePath
                };

                if (BuilderSettings.Get().SupportsVision)
                    result["base64"] = Convert.ToBase64String(bytes);

                return result.ToString();
            }
            catch (Exception e)
            {
                return Error($"Screenshot failed: {e.Message}");
            }
        }

        private string DoAnalyzeScreenshot(JObject args)
        {
            var path = Str(args, "screenshotPath");
            var prompt = Str(args, "prompt", null);
            var refPath = Str(args, "referenceImagePath", null);

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var fullPath = Path.Combine(projectRoot, path);
            if (!File.Exists(fullPath))
                return Error($"Screenshot not found: {path}");

            var stale = CheckCaptureFreshness(path, "analyze");
            if (stale != null) return Error(stale);

            var settings = BuilderSettings.Get();
            if (!settings.SupportsVision)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "Current model does not support vision. " +
                        "Visual screenshot analysis is unavailable. " +
                        "Use `inspect_hierarchy` and `inspect_components` to verify the UI structure, " +
                        "check RectTransform sizes/positions, Mask/ScrollRect settings, and element visibility. " +
                        "Screenshot was saved to: " + path
                }.ToString();
            }

            var bytes = File.ReadAllBytes(fullPath);
            if (!PngHasVisualVariance(bytes))
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = $"Screenshot at '{path}' is nearly uniform (blank/gray) and cannot be used for visual verification. " +
                        "Call `take_screenshot` again and only analyze a capture that returns success=true / validCapture=true. " +
                        "Do NOT claim Matches against a blank image."
                }.ToString();
            }

            var base64 = Convert.ToBase64String(bytes);
            var imageUrl = $"data:image/png;base64,{base64}";

            var images = new JArray { imageUrl };
            bool hasRef = false;
            string framingNote = null;

            if (!string.IsNullOrEmpty(refPath))
            {
                var refFullPath = Path.Combine(projectRoot, refPath);
                if (File.Exists(refFullPath))
                {
                    var refBytes = File.ReadAllBytes(refFullPath);
                    images.Add($"data:image/png;base64,{Convert.ToBase64String(refBytes)}");
                    hasRef = true;
                    framingNote = BuildFramingNote(bytes, refBytes);
                }
            }

            string message;
            if (!string.IsNullOrEmpty(prompt))
            {
                message = prompt;
                if (hasRef)
                    message += "\n\nA reference/design mockup image is also provided. Compare the current build against it.";
            }
            else if (hasRef)
            {
                message = "Compare the current UI build (Image 1) against the design mockup (Image 2). "
                    + "HARD RULE: If Image 1 is blank, nearly solid gray/black, or does not show the built UI, "
                    + "report CAPTURE FAILURE immediately and do NOT claim Matches for any category.\n"
                    + "FIRST, list every element you can actually SEE in Image 1 (icons, check marks, labels, frames). "
                    + "Anything you cannot see in Image 1 is MISSING, even if you created it earlier — "
                    + "never describe an element from your build plan as if it were rendered.\n"
                    + (framingNote != null ? framingNote + "\n" : "")
                    + "For EACH of the following categories, state whether it matches or list the specific difference:\n"
                    + "1. **Background/Frame**: Is the popup background sprite correct? Are decorative borders visible?\n"
                    + "2. **Header/Title bar**: Is there a colored banner behind the title? Does the text have outline/shadow?\n"
                    + "3. **Tab/Button styling**: Do selected/unselected tabs match the design's colors and shape?\n"
                    + "4. **List items**: Are icons, labels, and values correctly positioned and sized?\n"
                    + "5. **Text colors & fonts**: Do text colors, sizes, and weights match the design?\n"
                    + "6. **Spacing & alignment**: Are margins, paddings, and element gaps proportionally correct?\n"
                    + "7. **Close button**: Is it positioned and styled as shown in the design?\n"
                    + "8. **Overlay/Dimming**: Is the background overlay present if shown in the design?\n\n"
                    + "For each mismatch, describe the fix needed (e.g. 'Header needs a blue banner Image behind the title text').\n\n"
                    + "THEN call `report_visual_verdict` with screenshotPath='" + path + "', matches, "
                    + "visibleElements (what you actually see in Image 1), missingElements and mismatches.";
            }
            else
            {
                message = "Analyze this UI screenshot for visual quality. "
                    + "HARD RULE: If the image is blank/nearly solid gray, report CAPTURE FAILURE — do not invent layout observations.\n"
                    + "Check:\n"
                    + "1. Layout alignment and spacing consistency\n"
                    + "2. Text readability (overlap, truncation, color contrast)\n"
                    + "3. Missing or broken sprites\n"
                    + "4. Background/frame integrity (nine-slice stretching issues)\n"
                    + "5. Button/tab visual states\n"
                    + "6. Overall proportions and visual balance\n\n"
                    + "List specific elements that need fixing with actionable descriptions.";
            }

            return new JObject
            {
                ["success"] = true,
                ["message"] = message,
                ["_multimodal"] = true,
                ["_images"] = images,
                ["validCapture"] = true,
                ["_hint"] = "success=true only means the image was delivered — it is NOT a passing verdict. " +
                    "After you have looked at the image you MUST call `report_visual_verdict` for this exact screenshotPath."
            }.ToString();
        }

        /// <summary>
        /// Structured outcome of a visual comparison. `analyze_screenshot` only proves an image was
        /// delivered to the model; this is where the model has to commit to what it actually saw, so
        /// the completion gate can distinguish "verified" from "described from memory".
        /// </summary>
        private string DoReportVisualVerdict(JObject args)
        {
            var path = Str(args, "screenshotPath");
            if (string.IsNullOrEmpty(path))
                return Error("Missing 'screenshotPath'. Report the verdict for the screenshot you just analyzed.");

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!File.Exists(Path.Combine(projectRoot, path)))
                return Error($"Screenshot not found: {path}. Report the verdict for a screenshot that exists on disk.");

            var stale = CheckCaptureFreshness(path, "report the verdict for");
            if (stale != null) return Error(stale);

            if (args["matches"] == null)
                return Error("Missing 'matches'. State explicitly whether the build matches the design mockup.");

            var matches = Bool(args, "matches");
            var claims = ParseVisibleClaims(args["visibleElements"]);
            var missing = StringList(args, "missingElements");
            var mismatches = StringList(args, "mismatches");

            if (claims.Count == 0)
            {
                return Error("'visibleElements' is empty or malformed. Each entry needs a label plus the normalized " +
                    "bounding box (x, y, width, height in 0-1, top-left origin) where you SEE that element in the screenshot. " +
                    "A verdict without located observations is not verification.");
            }

            if (matches && (missing.Count > 0 || mismatches.Count > 0))
            {
                return Error($"Contradictory verdict: matches=true but you listed {missing.Count} missing element(s) " +
                    $"and {mismatches.Count} mismatch(es). Set matches=false and fix them, " +
                    "or drop the entries if they are not real differences.");
            }

            var unsubstantiated = FindUnsubstantiatedClaims(Path.Combine(projectRoot, path), claims);
            if (unsubstantiated.Count > 0)
            {
                return Error("These claimed elements are not detectable at the coordinates you gave — the region is " +
                    "indistinguishable from its surroundings: " + string.Join("; ", unsubstantiated) + ". " +
                    "Either give the correct bounding box, or move the entry to `missingElements` and set matches=false.");
            }

            if (matches)
            {
                var placeholders = FindUntexturedIconImages();
                if (placeholders.Count > 0)
                {
                    return Error($"matches=true is rejected: {placeholders.Count} icon-sized Image(s) still have no sprite " +
                        "and render as flat colour blocks — " + string.Join(", ", placeholders) + ". " +
                        "A colour block is not the mockup's artwork. Assign the real sprites, " +
                        "or set matches=false and list them in `mismatches`. " +
                        "Change ONLY the nodes listed here: re-applying a sprite you previously removed on purpose, " +
                        "or touching elements that already matched, makes the build worse. " +
                        "After the fix you MUST take a new screenshot — the current one no longer shows the scene.");
                }

                var violations = CollectStyleViolations();
                if (violations.Count > 0)
                {
                    return Error("matches=true is rejected — the scene still carries unstyled defaults that no mockup asks for:\n"
                        + string.Join("\n", violations.Select(v => "- " + v))
                        + "\nFix ONLY these nodes (set_text_properties_batch for text, set_image_batch for graphics, " +
                        "configure_selectable_states for tab/selection states) and leave every element that already matched untouched, " +
                        "then re-run take_screenshot → analyze_screenshot → report_visual_verdict.");
                }
            }

            var result = new JObject
            {
                ["success"] = true,
                ["screenshotPath"] = path,
                ["matches"] = matches,
                ["visibleElements"] = new JArray(claims.Select(c => (JToken)c.Label)),
                ["missingElements"] = new JArray(missing.Select(v => (JToken)v)),
                ["mismatches"] = new JArray(mismatches.Select(v => (JToken)v))
            };

            result["message"] = matches
                ? "Verdict recorded: build matches the design."
                : $"Verdict recorded: {missing.Count} missing, {mismatches.Count} mismatched. " +
                  "Fix ONLY these items — do not rebuild the UI from scratch — " +
                  "then take_screenshot → analyze_screenshot → report_visual_verdict again.";

            return result.ToString();
        }

        private struct VisibleClaim
        {
            public string Label;
            public Rect Region;
        }

        private static List<VisibleClaim> ParseVisibleClaims(JToken token)
        {
            var claims = new List<VisibleClaim>();
            var arr = token as JArray;
            if (arr == null && token != null && token.Type == JTokenType.String)
            {
                // Some models send batch payloads as a JSON-encoded string.
                try { arr = JArray.Parse(token.ToString()); } catch { }
            }
            if (arr == null) return claims;

            foreach (var entry in arr)
            {
                if (!(entry is JObject obj)) continue;
                var label = obj["label"]?.ToString();
                if (string.IsNullOrWhiteSpace(label)) continue;
                if (obj["x"] == null || obj["y"] == null || obj["width"] == null || obj["height"] == null) continue;

                var region = new Rect(
                    (float)obj["x"], (float)obj["y"],
                    (float)obj["width"], (float)obj["height"]);
                if (region.width <= 0.001f || region.height <= 0.001f) continue;

                claims.Add(new VisibleClaim { Label = label.Trim(), Region = region });
            }
            return claims;
        }

        /// <summary>
        /// Checks each claimed element against the pixels. A region counts as substantiated when it
        /// either contains internal detail or clearly differs from the ring of pixels around it;
        /// anything else means the model is describing something the screenshot does not show.
        /// </summary>
        private static List<string> FindUnsubstantiatedClaims(string screenshotFullPath, List<VisibleClaim> claims)
        {
            var failed = new List<string>();
            var tex = LoadTextureFromFile(screenshotFullPath);
            if (tex == null) return failed;

            try
            {
                foreach (var claim in claims)
                {
                    if (claim.Region.x < -0.01f || claim.Region.y < -0.01f
                        || claim.Region.xMax > 1.01f || claim.Region.yMax > 1.01f)
                    {
                        failed.Add($"{claim.Label} (bounding box outside the image)");
                        continue;
                    }

                    if (!RegionIsDistinguishable(tex, claim.Region))
                        failed.Add(claim.Label);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            return failed;
        }

        private static bool RegionIsDistinguishable(Texture2D tex, Rect normalized)
        {
            // Normalized rects use a top-left origin; Texture2D sampling starts bottom-left.
            int x0 = Mathf.Clamp(Mathf.RoundToInt(normalized.x * tex.width), 0, tex.width - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(normalized.xMax * tex.width), x0 + 1, tex.width);
            int yTop = Mathf.Clamp(Mathf.RoundToInt((1f - normalized.yMax) * tex.height), 0, tex.height - 1);
            int yBottom = Mathf.Clamp(Mathf.RoundToInt((1f - normalized.y) * tex.height), yTop + 1, tex.height);

            var inner = SampleRegion(tex, x0, yTop, x1, yBottom, out var innerRange);
            if (innerRange > 0.05f) return true;

            int padX = Mathf.Max(2, (x1 - x0) / 3);
            int padY = Mathf.Max(2, (yBottom - yTop) / 3);
            var outer = SampleRegion(tex,
                Mathf.Max(0, x0 - padX), Mathf.Max(0, yTop - padY),
                Mathf.Min(tex.width, x1 + padX), Mathf.Min(tex.height, yBottom + padY),
                out _);

            var diff = Mathf.Abs(inner.r - outer.r) + Mathf.Abs(inner.g - outer.g) + Mathf.Abs(inner.b - outer.b);
            return diff > 0.06f;
        }

        private static Color SampleRegion(Texture2D tex, int x0, int y0, int x1, int y1, out float luminanceRange)
        {
            const int samples = 8;
            float r = 0, g = 0, b = 0, min = 1f, max = 0f;
            int count = 0;

            for (int sy = 0; sy < samples; sy++)
            {
                for (int sx = 0; sx < samples; sx++)
                {
                    int px = Mathf.Clamp(x0 + (x1 - x0) * sx / (samples - 1), 0, tex.width - 1);
                    int py = Mathf.Clamp(y0 + (y1 - y0) * sy / (samples - 1), 0, tex.height - 1);
                    var c = tex.GetPixel(px, py);
                    r += c.r; g += c.g; b += c.b;
                    var luminance = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    min = Mathf.Min(min, luminance);
                    max = Mathf.Max(max, luminance);
                    count++;
                }
            }

            luminanceRange = max - min;
            return count == 0 ? Color.black : new Color(r / count, g / count, b / count);
        }

        /// <summary>
        /// Icon-sized Images without a sprite are the placeholder colour blocks that keep getting
        /// reported as finished artwork. Large sprite-less Images are left alone — flat panels and
        /// dimming overlays are legitimate.
        /// </summary>
        private static List<string> FindUntexturedIconImages()
        {
            const float iconMaxSize = 220f;
            var names = new List<string>();

            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                foreach (var img in canvas.GetComponentsInChildren<Image>(false))
                {
                    if (img == null || !img.isActiveAndEnabled) continue;
                    if (img.sprite != null || img.color.a <= 0.01f) continue;
                    if (IsLegitimateFlatBackground(img)) continue;

                    var size = img.rectTransform.rect.size;
                    if (size.x <= 1f || size.y <= 1f) continue;
                    if (size.x > iconMaxSize || size.y > iconMaxSize) continue;

                    names.Add(GetHierarchyPath(img.gameObject));
                    if (names.Count >= 12) return names;
                }
            }
            return names;
        }

        /// <summary>
        /// A checkbox/button background drawn as a deliberate flat colour is a valid reproduction of
        /// many mockups, unlike a missing icon. Flagging it made the agent re-apply the wrong sprite
        /// it had just (correctly) removed, so backgrounds are judged by the button-background check
        /// instead — that one only fires on untouched default colours.
        /// </summary>
        private static bool IsLegitimateFlatBackground(Image img)
        {
            var selectable = img.GetComponentInParent<Selectable>();
            if (selectable != null && selectable.targetGraphic == img) return true;

            var name = img.gameObject.name.ToLowerInvariant();
            return name.Contains("background") || name.Contains("bg")
                || name.Contains("panel") || name.Contains("frame")
                || name.Contains("bar") || name.Contains("fill");
        }

        /// <summary>
        /// Deterministic "the build is still at factory defaults" checks. A model looking at a small
        /// screenshot cannot reliably judge font size or wiring, so these are read off the scene
        /// instead of the pixels — they are exactly the differences users report after a "verified" build.
        /// </summary>
        private static List<string> CollectStyleViolations()
        {
            var violations = new List<string>();
            var texts = CollectVisibleTexts();

            var empty = texts.Where(t => string.IsNullOrWhiteSpace(t.Content) && !t.OnTexturedSelectable)
                .Select(t => t.Path).Take(8).ToList();
            if (empty.Count > 0)
                violations.Add($"{empty.Count} visible text element(s) render nothing because their text is empty: {string.Join(", ", empty)}. Pass the exact `text` content.");

            // An empty label on a button whose artwork already draws the glyph (+ / - / arrow) must not be
            // "fixed" by writing text into it — that stamps a second glyph over the sprite.
            var emptyOnArtwork = texts.Where(t => string.IsNullOrWhiteSpace(t.Content) && t.OnTexturedSelectable)
                .Select(t => t.Path).Take(8).ToList();
            if (emptyOnArtwork.Count > 0)
                violations.Add($"{emptyOnArtwork.Count} button label(s) are empty while the button already has a sprite: {string.Join(", ", emptyOnArtwork)}. " +
                    "If the sprite itself shows the glyph (a plus/minus/arrow icon button), DELETE the empty label with `destroy_object` — " +
                    "do NOT write text on top of the artwork. Only set `text` when the mockup really shows a word there.");

            if (texts.Count >= 4)
            {
                var distinctSizes = texts.Select(t => Mathf.RoundToInt(t.FontSize)).Distinct().Count();
                if (distinctSizes == 1)
                    violations.Add($"All {texts.Count} text elements share one font size ({Mathf.RoundToInt(texts[0].FontSize)}). " +
                        "A mockup always has a hierarchy (title > section label > body > caption) — set fontSize per role.");
            }

            var flatButtons = FindUnstyledButtonBackgrounds();
            if (flatButtons.Count > 0)
                violations.Add($"{flatButtons.Count} button background(s) are still a plain untextured rectangle: {string.Join(", ", flatButtons)}. Assign the real button sprite (type=Sliced) or tint them to the mockup's colour.");

            var unwiredInputs = FindUnwiredInputFields();
            if (unwiredInputs.Count > 0)
                violations.Add($"{unwiredInputs.Count} InputField(s) are not wired: {string.Join(", ", unwiredInputs)}. " +
                    "Without a textComponent/placeholder the field is just a white box. Recreate it with `create_inputfield`.");

            var emptyInputs = FindEmptyInputFields();
            if (emptyInputs.Count > 0)
                violations.Add($"{emptyInputs.Count} InputField(s) render nothing — text and placeholder are both empty: {string.Join(", ", emptyInputs)}. " +
                    "Write the mockup's content to the InputField itself (`set_text_properties` on the field, or on its Text child — the content is routed to the component), " +
                    "or give it a placeholder string. A blank field is just a coloured box.");

            var unstyledSelections = FindSelectablesWithoutStateDifference();
            if (unstyledSelections.Count > 0)
                violations.Add($"{unstyledSelections.Count} tab/selectable element(s) have no visual difference between states: {string.Join(", ", unstyledSelections)}. " +
                    "Use `configure_selectable_states` (selected/highlighted sprite or colour) so the selected tab/option actually looks selected.");

            var brokenSliders = FindIncompleteSliders();
            if (brokenSliders.Count > 0)
                violations.Add($"{brokenSliders.Count} Slider(s) do not read as a bar: {string.Join(", ", brokenSliders)}. " +
                    "A Slider with no Fill/Handle graphics is invisible — the mockup's bar is simply absent where the hierarchy says it exists. " +
                    "Recreate it with `create_slider` (backgroundSpritePath / fillSpritePath / handleSpritePath build and wire Background, Fill and Handle), " +
                    "or give the existing Background/Fill/Handle real sprites or tints with `set_image_batch`.");

            var collisions = FindOverlappingButtonContent();
            if (collisions.Count > 0)
                violations.Add($"{collisions.Count} button icon(s) are drawn on top of the button's own label text: {string.Join(", ", collisions)}. " +
                    "Move the icon out of the glyphs — give the label an explicit rect beside the icon, or recreate the button with " +
                    "`create_button` (`iconSpritePath` + `iconPosition`), which reserves separate areas for icon and label.");

            return violations;
        }

        private struct TextProbe
        {
            public string Path;
            public string Content;
            public float FontSize;
            /// <summary>Label of a Selectable whose background already carries artwork (an icon button).</summary>
            public bool OnTexturedSelectable;
        }

        private static List<TextProbe> CollectVisibleTexts()
        {
            var probes = new List<TextProbe>();
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(false))
                {
                    if (graphic == null || !graphic.isActiveAndEnabled) continue;
                    var type = graphic.GetType();
                    var textProp = type.GetProperty("text");
                    if (textProp == null || textProp.PropertyType != typeof(string)) continue;

                    // An input field's own text/placeholder nodes are legitimately empty.
                    if (IsInsideInputField(graphic.transform)) continue;

                    var fontSizeProp = type.GetProperty("fontSize");
                    float fontSize = 0f;
                    var raw = fontSizeProp?.GetValue(graphic);
                    if (raw is float f) fontSize = f;
                    else if (raw is int i) fontSize = i;

                    probes.Add(new TextProbe
                    {
                        Path = GetHierarchyPath(graphic.gameObject),
                        Content = textProp.GetValue(graphic) as string,
                        FontSize = fontSize,
                        OnTexturedSelectable = IsLabelOfTexturedSelectable(graphic.transform)
                    });
                }
            }
            return probes;
        }

        private static bool IsLabelOfTexturedSelectable(Transform t)
        {
            var selectable = t.GetComponentInParent<Selectable>();
            if (selectable == null || selectable.gameObject == t.gameObject) return false;
            var img = selectable.targetGraphic as Image ?? selectable.GetComponent<Image>();
            return img != null && img.sprite != null;
        }

        /// <summary>
        /// A Slider that was created as a bare component has no Background / Fill / Handle, so the
        /// mockup's bar is missing from the capture while the hierarchy still lists the node — the
        /// exact combination that gets reported as "slider row delivered".
        /// </summary>
        private static List<string> FindIncompleteSliders()
        {
            var names = new List<string>();
            foreach (var slider in UnityEngine.Object.FindObjectsOfType<Slider>())
            {
                if (slider == null || !slider.isActiveAndEnabled) continue;
                var defect = DescribeSliderDefect(slider);
                if (defect == null) continue;

                names.Add($"{GetHierarchyPath(slider.gameObject)} ({defect})");
                if (names.Count >= 8) break;
            }
            return names;
        }

        private static string DescribeSliderDefect(Slider slider)
        {
            if (slider.fillRect == null && slider.handleRect == null)
                return "no fillRect and no handleRect";

            bool hasVisiblePart = slider.GetComponentsInChildren<Graphic>(false)
                .Any(g => g != null && g.isActiveAndEnabled && g.color.a > 0.01f);
            if (!hasVisiblePart) return "no visible Image underneath";

            if (slider.fillRect != null && slider.fillRect.GetComponent<Graphic>() == null)
                return "fillRect has no Image";
            if (slider.handleRect != null && slider.handleRect.GetComponent<Graphic>() == null)
                return "handleRect has no Image";

            var untextured = new List<string>();
            if (IsUntexturedWhite(slider.fillRect)) untextured.Add("Fill");
            if (IsUntexturedWhite(slider.handleRect)) untextured.Add("Handle");
            if (untextured.Count > 0)
                return string.Join(" and ", untextured) + " still untextured white";

            return null;
        }

        private static bool IsUntexturedWhite(RectTransform rt)
        {
            var img = rt != null ? rt.GetComponent<Image>() : null;
            return img != null && img.isActiveAndEnabled && img.sprite == null && IsApproximately(img.color, Color.white);
        }

        /// <summary>
        /// An icon dropped into a button whose label stretches across the whole face lands on the
        /// glyphs: the price text ends up half-hidden behind the coin. Compared against the text's
        /// PREFERRED size (where the glyphs actually are), not the label rect, so a normal
        /// icon-left/label-centre button is not flagged.
        /// </summary>
        private static List<string> FindOverlappingButtonContent()
        {
            var names = new List<string>();
            foreach (var selectable in UnityEngine.Object.FindObjectsOfType<Selectable>())
            {
                if (selectable == null || !selectable.isActiveAndEnabled) continue;

                var faceArea = RectArea(GetWorldRect(selectable.transform as RectTransform));
                foreach (var label in selectable.GetComponentsInChildren<Graphic>(false))
                {
                    if (label == null || !label.isActiveAndEnabled) continue;
                    if (!TryGetRenderedTextBox(label, out var textBox)) continue;

                    foreach (var icon in selectable.GetComponentsInChildren<Image>(false))
                    {
                        if (icon == null || !icon.isActiveAndEnabled || icon.sprite == null) continue;
                        if (icon == selectable.targetGraphic) continue;
                        if (icon.transform == selectable.transform) continue;
                        if (icon.transform.IsChildOf(label.transform)) continue;

                        var iconRect = GetWorldRect(icon.rectTransform);
                        var iconArea = RectArea(iconRect);
                        if (iconArea <= 0.01f) continue;
                        // A full-face image is a background layer, not an icon sitting on the text.
                        if (faceArea > 0.01f && iconArea > faceArea * 0.6f) continue;

                        if (IntersectionArea(iconRect, textBox) < iconArea * 0.3f) continue;

                        names.Add($"{GetHierarchyPath(icon.gameObject)} over {GetHierarchyPath(label.gameObject)}");
                        if (names.Count >= 8) return names;
                    }
                }
            }
            return names;
        }

        private static bool TryGetRenderedTextBox(Graphic graphic, out Rect box)
        {
            box = default;
            var layout = graphic as ILayoutElement;
            if (layout == null) return false;

            var textProp = graphic.GetType().GetProperty("text");
            if (textProp == null || textProp.PropertyType != typeof(string)) return false;
            if (string.IsNullOrWhiteSpace(textProp.GetValue(graphic) as string)) return false;

            var rect = graphic.rectTransform.rect;
            if (rect.width <= 0.01f || rect.height <= 0.01f) return false;

            var world = GetWorldRect(graphic.rectTransform);
            var glyphWidth = Mathf.Clamp(layout.preferredWidth, 0f, rect.width);
            var glyphHeight = Mathf.Clamp(layout.preferredHeight, 0f, rect.height);
            if (glyphWidth <= 0.01f || glyphHeight <= 0.01f) { box = world; return true; }

            // The glyph block is assumed centred in its rect — a left/right aligned label can only make
            // this narrower than reality, which costs a detection but never invents one.
            var width = world.width * (glyphWidth / rect.width);
            var height = world.height * (glyphHeight / rect.height);
            box = new Rect(world.center.x - width * 0.5f, world.center.y - height * 0.5f, width, height);
            return true;
        }

        private static Rect GetWorldRect(RectTransform rt)
        {
            if (rt == null) return default;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var min = new Vector2(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y));
            var max = new Vector2(Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
            return new Rect(min, max - min);
        }

        private static float RectArea(Rect r) => Mathf.Max(0f, r.width) * Mathf.Max(0f, r.height);

        private static float IntersectionArea(Rect a, Rect b)
        {
            var width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            var height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return width <= 0f || height <= 0f ? 0f : width * height;
        }

        private static List<string> FindUnstyledButtonBackgrounds()
        {
            var names = new List<string>();
            foreach (var selectable in UnityEngine.Object.FindObjectsOfType<Selectable>())
            {
                if (selectable == null || !selectable.isActiveAndEnabled) continue;
                // A Slider's targetGraphic is its handle, not a button face — it is judged by the slider check.
                if (selectable is Slider) continue;
                var img = selectable.targetGraphic as Image;
                if (img == null) img = selectable.GetComponent<Image>();
                if (img == null || !img.isActiveAndEnabled) continue;
                if (img.sprite != null) continue;
                if (img.color.a <= 0.01f) continue;

                // A deliberately tinted flat button is a legitimate choice; an untouched white or
                // the old default blue means nobody ever styled it.
                bool looksUntouched = IsApproximately(img.color, Color.white)
                    || IsApproximately(img.color, new Color(0.2f, 0.45f, 0.85f));
                if (!looksUntouched) continue;

                names.Add(GetHierarchyPath(img.gameObject));
                if (names.Count >= 8) break;
            }
            return names;
        }

        private static bool IsApproximately(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        private static List<string> FindUnwiredInputFields()
        {
            var names = new List<string>();
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                foreach (var comp in canvas.GetComponentsInChildren<Component>(false))
                {
                    if (comp == null) continue;
                    var type = comp.GetType();
                    if (!IsInputFieldType(type)) continue;

                    var textComponent = type.GetProperty("textComponent")?.GetValue(comp);
                    var placeholder = type.GetProperty("placeholder")?.GetValue(comp);
                    if (textComponent != null && placeholder != null) continue;

                    names.Add(GetHierarchyPath(comp.gameObject));
                    if (names.Count >= 8) return names;
                }
            }
            return names;
        }

        /// <summary>
        /// An InputField whose own text is empty AND whose placeholder is empty draws nothing at
        /// all. Its label nodes are skipped by the generic empty-text check (they are legitimately
        /// empty on their own), so the field has to be judged as a whole.
        /// </summary>
        private static List<string> FindEmptyInputFields()
        {
            var names = new List<string>();
            foreach (var canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                foreach (var comp in canvas.GetComponentsInChildren<Component>(false))
                {
                    if (comp == null) continue;
                    var type = comp.GetType();
                    if (!IsInputFieldType(type)) continue;

                    var value = type.GetProperty("text")?.GetValue(comp) as string;
                    if (!string.IsNullOrEmpty(value)) continue;

                    var placeholder = type.GetProperty("placeholder")?.GetValue(comp) as Component;
                    var placeholderText = placeholder != null
                        ? placeholder.GetType().GetProperty("text")?.GetValue(placeholder) as string
                        : null;
                    if (!string.IsNullOrEmpty(placeholderText)) continue;

                    names.Add(GetHierarchyPath(comp.gameObject));
                    if (names.Count >= 8) return names;
                }
            }
            return names;
        }

        private static bool IsInsideInputField(Transform t)
        {
            for (var current = t; current != null; current = current.parent)
            {
                foreach (var comp in current.GetComponents<Component>())
                {
                    if (comp != null && IsInputFieldType(comp.GetType())) return true;
                }
            }
            return false;
        }

        private static bool IsInputFieldType(Type type) => ComponentHelper.IsInputFieldType(type);

        /// <summary>
        /// Toggles are how selected tabs / chosen options are expressed. One with no state sprite,
        /// no state tint and no wired graphic renders identically whether or not it is selected.
        /// </summary>
        private static List<string> FindSelectablesWithoutStateDifference()
        {
            var names = new List<string>();
            foreach (var toggle in UnityEngine.Object.FindObjectsOfType<Toggle>())
            {
                if (toggle == null || !toggle.isActiveAndEnabled) continue;

                if (toggle.graphic != null) continue;

                var state = toggle.spriteState;
                bool hasStateSprite = state.highlightedSprite != null || state.pressedSprite != null
                    || state.selectedSprite != null || state.disabledSprite != null;
                if (hasStateSprite && toggle.transition == Selectable.Transition.SpriteSwap) continue;

                var colors = toggle.colors;
                bool hasColorDifference = colors.selectedColor != colors.normalColor
                    || colors.highlightedColor != colors.normalColor;
                if (hasColorDifference && toggle.transition == Selectable.Transition.ColorTint) continue;

                names.Add(GetHierarchyPath(toggle.gameObject));
                if (names.Count >= 8) break;
            }
            return names;
        }

        private static bool TryGetPngSize(byte[] bytes, out int width, out int height)
        {
            width = height = 0;
            if (bytes == null || bytes.Length < 24) return false;
            if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return false;
            // IHDR width/height are big-endian 32-bit ints at offsets 16 and 20.
            width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            return width > 0 && height > 0;
        }

        /// <summary>
        /// The capture is a full-screen render at the project design resolution, while a mockup is
        /// usually a crop of one popup. Without this note the model judges proportions against the
        /// wrong frame and reports bogus spacing/size mismatches.
        /// </summary>
        private static string BuildFramingNote(byte[] shotBytes, byte[] refBytes)
        {
            if (!TryGetPngSize(shotBytes, out var sw, out var sh)) return null;
            if (!TryGetPngSize(refBytes, out var rw, out var rh)) return null;

            var shotAspect = sw / (float)sh;
            var refAspect = rw / (float)rh;
            var note = $"FRAMING: Image 1 is a full-screen capture ({sw}x{sh}, aspect {shotAspect:0.00}); " +
                       $"Image 2 is the mockup ({rw}x{rh}, aspect {refAspect:0.00}).";

            if (Mathf.Abs(shotAspect - refAspect) > 0.15f)
            {
                note += " The two frames differ, so do NOT judge by full-frame proportions: " +
                        "compare the popup content itself (element order, sprites, colors, relative sizes inside the popup), " +
                        "and ignore differences that only come from the surrounding screen area.";
            }
            return note;
        }

        private static List<string> StringList(JObject args, string key)
        {
            var list = new List<string>();
            if (args[key] is JArray arr)
            {
                foreach (var token in arr)
                {
                    var s = token?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                return list;
            }

            var raw = Str(args, key, null);
            if (string.IsNullOrWhiteSpace(raw)) return list;
            foreach (var part in raw.Split(','))
            {
                if (!string.IsNullOrWhiteSpace(part)) list.Add(part.Trim());
            }
            return list;
        }

        private static Texture2D LoadTextureFromFile(string fullPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                    return null;
                }
                return tex;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Project Config

        private string DoGetProjectConfig()
        {
            var config = Config.ProjectConfig.Current;
            return new JObject
            {
                ["success"] = true,
                ["config"] = JToken.Parse(config.BuildConfigSummaryJson())
            }.ToString();
        }

        #endregion

        #region Progress

        private string DoUpdateProgress(JObject args)
        {
            var completed = Str(args, "completed", "");
            var remaining = Str(args, "remaining", "");
            var issues = Str(args, "issues", "");

            var result = new JObject { ["success"] = true };
            if (!string.IsNullOrEmpty(completed)) result["completed"] = completed;
            if (!string.IsNullOrEmpty(remaining)) result["remaining"] = remaining;
            if (!string.IsNullOrEmpty(issues)) result["issues"] = issues;
            result["message"] = "Progress recorded.";
            return result.ToString();
        }

        #endregion

        #region Execute Code

        private string DoExecuteCode(JObject args)
        {
            var code = Str(args, "code");
            if (string.IsNullOrWhiteSpace(code)) return Error("No code provided.");

            var destructive = DescribeDestructiveCode(code);
            if (destructive != null) return Error(destructive);

            CompileResult compileResult;
            try { compileResult = _compiler.CompileInternal(code); }
            catch (Exception e) { return Error($"Compile exception: {e.Message}"); }

            if (!compileResult.Success)
                return Error("Compile error: " + string.Join("; ", compileResult.Errors));

            if (compileResult.Assembly == null)
                return Error("Compilation produced no assembly.");

            Type actionType;
            try { actionType = DynamicCompiler.FindActionType(compileResult.Assembly); }
            catch (ReflectionTypeLoadException e)
            {
                var loaderErrors = string.Join("; ", Array.ConvertAll(
                    e.LoaderExceptions ?? Array.Empty<Exception>(), ex => ex?.Message ?? "null"));
                return Error($"Type loading failed: {loaderErrors}");
            }

            if (actionType == null)
                return Error("No IAgentAction implementation found in code.");

            IAgentAction action;
            try { action = (IAgentAction)Activator.CreateInstance(actionType); }
            catch (Exception e) { return Error($"Failed to instantiate action: {e.Message}"); }

            var ctx = new ActionContext
            {
                TargetObject = Selection.activeGameObject,
                CanvasRoot = UnityEngine.Object.FindObjectOfType<Canvas>()?.gameObject
            };

            ActionResult actionResult;
            try { actionResult = action.Execute(ctx); }
            catch (Exception e) { return Error($"Runtime error: {e.GetType().Name}: {e.Message}"); }

            if (actionResult == null) return Error("execute_code returned null.");
            return actionResult.Success ? Ok(actionResult.Message ?? "Done.") : Error(actionResult.Message ?? "Unknown error.");
        }

        /// <summary>
        /// destroy_object refuses to delete prefab instances and populated Canvases; without this
        /// check the same teardown just moves into execute_code and the guard means nothing.
        /// Returns a refusal message, or null when the code is safe to run.
        /// </summary>
        private static string DescribeDestructiveCode(string code)
        {
            var destroyCalls = new[] { "DestroyImmediate", "Destroy(", "DestroyObject" };
            foreach (var call in destroyCalls)
            {
                if (code.IndexOf(call, StringComparison.Ordinal) < 0) continue;
                return $"execute_code must not delete GameObjects (found '{call}'). " +
                    "Deleting and re-creating UI loses authored prefab art and repeatedly regresses the build. " +
                    "Adjust the existing objects instead (set_rect_transform_batch / set_image_batch / set_text_properties_batch), " +
                    "or use `destroy_object` if a specific object really must go — it enforces the prefab-instance guard.";
            }

            if (code.IndexOf("RemoveComponent", StringComparison.Ordinal) >= 0)
            {
                return "execute_code must not strip components off prefab instances (found 'RemoveComponent'). " +
                    "Disable the behaviour instead, or fix the layout with the dedicated UI tools.";
            }

            return null;
        }

        #endregion

        #region Asset Indexing

        private string DoMatchSpriteByImage(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings and build the index first.");

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady)
            {
                indexer.EnsureInitialized();
                if (!indexer.IsReady)
                    return Error("Asset index not ready. The embedding model may not be loaded. Check the model path in settings.");
            }

            if (indexer.IndexedCount == 0)
                return Error("Asset index is empty. Run rebuild_asset_index first to build the visual index.");

            var imageBase64 = Str(args, "imageBase64");
            if (string.IsNullOrEmpty(imageBase64))
                return Error("Missing 'imageBase64' parameter.");

            byte[] imageBytes;
            try { imageBytes = Convert.FromBase64String(imageBase64); }
            catch { return Error("Invalid base64 image data."); }

            var topK = Int(args, "topK", 5);
            var minConfidence = Float(args, "minConfidence", (float)ImageMatchFilterDefault);

            var results = indexer.QueryByImage(imageBytes, topK);
            if (results == null || results.Count == 0)
                return new JObject { ["success"] = true, ["count"] = 0, ["matches"] = new JArray(),
                    ["message"] = "No matching sprites found." }.ToString();

            var filtered = results.Where(r => r.score >= minConfidence).ToList();
            var matchArr = BuildMatchArray(filtered);

            var imageMatchResult = new JObject
            {
                ["success"] = true,
                ["count"] = filtered.Count,
                ["matches"] = matchArr
            };
            AddLowConfidenceHintIfNeeded(imageMatchResult, matchArr, "score", ImageMatchConfidentScore, "image");
            return imageMatchResult.ToString();
        }

        /// <summary>
        /// Resolves and loads the design mockup image attached to the current task
        /// (see DesignImageContext), so region-based tools don't need the LLM to
        /// supply raw image bytes.
        /// </summary>
        private static Texture2D LoadDesignImageTexture(out string error)
        {
            error = null;
            var assetPath = DesignImageContext.CurrentAssetPath;
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "No design mockup image is attached to the current task. This tool only works when the user attached a design image at the start of the conversation.";
                return null;
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var fullPath = Path.Combine(projectRoot, assetPath);
            if (!File.Exists(fullPath))
            {
                error = $"Design mockup image not found on disk at: {assetPath}";
                return null;
            }

            var tex = LoadTextureFromFile(fullPath);
            if (tex == null) error = "Failed to decode the design mockup image.";
            return tex;
        }

        /// <summary>Crops a normalized (0-1, top-left origin) rectangle out of a readable texture and returns PNG bytes.</summary>
        private static byte[] CropTextureNormalized(Texture2D tex, float x, float y, float width, float height, out int outW, out int outH)
        {
            outW = 0; outH = 0;
            int texW = tex.width, texH = tex.height;

            int px = Mathf.Clamp(Mathf.RoundToInt(x * texW), 0, texW - 1);
            int pTop = Mathf.Clamp(Mathf.RoundToInt(y * texH), 0, texH - 1);
            int pw = Mathf.Clamp(Mathf.RoundToInt(width * texW), 1, texW - px);
            int ph = Mathf.Clamp(Mathf.RoundToInt(height * texH), 1, texH - pTop);

            // Unity texture pixel rows are bottom-up; our x/y are top-down (image reading order).
            int pBottom = Mathf.Clamp(texH - pTop - ph, 0, texH - ph);

            Color[] pixels;
            try { pixels = tex.GetPixels(px, pBottom, pw, ph); }
            catch { return null; }

            var cropped = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            try
            {
                cropped.SetPixels(pixels);
                cropped.Apply();
                outW = pw; outH = ph;
                return cropped.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cropped);
            }
        }

        // Below these bars a "match" is still returned (it passed minConfidence) but is unlikely
        // to actually look like the target element. Silently applying it produces exactly the
        // "wrong sprite used with confidence" failure mode seen in production logs. Instead of
        // hiding this uncertainty, we surface it to the model so it can choose a plain color
        // fallback (and say so) rather than pretend a shaky guess is a solid match.
        // The filter default must stay BELOW the confident bar: when both were 0.65 every surviving
        // match scored >= the bar, so `lowConfidence` could never fire and shaky matches were applied
        // as if they were solid.
        private const double ImageMatchConfidentScore = 0.72;
        private const double ImageMatchFilterDefault = 0.55;
        private const double TextMatchConfidentScore = 30.0;

        /// <summary>
        /// If the best (first) match's score/confidence is below the "confident" bar, tags it with
        /// lowConfidence=true and adds a top-level `_hint` telling the model not to silently trust it.
        /// </summary>
        private static void AddLowConfidenceHintIfNeeded(JObject resultObj, JArray matches, string scoreField, double confidentBar, string kind)
        {
            if (resultObj == null || matches == null || matches.Count == 0) return;
            if (!(matches[0] is JObject top)) return;
            var topScore = top[scoreField]?.Value<double>() ?? 0;
            if (topScore >= confidentBar) return;

            top["lowConfidence"] = true;
            resultObj["lowConfidence"] = true;
            resultObj["_hint"] = $"Best {kind} match confidence is low ({topScore:0.###} vs a healthy bar of {confidentBar:0.###}). " +
                "It likely does NOT visually match the target element. Do NOT silently apply it as if it were a solid match — " +
                "prefer a plain color-tinted Image as an honest fallback, and mention in your final summary that no confident asset was found for this element.";
        }

        private static JArray BuildMatchArray(List<MatchResult> matches)
        {
            var arr = new JArray();
            foreach (var m in matches)
            {
                arr.Add(new JObject
                {
                    ["assetPath"] = m.assetPath,
                    ["confidence"] = Math.Round(m.confidence, 1),
                    ["score"] = Math.Round(m.score, 4)
                });
            }
            return arr;
        }

        private string DoMapDesignRect(JObject args)
        {
            var mapped = MapDesignRectInternal(
                Float(args, "x", 0f),
                Float(args, "y", 0f),
                Float(args, "width", 0f),
                Float(args, "height", 0f),
                Str(args, "label", null),
                out var error);
            if (mapped == null) return Error(error);
            DesignImageContext.RegisterMappedRegions(1);
            return mapped.ToString();
        }

        private string DoMapDesignRectBatch(JObject args)
        {
            var regionsToken = args["regions"];
            JArray regions;
            if (regionsToken is JArray arr)
                regions = arr;
            else if (regionsToken != null && regionsToken.Type == JTokenType.String)
            {
                try { regions = JArray.Parse(regionsToken.ToString()); }
                catch { return Error("Invalid JSON in 'regions' parameter."); }
            }
            else
                return Error("Missing 'regions' array.");

            var results = new JArray();
            int success = 0, fail = 0;
            foreach (var item in regions)
            {
                if (!(item is JObject obj))
                {
                    results.Add(new JObject { ["success"] = false, ["error"] = "Region must be an object." });
                    fail++;
                    continue;
                }
                var mapped = MapDesignRectInternal(
                    obj["x"] != null ? (float)obj["x"] : 0f,
                    obj["y"] != null ? (float)obj["y"] : 0f,
                    obj["width"] != null ? (float)obj["width"] : 0f,
                    obj["height"] != null ? (float)obj["height"] : 0f,
                    obj["label"]?.ToString(),
                    out var error);
                if (mapped == null)
                {
                    results.Add(new JObject { ["success"] = false, ["error"] = error, ["label"] = obj["label"] });
                    fail++;
                }
                else
                {
                    results.Add(mapped);
                    success++;
                }
            }

            DesignImageContext.RegisterMappedRegions(success);

            return new JObject
            {
                ["success"] = fail == 0,
                ["total"] = regions.Count,
                ["mapped"] = success,
                ["failed"] = fail,
                ["results"] = results,
                ["message"] = $"Mapped {success}/{regions.Count} regions. Use anchoredPosition/sizeDelta with anchorMin=anchorMax=(0.5,0.5)."
            }.ToString();
        }

        private static JObject MapDesignRectInternal(float nx, float ny, float nw, float nh, string label, out string error)
        {
            error = null;
            if (nw <= 0f || nh <= 0f)
            {
                error = "width and height must be > 0 (normalized 0-1).";
                return null;
            }

            nx = Mathf.Clamp01(nx);
            ny = Mathf.Clamp01(ny);
            nw = Mathf.Clamp(nw, 0.001f, 1f - nx);
            nh = Mathf.Clamp(nh, 0.001f, 1f - ny);

            var cfg = ProjectConfig.Current;
            float canvasW = cfg?.basicInfo?.designWidth > 0 ? cfg.basicInfo.designWidth : 1920;
            float canvasH = cfg?.basicInfo?.designHeight > 0 ? cfg.basicInfo.designHeight : 1080;

            float imgW = DesignImageContext.HasSize ? DesignImageContext.Width : canvasW;
            float imgH = DesignImageContext.HasSize ? DesignImageContext.Height : canvasH;

            // Fit design image into canvas reference resolution (letterbox if aspect differs)
            float scale = Mathf.Min(canvasW / imgW, canvasH / imgH);
            float fittedW = imgW * scale;
            float fittedH = imgH * scale;
            float offsetX = (canvasW - fittedW) * 0.5f;
            float offsetY = (canvasH - fittedH) * 0.5f;

            float px = nx * imgW;
            float py = ny * imgH;
            float pw = nw * imgW;
            float ph = nh * imgH;

            float centerX = px + pw * 0.5f;
            float centerY = py + ph * 0.5f;

            // Top-left image space -> canvas pixel space (top-left of canvas)
            float canvasX = offsetX + centerX * scale;
            float canvasY = offsetY + centerY * scale;

            // Center-origin Unity UI (Y up)
            float anchoredX = canvasX - canvasW * 0.5f;
            float anchoredY = canvasH * 0.5f - canvasY;
            float sizeW = pw * scale;
            float sizeH = ph * scale;

            var result = new JObject
            {
                ["success"] = true,
                ["anchoredPosition"] = new JObject { ["x"] = Math.Round(anchoredX, 2), ["y"] = Math.Round(anchoredY, 2) },
                ["sizeDelta"] = new JObject { ["width"] = Math.Round(sizeW, 2), ["height"] = Math.Round(sizeH, 2) },
                ["anchorMin"] = new JObject { ["x"] = 0.5, ["y"] = 0.5 },
                ["anchorMax"] = new JObject { ["x"] = 0.5, ["y"] = 0.5 },
                ["pivot"] = new JObject { ["x"] = 0.5, ["y"] = 0.5 },
                ["normalized"] = new JObject { ["x"] = nx, ["y"] = ny, ["width"] = nw, ["height"] = nh },
                ["scale"] = Math.Round(scale, 6),
                ["imagePixelSize"] = new JObject { ["width"] = imgW, ["height"] = imgH },
                ["canvasDesignResolution"] = new JObject { ["width"] = canvasW, ["height"] = canvasH },
                ["hint"] = "Pass these values to set_rect_transform / set_rect_transform_batch (x/y = anchoredPosition, width/height = sizeDelta, set anchors to MiddleCenter)."
            };
            if (!string.IsNullOrEmpty(label))
                result["label"] = label;
            if (!DesignImageContext.HasSize)
                result["warning"] = "Design image pixel size unknown — assumed equal to canvas design resolution.";
            return result;
        }

        private string DoCropDesignImage(JObject args)
        {
            var x = Mathf.Clamp01(Float(args, "x", 0f));
            var y = Mathf.Clamp01(Float(args, "y", 0f));
            var width = Mathf.Clamp(Float(args, "width", 1f), 0.01f, 1f - x);
            var height = Mathf.Clamp(Float(args, "height", 1f), 0.01f, 1f - y);
            var label = Str(args, "label", "crop");

            var tex = LoadDesignImageTexture(out var error);
            if (tex == null) return Error(error);

            byte[] pngBytes;
            int outW, outH;
            try { pngBytes = CropTextureNormalized(tex, x, y, width, height, out outW, out outH); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }

            if (pngBytes == null || pngBytes.Length == 0)
                return Error("Crop produced an empty image. Check that x/y/width/height are within 0-1.");

            try
            {
                var cropDir = Path.Combine(Application.dataPath, "Screenshots", "Crops");
                if (!Directory.Exists(cropDir)) Directory.CreateDirectory(cropDir);
                var safeLabel = new string(label.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
                if (string.IsNullOrEmpty(safeLabel)) safeLabel = "crop";
                File.WriteAllBytes(Path.Combine(cropDir, $"{safeLabel}_{DateTime.Now:HHmmssfff}.png"), pngBytes);
                AssetDatabase.Refresh();
            }
            catch { /* preview still works even if saving to disk fails */ }

            var result = new JObject
            {
                ["success"] = true,
                ["width"] = outW,
                ["height"] = outH,
                ["message"] = $"Cropped region (x={x:F2}, y={y:F2}, w={width:F2}, h={height:F2}) from the design mockup — {outW}x{outH}px."
            };

            if (BuilderSettings.Get().SupportsVision)
            {
                result["_multimodal"] = true;
                result["_images"] = new JArray { $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}" };
            }

            return result.ToString();
        }

        private string DoMatchSpriteByRegion(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings and build the index first.");

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady)
            {
                indexer.EnsureInitialized();
                if (!indexer.IsReady)
                    return Error("Asset index not ready. The embedding model may not be loaded. Check the model path in settings.");
            }
            if (indexer.IndexedCount == 0)
                return Error("Asset index is empty. Run rebuild_asset_index first to build the visual index.");

            var x = Mathf.Clamp01(Float(args, "x", 0f));
            var y = Mathf.Clamp01(Float(args, "y", 0f));
            var width = Mathf.Clamp(Float(args, "width", 1f), 0.01f, 1f - x);
            var height = Mathf.Clamp(Float(args, "height", 1f), 0.01f, 1f - y);
            var topK = Int(args, "topK", 5);
            var minConfidence = Float(args, "minConfidence", (float)ImageMatchFilterDefault);

            var tex = LoadDesignImageTexture(out var error);
            if (tex == null) return Error(error);

            byte[] cropBytes;
            try { cropBytes = CropTextureNormalized(tex, x, y, width, height, out _, out _); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }

            if (cropBytes == null)
                return Error("Failed to crop the design mockup region. Check that x/y/width/height are within 0-1.");

            var results = indexer.QueryByImage(cropBytes, topK);
            if (results == null || results.Count == 0)
                return new JObject { ["success"] = true, ["count"] = 0, ["matches"] = new JArray(),
                    ["message"] = "No matching sprites found for this region." }.ToString();

            var filtered = results.Where(r => r.score >= minConfidence).ToList();
            var regionMatchArr = BuildMatchArray(filtered);
            var regionMatchResult = new JObject
            {
                ["success"] = true,
                ["region"] = new JObject { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height },
                ["count"] = filtered.Count,
                ["matches"] = regionMatchArr
            };
            AddLowConfidenceHintIfNeeded(regionMatchResult, regionMatchArr, "score", ImageMatchConfidentScore, "image");
            return regionMatchResult.ToString();
        }

        private string DoMatchSpriteByRegionBatch(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings and build the index first.");

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady)
            {
                indexer.EnsureInitialized();
                if (!indexer.IsReady)
                    return Error("Asset index not ready. The embedding model may not be loaded.");
            }
            if (indexer.IndexedCount == 0)
                return Error("Asset index is empty. Run rebuild_asset_index first.");

            var regionsToken = args["regions"] as JArray;
            if (regionsToken == null || regionsToken.Count == 0)
                return Error("Missing or empty 'regions' array.");

            var globalMinConfidence = Float(args, "minConfidence", (float)ImageMatchFilterDefault);

            var tex = LoadDesignImageTexture(out var error);
            if (tex == null) return Error(error);

            var resultsArr = new JArray();
            try
            {
                foreach (var item in regionsToken)
                {
                    var regionObj = item as JObject;
                    if (regionObj == null || regionObj["x"] == null || regionObj["y"] == null
                        || regionObj["width"] == null || regionObj["height"] == null)
                    {
                        resultsArr.Add(new JObject { ["error"] = "Region missing required x/y/width/height fields." });
                        continue;
                    }

                    var label = regionObj["label"]?.ToString() ?? "";
                    var x = Mathf.Clamp01((float)regionObj["x"]);
                    var y = Mathf.Clamp01((float)regionObj["y"]);
                    var width = Mathf.Clamp((float)regionObj["width"], 0.01f, 1f - x);
                    var height = Mathf.Clamp((float)regionObj["height"], 0.01f, 1f - y);
                    var topK = regionObj["topK"] != null ? (int)regionObj["topK"] : 5;

                    var cropBytes = CropTextureNormalized(tex, x, y, width, height, out _, out _);
                    var results = cropBytes != null ? indexer.QueryByImage(cropBytes, topK) : null;

                    if (results == null || results.Count == 0)
                    {
                        resultsArr.Add(new JObject { ["label"] = label, ["count"] = 0, ["matches"] = new JArray() });
                        continue;
                    }

                    var filtered = results.Where(r => r.score >= globalMinConfidence).ToList();
                    var regionMatchArr = BuildMatchArray(filtered);
                    var regionResultObj = new JObject
                    {
                        ["label"] = label,
                        ["region"] = new JObject { ["x"] = x, ["y"] = y, ["width"] = width, ["height"] = height },
                        ["count"] = filtered.Count,
                        ["matches"] = regionMatchArr
                    };
                    AddLowConfidenceHintIfNeeded(regionResultObj, regionMatchArr, "score", ImageMatchConfidentScore, "image");
                    resultsArr.Add(regionResultObj);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }

            return new JObject
            {
                ["success"] = true,
                ["totalRegions"] = regionsToken.Count,
                ["results"] = resultsArr
            }.ToString();
        }

        private string DoSearchSpritesByText(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings first.");

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady)
            {
                indexer.EnsureInitialized();
                if (!indexer.IsReady)
                    return Error("Asset index not ready. The embedding model may not be loaded.");
            }

            if (indexer.IndexedCount == 0)
                return Error("Asset index is empty. Run rebuild_asset_index first.");

            var query = Str(args, "query");
            if (string.IsNullOrEmpty(query))
                return Error("Missing 'query' parameter.");

            var topK = Int(args, "topK", 10);
            var minConfidence = Float(args, "minConfidence", 15f);

            var results = indexer.QueryByText(query, topK);
            if (results == null || results.Count == 0)
            {
                if (!indexer.IsTextSearchReady)
                    return Error("Text search not available. CLIP text model or tokenizer files not found. Download them via 'Window > UI Prefab Builder > Download CLIP Text Model'.");
                return new JObject { ["success"] = true, ["count"] = 0, ["matches"] = new JArray(),
                    ["message"] = "No matching sprites found for query: " + query }.ToString();
            }

            var filtered = results.Where(r => r.confidence >= minConfidence).ToList();
            var matchArr = BuildMatchArray(filtered);

            var textMatchResult = new JObject
            {
                ["success"] = true,
                ["query"] = query,
                ["count"] = filtered.Count,
                ["matches"] = matchArr
            };
            AddLowConfidenceHintIfNeeded(textMatchResult, matchArr, "confidence", TextMatchConfidentScore, "text");
            return textMatchResult.ToString();
        }

        private string DoSearchSpritesByTextBatch(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings first.");

            var indexer = AssetIndexer.Instance;
            if (!indexer.IsReady)
            {
                indexer.EnsureInitialized();
                if (!indexer.IsReady)
                    return Error("Asset index not ready. The embedding model may not be loaded.");
            }

            if (indexer.IndexedCount == 0)
                return Error("Asset index is empty. Run rebuild_asset_index first.");

            var queriesToken = args["queries"] as JArray;
            if (queriesToken == null || queriesToken.Count == 0)
                return Error("Missing or empty 'queries' array.");

            var globalMinConfidence = Float(args, "minConfidence", 15f);
            var resultsArr = new JArray();

            foreach (var item in queriesToken)
            {
                var queryObj = item as JObject;
                var query = queryObj?["query"]?.ToString();
                if (string.IsNullOrEmpty(query))
                {
                    resultsArr.Add(new JObject { ["query"] = "", ["error"] = "Missing 'query' field" });
                    continue;
                }

                var topK = queryObj["topK"] != null ? (int)queryObj["topK"] : 5;
                var results = indexer.QueryByText(query, topK);

                if (results == null || results.Count == 0)
                {
                    resultsArr.Add(new JObject
                    {
                        ["query"] = query, ["count"] = 0, ["matches"] = new JArray()
                    });
                    continue;
                }

                var filtered = results.Where(r => r.confidence >= globalMinConfidence).ToList();
                var matchArr = BuildMatchArray(filtered);

                var queryResultObj = new JObject
                {
                    ["query"] = query, ["count"] = filtered.Count, ["matches"] = matchArr
                };
                AddLowConfidenceHintIfNeeded(queryResultObj, matchArr, "confidence", TextMatchConfidentScore, "text");
                resultsArr.Add(queryResultObj);
            }

            return new JObject
            {
                ["success"] = true,
                ["totalQueries"] = queriesToken.Count,
                ["results"] = resultsArr
            }.ToString();
        }

        private string DoRebuildAssetIndex(JObject args)
        {
            var settings = BuilderSettings.Get();
            if (!settings.EnableAssetIndexing)
                return Error("Asset indexing is not enabled. Enable it in Agent Settings first.");

            var indexer = AssetIndexer.Instance;
            indexer.EnsureInitialized();

            if (!indexer.IsReady)
                return Error("Failed to initialize indexer. Check that the embedding model exists at: " + settings.EmbeddingModelPath);

            if (indexer.IsIndexing)
                return Ok("Asset indexing is already in progress.");

            indexer.RebuildFullIndex();
            return Ok("Asset index rebuild started in background. Use get_index_status to check progress.");
        }

        private string DoGetIndexStatus(JObject args)
        {
            var settings = BuilderSettings.Get();
            var indexer = AssetIndexer.Instance;
            return new JObject
            {
                ["success"] = true,
                ["enabled"] = settings.EnableAssetIndexing,
                ["ready"] = indexer.IsReady,
                ["indexing"] = indexer.IsIndexing,
                ["entryCount"] = indexer.IndexedCount,
                ["lastBuildTimestamp"] = indexer.LastBuildTimestamp,
                ["modelPath"] = settings.EmbeddingModelPath,
                ["textSearchReady"] = indexer.IsTextSearchReady,
                ["textModelPath"] = settings.TextModelPath
            }.ToString();
        }

        #endregion

        #region Helpers

        private static GameObject FindGO(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (name.StartsWith("#") && int.TryParse(name.Substring(1), out var instanceId))
            {
                var obj = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
                if (obj != null) return obj;
            }

            if (name.Contains("/"))
            {
                var parts = name.Split('/');
                var root = UnityEngine.Object.FindObjectsOfType<GameObject>().FirstOrDefault(g => g.name == parts[0]);
                if (root == null) return null;
                var current = root.transform;
                for (int i = 1; i < parts.Length; i++)
                {
                    current = current.Find(parts[i]);
                    if (current == null) return null;
                }
                return current.gameObject;
            }

            return UnityEngine.Object.FindObjectsOfType<GameObject>().FirstOrDefault(g => g.name == name);
        }

        private static bool HasTMP(GameObject go)
        {
            foreach (var c in go.GetComponents<Component>())
            {
                if (c != null && c.GetType().GetProperty("text")?.PropertyType == typeof(string)
                    && c.GetType().Name.Contains("TextMeshPro"))
                    return true;
            }
            return false;
        }

        private static string GetHierarchyPath(GameObject go)
        {
            if (go == null) return "";
            var path = go.name;
            var t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }

        private static Color ReadColor(JObject args, float defR, float defG, float defB, float defA)
            => new Color(Float(args, "r", defR), Float(args, "g", defG), Float(args, "b", defB), Float(args, "a", defA));

        private static RectOffset BuildPadding(JObject args)
        {
            int l = Int(args, "padLeft", 0), r = Int(args, "padRight", 0), t = Int(args, "padTop", 0), b = Int(args, "padBottom", 0);
            return (l == 0 && r == 0 && t == 0 && b == 0) ? null : new RectOffset(l, r, t, b);
        }

        private static TextAnchor ParseTextAnchor(string value)
            => Enum.TryParse<TextAnchor>(value, true, out var anchor) ? anchor : TextAnchor.UpperCenter;

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
            => Enum.TryParse(value, true, out result);

        private static string Str(JObject args, string key, string def = "") => args[key]?.ToString() ?? def;
        private static float Float(JObject args, string key, float def = 0) => args[key] != null ? (float)args[key] : def;
        private static float? NullFloat(JObject args, string key) => args[key] != null ? (float?)args[key] : null;
        private static bool? NullBool(JObject args, string key) => args[key] != null ? (bool?)args[key] : null;
        private static int Int(JObject args, string key, int def = 0) => args[key] != null ? (int)args[key] : def;
        private static bool Bool(JObject args, string key, bool def = false) => args[key] != null ? (bool)args[key] : def;
        private static string Ok(string msg) => new JObject { ["success"] = true, ["message"] = msg }.ToString();
        private static string Error(string msg) => new JObject { ["success"] = false, ["error"] = msg }.ToString();
        private static string Created(GameObject go) => go == null ? Error("Failed to create.") :
            new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) }.ToString();
        private static string TargetNotFound(string name) => string.IsNullOrWhiteSpace(name)
            ? Error("No 'target' was given (the parameter is missing or empty), so nothing was looked up. " +
                "Send it as a plain key/value pair — {\"target\": \"Canvas/Panel/Title\"} — and do not escape the keys.")
            : Error($"GameObject '{name}' not found.");
        private static JObject ItemFail(string target, string error) => new JObject { ["target"] = target, ["success"] = false, ["error"] = error };

        #endregion
    }
}

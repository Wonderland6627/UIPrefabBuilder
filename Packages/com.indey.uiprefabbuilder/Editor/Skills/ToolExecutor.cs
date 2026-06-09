using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Indey.UIPrefabBuilder.Compiler;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Core;
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

        public string Execute(string toolName, string argumentsJson)
        {
            try
            {
                var args = string.IsNullOrWhiteSpace(argumentsJson)
                    ? new JObject()
                    : JObject.Parse(argumentsJson);
                return ExecuteInternal(toolName, args);
            }
            catch (Exception e)
            {
                ConsoleLogger.Error($"Tool '{toolName}' error: {e.Message}");
                return Error($"Tool execution failed: {e.Message}");
            }
        }

        private string ExecuteInternal(string toolName, JObject args)
        {
            switch (toolName)
            {
                // Asset
                case "search_assets": return DoSearchAssets(args);
                case "search_assets_glob": return DoSearchAssetsGlob(args);
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
                case "set_image_sprite": return DoSetImageSprite(args);
                case "set_image_color": return DoSetImageColor(args);
                // Text
                case "set_text_properties": return DoSetTextProperties(args);
                case "set_text": return DoSetText(args);
                // Layout
                case "add_canvas_group": return DoAddCanvasGroup(args);
                case "add_vertical_layout": return DoAddVerticalLayout(args);
                case "add_horizontal_layout": return DoAddHorizontalLayout(args);
                case "add_grid_layout": return DoAddGridLayout(args);
                case "add_layout_element": return DoAddLayoutElement(args);
                // Effects
                case "add_mask": return DoAddMask(args);
                case "add_outline": return DoAddOutline(args);
                case "configure_selectable": return DoConfigureSelectable(args);
                // Generic component
                case "add_component": return DoAddComponent(args);
                case "set_component_property": return DoSetComponentProperty(args);
                // Sibling order
                case "set_sibling_index": return DoSetSiblingIndex(args);
                // GameObject management
                case "destroy_object": return DoDestroyObject(args);
                case "set_parent": return DoSetParent(args);
                case "rename_object": return DoRenameObject(args);
                // Query
                case "inspect_hierarchy": return DoInspectHierarchy(args);
                case "inspect_components": return DoInspectComponents(args);
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
                // Progress
                case "update_progress": return DoUpdateProgress(args);
                // Code
                case "execute_code": return DoExecuteCode(args);
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
            return Created(UICreator.CreateButton(Str(args, "name"), parent.transform, Str(args, "text", "Button"),
                new Vector2(Float(args, "width", 160), Float(args, "height", 40))));
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
            return Created(UICreator.CreateImage(Str(args, "name"), parent.transform, Str(args, "spritePath", ""),
                new Vector2(Float(args, "width", 100), Float(args, "height", 100))));
        }

        private string DoCreateInputField(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreateInputField(Str(args, "name"), parent.transform, Str(args, "placeholder", "Enter text..."),
                new Vector2(Float(args, "width", 200), Float(args, "height", 30))));
        }

        private string DoCreateSlider(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreateSlider(Str(args, "name"), parent.transform, Float(args, "min", 0), Float(args, "max", 1), Float(args, "value", 0.5f)));
        }

        private string DoCreateToggle(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            return Created(UICreator.CreateToggle(Str(args, "name"), parent.transform, Str(args, "label", "Toggle"), Bool(args, "isOn", true)));
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

                    GameObject go = null;
                    switch (type)
                    {
                        case "canvas": go = UICreator.CreateCanvas(name); break;
                        case "panel":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreatePanel(name, parentT, color); break;
                        case "button":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateButton(name, parentT, text ?? "Button", new Vector2(w, h)); break;
                        case "text":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateText(name, parentT, text ?? "", fontSize, color); break;
                        case "image":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateImage(name, parentT, spritePath, new Vector2(w, h)); break;
                        case "inputfield":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateInputField(name, parentT, text ?? "Enter text...", new Vector2(w, h)); break;
                        case "slider":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateSlider(name, parentT, 0, 1, 0.5f); break;
                        case "toggle":
                            if (parentT == null) { results.Add(ItemFail(name, $"Parent '{parentName}' not found")); fail++; continue; }
                            go = UICreator.CreateToggle(name, parentT, text ?? "Toggle", true); break;
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
                    results.Add(new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) });
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
            ComponentHelper.SetImageProperties(go, Str(args, "spritePath", null), Str(args, "type", null),
                Str(args, "fillMethod", null), NullFloat(args, "fillAmount"), NullBool(args, "preserveAspect"), color);
            return Ok($"Image properties updated on '{go.name}'.");
        }

        private string DoSetImageSprite(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.SetImageSprite(go, Str(args, "spritePath"));
            return Ok($"Sprite set on '{go.name}'.");
        }

        private string DoSetImageColor(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.SetImageColor(go, new Color(Float(args, "r"), Float(args, "g"), Float(args, "b"), Float(args, "a", 1)));
            return Ok($"Image color set on '{go.name}'.");
        }

        private string DoSetTextProperties(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            Color? color = null;
            if (args["r"] != null || args["g"] != null || args["b"] != null)
                color = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));
            int? fontSize = args["fontSize"] != null ? (int?)Int(args, "fontSize") : null;
            ComponentHelper.SetTextProperties(go, args["text"]?.ToString(), fontSize,
                args["alignment"]?.ToString(), args["fontStyle"]?.ToString(), color, args["overflow"]?.ToString());
            return Ok($"Text properties updated on '{go.name}'.");
        }

        private string DoSetText(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.SetText(go, Str(args, "text"));
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
                Bool(args, "childForceExpandWidth", true), Bool(args, "childForceExpandHeight", true));
            return Ok($"VerticalLayoutGroup added to '{go.name}'.");
        }

        private string DoAddHorizontalLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddHorizontalLayout(go, Float(args, "spacing", 8), ParseTextAnchor(Str(args, "childAlignment", "MiddleCenter")), BuildPadding(args),
                Bool(args, "childControlWidth", true), Bool(args, "childControlHeight", true),
                Bool(args, "childForceExpandWidth", true), Bool(args, "childForceExpandHeight", true));
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

        private string DoConfigureSelectable(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.ConfigureSelectable(go, Str(args, "transition", "ColorTint"),
                Str(args, "navigationMode", "Automatic"), Bool(args, "interactable", true));
            return Ok($"Selectable configured on '{go.name}'.");
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
            var name = go.name;
            Undo.DestroyObjectImmediate(go);
            return Ok($"Destroyed '{name}'.");
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

        private string DoInspectComponents(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            return new JObject { ["success"] = true, ["components"] = HierarchyInspector.DescribeComponents(go) }.ToString();
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
            var result = PrefabHelper.Create(go, Str(args, "path"));
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
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var ok = PrefabHelper.Apply(go);
            return ok ? Ok($"Prefab overrides applied for '{go.name}'.") : Error("Not a prefab instance or apply failed.");
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
                            var bytes = tex.EncodeToPNG();
                            UnityEngine.Object.DestroyImmediate(tex);
                            return bytes;
                        }
                    }
                    type = type.BaseType;
                }
            }
            catch { }
            return null;
        }

        private static byte[] FallbackCameraRender(int width, int height)
        {
            var camera = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (camera == null) return null;

            var rt = new RenderTexture(width, height, 24);
            var prevTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            camera.targetTexture = prevTarget;
            RenderTexture.active = null;

            var bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            return bytes;
        }

        private string DoTakeScreenshot(JObject args)
        {
            var filename = Str(args, "filename", "screenshot");
            var width = Int(args, "width", 540);
            var height = Int(args, "height", 960);

            try
            {
                int actualW = width, actualH = height;

                var bytes = TryCaptureGameViewRT(out actualW, out actualH);

                if (bytes == null)
                {
                    bytes = FallbackCameraRender(width, height);
                    actualW = width;
                    actualH = height;
                }

                if (bytes == null)
                    return Error("Screenshot failed: Game View not available and no camera found.");

                var screenshotDir = Path.Combine(Application.dataPath, "Screenshots");
                if (!Directory.Exists(screenshotDir)) Directory.CreateDirectory(screenshotDir);
                var filePath = Path.Combine(screenshotDir, filename + ".png");
                File.WriteAllBytes(filePath, bytes);
                AssetDatabase.Refresh();

                var result = new JObject
                {
                    ["success"] = true,
                    ["message"] = $"Screenshot saved to Assets/Screenshots/{filename}.png ({actualW}x{actualH})",
                    ["filePath"] = $"Assets/Screenshots/{filename}.png",
                    ["width"] = actualW,
                    ["height"] = actualH
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
            var prompt = Str(args, "prompt",
                "Analyze this UI screenshot for layout issues, alignment problems, text overlap, missing sprites, and styling improvements. List specific elements that need fixing.");
            var refPath = Str(args, "referenceImagePath", null);

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var fullPath = Path.Combine(projectRoot, path);
            if (!File.Exists(fullPath))
                return Error($"Screenshot not found: {path}");

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
            var base64 = Convert.ToBase64String(bytes);
            var imageUrl = $"data:image/png;base64,{base64}";

            var images = new JArray { imageUrl };
            var message = prompt;

            if (!string.IsNullOrEmpty(refPath))
            {
                var refFullPath = Path.Combine(projectRoot, refPath);
                if (File.Exists(refFullPath))
                {
                    var refBytes = File.ReadAllBytes(refFullPath);
                    images.Add($"data:image/png;base64,{Convert.ToBase64String(refBytes)}");
                    message += "\n\nA reference image is also provided for comparison. Identify differences between the current UI and the reference.";
                }
            }

            return new JObject
            {
                ["success"] = true,
                ["message"] = message,
                ["_multimodal"] = true,
                ["_images"] = images
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
        private static string TargetNotFound(string name) => Error($"GameObject '{name}' not found.");
        private static JObject ItemFail(string target, string error) => new JObject { ["target"] = target, ["success"] = false, ["error"] = error };

        #endregion
    }
}

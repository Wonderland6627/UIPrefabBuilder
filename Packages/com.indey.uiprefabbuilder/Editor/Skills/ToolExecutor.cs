using System;
using System.Linq;
using System.Reflection;
using System.Text;
using Indey.UIPrefabBuilder.Compiler;
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
                case "search_assets": return DoSearchAssets(args);
                case "get_asset_info": return DoGetAssetInfo(args);
                case "create_canvas": return DoCreateCanvas(args);
                case "create_panel": return DoCreatePanel(args);
                case "create_button": return DoCreateButton(args);
                case "create_text": return DoCreateText(args);
                case "create_image": return DoCreateImage(args);
                case "create_inputfield": return DoCreateInputField(args);
                case "create_slider": return DoCreateSlider(args);
                case "create_toggle": return DoCreateToggle(args);
                case "create_scrollview": return DoCreateScrollView(args);
                case "set_anchor": return DoSetAnchor(args);
                case "set_size": return DoSetSize(args);
                case "set_position": return DoSetPosition(args);
                case "set_image_sprite": return DoSetImageSprite(args);
                case "set_image_color": return DoSetImageColor(args);
                case "set_text": return DoSetText(args);
                case "add_canvas_group": return DoAddCanvasGroup(args);
                case "add_vertical_layout": return DoAddVerticalLayout(args);
                case "add_horizontal_layout": return DoAddHorizontalLayout(args);
                case "add_grid_layout": return DoAddGridLayout(args);
                case "add_layout_element": return DoAddLayoutElement(args);
                case "inspect_hierarchy": return DoInspectHierarchy(args);
                case "inspect_components": return DoInspectComponents(args);
                case "get_scene_overview": return DoGetSceneOverview();
                case "find_by_name": return DoFindByName(args);
                case "save_as_prefab": return DoSaveAsPrefab(args);
                case "execute_code": return DoExecuteCode(args);
                default: return Error($"Unknown tool: {toolName}");
            }
        }

        private string DoSearchAssets(JObject args)
        {
            var filter = Str(args, "filter");
            var limit = Int(args, "limit", 50);
            var results = AssetFinder.Find(filter, limit);
            var arr = new JArray(results.Select(p => (JToken)p));
            return new JObject { ["success"] = true, ["count"] = results.Count, ["assets"] = arr }.ToString();
        }

        private string DoGetAssetInfo(JObject args)
        {
            var info = AssetFinder.GetInfo(Str(args, "path"));
            return new JObject { ["success"] = true, ["info"] = info }.ToString();
        }

        private string DoCreateCanvas(JObject args)
        {
            var name = Str(args, "name", "Canvas");
            var modeStr = Str(args, "renderMode", "ScreenSpaceOverlay");
            if (!TryParseEnum(modeStr, out RenderMode mode))
                return Error($"Invalid renderMode: {modeStr}. Use ScreenSpaceOverlay, ScreenSpaceCamera, or WorldSpace.");
            var go = UICreator.CreateCanvas(name, mode);
            return Created(go);
        }

        private string DoCreatePanel(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var color = ReadColor(args, 1, 1, 1, 1);
            var go = UICreator.CreatePanel(Str(args, "name"), parent.transform, color);
            return Created(go);
        }

        private string DoCreateButton(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var text = Str(args, "text", "Button");
            var size = new Vector2(Float(args, "width", 160), Float(args, "height", 40));
            var go = UICreator.CreateButton(Str(args, "name"), parent.transform, text, size);
            return Created(go);
        }

        private string DoCreateText(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var color = new Color(Float(args, "r", 1), Float(args, "g", 1), Float(args, "b", 1), Float(args, "a", 1));
            var go = UICreator.CreateText(Str(args, "name"), parent.transform, Str(args, "content"), Int(args, "fontSize", 18), color);
            return Created(go);
        }

        private string DoCreateImage(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var spritePath = Str(args, "spritePath", "");
            var size = new Vector2(Float(args, "width", 100), Float(args, "height", 100));
            var go = UICreator.CreateImage(Str(args, "name"), parent.transform, spritePath, size);
            return Created(go);
        }

        private string DoCreateInputField(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var go = UICreator.CreateInputField(Str(args, "name"), parent.transform, Str(args, "placeholder", "Enter text..."),
                new Vector2(Float(args, "width", 200), Float(args, "height", 30)));
            return Created(go);
        }

        private string DoCreateSlider(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var go = UICreator.CreateSlider(Str(args, "name"), parent.transform, Float(args, "min", 0), Float(args, "max", 1), Float(args, "value", 0.5f));
            return Created(go);
        }

        private string DoCreateToggle(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var go = UICreator.CreateToggle(Str(args, "name"), parent.transform, Str(args, "label", "Toggle"), Bool(args, "isOn", true));
            return Created(go);
        }

        private string DoCreateScrollView(JObject args)
        {
            var parent = FindGO(Str(args, "parent"));
            if (parent == null) return TargetNotFound(Str(args, "parent"));
            var go = UICreator.CreateScrollView(Str(args, "name"), parent.transform,
                new Vector2(Float(args, "width", 300), Float(args, "height", 200)));
            return Created(go);
        }

        private string DoSetAnchor(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            if (!TryParseEnum(Str(args, "preset"), out AnchorPreset preset))
                return Error($"Invalid preset: {Str(args, "preset")}. Valid: TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight, StretchAll.");
            RectTransformHelper.SetAnchorPreset(go, preset);
            return Ok($"Anchor set to {preset} on '{go.name}'.");
        }

        private string DoSetSize(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            RectTransformHelper.SetSize(go, Float(args, "width"), Float(args, "height"));
            return Ok($"Size set to ({Float(args, "width")}, {Float(args, "height")}) on '{go.name}'.");
        }

        private string DoSetPosition(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            RectTransformHelper.SetPosition(go, Float(args, "x"), Float(args, "y"));
            return Ok($"Position set to ({Float(args, "x")}, {Float(args, "y")}) on '{go.name}'.");
        }

        private string DoSetImageSprite(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var path = Str(args, "spritePath");
            ComponentHelper.SetImageSprite(go, path);
            var img = go.GetComponent<Image>();
            var spriteName = img != null && img.sprite != null ? img.sprite.name : "null";
            return Ok($"Sprite set to '{spriteName}' on '{go.name}'.");
        }

        private string DoSetImageColor(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.SetImageColor(go, new Color(Float(args, "r"), Float(args, "g"), Float(args, "b"), Float(args, "a", 1)));
            return Ok($"Image color set on '{go.name}'.");
        }

        private string DoSetText(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            ComponentHelper.SetText(go, Str(args, "text"));
            return Ok($"Text set on '{go.name}'.");
        }

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
            var align = ParseTextAnchor(Str(args, "childAlignment", "UpperCenter"));
            var pad = BuildPadding(args);
            LayoutHelper.AddVerticalLayout(go, Float(args, "spacing", 8), align, pad);
            return Ok($"VerticalLayoutGroup added to '{go.name}'.");
        }

        private string DoAddHorizontalLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var align = ParseTextAnchor(Str(args, "childAlignment", "MiddleCenter"));
            var pad = BuildPadding(args);
            LayoutHelper.AddHorizontalLayout(go, Float(args, "spacing", 8), align, pad);
            return Ok($"HorizontalLayoutGroup added to '{go.name}'.");
        }

        private string DoAddGridLayout(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddGridLayout(go,
                new Vector2(Float(args, "cellWidth"), Float(args, "cellHeight")),
                new Vector2(Float(args, "spacingX", 0), Float(args, "spacingY", 0)));
            return Ok($"GridLayoutGroup added to '{go.name}'.");
        }

        private string DoAddLayoutElement(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            LayoutHelper.AddLayoutElement(go,
                NullFloat(args, "minWidth"), NullFloat(args, "minHeight"),
                NullFloat(args, "preferredWidth"), NullFloat(args, "preferredHeight"));
            return Ok($"LayoutElement added to '{go.name}'.");
        }

        private string DoInspectHierarchy(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var desc = HierarchyInspector.Describe(go, Int(args, "maxDepth", 4));
            return new JObject { ["success"] = true, ["hierarchy"] = desc }.ToString();
        }

        private string DoInspectComponents(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var desc = HierarchyInspector.DescribeComponents(go);
            return new JObject { ["success"] = true, ["components"] = desc }.ToString();
        }

        private string DoGetSceneOverview()
        {
            var overview = ContextBuilder.BuildSceneOverview();
            return new JObject { ["success"] = true, ["overview"] = overview }.ToString();
        }

        private string DoFindByName(JObject args)
        {
            var name = Str(args, "name");
            var found = SceneHelper.FindByName(name);
            var arr = new JArray();
            foreach (var go in found)
                arr.Add(new JObject { ["name"] = go.name, ["path"] = GetHierarchyPath(go) });
            return new JObject { ["success"] = true, ["count"] = found.Length, ["objects"] = arr }.ToString();
        }

        private string DoSaveAsPrefab(JObject args)
        {
            var go = FindGO(Str(args, "target"));
            if (go == null) return TargetNotFound(Str(args, "target"));
            var path = Str(args, "path");
            var result = PrefabHelper.Create(go, path);
            if (result == null) return Error($"Failed to save prefab to '{path}'.");
            return Ok($"Prefab saved to '{result}'.");
        }

        private string DoExecuteCode(JObject args)
        {
            var code = Str(args, "code");
            if (string.IsNullOrWhiteSpace(code)) return Error("No code provided.");

            CompileResult compileResult;
            try
            {
                compileResult = _compiler.CompileInternal(code);
            }
            catch (Exception e)
            {
                return Error($"Compile exception: {e.Message}");
            }

            if (!compileResult.Success)
                return Error("Compile error: " + string.Join("; ", compileResult.Errors));

            if (compileResult.Assembly == null)
                return Error("Compilation produced no assembly.");

            Type actionType;
            try
            {
                actionType = DynamicCompiler.FindActionType(compileResult.Assembly);
            }
            catch (ReflectionTypeLoadException e)
            {
                var loaderErrors = string.Join("; ", Array.ConvertAll(
                    e.LoaderExceptions ?? Array.Empty<Exception>(), ex => ex?.Message ?? "null"));
                return Error($"Type loading failed: {loaderErrors}");
            }

            if (actionType == null)
                return Error("No IAgentAction implementation found in code.");

            IAgentAction action;
            try
            {
                action = (IAgentAction)Activator.CreateInstance(actionType);
            }
            catch (Exception e)
            {
                return Error($"Failed to instantiate action: {e.Message}");
            }

            var ctx = new ActionContext
            {
                TargetObject = Selection.activeGameObject,
                CanvasRoot = UnityEngine.Object.FindObjectOfType<Canvas>()?.gameObject
            };

            ActionResult actionResult;
            try
            {
                actionResult = action.Execute(ctx);
            }
            catch (Exception e)
            {
                return Error($"Runtime error in execute_code: {e.GetType().Name}: {e.Message}");
            }

            if (actionResult == null)
                return Error("execute_code returned null ActionResult.");

            return actionResult.Success
                ? Ok(actionResult.Message ?? "Done.")
                : Error(actionResult.Message ?? "Unknown error.");
        }

        #region Helpers

        private static GameObject FindGO(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            if (name.Contains("/"))
            {
                var parts = name.Split('/');
                var root = UnityEngine.Object.FindObjectsOfType<GameObject>()
                    .FirstOrDefault(g => g.name == parts[0]);
                if (root == null) return null;
                var current = root.transform;
                for (int i = 1; i < parts.Length; i++)
                {
                    current = current.Find(parts[i]);
                    if (current == null) return null;
                }
                return current.gameObject;
            }

            var all = UnityEngine.Object.FindObjectsOfType<GameObject>();
            return all.FirstOrDefault(g => g.name == name);
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
        {
            return new Color(Float(args, "r", defR), Float(args, "g", defG), Float(args, "b", defB), Float(args, "a", defA));
        }

        private static RectOffset BuildPadding(JObject args)
        {
            int l = Int(args, "padLeft", 0), r = Int(args, "padRight", 0),
                t = Int(args, "padTop", 0), b = Int(args, "padBottom", 0);
            if (l == 0 && r == 0 && t == 0 && b == 0) return null;
            return new RectOffset(l, r, t, b);
        }

        private static TextAnchor ParseTextAnchor(string value)
        {
            if (Enum.TryParse<TextAnchor>(value, true, out var anchor)) return anchor;
            return TextAnchor.UpperCenter;
        }

        private static bool TryParseEnum<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse(value, true, out result);
        }

        private static string Str(JObject args, string key, string def = "")
        {
            return args[key]?.ToString() ?? def;
        }

        private static float Float(JObject args, string key, float def = 0)
        {
            var token = args[key];
            if (token == null) return def;
            return (float)token;
        }

        private static float? NullFloat(JObject args, string key)
        {
            var token = args[key];
            if (token == null) return null;
            return (float)token;
        }

        private static int Int(JObject args, string key, int def = 0)
        {
            var token = args[key];
            if (token == null) return def;
            return (int)token;
        }

        private static bool Bool(JObject args, string key, bool def = false)
        {
            var token = args[key];
            if (token == null) return def;
            return (bool)token;
        }

        private static string Ok(string message)
        {
            return new JObject { ["success"] = true, ["message"] = message }.ToString();
        }

        private static string Error(string message)
        {
            return new JObject { ["success"] = false, ["error"] = message }.ToString();
        }

        private static string Created(GameObject go)
        {
            if (go == null) return Error("Failed to create GameObject.");
            return new JObject { ["success"] = true, ["name"] = go.name, ["path"] = GetHierarchyPath(go) }.ToString();
        }

        private static string TargetNotFound(string name)
        {
            return Error($"GameObject '{name}' not found in scene.");
        }

        #endregion
    }
}

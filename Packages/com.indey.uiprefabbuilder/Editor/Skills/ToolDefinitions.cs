using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Indey.UIPrefabBuilder.Skills
{
    public static class ToolDefinitions
    {
        public static JArray BuildAll()
        {
            var tools = new JArray();
            foreach (var def in All)
                tools.Add(def);
            return tools;
        }

        public static readonly List<JObject> All = new List<JObject>
        {
            Def("search_assets",
                "Search project assets via AssetDatabase. Returns a list of matching asset paths.",
                Props(
                    P("filter", "string", "AssetDatabase search filter, e.g. 't:Sprite', 't:Sprite dialog', 't:Prefab'", true),
                    P("limit", "integer", "Maximum number of results to return", false, 50)
                )),

            Def("get_asset_info",
                "Get information about a specific asset at the given path.",
                Props(P("path", "string", "Asset path, e.g. 'Assets/Sprites/UI/btn_green.png'", true))),

            Def("create_canvas",
                "Create a new Canvas with CanvasScaler and GraphicRaycaster.",
                Props(
                    P("name", "string", "Name of the Canvas GameObject", false, "Canvas"),
                    P("renderMode", "string", "Render mode: ScreenSpaceOverlay, ScreenSpaceCamera, or WorldSpace", false, "ScreenSpaceOverlay")
                )),

            Def("create_panel",
                "Create a Panel (Image) that stretches to fill its parent.",
                Props(
                    P("name", "string", "Name of the Panel", true),
                    P("parent", "string", "Name of the parent GameObject to attach to", true),
                    P("r", "number", "Red (0-1)", false, 1),
                    P("g", "number", "Green (0-1)", false, 1),
                    P("b", "number", "Blue (0-1)", false, 1),
                    P("a", "number", "Alpha (0-1)", false, 1)
                )),

            Def("create_button",
                "Create a Button with a child Text label.",
                Props(
                    P("name", "string", "Name of the Button", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("text", "string", "Button label text", false, "Button"),
                    P("width", "number", "Width in pixels", false, 160),
                    P("height", "number", "Height in pixels", false, 40)
                )),

            Def("create_text",
                "Create a Text element (auto-detects TextMeshPro if available).",
                Props(
                    P("name", "string", "Name of the Text element", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("content", "string", "Text content to display", true),
                    P("fontSize", "integer", "Font size", false, 18),
                    P("r", "number", "Text color Red (0-1)", false, 1),
                    P("g", "number", "Text color Green (0-1)", false, 1),
                    P("b", "number", "Text color Blue (0-1)", false, 1),
                    P("a", "number", "Text color Alpha (0-1)", false, 1)
                )),

            Def("create_image",
                "Create an Image element with optional sprite from a project asset path.",
                Props(
                    P("name", "string", "Name of the Image element", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("spritePath", "string", "Asset path to a Sprite, e.g. 'Assets/Sprites/UI/dialog.png'. Leave empty for no sprite.", false, ""),
                    P("width", "number", "Width in pixels", false, 100),
                    P("height", "number", "Height in pixels", false, 100)
                )),

            Def("create_inputfield",
                "Create an InputField element.",
                Props(
                    P("name", "string", "Name of the InputField", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("placeholder", "string", "Placeholder text", false, "Enter text..."),
                    P("width", "number", "Width", false, 200),
                    P("height", "number", "Height", false, 30)
                )),

            Def("create_slider",
                "Create a Slider element.",
                Props(
                    P("name", "string", "Name of the Slider", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("min", "number", "Minimum value", false, 0),
                    P("max", "number", "Maximum value", false, 1),
                    P("value", "number", "Current value", false, 0.5)
                )),

            Def("create_toggle",
                "Create a Toggle with a label.",
                Props(
                    P("name", "string", "Name of the Toggle", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("label", "string", "Toggle label text", false, "Toggle"),
                    P("isOn", "boolean", "Initial toggle state", false, true)
                )),

            Def("create_scrollview",
                "Create a complete ScrollRect with Viewport (Mask) and Content (ContentSizeFitter) children. Vertical scroll by default. Content child can be targeted by name 'Content' for adding layout groups.",
                Props(
                    P("name", "string", "Name of the ScrollView", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("width", "number", "Width", false, 300),
                    P("height", "number", "Height", false, 200)
                )),

            Def("set_anchor",
                "Set anchor preset on a UI element. Presets: TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight, StretchAll.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("preset", "string", "Anchor preset name", true)
                )),

            Def("set_size",
                "Set the size (sizeDelta) of a UI element's RectTransform.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("width", "number", "Width in pixels", true),
                    P("height", "number", "Height in pixels", true)
                )),

            Def("set_position",
                "Set the anchored position of a UI element's RectTransform.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("x", "number", "X position", true),
                    P("y", "number", "Y position", true)
                )),

            Def("set_image_sprite",
                "Set or change the sprite of an Image component from a project asset path.",
                Props(
                    P("target", "string", "Name of the target GameObject with Image component", true),
                    P("spritePath", "string", "Asset path to sprite", true)
                )),

            Def("set_image_color",
                "Set the color of an Image component.",
                Props(
                    P("target", "string", "Name of the target GameObject with Image component", true),
                    P("r", "number", "Red (0-1)", true),
                    P("g", "number", "Green (0-1)", true),
                    P("b", "number", "Blue (0-1)", true),
                    P("a", "number", "Alpha (0-1)", false, 1)
                )),

            Def("set_text",
                "Set the text content of a Text or TextMeshProUGUI component.",
                Props(
                    P("target", "string", "Name of the target GameObject with Text component", true),
                    P("text", "string", "New text content", true)
                )),

            Def("add_canvas_group",
                "Add or configure a CanvasGroup on a GameObject.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("alpha", "number", "Alpha (0-1)", false, 1),
                    P("interactable", "boolean", "Is interactable", false, true),
                    P("blocksRaycasts", "boolean", "Blocks raycasts", false, true)
                )),

            Def("add_vertical_layout",
                "Add a VerticalLayoutGroup to a GameObject for automatic child arrangement.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("spacing", "number", "Spacing between children in pixels", false, 8),
                    P("childAlignment", "string", "Alignment: UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight", false, "UpperCenter"),
                    P("padLeft", "integer", "Left padding", false, 0),
                    P("padRight", "integer", "Right padding", false, 0),
                    P("padTop", "integer", "Top padding", false, 0),
                    P("padBottom", "integer", "Bottom padding", false, 0)
                )),

            Def("add_horizontal_layout",
                "Add a HorizontalLayoutGroup to a GameObject for automatic child arrangement.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("spacing", "number", "Spacing between children in pixels", false, 8),
                    P("childAlignment", "string", "Alignment: UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight", false, "MiddleCenter"),
                    P("padLeft", "integer", "Left padding", false, 0),
                    P("padRight", "integer", "Right padding", false, 0),
                    P("padTop", "integer", "Top padding", false, 0),
                    P("padBottom", "integer", "Bottom padding", false, 0)
                )),

            Def("add_grid_layout",
                "Add a GridLayoutGroup to a GameObject.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("cellWidth", "number", "Cell width", true),
                    P("cellHeight", "number", "Cell height", true),
                    P("spacingX", "number", "Horizontal spacing", false, 0),
                    P("spacingY", "number", "Vertical spacing", false, 0)
                )),

            Def("add_layout_element",
                "Add a LayoutElement to control how a child behaves in a layout group.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("minWidth", "number", "Minimum width", false),
                    P("minHeight", "number", "Minimum height", false),
                    P("preferredWidth", "number", "Preferred width", false),
                    P("preferredHeight", "number", "Preferred height", false)
                )),

            Def("inspect_hierarchy",
                "Describe the hierarchy tree of a GameObject and its children. Use to verify created UI structure.",
                Props(
                    P("target", "string", "Name of the root GameObject to inspect", true),
                    P("maxDepth", "integer", "Maximum depth to traverse", false, 4)
                )),

            Def("inspect_components",
                "Describe all components on a specific GameObject, including RectTransform details, Image sprite/color, Text content.",
                Props(P("target", "string", "Name of the GameObject to inspect", true))),

            Def("get_scene_overview",
                "Get an overview of all Canvas objects in the current scene. Use this to understand what already exists before creating new UI.",
                Props()),

            Def("find_by_name",
                "Find GameObjects in the scene by exact name. Returns a list of matching objects with their hierarchy paths.",
                Props(P("name", "string", "Exact name to search for", true))),

            Def("save_as_prefab",
                "Save a scene GameObject as a Prefab asset.",
                Props(
                    P("target", "string", "Name of the source GameObject in the scene", true),
                    P("path", "string", "Asset path for the prefab, e.g. 'Assets/Prefabs/MyDialog.prefab'", true)
                )),

            Def("execute_code",
                "Execute arbitrary C# code implementing IAgentAction. Use only when the other tools cannot achieve the desired result.",
                Props(P("code", "string", "Complete C# source code implementing IAgentAction interface", true))),
        };

        private static JObject Def(string name, string description, JObject parameters)
        {
            return new JObject
            {
                ["type"] = "function",
                ["function"] = new JObject
                {
                    ["name"] = name,
                    ["description"] = description,
                    ["parameters"] = parameters
                }
            };
        }

        private static JObject Props(params JProperty[] properties)
        {
            var props = new JObject();
            var required = new JArray();
            foreach (var p in properties)
            {
                if (p == null) continue;
                var propObj = (JObject)p.Value;
                props[p.Name] = propObj;
                if ((bool)(propObj["_required"] ?? false))
                    required.Add(p.Name);
                propObj.Remove("_required");
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = required
            };
        }

        private static JProperty P(string name, string type, string description, bool required, object defaultVal = null)
        {
            var obj = new JObject
            {
                ["type"] = type,
                ["description"] = description,
                ["_required"] = required
            };
            if (defaultVal != null)
                obj["default"] = JToken.FromObject(defaultVal);
            return new JProperty(name, obj);
        }

        private static JProperty P(string name, string type, string description, bool required)
        {
            var obj = new JObject
            {
                ["type"] = type,
                ["description"] = description,
                ["_required"] = required
            };
            return new JProperty(name, obj);
        }
    }
}

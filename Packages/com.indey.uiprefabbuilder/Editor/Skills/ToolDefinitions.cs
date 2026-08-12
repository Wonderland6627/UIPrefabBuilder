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
            // ── Asset Discovery ──
            Def("search_assets",
                "Search project assets via AssetDatabase. Returns a list of matching asset paths.",
                Props(
                    P("filter", "string", "AssetDatabase search filter, e.g. 't:Sprite', 't:Sprite dialog', 't:Prefab'", true),
                    P("limit", "integer", "Maximum number of results to return", false, 50)
                )),

            Def("search_assets_glob",
                "Search project assets by filename glob pattern. More flexible than search_assets for finding files by name. Supports * (any chars) and ? (single char) wildcards.",
                Props(
                    P("pattern", "string", "Glob pattern for filename, e.g. '*popup*', 'btn_*_lg*', '*dialog*.png'", true),
                    P("directory", "string", "Directory to search in, e.g. 'Assets/Sprites'. Default: Assets", false, "Assets"),
                    P("extensions", "string", "Comma-separated file extensions, e.g. '.png,.jpg'. Default: all", false),
                    P("limit", "integer", "Maximum results", false, 50)
                )),

            Def("search_assets_glob_batch",
                "Search multiple glob patterns at once. Returns results grouped by pattern. More efficient than multiple search_assets_glob calls.",
                Props(
                    PArray("patterns", "Array of glob patterns, e.g. ['*popup*', '*chest*', '*arrow*']", true,
                        new JObject { ["type"] = "string" }),
                    P("directory", "string", "Directory to search in, e.g. 'Assets/Sprites'. Default: Assets", false, "Assets"),
                    P("extensions", "string", "Comma-separated file extensions, e.g. '.png,.jpg'. Default: all", false),
                    P("limit", "integer", "Maximum results per pattern", false, 20)
                )),

            Def("get_asset_info",
                "Get information about a specific asset at the given path.",
                Props(P("path", "string", "Asset path, e.g. 'Assets/Sprites/UI/btn_green.png'", true))),

            // ── UI Creation (single) ──
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
                "Create a complete ScrollRect with Viewport (Mask) and Content (ContentSizeFitter) children.",
                Props(
                    P("name", "string", "Name of the ScrollView", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("width", "number", "Width", false, 300),
                    P("height", "number", "Height", false, 200)
                )),

            Def("create_dropdown",
                "Create a Dropdown element with predefined options.",
                Props(
                    P("name", "string", "Name of the Dropdown", true),
                    P("parent", "string", "Name of the parent GameObject", true),
                    P("options", "string", "Comma-separated list of option labels", false, "Option A,Option B,Option C"),
                    P("width", "number", "Width", false, 160),
                    P("height", "number", "Height", false, 30)
                )),

            // ── Batch Creation ──
            Def("create_batch",
                "Create multiple UI elements in a single call. PREFERRED over individual create calls when creating 2+ elements. The 'items' parameter is a JSON string array. Each item: {type, name, parent, text?, spritePath?, width?, height?, fontSize?, r?, g?, b?, a?}. Supported types: canvas, panel, button, text, image, inputfield, slider, toggle, dropdown, scrollview.",
                Props(P("items", "string", "JSON array string of UI element descriptors", true))),

            // ── RectTransform (combined) ──
            Def("set_rect_transform",
                "Set multiple RectTransform properties in one call. All fields optional — only provided fields are modified. PREFERRED over separate set_anchor/set_size/set_position calls. Supports both preset strings and raw anchor floats (raw values take priority over preset).",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("anchor", "string", "Anchor preset: TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight, StretchAll, StretchTop, StretchBottom, StretchLeft, StretchRight", false),
                    P("anchorMinX", "number", "Raw anchor min X (0-1). Takes priority over anchor preset when any anchorMin/Max is set.", false),
                    P("anchorMinY", "number", "Raw anchor min Y (0-1)", false),
                    P("anchorMaxX", "number", "Raw anchor max X (0-1)", false),
                    P("anchorMaxY", "number", "Raw anchor max Y (0-1)", false),
                    P("width", "number", "Width (sizeDelta.x)", false),
                    P("height", "number", "Height (sizeDelta.y)", false),
                    P("x", "number", "Anchored position X", false),
                    P("y", "number", "Anchored position Y", false),
                    P("pivotX", "number", "Pivot X (0-1)", false),
                    P("pivotY", "number", "Pivot Y (0-1)", false)
                )),

            Def("set_rect_transform_batch",
                "Set RectTransform properties for multiple elements in one call. ALWAYS use this when positioning 2+ elements. The 'items' parameter is a JSON string array. Each item: {target, anchor?, anchorMinX?, anchorMinY?, anchorMaxX?, anchorMaxY?, width?, height?, x?, y?, pivotX?, pivotY?}.",
                Props(P("items", "string", "JSON array string of rect transform configs", true))),

            // ── Legacy single-property tools (kept for backward compatibility) ──
            Def("set_anchor",
                "Set anchor preset on a UI element. Presets: TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight, StretchAll, StretchTop, StretchBottom, StretchLeft, StretchRight.",
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

            // ── Image Properties (Batch) ──
            Def("set_image_batch",
                "Set Image component properties for multiple elements in one call. ALWAYS use this when styling 2+ Image elements. Each item: {target (required), spritePath, type, fillMethod, fillAmount, preserveAspect, r/g/b/a}.",
                Props(P("items", "string", "JSON array string of image property configs", true))),

            // ── Image Properties ──
            Def("set_image",
                "Set Image component properties: sprite, type (Simple/Sliced/Tiled/Filled), fill, and preserveAspect. All fields optional except target.",
                Props(
                    P("target", "string", "Name of the target GameObject with Image component", true),
                    P("spritePath", "string", "Asset path to sprite", false),
                    P("type", "string", "Image type: Simple, Sliced, Tiled, Filled", false),
                    P("fillMethod", "string", "Fill method (for Filled type): Horizontal, Vertical, Radial90, Radial180, Radial360", false),
                    P("fillAmount", "number", "Fill amount 0-1 (for Filled type)", false),
                    P("preserveAspect", "boolean", "Preserve sprite aspect ratio", false),
                    P("r", "number", "Color Red (0-1)", false),
                    P("g", "number", "Color Green (0-1)", false),
                    P("b", "number", "Color Blue (0-1)", false),
                    P("a", "number", "Color Alpha (0-1)", false)
                )),

            Def("set_image_sprite",
                "Set or change the sprite of an Image component.",
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

            // ── Text Properties (Batch) ──
            Def("set_text_properties_batch",
                "Set text properties for multiple elements in one call. ALWAYS use this when styling 2+ Text/TMP elements. Each item: {target (required), text, fontSize, alignment, fontStyle, r/g/b/a, overflow}.",
                Props(P("items", "string", "JSON array string of text property configs", true))),

            // ── Text Properties ──
            Def("set_text_properties",
                "Set multiple text properties in one call. Works with both Legacy Text and TextMeshPro. All fields optional except target.",
                Props(
                    P("target", "string", "Name of the target GameObject with Text or TMP component", true),
                    P("text", "string", "Text content", false),
                    P("fontSize", "integer", "Font size", false),
                    P("alignment", "string", "Text alignment: Left, Center, Right, Justified (TMP only)", false),
                    P("fontStyle", "string", "Font style: Normal, Bold, Italic, BoldAndItalic", false),
                    P("r", "number", "Text color Red (0-1)", false),
                    P("g", "number", "Text color Green (0-1)", false),
                    P("b", "number", "Text color Blue (0-1)", false),
                    P("a", "number", "Text color Alpha (0-1)", false),
                    P("overflow", "string", "Text overflow: Overflow, Ellipsis, Truncate", false)
                )),

            Def("set_text",
                "Set the text content of a Text or TextMeshProUGUI component.",
                Props(
                    P("target", "string", "Name of the target GameObject with Text component", true),
                    P("text", "string", "New text content", true)
                )),

            // ── Layout ──
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
                    P("padBottom", "integer", "Bottom padding", false, 0),
                    P("childControlWidth", "boolean", "Whether the layout group controls child widths", false, true),
                    P("childControlHeight", "boolean", "Whether the layout group controls child heights", false, true),
                    P("childForceExpandWidth", "boolean", "Whether children are forced to expand to fill available width. Default false — keep false for design-mockup fidelity.", false, false),
                    P("childForceExpandHeight", "boolean", "Whether children are forced to expand to fill available height. Default false — keep false for design-mockup fidelity.", false, false)
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
                    P("padBottom", "integer", "Bottom padding", false, 0),
                    P("childControlWidth", "boolean", "Whether the layout group controls child widths", false, true),
                    P("childControlHeight", "boolean", "Whether the layout group controls child heights", false, true),
                    P("childForceExpandWidth", "boolean", "Whether children are forced to expand to fill available width. Default false — keep false for design-mockup fidelity.", false, false),
                    P("childForceExpandHeight", "boolean", "Whether children are forced to expand to fill available height. Default false — keep false for design-mockup fidelity.", false, false)
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

            Def("add_layout_element_batch",
                "Add LayoutElement to multiple GameObjects in one call. ALWAYS use this when configuring 2+ layout children. Each item: {target (required), minWidth, minHeight, preferredWidth, preferredHeight, flexibleWidth, flexibleHeight}.",
                Props(P("items", "string", "JSON array string of layout element configs", true))),

            Def("add_layout_element",
                "Add a LayoutElement to control how a child behaves in a layout group.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("minWidth", "number", "Minimum width", false),
                    P("minHeight", "number", "Minimum height", false),
                    P("preferredWidth", "number", "Preferred width", false),
                    P("preferredHeight", "number", "Preferred height", false),
                    P("flexibleWidth", "number", "Flexible width weight (e.g. 1 to fill remaining space)", false),
                    P("flexibleHeight", "number", "Flexible height weight (e.g. 1 to fill remaining space)", false)
                )),

            // ── UI Effects & Components ──
            Def("add_mask",
                "Add a Mask or RectMask2D to a GameObject.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("maskType", "string", "Mask type: Mask or RectMask2D", false, "RectMask2D"),
                    P("showMaskGraphic", "boolean", "Show the mask graphic (only for Mask type)", false, false)
                )),

            Def("add_outline_batch",
                "Add Outline or Shadow effects to multiple UI elements in one call. ALWAYS use this when adding effects to 2+ elements. Each item: {target (required), effectType, r/g/b/a, distanceX, distanceY}.",
                Props(P("items", "string", "JSON array string of outline/shadow configs", true))),

            Def("add_outline",
                "Add an Outline or Shadow effect to a UI element.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("effectType", "string", "Effect type: Outline or Shadow", false, "Outline"),
                    P("r", "number", "Effect color Red (0-1)", false, 0),
                    P("g", "number", "Effect color Green (0-1)", false, 0),
                    P("b", "number", "Effect color Blue (0-1)", false, 0),
                    P("a", "number", "Effect color Alpha (0-1)", false, 0.5),
                    P("distanceX", "number", "Effect distance X", false, 1),
                    P("distanceY", "number", "Effect distance Y", false, -1)
                )),

            Def("configure_selectable",
                "Configure Button/Toggle/Slider transition and navigation. Sets color tint transition by default.",
                Props(
                    P("target", "string", "Name of the target GameObject with Selectable", true),
                    P("transition", "string", "Transition: ColorTint, SpriteSwap, Animation, None", false, "ColorTint"),
                    P("navigationMode", "string", "Navigation: None, Horizontal, Vertical, Automatic, Explicit", false, "Automatic"),
                    P("interactable", "boolean", "Is interactable", false, true)
                )),

            // ── Generic Component ──
            Def("add_component",
                "Add any Unity component to a GameObject by type name. Supports UnityEngine.UI.*, TMPro.*, UnityEngine.* namespaces.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("componentType", "string", "Component type name, e.g. 'Button', 'Outline', 'Shadow', 'ContentSizeFitter', 'CanvasGroup'", true)
                )),

            Def("set_component_property_batch",
                "Set component properties on multiple GameObjects in one call. ALWAYS use this when setting properties on 2+ elements. Each item: {target (required), componentType, propertyName, value, assetPath?}.",
                Props(P("items", "string", "JSON array string of component property configs", true))),

            Def("set_component_property",
                "Set a property or field on any component via reflection. Supports primitives, enums, Color, Vector2/3, and asset references.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("componentType", "string", "Component type name, e.g. 'Image', 'Text', 'Button'", true),
                    P("propertyName", "string", "Name of the property or field to set", true),
                    P("value", "string", "Value to set (primitives, enum names, or 'r,g,b,a' for Color)", true),
                    P("assetPath", "string", "If the property is an asset reference (Sprite, Material, etc.), provide the asset path here", false)
                )),

            // ── Sibling Order ──
            Def("set_sibling_index",
                "Set the sibling index of a GameObject in its parent's children list, controlling rendering and layout order.",
                Props(
                    P("target", "string", "Name of the target GameObject", true),
                    P("index", "integer", "Sibling index (0 = first child)", true)
                )),

            // ── GameObject Management ──
            Def("destroy_object",
                "Delete a GameObject from the scene. Supports undo. REFUSES to delete a prefab instance root or a populated root Canvas — never tear down an instantiated prefab to rebuild it from scratch; adjust it in place instead.",
                Props(
                    P("target", "string", "Name of the target GameObject to destroy", true),
                    P("force", "boolean", "Override the prefab-instance / root-Canvas guard. Only use when the user explicitly asked for removal.", false, false)
                )),

            Def("destroy_object_batch",
                "Delete multiple GameObjects from the scene in one call. ALWAYS use this when destroying 2+ objects (e.g. cleaning up multiple temporary reference prefabs) instead of separate destroy_object calls. Same prefab-instance / root-Canvas guard as destroy_object.",
                Props(
                    PArray("targets", "Array of GameObject names to destroy", true,
                        new JObject { ["type"] = "string" }),
                    P("force", "boolean", "Override the prefab-instance / root-Canvas guard. Only use when the user explicitly asked for removal.", false, false)
                )),

            Def("set_parent",
                "Reparent a GameObject under a new parent.",
                Props(
                    P("target", "string", "Name of the target GameObject to move", true),
                    P("parent", "string", "Name of the new parent GameObject", true),
                    P("worldPositionStays", "boolean", "Keep world position (false for UI elements)", false, false)
                )),

            Def("rename_object",
                "Rename a GameObject.",
                Props(
                    P("target", "string", "Current name of the target GameObject", true),
                    P("newName", "string", "New name for the GameObject", true)
                )),

            // ── Inspection & Query ──
            Def("inspect_hierarchy",
                "Describe the hierarchy tree of a GameObject and its children.",
                Props(
                    P("target", "string", "Name of the root GameObject to inspect", true),
                    P("maxDepth", "integer", "Maximum depth to traverse", false, 4)
                )),

            Def("inspect_hierarchy_batch",
                "Describe the hierarchy tree of multiple GameObjects in one call. ALWAYS use this when inspecting 2+ roots instead of separate inspect_hierarchy calls.",
                Props(
                    PArray("targets", "Array of root GameObject names to inspect", true,
                        new JObject { ["type"] = "string" }),
                    P("maxDepth", "integer", "Maximum depth to traverse (applies to all targets)", false, 4)
                )),

            Def("inspect_components",
                "Describe all components on a specific GameObject.",
                Props(P("target", "string", "Name of the GameObject to inspect", true))),

            Def("inspect_components_batch",
                "Describe all components on multiple GameObjects in one call. ALWAYS use this when inspecting 2+ objects instead of separate inspect_components calls.",
                Props(
                    PArray("targets", "Array of GameObject names to inspect", true,
                        new JObject { ["type"] = "string" })
                )),

            Def("get_scene_overview",
                "Get an overview of all Canvas objects in the current scene.",
                Props()),

            Def("get_scene_info",
                "Get current scene name and path.",
                Props()),

            Def("find_by_name",
                "Find GameObjects in the scene by exact name.",
                Props(P("name", "string", "Exact name to search for", true))),

            Def("find_by_tag",
                "Find GameObjects in the scene by tag.",
                Props(P("tag", "string", "Tag to search for", true))),

            Def("find_ui_elements",
                "Find all UI elements of a specific type in the scene.",
                Props(
                    P("uiType", "string", "UI type: Button, Text, Image, Toggle, Slider, InputField, ScrollRect, Dropdown, or empty for all", false, ""),
                    P("limit", "integer", "Maximum results", false, 50)
                )),

            // ── Scene & Prefab ──
            Def("save_scene",
                "Save the current scene.",
                Props()),

            Def("save_as_prefab",
                "Save a scene GameObject as a NEW Prefab asset. Fails if a prefab already exists at the given path — overwriting existing project prefabs is not allowed. Use a fresh/unused path (e.g. add a suffix) if the target already exists.",
                Props(
                    P("target", "string", "Name of the source GameObject in the scene", true),
                    P("path", "string", "Asset path for the NEW prefab (must not already exist), e.g. 'Assets/Prefabs/MyDialog.prefab'", true)
                )),

            Def("instantiate_prefab",
                "Instantiate a prefab from an asset path into the scene.",
                Props(
                    P("path", "string", "Asset path to the prefab", true),
                    P("parent", "string", "Name of the parent GameObject (optional)", false),
                    P("name", "string", "Override name for the instance (optional)", false)
                )),

            Def("find_prefab_instances",
                "Find all instances of a prefab in the current scene.",
                Props(P("path", "string", "Asset path to the prefab", true))),

            // ── Screenshot & Visual Analysis ──
            Def("take_screenshot",
                "Capture a screenshot of the built UI and save it to Assets/Screenshots/. Uses a temporary ScreenSpaceCamera render so Overlay canvases are included. Only writes the file when the capture is non-uniform (validCapture=true). On failure, no usable file is produced — do NOT analyze a previous gray image at the same path; retake until success.",
                Props(
                    P("filename", "string", "Filename without extension", false, "screenshot"),
                    P("width", "integer", "Screenshot width in pixels (default: scaled design width)", false),
                    P("height", "integer", "Screenshot height in pixels (default: scaled design height)", false)
                )),

            Def("analyze_screenshot",
                "Send a screenshot to the LLM for visual analysis. Rejects blank/nearly-uniform images (success=false). The image is injected as multimodal content so the model can see layout issues. Use only after take_screenshot returns success=true. IMPORTANT: When reproducing a design mockup, ALWAYS pass referenceImagePath. success=true only means the image was delivered — it is NOT a passing verdict; you must follow up with report_visual_verdict.",
                Props(
                    P("screenshotPath", "string", "Asset path to screenshot file, e.g. 'Assets/Screenshots/screenshot.png'", true),
                    P("prompt", "string", "Optional custom prompt. If omitted, a structured checklist prompt is auto-generated (with reference comparison if referenceImagePath is set)", false),
                    P("referenceImagePath", "string", "Asset path to the original design mockup image for comparison. STRONGLY RECOMMENDED when reproducing a design — enables structured diff analysis.", false)
                )),

            Def("report_visual_verdict",
                "Record the structured outcome of comparing a screenshot against the design mockup. Call this right after you have looked at the images returned by analyze_screenshot. Every entry in visibleElements must carry the bounding box where you SEE it; the tool samples those pixels and rejects claims about regions that are indistinguishable from their surroundings. An element you created but cannot see is `missing`, not `visible`. matches=true is also rejected while icon-sized Images still have no sprite (flat colour placeholders). The task cannot be finished until the latest screenshot has an accepted verdict.",
                Props(
                    P("screenshotPath", "string", "Asset path of the screenshot you just analyzed", true),
                    P("matches", "boolean", "true only when the build has no significant visual difference from the mockup", true),
                    PArray("visibleElements", "Elements you can actually SEE, each located on the screenshot: {label, x, y, width, height} with the box normalized 0-1, top-left origin", true,
                        new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["label"] = new JObject { ["type"] = "string", ["description"] = "What the element is, e.g. 'green check mark on first option'" },
                                ["x"] = new JObject { ["type"] = "number", ["description"] = "Left edge, 0-1" },
                                ["y"] = new JObject { ["type"] = "number", ["description"] = "Top edge, 0-1 (top-left origin)" },
                                ["width"] = new JObject { ["type"] = "number", ["description"] = "Width, 0-1" },
                                ["height"] = new JObject { ["type"] = "number", ["description"] = "Height, 0-1" }
                            },
                            ["required"] = new JArray { "label", "x", "y", "width", "height" }
                        }),
                    PArray("missingElements", "Elements present in the mockup but NOT visible in the screenshot, e.g. ['green check marks', 'gender icons']", false,
                        new JObject { ["type"] = "string" }),
                    PArray("mismatches", "Elements that are visible but visually wrong (size, color, sprite, position), each with the fix needed", false,
                        new JObject { ["type"] = "string" }),
                    P("notes", "string", "Optional extra observations", false)
                )),

            // ── Project Config ──
            Def("get_project_config",
                "Get the project-specific configuration including design basics (orientation, resolution), sprite mapping (role→asset path), prefab mapping (role→prefab path), component overrides, and project rules. Use this to understand project conventions before building UI.",
                Props()),

            // ── Progress Tracking ──
            Def("update_progress",
                "Track your progress on the current task. Use this to record completed steps, remaining work, and known issues. Helps maintain direction in long tasks.",
                Props(
                    P("completed", "string", "Comma-separated list of completed steps", false, ""),
                    P("remaining", "string", "Comma-separated list of remaining steps", false, ""),
                    P("issues", "string", "Any known issues to fix", false, "")
                )),

            // ── Code Execution ──
            Def("execute_code",
                "Execute arbitrary C# code implementing IAgentAction. Use for complex batch operations or when other tools cannot achieve the desired result. Destroy / DestroyImmediate / RemoveComponent calls are REJECTED — never delete or rebuild UI here; adjust the existing objects, or use destroy_object which enforces the prefab-instance guard.",
                Props(P("code", "string", "Complete C# source code implementing IAgentAction interface", true))),

            // ── Asset Indexing & Visual Match ──
            Def("match_sprite_by_image",
                "Match a cropped design mockup region against indexed project sprites using visual similarity (CLIP embedding + cosine similarity). Returns Top-K matching sprite asset paths ranked by confidence. Requires asset indexing to be enabled and index built. NOTE: this requires you to supply real cropped image bytes as base64, which you generally CANNOT produce yourself — prefer `match_sprite_by_region` / `match_sprite_by_region_batch` instead, which crop the attached design mockup for you from a bounding box.",
                Props(
                    P("imageBase64", "string", "Base64-encoded PNG/JPG image bytes of the cropped region from the design mockup", true),
                    P("topK", "integer", "Number of top matches to return", false, 5),
                    P("minConfidence", "number", "Minimum confidence threshold (0-1). Results below this score are filtered out.", false, 0.65)
                )),

            Def("crop_design_image",
                "Crop a rectangular region out of the design mockup image attached to this task and return it as a preview image, so you can visually confirm a bounding box before using it with match_sprite_by_region. Coordinates are normalized (0-1), origin at the top-left of the image. Purely for inspection — does not touch the scene.",
                Props(
                    P("x", "number", "Left edge, normalized 0-1 (0 = left edge of image)", true),
                    P("y", "number", "Top edge, normalized 0-1 (0 = top edge of image)", true),
                    P("width", "number", "Width of the region, normalized 0-1", true),
                    P("height", "number", "Height of the region, normalized 0-1", true),
                    P("label", "string", "Optional label for the crop (used in the saved filename)", false, "crop")
                )),

            Def("map_design_rect",
                "Convert a normalized design-mockup bbox (0-1, top-left origin) into Canvas RectTransform values (anchoredPosition + sizeDelta) for center anchor (0.5,0.5). Uses the attached design image pixel size fitted into ProjectConfig design resolution. ALWAYS call this (or the batch variant) before set_rect_transform when reproducing a design mockup — do not guess pixel sizes.",
                Props(
                    P("x", "number", "Left edge, normalized 0-1", true),
                    P("y", "number", "Top edge, normalized 0-1", true),
                    P("width", "number", "Width, normalized 0-1", true),
                    P("height", "number", "Height, normalized 0-1", true),
                    P("label", "string", "Optional label returned in the result", false)
                )),

            Def("map_design_rect_batch",
                "Convert MULTIPLE normalized design-mockup bboxes into Canvas RectTransform values in one call. ALWAYS prefer this over multiple map_design_rect calls.",
                Props(
                    PArray("regions", "Array of regions. Example: [{\"x\":0.1,\"y\":0.05,\"width\":0.8,\"height\":0.1,\"label\":\"header\"}]", true,
                        new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["x"] = new JObject { ["type"] = "number", ["description"] = "Left edge, normalized 0-1" },
                                ["y"] = new JObject { ["type"] = "number", ["description"] = "Top edge, normalized 0-1" },
                                ["width"] = new JObject { ["type"] = "number", ["description"] = "Width, normalized 0-1" },
                                ["height"] = new JObject { ["type"] = "number", ["description"] = "Height, normalized 0-1" },
                                ["label"] = new JObject { ["type"] = "string", ["description"] = "Optional label" }
                            },
                            ["required"] = new JArray { "x", "y", "width", "height" }
                        })
                )),

            Def("match_sprite_by_region",
                "Match a region of the design mockup (given as a normalized bounding box) against indexed project sprites using real visual similarity (CLIP image embedding + cosine similarity) — NO need to crop or encode images yourself, the region is cropped server-side from the mockup attached to this task. This is the PREFERRED way to find the best-matching sprite for a specific visual element (icon, button, frame, etc.) seen in a design mockup. Requires asset indexing enabled and index built.",
                Props(
                    P("x", "number", "Left edge of the element's bounding box, normalized 0-1 (0 = left edge of image)", true),
                    P("y", "number", "Top edge of the element's bounding box, normalized 0-1 (0 = top edge of image)", true),
                    P("width", "number", "Width of the bounding box, normalized 0-1", true),
                    P("height", "number", "Height of the bounding box, normalized 0-1", true),
                    P("topK", "integer", "Number of top matches to return", false, 5),
                    P("minConfidence", "number", "Minimum confidence threshold (0-1). Results below this score are filtered out.", false, 0.65)
                )),

            Def("match_sprite_by_region_batch",
                "Match MULTIPLE regions of the design mockup against indexed project sprites in a single call. ALWAYS prefer this over multiple match_sprite_by_region calls when matching 2+ elements from the same mockup. Each region is a normalized bounding box (x, y, width, height in 0-1, top-left origin).",
                Props(
                    PArray("regions", "Array of regions to match. Example: [{\"x\":0.1,\"y\":0.05,\"width\":0.3,\"height\":0.08,\"label\":\"title banner\"},{\"x\":0.4,\"y\":0.5,\"width\":0.2,\"height\":0.1,\"label\":\"use button\"}]", true,
                        new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["x"] = new JObject { ["type"] = "number", ["description"] = "Left edge, normalized 0-1" },
                                ["y"] = new JObject { ["type"] = "number", ["description"] = "Top edge, normalized 0-1" },
                                ["width"] = new JObject { ["type"] = "number", ["description"] = "Width, normalized 0-1" },
                                ["height"] = new JObject { ["type"] = "number", ["description"] = "Height, normalized 0-1" },
                                ["label"] = new JObject { ["type"] = "string", ["description"] = "Optional label to identify this region in the results" },
                                ["topK"] = new JObject { ["type"] = "integer", ["description"] = "Number of top matches to return for this region (default 5)" }
                            },
                            ["required"] = new JArray("x", "y", "width", "height")
                        }),
                    P("minConfidence", "number", "Minimum confidence threshold (0-1) applied to all regions. Results below this score are filtered out.", false, 0.65)
                )),

            Def("search_sprites_by_text",
                "Search indexed sprites by text description using CLIP text-image matching. Finds sprites that visually match a natural language query (e.g. 'treasure chest', 'red button', 'cat icon', 'gold coin'). Requires asset indexing enabled, index built, and CLIP text model downloaded. More powerful than filename search — understands visual semantics.",
                Props(
                    P("query", "string", "Natural language description of the sprite to find, e.g. 'treasure chest', 'blue button', 'sword icon'", true),
                    P("topK", "integer", "Number of top matches to return", false, 10),
                    P("minConfidence", "number", "Minimum confidence threshold (0-100). Results below this score are filtered out.", false, 15)
                )),

            Def("search_sprites_by_text_batch",
                "Search indexed sprites by multiple text descriptions in one call. Each query uses CLIP text-image matching independently. Much more efficient than calling search_sprites_by_text multiple times — use this when you need to find 2+ different sprites.",
                Props(
                    PArray("queries", "Array of search queries. Example: [{\"query\":\"gold coin\"},{\"query\":\"red button\",\"topK\":3}]", true,
                        new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["query"] = new JObject { ["type"] = "string", ["description"] = "Natural language description of the sprite to find" },
                                ["topK"] = new JObject { ["type"] = "integer", ["description"] = "Number of top matches to return (default 5)" }
                            },
                            ["required"] = new JArray("query")
                        }),
                    P("minConfidence", "number", "Minimum confidence threshold (0-100) applied to all queries. Results below this score are filtered out.", false, 15)
                )),

            Def("rebuild_asset_index",
                "Trigger a full rebuild of the asset visual index. Scans configured sprite directories, computes CLIP embeddings for each sprite, and stores them for fast visual matching. Use when sprites have been bulk-imported or index appears stale. Runs asynchronously in the background.",
                Props()),

            Def("get_index_status",
                "Get the current status of the asset visual index: whether it is ready, the number of indexed entries, whether indexing is in progress, and the last build timestamp.",
                Props()),
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

        private static JProperty PArray(string name, string description, bool required, JObject items)
        {
            var obj = new JObject
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = items,
                ["_required"] = required
            };
            return new JProperty(name, obj);
        }
    }
}

using System;
using System.Collections.Generic;
using Indey.UIPrefabBuilder.Config;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Indey.UIPrefabBuilder.UI
{
    public class ProjectConfigPanel
    {
        private Vector2 _scroll;
        private bool _dirty;

        private bool _foldBasic = true;
        private bool _foldSprites = true;
        private bool _foldPrefabs = true;
        private bool _foldComponents;
        private bool _foldRules;
        private float _panelWidth = 300f;

        private const float PreviewSize = 56f;
        private const int NoHover = int.MinValue;
        private const int SpriteBarHover = -2;
        private const int PrefabBarHover = -3;

        private int _spriteDragHover = NoHover;
        private int _prefabDragHover = NoHover;
        private bool _dragCacheBuilt;
        private bool _requestRepaint;
        private Action _repaint;
        private readonly List<Sprite> _dragSprites = new List<Sprite>();
        private readonly List<GameObject> _dragPrefabs = new List<GameObject>();
        private readonly List<Sprite> _spriteScratch = new List<Sprite>();
        private readonly Dictionary<string, CachedObject<Sprite>> _spriteCache = new Dictionary<string, CachedObject<Sprite>>();
        private readonly Dictionary<string, CachedObject<GameObject>> _prefabCache = new Dictionary<string, CachedObject<GameObject>>();
        private readonly Dictionary<int, CachedObject<Sprite>> _previewSpriteCache = new Dictionary<int, CachedObject<Sprite>>();
        private readonly Dictionary<string, float> _textHeightCache = new Dictionary<string, float>();

        public void Draw(Rect rect, Action requestRepaint = null)
        {
            var config = ProjectConfig.Current;
            _panelWidth = rect.width;
            _repaint = requestRepaint;
            _requestRepaint = false;
            _dragCacheBuilt = false;
            BuilderStyles.EnsureStyles();
            EnsureMappingStyles();

            if (Event.current.type == EventType.DragExited)
            {
                _dragSprites.Clear();
                _dragPrefabs.Clear();
                if (_spriteDragHover != NoHover || _prefabDragHover != NoHover)
                {
                    _spriteDragHover = NoHover;
                    _prefabDragHover = NoHover;
                    _requestRepaint = true;
                }
            }

            GUILayout.BeginArea(rect);
            DrawToolbar();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawBasicInfo(config);
            GUILayout.Space(BuilderStyles.SectionGap);
            DrawSpriteMapping(config);
            GUILayout.Space(BuilderStyles.SectionGap);
            DrawPrefabMapping(config);
            GUILayout.Space(BuilderStyles.SectionGap);
            DrawComponentOverrides(config);
            GUILayout.Space(BuilderStyles.SectionGap);
            DrawRules(config);

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();

            if (_requestRepaint)
                _repaint?.Invoke();
        }

        private void DrawToolbar()
        {
            BuilderStyles.DrawPanelToolbar("Project Config", () =>
            {
                if (_dirty)
                    BuilderStyles.ColoredLabel("*", BuilderStyles.Dirty, BuilderStyles.PanelTitle, GUILayout.Width(8));

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(38)))
                {
                    ProjectConfig.Save();
                    _dirty = false;
                    ClearAssetCaches();
                }
                if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(45)))
                {
                    var path = EditorUtility.SaveFilePanel("Export Project Config", "", "UIPrefabBuilderConfig.json", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        ProjectConfig.Export(path);
                        EditorUtility.RevealInFinder(path);
                    }
                }
                if (GUILayout.Button("Import", EditorStyles.toolbarButton, GUILayout.Width(48)))
                {
                    var path = EditorUtility.OpenFilePanel("Import Project Config", "", "json");
                    if (!string.IsNullOrEmpty(path))
                    {
                        ProjectConfig.Import(path);
                        _dirty = true;
                        ClearAssetCaches();
                    }
                }
                if (GUILayout.Button("Reset", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    if (EditorUtility.DisplayDialog("Reset Project Config", "Reset all project configuration to defaults?", "Reset", "Cancel"))
                    {
                        ProjectConfig.Reset();
                        _dirty = true;
                        ClearAssetCaches();
                    }
                }
            }, 85f);
        }

        #region Basic Info

        private void DrawBasicInfo(ProjectConfig config)
        {
            if (!BuilderStyles.BeginSectionFoldout(ref _foldBasic, "Basic Info"))
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            config.basicInfo.orientation = (BasicInfo.ScreenOrientation)EditorGUILayout.EnumPopup("Orientation", config.basicInfo.orientation);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Resolution", GUILayout.Width(EditorGUIUtility.labelWidth));
            config.basicInfo.designWidth = EditorGUILayout.IntField(config.basicInfo.designWidth, GUILayout.Width(55));
            GUILayout.Label("x", GUILayout.Width(10));
            config.basicInfo.designHeight = EditorGUILayout.IntField(config.basicInfo.designHeight, GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();

            config.basicInfo.screenMatchMode = (BasicInfo.CanvasMatchMode)EditorGUILayout.EnumPopup("Screen Match Mode", config.basicInfo.screenMatchMode);
            if (config.basicInfo.screenMatchMode == BasicInfo.CanvasMatchMode.MatchWidthOrHeight)
                config.basicInfo.canvasMatchMode = EditorGUILayout.Slider("Match W/H", config.basicInfo.canvasMatchMode, 0f, 1f);

            GUILayout.Label("Notes", EditorStyles.miniLabel);
            config.basicInfo.projectNotes = DrawWrappedTextArea(config.basicInfo.projectNotes, 48f);

            if (EditorGUI.EndChangeCheck()) _dirty = true;
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Sprite Mapping

        private void DrawSpriteMapping(ProjectConfig config)
        {
            var list = config.spriteMapping;
            bool addAtTop = false;
            bool expanded = BeginMappingSection(ref _foldSprites, "Sprite Mapping (" + list.Count + ")",
                "Add a sprite entry", () => addAtTop = true);
            if (!expanded)
            {
                if (addAtTop)
                {
                    _foldSprites = true;
                    InsertBlankSprite(list, 0);
                }
                return;
            }

            EnsureDragCache();
            int count = list.Count;
            int removeIndex = -1;
            int hovered = NoHover;
            bool dropped = false;

            for (int i = 0; i < count; i++)
                DrawSpriteCard(list, list[i], i, ref hovered, ref dropped, ref removeIndex);

            string hotHint = _dragSprites.Count > 1
                ? "Release to add " + _dragSprites.Count + " sprites"
                : "Release to add";
            DrawDropBar(
                "Add",
                count == 0 ? "Drop sprites here" : "or drop sprites here",
                hotHint,
                count == 0 ? 44f : 26f,
                _spriteDragHover == SpriteBarHover,
                ref hovered,
                SpriteBarHover,
                _dragSprites.Count > 0,
                () => { list.Add(new SpriteEntry()); ClearAssetCaches(); },
                () => AddDraggedSprites(list));

            CommitMappingHover(ref _spriteDragHover, hovered);
            if (removeIndex >= 0)
            {
                list.RemoveAt(removeIndex);
                _dirty = true;
                ClearAssetCaches();
            }
            if (addAtTop)
                InsertBlankSprite(list, 0);
        }

        private void DrawSpriteCard(List<SpriteEntry> list, SpriteEntry entry, int index,
            ref int hovered, ref bool dropped, ref int removeIndex)
        {
            EditorGUILayout.BeginVertical(CardStyle);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            var previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize,
                GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
            GUILayout.Space(6f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            entry.role = HintTextField(entry.role, "Role", "sprite-role-" + index, RoleFieldStyle);
            if (DrawRemoveButton("Remove"))
                removeIndex = index;
            EditorGUILayout.EndHorizontal();

            var sprite = LoadSprite(entry.assetPath);
            var picked = (Sprite)EditorGUILayout.ObjectField(sprite, typeof(Sprite), false);
            if (picked != sprite)
            {
                AssignSprite(entry, picked, fillRole: true);
                _spriteCache.Remove(entry.assetPath ?? "");
                sprite = LoadSprite(entry.assetPath);
            }

            entry.description = HintTextField(entry.description, "Description", "sprite-desc-" + index, null);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            bool missing = sprite == null && !string.IsNullOrEmpty(entry.assetPath);
            bool warnSlice = sprite != null && entry.imageType == "Sliced" && sprite.border == Vector4.zero;

            EditorGUILayout.BeginHorizontal();
            var prevBg = GUI.backgroundColor;
            if (warnSlice)
                GUI.backgroundColor = new Color(1f, 0.75f, 0.4f);
            entry.imageType = DrawImageTypePopup(entry.imageType);
            GUI.backgroundColor = prevBg;

            var spriteMeta = string.IsNullOrEmpty(entry.assetPath)
                ? "Pick a sprite, or drop one on this card"
                : entry.assetPath;
            var spriteTip = string.IsNullOrEmpty(entry.assetPath)
                ? "Assign a sprite"
                : (missing ? "Missing asset\n" + entry.assetPath : entry.assetPath);
            if (warnSlice)
                spriteTip = "No 9-slice border — Sliced will stretch the corners.\n" + spriteTip;
            var spriteMetaStyle = string.IsNullOrEmpty(entry.assetPath)
                ? DropHintStyle
                : (missing ? MissingPathStyle : PathStyle);
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            if (DrawClippedButton(spriteMeta, spriteTip, spriteMetaStyle, sprite != null))
                PingObject(sprite);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            var cardRect = GUILayoutUtility.GetLastRect();
            bool dragHover = _spriteDragHover == index;
            DrawCardChrome(cardRect, missing, sprite != null, dragHover);
            DrawSpritePreview(previewRect, sprite, dragHover, BuildSpriteTooltip(sprite, entry.assetPath, entry.imageType));
            HandlePreviewClick(previewRect, sprite, false);
            ShowCardMenu(cardRect, sprite, false, () => RemoveSpriteAt(list, index));

            if (dropped)
                return;

            var action = PollDrop(cardRect, _dragSprites.Count > 0);
            if (action == DropAction.Hover)
                hovered = index;
            else if (action == DropAction.Perform)
            {
                DropSpritesOnEntry(list, entry);
                dropped = true;
            }
        }

        private static readonly string[] ImageTypeOptions = { "Simple", "Sliced", "Tiled", "Filled" };

        private static string DrawImageTypePopup(string current)
        {
            int idx = Array.IndexOf(ImageTypeOptions, current);
            int shown = idx < 0 ? 0 : idx;
            int picked = EditorGUILayout.Popup(shown, ImageTypeOptions, GUILayout.Width(76));
            if (idx < 0 && picked == shown)
                return current;
            return ImageTypeOptions[picked];
        }

        #endregion

        #region Prefab Mapping

        private void DrawPrefabMapping(ProjectConfig config)
        {
            var list = config.prefabMapping;
            bool addAtTop = false;
            bool expanded = BeginMappingSection(ref _foldPrefabs, "Prefab Mapping (" + list.Count + ")",
                "Add a prefab entry", () => addAtTop = true);
            if (!expanded)
            {
                if (addAtTop)
                {
                    _foldPrefabs = true;
                    InsertBlankPrefab(list, 0);
                }
                return;
            }

            EnsureDragCache();
            int count = list.Count;
            int removeIndex = -1;
            int hovered = NoHover;
            bool dropped = false;

            for (int i = 0; i < count; i++)
                DrawPrefabCard(list, list[i], i, ref hovered, ref dropped, ref removeIndex);

            string hotHint = _dragPrefabs.Count > 1
                ? "Release to add " + _dragPrefabs.Count + " prefabs"
                : "Release to add";
            DrawDropBar(
                "Add",
                count == 0 ? "Drop prefabs here" : "or drop prefabs here",
                hotHint,
                count == 0 ? 44f : 26f,
                _prefabDragHover == PrefabBarHover,
                ref hovered,
                PrefabBarHover,
                _dragPrefabs.Count > 0,
                () => { list.Add(new PrefabEntry()); ClearAssetCaches(); },
                () => AddDraggedPrefabs(list));

            CommitMappingHover(ref _prefabDragHover, hovered);
            if (removeIndex >= 0)
            {
                list.RemoveAt(removeIndex);
                _dirty = true;
                ClearAssetCaches();
            }
            if (addAtTop)
                InsertBlankPrefab(list, 0);
        }

        private void DrawPrefabCard(List<PrefabEntry> list, PrefabEntry entry, int index,
            ref int hovered, ref bool dropped, ref int removeIndex)
        {
            EditorGUILayout.BeginVertical(CardStyle);
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            var previewRect = GUILayoutUtility.GetRect(PreviewSize, PreviewSize,
                GUILayout.Width(PreviewSize), GUILayout.Height(PreviewSize));
            GUILayout.Space(6f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            entry.role = HintTextField(entry.role, "Role", "prefab-role-" + index, RoleFieldStyle);
            if (DrawRemoveButton("Remove"))
                removeIndex = index;
            EditorGUILayout.EndHorizontal();

            var prefab = LoadPrefab(entry.prefabPath);
            var picked = (GameObject)EditorGUILayout.ObjectField(prefab, typeof(GameObject), false);
            if (picked != prefab)
            {
                AssignPrefab(entry, picked, fillRole: true);
                _prefabCache.Remove(entry.prefabPath ?? "");
                prefab = LoadPrefab(entry.prefabPath);
            }

            entry.description = HintTextField(entry.description, "Description", "prefab-desc-" + index, null);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            bool missing = prefab == null && !string.IsNullOrEmpty(entry.prefabPath);
            var summary = SummarizePrefab(prefab);

            string prefabMeta;
            string prefabTip;
            GUIStyle prefabMetaStyle;
            if (string.IsNullOrEmpty(entry.prefabPath))
            {
                prefabMeta = "Pick a prefab, or drop one on this card";
                prefabTip = "Assign a prefab";
                prefabMetaStyle = DropHintStyle;
            }
            else if (missing)
            {
                prefabMeta = entry.prefabPath;
                prefabTip = "Missing asset\n" + entry.prefabPath;
                prefabMetaStyle = MissingPathStyle;
            }
            else
            {
                prefabMeta = string.IsNullOrEmpty(summary) ? entry.prefabPath : summary;
                prefabTip = string.IsNullOrEmpty(summary) ? entry.prefabPath : summary + "\n" + entry.prefabPath;
                prefabMetaStyle = PathStyle;
            }
            if (EditorGUI.EndChangeCheck())
                _dirty = true;

            if (DrawClippedButton(prefabMeta, prefabTip, prefabMetaStyle, prefab != null))
                PingObject(prefab);
            EditorGUILayout.EndVertical();

            var cardRect = GUILayoutUtility.GetLastRect();
            bool dragHover = _prefabDragHover == index;
            DrawCardChrome(cardRect, missing, prefab != null, dragHover);
            DrawPrefabPreview(previewRect, prefab, dragHover, BuildPrefabTooltip(prefab, entry.prefabPath));
            HandlePreviewClick(previewRect, prefab, true);
            ShowCardMenu(cardRect, prefab, true, () => RemovePrefabAt(list, index));

            if (dropped)
                return;

            var action = PollDrop(cardRect, _dragPrefabs.Count > 0);
            if (action == DropAction.Hover)
                hovered = index;
            else if (action == DropAction.Perform)
            {
                DropPrefabsOnEntry(list, entry);
                dropped = true;
            }
        }

        #endregion

        #region Component Overrides

        private void DrawComponentOverrides(ProjectConfig config)
        {
            if (!BuilderStyles.BeginSectionFoldout(ref _foldComponents, "Component Overrides"))
                return;

            var ov = config.componentOverrides;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();

            ov.textComponent = EditorGUILayout.TextField("Text", ov.textComponent);
            ov.buttonComponent = EditorGUILayout.TextField("Button", ov.buttonComponent);
            ov.imageComponent = EditorGUILayout.TextField("Image", ov.imageComponent);
            ov.inputFieldComponent = EditorGUILayout.TextField("InputField", ov.inputFieldComponent);

            if (EditorGUI.EndChangeCheck()) _dirty = true;

            GUILayout.Space(BuilderStyles.SubGap);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Custom Components", BuilderStyles.SectionLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.AddIconWidth)))
            {
                ov.customComponents.Add(new CustomComponentEntry());
                _dirty = true;
            }
            EditorGUILayout.EndHorizontal();

            int removeIndex = -1;
            for (int i = 0; i < ov.customComponents.Count; i++)
            {
                var c = ov.customComponents[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                c.role = EditorGUILayout.TextField("Role", c.role);
                c.typeName = EditorGUILayout.TextField("Type", c.typeName);
                c.description = EditorGUILayout.TextField("Desc", c.description);

                if (EditorGUI.EndChangeCheck()) _dirty = true;

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0) { ov.customComponents.RemoveAt(removeIndex); _dirty = true; }
            EditorGUILayout.EndVertical();
        }

        #endregion

        #region Rules

        private void DrawRules(ProjectConfig config)
        {
            if (!BuilderStyles.BeginSectionFoldout(ref _foldRules,
                    $"Rules ({config.rules.Count})",
                    () =>
                    {
                        if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.AddIconWidth)))
                        {
                            config.rules.Add("");
                            _dirty = true;
                        }
                    }))
                return;

            EditorGUILayout.HelpBox(
                "Each rule is injected into the AI Agent's system prompt.\n" +
                "Write conventions, coding standards, or project-specific instructions.",
                MessageType.Info);
            GUILayout.Space(2);

            int removeIndex = -1;

            for (int i = 0; i < config.rules.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.BeginChangeCheck();

                config.rules[i] = DrawWrappedTextArea(config.rules[i], 48f);

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(55)))
                    removeIndex = i;
                EditorGUILayout.EndHorizontal();

                if (EditorGUI.EndChangeCheck()) _dirty = true;
                EditorGUILayout.EndVertical();
            }

            if (removeIndex >= 0)
            {
                config.rules.RemoveAt(removeIndex);
                _dirty = true;
            }
        }

        #endregion

        #region Mapping UI

        private static bool _mappingStylesReady;
        private static GUIStyle CardStyle;
        private static GUIStyle RoleFieldStyle;
        private static GUIStyle HintStyle;
        private static GUIStyle PathStyle;
        private static GUIStyle MissingPathStyle;
        private static GUIStyle DropHintStyle;
        private static GUIStyle PreviewCaptionStyle;
        private static GUIStyle ReplaceStyle;
        private static Texture2D _checker;
        private static readonly System.Text.StringBuilder _summary = new System.Text.StringBuilder(64);

        private static void EnsureMappingStyles()
        {
            if (_mappingStylesReady)
                return;
            _mappingStylesReady = true;

            CardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(6, 4, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };
            RoleFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                fontStyle = FontStyle.Bold
            };
            HintStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(3, 3, 0, 0),
                fontSize = EditorStyles.textField.fontSize,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.35f) : new Color(0f, 0f, 0f, 0.4f) }
            };
            PathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(4, 2, 0, 0)
            };
            MissingPathStyle = new GUIStyle(PathStyle);
            MissingPathStyle.normal.textColor = new Color(1f, 0.45f, 0.35f);
            DropHintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 11,
                clipping = TextClipping.Clip,
                wordWrap = false,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.3f, 0.3f, 0.3f) }
            };
            PreviewCaptionStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 8,
                clipping = TextClipping.Clip
            };
            ReplaceStyle = new GUIStyle(EditorStyles.whiteMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        private static Texture2D CheckerTexture
        {
            get
            {
                if (_checker != null)
                    return _checker;

                const int cell = 4;
                const int cells = 8;
                const int size = cell * cells;
                _checker = new Texture2D(size, size, TextureFormat.RGB24, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                    name = "UIPrefabBuilder Checker"
                };
                bool pro = EditorGUIUtility.isProSkin;
                var dark = pro ? new Color(0.16f, 0.16f, 0.16f) : new Color(0.75f, 0.75f, 0.75f);
                var light = pro ? new Color(0.27f, 0.27f, 0.27f) : new Color(0.9f, 0.9f, 0.9f);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                        pixels[y * size + x] = (((x / cell) + (y / cell)) & 1) == 0 ? dark : light;
                }
                _checker.SetPixels(pixels);
                _checker.Apply(false, true);
                return _checker;
            }
        }

        private enum DropAction { None, Hover, Perform }

        private static bool BeginMappingSection(ref bool fold, string title, string addTooltip, Action onAdd)
        {
            return BuilderStyles.BeginSectionFoldout(ref fold, title, () =>
            {
                if (GUILayout.Button(new GUIContent("+ Add", addTooltip), EditorStyles.miniButton, GUILayout.Width(58)))
                    onAdd();
            });
        }

        private void DrawDropBar(string buttonLabel, string hint, string hotHint, float height, bool hot,
            ref int hovered, int hoverId, bool canAccept, Action onAdd, Action onDrop)
        {
            var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            var action = PollDrop(rect, canAccept);
            if (action == DropAction.Hover)
                hovered = hoverId;
            else if (action == DropAction.Perform)
                onDrop?.Invoke();

            if (Event.current.type == EventType.Repaint)
            {
                var fill = hot
                    ? new Color(0.24f, 0.5f, 1f, 0.28f)
                    : new Color(0.5f, 0.5f, 0.5f, EditorGUIUtility.isProSkin ? 0.14f : 0.08f);
                EditorGUI.DrawRect(rect, fill);
                DrawBorder(rect, hot ? BuilderStyles.Accent : new Color(0.55f, 0.55f, 0.55f, 0.7f));
            }

            var btnRect = new Rect(rect.x + 4f, rect.y + Mathf.Max(3f, (height - 18f) * 0.5f), 52f, 18f);
            if (GUI.Button(btnRect, buttonLabel, EditorStyles.miniButton))
            {
                onAdd?.Invoke();
                _dirty = true;
            }

            var hintRect = new Rect(btnRect.xMax + 8f, rect.y, Mathf.Max(0f, rect.width - btnRect.width - 16f), rect.height);
            GUI.Label(hintRect, hot ? hotHint : hint, DropHintStyle);
        }

        private void CommitMappingHover(ref int slot, int hovered)
        {
            var evt = Event.current.type;
            if (evt == EventType.DragPerform)
            {
                slot = NoHover;
                _requestRepaint = true;
                return;
            }
            if (evt != EventType.DragUpdated)
                return;
            if (slot != hovered)
                _requestRepaint = true;
            slot = hovered;
        }

        private DropAction PollDrop(Rect rect, bool accept)
        {
            var evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                return DropAction.None;
            if (rect.width < 2f || !rect.Contains(evt.mousePosition))
                return DropAction.None;
            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                return DropAction.None;

            DragAndDrop.visualMode = accept ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (!accept)
            {
                evt.Use();
                return DropAction.None;
            }
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                evt.Use();
                return DropAction.Perform;
            }
            evt.Use();
            return DropAction.Hover;
        }

        private static void DrawCardChrome(Rect cardRect, bool missing, bool assigned, bool dragHover)
        {
            if (Event.current.type != EventType.Repaint || cardRect.width < 2f)
                return;

            Color stripe;
            if (missing)
                stripe = new Color(1f, 0.55f, 0.25f, 1f);
            else if (assigned)
                stripe = new Color(0.35f, 0.72f, 0.45f, 0.95f);
            else
                stripe = new Color(0.5f, 0.5f, 0.5f, 0.35f);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 3f, cardRect.height), stripe);
            if (dragHover)
                DrawBorder(cardRect, BuilderStyles.Accent);
        }

        private static void DrawBorder(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static bool DrawRemoveButton(string tooltip)
        {
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.66f);
            var hit = GUILayout.Button(new GUIContent("\u00d7", tooltip), EditorStyles.miniButton,
                GUILayout.Width(22f), GUILayout.Height(EditorGUIUtility.singleLineHeight));
            GUI.backgroundColor = prev;
            return hit;
        }

        private static string HintTextField(string value, string hint, string controlName, GUIStyle fieldStyle)
        {
            var style = fieldStyle ?? EditorStyles.textField;
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, style);
            GUI.SetNextControlName(controlName);
            var result = EditorGUI.TextField(rect, value ?? "", style);
            if (string.IsNullOrEmpty(result)
                && Event.current.type == EventType.Repaint
                && GUI.GetNameOfFocusedControl() != controlName)
            {
                var hintRect = new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height);
                HintStyle.Draw(hintRect, new GUIContent(hint), false, false, false, false);
            }
            return result;
        }

        private static bool DrawClippedButton(string text, string tooltip, GUIStyle style, bool link)
        {
            var content = new GUIContent(text ?? "", tooltip ?? "");
            var rect = GUILayoutUtility.GetRect(content, style,
                GUILayout.Height(EditorGUIUtility.singleLineHeight), GUILayout.ExpandWidth(true));
            if (link)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            return GUI.Button(rect, content, style);
        }

        private void DrawSpritePreview(Rect rect, Sprite sprite, bool dragHover, string tooltip)
        {
            DrawThumbnailChrome(rect, sprite == null ? "None" : null);
            if (sprite != null)
                DrawSpriteTexture(rect, sprite);
            DrawPreviewOverlay(rect, sprite, dragHover, tooltip);
        }

        private void DrawPrefabPreview(Rect rect, GameObject prefab, bool dragHover, string tooltip)
        {
            var previewSprite = CachedPreviewSprite(prefab);
            DrawThumbnailChrome(rect, prefab == null ? "Prefab" : null);
            if (previewSprite != null)
                DrawSpriteTexture(rect, previewSprite);
            else if (prefab != null)
                DrawAssetPreview(rect, prefab);
            DrawPreviewOverlay(rect, prefab, dragHover, tooltip);
        }

        private static void DrawThumbnailChrome(Rect rect, string emptyLabel)
        {
            if (Event.current.type != EventType.Repaint || rect.width < 2f)
                return;

            var edge = EditorGUIUtility.isProSkin ? new Color(0.08f, 0.08f, 0.08f) : new Color(0.45f, 0.45f, 0.45f);
            EditorGUI.DrawRect(rect, edge);
            var inner = new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            GUI.DrawTexture(inner, CheckerTexture, ScaleMode.StretchToFill, false);
            if (!string.IsNullOrEmpty(emptyLabel))
                EditorStyles.centeredGreyMiniLabel.Draw(rect, new GUIContent(emptyLabel), false, false, false, false);
        }

        private static void DrawSpriteTexture(Rect outer, Sprite sprite)
        {
            if (Event.current.type != EventType.Repaint || sprite == null)
                return;

            var tex = sprite.texture;
            var tr = sprite.textureRect;
            if (tex == null || tex.width < 1 || tex.height < 1 || tr.width < 1f || tr.height < 1f)
            {
                var fallback = AssetPreview.GetMiniThumbnail(sprite);
                if (fallback != null)
                    GUI.DrawTexture(FitRect(outer, 1f, 8f), fallback, ScaleMode.ScaleToFit, true);
                return;
            }

            var imageRect = new Rect(outer.x, outer.y, outer.width, Mathf.Max(1f, outer.height - 12f));
            var fitted = FitRect(imageRect, tr.width / tr.height, 3f);
            var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
            GUI.DrawTextureWithTexCoords(fitted, tex, uv, true);

            var caption = (int)sprite.rect.width + "\u00d7" + (int)sprite.rect.height;
            var bar = new Rect(outer.x + 1f, outer.yMax - 13f, outer.width - 2f, 12f);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.55f));
            PreviewCaptionStyle.Draw(bar, new GUIContent(caption), false, false, false, false);
        }

        private void DrawAssetPreview(Rect outer, UnityEngine.Object asset)
        {
            if (asset == null || Event.current.type != EventType.Repaint)
                return;

            var preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null)
            {
                preview = AssetPreview.GetMiniThumbnail(asset);
                if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                    _requestRepaint = true;
            }
            if (preview == null)
                return;

            float aspect = preview.height > 0 ? preview.width / (float)preview.height : 1f;
            GUI.DrawTexture(FitRect(outer, aspect, 4f), preview, ScaleMode.ScaleToFit, true);
        }

        private static void DrawPreviewOverlay(Rect rect, UnityEngine.Object asset, bool dragHover, string tooltip)
        {
            if (rect.width < 2f)
                return;
            if (asset != null)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            GUI.Label(rect, new GUIContent(string.Empty, tooltip ?? ""), GUIStyle.none);
            if (!dragHover || Event.current.type != EventType.Repaint)
                return;
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.45f, 0.9f, 0.4f));
            ReplaceStyle.Draw(rect, new GUIContent("Replace"), false, false, false, false);
        }

        private static void HandlePreviewClick(Rect rect, UnityEngine.Object obj, bool openOnDoubleClick)
        {
            if (obj == null || rect.width < 2f)
                return;
            var evt = Event.current;
            if (evt.type != EventType.MouseDown || evt.button != 0 || !rect.Contains(evt.mousePosition))
                return;

            if (openOnDoubleClick && evt.clickCount >= 2)
                AssetDatabase.OpenAsset(obj);
            else
                PingObject(obj);
            evt.Use();
        }

        private void ShowCardMenu(Rect rect, UnityEngine.Object obj, bool isPrefab, Action remove)
        {
            if (Event.current.type != EventType.ContextClick || rect.width < 2f || !rect.Contains(Event.current.mousePosition))
                return;

            var menu = new GenericMenu();
            if (obj != null)
            {
                var target = obj;
                menu.AddItem(new GUIContent("Locate"), false, () => PingObject(target));
                if (isPrefab)
                    menu.AddItem(new GUIContent("Open"), false, () => AssetDatabase.OpenAsset(target));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Locate"));
            }

            menu.AddItem(new GUIContent("Remove"), false, () =>
            {
                EditorApplication.delayCall += () =>
                {
                    remove?.Invoke();
                    _repaint?.Invoke();
                };
            });
            menu.ShowAsContext();
            Event.current.Use();
        }

        private static Rect FitRect(Rect outer, float aspect, float pad)
        {
            var inner = new Rect(
                outer.x + pad,
                outer.y + pad,
                Mathf.Max(1f, outer.width - pad * 2f),
                Mathf.Max(1f, outer.height - pad * 2f));
            if (aspect <= 0.01f)
                return inner;

            float box = inner.width / inner.height;
            if (aspect > box)
            {
                float h = inner.width / aspect;
                return new Rect(inner.x, inner.y + (inner.height - h) * 0.5f, inner.width, h);
            }

            float w = inner.height * aspect;
            return new Rect(inner.x + (inner.width - w) * 0.5f, inner.y, w, inner.height);
        }

        private static string BuildSpriteTooltip(Sprite sprite, string path, string imageType)
        {
            if (sprite == null)
                return string.IsNullOrEmpty(path) ? "Drop a sprite to assign it" : "Missing\n" + path;

            var size = sprite.rect;
            var text = sprite.name + "\n" + (int)size.width + "\u00d7" + (int)size.height;
            var border = sprite.border;
            if (border != Vector4.zero)
            {
                text += "\nBorder L " + border.x.ToString("0")
                    + "  B " + border.y.ToString("0")
                    + "  R " + border.z.ToString("0")
                    + "  T " + border.w.ToString("0");
            }
            else if (imageType == "Sliced")
            {
                text += "\nNo 9-slice border";
            }
            text += "\nClick to locate";
            if (!string.IsNullOrEmpty(path))
                text += "\n" + path;
            return text;
        }

        private static string BuildPrefabTooltip(GameObject prefab, string path)
        {
            if (prefab == null)
                return string.IsNullOrEmpty(path) ? "Drop a prefab to assign it" : "Missing\n" + path;

            var text = prefab.name + "\nClick to locate, double-click to open";
            if (!string.IsNullOrEmpty(path))
                text += "\n" + path;
            return text;
        }

        private static Sprite FindPreviewSprite(GameObject prefab)
        {
            if (prefab == null)
                return null;

            Sprite best = null;
            float bestArea = 0f;
            var images = prefab.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                if (image == null || image.sprite == null)
                    continue;
                float area = image.sprite.rect.width * image.sprite.rect.height;
                if (image.gameObject == prefab && area >= 16f)
                    return image.sprite;
                if (area <= bestArea)
                    continue;
                bestArea = area;
                best = image.sprite;
            }
            if (best != null)
                return best;

            var renderer = prefab.GetComponentInChildren<SpriteRenderer>(true);
            return renderer != null ? renderer.sprite : null;
        }

        private static string SummarizePrefab(GameObject prefab)
        {
            if (prefab == null)
                return "";

            var sb = _summary;
            sb.Clear();
            int shown = 0;
            var comps = prefab.GetComponents<Component>();
            for (int i = 0; i < comps.Length && shown < 3; i++)
            {
                var component = comps[i];
                if (component == null)
                    continue;
                var name = component.GetType().Name;
                if (name == "RectTransform" || name == "Transform" || name == "CanvasRenderer")
                    continue;
                if (shown > 0)
                    sb.Append(" · ");
                sb.Append(name);
                shown++;
            }

            int children = prefab.transform.childCount;
            if (children > 0)
            {
                if (shown > 0)
                    sb.Append(" · ");
                sb.Append(children).Append(children == 1 ? " child" : " children");
            }
            return sb.Length == 0 ? "Empty prefab" : sb.ToString();
        }

        private void EnsureDragCache()
        {
            if (_dragCacheBuilt)
                return;
            _dragCacheBuilt = true;

            var type = Event.current.type;
            if (type != EventType.DragUpdated && type != EventType.DragPerform)
                return;

            _dragSprites.Clear();
            _dragPrefabs.Clear();
            var refs = DragAndDrop.objectReferences;
            if (refs == null)
                return;

            for (int i = 0; i < refs.Length; i++)
            {
                CollectSpritesFromObject(refs[i]);
                var prefab = AsPrefabAsset(refs[i]);
                if (prefab != null && !_dragPrefabs.Contains(prefab))
                    _dragPrefabs.Add(prefab);
            }
        }

        private void CollectSpritesFromObject(UnityEngine.Object obj)
        {
            if (obj is Sprite sprite)
            {
                AddDragSprite(sprite);
                return;
            }
            if (!(obj is Texture2D))
                return;

            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path))
                return;

            _spriteScratch.Clear();
            var reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
            for (int i = 0; i < reps.Length; i++)
            {
                if (reps[i] is Sprite sub)
                    _spriteScratch.Add(sub);
            }
            if (_spriteScratch.Count == 0)
            {
                var main = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (main != null)
                    AddDragSprite(main);
                return;
            }
            if (_spriteScratch.Count > 8)
            {
                var pick = _spriteScratch[0];
                for (int i = 0; i < _spriteScratch.Count; i++)
                {
                    if (_spriteScratch[i].name == obj.name)
                    {
                        pick = _spriteScratch[i];
                        break;
                    }
                }
                AddDragSprite(pick);
                return;
            }
            for (int i = 0; i < _spriteScratch.Count; i++)
                AddDragSprite(_spriteScratch[i]);
        }

        private void AddDragSprite(Sprite sprite)
        {
            if (sprite == null || _dragSprites.Contains(sprite))
                return;
            _dragSprites.Add(sprite);
        }

        private static GameObject AsPrefabAsset(UnityEngine.Object obj)
        {
            var go = obj as GameObject;
            if (go == null)
                return null;
            if (PrefabUtility.IsPartOfPrefabAsset(go))
                return go;
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return null;
            return PrefabUtility.GetCorrespondingObjectFromOriginalSource(go) as GameObject;
        }

        private void InsertBlankSprite(List<SpriteEntry> list, int index)
        {
            list.Insert(index, new SpriteEntry());
            _dirty = true;
            ClearAssetCaches();
        }

        private void InsertBlankPrefab(List<PrefabEntry> list, int index)
        {
            list.Insert(index, new PrefabEntry());
            _dirty = true;
            ClearAssetCaches();
        }

        private void RemoveSpriteAt(List<SpriteEntry> list, int index)
        {
            if (index < 0 || index >= list.Count)
                return;
            list.RemoveAt(index);
            _dirty = true;
            ClearAssetCaches();
        }

        private void RemovePrefabAt(List<PrefabEntry> list, int index)
        {
            if (index < 0 || index >= list.Count)
                return;
            list.RemoveAt(index);
            _dirty = true;
            ClearAssetCaches();
        }

        private void AddDraggedSprites(List<SpriteEntry> list)
        {
            for (int i = 0; i < _dragSprites.Count; i++)
            {
                var sprite = _dragSprites[i];
                var path = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(path) || ContainsSpritePath(list, path))
                    continue;
                list.Add(new SpriteEntry
                {
                    role = sprite.name,
                    assetPath = path,
                    imageType = "Simple"
                });
                _dirty = true;
                ClearAssetCaches();
            }
        }

        private void AddDraggedPrefabs(List<PrefabEntry> list)
        {
            for (int i = 0; i < _dragPrefabs.Count; i++)
            {
                var prefab = _dragPrefabs[i];
                var path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || ContainsPrefabPath(list, path))
                    continue;
                list.Add(new PrefabEntry
                {
                    role = prefab.name,
                    prefabPath = path
                });
                _dirty = true;
                ClearAssetCaches();
            }
        }

        private void DropSpritesOnEntry(List<SpriteEntry> list, SpriteEntry entry)
        {
            if (_dragSprites.Count == 0)
                return;
            AssignSprite(entry, _dragSprites[0], true);
            _dirty = true;
            ClearAssetCaches();
            for (int i = 1; i < _dragSprites.Count; i++)
            {
                var sprite = _dragSprites[i];
                var path = AssetDatabase.GetAssetPath(sprite);
                if (string.IsNullOrEmpty(path) || ContainsSpritePath(list, path))
                    continue;
                list.Add(new SpriteEntry
                {
                    role = sprite.name,
                    assetPath = path,
                    imageType = "Simple"
                });
                ClearAssetCaches();
            }
        }

        private void DropPrefabsOnEntry(List<PrefabEntry> list, PrefabEntry entry)
        {
            if (_dragPrefabs.Count == 0)
                return;
            AssignPrefab(entry, _dragPrefabs[0], true);
            _dirty = true;
            ClearAssetCaches();
            for (int i = 1; i < _dragPrefabs.Count; i++)
            {
                var prefab = _dragPrefabs[i];
                var path = AssetDatabase.GetAssetPath(prefab);
                if (string.IsNullOrEmpty(path) || ContainsPrefabPath(list, path))
                    continue;
                list.Add(new PrefabEntry
                {
                    role = prefab.name,
                    prefabPath = path
                });
                ClearAssetCaches();
            }
        }

        private static void AssignSprite(SpriteEntry entry, Sprite sprite, bool fillRole)
        {
            if (sprite == null)
            {
                entry.assetPath = "";
                return;
            }
            entry.assetPath = AssetDatabase.GetAssetPath(sprite);
            if (fillRole && string.IsNullOrEmpty(entry.role))
                entry.role = sprite.name;
        }

        private static void AssignPrefab(PrefabEntry entry, GameObject picked, bool fillRole)
        {
            if (picked == null)
            {
                entry.prefabPath = "";
                return;
            }
            var asset = AsPrefabAsset(picked);
            if (asset == null)
                return;
            entry.prefabPath = AssetDatabase.GetAssetPath(asset);
            if (fillRole && string.IsNullOrEmpty(entry.role))
                entry.role = asset.name;
        }

        private static bool ContainsSpritePath(List<SpriteEntry> list, string path)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].assetPath == path)
                    return true;
            }
            return false;
        }

        private static bool ContainsPrefabPath(List<PrefabEntry> list, string path)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].prefabPath == path)
                    return true;
            }
            return false;
        }

        private Sprite LoadSprite(string path)
        {
            return LoadCached(_spriteCache, path, assetPath => AssetDatabase.LoadAssetAtPath<Sprite>(assetPath));
        }

        private GameObject LoadPrefab(string path)
        {
            return LoadCached(_prefabCache, path, assetPath => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
        }

        private static T LoadCached<T>(Dictionary<string, CachedObject<T>> cache, string path, Func<string, T> load) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (cache.TryGetValue(path, out var slot))
            {
                if (slot.Missing || slot.Value != null)
                    return slot.Value;
                cache.Remove(path);
            }

            var loaded = load(path);
            cache[path] = new CachedObject<T> { Value = loaded, Missing = loaded == null };
            return loaded;
        }

        private Sprite CachedPreviewSprite(GameObject prefab)
        {
            if (prefab == null)
                return null;
            int id = prefab.GetInstanceID();
            if (_previewSpriteCache.TryGetValue(id, out var slot))
            {
                if (slot.Missing || slot.Value != null)
                    return slot.Value;
                _previewSpriteCache.Remove(id);
            }

            var found = FindPreviewSprite(prefab);
            _previewSpriteCache[id] = new CachedObject<Sprite> { Value = found, Missing = found == null };
            return found;
        }

        private void ClearAssetCaches()
        {
            _spriteCache.Clear();
            _prefabCache.Clear();
            _previewSpriteCache.Clear();
            _textHeightCache.Clear();
        }

        private sealed class CachedObject<T> where T : UnityEngine.Object
        {
            public T Value;
            public bool Missing;
        }

        private static void PingObject(UnityEngine.Object obj)
        {
            if (obj == null)
                return;
            EditorGUIUtility.PingObject(obj);
            Selection.activeObject = obj;
        }

        #endregion

        /// <summary>
        /// Text area that grows to fit its wrapped content. The height must come out identical in the
        /// Layout and Repaint passes, so it is derived from the panel rect rather than from a measured
        /// control rect (which is unreliable during Layout).
        /// </summary>
        private string DrawWrappedTextArea(string text, float minHeight)
        {
            var style = BuilderStyles.WrappedTextArea;
            var value = text ?? "";
            var wrapWidth = Mathf.Max(80f, _panelWidth - 40f);
            var key = wrapWidth.ToString("0.0") + "\n" + minHeight.ToString("0.0") + "\n" + value;
            if (!_textHeightCache.TryGetValue(key, out var height))
            {
                height = style.CalcHeight(new GUIContent(value), wrapWidth);
                if (value.EndsWith("\n"))
                    height += style.lineHeight;
                height = Mathf.Max(minHeight, height + 4f);
                if (_textHeightCache.Count > 64)
                    _textHeightCache.Clear();
                _textHeightCache[key] = height;
            }
            return EditorGUILayout.TextArea(value, style, GUILayout.Height(height), GUILayout.ExpandWidth(true));
        }
    }
}

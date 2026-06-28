using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Core;
using Indey.UIPrefabBuilder.Logging;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class BuilderWindow : EditorWindow
    {
        [MenuItem("Window/UI Prefab Builder/Open Builder", priority = 1000)]
        public static void Open() => GetWindow<BuilderWindow>("UI Prefab Builder").Show();

        private AgentEngine _agent;
        private string _input = "";
        private Vector2 _chatScroll, _inputScroll, _historyScroll;
        private bool _needsRepaint;
        private float _lastRepaint;

        private readonly List<ChatBubble> _bubbles = new List<ChatBubble>();
        private readonly List<string> _attachedFiles = new List<string>();
        private string _streamBuffer = "";
        private string _thinkingStreamBuffer = "";
        private bool _isStreaming;
        private bool _isThinking;
        private bool _showHistory;

        // Styles
        private GUIStyle _headerStyle, _userBubble, _aiBubble, _thinkBubble, _codeBubble, _resultBubble, _toolCallBubble;
        private GUIStyle _toolCallLabel;
        private GUIStyle _inputStyle, _statusLabel;
        private bool _stylesInit;

        // Sub-panels
        private SessionManager _sessions;
        private SettingsPanel _settingsPanel;
        private ConsolePanel _consolePanel;
        private ProjectConfigPanel _projectConfigPanel;

        // Split views
        private SplitView _hSplitLeft;   // left | center+right
        private SplitView _hSplitRight;  // center | right
        private SplitView _vSplitLeft;   // left-top | left-bottom (console)

        private void OnEnable()
        {
            ConsoleLogger.Initialize();

            var settings = BuilderSettings.Get();
            _agent = new AgentEngine(settings);
            _agent.OnStateChanged += s =>
            {
                if (s == AgentState.Thinking || s == AgentState.CallingTool)
                    FinalizeContentStream();
                _needsRepaint = true;
            };
            _agent.OnStreamToken += t => { _streamBuffer += t; _isStreaming = true; _needsRepaint = true; };
            _agent.OnThinkingToken += t => { _thinkingStreamBuffer += t; _isThinking = true; _needsRepaint = true; };
            _agent.OnThinking += t => { FinalizeThinkingStream(t); };
            _agent.OnComplete += full => { FinishStream(full); };
            _agent.OnError += e => { AddBubble(BubbleType.Error, e); _isStreaming = false; _isThinking = false; };
            _agent.OnExecuted += r => { };
            _agent.OnToolCall += (name, args, result) =>
            {
                if (result != null)
                    AddBubble(BubbleType.ToolCall, FormatToolCall(name, args, result));
            };

            _sessions = new SessionManager();
            _settingsPanel = new SettingsPanel();
            _consolePanel = new ConsolePanel();
            _projectConfigPanel = new ProjectConfigPanel();
            _sessions.RestoreLastSession(_agent.History, _bubbles);

            _hSplitLeft = new SplitView("UIPrefabBuilder_HSplitLeft", SplitDirection.Horizontal, 220f, 150f, 400f);
            _hSplitRight = new SplitView("UIPrefabBuilder_HSplitRight", SplitDirection.Horizontal, 220f, 150f, 400f);
            _vSplitLeft = new SplitView("UIPrefabBuilder_VSplitLeft", SplitDirection.Vertical, 200f, 80f);

            ConsoleLogger.OnLogAdded += _ => _needsRepaint = true;

            EditorApplication.update += Tick;
            ConsoleLogger.Log("BuilderWindow opened");
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            _sessions.SaveCurrentSession(_agent.History, _bubbles);
            _agent?.Dispose();
        }

        private void Tick()
        {
            if (!_needsRepaint) return;
            if (Time.realtimeSinceStartup - _lastRepaint < 0.05f) return;
            _lastRepaint = Time.realtimeSinceStartup;
            _needsRepaint = false;
            Repaint();
        }

        private void OnGUI()
        {
            InitStyles();
            DrawHeader();

            var bodyRect = new Rect(0, EditorStyles.toolbar.fixedHeight, position.width, position.height - EditorStyles.toolbar.fixedHeight);

            // Right split: measure from right edge inward
            var rightWidth = _hSplitRight.SplitPosition;
            var rightPanelRect = new Rect(bodyRect.xMax - rightWidth, bodyRect.y, rightWidth, bodyRect.height);
            var leftAndCenterRect = new Rect(bodyRect.x, bodyRect.y, bodyRect.width - rightWidth - 4f, bodyRect.height);

            // Left split within leftAndCenter
            var leftPanelRect = _hSplitLeft.BeginSplit(leftAndCenterRect);
            var centerPanelRect = _hSplitLeft.EndSplit(leftAndCenterRect);

            // Right splitter handle (between center and right)
            var rightSplitterRect = new Rect(rightPanelRect.x - 4f, bodyRect.y, 4f, bodyRect.height);
            HandleRightSplitter(rightSplitterRect, bodyRect);
            EditorGUI.DrawRect(rightSplitterRect, new Color(0.15f, 0.15f, 0.15f, 1f));

            DrawLeftPanel(leftPanelRect);
            DrawCenterPanel(centerPanelRect);
            DrawRightPanel(rightPanelRect);

            HandleKeyboard();
        }

        #region Right Splitter (custom, drags from right)
        private bool _rightSplitterDragging;

        private void HandleRightSplitter(Rect splitterRect, Rect bodyRect)
        {
            var hitRect = new Rect(splitterRect.x - 2, splitterRect.y, splitterRect.width + 4, splitterRect.height);
            EditorGUIUtility.AddCursorRect(hitRect, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (hitRect.Contains(e.mousePosition)) { _rightSplitterDragging = true; e.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (_rightSplitterDragging)
                    {
                        var newWidth = bodyRect.xMax - e.mousePosition.x;
                        newWidth = Mathf.Clamp(newWidth, 150f, Mathf.Min(400f, bodyRect.width - _hSplitLeft.SplitPosition - 200f));
                        // Store via EditorPrefs since SplitView doesn't expose setter
                        EditorPrefs.SetFloat("UIPrefabBuilder_HSplitRight", newWidth);
                        _hSplitRight = new SplitView("UIPrefabBuilder_HSplitRight", SplitDirection.Horizontal, newWidth, 150f, 400f);
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_rightSplitterDragging) { _rightSplitterDragging = false; e.Use(); }
                    break;
            }
        }
        #endregion

        #region Header
        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UIPrefabBuilder", _headerStyle, GUILayout.Width(120));
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("New Chat", EditorStyles.toolbarButton, GUILayout.Width(65))) NewChat();
            if (GUILayout.Button(_showHistory ? "Close History" : "History", EditorStyles.toolbarButton, GUILayout.Width(85)))
                _showHistory = !_showHistory;
            if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(50))) ExportChat();

            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Left Panel
        private void DrawLeftPanel(Rect rect)
        {
            var topRect = _vSplitLeft.BeginSplit(rect);
            var bottomRect = _vSplitLeft.EndSplit(rect);

            _settingsPanel.Draw(topRect);
            _consolePanel.Draw(bottomRect);
        }
        #endregion

        #region Center Panel (Chat)
        private void DrawCenterPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);

            if (_showHistory)
            {
                DrawHistoryOverlay();
            }
            else
            {
                DrawChatArea();
            }

            DrawStatusBar();
            DrawInputArea();

            GUILayout.EndArea();
        }

        private void DrawChatArea()
        {
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll, GUILayout.ExpandHeight(true));

            foreach (var b in _bubbles)
                DrawBubble(b);

            // Thinking stream (real-time)
            if (_isThinking && !string.IsNullOrEmpty(_thinkingStreamBuffer))
            {
                DrawBubble(new ChatBubble { Type = BubbleType.Thinking, Content = _thinkingStreamBuffer });
            }

            // Content stream (real-time)
            if (_isStreaming && !string.IsNullOrEmpty(_streamBuffer))
            {
                DrawBubble(new ChatBubble { Type = BubbleType.AIStream, Content = _streamBuffer });
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawHistoryOverlay()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            GUILayout.Label("Chat History", EditorStyles.boldLabel);
            GUILayout.Space(4);

            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll);
            var sessions = _sessions.ListSessions();
            foreach (var s in sessions)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.BeginVertical();
                GUILayout.Label(s.Title, EditorStyles.boldLabel);
                GUILayout.Label(s.UpdatedAt.ToString("g"), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (GUILayout.Button("Load", GUILayout.Width(50)))
                {
                    _sessions.LoadSession(s.SessionId, _agent.History, _bubbles);
                    _showHistory = false;
                }
                if (GUILayout.Button("\u00d7", GUILayout.Width(24))) _sessions.DeleteSession(s.SessionId);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawBubble(ChatBubble b)
        {
            bool isUser = b.Type == BubbleType.User;
            GUIStyle style;
            switch (b.Type)
            {
                case BubbleType.User: style = _userBubble; break;
                case BubbleType.AI: case BubbleType.AIStream: style = _aiBubble; break;
                case BubbleType.Thinking: style = _thinkBubble; break;
                case BubbleType.Code: style = _codeBubble; break;
                case BubbleType.Result: style = _resultBubble; break;
                case BubbleType.Error: style = _resultBubble; break;
                case BubbleType.ToolCall: style = _toolCallBubble; break;
                default: style = _aiBubble; break;
            }

            float maxBubbleWidth = position.width * 0.55f;

            EditorGUILayout.BeginHorizontal();

            if (isUser)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Space(20);
            }
            else
            {
                GUILayout.Space(4);
            }

            EditorGUILayout.BeginVertical(style, GUILayout.MaxWidth(maxBubbleWidth));

            if (b.Type == BubbleType.Thinking)
            {
                GUILayout.Label("Thinking...", EditorStyles.miniBoldLabel);
                GUILayout.Label(b.Content, EditorStyles.wordWrappedMiniLabel);
            }
            else if (b.Type == BubbleType.Code)
            {
                GUILayout.Label("Generated Code:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(b.Content, EditorStyles.textArea, GUILayout.MinHeight(60));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy", GUILayout.Width(50))) EditorGUIUtility.systemCopyBuffer = b.Content;
                EditorGUILayout.EndHorizontal();
            }
            else if (b.Type == BubbleType.ToolCall)
            {
                GUILayout.Label(b.Content, _toolCallLabel);
            }
            else
            {
                GUILayout.Label(b.Content, EditorStyles.wordWrappedLabel);
            }

            if (b.AttachedFilePaths != null && b.AttachedFilePaths.Count > 0)
            {
                GUILayout.Space(2);
                foreach (var fp in b.AttachedFilePaths)
                    GUILayout.Label("\ud83d\uddbc " + Path.GetFileName(fp), EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();

            if (isUser)
            {
                GUILayout.Space(4);
            }
            else
            {
                GUILayout.Space(20);
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawAttachmentArea();

            // Input text area
            _inputScroll = EditorGUILayout.BeginScrollView(_inputScroll, GUILayout.Height(55));
            _input = EditorGUILayout.TextArea(_input, _inputStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // Bottom row: Send/Stop + Confirm/Undo
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            var state = _agent?.State ?? AgentState.Idle;
            bool isRunning = state == AgentState.Thinking || state == AgentState.CallingTool
                || state == AgentState.WaitingForLLM || state == AgentState.BuildingContext
                || state == AgentState.Compiling || state == AgentState.Executing;
            if (isRunning)
            {
                if (GUILayout.Button("Stop", GUILayout.Width(55))) _agent?.Cancel();
            }
            else
            {
                bool hasContent = !string.IsNullOrWhiteSpace(_input) || _attachedFiles.Count > 0;
                GUI.enabled = hasContent;
                if (GUILayout.Button("Send", GUILayout.Width(55))) Send();
                GUI.enabled = true;
            }

            if (state == AgentState.WaitingConfirmation)
            {
                if (GUILayout.Button("Confirm", GUILayout.Width(60))) _agent?.Confirm();
                if (GUILayout.Button("Undo", GUILayout.Width(45))) _agent?.Rollback();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static readonly HashSet<string> AllowedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp"
        };
        private const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB

        private void DrawAttachmentArea()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                var path = EditorUtility.OpenFilePanelWithFilters(
                    "Select Design Mockup", "",
                    new[] { "Image files", "png,jpg,jpeg,webp,bmp", "All files", "*" });
                if (!string.IsNullOrEmpty(path))
                    TryAddAttachment(path);
            }
            if (_attachedFiles.Count == 0)
                GUILayout.Label("Attach design mockup...", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_attachedFiles.Count == 0) return;

            int removeIdx = -1;
            for (int i = 0; i < _attachedFiles.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8);
                GUILayout.Label("\u2022 " + Path.GetFileName(_attachedFiles[i]),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(18)))
                    removeIdx = i;
                EditorGUILayout.EndHorizontal();
            }
            if (removeIdx >= 0) _attachedFiles.RemoveAt(removeIdx);
        }

        private void TryAddAttachment(string path)
        {
            var ext = Path.GetExtension(path);
            if (!AllowedImageExtensions.Contains(ext))
            {
                EditorUtility.DisplayDialog("Unsupported Format",
                    $"Only image files are supported ({string.Join(", ", AllowedImageExtensions)}).\nSelected: {ext}", "OK");
                return;
            }

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                EditorUtility.DisplayDialog("File Not Found", $"File does not exist:\n{path}", "OK");
                return;
            }
            if (info.Length > MaxFileBytes)
            {
                EditorUtility.DisplayDialog("File Too Large",
                    $"File size ({info.Length / (1024 * 1024):F1} MB) exceeds the 10 MB limit.", "OK");
                return;
            }

            if (!_attachedFiles.Contains(path))
                _attachedFiles.Add(path);
        }

        private void DrawStatusBar()
        {
            var state = _agent?.State ?? AgentState.Idle;
            var (label, color) = GetStateInfo(state);

            EditorGUILayout.BeginHorizontal();
            var prev = GUI.color;
            GUI.color = color;
            GUILayout.Label("\u25cf", GUILayout.Width(12));
            GUI.color = prev;
            GUILayout.Label(label, _statusLabel, GUILayout.Width(90));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Msgs: {_agent?.History?.Count ?? 0}", _statusLabel);
            GUILayout.Space(4);
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(42))) ClearChat();
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Right Panel
        private void DrawRightPanel(Rect rect)
        {
            _projectConfigPanel.Draw(rect);
        }
        #endregion

        #region State Info
        private (string, Color) GetStateInfo(AgentState s) => s switch
        {
            AgentState.Idle => ("Idle", Color.gray),
            AgentState.Thinking => ("Thinking...", new Color(0.3f, 0.6f, 1f)),
            AgentState.CallingTool => ("Calling tool...", new Color(1f, 0.7f, 0.2f)),
            AgentState.WaitingConfirmation => ("Confirm?", new Color(0.2f, 0.9f, 0.4f)),
            AgentState.Completed => ("Done", new Color(0.2f, 0.8f, 0.2f)),
            AgentState.Error => ("Error", Color.red),
            AgentState.BuildingContext => ("Context...", new Color(0.4f, 0.7f, 1f)),
            AgentState.WaitingForLLM => ("Thinking...", new Color(0.3f, 0.6f, 1f)),
            AgentState.ExtractingCode => ("Extracting...", new Color(0.5f, 0.5f, 1f)),
            AgentState.Compiling => ("Compiling...", new Color(1f, 0.8f, 0.2f)),
            AgentState.Executing => ("Executing...", new Color(1f, 0.6f, 0.2f)),
            AgentState.ObservingResult => ("Observing...", new Color(0.2f, 0.8f, 0.2f)),
            _ => ("Unknown", Color.gray)
        };
        #endregion

        #region Actions
        private void Send()
        {
            var text = _input?.Trim() ?? "";
            bool hasText = !string.IsNullOrWhiteSpace(text);
            bool hasFiles = _attachedFiles.Count > 0;

            if (!hasText && !hasFiles) return;

            if (hasFiles && !BuilderSettings.Get().SupportsVision)
            {
                EditorUtility.DisplayDialog("Vision Not Enabled",
                    "The current model does not have 'Supports Vision' enabled.\n\n" +
                    "Please enable it in Settings before sending images.", "OK");
                return;
            }

            var displayText = hasText ? text : "";
            if (hasFiles)
            {
                var fileNames = new StringBuilder();
                foreach (var f in _attachedFiles)
                    fileNames.AppendLine($"  [{Path.GetFileName(f)}]");
                displayText = (hasText ? text + "\n" : "") + fileNames.ToString().TrimEnd();
            }
            AddBubble(BubbleType.User, displayText, hasFiles ? new List<string>(_attachedFiles) : null);

            if (hasFiles)
                _agent?.StartTask(text, new List<string>(_attachedFiles));
            else
                _agent?.StartTask(text);

            _input = "";
            _attachedFiles.Clear();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
            GUI.FocusControl(null);
        }

        private void FinalizeThinkingStream(string fullThinking)
        {
            if (!string.IsNullOrWhiteSpace(_thinkingStreamBuffer))
                AddBubble(BubbleType.Thinking, _thinkingStreamBuffer);
            else if (!string.IsNullOrWhiteSpace(fullThinking))
                AddBubble(BubbleType.Thinking, fullThinking);
            _thinkingStreamBuffer = "";
            _isThinking = false;
        }

        private void FinalizeContentStream()
        {
            if (!string.IsNullOrWhiteSpace(_streamBuffer))
                AddBubble(BubbleType.AI, _streamBuffer);
            _streamBuffer = "";
            _isStreaming = false;
        }

        private void FinishStream(string full)
        {
            _isStreaming = false;
            if (!string.IsNullOrWhiteSpace(full))
                AddBubble(BubbleType.AI, full);
            _streamBuffer = "";
            _sessions.SaveCurrentSession(_agent.History, _bubbles);
        }

        private void NewChat()
        {
            _sessions.SaveCurrentSession(_agent.History, _bubbles);
            _bubbles.Clear();
            _agent.History.Clear();
            _agent.Refresh();
            _sessions.NewSession();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
            ConsoleLogger.Log("New chat session created");
        }

        private void ClearChat()
        {
            _bubbles.Clear();
            _agent?.History?.Clear();
            _agent?.Refresh();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
        }

        private void ExportChat()
        {
            if (_bubbles.Count == 0) return;
            var modelName = BuilderSettings.Get().ModelName;
            var md = SessionManager.ExportToMarkdown(_bubbles, modelName);
            var defaultDir = SessionManager.DataDirectory;
            var defaultName = $"chat_export_{DateTime.Now:yyyyMMdd_HHmmss}.md";
            var path = EditorUtility.SaveFilePanel("Export Chat", defaultDir, defaultName, "md");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, md, Encoding.UTF8);
            ConsoleLogger.Log($"Chat exported to: {path}");
            EditorUtility.RevealInFinder(path);
        }
        #endregion

        #region Helpers
        private void AddBubble(BubbleType type, string content, List<string> attachedFiles = null)
        {
            if (string.IsNullOrWhiteSpace(content) && (attachedFiles == null || attachedFiles.Count == 0)) return;
            _bubbles.Add(new ChatBubble { Type = type, Content = content ?? "", AttachedFilePaths = attachedFiles });
            _chatScroll.y = float.MaxValue;
            _needsRepaint = true;
        }

        private static string FormatToolCall(string name, string args, string result)
        {
            var sb = new StringBuilder();
            sb.Append($"\u2699 {name}");
            try
            {
                var resObj = Newtonsoft.Json.Linq.JObject.Parse(result);
                var success = (bool)(resObj["success"] ?? false);
                var msg = resObj["message"]?.ToString() ?? resObj["error"]?.ToString();
                sb.Append(success ? " \u2714" : " \u2716");
                if (!string.IsNullOrEmpty(msg)) sb.Append($" {msg}");
            }
            catch
            {
                if (result.Length > 120) sb.Append($": {result.Substring(0, 120)}...");
                else sb.Append($": {result}");
            }
            return sb.ToString();
        }

        private void HandleKeyboard()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Return && (e.control || e.command))
            {
                Send();
                e.Use();
            }
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _userBubble = CreateBubbleStyle(new Color(0.15f, 0.38f, 0.75f, 0.35f));
            _aiBubble = CreateBubbleStyle(new Color(0.22f, 0.22f, 0.28f, 0.35f));
            _thinkBubble = CreateBubbleStyle(new Color(0.5f, 0.4f, 0.7f, 0.2f));
            _codeBubble = CreateBubbleStyle(new Color(0.1f, 0.1f, 0.15f, 0.4f));
            _resultBubble = CreateBubbleStyle(new Color(0.1f, 0.5f, 0.2f, 0.25f));
            _toolCallBubble = CreateBubbleStyle(new Color(0.12f, 0.3f, 0.45f, 0.25f));
            _toolCallLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                richText = false,
                normal = { textColor = new Color(0.6f, 0.8f, 0.9f) }
            };
            _inputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 };
            _statusLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        }

        private static GUIStyle CreateBubbleStyle(Color bg)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, bg);
            tex.Apply();
            return new GUIStyle(EditorStyles.helpBox)
            {
                normal = { background = tex },
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(4, 4, 2, 2),
                wordWrap = true
            };
        }
        #endregion
    }

    public enum BubbleType { User, AI, AIStream, Thinking, Code, Result, Error, ToolCall }

    public class ChatBubble
    {
        public BubbleType Type;
        public string Content;
        public bool Expanded;
        public List<string> AttachedFilePaths;
    }
}

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
        [MenuItem("Window/UI Prefab Builder")]
        public static void Open() => GetWindow<BuilderWindow>("UI Prefab Builder").Show();

        private AgentEngine _agent;
        private string _input = "";
        private Vector2 _chatScroll, _inputScroll, _historyScroll, _rightScroll;
        private bool _needsRepaint;
        private float _lastRepaint;

        private readonly List<ChatBubble> _bubbles = new List<ChatBubble>();
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

        // Split views
        private SplitView _hSplitLeft;   // left | center+right
        private SplitView _hSplitRight;  // center | right
        private SplitView _vSplitLeft;   // left-top | left-bottom (console)

        private void OnEnable()
        {
            ConsoleLogger.Initialize();

            var settings = BuilderSettings.Get();
            _agent = new AgentEngine(settings);
            _agent.OnStateChanged += s => { _needsRepaint = true; };
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

            DrawLeftTopPlaceholder(topRect);
            _consolePanel.Draw(bottomRect);
        }

        private void DrawLeftTopPlaceholder(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            GUILayout.Label("Project Config", EditorStyles.miniBoldLabel);
            GUILayout.Space(8);
            EditorGUILayout.HelpBox("This area is reserved for project constraint configuration in a future release.", MessageType.Info);
            GUILayout.EndArea();
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

            // Context buttons row
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("@Scene", EditorStyles.miniButton, GUILayout.Width(55)))
                _input += $"\n[Context: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}]";
            if (GUILayout.Button("@Selection", EditorStyles.miniButton, GUILayout.Width(65)))
            {
                var sel = Selection.activeGameObject;
                if (sel != null) _input += $"\n[Selection: {ContextBuilder.GetPath(sel)}]";
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

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
                GUI.enabled = !string.IsNullOrWhiteSpace(_input);
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
            GUILayout.BeginArea(rect);
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            _settingsPanel.DrawLLMSettings();
            GUILayout.Space(6);
            _settingsPanel.DrawAgentSettings();
            GUILayout.Space(6);
            DrawSkillsList();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSkillsList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Skills", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(55))) _agent?.RefreshSkills();
            if (GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(40))) OpenSkillsDirectory();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            var skills = _agent?.Skills?.AllSkills;
            if (skills != null)
            {
                foreach (var s in skills)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    GUILayout.Label(s.Name, EditorStyles.boldLabel);
                    GUILayout.Label(s.Description, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.EndVertical();
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
            if (string.IsNullOrWhiteSpace(_input)) return;
            AddBubble(BubbleType.User, _input.Trim());
            _agent?.StartTask(_input.Trim());
            _input = "";
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
            _agent.RefreshSkills();
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
            _agent?.RefreshSkills();
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
        private static void OpenSkillsDirectory()
        {
            var path = Path.GetFullPath("Packages/com.indey.uiprefabbuilder/Skills~");
            if (Directory.Exists(path))
                EditorUtility.RevealInFinder(path);
            else
                ConsoleLogger.Warning("Skills~ directory not found: " + path);
        }

        private void AddBubble(BubbleType type, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            _bubbles.Add(new ChatBubble { Type = type, Content = content });
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
    }
}

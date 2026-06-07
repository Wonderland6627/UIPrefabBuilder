using System;
using System.Collections.Generic;
using Indey.UIPrefabBuilder.Config;
using Indey.UIPrefabBuilder.Core;
using UnityEditor;
using UnityEngine;

namespace Indey.UIPrefabBuilder.UI
{
    public class BuilderWindow : EditorWindow
    {
        private enum Tab { Chat, History, Skills }

        [MenuItem("Window/UI Prefab Builder")]
        public static void Open() => GetWindow<BuilderWindow>("UI Prefab Builder").Show();

        private AgentEngine _agent;
        private Tab _currentTab = Tab.Chat;
        private string _input = "";
        private Vector2 _chatScroll, _inputScroll, _historyScroll, _skillsScroll;
        private bool _showSettings;
        private bool _needsRepaint;
        private float _lastRepaint;

        private readonly List<ChatBubble> _bubbles = new List<ChatBubble>();
        private string _streamBuffer = "";
        private bool _isStreaming;

        // Styles
        private GUIStyle _headerStyle, _userBubble, _aiBubble, _thinkBubble, _codeBubble, _resultBubble;
        private GUIStyle _inputStyle, _buttonStyle, _tabActive, _tabInactive, _statusLabel, _toolbarBtn;
        private bool _stylesInit;

        private SessionManager _sessions;
        private SettingsPanel _settingsPanel;

        private void OnEnable()
        {
            var settings = BuilderSettings.Get();
            _agent = new AgentEngine(settings);
            _agent.OnStateChanged += s => { _needsRepaint = true; };
            _agent.OnStreamToken += t => { _streamBuffer += t; _isStreaming = true; _needsRepaint = true; };
            _agent.OnThinking += t => { AddBubble(BubbleType.Thinking, t); };
            _agent.OnComplete += full => { FinishStream(full); };
            _agent.OnError += e => { AddBubble(BubbleType.Error, e); _isStreaming = false; };
            _agent.OnExecuted += r => { AddBubble(r.Success ? BubbleType.Result : BubbleType.Error, r.Message); };
            _sessions = new SessionManager();
            _settingsPanel = new SettingsPanel();
            _sessions.RestoreLastSession(_agent.History, _bubbles);
            EditorApplication.update += Tick;
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
            if (_showSettings) { _settingsPanel.Draw(); return; }
            DrawTabBar();
            switch (_currentTab)
            {
                case Tab.Chat: DrawChatTab(); break;
                case Tab.History: DrawHistoryTab(); break;
                case Tab.Skills: DrawSkillsTab(); break;
            }
            DrawStatusBar();
            HandleKeyboard();
        }

        #region Header
        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UI Prefab Builder", _headerStyle, GUILayout.Width(140));
            GUILayout.FlexibleSpace();
            var settings = BuilderSettings.Get();
            EditorGUI.BeginChangeCheck();
            var model = EditorGUILayout.TextField(settings.ModelName, GUILayout.Width(150));
            if (EditorGUI.EndChangeCheck()) { settings.ModelName = model; settings.Save(); }
            if (GUILayout.Button(new GUIContent("+", "New Chat"), _toolbarBtn, GUILayout.Width(24))) NewChat();
            var settingsIcon = _showSettings ? EditorGUIUtility.IconContent("d_clear") : EditorGUIUtility.IconContent("d_Settings");
            if (GUILayout.Button(settingsIcon, _toolbarBtn, GUILayout.Width(26))) _showSettings = !_showSettings;
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region TabBar
        private void DrawTabBar()
        {
            EditorGUILayout.BeginHorizontal();
            DrawTab("Chat", Tab.Chat);
            DrawTab("History", Tab.History);
            DrawTab("Skills", Tab.Skills);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            DrawContextBar();
        }

        private void DrawTab(string label, Tab tab)
        {
            var style = _currentTab == tab ? _tabActive : _tabInactive;
            if (GUILayout.Button(label, style, GUILayout.Width(70))) _currentTab = tab;
        }

        private void DrawContextBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            GUILayout.Label($"Scene: {scene}", _statusLabel, GUILayout.ExpandWidth(false));
            GUILayout.Space(8);
            var sel = Selection.activeGameObject;
            if (sel != null) GUILayout.Label($"Selected: {sel.name}", _statusLabel, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Chat Tab
        private void DrawChatTab()
        {
            var chatRect = EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll);
            foreach (var b in _bubbles)
                DrawBubble(b);
            if (_isStreaming && !string.IsNullOrEmpty(_streamBuffer))
                DrawBubble(new ChatBubble { Type = BubbleType.AIStream, Content = _streamBuffer });
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
            DrawInputArea();
        }

        private void DrawBubble(ChatBubble b)
        {
            GUIStyle style;
            switch (b.Type)
            {
                case BubbleType.User: style = _userBubble; break;
                case BubbleType.AI: case BubbleType.AIStream: style = _aiBubble; break;
                case BubbleType.Thinking: style = _thinkBubble; break;
                case BubbleType.Code: style = _codeBubble; break;
                case BubbleType.Result: style = _resultBubble; break;
                case BubbleType.Error: style = _resultBubble; break;
                default: style = _aiBubble; break;
            }

            EditorGUILayout.BeginHorizontal();
            if (b.Type == BubbleType.User) GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(style, GUILayout.MaxWidth(position.width * 0.8f));
            if (b.Type == BubbleType.Thinking)
            {
                b.Expanded = EditorGUILayout.Foldout(b.Expanded, "Thinking...");
                if (b.Expanded) GUILayout.Label(b.Content, EditorStyles.wordWrappedMiniLabel);
            }
            else if (b.Type == BubbleType.Code)
            {
                GUILayout.Label("Generated Code:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(b.Content, EditorStyles.textArea, GUILayout.MinHeight(60));
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Copy", GUILayout.Width(50))) EditorGUIUtility.systemCopyBuffer = b.Content;
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label(b.Content, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndVertical();
            if (b.Type != BubbleType.User) GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("@Scene", EditorStyles.miniButton, GUILayout.Width(60)))
                _input += $"\n[Context: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}]";
            if (GUILayout.Button("@Selection", EditorStyles.miniButton, GUILayout.Width(70)))
            {
                var sel = Selection.activeGameObject;
                if (sel != null) _input += $"\n[Selection: {ContextBuilder.GetPath(sel)}]";
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            _inputScroll = EditorGUILayout.BeginScrollView(_inputScroll, GUILayout.Height(60));
            _input = EditorGUILayout.TextArea(_input, _inputStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var state = _agent?.State ?? AgentState.Idle;
            if (state != AgentState.Idle && state != AgentState.Error && state != AgentState.WaitingConfirmation)
            {
                if (GUILayout.Button("Stop", GUILayout.Width(60))) _agent?.Cancel();
            }
            else
            {
                GUI.enabled = !string.IsNullOrWhiteSpace(_input);
                if (GUILayout.Button("Send", GUILayout.Width(60))) Send();
                GUI.enabled = true;
            }

            if (state == AgentState.WaitingConfirmation)
            {
                if (GUILayout.Button("Confirm", GUILayout.Width(70))) _agent?.Confirm();
                if (GUILayout.Button("Undo", GUILayout.Width(50))) _agent?.Rollback();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        #endregion

        #region History Tab
        private void DrawHistoryTab()
        {
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
                    _currentTab = Tab.Chat;
                }
                if (GUILayout.Button("×", GUILayout.Width(24))) _sessions.DeleteSession(s.SessionId);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Skills Tab
        private void DrawSkillsTab()
        {
            _skillsScroll = EditorGUILayout.BeginScrollView(_skillsScroll);
            if (GUILayout.Button("Refresh Skills")) _agent?.RefreshSkills();
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
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region Status Bar
        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var state = _agent?.State ?? AgentState.Idle;
            var (label, color) = GetStateInfo(state);
            var prev = GUI.color; GUI.color = color;
            GUILayout.Label("●", GUILayout.Width(14));
            GUI.color = prev;
            GUILayout.Label(label, _statusLabel, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Msgs: {_agent?.History?.Count ?? 0}", _statusLabel);
            GUILayout.Space(8);
            if (GUILayout.Button("Undo All AI", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                // TransactionManager rollback happens via agent
            }
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50))) ClearChat();
            EditorGUILayout.EndHorizontal();
        }

        private (string, Color) GetStateInfo(AgentState s) => s switch
        {
            AgentState.Idle => ("Idle", Color.gray),
            AgentState.BuildingContext => ("Context...", new Color(0.4f, 0.7f, 1f)),
            AgentState.WaitingForLLM => ("Thinking...", new Color(0.3f, 0.6f, 1f)),
            AgentState.ExtractingCode => ("Extracting...", new Color(0.5f, 0.5f, 1f)),
            AgentState.Compiling => ("Compiling...", new Color(1f, 0.8f, 0.2f)),
            AgentState.Executing => ("Executing...", new Color(1f, 0.6f, 0.2f)),
            AgentState.ObservingResult => ("Observing...", new Color(0.2f, 0.8f, 0.2f)),
            AgentState.WaitingConfirmation => ("Confirm?", new Color(0.2f, 0.9f, 0.4f)),
            AgentState.Error => ("Error", Color.red),
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
            _isStreaming = false;
            GUI.FocusControl(null);
        }

        private void FinishStream(string full)
        {
            _isStreaming = false;
            AddBubble(BubbleType.AI, full);
            _streamBuffer = "";
            var code = Indey.UIPrefabBuilder.Compiler.CodeExtractor.ExtractFirst(full);
            if (!string.IsNullOrEmpty(code)) AddBubble(BubbleType.Code, code);
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
            _isStreaming = false;
        }

        private void ClearChat()
        {
            _bubbles.Clear();
            _agent?.History?.Clear();
            _agent?.RefreshSkills();
            _streamBuffer = "";
            _isStreaming = false;
        }
        #endregion

        #region Helpers
        private void AddBubble(BubbleType type, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            _bubbles.Add(new ChatBubble { Type = type, Content = content });
            _chatScroll.y = float.MaxValue;
            _needsRepaint = true;
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
            _userBubble = CreateBubbleStyle(new Color(0.2f, 0.45f, 0.85f, 0.25f));
            _aiBubble = CreateBubbleStyle(new Color(0.25f, 0.25f, 0.3f, 0.3f));
            _thinkBubble = CreateBubbleStyle(new Color(0.5f, 0.4f, 0.7f, 0.2f));
            _codeBubble = CreateBubbleStyle(new Color(0.1f, 0.1f, 0.15f, 0.4f));
            _resultBubble = CreateBubbleStyle(new Color(0.1f, 0.5f, 0.2f, 0.25f));
            _inputStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, fontSize = 13 };
            _buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            _tabActive = new GUIStyle(EditorStyles.toolbarButton) { fontStyle = FontStyle.Bold };
            _tabInactive = new GUIStyle(EditorStyles.toolbarButton);
            _statusLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            _toolbarBtn = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 14 };
        }

        private static GUIStyle CreateBubbleStyle(Color bg)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, bg);
            tex.Apply();
            return new GUIStyle(EditorStyles.helpBox) { normal = { background = tex }, padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(4, 4, 2, 2), wordWrap = true };
        }
        #endregion
    }

    public enum BubbleType { User, AI, AIStream, Thinking, Code, Result, Error }

    public class ChatBubble
    {
        public BubbleType Type;
        public string Content;
        public bool Expanded;
    }
}

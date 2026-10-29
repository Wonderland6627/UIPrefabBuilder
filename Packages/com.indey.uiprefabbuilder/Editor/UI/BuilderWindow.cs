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
        public static void ShowWindow() => GetWindow<BuilderWindow>("UI Prefab Builder").Show();

        private AgentEngine _agent;
        private string _input = "";
        private Vector2 _chatScroll, _inputScroll, _historyScroll;
        private bool _needsRepaint;
        private float _lastRepaint;

        private readonly List<ChatBubble> _bubbles = new List<ChatBubble>();
        [NonSerialized] private List<float> _bubbleOffsets = new List<float>();
        [NonSerialized] private List<float> _bubbleHeights = new List<float>();
        [NonSerialized] private bool _bubbleLayoutDirty = true;
        [NonSerialized] private float _bubbleLayoutWidth = -1f;
        [NonSerialized] private int _bubbleLayoutCount = -1;
        [NonSerialized] private int _chatVisibleFirst;
        [NonSerialized] private int _chatVisibleLast;
        [NonSerialized] private bool _chatShowThinking;
        [NonSerialized] private bool _chatShowStreaming;
        [NonSerialized] private bool _endGuiPass;
        private const float ChatVirtualizationBuffer = 360f;
        private readonly List<string> _attachedFiles = new List<string>();
        private bool _visionSendPending;
        private string _streamBuffer = "";
        private string _thinkingStreamBuffer = "";
        private bool _isStreaming;
        private bool _isThinking;
        private bool _showHistory;
        private string _titleRequestedSessionId;
        private PrefabWorkflowMode _workflowMode = PrefabWorkflowMode.Auto;
        private bool _changedAssetsExpanded = true;
        private Vector2 _changedAssetsScroll;
        private readonly List<SessionAssetChange> _orderedChangedAssets = new List<SessionAssetChange>();
        private bool _changedAssetsDirty = true;
        private bool _consoleRepaintPending;
        private double _lastConsoleRepaint;
        private const string WorkflowModePref = "UIPrefabBuilder_WorkflowMode";
        private bool _ignoreStream;
        private double _lastSessionPersist;

        [NonSerialized] private double _turnStartedAt;
        [NonSerialized] private double _turnElapsed;
        [NonSerialized] private bool _turnTiming;
        [NonSerialized] private bool _turnVisible;
        [NonSerialized] private double _responseStartedAt;
        [NonSerialized] private double _thinkingStartedAt;
        [NonSerialized] private double _thinkingEndedAt;
        [NonSerialized] private double _contentStartedAt;
        [NonSerialized] private float _unstampedThinking = -1f;
        [NonSerialized] private int _thinkingStampIndex;
        [NonSerialized] private int _responseHistoryCount;
        [NonSerialized] private bool _awaitingContent;
        [NonSerialized] private ChatBubble _openTool;
        [NonSerialized] private ChatBubble _openDirective;
        [NonSerialized] private string _openDirectiveMessageId;

        // Styles
        private GUIStyle _headerStyle, _userBubble, _aiBubble, _thinkBubble, _codeBubble, _resultBubble, _errorBubble, _toolCallBubble;
        private GUIStyle _directiveBubble, _directiveTitle, _directiveBody;
        private GUIStyle _toolCallLabel, _toolCallHeader;
        private GUIStyle _codeTitle, _codeBody, _codeNote;
        private float _centerWidth;
        private GUIStyle _markdownBody, _codeBlockStyle;
        private GUIStyle _inputStyle, _statusLabel, _elapsedLabel;
        private bool _stylesInit;
        private bool _markdownFailed;

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
            _bubbleOffsets = _bubbleOffsets ?? new List<float>();
            _bubbleHeights = _bubbleHeights ?? new List<float>();
            InvalidateBubbleLayouts();
            ConsoleLogger.Initialize();

            var settings = BuilderSettings.Get();
            _agent = new AgentEngine(settings);
            _agent.OnStateChanged += s =>
            {
                FlushUnstampedThinking();
                if (s == AgentState.Thinking || s == AgentState.CallingTool)
                    FinalizeContentStream();
                if (s == AgentState.Thinking)
                {
                    _responseStartedAt = EditorApplication.timeSinceStartup;
                    _awaitingContent = true;
                    _responseHistoryCount = _agent?.History?.Count ?? 0;
                }
                if (_turnTiming && !IsAgentRunning())
                    FreezeTurnTimer();
                _needsRepaint = true;
            };
            _agent.OnStreamToken += t =>
            {
                if (_ignoreStream) return;
                CloseOpenDirective();
                if (_contentStartedAt <= 0)
                    _contentStartedAt = EditorApplication.timeSinceStartup;
                _streamBuffer += t;
                _isStreaming = true;
                _needsRepaint = true;
            };
            _agent.OnThinkingToken += t =>
            {
                if (_ignoreStream) return;
                CloseOpenDirective();
                if (_thinkingStartedAt <= 0)
                    _thinkingStartedAt = EditorApplication.timeSinceStartup;
                _thinkingStreamBuffer += t;
                _isThinking = true;
                _needsRepaint = true;
            };
            _agent.OnThinking += t => { FinalizeThinkingStream(t); };
            _agent.OnComplete += full => { FinishStream(full); };
            _agent.OnError += e =>
            {
                CloseOpenActivity();
                AddBubble(BubbleType.Error, e);
                _isStreaming = false;
                _isThinking = false;
            };
            _agent.OnExecuted += r => { };
            _agent.OnToolCall += (name, args, result) => AcceptToolCall(name, args, result);
            _agent.OnDirective += text => AddDirectiveBubble(text);

            _sessions = new SessionManager();
            _settingsPanel = new SettingsPanel();
            _consolePanel = new ConsolePanel();
            _projectConfigPanel = new ProjectConfigPanel();
            _workflowMode = LoadWorkflowMode();
            SessionAssetChanges.Changed += OnSessionAssetsChanged;
            _sessions.RestoreLastSession(_agent.History, _bubbles);

            _hSplitLeft = new SplitView("UIPrefabBuilder_HSplitLeft", SplitDirection.Horizontal, 220f, 150f, 400f);
            _hSplitRight = new SplitView("UIPrefabBuilder_HSplitRight", SplitDirection.Horizontal, 320f, 260f, 640f);
            if (_hSplitRight.SplitPosition < 260f)
                _hSplitRight.SplitPosition = 320f;
            _vSplitLeft = new SplitView("UIPrefabBuilder_VSplitLeft", SplitDirection.Vertical, 200f, 80f);

            ConsoleLogger.OnLogAdded += OnLogAdded;

            EditorApplication.update += Tick;
            ConsoleLogger.LogVolatile("BuilderWindow opened");
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            SessionAssetChanges.Changed -= OnSessionAssetsChanged;
            ConsoleLogger.OnLogAdded -= OnLogAdded;
            PersistSession();
            _agent?.Dispose();
        }

        private void Tick()
        {
            if (_consoleRepaintPending && EditorApplication.timeSinceStartup - _lastConsoleRepaint >= 0.15)
            {
                _consoleRepaintPending = false;
                _lastConsoleRepaint = EditorApplication.timeSinceStartup;
                _needsRepaint = true;
            }
            if ((_isStreaming || _isThinking) && EditorApplication.timeSinceStartup - _lastSessionPersist >= 1.0)
                PersistSession();

            bool live = _turnTiming || _isThinking || _isStreaming
                || (_openTool != null && _openTool.TimingOpen)
                || (_openDirective != null && _openDirective.TimingOpen);
            if (!live && !_needsRepaint) return;
            float minInterval = _needsRepaint ? 0.05f : 0.1f;
            if (Time.realtimeSinceStartup - _lastRepaint < minInterval) return;
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

            // Right splitter handle (between center and right) — reverse resize from right edge
            var rightSplitterRect = new Rect(rightPanelRect.x - 4f, bodyRect.y, 4f, bodyRect.height);
            _hSplitRight.HandleReverseHorizontalSplitter(
                rightSplitterRect, bodyRect, _hSplitLeft.SplitPosition + 200f);

            DrawLeftPanel(leftPanelRect);
            DrawCenterPanel(centerPanelRect);
            DrawRightPanel(rightPanelRect);

            HandleKeyboard();
        }

        #region Header
        private bool IsAgentRunning()
        {
            var state = _agent?.State ?? AgentState.Idle;
            return state == AgentState.Thinking || state == AgentState.CallingTool
                || state == AgentState.WaitingForLLM || state == AgentState.BuildingContext
                || state == AgentState.Compiling || state == AgentState.Executing
                || state == AgentState.WaitingConfirmation;
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UIPrefabBuilder", _headerStyle, GUILayout.Width(120));
            GUILayout.FlexibleSpace();

            bool running = IsAgentRunning();
            GUI.enabled = !running;
            if (GUILayout.Button("New Chat", EditorStyles.toolbarButton, GUILayout.Width(65)))
            {
                NewChat();
                _endGuiPass = true;
            }
            if (GUILayout.Button(_showHistory ? "Close History" : "History", EditorStyles.toolbarButton, GUILayout.Width(85)))
                _showHistory = !_showHistory;
            GUI.enabled = true;
            if (GUILayout.Button("Export", EditorStyles.toolbarButton, GUILayout.Width(50))) ExportChat();

            EditorGUILayout.EndHorizontal();
            if (_endGuiPass)
            {
                _endGuiPass = false;
                _chatVisibleFirst = 0;
                _chatVisibleLast = 0;
                _chatShowThinking = false;
                _chatShowStreaming = false;
                GUIUtility.ExitGUI();
            }
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
            _centerWidth = rect.width;
            GUILayout.BeginArea(rect);
            try
            {
                if (_showHistory)
                {
                    DrawHistoryOverlay();
                }
                else
                {
                    DrawChatArea();
                    DrawChangedAssets();
                }

                DrawStatusBar();
                DrawInputArea();
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private void DrawChatArea()
        {
            float layoutWidth = Mathf.Max(120f, _centerWidth);
            bool layout = Event.current.type == EventType.Layout;
            if (layout)
                EnsureBubbleLayout(layoutWidth);

            _chatScroll = EditorGUILayout.BeginScrollView(_chatScroll, GUILayout.ExpandHeight(true));
            try
            {
                if (layout)
                    CaptureChatWindow(layoutWidth);

                int first = _chatVisibleFirst;
                int lastExclusive = _chatVisibleLast;
                int count = _bubbles.Count;
                bool windowReady = _bubbleOffsets != null
                    && _bubbleOffsets.Count == count + 1
                    && first >= 0
                    && lastExclusive >= first
                    && lastExclusive <= count;
                if (!windowReady)
                {
                    first = 0;
                    lastExclusive = 0;
                }

                if (first > 0)
                    GUILayout.Space(_bubbleOffsets[first]);

                for (int i = first; i < lastExclusive; i++)
                    DrawBubble(_bubbles[i], i);

                if (lastExclusive < _bubbles.Count)
                {
                    float renderedBottom = _bubbleOffsets[lastExclusive];
                    float totalHeight = _bubbleOffsets[_bubbleOffsets.Count - 1];
                    GUILayout.Space(Mathf.Max(0f, totalHeight - renderedBottom));
                }

                if (_chatShowThinking)
                {
                    DrawBubble(new ChatBubble
                    {
                        Type = BubbleType.Thinking,
                        Content = _thinkingStreamBuffer,
                        TimingOpen = true,
                        StartedAt = _thinkingStartedAt > 0 ? _thinkingStartedAt : EditorApplication.timeSinceStartup
                    });
                }

                if (_chatShowStreaming)
                {
                    DrawBubble(new ChatBubble
                    {
                        Type = BubbleType.AIStream,
                        Content = _streamBuffer,
                        TimingOpen = true,
                        StartedAt = _contentStartedAt > 0 ? _contentStartedAt : EditorApplication.timeSinceStartup
                    });
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void CaptureChatWindow(float layoutWidth)
        {
            EnsureBubbleLayout(layoutWidth);
            float viewportHeight = Mathf.Max(180f, position.height - 190f);
            float scrollY = _chatScroll.y;
            float total = _bubbleOffsets.Count == 0 ? 0f : _bubbleOffsets[_bubbleOffsets.Count - 1];
            if (float.IsInfinity(scrollY) || scrollY >= total)
                scrollY = Mathf.Max(0f, total - viewportHeight);
            float visibleTop = Mathf.Max(0f, scrollY - ChatVirtualizationBuffer);
            float visibleBottom = scrollY + viewportHeight + ChatVirtualizationBuffer;
            int first = FindBubbleAt(visibleTop);
            int lastExclusive = FindBubbleAfter(visibleBottom);
            if (lastExclusive < first)
                lastExclusive = first;
            _chatVisibleFirst = first;
            _chatVisibleLast = lastExclusive;
            _chatShowThinking = _isThinking && !string.IsNullOrEmpty(_thinkingStreamBuffer);
            _chatShowStreaming = _isStreaming && !string.IsNullOrEmpty(_streamBuffer);
        }

        private void EnsureBubbleLayout(float width)
        {
            if (_bubbleOffsets == null)
                _bubbleOffsets = new List<float>();
            if (_bubbleHeights == null)
                _bubbleHeights = new List<float>();
            if (!_bubbleLayoutDirty
                && _bubbleLayoutCount == _bubbles.Count
                && _bubbleOffsets.Count == _bubbles.Count + 1
                && _bubbleHeights.Count == _bubbles.Count
                && Mathf.Abs(_bubbleLayoutWidth - width) < 1f)
                return;

            _bubbleOffsets.Clear();
            _bubbleHeights.Clear();
            _bubbleOffsets.Add(0f);
            float offset = 0f;
            for (int i = 0; i < _bubbles.Count; i++)
            {
                var bubble = _bubbles[i];
                float height = bubble?.RenderCache != null && bubble.RenderCache.Matches(
                    bubble, width, position.width, EditorGUIUtility.isProSkin, ElapsedText(bubble) != null)
                    ? bubble.RenderCache.LayoutHeight
                    : EstimateBubbleHeight(bubble, width);
                _bubbleHeights.Add(height);
                offset += height;
                _bubbleOffsets.Add(offset);
            }

            _bubbleLayoutWidth = width;
            _bubbleLayoutCount = _bubbles.Count;
            _bubbleLayoutDirty = false;
        }

        private int FindBubbleAt(float offset)
        {
            int count = _bubbles.Count;
            if (_bubbleOffsets == null || _bubbleOffsets.Count != count + 1)
                return 0;
            int low = 0;
            int high = count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_bubbleOffsets[middle + 1] <= offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private int FindBubbleAfter(float offset)
        {
            int count = _bubbles.Count;
            if (_bubbleOffsets == null || _bubbleOffsets.Count != count + 1)
                return count;
            int low = 0;
            int high = count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_bubbleOffsets[middle] < offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return Mathf.Clamp(low, 0, _bubbles.Count);
        }

        private static float EstimateBubbleHeight(ChatBubble bubble, float width)
        {
            if (bubble == null) return 0f;
            int chars = (bubble.Content?.Length ?? 0) + (bubble.Note?.Length ?? 0);
            float textWidth = Mathf.Max(120f, width * (bubble.Type == BubbleType.User ? 0.55f : 0.78f));
            float lines = Mathf.Max(1f, chars / Mathf.Max(24f, textWidth / 7f));
            float baseHeight = 42f + lines * 18f;
            if (bubble.Type == BubbleType.Code)
                baseHeight = Mathf.Min(520f, Mathf.Max(86f, baseHeight));
            else if (bubble.Type == BubbleType.Directive)
                baseHeight += 26f;
            return baseHeight + 4f;
        }

        private void UpdateBubbleHeight(int index, ChatBubble bubble)
        {
            if (index < 0 || index >= _bubbleHeights.Count || bubble?.RenderCache == null) return;
            float height = bubble.RenderCache.LayoutHeight;
            if (Mathf.Abs(_bubbleHeights[index] - height) < 0.5f) return;
            _bubbleLayoutDirty = true;
        }

        private void DrawHistoryOverlay()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Chat History", BuilderStyles.SectionLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Open Folder", "打开 Sessions 目录。日志在旁边的 Logs 目录，附件在 Attachments 目录，文件名与会话相同。"),
                    EditorStyles.miniButton, GUILayout.Width(88)))
                SessionManager.OpenDirectory(SessionManager.SessionDir);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll);
            var sessions = _sessions.ListSessions();
            foreach (var s in sessions)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.BeginVertical();
                var title = string.IsNullOrEmpty(s.Title) ? SessionManager.PlaceholderTitle : s.Title;
                GUILayout.Label(title, EditorStyles.boldLabel);
                GUILayout.Label(s.UpdatedAt.ToString("g"), EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
                if (GUILayout.Button("Load", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    _sessions.LoadSession(s.SessionId, _agent.History, _bubbles);
                    InvalidateBubbleLayouts();
                    _showHistory = false;
                }
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.RemoveIconWidth)))
                    _sessions.DeleteSession(s.SessionId);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawBubble(ChatBubble b, int bubbleIndex = -1)
        {
            if (b == null) return;
            var cache = GetRenderCache(b);
            UpdateBubbleHeight(bubbleIndex, b);
            if (b.Type == BubbleType.Directive)
            {
                DrawDirective(b, cache);
                return;
            }
            if (b.Type == BubbleType.Code)
            {
                DrawCodeBubble(b, cache);
                return;
            }

            bool isUser = b.Type == BubbleType.User;
            GUIStyle style;
            switch (b.Type)
            {
                case BubbleType.User: style = _userBubble; break;
                case BubbleType.AI: case BubbleType.AIStream: style = _aiBubble; break;
                case BubbleType.Thinking: style = _thinkBubble; break;
                case BubbleType.Result: style = _resultBubble; break;
                case BubbleType.Error: style = _errorBubble; break;
                case BubbleType.ToolCall: style = _toolCallBubble; break;
                default: style = _aiBubble; break;
            }

            float maxBubbleWidth = b.Type == BubbleType.ToolCall
                ? Mathf.Clamp(_centerWidth - 28f, 260f, 560f)
                : position.width * 0.55f;

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
                GUILayout.Label(ThinkingTitle(b.TimingOpen), EditorStyles.miniBoldLabel);
                DrawWrappedText(b.Content, EditorStyles.wordWrappedMiniLabel, maxBubbleWidth - 24f, cache.ContentHeight);
            }
            else if (b.Type == BubbleType.ToolCall)
            {
                DrawToolCall(cache, maxBubbleWidth - 24f);
            }
            else if (b.Type == BubbleType.AI || b.Type == BubbleType.AIStream)
            {
                DrawMarkdownContent(cache, maxBubbleWidth - 16f);
            }
            else
            {
                DrawWrappedText(b.Content, EditorStyles.wordWrappedLabel, maxBubbleWidth - 24f, cache.ContentHeight);
            }

            if (b.AttachedFilePaths != null && b.AttachedFilePaths.Count > 0)
            {
                GUILayout.Space(2);
                foreach (var fp in b.AttachedFilePaths)
                    GUILayout.Label("\ud83d\uddbc " + Path.GetFileName(fp), EditorStyles.miniLabel);
            }

            DrawElapsedFooter(b);
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

        private void DrawToolCall(ChatBubbleRenderCache cache, float width)
        {
            var layoutWidth = Mathf.Max(40f, width);
            GUILayout.Label(cache.ToolHeader, _toolCallHeader, GUILayout.Width(layoutWidth), GUILayout.Height(cache.ToolHeaderHeight));
            if (!string.IsNullOrEmpty(cache.ToolFlow))
            {
                DrawWrappedText(cache.ToolFlow, _toolCallLabel, layoutWidth, cache.ToolFlowHeight);
            }
        }

        private void DrawCodeBubble(ChatBubble b, ChatBubbleRenderCache cache)
        {
            float width = cache.BubbleWidth;
            var viewWidth = cache.ContentWidth;
            var code = cache.Code;
            float codeHeight = cache.CodeHeight;
            const float maxView = 440f;
            var viewHeight = Mathf.Min(codeHeight, maxView);
            float noteHeight = cache.NoteHeight;
            float blockHeight = cache.CodeBlockHeight;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal(_codeBubble, GUILayout.MaxWidth(width));

            var bar = GUILayoutUtility.GetRect(3f, blockHeight, GUILayout.Width(3f), GUILayout.Height(blockHeight));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(bar, BuilderStyles.CodeAccent);
            GUILayout.Space(8f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(b.Title) ? "\u2699 code" : b.Title, _codeTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(48)))
                EditorGUIUtility.systemCopyBuffer = b.Content ?? "";
            EditorGUILayout.EndHorizontal();

            if (codeHeight <= maxView)
            {
                EditorGUILayout.SelectableLabel(code, _codeBody, GUILayout.Width(viewWidth), GUILayout.Height(codeHeight));
            }
            else
            {
                b.CodeScroll = EditorGUILayout.BeginScrollView(b.CodeScroll, GUILayout.Height(maxView));
                EditorGUILayout.SelectableLabel(code, _codeBody, GUILayout.Width(viewWidth), GUILayout.Height(codeHeight));
                EditorGUILayout.EndScrollView();
            }

            if (!string.IsNullOrEmpty(cache.FlowedNote))
            {
                GUILayout.Space(2f);
                GUILayout.Label(cache.FlowedNote, _codeNote, GUILayout.Width(viewWidth), GUILayout.Height(noteHeight));
            }

            DrawElapsedFooter(b);
            GUILayout.Space(4f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private void DrawDirective(ChatBubble b, ChatBubbleRenderCache cache)
        {
            var body = cache.DirectiveBody;
            var textWidth = cache.ContentWidth;
            var bodyHeight = cache.ContentHeight;
            float elapsedExtra = ElapsedText(b) != null ? 18f : 0f;

            EditorGUILayout.BeginVertical(_directiveBubble);
            EditorGUILayout.BeginHorizontal();
            var accentHeight = 16f + bodyHeight + elapsedExtra;
            var accent = GUILayoutUtility.GetRect(3f, accentHeight, GUILayout.Width(3f), GUILayout.Height(accentHeight));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(accent, BuilderStyles.DirectiveAccent);
            GUILayout.Space(8f);
            EditorGUILayout.BeginVertical();
            GUILayout.Label(cache.DirectiveHeading, _directiveTitle, GUILayout.Width(textWidth), GUILayout.Height(cache.DirectiveHeadingHeight));
            if (!string.IsNullOrEmpty(body))
                GUILayout.Label(body, _directiveBody, GUILayout.Width(textWidth), GUILayout.Height(bodyHeight));
            DrawElapsedFooter(b);
            GUILayout.Space(6f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (b.AttachedFilePaths != null)
            {
                foreach (var fp in b.AttachedFilePaths)
                {
                    if (string.IsNullOrEmpty(fp)) continue;
                    GUILayout.Label("附图  " + Path.GetFileName(fp), _directiveBody);
                }
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private static string DirectiveHeading(string content)
        {
            var trimmed = (content ?? "").TrimStart();
            if (trimmed.StartsWith("[DesignImageMeta]", StringComparison.Ordinal))
                return "AGENT · 设计稿参数";
            if (trimmed.StartsWith("Compare the current UI", StringComparison.Ordinal)
                || trimmed.StartsWith("Analyze this UI screenshot", StringComparison.Ordinal)
                || trimmed.StartsWith("Please analyze the provided screenshot", StringComparison.Ordinal)
                || trimmed.StartsWith("Cropped region (", StringComparison.Ordinal))
                return "AGENT · 看图指令";
            if (trimmed.StartsWith("VISUAL VERIFICATION", StringComparison.Ordinal)
                || trimmed.StartsWith("Your own verdict", StringComparison.Ordinal)
                || trimmed.StartsWith("Stop building:", StringComparison.Ordinal))
                return "AGENT · 校验指令";
            if (trimmed.StartsWith("The user confirmed.", StringComparison.Ordinal)
                || trimmed.StartsWith("The user refused editing", StringComparison.Ordinal))
                return "AGENT · 确认指令";
            if (trimmed.StartsWith("You are making many individual search calls.", StringComparison.Ordinal)
                || trimmed.StartsWith("You have been searching", StringComparison.Ordinal)
                || trimmed.StartsWith("You have spent many steps searching.", StringComparison.Ordinal)
                || trimmed.StartsWith("You have enough information to start building.", StringComparison.Ordinal))
                return "AGENT · 搜索指令";
            return "AGENT · 流程指令";
        }

        private static string FormatDirectiveBody(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            var trimmed = content.Trim();
            const string marker = "[DesignImageMeta]";
            if (!trimmed.StartsWith(marker, StringComparison.Ordinal))
                return trimmed;
            var rest = trimmed.Substring(marker.Length).Trim();
            return string.IsNullOrEmpty(rest) ? trimmed : rest;
        }

        private void DrawWrappedText(string content, GUIStyle style, float width, float cachedHeight = -1f)
        {
            if (string.IsNullOrEmpty(content)) return;
            var layoutWidth = Mathf.Max(40f, width);
            var height = cachedHeight > 0f ? cachedHeight : Mathf.Max(18f, style.CalcHeight(new GUIContent(content), layoutWidth));
            GUILayout.Label(content, style, GUILayout.Width(layoutWidth), GUILayout.Height(height));
        }

        private void DrawMarkdownContent(ChatBubbleRenderCache cache, float contentWidth)
        {
            if (cache.MarkdownBlocks == null || cache.MarkdownBlocks.Count == 0)
                return;

            for (var i = 0; i < cache.MarkdownBlocks.Count; i++)
            {
                if (i > 0)
                    GUILayout.Space(4f);

                var block = cache.MarkdownBlocks[i];
                if (block.Kind == MarkdownBlockKind.Code)
                    DrawFenceBlock(block.Text, block.Language, contentWidth, block.Height);
                else
                    GUILayout.Label(block.Text, _markdownBody, GUILayout.Width(contentWidth), GUILayout.Height(block.Height));
            }
        }

        private void DrawFenceBlock(string code, string language, float contentWidth, float cachedHeight = -1f)
        {
            var shown = string.IsNullOrEmpty(code) ? " " : code;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(language) ? "code" : language, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(48)))
                EditorGUIUtility.systemCopyBuffer = code ?? "";
            EditorGUILayout.EndHorizontal();

            var width = Mathf.Max(120f, contentWidth - 28f);
            var height = cachedHeight > 0f ? cachedHeight : Mathf.Max(20f, _codeBlockStyle.CalcHeight(new GUIContent(shown), width));
            EditorGUILayout.SelectableLabel(shown, _codeBlockStyle, GUILayout.MinHeight(height));
            EditorGUILayout.EndVertical();
        }

        private ChatBubbleRenderCache GetRenderCache(ChatBubble bubble)
        {
            float sourceWidth = Mathf.Max(120f, _centerWidth);
            float windowWidth = Mathf.Max(120f, position.width);
            bool proSkin = EditorGUIUtility.isProSkin;
            if (bubble.RenderCache != null && bubble.RenderCache.Matches(bubble, sourceWidth, windowWidth, proSkin, ElapsedText(bubble) != null))
                return bubble.RenderCache;

            bool hasElapsed = ElapsedText(bubble) != null;
            var cache = new ChatBubbleRenderCache
            {
                Type = bubble.Type,
                Content = bubble.Content,
                Title = bubble.Title,
                Note = bubble.Note,
                SourceWidth = sourceWidth,
                WindowWidth = windowWidth,
                ProSkin = proSkin,
                HasElapsed = hasElapsed
            };

            if (bubble.Type == BubbleType.Code)
            {
                cache.BubbleWidth = Mathf.Max(280f, _centerWidth - 20f);
                cache.ContentWidth = Mathf.Max(160f, cache.BubbleWidth - 56f);
                cache.Code = string.IsNullOrEmpty(bubble.Content) ? " " : bubble.Content;
                int lines = 1;
                for (int i = 0; i < cache.Code.Length; i++)
                    if (cache.Code[i] == '\n') lines++;
                float lineBased = lines * (Mathf.Max(12, _codeBody.fontSize) + 3) + 8f;
                cache.CodeHeight = Mathf.Max(lineBased, _codeBody.CalcHeight(new GUIContent(cache.Code), cache.ContentWidth));
                if (!string.IsNullOrEmpty(bubble.Note))
                {
                    cache.FlowedNote = ToolCallPresenter.Flow(bubble.Note, Mathf.Max(8f, cache.ContentWidth - 4f),
                        text => _codeNote.CalcSize(new GUIContent(text)).x);
                    cache.NoteHeight = Mathf.Max(16f, _codeNote.CalcHeight(new GUIContent(cache.FlowedNote), cache.ContentWidth));
                }
                float viewHeight = Mathf.Min(cache.CodeHeight, 440f);
                cache.CodeBlockHeight = 22f + viewHeight + (cache.NoteHeight > 0f ? cache.NoteHeight + 8f : 0f)
                    + (hasElapsed ? 16f : 0f) + 6f;
                cache.LayoutHeight = cache.CodeBlockHeight + 12f;
            }
            else if (bubble.Type == BubbleType.Directive)
            {
                cache.ContentWidth = Mathf.Max(140f, _centerWidth - 64f);
                cache.DirectiveBody = FormatDirectiveBody(bubble.Content);
                cache.DirectiveHeading = DirectiveHeading(bubble.Content);
                cache.DirectiveHeadingHeight = Mathf.Max(16f, _directiveTitle.CalcHeight(new GUIContent(cache.DirectiveHeading), cache.ContentWidth));
                cache.ContentHeight = string.IsNullOrEmpty(cache.DirectiveBody)
                    ? 0f
                    : Mathf.Max(16f, _directiveBody.CalcHeight(new GUIContent(cache.DirectiveBody), cache.ContentWidth));
                int attachedCount = bubble.AttachedFilePaths?.Count ?? 0;
                cache.LayoutHeight = cache.DirectiveHeadingHeight + cache.ContentHeight + 40f + attachedCount * 18f
                    + (hasElapsed ? 18f : 0f);
            }
            else
            {
                cache.BubbleWidth = bubble.Type == BubbleType.ToolCall
                    ? Mathf.Clamp(_centerWidth - 28f, 260f, 560f)
                    : position.width * 0.55f;
                cache.ContentWidth = bubble.Type == BubbleType.AI || bubble.Type == BubbleType.AIStream
                    ? cache.BubbleWidth - 16f
                    : cache.BubbleWidth - 24f;

                if (bubble.Type == BubbleType.ToolCall)
                {
                    var text = bubble.Content ?? "";
                    int split = text.IndexOf('\n');
                    cache.ToolHeader = split < 0 ? text : text.Substring(0, split);
                    var details = split < 0 ? "" : text.Substring(split + 1);
                    cache.ToolHeaderHeight = Mathf.Max(18f, _toolCallHeader.CalcHeight(new GUIContent(cache.ToolHeader), cache.ContentWidth));
                    if (!string.IsNullOrEmpty(details))
                    {
                        cache.ToolFlow = ToolCallPresenter.Flow(details, Mathf.Max(8f, cache.ContentWidth - 4f),
                            value => _toolCallLabel.CalcSize(new GUIContent(value)).x);
                        cache.ToolFlowHeight = Mathf.Max(18f, _toolCallLabel.CalcHeight(new GUIContent(cache.ToolFlow), cache.ContentWidth));
                    }
                    cache.ContentHeight = cache.ToolHeaderHeight + cache.ToolFlowHeight;
                }
                else if (bubble.Type == BubbleType.AI || bubble.Type == BubbleType.AIStream)
                {
                    cache.MarkdownBlocks = BuildMarkdownLayout(bubble.Content, cache.ContentWidth);
                    for (int i = 0; i < cache.MarkdownBlocks.Count; i++)
                    {
                        cache.ContentHeight += cache.MarkdownBlocks[i].Height;
                        if (i > 0) cache.ContentHeight += 4f;
                    }
                }
                else
                {
                    var style = bubble.Type == BubbleType.Thinking ? EditorStyles.wordWrappedMiniLabel : EditorStyles.wordWrappedLabel;
                    cache.ContentHeight = string.IsNullOrEmpty(bubble.Content)
                        ? 0f
                        : Mathf.Max(18f, style.CalcHeight(new GUIContent(bubble.Content), cache.ContentWidth));
                    if (bubble.Type == BubbleType.Thinking)
                        cache.ContentHeight += 18f;
                }

                int attachments = bubble.AttachedFilePaths?.Count ?? 0;
                cache.LayoutHeight = cache.ContentHeight + 22f + attachments * 18f + (hasElapsed ? 16f : 0f);
            }

            bubble.RenderCache = cache;
            return cache;
        }

        private List<MarkdownRenderBlock> BuildMarkdownLayout(string content, float width)
        {
            try
            {
                var parsed = MarkdownView.Parse(content, MarkdownView.Theme.ForEditorSkin(EditorGUIUtility.isProSkin));
                var blocks = new List<MarkdownRenderBlock>(parsed.Count);
                for (int i = 0; i < parsed.Count; i++)
                {
                    var block = parsed[i];
                    float blockWidth = block.Kind == MarkdownBlockKind.Code ? Mathf.Max(120f, width - 28f) : width;
                    var style = block.Kind == MarkdownBlockKind.Code ? _codeBlockStyle : _markdownBody;
                    blocks.Add(new MarkdownRenderBlock
                    {
                        Kind = block.Kind,
                        Text = block.Text,
                        Language = block.Language,
                        Height = Mathf.Max(20f, style.CalcHeight(new GUIContent(string.IsNullOrEmpty(block.Text) ? " " : block.Text), blockWidth))
                    });
                }
                return blocks;
            }
            catch (Exception e)
            {
                if (!_markdownFailed)
                {
                    _markdownFailed = true;
                    Debug.LogException(e);
                }
                return new List<MarkdownRenderBlock>
                {
                    new MarkdownRenderBlock
                    {
                        Kind = MarkdownBlockKind.Text,
                        Text = content ?? "",
                        Height = Mathf.Max(18f, _markdownBody.CalcHeight(new GUIContent(content ?? ""), width))
                    }
                };
            }
        }

        private void InvalidateBubbleLayouts()
        {
            _bubbleLayoutDirty = true;
            _bubbleLayoutCount = -1;
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawAttachmentArea();

            // Input text area
            var resumeBlock = _agent?.History?.ResumeBlockedReason;
            if (!string.IsNullOrEmpty(resumeBlock))
                EditorGUILayout.HelpBox(resumeBlock, MessageType.Warning);

            _inputScroll = EditorGUILayout.BeginScrollView(_inputScroll, GUILayout.Height(55));
            _input = EditorGUILayout.TextArea(_input, _inputStyle, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            // Bottom row: mode + Send/Stop + Confirm/Undo
            EditorGUILayout.BeginHorizontal();
            var state = _agent?.State ?? AgentState.Idle;
            bool isRunning = IsAgentRunning();
            DrawWorkflowMode(isRunning);
            GUILayout.FlexibleSpace();
            if (isRunning)
            {
                if (GUILayout.Button("Stop", EditorStyles.miniButton, GUILayout.Width(55))) StopTask();
            }
            else
            {
                bool hasContent = !string.IsNullOrWhiteSpace(_input) || _attachedFiles.Count > 0;
                GUI.enabled = hasContent;
                if (GUILayout.Button("Send", EditorStyles.miniButton, GUILayout.Width(55))) Send();
                GUI.enabled = true;
            }

            if (state == AgentState.WaitingConfirmation)
            {
                if (GUILayout.Button("Confirm", EditorStyles.miniButton, GUILayout.Width(60))) _agent?.Confirm();
                if (GUILayout.Button("Undo", EditorStyles.miniButton, GUILayout.Width(45))) _agent?.Rollback();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawWorkflowMode(bool isRunning)
        {
            EditorGUI.BeginDisabledGroup(isRunning);
            GUILayout.Label("Mode", EditorStyles.miniLabel, GUILayout.Width(36));
            var next = (PrefabWorkflowMode)GUILayout.Toolbar(
                (int)_workflowMode,
                new[] { "Auto", "New", "Modify" },
                EditorStyles.miniButton,
                GUILayout.Width(168));
            if (next != _workflowMode)
            {
                _workflowMode = next;
                EditorPrefs.SetInt(WorkflowModePref, (int)next);
            }
            EditorGUI.EndDisabledGroup();
        }

        private static PrefabWorkflowMode LoadWorkflowMode()
        {
            var stored = EditorPrefs.GetInt(WorkflowModePref, (int)PrefabWorkflowMode.Auto);
            if (!Enum.IsDefined(typeof(PrefabWorkflowMode), stored)) return PrefabWorkflowMode.Auto;
            return (PrefabWorkflowMode)stored;
        }

        private void DrawChangedAssets()
        {
            var items = SessionAssetChanges.Items;
            if (items.Count == 0) return;

            if (_changedAssetsDirty)
            {
                _orderedChangedAssets.Clear();
                _orderedChangedAssets.AddRange(items);
                _orderedChangedAssets.Sort((a, b) => b.Ticks.CompareTo(a.Ticks));
                _changedAssetsDirty = false;
            }
            var ordered = _orderedChangedAssets;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var title = ordered.Count == 1 ? "1 File" : ordered.Count + " Files";
            _changedAssetsExpanded = EditorGUILayout.Foldout(_changedAssetsExpanded, title, true);
            if (_changedAssetsExpanded)
            {
                var height = Mathf.Min(96f, ordered.Count * 20f + 4f);
                _changedAssetsScroll = EditorGUILayout.BeginScrollView(_changedAssetsScroll, GUILayout.Height(height));
                foreach (var item in ordered)
                {
                    var kind = item.Kind == SessionAssetChangeKind.Added ? "Added" : "Modified";
                    var row = new GUIContent(Path.GetFileName(item.Path) + "    " + kind, item.Path);
                    if (GUILayout.Button(row, EditorStyles.label))
                        PingProjectAsset(item.Path);
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void OnSessionAssetsChanged()
        {
            _changedAssetsDirty = true;
            _needsRepaint = true;
        }

        private void OnLogAdded(LogEntry _)
        {
            _consoleRepaintPending = true;
        }

        private static void PingProjectAsset(string assetPath)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            EditorUtility.FocusProjectWindow();
        }

        private static readonly HashSet<string> AllowedImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".webp", ".bmp"
        };
        private const long MaxFileBytes = 10 * 1024 * 1024; // 10 MB

        private void DrawAttachmentArea()
        {
            var supportsVision = BuilderSettings.Get().SupportsVision;

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!supportsVision || _visionSendPending);
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.AddIconWidth)))
            {
                var path = EditorUtility.OpenFilePanelWithFilters(
                    "Select Design Mockup", "",
                    new[] { "Image files", "png,jpg,jpeg,webp,bmp", "All files", "*" });
                if (!string.IsNullOrEmpty(path))
                    TryAddAttachment(path);
            }
            EditorGUI.EndDisabledGroup();

            if (!supportsVision)
                GUILayout.Label("Design mockups require Supports Vision (Settings).", EditorStyles.miniLabel);
            else if (_attachedFiles.Count == 0)
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
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.RemoveIconWidth)))
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

            bool thinkingLabel = state == AgentState.Thinking || state == AgentState.WaitingForLLM;
            if (thinkingLabel)
                label = "Thinking" + ThinkingDots();

            EditorGUILayout.BeginHorizontal();
            BuilderStyles.ColoredLabel("\u25cf", color, EditorStyles.label, GUILayout.Width(12));
            if (thinkingLabel)
                GUILayout.Label(label, _statusLabel, GUILayout.Width(96));
            else
                GUILayout.Label(label, _statusLabel);
            if (_turnVisible)
            {
                GUILayout.Space(6);
                GUILayout.Label(FormatDuration(TurnSeconds()), _statusLabel);
            }
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
            _projectConfigPanel.Draw(rect, Repaint);
        }
        #endregion

        #region State Info
        private (string, Color) GetStateInfo(AgentState s) => s switch
        {
            AgentState.Idle => ("Idle", BuilderStyles.StateIdle),
            AgentState.Thinking => ("Thinking...", BuilderStyles.StateThinking),
            AgentState.CallingTool => ("Calling tool...", BuilderStyles.StateTool),
            AgentState.WaitingConfirmation => ("Confirm?", BuilderStyles.StateConfirm),
            AgentState.Completed => ("Done", BuilderStyles.StateDone),
            AgentState.Error => ("Error", BuilderStyles.StateError),
            AgentState.BuildingContext => ("Context...", BuilderStyles.StateContext),
            AgentState.WaitingForLLM => ("Thinking...", BuilderStyles.StateThinking),
            AgentState.ExtractingCode => ("Extracting...", BuilderStyles.StateExtract),
            AgentState.Compiling => ("Compiling...", BuilderStyles.StateCompile),
            AgentState.Executing => ("Executing...", BuilderStyles.StateExecute),
            AgentState.ObservingResult => ("Observing...", BuilderStyles.StateDone),
            _ => ("Unknown", BuilderStyles.StateIdle)
        };
        #endregion

        #region Actions
        private void Send()
        {
            var text = _input?.Trim() ?? "";
            bool hasText = !string.IsNullOrWhiteSpace(text);
            bool hasFiles = _attachedFiles.Count > 0;

            if (!hasText && !hasFiles) return;
            if (_visionSendPending) return;

            var settings = BuilderSettings.Get();

            if (hasFiles && !settings.SupportsVision)
            {
                EditorUtility.DisplayDialog("Vision Required",
                    "Design mockup images require a vision-capable model.\n\n" +
                    "Enable Supports Vision in Settings (left panel). The tool will probe the API with a test image.\n\n" +
                    "Without vision you can still build UI using text-only prompts.",
                    "OK");
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

            var workflowMode = _workflowMode;
            var selected = Selection.activeGameObject;
            FlushUnstampedThinking();
            BeginTurnTimer();

            if (hasFiles)
            {
                var filesCopy = new List<string>(_attachedFiles);
                var userText = text;
                var bubbleText = displayText;

                if (VisionCapabilityProbe.IsCacheValid(settings))
                {
                    BeginImageTask(userText, filesCopy, bubbleText, workflowMode, selected);
                    return;
                }

                _visionSendPending = true;
                AddBubble(BubbleType.AI, "Checking vision support before starting…");
                _needsRepaint = true;

                VisionCapabilityProbe.ProbeAsync(settings, result =>
                {
                    _visionSendPending = false;
                    if (result != null && result.Supported)
                    {
                        settings.SupportsVision = true;
                        settings.RestoreAutoDisabledVisualVerification();
                        settings.Save();
                        BeginImageTask(userText, filesCopy, bubbleText, workflowMode, selected);
                    }
                    else
                    {
                        settings.SupportsVision = false;
                        settings.AutoDisableVisualVerification();
                        settings.Save();
                        FreezeTurnTimer();
                        var reason = result?.Reason ?? "Unknown failure.";
                        AddBubble(BubbleType.Error,
                            "Task aborted: vision probe failed. Supports Vision has been turned off.\n\n" + reason);
                        EditorUtility.DisplayDialog(
                            "Vision Probe Failed",
                            "This image task was cancelled because the model/endpoint failed the vision probe.\n\n" + reason,
                            "OK");
                        _needsRepaint = true;
                    }
                });
                return;
            }

            ResetActivityClocks();
            AddBubble(BubbleType.User, displayText, null);
            _ignoreStream = false;
            _agent?.StartTask(text, workflowMode, selected);
            RequestAutoTitle(hasText ? text : displayText);
            PersistSession();
            _input = "";
            _attachedFiles.Clear();
            GUI.FocusControl(null);
        }

        private void BeginImageTask(string userText, List<string> files, string bubbleText, PrefabWorkflowMode mode, GameObject selected)
        {
            ResetActivityClocks();
            AddBubble(BubbleType.User, bubbleText, new List<string>(files));
            _ignoreStream = false;
            _agent?.StartTask(userText, files, mode, selected);
            RequestAutoTitle(string.IsNullOrWhiteSpace(userText) ? bubbleText : userText);
            PersistSession();
            _input = "";
            _attachedFiles.Clear();
            GUI.FocusControl(null);
            _needsRepaint = true;
        }

        private void FinalizeThinkingStream(string fullThinking)
        {
            CloseOpenDirective();
            string text = null;
            if (!string.IsNullOrWhiteSpace(_thinkingStreamBuffer))
                text = _thinkingStreamBuffer;
            else if (!string.IsNullOrWhiteSpace(fullThinking))
                text = fullThinking;

            if (text != null)
            {
                if (_thinkingStartedAt <= 0)
                    _thinkingStartedAt = _responseStartedAt > 0 ? _responseStartedAt : EditorApplication.timeSinceStartup;
                var duration = Math.Max(0, EditorApplication.timeSinceStartup - _thinkingStartedAt);
                var bubble = AddBubble(BubbleType.Thinking, text);
                if (bubble != null)
                    bubble.DurationSeconds = duration;
                _unstampedThinking = (float)duration;
                _thinkingStampIndex = _agent?.History?.Count ?? 0;
                _thinkingEndedAt = EditorApplication.timeSinceStartup;
            }

            _thinkingStreamBuffer = "";
            _isThinking = false;
            _thinkingStartedAt = 0;
        }

        private void FinalizeContentStream()
        {
            if (!string.IsNullOrWhiteSpace(_streamBuffer))
            {
                var duration = ContentDuration();
                StampAssistantContent((float)duration);
                var bubble = AddBubble(BubbleType.AI, _streamBuffer);
                if (bubble != null)
                    bubble.DurationSeconds = duration;
                _awaitingContent = false;
            }
            _streamBuffer = "";
            _isStreaming = false;
            _contentStartedAt = 0;
        }

        private void FinishStream(string full)
        {
            CloseOpenDirective();
            FlushUnstampedThinking();
            _isStreaming = false;
            if (!string.IsNullOrWhiteSpace(full))
            {
                var timed = _awaitingContent || _contentStartedAt > 0;
                var duration = timed ? ContentDuration() : -1;
                if (timed)
                    StampAssistantContent((float)duration);
                var bubble = AddBubble(BubbleType.AI, full);
                if (bubble != null && timed)
                    bubble.DurationSeconds = duration;
            }
            _streamBuffer = "";
            _contentStartedAt = 0;
            _awaitingContent = false;
            PersistSession();
        }

        private void StopTask()
        {
            _ignoreStream = true;
            var partial = _streamBuffer;
            CloseOpenActivity();
            _agent?.Cancel();
            FinalizeThinkingStream(null);
            FinalizeContentStream();
            _isStreaming = false;
            _isThinking = false;
            if (!string.IsNullOrWhiteSpace(partial))
            {
                var message = _agent?.History?.AddAssistant(partial);
                if (message != null && message.ContentSeconds < 0)
                {
                    for (int i = _bubbles.Count - 1; i >= 0; i--)
                    {
                        if (_bubbles[i].Type != BubbleType.AI || _bubbles[i].DurationSeconds < 0) continue;
                        message.ContentSeconds = (float)_bubbles[i].DurationSeconds;
                        break;
                    }
                }
            }
            PersistSession();
        }

        private void PersistSession()
        {
            if (_sessions == null || _agent?.History == null) return;
            _lastSessionPersist = EditorApplication.timeSinceStartup;
            _sessions.SaveCurrentSession(_agent.History, SnapshotBubbles());
        }

        private List<ChatBubble> SnapshotBubbles()
        {
            if (string.IsNullOrWhiteSpace(_thinkingStreamBuffer) && string.IsNullOrWhiteSpace(_streamBuffer))
                return _bubbles;

            var copy = new List<ChatBubble>(_bubbles);
            if (!string.IsNullOrWhiteSpace(_thinkingStreamBuffer))
                copy.Add(new ChatBubble { Type = BubbleType.Thinking, Content = _thinkingStreamBuffer });
            if (!string.IsNullOrWhiteSpace(_streamBuffer))
                copy.Add(new ChatBubble { Type = BubbleType.AIStream, Content = _streamBuffer });
            return copy;
        }

        private void RequestAutoTitle(string userText)
        {
            if (_agent == null || _sessions == null) return;
            var sessionId = _sessions.CurrentSessionId;
            if (string.IsNullOrEmpty(sessionId) || _titleRequestedSessionId == sessionId) return;
            if (!_sessions.NeedsAutoTitle()) return;

            var text = string.IsNullOrWhiteSpace(userText) ? "根据附件实现界面" : userText.Trim();
            if (text.Length > 2000) text = text.Substring(0, 2000);
            PersistSession();
            _titleRequestedSessionId = sessionId;

            _agent.RequestPlainCompletion(
                SessionManager.TitleSummarySystemPrompt,
                "用户请求：\n" + text,
                title =>
                {
                    if (this == null) return;
                    _sessions.TryApplySummaryTitle(sessionId, title, out _);
                    Repaint();
                },
                err => Debug.LogWarning("[UIPrefabBuilder] Title summary failed: " + err));
        }

        private void NewChat()
        {
            _titleRequestedSessionId = null;
            _sessions.SaveCurrentSession(_agent.History, SnapshotBubbles());
            _bubbles.Clear();
            InvalidateBubbleLayouts();
            SessionAssetChanges.Clear();
            _agent.History.Clear();
            _agent.Refresh();
            _sessions.NewSession();
            DesignImageContext.Clear();
            ResetTurnTimer();
            ResetActivityClocks();
            ConsoleLogger.LogVolatile("New chat session created");
        }

        private void ClearChat()
        {
            _bubbles.Clear();
            InvalidateBubbleLayouts();
            _agent?.History?.Clear();
            _agent?.Refresh();
            DesignImageContext.Clear();
            ResetTurnTimer();
            ResetActivityClocks();
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
        private ChatBubble AddBubble(BubbleType type, string content, List<string> attachedFiles = null, string title = null, string note = null, bool timingOpen = false)
        {
            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(title) && (attachedFiles == null || attachedFiles.Count == 0)) return null;
            var bubble = new ChatBubble
            {
                Type = type,
                Content = content ?? "",
                AttachedFilePaths = attachedFiles,
                Title = title,
                Note = note,
                TimingOpen = timingOpen,
                StartedAt = timingOpen ? EditorApplication.timeSinceStartup : 0,
                DurationSeconds = -1
            };
            _bubbles.Add(bubble);
            InvalidateBubbleLayouts();
            _chatScroll.y = float.MaxValue;
            _needsRepaint = true;
            PersistSession();
            return bubble;
        }

        private void AcceptToolCall(string name, string args, string result)
        {
            if (result == null)
            {
                CloseOpenDirective();
                if (_openTool != null && _openTool.TimingOpen)
                {
                    _openTool.DurationSeconds = Math.Max(0, EditorApplication.timeSinceStartup - _openTool.StartedAt);
                    _openTool.TimingOpen = false;
                    StampLatestTool((float)_openTool.DurationSeconds);
                }
                var presented = ToolCallPresenter.Present(name, args, null);
                _openTool = AddBubble(presented.Type, presented.Content, null, presented.Title, presented.Note, true);
                return;
            }

            var done = ToolCallPresenter.Present(name, args, result);
            var bubble = _openTool;
            _openTool = null;
            if (bubble == null)
            {
                bubble = AddBubble(done.Type, done.Content, null, done.Title, done.Note);
            }
            else
            {
                bubble.Type = done.Type;
                bubble.Content = done.Content ?? "";
                bubble.Title = done.Title;
                bubble.Note = done.Note;
                bubble.RenderCache = null;
                InvalidateBubbleLayouts();
                _chatScroll.y = float.MaxValue;
                _needsRepaint = true;
            }

            if (bubble == null || !bubble.TimingOpen) return;
            bubble.DurationSeconds = Math.Max(0, EditorApplication.timeSinceStartup - bubble.StartedAt);
            bubble.TimingOpen = false;
            StampLatestTool((float)bubble.DurationSeconds);
            PersistSession();
        }

        private void BeginTurnTimer()
        {
            _turnStartedAt = EditorApplication.timeSinceStartup;
            _turnElapsed = 0;
            _turnTiming = true;
            _turnVisible = true;
            _needsRepaint = true;
        }

        private void FreezeTurnTimer()
        {
            CloseOpenDirective();
            if (!_turnTiming) return;
            _turnElapsed = Math.Max(0, EditorApplication.timeSinceStartup - _turnStartedAt);
            _turnTiming = false;
        }

        private void ResetTurnTimer()
        {
            _turnTiming = false;
            _turnVisible = false;
            _turnElapsed = 0;
            _turnStartedAt = 0;
        }

        private double TurnSeconds()
        {
            if (_turnTiming)
                return Math.Max(0, EditorApplication.timeSinceStartup - _turnStartedAt);
            return Math.Max(0, _turnElapsed);
        }

        private void ResetActivityClocks()
        {
            CloseOpenDirective();
            _responseStartedAt = 0;
            _thinkingStartedAt = 0;
            _thinkingEndedAt = 0;
            _contentStartedAt = 0;
            _unstampedThinking = -1f;
            _thinkingStampIndex = 0;
            _responseHistoryCount = _agent?.History?.Count ?? 0;
            _awaitingContent = false;
            _openTool = null;
            _openDirective = null;
            _openDirectiveMessageId = null;
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
        }

        private void CloseOpenActivity()
        {
            CloseOpenDirective();
            if (_openTool == null || !_openTool.TimingOpen) return;
            _openTool.DurationSeconds = Math.Max(0, EditorApplication.timeSinceStartup - _openTool.StartedAt);
            _openTool.TimingOpen = false;
            StampLatestTool((float)_openTool.DurationSeconds);
            _openTool = null;
        }

        private double ContentDuration()
        {
            double start;
            if (_contentStartedAt > 0)
                start = _contentStartedAt;
            else if (_thinkingEndedAt > 0)
                start = _thinkingEndedAt;
            else
                start = _responseStartedAt;
            if (start <= 0) return 0;
            return Math.Max(0, EditorApplication.timeSinceStartup - start);
        }

        private void FlushUnstampedThinking()
        {
            if (_unstampedThinking < 0 || _agent?.History == null) return;
            var messages = _agent.History.Messages;
            for (int i = messages.Count - 1; i >= _thinkingStampIndex && i >= 0; i--)
            {
                if (messages[i].Role != ChatRole.Assistant) continue;
                if (messages[i].ThinkingSeconds < 0)
                    messages[i].ThinkingSeconds = _unstampedThinking;
                _unstampedThinking = -1f;
                return;
            }
        }

        private void StampAssistantContent(float seconds)
        {
            var messages = _agent?.History?.Messages;
            if (messages == null) return;
            for (int i = messages.Count - 1; i >= _responseHistoryCount && i >= 0; i--)
            {
                if (messages[i].Role != ChatRole.Assistant) continue;
                messages[i].ContentSeconds = seconds;
                return;
            }
        }

        private void StampLatestTool(float seconds)
        {
            var messages = _agent?.History?.Messages;
            if (messages == null) return;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role != ChatRole.Tool) continue;
                messages[i].ToolSeconds = seconds;
                return;
            }
        }

        private void DrawElapsedFooter(ChatBubble b)
        {
            var text = ElapsedText(b);
            if (text == null || _elapsedLabel == null) return;
            GUILayout.Space(2f);
            GUILayout.Label(text, _elapsedLabel, GUILayout.ExpandWidth(true));
        }

        private static string ElapsedText(ChatBubble b)
        {
            if (b == null || !ShowsElapsed(b.Type)) return null;
            if (b.TimingOpen)
            {
                var start = b.StartedAt > 0 ? b.StartedAt : EditorApplication.timeSinceStartup;
                return FormatDuration(Math.Max(0, EditorApplication.timeSinceStartup - start));
            }
            if (b.DurationSeconds < 0) return null;
            return FormatDuration(b.DurationSeconds);
        }

        private static bool ShowsElapsed(BubbleType type)
        {
            return type == BubbleType.Thinking
                || type == BubbleType.AI
                || type == BubbleType.AIStream
                || type == BubbleType.ToolCall
                || type == BubbleType.Code
                || type == BubbleType.Directive;
        }

        private void AddDirectiveBubble(string text)
        {
            CloseOpenDirective();
            var bubble = AddBubble(BubbleType.Directive, text, timingOpen: true);
            if (bubble == null) return;
            _openDirective = bubble;
            _openDirectiveMessageId = LatestUserMessageId();
        }

        private void CloseOpenDirective()
        {
            if (_openDirective == null || !_openDirective.TimingOpen)
            {
                _openDirective = null;
                _openDirectiveMessageId = null;
                return;
            }

            var started = _openDirective.StartedAt > 0 ? _openDirective.StartedAt : EditorApplication.timeSinceStartup;
            _openDirective.DurationSeconds = Math.Max(0, EditorApplication.timeSinceStartup - started);
            _openDirective.TimingOpen = false;
            StampUserMessage(_openDirectiveMessageId, (float)_openDirective.DurationSeconds);
            _openDirective = null;
            _openDirectiveMessageId = null;
            PersistSession();
        }

        private string LatestUserMessageId()
        {
            var messages = _agent?.History?.Messages;
            if (messages == null) return null;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].Role == ChatRole.User)
                    return messages[i].MessageId;
            }
            return null;
        }

        private void StampUserMessage(string messageId, float seconds)
        {
            if (string.IsNullOrEmpty(messageId)) return;
            var messages = _agent?.History?.Messages;
            if (messages == null) return;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].MessageId != messageId) continue;
                messages[i].ContentSeconds = seconds;
                return;
            }
        }

        private static string FormatDuration(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
                seconds = 0;
            if (seconds < 0.05)
                return "0s";
            if (seconds < 60)
                return seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
            int total = (int)seconds;
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;
            int secs = total % 60;
            if (hours > 0)
                return hours + "h " + minutes.ToString("00") + "m " + secs.ToString("00") + "s";
            return minutes + "m " + secs.ToString("00") + "s";
        }

        private static string ThinkingTitle(bool animate)
        {
            return animate ? "Thinking" + ThinkingDots() : "Thinking...";
        }

        private static string ThinkingDots()
        {
            int step = (int)(EditorApplication.timeSinceStartup / 0.45d) % 3;
            if (step <= 0) return ".";
            if (step == 1) return "..";
            return "...";
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
            if (_stylesInit && _markdownBody != null && _codeBlockStyle != null && _directiveBubble != null && _codeBody != null && _elapsedLabel != null)
            {
                ApplyDirectiveMetrics();
                return;
            }
            _stylesInit = true;
            BuilderStyles.EnsureStyles();
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            _userBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleUser);
            _aiBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleAI);
            _thinkBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleThink);
            _codeBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleCode);
            _resultBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleResult);
            _errorBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleError);
            _toolCallBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleToolCall);
            _directiveBubble = BuilderStyles.CreateBubbleStyle(BuilderStyles.BubbleDirective);
            ApplyDirectiveMetrics();
            _directiveTitle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 10,
                wordWrap = false,
                normal = { textColor = BuilderStyles.DirectiveTitle }
            };
            _directiveBody = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                wordWrap = true,
                richText = false,
                normal = { textColor = BuilderStyles.DirectiveBody }
            };
            _toolCallHeader = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11,
                wordWrap = true,
                richText = false,
                normal = { textColor = BuilderStyles.ToolCallText }
            };
            _toolCallLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                normal = { textColor = BuilderStyles.ToolCallDetail }
            };
            _codeTitle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                normal = { textColor = BuilderStyles.CodeTitle }
            };
            _codeNote = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                fontSize = 11,
                richText = false,
                wordWrap = false,
                normal = { textColor = BuilderStyles.CodeNote }
            };
            _codeBody = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false,
                fontSize = 12,
                padding = new RectOffset(6, 6, 4, 4)
            };
            var codeText = new Color(0.84f, 0.9f, 0.84f);
            _codeBody.normal = new GUIStyleState { textColor = codeText };
            _codeBody.focused = new GUIStyleState { textColor = codeText };
            _codeBody.hover = new GUIStyleState { textColor = codeText };
            _codeBody.active = new GUIStyleState { textColor = codeText };
            _markdownBody = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = true,
                wordWrap = true
            };
            _codeBlockStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true,
                richText = false,
                fontSize = 11
            };
            var codeFont = LoadCodeFont();
            if (codeFont != null)
            {
                _codeBody.font = codeFont;
                _codeBlockStyle.font = codeFont;
            }
            _inputStyle = BuilderStyles.CreateInputStyle();
            _statusLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            _elapsedLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontSize = 10,
                padding = new RectOffset(0, 2, 0, 0),
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.68f, 0.68f, 0.68f)
                        : new Color(0.35f, 0.35f, 0.35f)
                }
            };
        }

        private static Font LoadCodeFont()
        {
            try
            {
                var font = Font.CreateDynamicFontFromOSFont(new[] { "Cascadia Mono", "Consolas", "Courier New" }, 12);
                if (font == null) return null;
                font.hideFlags = HideFlags.HideAndDontSave;
                return font;
            }
            catch
            {
                return null;
            }
        }

        private void ApplyDirectiveMetrics()
        {
            if (_directiveBubble == null) return;
            _directiveBubble.border = new RectOffset(0, 0, 0, 0);
            _directiveBubble.overflow = new RectOffset(0, 0, 0, 0);
            _directiveBubble.padding = new RectOffset(10, 12, 8, 14);
            _directiveBubble.margin = new RectOffset(4, 10, 2, 4);
        }
        #endregion
    }

    public enum BubbleType { User, AI, AIStream, Thinking, Code, Result, Error, ToolCall, Directive }

    public class ChatBubble
    {
        public BubbleType Type;
        public string Content;
        public string Title;
        public string Note;
        public Vector2 CodeScroll;
        public bool Expanded;
        public List<string> AttachedFilePaths;
        public double StartedAt;
        public double DurationSeconds = -1;
        public bool TimingOpen;
        [NonSerialized] public ChatBubbleRenderCache RenderCache;
    }

    public sealed class ChatBubbleRenderCache
    {
        public BubbleType Type;
        public string Content;
        public string Title;
        public string Note;
        public float SourceWidth;
        public float WindowWidth;
        public bool ProSkin;
        public bool HasElapsed;
        public float BubbleWidth;
        public float ContentWidth;
        public float ContentHeight;
        public float LayoutHeight;
        public string ToolHeader;
        public float ToolHeaderHeight;
        public string ToolFlow;
        public float ToolFlowHeight;
        public string Code;
        public float CodeHeight;
        public string FlowedNote;
        public float NoteHeight;
        public float CodeBlockHeight;
        public string DirectiveHeading;
        public float DirectiveHeadingHeight;
        public string DirectiveBody;
        public List<MarkdownRenderBlock> MarkdownBlocks;

        public bool Matches(ChatBubble bubble, float sourceWidth, float windowWidth, bool proSkin, bool hasElapsed)
        {
            return bubble != null
                && Type == bubble.Type
                && Content == bubble.Content
                && Title == bubble.Title
                && Note == bubble.Note
                && Mathf.Abs(SourceWidth - sourceWidth) < 1f
                && Mathf.Abs(WindowWidth - windowWidth) < 1f
                && ProSkin == proSkin
                && HasElapsed == hasElapsed;
        }
    }

    public struct MarkdownRenderBlock
    {
        public MarkdownBlockKind Kind;
        public string Text;
        public string Language;
        public float Height;
    }
}

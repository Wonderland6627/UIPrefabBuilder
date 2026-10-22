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
        private readonly List<string> _attachedFiles = new List<string>();
        private bool _visionSendPending;
        private string _streamBuffer = "";
        private string _thinkingStreamBuffer = "";
        private bool _isStreaming;
        private bool _isThinking;
        private bool _showHistory;
        private string _titleRequestedSessionId;
        private bool _ignoreStream;
        private double _lastSessionPersist;

        // Styles
        private GUIStyle _headerStyle, _userBubble, _aiBubble, _thinkBubble, _codeBubble, _resultBubble, _errorBubble, _toolCallBubble;
        private GUIStyle _toolCallLabel;
        private GUIStyle _markdownBody, _codeBlockStyle;
        private GUIStyle _inputStyle, _statusLabel;
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
            ConsoleLogger.Initialize();

            var settings = BuilderSettings.Get();
            _agent = new AgentEngine(settings);
            _agent.OnStateChanged += s =>
            {
                if (s == AgentState.Thinking || s == AgentState.CallingTool)
                    FinalizeContentStream();
                _needsRepaint = true;
            };
            _agent.OnStreamToken += t =>
            {
                if (_ignoreStream) return;
                _streamBuffer += t;
                _isStreaming = true;
                _needsRepaint = true;
            };
            _agent.OnThinkingToken += t =>
            {
                if (_ignoreStream) return;
                _thinkingStreamBuffer += t;
                _isThinking = true;
                _needsRepaint = true;
            };
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
            _hSplitRight = new SplitView("UIPrefabBuilder_HSplitRight", SplitDirection.Horizontal, 320f, 260f, 640f);
            if (_hSplitRight.SplitPosition < 260f)
                _hSplitRight.SplitPosition = 320f;
            _vSplitLeft = new SplitView("UIPrefabBuilder_VSplitLeft", SplitDirection.Vertical, 200f, 80f);

            ConsoleLogger.OnLogAdded += _ => _needsRepaint = true;

            EditorApplication.update += Tick;
            ConsoleLogger.LogVolatile("BuilderWindow opened");
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            PersistSession();
            _agent?.Dispose();
        }

        private void Tick()
        {
            if ((_isStreaming || _isThinking) && EditorApplication.timeSinceStartup - _lastSessionPersist >= 1.0)
                PersistSession();

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
                || state == AgentState.Compiling || state == AgentState.Executing;
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("UIPrefabBuilder", _headerStyle, GUILayout.Width(120));
            GUILayout.FlexibleSpace();

            bool running = IsAgentRunning();
            GUI.enabled = !running;
            if (GUILayout.Button("New Chat", EditorStyles.toolbarButton, GUILayout.Width(65))) NewChat();
            if (GUILayout.Button(_showHistory ? "Close History" : "History", EditorStyles.toolbarButton, GUILayout.Width(85)))
                _showHistory = !_showHistory;
            GUI.enabled = true;
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
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Chat History", BuilderStyles.SectionLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("Open Folder", "打开 Sessions 目录。日志在旁边的 Logs 目录，两边文件名相同。"),
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
                    _showHistory = false;
                }
                if (GUILayout.Button("\u00d7", EditorStyles.miniButton, GUILayout.Width(BuilderStyles.RemoveIconWidth)))
                    _sessions.DeleteSession(s.SessionId);
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
                case BubbleType.Error: style = _errorBubble; break;
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
                if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                    EditorGUIUtility.systemCopyBuffer = b.Content;
                EditorGUILayout.EndHorizontal();
            }
            else if (b.Type == BubbleType.ToolCall)
            {
                GUILayout.Label(b.Content, _toolCallLabel);
            }
            else if (b.Type == BubbleType.AI || b.Type == BubbleType.AIStream)
            {
                DrawMarkdownContent(b.Content, maxBubbleWidth - 16f);
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

        private void DrawMarkdownContent(string content, float contentWidth)
        {
            if (string.IsNullOrEmpty(content))
                return;

            List<MarkdownBlock> blocks;
            try
            {
                var theme = MarkdownView.Theme.ForEditorSkin(EditorGUIUtility.isProSkin);
                blocks = MarkdownView.Parse(content, theme);
            }
            catch (Exception e)
            {
                if (!_markdownFailed)
                {
                    _markdownFailed = true;
                    Debug.LogException(e);
                }
                GUILayout.Label(content, EditorStyles.wordWrappedLabel);
                return;
            }

            if (blocks.Count == 0)
            {
                GUILayout.Label(content, EditorStyles.wordWrappedLabel);
                return;
            }

            for (var i = 0; i < blocks.Count; i++)
            {
                if (i > 0)
                    GUILayout.Space(4f);

                var block = blocks[i];
                if (block.Kind == MarkdownBlockKind.Code)
                    DrawFenceBlock(block.Text, block.Language, contentWidth);
                else
                    GUILayout.Label(block.Text, _markdownBody);
            }
        }

        private void DrawFenceBlock(string code, string language, float contentWidth)
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
            var height = Mathf.Max(20f, _codeBlockStyle.CalcHeight(new GUIContent(shown), width));
            EditorGUILayout.SelectableLabel(shown, _codeBlockStyle, GUILayout.MinHeight(height));
            EditorGUILayout.EndVertical();
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
            bool isRunning = IsAgentRunning();
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

            EditorGUILayout.BeginHorizontal();
            BuilderStyles.ColoredLabel("\u25cf", color, EditorStyles.label, GUILayout.Width(12));
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

            if (hasFiles)
            {
                var filesCopy = new List<string>(_attachedFiles);
                var userText = text;
                var bubbleText = displayText;

                if (VisionCapabilityProbe.IsCacheValid(settings))
                {
                    BeginImageTask(userText, filesCopy, bubbleText);
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
                        BeginImageTask(userText, filesCopy, bubbleText);
                    }
                    else
                    {
                        settings.SupportsVision = false;
                        settings.AutoDisableVisualVerification();
                        settings.Save();
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

            AddBubble(BubbleType.User, displayText, null);
            _ignoreStream = false;
            _agent?.StartTask(text);
            RequestAutoTitle(hasText ? text : displayText);
            PersistSession();
            _input = "";
            _attachedFiles.Clear();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
            GUI.FocusControl(null);
        }

        private void BeginImageTask(string userText, List<string> files, string bubbleText)
        {
            AddBubble(BubbleType.User, bubbleText, new List<string>(files));
            _ignoreStream = false;
            _agent?.StartTask(userText, files);
            RequestAutoTitle(string.IsNullOrWhiteSpace(userText) ? bubbleText : userText);
            PersistSession();
            _input = "";
            _attachedFiles.Clear();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
            GUI.FocusControl(null);
            _needsRepaint = true;
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
            PersistSession();
        }

        private void StopTask()
        {
            _ignoreStream = true;
            var partial = _streamBuffer;
            _agent?.Cancel();
            FinalizeThinkingStream(null);
            FinalizeContentStream();
            _isStreaming = false;
            _isThinking = false;
            if (!string.IsNullOrWhiteSpace(partial))
                _agent?.History?.AddAssistant(partial);
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
            _agent.History.Clear();
            _agent.Refresh();
            _sessions.NewSession();
            _streamBuffer = "";
            _thinkingStreamBuffer = "";
            _isStreaming = false;
            _isThinking = false;
            ConsoleLogger.LogVolatile("New chat session created");
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
            PersistSession();
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
            if (_stylesInit && _markdownBody != null && _codeBlockStyle != null)
                return;
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
            _toolCallLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                richText = false,
                normal = { textColor = BuilderStyles.ToolCallText }
            };
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
            _inputStyle = BuilderStyles.CreateInputStyle();
            _statusLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
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

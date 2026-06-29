# Changelog

## [0.2.0] - 2026-06-29

### Added
- **设计稿上传**：聊天输入框支持附加 PNG/JPG/WebP 设计稿，Agent 自动分析结构并还原 UI
- **CLIP 视觉资源索引**：基于 Unity Sentis 的 CLIP embedding，支持语义搜图与视觉相似度匹配
- **视觉搜图工具**：`search_sprites_by_text`、`search_sprites_by_text_batch`、`match_sprite_by_image`
- **索引管理工具**：`rebuild_asset_index`、`get_index_status`
- **一键环境部署**：Settings 面板内 Sentis 安装、CLIP Vision/Text 模型下载与进度追踪（`ModelDownloadManager`）
- **任务意图识别**：自动区分 Build / Analyze / Modify / Query，Analyze/Query 模式仅暴露只读工具
- **批量工具扩展**：`set_image_batch`、`set_text_properties_batch`、`add_outline_batch`、`add_layout_element_batch`、`set_component_property_batch`
- **设计稿对比验证**：`analyze_screenshot` 支持 `referenceImagePath` 参数，实现效果图与实现稿并排对比
- **CanvasScaler 模式**：ProjectConfig 新增 `screenMatchMode`（MatchWidthOrHeight / Expand / Shrink）

### Improved
- Agent System Prompt 按任务意图动态裁剪，分析类请求不再误触发 UI 创建
- `execute_code` 自动注入 `ActionName`/`Description`，修复裸 `return;` 编译失败
- Sprite Mapping 过滤空条目，Prompt 注入更精准
- 设计稿还原新增 Visual Fidelity Checklist（背景层、标题栏、Tab 状态、文字样式等）

## [0.1.0] - 2026-06-07

### Added
- Initial package structure
- ReAct Agent engine with non-blocking LLM communication
- Dynamic C# compilation via Roslyn
- Anthropic-standard Skills system
- IMGUI EditorWindow with chat interface

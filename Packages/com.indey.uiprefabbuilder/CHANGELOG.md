# Changelog

## [0.3.0] - 2026-07-07

### Added
- **真正的设计稿区域视觉匹配**：新增 `match_sprite_by_region` / `match_sprite_by_region_batch`，直接在设计稿上给出归一化 bbox（0-1，左上角原点），服务端自动裁剪并做 CLIP 图像相似度匹配 —— 解决了 `match_sprite_by_image` 要求 LLM 自行产出裁剪像素/base64 而实际不可用的问题；新增 `crop_design_image` 用于匹配前预览裁剪区域
- 新增 `Core/DesignImageContext`：把当前任务的设计稿统一暴露为 Assets 相对路径，供上述新工具复用（设计稿引用范围为单次任务/单轮，不跨多轮持久化）
- **批量清理/查询工具**：新增 `destroy_object_batch`、`inspect_hierarchy_batch`、`inspect_components_batch`，覆盖此前只能逐个调用的高频场景
- **`take_screenshot` 自动预设 Camera/EventSystem**：截图前自动检测场景中是否存在可用的启用状态 Camera 与 EventSystem，缺失时自动创建一个带合理默认值（SolidColor 背景、cullingMask=Everything、位置 (0,0,-10)）的预览摄像机，不再需要 Agent 通过 `execute_code` 反复试探摄像机/Canvas 渲染配置

### Fixed
- **`save_as_prefab` 静默覆盖已有 prefab**：新增覆盖保护，目标路径已存在同名 prefab 时直接拒绝执行并报错，不再无声覆盖工程里已有的正式 prefab 资产
- **`apply_prefab` 可写穿到 Project 资产**：彻底禁用该工具（`PrefabUtility.ApplyPrefabInstance` 会把场景实例的改动写回源 prefab 资产）。Agent 现在只允许修改 Hierarchy 中的场景实例，不允许覆盖或修改 Project 中的 prefab 资产；System Prompt 的 SAFETY 章节新增明确规则说明这一限制（含 `execute_code` 场景）
- **纯分析请求被强行拉入建造流程**：移除了"搜索过但只回复文字就强制注入'请开始建造'"的 nudge 逻辑——该逻辑不看用户实际意图，曾导致像"分析此UI设计稿的结构"这类只读分析请求被强行转成建造任务
- **只读分析任务被误读为"禁止调用任何工具"**：修正 SAFETY 规则措辞——只读/分析类任务禁止创建/建造/保存/修改，但仍应正常调用 `match_sprite_by_region_batch`/`search_sprites_by_text_batch` 等非破坏性搜索工具做真实的素材匹配，而不是仅凭视觉描述直接下结论
- **`match_sprite_by_region_batch` 未被优先使用**：加强系统提示措辞——只要本轮任务有设计稿可用，就必须先对每个视觉元素调用 `match_sprite_by_region_batch` 做真实图像匹配，再考虑退化到 `search_sprites_by_text_batch` 纯文字搜索（纯文字搜索容易搜到"语义对但视觉错"的素材，如用圆形勾选框替代方形勾选框）
- **低置信度素材匹配被静默当作正确答案使用**：`match_sprite_by_region(_batch)` / `search_sprites_by_text(_batch)` 之前只用 `minConfidence` 做二值过滤，一旦"勉强及格"的最佳匹配通过了过滤，模型会把它当成正确答案直接用上，产出视觉上明显不对的贴图。现在这四个工具在最佳匹配分数低于健康阈值（图像匹配 score<0.65 / 文字匹配 confidence<30）时，会在返回结果里标记 `lowConfidence: true` 并附带 `_hint`；系统提示同步要求模型看到该标记时改用纯色占位并在总结里明确告知用户"该元素未找到可靠素材"，而不是悄悄用一个视觉不符的贴图充数
- **截图前反复用 `execute_code` 调试摄像机，浪费大量步骤**：场景内没有 Camera/EventSystem 时，Agent 曾连续写十几个 `execute_code` 脚本来回切换 Canvas 渲染模式、clearFlags、cullingMask 才能截到图。现在 `take_screenshot` 内部自动补齐缺失的 Camera/EventSystem（见上），并在系统提示中明确禁止再手写 `execute_code` 调试渲染管线，截图异常应优先排查 UI 布局而非摄像机

### Improved
- **Agent 护栏**：`execute_code` 拦截 Camera/clearFlags/cullingMask 等渲染管线调试；连续提交高度相似代码时自动阻断并引导改用专用工具；连续多次单次搜索时注入批量搜索提醒；`analyze_screenshot` 后若反复用 `execute_code` 调试渲染则强制转向布局修复
- **设计稿路径统一**：上传的设计稿自动复制/映射到 `Assets/Screenshots/`，供 `analyze_screenshot`、`crop_design_image`、`match_sprite_by_region` 等工具共享同一 Assets 相对路径
- **搜索预算收紧**：搜索软/硬预算与建造后搜索预算下调，减少纯搜索阶段空转步数

## [0.2.0] - 2026-06-29

### Added
- **设计稿上传**：聊天输入框支持附加 PNG/JPG/WebP 设计稿，Agent 自动分析结构并还原 UI
- **CLIP 视觉资源索引**：基于 Unity Sentis 的 CLIP embedding，支持语义搜图与视觉相似度匹配
- **视觉搜图工具**：`search_sprites_by_text`、`search_sprites_by_text_batch`、`match_sprite_by_image`
- **索引管理工具**：`rebuild_asset_index`、`get_index_status`
- **一键环境部署**：Settings 面板内 Sentis 安装、CLIP Vision/Text 模型下载与进度追踪（`ModelDownloadManager`）
- **批量工具扩展**：`set_image_batch`、`set_text_properties_batch`、`add_outline_batch`、`add_layout_element_batch`、`set_component_property_batch`
- **设计稿对比验证**：`analyze_screenshot` 支持 `referenceImagePath` 参数，实现效果图与实现稿并排对比
- **CanvasScaler 模式**：ProjectConfig 新增 `screenMatchMode`（MatchWidthOrHeight / Expand / Shrink）

### Improved
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

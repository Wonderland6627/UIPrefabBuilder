# Changelog

## [0.5.1] - 2026-08-14

### Fixed
- **Slider 创建后完全不可见**：`create_slider` 此前只挂一个空的 `Slider` 组件，节点在层级里但一个像素都不画（设计稿里的数量条/进度条整段消失）。现在创建完整的 Background / Fill Area→Fill / Handle Slide Area→Handle 并接线 `fillRect`、`handleRect`、`targetGraphic`，同时按 `value` 摆好 Fill 与 Handle，编辑器截图即所见
- **图标钮被逼着叠字**：`create_button` 传空文案时不再生成 Text 子节点；裁定环节对「已有 sprite 的按钮上的空标签」改为提示删除空标签，而不是要求写文案——后者会在 `+`/`-`/箭头图形上再压一层文字
- **按钮内图标压住自身文案**：`create_button` 新增 `iconSpritePath` / `iconPosition` / `iconWidth` / `iconHeight`，图标与标签分占互不相交的区域（此前把图标丢进铺满按钮的标签上，价格数字会被图标遮掉半个）
- **`create_batch` 丢参数**：button 的 `spritePath`（背景图）、`fontSize` 与 inputfield 的文字颜色此前被忽略，必须事后再补一轮 `set_image_batch` / `set_text_properties_batch`

### Added
- **`create_slider` 完整参数**：`backgroundSpritePath` / `fillSpritePath` / `handleSpritePath`、`handleWidth`、`vertical`、宽高与 background/fill/handle 三组 tint
- **`create_batch` 支持新字段**：`iconSpritePath`、`iconPosition`、`iconWidth/Height`、`fillSpritePath`、`handleSpritePath`、`handleWidth`、`min/max/value`、`vertical`

### Improved
- **视觉裁定新增两类硬检查**：Slider 无 Fill/Handle 图形（或 Fill/Handle 仍是未贴图纯白）时拒绝 `matches=true`；按钮内图标压在自身文案字形上时拒绝 `matches=true`（按文本 preferred 尺寸而非标签矩形判定，正常的「图标居左 + 文字居中」不会误报）
- **Prompt 结构指引**：新增进度条/数量条必须用 `create_slider` 并同时传三张图、图标钮用空文案、图标+文字同框用 `create_button` 的图标参数

## [0.5.0] - 2026-08-13

### Added
- **`create_tab_bar`**：按 ToggleGroup + 每 Tab 未选中底图 / Selected 高亮 / Label 创建真实页签栏，替代用普通 Button 冒充选中态
- **`configure_selectable_states` / `configure_selectable_states_batch`**：配置 Button/Toggle/Tab 的 normal/highlighted/pressed/selected/disabled tint 与 SpriteSwap
- **`wire_toggle_graphics`**：把已有 Toggle 的 `targetGraphic` / `graphic` 接到背景与选中高亮子节点
- **完整接线的 `create_inputfield` / `create_toggle`**：InputField 自动建 Text Area → Placeholder / Text；Toggle 自动建 Background + Checkmark 选中高亮
- **TMP 文字样式扩展**：`set_text_properties(_batch)` 支持 `fontAssetPath`、outline、字距/行距、自动字号、换行等

### Fixed
- **Project Config 面板 Rules/Notes 换行高度**：按面板宽度 `CalcHeight` 自适应，避免 Layout/Repaint 高度不一致裁切光标
- **`set_text_properties` 静默成功**：目标无文本组件时返回失败；仅在回落到唯一文本子节点时给出 warning
- **错误 Sprite 无法靠 tint 清除**：`set_image(_batch)` 支持 `spritePath=""` 显式清空贴图，避免错误美术被染色后仍残留

### Improved
- **结构优先 Prompt**：强制 Tab→`create_tab_bar`、可选项→`create_toggle`、编辑框→`create_inputfield`；设计稿须先完整盘点再测量
- **测量闸门**：有设计稿时 `create_batch` 在完成 `map_design_rect(_batch)` 前会被拒绝
- **视觉裁定加强**：拒绝工厂默认（空文案、统一字号、未贴图按钮、未接线 InputField、无选中态差异）；场景变更会使截图链失效并强制重截
- **PROJECT RULES 优先级**：Rules 覆盖 `map_design_rect` 尺寸与后续视觉抛光；任务开始写入会话日志，终裁前须复核
- **定点修复护栏**：verdict 拒绝后只改点名节点，禁止回退已修好的改动或重刷已匹配元素

## [0.4.0] - 2026-08-12

### Added
- **结构化视觉裁定 `report_visual_verdict`**：`analyze_screenshot` 仅表示截图已送达模型；任务结束前必须对最新截图提交可接受的 verdict。`visibleElements` 需带归一化 bbox，工具会采样像素拒绝「看不见却声称可见」的断言；图标级无 Sprite 占位色块时拒绝 `matches=true`
- **视觉验证闸门**：强制 `take_screenshot` → `analyze_screenshot` → `report_visual_verdict` 闭环；不匹配时最多 3 轮定点修复，用尽后要求诚实总结；空回复也会 nudge 继续
- **设计稿坐标换算**：`map_design_rect` / `map_design_rect_batch`，将归一化 bbox 转为 Canvas `anchoredPosition` + `sizeDelta`；任务消息注入 `[DesignImageMeta]`（像素尺寸与设计分辨率）
- **Vision 能力探测**：勾选 Supports Vision 时向 API 发送 1×1 测试图确认多模态可读图；失败自动取消勾选并弹窗说明；带设计稿发任务前缓存复检失败则中止本轮
- **`search_assets_glob_batch`**：批量 Glob 文件名搜索

### Fixed
- **`EnableVisualVerification` 未生效**：有设计稿且发生过 Build 时，结束前强制走完视觉验证链
- **LayoutGroup 默认 ForceExpand**：`childForceExpandWidth/Height` 默认改为 `false`，减少设计稿间距被撑开
- **Sprite 静默变白方块**：`set_image` / `set_image_sprite` 等区分「路径不存在」与「TextureType 不是 Sprite」，失败返回可诊断错误；赋 Sprite 时若未指定 color 则重置为白色，避免默认蓝色 tint 污染贴图
- **CLIP 索引变形**：PNG/JPG 优先用磁盘原始字节解码 embedding，避免 Unity 导入管线 NPOT 缩放/压缩拉低视觉匹配准确率

### Improved
- **Prefab 实例保护**：`destroy_object` / `destroy_object_batch` 拒绝删除 Prefab 实例与已有内容的 Canvas；`execute_code` 同步拦截 `Destroy` / `DestroyImmediate` / `RemoveComponent`，防止绕过护栏拆掉已实例化的美术资源
- **截图可见性临时强制**：`take_screenshot` 捕获期间临时拉起隐藏的 CanvasGroup / 未播放的 intro Animator / 零 scale，避免 Agent 为截图去改 Prefab
- **Visual Verification 自动开关记忆**：探测失败仅自动关闭，探测成功可恢复；用户手动开关后不再被探测覆盖
- **视觉搜索结果压缩**：MessageHistory 压缩时保留全部 region/query，只裁剪每项 top matches，避免截断 JSON 丢掉后半区域
- **图像匹配默认阈值**：`minConfidence` 默认提升至 `0.65`
- 无 Vision 时禁用设计稿附加区；Visual Verification 依赖 Supports Vision
- 设计稿还原 Prompt 收紧：禁止目测估位，优先已有相关 Prefab 实例化后再调布局/贴图；禁止删掉重建
- 默认模型名改为 `gemini-3.6-flash`

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

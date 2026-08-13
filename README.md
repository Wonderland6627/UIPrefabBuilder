<h1 align="center">UI Prefab Builder</h1>

<p align="center">
  <strong>Unity Editor 内嵌 AI Agent —— 用自然语言构建 UGUI</strong>
</p>

<p align="center">
  <a href="https://unity.com/releases/editor/whats-new/2021.3.0"><img src="https://img.shields.io/badge/Unity-2021.3%2B-blue?logo=unity" alt="Unity 2021.3+"/></a>
  <img src="https://img.shields.io/badge/version-0.5.0-green" alt="Version"/>
  <img src="https://img.shields.io/badge/platform-Editor%20Only-lightgrey" alt="Platform"/>
  <img src="https://img.shields.io/badge/LLM-OpenAI%20Compatible-orange?logo=openai" alt="LLM"/>
  <img src="https://img.shields.io/badge/license-MIT-purple" alt="License"/>
</p>

<p align="center">
  <a href="#-features">Features</a> •
  <a href="#-installation">Installation</a> •
  <a href="#-quick-start">Quick Start</a> •
  <a href="#-how-it-works">How It Works</a> •
  <a href="#-tool-system">Tools</a> •
  <a href="#-extending">Extending</a>
</p>

---

## 📸 Preview

<p align="center">
  <img src="Documentation~/Images/builder-window.png" alt="Builder Window Screenshot" width="900"/>
</p>

---

## ✨ Features

| | Feature | Description |
|---|---------|-------------|
| 🤖 | **自然语言驱动** | 描述需求，AI 自主规划并执行完整 UI 构建流程 |
| 🖼️ | **设计稿还原** | 上传设计稿图片，Agent 分析结构、匹配本地资源、还原 UGUI 并对比验证 |
| 🔍 | **CLIP 视觉索引** | 基于 CLIP embedding 的语义搜图与视觉相似度匹配，超越文件名搜索 |
| 🔧 | **78 结构化工具** | 基于 OpenAI Function Calling，覆盖资源搜索、批量创建、Tab/Toggle 交互结构、属性设置、布局、特效、Prefab 全流程 |
| 📐 | **项目级配置** | Sprite/Prefab 映射、组件类型覆盖、Rules 规则（优先于目测尺寸）、设计分辨率，自动注入 Agent Prompt |
| ⚡ | **Batch-First 架构** | `create_batch` 一次创建整棵 UI 树，`set_rect_transform_batch` 一次定位所有元素 |
| 🛡️ | **Prefab 安全保护** | 禁止覆盖已有 prefab、禁止 apply 写回源、禁止销毁 Prefab 实例/已有 Canvas；优先 `instantiate_prefab` 后就地调整 |
| 🚧 | **Agent 护栏** | 拦截摄像机调试/破坏性 `execute_code`、重复脚本与单次搜索空转；强制截图→分析→结构化裁定闭环 |
| 🔄 | **4 阶段工作流** | Understand → Build → Verify → Finalize，Agent 自主遵循最佳实践 |
| 🎛️ | **交互结构优先** | Tab 用 `create_tab_bar`、可选项用 `create_toggle`、编辑框用完整接线的 `create_inputfield`；禁止用纯 Image+Text 冒充 |
| 📸 | **视觉验证** | `take_screenshot` → `analyze_screenshot` → `report_visual_verdict`（像素抽样 + 工厂默认/结构校验），改场景后强制重截；不匹配则定点修复（需 Vision） |
| 🪞 | **通用反射引擎** | `set_component_property` 可通过反射设置任意组件的任意属性 |
| 📡 | **流式响应** | SSE 实时流式输出，支持 Thinking/Extended Thinking 展示 |
| 💾 | **会话管理** | 多会话保存/加载/导出，持久化于 `Library/UIPrefabBuilder/Sessions`，每会话独立日志 |
| 🧠 | **上下文压缩** | 自动截断/摘要历史工具结果，长任务不会 token 溢出 |
| ↩️ | **事务级 Undo** | `TransactionManager` 将单次 Agent 任务打包为 Undo 组，可随时 Rollback 整次任务 |

---

## 📥 Installation

### 方式一：Git URL（推荐）

打开 Unity 的 **Window → Package Manager → + → Add package from git URL**，输入：

```
https://github.com/Wonderland6627/UIPrefabBuilder.git?path=Packages/com.indey.uiprefabbuilder
```

如需锁定版本，可追加 `#v版本号`：

```
https://github.com/Wonderland6627/UIPrefabBuilder.git?path=Packages/com.indey.uiprefabbuilder#v0.5.0
```

### 方式二：手动编辑 manifest.json

编辑项目中 `Packages/manifest.json`，在 `dependencies` 中添加：

```json
{
  "dependencies": {
    "com.indey.uiprefabbuilder": "https://github.com/Wonderland6627/UIPrefabBuilder.git?path=Packages/com.indey.uiprefabbuilder",
    ...
  }
}
```

> **环境要求：** Unity 2021.3+，Git 已安装并加入系统 PATH。

---

## 🚀 Quick Start

### 1. 配置 LLM

1. 打开 **Window → UI Prefab Builder**
2. 在**左侧** Settings 面板（上半区）填入：
   - **Base URL** — OpenAI 兼容 API 地址（如 `https://api.openai.com/v1`）
   - **API Key** — 你的 API 密钥
   - **Model** — 模型名称（如 `gpt-4o`、`claude-sonnet-4-20250514`）
3. 在 Settings 面板（下半区 **Agent Configuration**）按需调整超时、步数、Extended Thinking、Visual Verification 等

### 2. 配置项目规范（推荐）

**右侧** **Project Config** 面板中设置项目约定，保存到 `ProjectSettings/UIPrefabBuilderConfig.json`：

- **Basic Info** — 横/竖屏、设计分辨率、CanvasScaler Match W/H 或 Expand/Shrink 模式、项目备注
- **Sprite Mapping** — 角色名 → Sprite 路径（如 `confirm_button` → `Assets/Sprites/UI/btn_green.png`）
- **Prefab Mapping** — 角色名 → Prefab 路径（Agent 优先 `instantiate_prefab` 而非从零创建）
- **Component Overrides** — 指定 Text/Button/Image/InputField 及自定义组件类型（如 TMP）
- **Rules** — 项目级约定规则，自动注入 Agent System Prompt
- **Asset Indexing** — 索引目录与忽略规则，配合 Settings 面板启用 CLIP 视觉索引

配置会自动注入 Agent System Prompt，创建 Canvas 时也会应用设计分辨率。

### 3. 启用视觉资源索引（推荐，设计稿还原必备）

在 **Settings 面板 → Asset Indexing** 中：

1. 勾选 **Enable Asset Indexing**
2. 点击 **Install** 安装 Unity Sentis（或手动通过 Package Manager 添加 `com.unity.sentis`）
3. 点击 **Download** 下载 CLIP Vision / Text 模型（一键下载，带进度条）
4. 配置索引目录（默认 `Assets/Sprites`），点击 **Build Index**

索引构建后，Agent 可通过自然语言描述（如 "gold coin"、"red button"）或设计稿裁剪区域匹配项目 Sprite。

### 4. 开始构建

```
1. 在聊天输入框输入需求，或点击 "+" 附加设计稿图片
2. 按 Send（或 Ctrl+Enter）发送
3. 观察 AI 四阶段工作流：搜索资源 → 批量创建 → 验证修复 → 总结交付
4. 满意后继续下一个任务，不满意点 Undo 回滚
```

### 示例指令

```
"搜索项目里所有 dialog 相关的 Sprite，创建一个设置弹窗"
"批量创建 3x4 的物品网格，每格包含图标和数量文字"
"给现有的 Canvas 添加一个底部导航栏，包含 4 个按钮"
"检查场景里的 ScrollView 结构，修复布局问题"
"把 SettingsPanel 保存为 Prefab 到 Assets/Prefabs/"
"分析这张设计稿的 UI 结构，不要创建任何东西"
"根据附件设计稿还原这个弹窗界面"
```

---

## 🔬 How It Works

### 4 阶段工作流

```mermaid
flowchart LR
    A[💬 用户输入] --> B["🔍 Phase 1: Understand<br/>项目配置 + 搜索资源 + 分析场景"]
    B --> C["🏗️ Phase 2: Build<br/>批量创建 + 批量布局"]
    C --> D["✅ Phase 3: Verify<br/>截图分析 / 层级检查"]
    D --> E["📦 Phase 4: Finalize<br/>保存 Prefab + 总结"]
    D -->|发现问题| C
```

### 核心架构

```mermaid
flowchart TB
    subgraph UI["BuilderWindow · 三栏布局"]
        direction LR
        LEFT["Settings + Console<br/>LLM / Agent 设置 · 实时日志"]
        CENTER["Chat Panel<br/>流式消息 · Thinking · 工具调用"]
        RIGHT["Project Config<br/>Sprite/Prefab · Rules"]
    end

    CENTER --> ENG["AgentEngine<br/>4-Phase Agentic Loop"]
    RIGHT -.->|BuildPromptSection| ENG

    ENG --> TR["ToolRegistry<br/>Definitions"]
    ENG --> LLM["LLMClient<br/>SSE · Tool Call · Vision"]
    ENG --> MH["MessageHistory<br/>Compression · Multimodal"]

    TR --> TM["TransactionManager<br/>任务级 Undo"]
    TR --> SK["Skills Layer<br/>ToolExecutor → UICreator / Helpers"]

    SK --> IDX["Indexing<br/>CLIP · AssetIndexer · VectorStore"]
    SK --> CMP["Compiler<br/>DynamicCompiler · SandboxValidator"]
    SK --> TX["Transaction<br/>UndoScope · SnapshotService"]
    SK --> ASY["Async<br/>BackgroundWorker · MainThreadDispatcher"]

    LEFT --> LOG["ConsoleLogger<br/>Library/UIPrefabBuilder/Logs · Sessions"]
```

| 层级 | 模块 | 职责 |
|------|------|------|
| UI | `BuilderWindow` | 三栏 SplitView：左 Settings+Console，中 Chat，右 Project Config |
| Core | `AgentEngine` | 4 阶段循环、System Prompt 构建、工具调度、视觉裁定闸门与 Agent 护栏 |
| Core | `DesignImageContext` | 当前任务设计稿 Assets 路径，供区域裁剪与视觉匹配工具共享 |
| Core | `LLMClient` / `MessageHistory` | SSE 流式通信、上下文压缩（含视觉搜索结果结构化裁剪）、多模态 |
| Skills | `ToolRegistry` + `ToolExecutor` | 78 工具定义与执行 |
| Indexing | `AssetIndexer` / `EmbeddingService` | CLIP 视觉索引、语义搜图、相似度匹配 |
| Infra | `Compiler` / `Transaction` / `Async` | 动态编译、Undo 事务、后台任务 |
| Config | `BuilderSettings` / `ProjectConfig` | Editor 设置（Preferences）与项目规范（JSON） |

> Editor 设置保存在 `UIPrefabBuilder/Settings.asset`（Editor Preferences）；项目配置保存在 `ProjectSettings/UIPrefabBuilderConfig.json`。

### Agent 循环

| 阶段 | 说明 |
|------|------|
| **Thinking** | 消息历史 + 完整工具定义 + 项目配置发给 LLM，等待推理 |
| **CallingTool** | 执行 `tool_calls`，收集结果，连续失败自动注入修复提示 |
| **Thinking** (再次) | 工具结果 + 可选截图回传 LLM，动态调整策略 |
| **Completed** | 不再调用工具，输出最终总结 |

步数上限可配置（默认无限制，硬上限 500 步安全阀）。

---

## 🔧 Tool System

共 **78 内置工具**，按 OpenAI Function Calling 标准定义。

### 资源探索

| 工具 | 功能 |
|------|------|
| `search_assets` | AssetDatabase 类型搜索（`t:Sprite`、`t:Prefab`） |
| `search_assets_glob` | 文件名 Glob 模式搜索（`*popup*`、`btn_*_lg*`） |
| `search_assets_glob_batch` | **批量** Glob 文件名搜索（推荐） |
| `get_asset_info` | 获取资源详情（尺寸、9-slice border、pivot 等） |
| `get_project_config` | 获取项目配置（设计规范、Sprite/Prefab 映射、组件覆盖） |

### 视觉资源索引（需启用 Asset Indexing）

| 工具 | 功能 |
|------|------|
| `search_sprites_by_text` | CLIP 语义搜图，按自然语言描述匹配 Sprite（如 "gold coin"、"red button"） |
| `search_sprites_by_text_batch` | **批量语义搜图**，一次查找多个 Sprite（推荐） |
| `match_sprite_by_region` | **（推荐）** 直接在设计稿上给出归一化 bbox（0-1，左上角原点），服务端自动裁剪并做 CLIP 图像相似度匹配 —— 不需要模型自己产出裁剪像素/base64 |
| `match_sprite_by_region_batch` | **批量**版 `match_sprite_by_region`，一次匹配多个设计稿区域（推荐） |
| `crop_design_image` | 按 bbox 裁剪设计稿区域并返回预览图，用于匹配前肉眼确认坐标是否准确 |
| `map_design_rect` | 将设计稿归一化 bbox 换算为 Canvas `anchoredPosition` + `sizeDelta`（禁止目测估位） |
| `map_design_rect_batch` | **批量**版 `map_design_rect` |
| `match_sprite_by_image` | （legacy）需要调用方直接传入裁剪好的 base64 图片字节，文本类 LLM 通常无法产出真实像素，建议优先使用 `match_sprite_by_region` |
| `rebuild_asset_index` | 全量重建视觉索引（后台异步） |
| `get_index_status` | 查询索引状态（是否就绪、条目数、构建时间） |

> **低置信度提示**：默认 `minConfidence=0.65`；最佳匹配低于健康阈值（图像 score<0.65 / 文字 confidence<30）时标记 `lowConfidence: true` 并附带 `_hint`，Agent 应改用纯色占位并在总结中说明。上下文压缩会保留全部 region/query，只裁剪每项 top matches。

### UI 创建

| 工具 | 功能 |
|------|------|
| `create_batch` | **批量创建**多个 UI 元素（推荐，单次调用创建整棵 UI 树） |
| `create_canvas` | 创建 Canvas（自动应用 ProjectConfig 设计分辨率） |
| `create_panel` / `create_button` / `create_text` / `create_image` | 单个元素创建（尊重 Component Overrides） |
| `create_inputfield` | 创建完整接线的 InputField（Text Area → Placeholder / Text；支持 placeholder、text、fontSize、backgroundSpritePath；TMP 可用时用 TMP_InputField） |
| `create_toggle` | 创建完整接线的 Toggle（Background + Checkmark 选中高亮 + 可选 Label；支持 background/checkmark Sprite） |
| `create_tab_bar` | **Tab 栏**：ToggleGroup + 每 Tab 未选中底图 / Selected 高亮 / Label（禁止用普通 Button 冒充） |
| `create_slider` / `create_dropdown` | 交互控件创建 |
| `create_scrollview` | 创建完整 ScrollRect（含 Viewport + Content + ContentSizeFitter） |

### RectTransform

| 工具 | 功能 |
|------|------|
| `set_rect_transform` | 一次性设置 anchor/size/position/pivot（推荐） |
| `set_rect_transform_batch` | **批量定位**多个元素（推荐） |
| `set_anchor` / `set_size` / `set_position` | 单属性设置（兼容保留） |

支持 14 种锚点预设（含 StretchTop/Bottom/Left/Right）和原始 anchorMin/Max 浮点值。

### 属性修改

| 工具 | 功能 |
|------|------|
| `set_image_batch` | **批量设置**多个 Image 属性（sprite、type、color） |
| `set_image` | 综合 Image 属性（sprite、type=Sliced/Filled、fillAmount、preserveAspect、color） |
| `set_text_properties_batch` | **批量设置**多个 Text 属性（内容、字号、alignment、color、字体、描边、字距等） |
| `set_text_properties` | 综合 Text 属性（内容、字号、alignment、fontStyle、color、overflow、`fontAssetPath`、TMP outline、字距/行距/自动字号/换行），兼容 Legacy Text 和 TMP；无文本组件时返回失败 |
| `set_image_sprite` / `set_image_color` / `set_text` | 单属性设置（兼容保留）；`set_image` 支持 `spritePath=""` 清除错误贴图 |
| `set_component_property_batch` | **批量反射**设置多个组件属性 |
| `set_component_property` | **通用反射**：设置任意组件的任意属性（支持基本类型、枚举、Color、Vector、资源引用） |
| `add_component` | 按类型名添加任意 Unity 组件 |

### 布局管理

| 工具 | 功能 |
|------|------|
| `add_vertical_layout` / `add_horizontal_layout` | 布局组（含 childControl/childForceExpand 参数） |
| `add_grid_layout` | 网格布局 |
| `add_layout_element_batch` | **批量添加** LayoutElement |
| `add_layout_element` | LayoutElement（含 flexibleWidth/Height） |
| `add_canvas_group` | CanvasGroup（透明度、交互控制） |

### UI 特效

| 工具 | 功能 |
|------|------|
| `add_mask` | 添加 Mask 或 RectMask2D |
| `add_outline_batch` | **批量添加** Outline 或 Shadow 效果 |
| `add_outline` | 添加 Outline 或 Shadow 效果 |
| `configure_selectable` | 配置 Button/Toggle 的 Transition 类型和 Navigation（不负责选中态外观） |
| `configure_selectable_states` | 配置 Selectable 各状态 tint / SpriteSwap（normal/highlighted/pressed/selected/disabled） |
| `configure_selectable_states_batch` | **批量**版 `configure_selectable_states`（推荐用于 Tab / 选项网格） |
| `wire_toggle_graphics` | 将已有 Toggle 的 targetGraphic / graphic 接到背景与选中高亮子节点 |

### GameObject 管理

| 工具 | 功能 |
|------|------|
| `destroy_object` | 删除 GameObject（支持 Undo；拒绝 Prefab 实例与已有内容的 Canvas） |
| `destroy_object_batch` | **批量删除**多个 GameObject（推荐；同样受 Prefab/Canvas 保护） |
| `set_parent` | 重新挂载到新父节点 |
| `rename_object` | 重命名 |
| `set_sibling_index` | 调整渲染和布局顺序 |

### 场景查询

| 工具 | 功能 |
|------|------|
| `get_scene_overview` | 所有 Canvas 和 UI 结构概览 |
| `get_scene_info` | 当前场景名称和路径 |
| `find_by_name` / `find_by_tag` | 按名称/标签查找 |
| `find_ui_elements` | 按 UI 类型查找（Button、Image、Text 等） |
| `inspect_hierarchy` | 描述层级树 |
| `inspect_hierarchy_batch` | **批量**描述多个根节点的层级树（推荐） |
| `inspect_components` | 列出组件详情 |
| `inspect_components_batch` | **批量**列出多个对象的组件详情（推荐） |

### Prefab 操作

| 工具 | 功能 |
|------|------|
| `save_as_prefab` | 保存为**新** Prefab（目标路径已存在同名 prefab 时拒绝执行并报错，防止覆盖已有资产） |
| `instantiate_prefab` | 实例化 Prefab 到场景（ProjectConfig 映射的 Prefab 优先使用） |
| `find_prefab_instances` | 查找场景中的 Prefab 实例 |
| `save_scene` | 保存当前场景 |

> ⚠️ **Prefab 保护**：Agent 只允许修改 Hierarchy 中的场景实例，不允许覆盖已存在的 prefab 资产。`apply_prefab` 已被禁用；`save_as_prefab` 拒绝覆盖已有路径；`destroy_object(_batch)` 拒绝拆除 Prefab 实例与已有内容的 Canvas。设计稿还原优先实例化高度相关 Prefab 后就地调整，禁止删掉重建。`execute_code` 同步禁止 `Destroy`/`DestroyImmediate`/`RemoveComponent` 以及 `PrefabUtility.ApplyPrefabInstance` / 覆盖式 `SaveAsPrefabAssetAndConnect`。

### 视觉验证

| 工具 | 功能 |
|------|------|
| `take_screenshot` | 截取 Game View（自动补齐 Camera/EventSystem；临时强制隐藏 CanvasGroup / Animator / 零 scale 可见，避免为截图改 Prefab） |
| `analyze_screenshot` | 将截图发送给 Vision 模型分析布局问题（支持 `referenceImagePath` 与设计稿并排对比；`success=true` 仅表示已送达） |
| `report_visual_verdict` | **结构化裁定**：对最新截图提交 matches / visibleElements(bbox) / missingElements / mismatches；像素抽样校验可见声明；工厂默认（空文案、统一字号、未贴图按钮、未接线 InputField、无选中态 Tab）与图标无 Sprite 占位时拒绝 matches=true |

> 设计稿建造任务结束前必须完成同一张截图上的 `take_screenshot` → `analyze_screenshot` → `report_visual_verdict` 闭环；任意改场景会使当前截图失效，必须重截；不匹配时仅定点修复后重跑该链（最多 3 轮）。有设计稿时 `create_batch` 在完成 `map_design_rect(_batch)` 测量前会被拒绝。

### 辅助工具

| 工具 | 功能 |
|------|------|
| `update_progress` | 记录进度（已完成/剩余/问题），帮助长任务保持方向 |
| `execute_code` | 动态编译执行 C# 代码（复杂批量操作兜底；破坏性删除与摄像机调试会被拒绝） |

---

## ⚙️ Configuration

### Editor 设置（Settings 面板 · 左侧）

保存在 `UIPrefabBuilder/Settings.asset`（Editor Preferences），分为 **LLM Configuration** 和 **Agent Configuration** 两组：

#### LLM Configuration

| 设置项 | 说明 | 默认值 |
|--------|------|--------|
| Base URL | OpenAI 兼容 API 端点 | `https://api.openai.com/v1` |
| Model | 模型标识符 | `gpt-4o-mini` |
| API Key | API 密钥（`SecureKeyStore` 加密存储） | — |
| Temperature | 采样温度（-1 = 使用模型默认） | -1 |
| Supports Vision | 是否启用截图视觉分析 | `false` |

#### Agent Configuration

| 设置项 | 说明 | 默认值 |
|--------|------|--------|
| Timeout (s) | LLM 请求超时秒数 | 60 |
| Max Steps | Agent 最大步数（0 = 无限制，硬上限 500） | 0 |
| Max Retries | LLM 请求失败重试次数 | 2 |
| Exec Timeout (s) | 工具执行超时秒数 | 5 |
| Auto Execute | 是否自动执行 | `true` |
| Extended Thinking | 是否启用扩展思考（Claude 系列） | `false` |
| Thinking Budget | Extended Thinking token 预算 | 4096 |
| Visual Verification | 是否启用 Phase 3 视觉验证流程（依赖 Supports Vision；探测失败可自动关闭，成功可恢复；用户手动开关不被覆盖） | `true` |

#### Asset Indexing（Settings 面板 · 左侧）

| 设置项 | 说明 | 默认值 |
|--------|------|--------|
| Enable Asset Indexing | 启用 CLIP 视觉资源索引 | `false` |
| Index Directories | 参与索引的 Sprite 目录列表 | `Assets/Sprites` |
| Ignore Patterns | 忽略的文件名模式 | — |
| Min Confidence | 语义搜图最低置信度阈值 | 15 |
| Embedding Model Path | CLIP ONNX 模型路径 | 包内默认路径 |

依赖项（Settings 面板内一键管理）：
- **Unity Sentis** — CLIP 推理运行时（`com.unity.sentis`）
- **CLIP Vision Model** — 图像 embedding（`clip_vit_visual.onnx`）
- **CLIP Text Model** — 文本 embedding + vocab（`clip_vit_textual.onnx`）

### 项目配置（Project Config 面板 · 右侧）

保存在 `ProjectSettings/UIPrefabBuilderConfig.json`，支持 Save / Export / Import / Reset。

| 配置项 | 说明 | 对 Agent 的影响 |
|--------|------|----------------|
| Orientation | 横屏 / 竖屏 | 注入设计规范到 Prompt |
| Design Resolution | 设计分辨率（如 1920×1080） | 创建 Canvas 时自动设置 CanvasScaler |
| Screen Match Mode | CanvasScaler 匹配模式（MatchWidthOrHeight / Expand / Shrink） | 创建 Canvas 时自动应用 |
| Match W/H | CanvasScaler matchWidthOrHeight（仅 MatchWidthOrHeight 模式） | 创建 Canvas 时自动应用 |
| Project Notes | 项目备注 | 注入 Prompt，Agent 遵循项目约定 |
| Sprite Mapping | 角色 → Sprite 路径 + Image Type | Agent 优先直接使用映射路径，跳过搜索 |
| Prefab Mapping | 角色 → Prefab 路径 | Agent 优先 `instantiate_prefab` 而非从零创建 |
| Component Overrides | Text/Button/Image/InputField + 自定义组件 | UICreator 创建时使用指定组件（如 TMP） |
| Rules | 项目级约定规则列表 | 注入 Prompt 且**优先于** `map_design_rect` 尺寸与后续视觉抛光；终裁前须复核 |

配置示例：

```json
{
  "basicInfo": {
    "orientation": "Landscape",
    "designWidth": 1920,
    "designHeight": 1080,
    "screenMatchMode": "MatchWidthOrHeight",
    "canvasMatchMode": 0.5,
    "projectNotes": "Use Sliced for dialog backgrounds"
  },
  "spriteMapping": [
    {
      "role": "confirm_button",
      "assetPath": "Assets/Sprites/UI/btn_green_lg.png",
      "imageType": "Sliced"
    }
  ],
  "prefabMapping": [
    {
      "role": "standard_button",
      "prefabPath": "Assets/Prefabs/UI/MyButton.prefab"
    }
  ],
  "componentOverrides": {
    "textComponent": "TMPro.TextMeshProUGUI",
    "customComponents": [
      { "role": "badge", "typeName": "MyProject.BadgeView", "description": "角标组件" }
    ]
  },
  "rules": [
    "所有弹窗背景使用 Sliced 模式",
    "按钮间距统一为 16px"
  ]
}
```

---

## 🔧 Extending

### 添加自定义工具

1. 在 `Editor/Skills/ToolDefinitions.cs` 中添加工具定义（OpenAI function schema）：

```csharp
Def("my_tool",
    "Description of what this tool does.",
    Props(
        P("param1", "string", "Parameter description", true),
        P("param2", "number", "Optional param", false, 42)
    )),
```

2. 在 `Editor/Skills/ToolExecutor.cs` 的 `ExecuteInternal` 中添加 case 分支：

```csharp
case "my_tool": return DoMyTool(args);
```

3. 实现执行方法（可复用 `Skills/` 下的 Helper 类）：

```csharp
private string DoMyTool(JObject args)
{
    var param1 = Str(args, "param1");
    // 你的逻辑...
    return Ok("Tool executed successfully.");
}
```

4. 调用 `ToolRegistry.Rebuild()` 或通过重启窗口刷新工具列表。

### 通用组件反射

无需写新工具即可操作任意组件属性：

```
set_component_property(target="MyObj", componentType="Image", propertyName="fillAmount", value="0.75")
set_component_property(target="MyObj", componentType="ScrollRect", propertyName="elasticity", value="0.5")
```

### execute_code 兜底

当内置工具无法满足需求时，`execute_code` 通过 `Compiler/DynamicCompiler` 动态编译执行 C#。支持自动包装（只写语句体）和完整 `IAgentAction` 类两种模式，编译在 `Async/BackgroundWorker` 中异步执行。

### 目标解析

工具的 `target` 参数支持三种定位方式：
- 按名称：`"Title"`
- 按路径：`"Canvas/Panel/Title"`
- 按 InstanceID：`"#12345"`

---

## 🏗️ Technical Highlights

<table>
<tr>
<td width="50%">

### 📐 项目级配置注入

ProjectConfig 贯穿 Agent 全流程：
- `AgentEngine.RebuildSystemPrompt()` 自动注入 Sprite/Prefab 映射、组件约定和 Rules
- `get_project_config` 工具供 Agent 运行时查询
- `UICreator` 创建 Canvas/UI 时直接应用配置
- 配置文件可 Export/Import，团队共享设计规范

</td>
<td width="50%">

### 🖼️ 设计稿还原工作流

上传设计稿后 Agent 自动执行：
- 分析 UI 层级结构（面板、按钮、图标、背景）；高度相关 Prefab 优先 `instantiate_prefab` 后就地调整
- `map_design_rect(_batch)` 将设计稿 bbox 换算为 Canvas 坐标，禁止目测估位
- `match_sprite_by_region_batch` 用设计稿归一化 bbox 做 CLIP 图对图匹配（优先于纯文字），低置信度回退纯色占位
- `crop_design_image` 预览裁剪区域确认坐标准确
- `create_batch` + `set_rect_transform_batch` 批量构建；Sprite 加载失败会返回可诊断错误
- Tab / Toggle / InputField 必须用专用创建工具还原真实交互结构，禁止 Image+Text 冒充
- `take_screenshot` → `analyze_screenshot` → `report_visual_verdict` 闭环验证（像素抽样 + 工厂默认/结构校验 + 占位色块拒绝）；改场景后强制重截
- Visual Fidelity Checklist 检查背景层、Tab 选中态、文字描边/字号、InputField 接线等
- PROJECT RULES 覆盖目测尺寸；纯分析请求不会被强行拉入建造流程，但仍会调用搜索工具做真实素材匹配

</td>
</tr>
<tr>
<td>

### 🔍 CLIP 视觉索引

超越文件名搜索的语义资源匹配：
- `search_sprites_by_text` / `search_sprites_by_text_batch` — 按自然语言描述匹配 Sprite
- `match_sprite_by_region` / `match_sprite_by_region_batch` — 直接给设计稿归一化 bbox，服务端自动裁剪并做真正的图对图 CLIP 相似度匹配
- `crop_design_image` — 预览裁剪区域，确认坐标再匹配
- 低置信度结果自动标记 `lowConfidence: true`，避免静默使用视觉不符的贴图
- Settings 面板一键安装 Sentis + 下载模型 + 构建索引

</td>
<td>

### ⚡ Batch-First 原则

Agent 遵循"批量优先"策略最小化工具调用轮次：
- `create_batch` 一次调用创建整棵 UI 树
- `set_rect_transform_batch` 一次定位所有元素
- `set_image_batch` / `set_text_properties_batch` 批量样式设置
- `match_sprite_by_region_batch` / `search_sprites_by_text_batch` 批量视觉/语义搜图
- `destroy_object_batch` / `inspect_hierarchy_batch` / `inspect_components_batch` 批量管理与查询
- 减少 LLM 往返次数，任务执行更快更稳定

</td>
</tr>
<tr>
<td>

### 📸 Vision 自检循环

支持 Vision 的模型可以"看见"自己的作品：
- `take_screenshot` 截取 Game View（自动补齐 Camera/EventSystem，临时强制隐藏 UI 可见）
- `analyze_screenshot` 多模态分析（仅表示截图已送达）
- `report_visual_verdict` 结构化裁定 + 像素抽样 + 工厂默认/交互结构校验
- 场景变更会使截图失效，分析/裁定会拒绝过期 capture
- 不匹配 → 仅定点修复（禁止删掉重建、禁止回退已修好的节点）→ 重跑验证链（最多 3 轮）

</td>
<td>

### 🧠 上下文压缩

长任务不会 token 溢出：
- 工具结果自动截断（>2000 字符）
- 视觉搜索结果保留全部 region，只裁剪 top matches
- 历史消息超过 50 条时自动摘要化旧的 tool 结果
- 资源列表 >20 条自动裁剪；首条用户消息和最近 18 条受保护

</td>
</tr>
</table>

---

## 📦 Dependencies

| 包 | 版本 | 用途 |
|----|------|------|
| `com.unity.nuget.newtonsoft-json` | 3.0.2 | JSON 序列化（Tool Calling / 会话 / 项目配置） |
| `com.unity.ugui` | — | Unity UI 组件 |
| `com.unity.sentis` | 1.3+ | CLIP 推理运行时（视觉索引，可选，Settings 面板一键安装） |
| TextMeshPro | ≥3.0 | 文本组件（可选，自动检测或通过 Component Overrides 指定） |

---

## 📋 Roadmap

- [x] 多模态支持（截图 → Vision 分析 → 自动修复）
- [x] 设计稿上传与还原（附加图片 → 结构分析 → 资源匹配 → 对比验证）
- [x] CLIP 视觉资源索引与语义搜图
- [x] 批量创建 / 批量布局 / 批量样式工具
- [x] 通用组件反射引擎
- [x] 上下文压缩（长任务 token 管理）
- [x] 项目级约束配置（设计规范、Sprite/Prefab 映射、组件覆盖、Rules）
- [ ] Prefab 模板库 & 常用 UI 一键生成
- [ ] 自定义工具插件化注册
- [ ] 动画 / 事件绑定工具
- [ ] MCP Server 模式（从外部 IDE 调用）
- [ ] 单元测试覆盖

---

## 📄 License

MIT License - 详见 [LICENSE](LICENSE)

---

<p align="center">
  <sub>Built with ❤️ by <strong>Indey</strong> | Powered by LLM Tool Calling + Unity Editor</sub>
</p>

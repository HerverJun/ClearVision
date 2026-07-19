---
name: clearvision-studio-ui-design
description: ClearVision Studio 专属 UI 设计、审查与改造规则。用于 Studio UI Next 的页面、工作区、流程画布、属性、预览、ROI、工程、检测结果、工作站、设备设置、AI、导航、状态与中文文案；要求以 codex初稿 的真实功能和操作语义为基线，遵守 Quiet Precision、工业高信息密度、简体中文优先、1920×1080 与 Windows 125% 缩放效率，以及既有后端权威和单一 Owner 架构边界。设计、评审、视觉精修或实现 ClearVision Studio 前端时优先使用。
---

# ClearVision Studio UI Design

把 ClearVision Studio 视为 WinForms + WebView2 承载的工业视觉工程工具，而不是营销网站、普通 SaaS 后台或纯前端应用。让视觉升级提高理解速度和操作效率，不得缩减能力、改变业务权威或增加无意义步骤。

## 规则优先级

按以下顺序处理冲突：

1. 仓库根 `AGENTS.md` 的产品定位、权威边界、单一 Owner、保存、Runtime、Station、HostBridge 和 Canvas 红线。
2. 本 Skill 的旧版功能基线、中文体验、1080P 效率和 Quiet Precision 规则。
3. 当前代码、契约、Design System tokens 与阶段文档。
4. `$impeccable` 的通用视觉建议。
5. `$vue-best-practices` 的 Vue 实现规范和 `$web-design-guidelines` 的交互、响应式、可访问性审查。

通用 Skill 与本项目的功能完整性、中文体验、1080P 效率或 Quiet Precision 冲突时，采用本 Skill。不得借此覆盖 Vue 工程质量、键盘操作、对比度、语义结构和可访问性要求。

## 先取证，再判断

每次调用都重新读取当前代码；不要把本 Skill、旧截图、文件日期或历史 PASS 当成最新实现事实。

- 旧版功能与操作语义基线：`C:\Users\HerverJun\Desktop\ClearVision`
- 新版实现：`C:\Users\HerverJun\Desktop\ClearVision-UI-Next`
- 旧版正式前端：`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/`
- 新版前端：`ClearVision.Product/src/ClearVision.Product.Desktop/StudioUI/`
- 新版阶段与证据：`docs/进行中/StudioUINext/`
- Quiet Precision 取证：`docs/quiet-precision/`

优先读取与当前页面直接相关的文件。代码与文档冲突时，以当前代码和配置为准，并把文档漂移列为发现。不得以已废弃的 `FrontendV2/` 作为实现、目录或视觉地基。

## 产品与架构语境

保持以下事实：

- Studio 用于工程配置、调试、预览和正式运行控制；Runtime 与 Station 不是前端页面的替代物。
- Project、Flow、GlobalVariables、正式 assets、Inspection、Results、Runtime Package 和 Station 状态的权威在现有后端链路。
- Preview 是可丢弃调试投影，不等于 Formal Run 或正式结果。
- `PersistenceRevision` 是正式保存并发身份；本地 revision 只用于草稿与 stale 防护。
- Vue 只组合 UI；命令式 FlowCanvas、ImageCanvas、EventSource、AbortController 和 WebView2 bridge 由窄 adapter/owner 管理并负责 dispose。
- 同一 capability 只能有一个 mounted owner、一个订阅集合和一个写入口。隐藏 DOM 不等于卸载。
- 复用唯一 API transport、Host adapter、Canvas 内核和保存链。发现契约缺口时先报告，不自行新增第二套权威。

## 旧版核心能力基线

不要复制旧版视觉，但要核对下列真实能力和操作语义是否被保留、重定位、明确延后或意外丢失。

### 主框架与高频操作

- 主导航包含工程、流程、检测、追溯、监控、AI、设置。
- 全局高频操作包含最终判定、保存、运行、全局变量、主题、会话和状态反馈。
- 底部状态提供用户、就绪/运行信息、资源/FPS、当前工程和版本等持续上下文。
- 视图切换、权限限制、未保存变更、初始化失败和会话失效有明确反馈。

### 工程生命周期

- 列表、搜索、最近工程、创建标准/示例工程、打开、关闭、删除。
- 正式保存、未保存检测、本机草稿提醒、冲突/失败反馈。
- 导入、导出、运行包导出以及相应权限和确认流程。
- 工程切换前处理当前草稿，不能静默丢失或把本地缓存冒充正式保存。

### 流程画布与算子库

- 算子分组入口、搜索/选择、点击或拖拽添加算子。
- 节点选择、移动、删除、复制等编辑操作；端口、类型兼容、连线校验和环路防护。
- 缩放、平移、视图恢复、子图导航、Lint/DryRun 警告和快捷键。
- Canonical FlowCanvas、序列化、连接和 pointer 语义必须继续复用，不另造画布内核。

### 属性与参数编辑

- 显示算子身份、生命周期、输入输出端口、连接摘要和参数分组。
- 支持文本、数字、滑块、选择、布尔、路径、相机绑定、全局变量绑定、校验范围和必填状态。
- 保留依赖参数、只读参数、特殊编辑器、标定/测量类工作台和错误定位能力。
- 不得为了界面简洁把关键参数隐藏到难以发现的深层弹窗。

### 图像、预览、ROI 与结果

- 输入/输出图像、缩放、平移、适应窗口、实际大小、像素/视图信息和 overlay。
- ROI/几何编辑、拖拽调整、图像边界约束、撤销/重做和参数同步。
- 节点手动/自动预览、结构化输出、场景、artifact、诊断、原始数据和 stale/error 状态。
- 预览、正式运行与历史结果必须在文案和状态上严格区分。

### 检测、追溯与反馈

- 单次运行、连续运行、停止、相机/工程前置条件和运行进度。
- 缺料超时、连续 NG 等运行保护必须解释原因和后续检查方向，不能只显示“失败”。
- OK、NG、执行失败、未判定等结果，以及统计、最近结果和查看全部结果入口。
- 历史筛选、分页、KPI、趋势/缺陷/吞吐、详情、诊断、证据、对比、导出和实时更新等能力按当前合同核对。

### 设备、AI、Station 与系统设置

- 相机发现、绑定、参数、触发来源、连续/单帧预览和标定入口。
- PLC 协议与连接测试、TCP Client/Server Profile 与收发调试。
- Station 通讯配置、token 管理、在线/离线/告警、健康、日志、命令、运行包和结果工作台。
- AI 模型配置、测试，以及 AI 工程生成、澄清、计划、Build、Apply/Undo/恢复等现有流程。
- 通用设置覆盖软件外观、文件存储、数据库、运行时、用户与安全策略，并提供清晰的保存范围。

## Studio UI Next 当前承载方式

检查当前代码，不根据路线图猜测。当前主要结构包括：

- Product Shell、Router、Auth/Session、Leave Guard、Overview、Projects、Operators、Results、Diagnostics、About。
- 受 profile 控制的 Stations 只读页面。
- Project Workspace 内的 canonical FlowCanvas、Operator Rail、Inspector、ImageViewport/ROI、Preview、Persistence、Formal Run/Stop/Reconcile。
- 唯一 API transport、Host adapter、query owner、workspace owner 和 capability-local lifecycle owner。
- Design System tokens、primitives、patterns，以及 light/dark、compact/comfortable 投影。

审查时明确标注旧版能力在 Next 中属于：`已等价保留`、`已优化保留`、`已重定位`、`只读接受`、`按 profile 隐藏`、`明确延后`、`缺失/回归`。不要因为路由、按钮或漂亮空状态存在就判定功能完整；必须验证参数、状态、错误、权限、写入和操作步骤。

## Quiet Precision 的具体定义

追求高级、克制、精密的苹果式产品体验，但不要模仿苹果官网或把 Studio 做成消费级展示页。

- 以清晰层级、准确对齐、稳定比例、细致排版和低噪声表面建立高级感。
- 使用 Windows 原生/系统型无衬线字体栈，保持中文、数字和单位清晰；产品标签不用展示字体。
- 使用中性 graphite 表面区分 app、page、raised、floating；品牌色不染大面积背景。
- 品牌强调、技术信息、OK、NG、Warning、Info、Idle 使用不同语义 token。
- 圆角服从现有 tokens，主要用于控件、浮层和真正独立的容器；连续工作区优先使用对齐、背景层级和单向分隔线。
- 阴影只表示真实 elevation；浮层和 modal 可用克制阴影，普通面板不用“描边 + 大软阴影”。
- 动效用于状态变化、反馈、展开和焦点连续性；保持快速、平稳，并支持 reduced motion。不要编排页面入场动画。
- 每个局部操作组只保留一个明确主操作。危险操作与普通品牌强调分离。
- 让 Canvas、图像、数据和控制本身成为视觉主体，不添加无业务意义的装饰。

禁止：

- 大面积无效留白、巨型标题、宽幅 hero、营销式口号或装饰性指标卡。
- 到处使用毛玻璃、渐变、发光、霓虹、科技网格和动画背景。
- 卡片套卡片、每块内容都加完整边框、圆角和阴影。
- 用隐藏信息代替层级设计，或把常用操作塞进多层菜单。
- 用蓝、绿、红制造“科技感”，或让正常/离线/NG/执行错误共享模糊颜色。
- 模仿 macOS 窗口控件、消费级大胶囊和过度柔软的移动端样式。

## 简体中文优先

当前只优化简体中文，不为未来多语言预留无效空间，也不按英文排版习惯牺牲中文效率。

- 统一术语：优先使用“工程、流程、算子、属性检查器、预览、感兴趣区域（ROI）、工作站、检测结果、正式运行、保存、未配置、未判定、离线”。
- 面向用户的界面不要混用 `Run`、`Stop`、`Reconcile`、`draft`、`owner`、`stale` 等英文；确有诊断价值时提供中文主标签，技术词放次级说明。
- 标题短而具体；字段名表达对象，按钮表达动作，提示说明原因与下一步。
- 长中文字段优先给合理宽度、换行或 tooltip；不要无理由截断关键设备名、工程名、参数名和错误原因。
- 紧凑界面仍保持中文可读行高。说明文字不得挤压 Canvas、Preview、Inspector 或数据表。
- 错误和警告采用“发生了什么 → 影响什么 → 用户下一步做什么”；保留诊断码但不让诊断码代替中文解释。
- 同一概念在导航、标题、按钮、状态、日志和帮助中保持同名。区分“工程”与代码层 `Project`、“工作站”与 `Station`，不要随意切换。
- 按钮文案直接、可预测；避免“确认”“处理”“执行”等缺少对象的泛化动词。

## 1920×1080 与 Windows 125% 规则

把 `1920×1080 / Windows 100% 与 125% / WebView2 桌面窗口` 作为主要环境。不要只看 4K 或浏览器全屏截图。

- 同时检查真实 1920×1080 WebView2 与 125% 系统缩放。浏览器 DPR 或 `force-device-scale-factor` 只能作为分层证据，不能冒充真实 Windows DPI。
- 把 125% 下较小的有效逻辑工作区和 Windows 标题栏占用计入空间预算；使用 1366×768、1350×704 或相近 client size 作为短屏压力检查。
- Workspace 路由不要再叠加大 page hero。新增固定 chrome 不得无证据超过现有 Shell top bar、Workspace toolbar 和 status bar 的基线。
- 首屏保持保存、正式运行/停止、核心状态、算子入口、Canvas、Inspector 和 Preview 可达；低频说明不能占据工作区中心。
- Canvas 是主工作区；Inspector 和 Preview 必须有可用尺寸，并支持 splitter、折叠或合理收缩。不要把 Canvas 压成装饰区。
- Preview 可收缩为状态条；高级参数可折叠；高频参数和当前错误保持可见。
- 避免页面滚动嵌套面板滚动再嵌套表格滚动。每个轴明确唯一滚动 owner。
- 避免水平滚动；表格优先固定关键列、压缩次要列、提供详情或受控列隐藏，不要截掉主标识和状态。
- Modal、菜单和 popover 必须留在可视范围；长内容使用受控 body 滚动和可见的操作区。
- 在 compact 与 comfortable 两种 density 下分别检查；comfortable 只增加适度呼吸，不得制造大片空白。

## 高信息密度而不拥挤

- 先确定主任务和主工作区，再安排辅助区域。
- 高频功能保持可见，低频高级功能按真实使用频率折叠。
- 用列对齐、基线、分组标题、间距节奏、表面层级和字重管理复杂度。
- 让持续列表、表格、描述列表和工具条承担密集信息；不要把每条记录改成独立大卡片。
- 状态和单位靠近数据；编辑控件靠近对象；错误靠近触发问题的字段或工作区。
- 任何“更简洁”方案都要检查是否增加点击、来回切页、记忆负担或上下文丢失。

## 状态必须真实

至少区分：正常、运行中、等待、成功、警告、错误、NG、未判定、未配置、只读、冲突、结果未知、过期、离线、禁用。

- 不从文案或颜色猜测业务状态；使用后端合同和既有投影。
- 区分执行状态与判定结果，例如“执行成功但判定 NG”和“执行失败”。
- 区分 Preview、Formal Run、历史结果和 Station 上报。
- 区分未配置、无数据、加载中、请求取消、旧数据、无权限和服务离线。
- 对 unknown outcome、save conflict、401、运行中离开和 destructive action 保留现有 reconcile、Leave Guard 和确认语义。
- 颜色只强化状态，必须同时有中文标签、图标/形状或结构线索，不能只靠颜色。

## 强制工作流

1. 用一句话写出当前页面的核心任务、主要用户和高频动作。
2. 在旧版代码中找到对应 capability、入口、状态、参数、错误和完整操作路径。
3. 在 Next 当前代码中找到承载页面、owner、合同、feature flag、路由和 Design System 组件。
4. 建立功能对照，逐项标记保留、优化、重定位、只读、隐藏、延后或缺失。
5. 分别从中文体验、1080P/125% 空间、信息密度、状态真实性和可访问性检查。
6. 把发现分类为功能、布局、交互、文案/术语、状态、纯视觉或架构边界；不要混写。
7. 按优先级输出可执行建议，并说明证据、用户影响、具体改法和验证方式。
8. 若实施 Vue 代码，使用 `$vue-best-practices`；若精修视觉，使用 `$impeccable`；最终用 `$web-design-guidelines` 做交互和可访问性复核。
9. 完成后必须在真实浏览器或 WebView2 中截图复审。至少覆盖目标页面的 1920×1080、Windows 125% 或等价真实 WebView2 证据、compact/comfortable，以及关键运行/错误/空/长中文状态。

## 优先级与输出格式

使用以下优先级：

- `P0`：可能造成数据丢失、错误执行、绕过权威、危险状态误判或核心任务不可完成。
- `P1`：旧版核心能力缺失/难找、步骤明显增加、1080P 首屏受阻、状态或中文语义会误导操作。
- `P2`：密度、层级、截断、滚动、键盘、对比度或一致性明显影响效率。
- `P3`：不影响任务完成的视觉精修。

每条建议使用：

```text
[优先级] 问题标题
类型：功能 / 布局 / 交互 / 文案 / 状态 / 视觉 / 架构
证据：旧版能力 + Next 当前代码或截图
影响：对中文用户、1080P 或操作效率的具体影响
建议：可直接实现的调整，不使用空泛形容词
验证：需要检查的尺寸、状态、交互和截图
```

先列功能完整性和任务效率，再列视觉问题。禁止只输出“增加留白、统一圆角、优化阴影”。如果没有检查旧版对应能力或没有说明 1080P/中文影响，不得给出最终设计结论。

## 最终自检

- 旧版对应功能、入口、参数、状态、错误和操作步骤是否全部核对？
- 新版是否更漂亮但步骤更多、入口更深或反馈更弱？
- 中文字段、按钮、状态、错误和长文本是否清楚且术语统一？
- 1920×1080 与 Windows 125% 下，Canvas、Preview、Inspector 和高频操作是否可用？
- 是否出现双层滚动、水平滚动、超屏浮层或首屏数据过晚？
- 品牌色与 OK/NG/Warning/Info/Idle 是否分离？
- 是否保留后端权威、唯一 owner、保存链、Canvas 和 HostBridge 边界？
- 是否通过真实浏览器或 WebView2 截图复审，并诚实标注未运行的证据？

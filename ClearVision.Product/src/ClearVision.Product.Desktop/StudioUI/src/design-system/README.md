# ClearVision Design System V1.1

Design System V1 服务正式 Studio 产品页面与隔离 Labs。它不导入 legacy CSS，不创建第二产品 Shell，
也不承载 Project、Flow、Session、Inspection 或 Station authority。

## Quiet Precision / 静谧精密

- app、page、raised、floating 使用中性灰白/石墨表面，不以蓝灰染大面积背景；
- 丹红只用于品牌、导航选中与关键意图；普通系统事实、链接和焦点使用技术蓝；
- brand、OK、NG、warning、info、idle 与 Canvas 技术蓝使用独立 token；
- 正式产品默认 compact，允许持久化切换 compact/comfortable；
- light/dark 都必须维持相同的信息层级、焦点与状态语义；
- 常规页面优先排版、间距、背景差和单向分割线，避免 Panel、Table 与内部卡片重复整圈描边；
- 阴影保持克制，只用于 raised、floating、modal 与显式 elevated 场景。

## 排版层级

- Display：内部视觉样本和特殊产品身份；
- Page Title：正式路由唯一 `h1`；
- Section Title：Panel 和工作区分区；
- Body / Secondary / Caption：按信息权重递减，不依靠大量粗体制造层级；
- Numeric：使用 tabular lining numbers，稳定呈现计数、时间、耗时、版本与状态值。

字体栈只使用 Windows/系统可用字体，不下载外部字体或在线资源。普通交互动效为 140–200ms，
不使用弹跳和大幅位移，并由系统偏好及根 projection 同时尊重 reduced motion。

## Public API

正式页面统一从 `@/design-system` 导入。根出口转发：

- `primitives`：Button、Field、Select、DataTable、SearchField、Pagination、DescriptionList、
  InlineAlert、Modal、Toast、Splitter 等；
- `patterns`：PageHeader、Breadcrumbs、Toolbar、PageState；
- `icons`：仅包含当前产品与 patterns 实际使用的无依赖 SVG 图标。

patterns 只负责呈现和交互语义，不识别 HTTP、query、permission 或业务 DTO。Loading、Empty、Error、
Unauthorized、Forbidden 与 Not Found 由 `CvPageState` 统一呈现；Stale 与 Partial Failure 使用
`CvInlineAlert`，并由 capability 保留 previous data。

## 中文产品文案规范

- 正式导航、标题、字段、空状态、错误、按钮与 ARIA label 使用简洁中文；
- 一条文案只表达一个结果或下一步，不使用 Demo、Prompt、placeholder 等开发语气；
- Unauthorized 明确说明“需要预置会话”，不伪造登录 handoff；
- Forbidden 与网络故障分开表述，403 不写成“服务不可用”；
- Stale/Partial Failure 明确说明正在显示上次数据；
- 危险、NG、品牌丹红和技术 info 不互相借用语义；
- API path、schemaVersion、hostKind、PersistenceRevision 等协议名可保留英文；
- Labs 可保留必要的技术英文，但必须标明内部实验室，不进入正式导航。

## 可访问性与生命周期

- 原生 table、caption、heading、nav、dl 和 button 语义优先；
- icon-only 控件必须有可读 label；
- keyboard focus 使用统一 focus-visible token；
- Product Shell 提供 skip link、唯一 `main`，正式路由切换后把焦点恢复到主要内容；
- 混合筛选控件使用 group 与自然 Tab，纯按钮 toolbar 才使用方向键模型；
- 静态 Empty、Unauthorized、Forbidden 与 404 不创建多余 live region；
- Modal、Toast、Splitter、Toolbar 等 transient listener/timer 由 mounted owner 清理；
- reduced-motion 由系统偏好和根 projection 共同控制；
- 1366×768 不允许全局横向滚动，表格需要时只在局部容器滚动。

## 边界

Design System 不读取 API、不持有 query state、不访问 localStorage、不决定 route permission，也不创建
App Shell。tokens、根 public exports 和本 README 由主协调 owner 维护；capability 只组合公开组件。

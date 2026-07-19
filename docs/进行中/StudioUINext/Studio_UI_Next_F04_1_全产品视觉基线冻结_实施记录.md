# Studio UI Next F04.1 第一阶段：全产品视觉基线冻结实施记录

> 实施日期：2026-07-20（Asia/Shanghai）
> 工作分支：`studio-ui-next`
> 用户给定视觉基线：`04f9620c44643e9f6f26b98366f44c297b759064`
> 实际实施起点：`0880cc727b7922f497381d971973b0212c502842`（包含上述基线及其后的 Operator Rail 高度修复）
> 视觉参考：当前流程工作台与 Stitch `screen.png`；`code.html` 仅用于尺寸和样式取证
> 产品视觉确认：`PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER`

## 1. 真实路由审计

本轮从当前 `router.ts`、Product Shell、Design System 和真实页面实现重新取证，没有使用废弃的 `FrontendV2/`。

| 路由组 | 真实页面 | 本轮处理 |
| --- | --- | --- |
| 认证 | Setup、Login、Change Password | 统一丹红雪花品牌、表面、控件和焦点；不改认证合同 |
| 产品首页 | Overview | 接入唯一顶部 Product Shell 与统一页面容器 |
| 工程 | Projects、Project Detail | 保留列表、详情、创建、打开、删除和保存语义；只统一公共视觉 |
| 流程 | Project Workspace | 保留当前 Stitch 方向、Canvas/Inspector/Preview Owner 与内核；只接收公共 token/status 修正 |
| 算子 | Operators、Operator Detail | 接入唯一顶部 Product Shell；不改只读合同 |
| 追溯 | Results | 保留现有筛选、表格、详情和双轴结果；统一表面、控件和执行错误色 |
| 监控 | Stations、Station Detail | 继续受 `Studio2.StationsRead` profile 控制；统一表面、控件和九类结果色 |
| 系统 | Diagnostics、About、Forbidden、Not Found | 接入唯一顶部 Product Shell 与统一状态组件 |
| 内部实验室 | Design Lab、Canvas Lab | 继续隔离，不进入正式产品导航，不作为产品视觉截图 |

## 2. 审计发现

1. 普通产品路由使用 `208px` 深色侧栏、`CV` 方块品牌和面包屑顶栏；流程工作台使用丹红雪花、顶部产品导航和白色工作台 chrome，形成两套明显产品视觉。
2. 业务页面虽然广泛复用 `CvPageHeader`、`CvPanel`、`CvButton`、`CvField`、`CvSelect`、`CvDataTable` 和 `CvPageState`，但默认 Panel 同时使用完整描边与宽软阴影，连续页面表面偏向普通 SaaS 卡片。
3. `CvInlineAlert` 使用 2px 彩色侧边条；Modal/Toast 同时使用描边与宽阴影；全局滚动条没有统一 token。
4. NG 与执行失败在结果投影中共用 `ng` tone，违背“判定结果”和“执行错误”双轴分色要求。
5. 业务页仍有实现合同型说明、原始 ISO 时间和局部卡片化，但本阶段不重组页面信息架构，只记录为后续范围。

## 3. 冻结后的公共视觉基线

### 3.1 唯一 Product Shell

- 所有正式产品路由复用同一个 Product Shell，不再挂载普通页面专属侧栏。
- 左侧固定使用 `CvBrand` 丹红雪花与 ClearVision 字标；窄宽度只收缩字标，不创建替代品牌。
- 顶部产品导航固定为“工程、流程、检测、追溯、监控、AI、设置”；未开放 capability 保持 disabled，并保留原因 tooltip，不开启假能力。
- 概览、算子库和关于保留在“更多”产品入口；角色与 profile 继续决定诊断和工作站入口。
- 普通页面与 Workspace 只切换几何密度，不切换 Shell owner、订阅或状态树。

### 3.2 Tokens 与状态语义

- 丹红：`#B6453C`；hover：`#9F3932`。
- 工业蓝：`#47738F`；Canvas 连线蓝：`#3F718E`。
- OK：`#16866F`；NG：`#D12F3F`；执行错误：`#B85B16`。
- warning、info、idle 与 destructive 均使用独立 token；删除操作不再借用 NG token。
- 统一 app/page/raised/floating/sunken 表面、控件边界、focus ring、低噪声阴影和全局窄滚动条。

### 3.3 公共组件

- `CvPageHeader`：统一页面标题区、丹红上下文标签、标题/说明排版和底部分隔。
- `CvPanel` / `CvSurface`：默认使用 tonal layer + 低对比描边；普通 Panel 不再默认叠加宽阴影。
- `CvButton` / `CvIconButton`：品牌主按钮、工业蓝次按钮、独立 destructive 按钮与统一交互反馈。
- `CvField` / `CvSelect` / `CvSearchField`：统一高度、边界、hover/focus、placeholder 与 Windows dark-mode 表面。
- `CvDataTable` / `CvDescriptionList` / `CvPagination`：统一表头、行高、淡蓝交替行、局部滚动和数字对齐。
- `CvStatusBadge`：新增 `error` tone，NG 与执行失败不再混色。
- `CvPageState` / `CvInlineAlert` / `CvToastRegion`：统一空、加载、错误、warning 与瞬态反馈；取消装饰性彩色侧条。
- `CvModal`：保留焦点 trap/恢复和受控 body 滚动，以真实 elevation 表达浮层。

## 4. 明确保留到下一阶段的旧视觉区域

1. Workspace 左侧 Operator Rail 的深石墨工具面属于当前已确认工作台方向，不是旧 Product Shell 回归。
2. FlowCanvas 节点、端口、连线、Minimap 与命令式交互内核本轮不重写；仅保留当前视觉实现。
3. Inspector、Preview 和 ImageViewport 中仍存在少量局部硬编码白色/浅灰色，用于当前工作台精修结果；后续 dark theme 专项再收敛为局部 token。
4. Projects、Project Detail、Results、Stations 仍保留当前 Panel 分区与技术说明；本阶段只统一公共表面，不大规模改变信息架构或产品文案。
5. 完整检测、AI、设备设置、最终判定、全局变量、结果图片/证据/对比/导出等能力仍按现有 profile/路线延后，没有通过视觉入口伪造实现。

## 5. 验证与证据

- TypeScript / Vue 类型检查、ESLint、Vitest、Vite build 和相关 Playwright 产品路由回归均按最终提交重新运行。
- 产品截图统一写入 `.tmp/studio-ui-next/f04/f04-1-final/`，覆盖：
  - 流程工作台 1920×1080；
  - 工程页 1600×1000；
  - 结果页 1600×1000；
  - 监控页 1600×1000；
  - 流程工作台 1366×768 短屏。
- Browser fixture 只证明真实 Vue 产品路由、组件、Owner 和网络合同投影；不冒充真实 WebView2 或 Windows 125% 系统 DPI。
- `PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER` 保持不变，等待用户审阅最终截图。

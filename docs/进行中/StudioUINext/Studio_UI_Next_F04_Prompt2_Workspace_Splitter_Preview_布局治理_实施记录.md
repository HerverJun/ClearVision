# Studio UI Next F04 Prompt 2/5：Workspace Splitter 与 Preview 布局治理实施记录

> 实施日期：2026-07-19（Asia/Shanghai）
> 初始基线：`4c75223da2ac774e648a24200d4f2092bc44c49c`
> 权威输入：`Studio_UI_Next_F04_视觉审计与优化方案_PROPOSED.md`、Prompt 1 实施记录、`clearvision-studio-ui-design`、`impeccable`、`vue-best-practices`、`web-design-guidelines`
> 范围：Workspace 的 Canvas / Inspector 水平分配、Canvas / Preview 垂直分配、Preview 折叠恢复、窄屏覆盖层焦点与布局偏好；未修改业务 Owner、工程数据、保存、运行、API、后端或 Canvas 内核合同。

## 1. 修改前的核心问题

Prompt 1 已收敛 Product Shell、Workspace Chrome 和默认几何，但仍保留以下 P1 问题：

1. Inspector 固定为约 296px，用户无法在“搭流程”和“调参数”之间分配空间。
2. Preview 固定占用 160–220px，只有短屏 CSS 收缩，没有显式折叠、展开与恢复模型。
3. 已有 `CvSplitter` primitive 未进入正式 Workspace，且缺少反向面板几何、复位、值语义和拖动结束通知。
4. 窄屏 Inspector / Operator Rail 虽有恢复按钮，但打开后焦点、Escape 关闭和触发器回焦不完整。
5. 页面重新进入后无法恢复用户的布局工作模式。

本轮不以隐藏或卸载业务能力换取空间。FlowCanvas、Inspector、Preview、Image 与 ROI Owner 的数量和生命周期保持不变。

## 2. 最终交互模型

### 2.1 Canvas 与 Inspector

- Canvas 与 Inspector 之间使用正式纵向 `separator`，分隔带宽 8px。
- 向左拖动增加 Inspector 宽度，向右拖动减少宽度，符合右侧面板的空间方向。
- 默认宽度 296px；静态边界为 248–420px。
- 运行时最大值还会根据 Workspace 宽度动态收紧，始终为 Canvas 预留：
  - 宽布局至少 600px；
  - 紧凑宽布局至少 520px。
- 低于 980px 时 Inspector 转为右侧覆盖层，不显示失效的 splitter；“打开属性检查器”入口持续可见。

### 2.2 Canvas 与 Preview

- Canvas 与 Preview 之间使用正式横向 `separator`，分隔带高 8px。
- 向上拖动增加 Preview 高度，向下拖动减少高度，符合底部面板的空间方向。
- 默认高度 220px；静态边界为 160–420px。
- 运行时最大值根据 Workspace 高度动态收紧，始终保留 352px Canvas surface 预算，其中包含 32px 工具栏、至少 300px Canvas stage 和 20px 状态区。
- Preview 标题栏保留一个图标按钮：展开时提示“折叠预览区，为流程画布释放空间”，折叠时提示“展开预览区并恢复上次高度”。
- 折叠后 Preview 收为 38px 恢复条，splitter 隐藏；展开恢复到上次偏好高度，并再次受当前视口动态上限约束。
- Preview 组件和业务 Owner 在折叠时仍保持 mounted；仅内容体进入 `inert`、`aria-hidden` 和不可见状态，不中断 Preview / Image / ROI 合同。

### 2.3 键盘与焦点

两个 splitter 均支持：

| 操作 | 行为 |
| --- | --- |
| 方向键 | 按 8px 微调，并遵循右侧/底部面板的反向几何 |
| `Shift` + 方向键 | 按 32px 加速调整 |
| `Home` | 调至当前最小值 |
| `End` | 调至当前最大值 |
| `Enter` | 恢复默认值 |
| 双击 | 恢复默认值 |

splitter 使用 `role="separator"`、正确的 `aria-orientation`、`aria-valuemin/max/now` 和中文 `aria-valuetext`。键盘焦点只强调中央握柄，避免整条分隔带形成消费级高亮。

窄屏覆盖层打开后，焦点进入首个可操作控件；按 Escape 关闭后返回对应“打开算子区”或“打开属性检查器”按钮。打开一侧覆盖层会关闭另一侧，避免两个浮层同时压住 Canvas。

## 3. 尺寸边界与恢复策略

| 区域 | 最小值 | 默认值 | 静态最大值 | 动态保护 |
| --- | ---: | ---: | ---: | --- |
| Inspector | 248px | 296px | 420px | 为 Canvas 保留宽布局 600px、紧凑宽布局 520px |
| Preview | 160px | 220px | 420px | 为 Canvas surface 保留 352px |
| Preview 折叠态 | 38px | — | — | 始终保留标题、状态和恢复按钮 |

尺寸偏好保存在 `localStorage` 的 `clearvision.studio-ui.workspace-layout.v1` 中，只包含：

- `schemaVersion: 1`；
- Inspector 偏好宽度；
- Preview 偏好高度；
- Preview 折叠状态。

它是跨工程共享、可丢弃的 Studio UI 投影，不进入 Project、Flow、Workspace Snapshot 或后端持久化。拖动结束、键盘调整、复位、折叠切换与 Workspace dispose 时提交偏好。无效 JSON、缺失字段或存储失败均降级到稳定默认值，不影响 Workspace 启动。

偏好值和当前有效值分离：用户在大窗口选择的尺寸不会因进入短屏而被永久覆盖；返回大窗口后可恢复原偏好。`ResizeObserver` 负责动态夹取，Owner dispose 时明确断开 observer 或 fallback resize listener。

## 4. 中文体验与可访问性

- 用户提示统一使用“属性检查器”“预览区”“折叠”“展开”“恢复默认宽度/高度”等自然中文。
- splitter 的可访问值不是裸数字，而是“属性检查器宽度 296 像素”“预览区高度 220 像素”。
- tooltip 明确告知拖动、方向键、Shift、Home、End、Enter 和双击能力。
- Preview 折叠按钮为图标按钮，但具备完整中文 `aria-label`、`title`、`aria-controls` 和 `aria-expanded`。
- 覆盖层宿主和 splitter 均具有可见焦点，不依赖浏览器默认不一致的 focus ring。
- 未新增英文合同词，未改变现有业务枚举、API 字段或测试证据字段。

## 5. 目标视口布局矩阵

以下数据来自浅色主题 Browser fixture，DPR 1；数值为真实 CSS 几何，不等同于 Windows 系统 DPI 证据。

| 视口 | 密度 | Canvas stage | Inspector | Preview | Preview 当前最大值 | 全局溢出 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| 1920×1080 | compact | 1346×698 | 296 | 220 | 420 | H=0 / V=0 |
| 1920×1080 | comfortable | 1342×692 | 296 | 220 | 420 | H=0 / V=0 |
| 1350×704 | compact | 776×322 | 296 | 220 | 242 | H=0 / V=0 |
| 1350×704 | comfortable | 772×316 | 296 | 220 | 236 | H=0 / V=0 |

1920×1080 Inspector 边界状态：

| 状态 | Inspector | Canvas stage | Preview |
| --- | ---: | ---: | ---: |
| 最窄 | 248 | 1394×698 | 220 |
| 默认 | 296 | 1346×698 | 220 |
| 最宽 | 420 | 1222×698 | 220 |

Preview 交互证据还覆盖：

- 最小 160px、最大 420px、默认 220px；
- 折叠为 38px 后 Canvas 取得剩余垂直空间；
- 页面离开并重新进入 Workspace 后，Inspector 宽度、Preview 高度和折叠状态恢复；
- 恢复时 Owner count 仍为 1，没有复制 Preview 或 Canvas Owner。

1350×704 的默认 Preview 仍保持 220px，因为 Canvas stage 分别为 322px / 316px，满足审计要求的不低于 600×300；其动态最大值会被收紧至 242px / 236px，阻止拖动压垮 Canvas。

## 6. 实际修改范围

### Workspace 布局与共享 primitive

- `FlowWorkspace.vue`：正式接入两个 splitter、Preview 折叠态、动态 CSS 几何、窄屏覆盖层焦点与恢复入口。
- `workspaceLayoutOwner.ts`：单一 Workspace UI 布局偏好 Owner、动态边界、存储、恢复和 dispose。
- `flow/index.ts`：导出布局 Owner 与边界常量供单测复用。
- `CvSplitter.vue`：补充反向几何、中文值文本、Home/End/Enter、双击复位、Shift 加速、拖动状态与结束通知。
- `PreviewPanel.vue`：承载折叠/展开入口，折叠时保留 Owner 并隔离内容焦点。

### 验证与证据

- splitter unit：正向/反向 pointer、键盘边界、Enter/双击复位、值语义。
- layout Owner unit：动态夹取、Canvas 预算、偏好持久化、重新进入恢复、无效数据降级、observer dispose。
- Workspace E2E：Inspector/Preview 最小、最大、默认、pointer/键盘、Preview 折叠/恢复、路由重新进入、窄屏焦点与 Escape、四种视口/密度矩阵。
- visual evidence helper：记录两个 splitter、动态边界、折叠态和实际布局值。

## 7. 验证结果

- `npm run typecheck`：PASS。
- `npm run lint`：PASS，0 warning。
- `npm run test:unit`：PASS，76 files / 485 tests。
- 最终改动后的定向 unit：PASS，2 files / 6 tests。
- `npm run build`：PASS；保留既有 large chunk warning，本轮未新增拆包范围。
- `f03-workspace.spec.ts`：PASS，47/47；覆盖既有保存、正式运行、停止、核对、Preview、ROI、Owner 生命周期和性能回归，以及本轮布局治理。
- 最终性能样本：100/150 节点中位 128ms、最大 143ms；300/450 节点中位 131ms、最大 151ms。
- Browser visual evidence：console error 0、page error 0、全局水平/垂直 overflow 0。
- 最终截图与 JSON 元数据目录：`.tmp/studio-ui-next/f04/visual-prompt2-final/`。

Windows DPI 证据边界：

- 本机 `AppliedDPI=96`、`GetDpiForSystem=96`，当前真实系统缩放为 100%。
- Windows 125%：**NOT PERFORMED / 待人工验证**。
- 未使用浏览器缩放或 DPR 模拟冒充系统缩放。
- 本轮未新增真实 WebView2 125% 旅程；Browser fixture 只证明浏览器布局和交互回归。

## 8. 遗留问题与 Prompt 3 建议

### 仍未解决

- 真实 Windows WebView2 125%、跨显示器 100%/125% 移动仍需人工环境验证。
- 1350×704 下的极端长中文工程名、保存冲突长消息、运行长错误和 Preview 大量结构化结果仍需状态专项截图。
- Operator Rail 目录项、Flow toolbar 分组、Inspector 表单区块、Preview 图像/结果/ROI/诊断内部层级仍保留 Prompt 3 精修空间。
- 审计记录中的 P0 旧版能力缺口未在本轮补齐，也未通过布局治理掩盖。
- 覆盖层未引入 modal 遮罩或完整焦点陷阱；当前采用非模态工具窗模型。若后续长表单测试发现焦点越界造成严重误操作，再评估是否升级交互模型。

### Prompt 3 推荐顺序

适合进入 Prompt 3，但不应重新修改本轮的布局权威或业务 Owner：

1. 先精修 Operator Rail 的高密度目录层级、搜索和选中态，使 Canvas 左侧输入更像专业工具库。
2. 再治理 Flow toolbar 的命令分组、图标语义、禁用态与中文提示，不增加工具栏高度。
3. 顺序精修 Inspector section/header 和技术状态中文，验证 248px 最窄宽度下的长字段。
4. 最后治理 Preview 内部图像、结果、ROI、诊断和长错误的视觉主次与滚动 Owner。

可直接共享到后续页面的能力包括 `CvSplitter` 的键盘/反向/复位语义、布局 Owner 的“偏好值与有效值分离”策略、折叠内容的 `inert` 模式，以及窄屏覆盖层的打开聚焦和 Escape 回焦模式。

需要用户视觉确认后再扩散的选择包括：Preview 默认 220px 是否适合主要工作流、Inspector 默认 296px 是否需要按岗位区分、splitter 握柄可见强度、短屏下 Preview 是否应默认折叠，以及非模态覆盖层是否需要遮罩。

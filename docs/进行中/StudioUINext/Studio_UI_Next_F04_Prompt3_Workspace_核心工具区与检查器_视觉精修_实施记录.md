# Studio UI Next F04 Prompt 3/5：Workspace 核心工具区与检查器视觉精修实施记录

> 实施日期：2026-07-19（Asia/Shanghai）
> 初始基线：`6fc88e3623670a98efe47a3cb37f25985d96df24`
> 权威输入：F04 视觉审计方案、Prompt 1/2 实施记录、`clearvision-studio-ui-design`、`impeccable`、`vue-best-practices`、`web-design-guidelines`
> 范围：Operator Rail、Flow toolbar、Inspector、Preview/Image/ROI 及其共享 Workspace 表现层；未修改保存、正式运行、API、后端、Canvas 内核、splitter 或布局偏好合同。

## 1. 结论

本轮把 Workspace 内部高频区域从“边框卡片与普通后台表单的组合”收敛为连续、低噪声、高信息密度的工业桌面工作台：Canvas 继续拥有最大面积和最强色面，算子区与属性检查器承担窄侧栏工具角色，Preview 明确区分图像、ROI、结构化结果、附加结果和诊断信息。

四个区域已具备进入 Prompt 4 的稳定外围语法。下一轮可以集中精修 FlowCanvas 节点、端口、连线和 minimap，不需要重新打开 Prompt 2 的布局 Owner，也不需要再扩张 Workspace Chrome。

## 2. 四个区域解决的问题

### 2.1 Operator Rail

修改前的问题：分类胶囊横向滚动、每个算子都是完整描边卡片、搜索与分类密度偏松、点击与拖动入口不够明确、长中文与技术类型互相争夺空间。

本轮调整：

- 使用共享搜索框和原生紧凑分类选择器，避免分类横向滚动；Windows 原生选择器显式设置前景与背景色。
- 算子条目改为连续列表行，仅用单向分隔线、hover、focus 和 dragging 状态建立层级。
- 搜索覆盖名称、类型、端口与参数；提示改为“名称、类型或参数…”。
- 标题显示当前结果数；过滤后显示“命中数 / 可用数”。
- 保留“显示兼容算子”、单击添加和 HTML 拖放合同；拖动时显示抓取图标与明确拖动态。
- 长算子名、描述、分类和技术类型使用各自的截断与 tooltip；技术类型使用等宽字体并标记 `translate="no"`。
- 超过 50 项的目录使用 `content-visibility: auto`，列表保持唯一纵向滚动 owner。

### 2.2 Flow toolbar

修改前的问题：操作按顺序平铺，图标与文字混杂，复制、副本、启停和删除主次不清，缩放命令与编辑命令等权，禁用态只有透明度差异。

本轮调整：

- 按“历史操作 / 节点编辑 / 节点状态 / 画布视图”分组，不增加 32px 工具栏高度。
- 撤销、重做、缩放使用图标按钮；复制、粘贴、副本、启用/禁用、删除保留中文标签。
- 启停标签根据所选节点真实状态显示“启用”“禁用”或“切换状态”。
- 删除在静止状态不使用危险色，只在可操作 hover 时显示 NG 语义。
- 补齐中文 `title`、`aria-label`、`aria-keyshortcuts`、focus-visible、active、disabled 状态。
- 底部状态压缩为“节点 · 连线 · 已选”和“本地流程 rN”，反馈使用中文主语义。

### 2.3 Inspector

修改前的问题：节点身份、基础属性、端口和参数近似等权；参数逐项卡片化；英文执行状态和内部 `metadata / G3 / Flow draft / Inspector owner` 文案进入用户界面；长中文字段与错误缺少稳定展示策略。

本轮调整：

- 使用共享 pane header，明确“属性检查器 / 当前选择模式 / 可编辑性”。
- 执行状态映射为“尚未执行、执行中、执行成功、执行失败、已跳过”，颜色只作为中文标签的增强。
- 节点身份、类型、描述、最近耗时、执行错误形成清晰顺序；技术类型保持次级等宽信息。
- 基础属性、端口和参数采用连续区段与单向分隔线，取消逐字段卡片。
- 参数标签、帮助、默认值、未定义、已弃用、条件禁用、专用编辑器、空值和错误全部使用简体中文。
- 输入控件补齐 `name`、`autocomplete`、`inputmode`、`aria-invalid` 和 `aria-describedby`；错误与帮助文本绑定到对应控件。
- 长字段名允许换行，帮助文本限制为两行并保留 tooltip，错误原因允许完整换行。
- 248px 使用容器查询保持节点状态、端口和字段可读；296px 为默认密度；420px 提供参数调试空间。
- 修复一个并发时序缺陷：Owner 发布等价参数投影时不再重置尚未 blur/Enter 的本地输入。无效值可保留在字段中并与错误同时显示，不再出现“字段回到旧值但错误仍指向新值”的矛盾。

### 2.4 Preview / Image / ROI

修改前的问题：图像、ROI、结果、附加结果和诊断由多层边框块组织；短宽度直接隐藏详情；结构化结果存在嵌套滚动；状态词包含 `STALE / Artifacts / Flow draft`；空、错和加载状态层级不足。

本轮调整：

- Preview header 使用共享 pane header；主动作统一为“预览节点”，运行中显示“正在预览…”，取消动作显示“取消预览”。
- 状态统一为“等待选择、预览中、预览完成、无输出、条件未满足、已取消、会话失效、预览失败”。
- ROI 统一为“感兴趣区域（ROI）”，操作使用“编辑 ROI、撤销、重做、放弃、应用 ROI”。
- 图像工具统一使用缩小、放大、适应、1:1 图标语法；Canvas 增加中文可访问名称和键盘焦点。
- 图像空、加载、错误状态说明“发生了什么”和下一步；像素探针只在锁定或 ROI 统计状态下进入礼貌播报，避免 hover 时高频打扰屏幕阅读器。
- Preview 详情采用连续区段；结构化结果不再创建第二个纵向滚动区，详情列是唯一纵向滚动 owner。
- 附加结果显示角色、MIME 和本地化字节数；诊断码与中文原因分列，长错误完整换行。
- 移除旧的窄宽度 `display: none`，1350×704 下仍同时保留图像和结果详情。
- Prompt 2 的折叠、恢复、`inert`、splitter 和布局持久化语义保持不变。

## 3. 共享视觉规则

### 3.1 新增 `WorkspacePaneHeader`

`WorkspacePaneHeader.vue` 是仅负责表现的共享 Workspace pane header：

- 固定 38px 连续标题栏；
- 标题、详情、状态与操作具有统一基线；
- 长标题使用省略和 tooltip；
- 使用 raised 表面与单向底部分隔，不新增阴影或卡片外框；
- 不持有 Owner、业务状态或布局状态。

Operator、Inspector 和 Preview 复用该组件，避免三个页面继续各自维护标题栏语法。

### 3.2 共享图标与搜索 primitive

- 设计系统图标集合增加撤销、重做、复制、粘贴、副本、电源、删除、缩放、适应、实际大小和拖动图标；沿用同一 24×24、1.8px stroke 语法。
- `CvSearchField` 仅增加可选 `inputTestId`，把测试标识放到真实 input；搜索行为和双向绑定合同未改变。
- 本轮未新增第二套 tokens，也未修改现有颜色、圆角、阴影或密度 token。

## 4. 中文术语与长文本

| 修改前或内部表达 | 用户界面表达 |
| --- | --- |
| Inspector | 属性检查器 |
| metadata | 参数定义 |
| Flow draft / draft | 本地流程 / 当前流程数据 |
| STALE | 结果已过期 |
| Artifacts | 附加结果 |
| Manual Preview | 预览节点 |
| ROI / 图像参数 | 感兴趣区域（ROI） |
| deprecated | 已弃用 |
| Use default value (null) | 使用默认值（空值） |
| NotExecuted / Executing / Success / Failed / Skipped | 尚未执行 / 执行中 / 执行成功 / 执行失败 / 已跳过 |

工程名、节点名、算子名、参数名和错误原因分别采用换行、两行摘要、单行截断和 tooltip；不会为未来英文长度预留无效空间。诊断码、算子类型和 MIME 等确有排障价值的技术标识保留为次级信息，并使用 `translate="no"`。

## 5. 状态表达

| 状态 | 表达方式 |
| --- | --- |
| hover | 低对比度交互底色，不改变布局 |
| focus | 统一 2px focus ring；Canvas 使用内描边 |
| selected | Canvas 选择继续由 canonical FlowCanvas 表达；算子分类由原生 select 表达；拖动条目有独立 dragging 状态 |
| disabled | 控件降噪但标签保持可读；鼠标形态明确不可操作 |
| loading | 中文进行时文案与现有 Button loading 状态；不新增页面级动画 |
| empty | 图标、当前状态和下一步说明三层结构 |
| error | 中文原因、影响和恢复方向；字段错误靠近字段，节点/图像错误靠近对应工作区 |
| stale | “结果已过期”与重新预览说明，不只依赖警告色 |
| success / NG | 成功、执行失败和业务 NG 继续使用不同语义，不合并为泛化红绿状态 |

所有新增 transition 使用已有 motion token；全局 reduced-motion 投影会把持续时间降为 0ms。

## 6. 目标视口与实际几何

下表来自浅色主题 Browser fixture，DPR 1。数值是 CSS viewport 几何，不是 Windows 系统 DPI 证据。Prompt 3 未改变 Prompt 2 的外层尺寸边界。

| 视口 | 密度 | Operator | Canvas stage | Inspector | Preview | 页面溢出 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| 1920×1080 | compact | 210px | 1346×698 | 296px | 220px | H=0 / V=0 |
| 1920×1080 | comfortable | 210px | 1342×692 | 296px | 220px | H=0 / V=0 |
| 1350×704 | compact | 210px | 776×322 | 296px | 220px | H=0 / V=0 |
| 1350×704 | comfortable | 210px | 772×316 | 296px | 220px | H=0 / V=0 |

1920×1080 compact 的 Inspector 宽度状态：

| Inspector | Canvas stage | Preview 图像区 | Preview 详情区 |
| ---: | ---: | ---: | ---: |
| 248px | 1394×698 | 871×150 | 523×181 |
| 296px | 1346×698 | 841×150 | 505×181 |
| 420px | 1222×698 | 764×150 | 458×181 |

1350×704 comfortable 的 Preview 状态证据中，图像区约为 482×150，详情区约为 290×181；成功、有图、ROI 编辑、无图和错误状态均同时保留两列。所有专项状态的 Operator、Flow toolbar、Inspector 与 Preview 详情横向溢出均为 0；Inspector 和 Preview 详情各自保持单一、受控的纵向滚动。

## 7. 验证结果

- `npm run typecheck`：PASS。
- `npm run lint`：PASS，0 warning。
- `npm run test:unit`：PASS，76 files / 487 tests。
- `npm run build`：PASS；保留既有大 chunk warning，本轮未增加拆包范围。
- `f03-workspace.spec.ts`：PASS，50/50；包括 Prompt 1/2 回归、Prompt 3 状态、20 次 Owner 生命周期循环和性能样本。
- Prompt 3 参数草稿并发回归：5 workers × 5 次，5/5 PASS。
- 最终 Browser 截图与 JSON：`.tmp/studio-ui-next/f04/visual-prompt3-final/`，提交后使用最终 SHA 重新生成。
- 真实 Windows 当前缩放：100%，`AppliedDPI=96`、`GetDpiForSystem=96`。
- 真实 Windows 125%：**NOT PERFORMED / 待人工验证**；未使用浏览器缩放或 DPR 冒充系统 DPI。
- 真实 WebView2 Prompt 3 专项旅程：**NOT RUN**；Browser fixture 不冒充 WebView2。

## 8. 合同与边界

本轮没有修改：

- Project / Flow / GlobalVariables 权威；
- 保存、PersistenceRevision、正式运行、停止或核对合同；
- API transport、HostBridge、Runtime、Station 或后端；
- FlowCanvas 节点、端口、连线内核与序列化；
- Preview、Image、ROI、Inspector 的 Owner 数量与写入口；
- Prompt 2 splitter、Preview 折叠恢复和布局偏好 schema。

Owner 文件的变化只涉及用户可见投影文案；ParameterEditor 的 watch 修复只保证本地输入不会被等价投影刷新提前覆盖，正式参数提交仍通过既有 Inspector Owner 和 FlowCanvas command。

## 9. 遗留问题与 Prompt 4 建议

### 9.1 本轮明确未解决

- Canvas 长中文节点标题仍受旧节点视觉限制，存在溢出、重叠和连线标签干扰；这是当前最明显的视觉缺口。
- 节点默认、选中、禁用、执行中、成功、执行失败、业务 NG 状态尚未形成完整视觉矩阵。
- 端口类型、可连接/不可连接反馈和连线默认/选中/错误对比仍需专项精修。
- minimap 的节点块、视口框和背景层级仍偏原型感。
- 旧版最终判定、全局变量、工程导入导出、完整检测控制、结果证据/对比/导出、AI 与设备设置等 P0 能力缺口保持原结论；本轮没有隐藏或补造这些能力。
- 真实 WebView2 125%、跨显示器 100%/125% 和现场触控/高 DPI 仍待人工验证。

### 9.2 Prompt 4 推荐顺序

1. 先治理长中文节点标题、技术类型和节点尺寸边界。
2. 再建立节点选择、禁用、执行、失败和业务判定状态矩阵。
3. 精修端口类型、连接兼容提示、拖线候选和拒绝反馈。
4. 最后收敛连线、箭头、选中态、错误态和 minimap 层级。

适合直接共享到 Prompt 4 的规则：Workspace pane header、工具按钮分组、统一图标、focus ring、状态 token、长文本策略、连续区段与单向分隔线。

必须等待用户视觉确认的事项：节点默认宽高、长标题是两行截断还是自适应高度、选中态蓝色强度、执行状态是否常驻节点、连线默认对比度、端口标签密度和 minimap 可见强度。

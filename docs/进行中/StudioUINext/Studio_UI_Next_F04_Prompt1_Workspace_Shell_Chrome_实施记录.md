# Studio UI Next F04 Prompt 1/5：Workspace Shell / Chrome 收敛实施记录

> 实施日期：2026-07-19（Asia/Shanghai）
> 初始基线：`65640f02902c36f6cdcfd3b0f6b63e90db8f81cd`
> 权威输入：`Studio_UI_Next_F04_视觉审计与优化方案_PROPOSED.md`、`clearvision-studio-ui-design`、`impeccable`、`vue-best-practices`
> 范围：只调整 Workspace Shell、固定 Chrome、工作区几何、用户可见中文和相关视觉证据；未修改后端、API、保存协议、正式运行协议、Canvas 内核或 Owner 生命周期。

## 1. 修改前的核心问题

1. Workspace 产品导航使用 `item.label.slice(0, 1)`，形成“概 / 工 / 算 / 检 / 诊 / 关”等歧义入口；“工程”和“工作站”还存在首字冲突风险。
2. Product topbar、Workspace toolbar、Flow toolbar、Flow status、Workspace statusbar 连续叠加，且工程名、ProjectId、PersistenceRevision、Owner 计数和阶段标识同时常驻，Canvas 被工程诊断语言包围。
3. Workspace toolbar 同时放置命令、三个状态 Badge、兼容合同和工程技术身份；在冲突、结果未知等状态下横向压力明显。
4. Canvas、Operator Rail、Preview、Inspector 使用近似等权的白色面板和完整分隔，工作对象不够突出。
5. `Run`、`Stop`、`Reconcile`、`draft`、`STALE`、`Inspector`、`metadata`、`artifact` 等内部词直接暴露给简体中文用户。
6. 低于 1220px 时 Inspector 直接隐藏、低于 920px 时 Operator Rail 直接隐藏，且没有恢复入口。
7. 旧布局固定使用 196–232px Operator Rail、280–320px Inspector、180–280px Preview；在短屏中 Canvas 有效高度不足。

## 2. 实际修改范围

### 2.1 Product Shell

- Workspace 产品 rail 改为“线性图标 + 完整中文短标签 + 原有 tooltip/aria-label”，不再截取首字。
- 新增并复用 Overview、Projects、Operators、Stations、Results、Diagnostics、About 七个设计系统图标。
- Workspace 模式下 Product topbar 收敛到 40px，左侧只表达“Studio / 工程工作台”，不再重复页面面包屑。
- Workspace rail 紧凑密度由 52px 调整为 60px，舒适密度为 64px；增加的少量横向空间换取稳定语义和可辨识度。
- 外观、用户、修改密码、退出、本地服务状态入口全部保留；仅在 Workspace 模式隐藏低优先级的主题/密度和角色副文本。

### 2.2 Workspace Shell

- Workspace toolbar 紧凑密度由 44px 收敛为 40px，舒适密度由 52px 收敛为 44px。
- 工程导航改为单行“工程列表 / 工程详情 / 工程名 / 版本”；ProjectId、PersistenceRevision 转移到元素 title 和既有 `data-*` 证据，不再常驻主界面。
- 工具栏只承载保存、正式运行、当前工程结果及真实恢复命令；保存与运行状态转移到底部低噪声业务状态栏。
- 兼容保存状态只在 `blocked` 或 `opaque-passthrough` 时出现；普通 `compatible` 状态不再占据主工具栏。
- Owner、订阅、读写计数收进底栏“技术状态”浮层，普通用户默认只看到保存与正式运行状态。
- Workspace 外轮廓取消完整边框，采用连续表面与单向分隔线。

### 2.3 Flow 工作区几何与固定 Chrome

- 默认三栏调整为：Operator Rail 180–210px、Canvas 最小 600px、Inspector 260–296px。
- Preview 默认高度调整为 160–220px；短屏不高于 760px 时为 140–160px，不高于 650px 时收敛为 38px 状态区。
- Flow toolbar 由 36px 收敛为 32px，Flow status 由 24px 收敛为 20px；命令按钮默认使用透明连续工具面，不再每个按钮都形成小卡片。
- 低于 980px 时 Inspector 改为可恢复的右侧覆盖层入口；低于 760px 时 Operator Rail 改为可恢复的左侧覆盖层入口。Owner 与订阅保持挂载，不新增布局权威或业务状态树。
- 未实现 splitter、尺寸拖拽或本地布局持久化；本轮只建立稳定几何与恢复入口。

## 3. 视觉层级变化

修改后的主次关系为：

1. Canvas 是面积、色面和连续性最强的一级工作对象。
2. Operator Rail 与 Inspector 是窄侧栏工具面，通过单向边界与 Canvas 区分，不再与 Canvas 等权。
3. Preview 是二级调试区，短屏优先让位于 Canvas，但入口与标题保持首屏可见。
4. 保存、正式运行、当前工程结果是 Workspace 顶部的一级业务命令。
5. 保存/运行状态位于底部低噪声状态栏；Owner、revision、订阅、hash 等进入诊断层。

本轮没有调整 Canvas 节点、连线、Inspector 表单、Preview 结果内容的完整视觉语法，因此节点卡片、Operator item 卡片和 Inspector 参数容器仍保留下一轮精修空间。

## 4. 中文术语修正

| 修改前 | 修改后 |
| --- | --- |
| Run | 正式运行 |
| Stop | 停止运行 |
| Reconcile（运行） | 查询运行结果 |
| Reconcile（保存） | 核对保存结果 |
| 重放 draft | 重新应用本地草稿 |
| 放弃 draft | 放弃本地草稿 |
| Formal Run: ready / executing / outcome unknown | 正式运行就绪 / 正式运行中 / 运行结果待确认 |
| STALE | 已过期 |
| Inspector | 属性检查器 |
| metadata | 参数定义 / 算子信息 |
| artifact | 附加结果 |
| Flow draft | 本地流程草稿 |
| opaque passthrough | 含兼容字段，保存时将原样保留 |

合同枚举、`data-*`、Owner 类型名和 API 路径仍保留在代码与证据层，没有改变后端契约。

## 5. 1080P 与短屏验证结果

Browser fixture，浅色主题，DPR 1；截图元数据记录真实 CSS viewport、主题、密度、全局 overflow 和 Workspace 几何。

| 视口 | 密度 | Product topbar | Workspace toolbar | Canvas stage | Operator | Inspector | Preview | Statusbar | 全局溢出 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 1920×1080 | compact | 40 | 40 | 1354×706 | 210 | 296 | 220 | 22 | H=0 / V=0 |
| 1920×1080 | comfortable | 40 | 44 | 1350×700 | 210 | 296 | 220 | 24 | H=0 / V=0 |
| 1350×704 | compact | 40 | 40 | 784×390 | 210 | 296 | 160 | 22 | H=0 / V=0 |
| 1350×704 | comfortable | 40 | 44 | 780×384 | 210 | 296 | 160 | 24 | H=0 / V=0 |

四个场景均满足：

- 无页面水平滚动或全局双层滚动；
- 保存、正式运行、当前工程结果首屏可见；
- Canvas、Operator Rail、Preview、属性检查器同时可达；
- 1350×704 下 Canvas 大于审计基线 600×300；
- 1920×1080 下 Canvas 大于审计基线 900×480；
- compact 与 comfortable 均未遮挡关键命令。

最终证据目录：`.tmp/studio-ui-next/f04/visual-prompt1-final/`。该目录由最终提交 SHA 重新生成，包含四张 PNG 和四份 JSON 几何/运行时元数据。

## 6. 构建、测试与证据

- `npm run typecheck`：PASS。
- `npm run lint`：PASS，0 warning。
- `npm run test:unit`：PASS，75 files / 481 tests。
- `npm run build`：PASS；保留既有大 chunk warning，本轮未新增代码拆分任务。
- `f03-workspace.spec.ts`：PASS，45/45；覆盖保存、正式运行、停止/核对、Preview、ROI、Inspector、20 次生命周期循环、100/150 与 300/450 性能样本，以及四场景空间矩阵。
- Browser visual matrix：PASS，4/4；console error 与 page error 均为 0。
- Windows 系统 DPI：`GetDpiForSystem=96`、`AppliedDPI=96`，本机当前为真实 100% 缩放。
- Windows 125%：**NOT PERFORMED / 待人工验证**。本轮没有用浏览器缩放或 DPR 模拟冒充系统缩放。
- 真实 WebView2 Prompt 1 专项旅程：**NOT RUN**；既有 WebView2 证据只作为基线，不冒充本轮最终截图。

## 7. 未解决的 P0 / P1 问题

### P0：功能或操作回归 / 能力承载缺口

本轮未补齐、也未通过视觉调整隐藏以下已知缺口：

- 旧版最终判定配置、全局变量完整工作台；
- 工程导入/导出、示例工程、运行包与 Station 交付链；
- 完整检测控制、结果图像证据、对比与导出；
- AI、设备设置和其他尚未进入 Next pilot 的旧版能力。

这些问题继续由能力矩阵和产品替换门禁管理，不能由视觉实现自行扩权。

### P1：布局与真实环境

- 尚未实现 Operator / Canvas / Inspector splitter，也未实现 Preview 拖拽高度和用户布局偏好。
- Preview 仅有短屏 CSS 收敛，没有完整的显式折叠/展开按钮与状态持久化。
- 低于 980px 的覆盖层恢复入口已存在，但键盘焦点管理、遮罩与复杂长表单仍需下一轮专项验证。
- 真实 Windows 125% 系统缩放、跨显示器移动、真实 WebView2 100%/125% 对照仍待人工环境证据。
- 长工程名、极端保存冲突长消息和正式运行长错误在 1350×704 下仍需状态专项截图。

## 8. 下一轮建议

适合进入 Prompt 2/5 的 Canvas 周边与 splitter 优化，建议由同一个 FlowCanvas + Inspector + Preview 实现 Owner 顺序完成：

1. 先实现 Operator / Canvas / Inspector 水平 splitter，冻结最小宽度、键盘步进和双击复位。
2. 再实现 Preview 折叠/展开与垂直 splitter，保证短屏 38–40px 恢复条。
3. 补齐 Overlay Inspector/Operator 的焦点返回、Escape 和窄屏长内容滚动。
4. 最后精修 Operator Rail、Canvas toolbar、Inspector header/section、Preview header 的共享连续表面语法。

需要用户视觉确认后再扩散的事项：rail 60/64px 的最终宽度、Preview 默认 220px 高度、浅色 Canvas 冷色强度、品牌红是否继续作为实心主操作、comfortable 的最终默认程度。上述选择不影响本轮工程与可用性结论。

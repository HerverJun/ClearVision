# M00 Legacy / Next 任务与视觉差异矩阵

```text
SOURCE_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
COMPARISON_RULE=同一 ProjectId、角色、工程/节点/结果 ID、数据版本才可比较
DIFFERENT_SOURCE=NON_COMPARABLE
```

| 任务语义 | Legacy 入口/权威 | Next 入口/投影 | 当前分类 | 后续阶段 |
| --- | --- | --- | --- | --- |
| 工程选择与打开 | Legacy project manager / 后端 Project | `/projects`、Project lifecycle owner | 已重定位，authority 保持后端 | M03-A |
| 流程编辑 | Legacy FlowCanvas adapter | `/projects/:id/workspace`、canonical Canvas adapter | 已等价保留；视觉继续校准 | M02 |
| 属性/资源配置 | Legacy inspector 参数合同 | Workspace Inspector owner | 已重定位；保存仍走既有 coordinator | M02 |
| 预览与 ROI | Legacy preview/image/ROI modules | Preview/ROI owners + shared adapter | 共享核心保留，视觉差异待收口 | M02 |
| 正式运行 | Legacy run controls | Workspace RunStatusBar/RunConsole + authenticated HTTP/SSE | 已重定位；不新增执行 authority | M02/M03-B |
| 检测结果 | Legacy results | `/results`、results-read projection | 已重定位；证据/来源需回归 | M03-B |
| 工作站 | Legacy station surface | `/stations`、stations-read projection | 已重定位；Operator 只读 | M03-C |
| 设置 | Legacy settings | `/settings`、唯一 settings owner | 已重定位；文案与状态待收口 | M04-A |
| AI 工程 | Legacy/既有 AgentRun | `/ai`、AI Session owner | 已重定位；不改变 AgentRun authority | M04-B |
| 低频支持 | Legacy diagnostics/about/operators | Next 公共 Shell 下只读页 | 按 profile 隐藏/保留 | M04-C |
| Legacy fallback | `wwwroot/index.html` 及旧 profile | 仍可配置回退 | 只读接受，未批准退役 | M08/M09 |

## 明确不比较/不伪造

- 不同 ProjectId、不同 persistence revision、不同结果来源或不同角色的截图标记 `NON_COMPARABLE`。
- Next Browser fixture 的 DPR/viewport 只表示 Chromium；不写成 Windows DPI 或 WebView2 通过。
- 静态 fixture 没有真实图像流、Station 在线状态或 AI 模型进度时，只显示已有 projection 的空态/unknown，不制造数据。

## 当前缺失回归台账

| ID | 缺失/回归 | owner | 状态 |
| --- | --- | --- | --- |
| M00-R01 | CSS 不应通过 `nth-child` 隐藏动态导航 | `COORD-M` | M01 |
| M00-R02 | 产品 typography 仍有负字距 token/消费点 | `COORD-M` | M01 |
| M00-R03 | 公共浮层 elevation 语义存在未定义消费 | `COORD-M` | M01 |
| M00-R04 | Workspace dirty 变更需完整 geometry/owner ledger | `OWN-M02-WORKSPACE` | M02 |
| M00-R05 | 真实 WebView2 125% 证据缺失 | `COORD-M`/Host | BLOCKED |
| M00-R06 | 独立无 Node 目标机证据缺失 | Release owner / target-machine owner | NOT_PERFORMED |

2026-08-07 已在真实 Debug/Release WebView2 的 Windows 原生 100%（96 DPI）会话取得 Workspace、Golden
Journey 与 publish route 证据；该结果不改变 `M00-R05`，也不替代产品负责人对 Legacy 任务熟悉度的签收。

## Current TODO execution addendum (2026-08-07)

```text
SOURCE_SHA=68e6e4286d008433f804ef90de00c8017184c177_PLUS_SCOPED_WORKTREE
BRANCH=studio-ui-next
F03_BROWSER=PASS_59_OF_59
F04R_BROWSER=PASS_2_OF_2
REAL_WEBVIEW2_125=NOT_PERFORMED
INDEPENDENT_NO_NODE=NOT_PERFORMED
FIELD_HARDWARE=NOT_PERFORMED
```

本次实现把“主体已迁移”与“细分操作已闭合”分开记录：

| 任务语义 | 当前细分状态 | 视觉/交互证据 | authority 边界 |
| --- | --- | --- | --- |
| Inspector file/path/color | `IMPLEMENTED` | F03 可见交互 + unit | FilePickerPort、InspectorOwner 和 canonical Flow draft |
| AI pending file parameter | `IMPLEMENTED_PARTIAL` | unit + workspace fixture | AgentRun 合同仍由后端决定；附件/资源绑定未伪造 |
| Flow template | `IMPLEMENTED_PARTIAL_EVIDENCE` | owner/decoder unit | 应用只改 canonical draft，显式保存走既有 lifecycle |
| Project JSON import/export | `BLOCKED_BY_CONTRACT` | 无可比较 Next journey | 不复制 Legacy repository write |
| N 点标定 | `PARTIAL` | owner/contract unit；无完整 Playwright | draft、candidate、formal asset 三种身份分离 |
| 二维比例/偏移标定 | `BLOCKED_BY_CONTRACT` | 无当前 Next contract | 不建立第二 calibration authority |
| GlobalVariables runtime value | `IMPLEMENTED_PARTIAL_EVIDENCE` | owner unit + workspace integration | 运行值写入不回写工程定义 JSON |
| Results trend/distribution/report | `IMPLEMENTED_PARTIAL_EVIDENCE` | analysis owner/contract unit | 只读查询；整批 export contract 尚缺 |
| Line sequence / Station test package / advanced settings | `BLOCKED_BY_CONTRACT` | 不产生伪造场景 | 复用后端 command authority，缺口先停 |

本附录不把 Chromium viewport/DPR、已有 100% WebView2 证据或本机 Node 子进程计数扩大解释为 Windows 125%、独立 no-Node 或现场通过；短屏的 Chromium 旅程通过不改变 `M00-R05` 与 `M00-R06`。

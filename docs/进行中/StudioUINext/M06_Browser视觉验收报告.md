# M06 Browser 视觉验收报告

```text
STAGE=M06
STATE=PASS_BROWSER_PARTIAL_ACCEPTANCE
EVIDENCE_SOURCE_SHA=9800d6045a9f5fdfc62a166242e83529b833dc7d
M00_BASELINE_SHA=f8f581932469f7c52fe547b7bcabe8ad45d89532
BRANCH=studio-ui-next
EVIDENCE_WORKTREE_STATE=DIRTY_SCOPE_CLASSIFIED
CURRENT_BASELINE_BROWSER=PASS_146_SKIPPED_26_FAILED_0_CONTENT_EQUIVALENT_PRECOMMIT_CANDIDATE
OWNER=COORD-M
HOST_KIND=CHROMIUM_BROWSER
STARTUP_PROFILE=STUDIO_UI_NEXT_BROWSER_FIXTURE
AUTH_SOURCE=HARNESS_SEEDED_SESSION
FEATURE_FLAGS_CHANGED=NO
ROUTES_CHANGED=NO
AUTHORITY_CHANGED=NO
OWNER_TOPOLOGY_CHANGED=NO
PRODUCT_VISUAL_CONFIRMATION=NOT_GRANTED
```

## 结论

M06 的落盘 Browser 回归证据绑定 `EVIDENCE_SOURCE_SHA`：F02/F03/F04/F06 的证据 JSON 共 `26/5/91/32`
份，全部可解析，source SHA 全部匹配，全部有对应截图，记录的 console/page error 和水平溢出均为 0。
同一完整测试矩阵已在提交前、随后原样形成 `M00_BASELINE_SHA` 的内容等价候选上运行，结果为
`172 total / 146 passed / 26 explicit skipped / 0 failed`；未声称命令在 commit SHA 上重跑。

该结论只覆盖 Chromium Browser fixture 和现有静态本地服务。它不授予真实 WebView2、Windows 100%/125% DPI、Release/no-Node 目标机、现场 Camera/PLC/TCP/Station、生产 soak 或产品负责人视觉签收，因此 M06 不能标记为最终完成。

## 证据矩阵

| Phase | Evidence root | JSON | 截图 | SHA | runtime errors | horizontal overflow |
| --- | --- | ---: | ---: | --- | ---: | ---: |
| F02 | `.tmp/studio-ui-next/f02-1/m06/<sha>/` | 26 | 26 | 全部匹配 | 0 | 0 |
| F03 | `.tmp/studio-ui-next/f03/m06/<sha>/` | 5 | 5 | 全部匹配 | 0 | 0 |
| F04 | `.tmp/studio-ui-next/f04/m06/<sha>/` | 91 | 91 | 全部匹配 | 0 | 0 |
| F06 | `.tmp/studio-ui-next/f06-g5/m06/<sha>/` | 32 | 32 | 全部匹配 | 0 | 0 |

根索引：`.tmp/studio-ui-next/m-series/m06/9800d6045a9f5fdfc62a166242e83529b833dc7d/manifest.json`。截图位于同一 SHA 下的 `playwright/` 和 `visual-playwright/` 目录；仓库只保留索引和结论，不把批量截图纳入提交范围。

## 覆盖范围

- Shell、Projects、Workspace、Inspection、Results、Stations、Settings、AI 以及低频支持页面的当前 Browser 场景。
- `1920x1080`、`1366x768`、`1350x704`、`1366x600` 等压力视口，light/dark 和 compact/comfortable 投影。
- Admin/Engineer/Operator 的可见性与只读投影、关键 feature flag 分支、loading/empty/data/error/stale/conflict/readonly/unknown/running/OK/NG/execution-error 场景。
- 项目生命周期、Workspace owner、Canvas/Inspector/Preview/ROI、正式运行状态、AI build/history/recovery 以及 Settings/Station 只读状态。
- 运行时错误、水平溢出、请求方法审计和现有 owner/resource ledger 断言。

## 运行记录

| 检查 | 结果 | 说明 |
| --- | --- | --- |
| F02 Browser phase | `47/47 PASS` | 使用 F02 fixture；不是 WebView2 |
| F03 Browser phase | `54/54 PASS` | Workspace phase；不是 WebView2 |
| F04 Browser phase | `80 PASS / 5 SKIP` | 保留现有显式 skip；无失败 |
| F06 Browser phase | `29/29 PASS` | AI/history phase；不是 WebView2 |
| M06/M07 full regression | `146 PASS / 26 SKIP / 0 FAIL` | `172 total`；新增 Workspace modal focus 回归 |
| console/page errors | `0` | 以落盘 JSON 为准 |
| visual fixture screenshot | `PASS` | 截图稳定且与 JSON 元数据对应 |
| WebView2 native DPI | `PASS_100_ONLY` | 当前会话真实 WebView2 native DPI 96；125% 仍缺失 |
| Windows 100%/125% matrix | `PARTIAL` | 100% Debug/Release baseline PASS；125% `NOT_PERFORMED`；Browser DPR 不替代 Windows DPI |
| independent no-Node machine | `NOT_PERFORMED` | 外部 Node/CDP driver 不能证明目标机无 Node |
| product-owner visual sign-off | `NOT_GRANTED` | 无法代签 Quiet Precision/任务熟悉度 |

## 审计判断

- Browser 证据支持 `M06_BROWSER_GATE=PASS`。
- 当前阶段整体为 `M06_STATE=PASS_BROWSER_PARTIAL_ACCEPTANCE`，阻塞项是产品签收和 M08/M09 外部门禁，不把 fixture 误解释为 WebView2。
- 代码和测试范围未改变后端 authority、ProjectSaveCoordinator、PersistenceRevision、AgentRun、Runtime、Station、HostBridge 或 canonical Canvas owner。
- Labs 截图只作为非生产视觉样本；不作为发布资产或现场验收证据。

## 未决项

1. 已完成真实 Debug/Release WebView2 的 1920x1080、Windows 100% 基线；下一步在真实 Windows 125% 补齐 light/dark 与核心 Workspace 旅程。
2. 在独立无 Node 目标机完成 no-Node 证据，并与本地外部 CDP driver 证据分开报告。
3. 完成现场 Camera/PLC/TCP/Station、生产 soak 和产品负责人视觉签收。

## 下一阶段入口

M07 的自动化可访问性/响应式/状态韧性审计已追加；真实 WebView2/DPI 和产品签收仍是 M08/M09 的进入条件。

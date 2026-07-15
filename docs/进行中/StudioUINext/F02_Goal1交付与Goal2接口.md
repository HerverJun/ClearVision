# F02 Goal 1 交付与 Goal 2 接口

## 1. SHA 与状态

```text
F02_INITIAL_SHA=f6d4d98a53914bac088cd62cda261b2c08a11670
F02_OPERATOR_CONTRACT_SHA=4958ecab5873160d96b8c34efcf5f488257ea4df
F02_GOAL1_PRODUCT_EVIDENCE_SHA=f5385531b709b22509f7479a63aabb6419fe898d
AUTH_ENTRY_DECISION=PRESEEDED_SESSION_PREVIEW_ONLY
F02_STATE=NOT_DONE
```

`F02_OPERATOR_CONTRACT_SHA` 是独立 Operator 合同门禁结论提交，不代表合同同步 PASS。该提交的
真实结论仍是：

```text
OPERATOR_CONTRACT_GATE=BLOCKED
BLOCKED_OPERATOR_SYNC_SCOPE_EXPANSION
BLOCKED_OPERATOR_METADATA_FIELD_NOT_AUTHORITATIVE
```

因此 Goal 1 没有实现 Operator Catalog，也不宣称 F02 DONE。最终 Goal 1 文档提交与远端 SHA
由推送完成后的 Git 事实记录，不在本文内循环写入。

## 2. Goal 1 交付

- Design System V1：正式 compact 默认、light/dark 与 compact/comfortable 偏好、根 public
  exports、中文文案规范、紧凑表格、搜索、分页、描述列表、页面标题、工具栏、状态页、提示和图标；
- App Shell：唯一 `ProductLayout`、隔离 `InternalLabLayout`、中文导航、route meta、面包屑、404，
  `/` 重定向 `/overview`，Labs 不进入正式导航；
- read platform：在既有 `apiTransport.ts` 上建立唯一 `readQuery`，覆盖 decoder、Abort、
  latest-request-wins、stale previous data、partial failure、protected cache、session generation 与 dispose；
- shared owner：唯一 `sessionProjectionOwner` 与 `systemStatusOwner`，Overview、Top Bar 与 Diagnostics
  只消费共享投影；
- 首批页面：GET-only Overview、Projects list/detail、Diagnostics 与 About；Projects 列表不消费 Flow、
  assets、GlobalVariables，也不推断算子数或连接数，详情统计只来自详情 GET；
- evidence foundation：F02 Browser fixture、GET method audit、`studio-product` WebView2 expectation、
  product-route verifier、性能脚本和 `EvidencePhase=f02` 参数化；F01 evidence 未被覆盖；
- architecture guards：唯一 fetch、无 Axios/第二 client、产品 GET-only、单 Shell、单 session/status/query
  owner、无 EventSource/第二 EventBus/ServiceRegistry、Labs 导航隔离、默认入口 flag 关闭。

`Studio:StudioUiEnabled=false` 未改变。Project、Flow、GlobalVariables、assets、PersistenceRevision、
Runtime、Station、Inspection 与正式结果的权威边界未改变。

## 3. API 与权限冻结结论

```text
F02_API_CONTRACT_FREEZE=PASS
F02_PERMISSION_CONTRACT_FREEZE=PASS
PRODUCT_REQUEST_METHODS=GET_ONLY
```

Goal 1 产品只批准：

```text
GET /health
GET /api/auth/me
GET /api/projects
GET /api/projects/recent?count=...
GET /api/projects/search?keyword=...
GET /api/projects/{id}
```

401 推进 session generation 并清理受保护缓存；403、404、5xx、malformed payload、Abort、stale
与 partial failure 均有独立状态与测试。前端 role 仅用于 UI 投影，不替代后端授权。

## 4. 性能与生命周期

Initial 基线仍以 `F02_INITIAL_SHA` 的报告为准：Diagnostics 中位 41.59 ms、Design Lab 中位
36.37 ms、20 次路由切换 p95 42.39 ms，主证据 SHA256 为
`E7F251517889A84408ABBC1D36D0E7C52CD95D37769C4A51075A865300179746`。

Goal 1 产品证据在 `F02_GOAL1_PRODUCT_EVIDENCE_SHA` 上使用同机、Debug Desktop、1366×768、
DPR 1、隔离空 SQLite、5 次导航与 20 次切换方法捕获：

| 指标 | 结果 |
| --- | ---: |
| Overview 首次可交互中位 | 28.38 ms |
| Projects 首次可交互中位 | 20.42 ms |
| Overview/Projects 路由切换 p95 | 27.78 ms |
| active interval | 2 → 2 |
| active timeout | 0 → 0 |
| DOM element delta | 0 |
| runtime console/page error | 0 |

证据：`.tmp/studio-ui-next/f02/goal1-product-performance/evidence/studio-ui-product-performance.json`，
SHA256 `A168270A4118F269B37ADCB8F85B8B278874CC1C5FD5D683A9322F9A7DDEBD8A`，
`DATA_SOURCE=REAL_WEBVIEW2_EMPTY_AUTHORITY`，`AUTH_SOURCE=HARNESS_SEEDED_SESSION`。

listener、CDP node、heap 与正常完成请求的 outstanding AbortController 仍是观测值，不能单独判定泄漏；
owner/dispose 单测、路由循环测试和稳定 timer/DOM 计数共同作为 Goal 1 证据。Initial 的技术 Lab 路由
与 Goal 1 产品路由不是同一页面，以上数值只作方法一致的产品基线，不宣称等价性能提升。

```text
F01_CANVAS_LAB_BASELINE=REBASED_WITH_REASON
```

原因：F02 composition root 在 Labs 路由也保留唯一 app-level session/status owner，所以 Lab 环境新增
两条稳定 interval 与只读 health/auth 请求；Canonical Canvas owner、画布宿主边界和 F01 evidence
未被替换。Design/Canvas Playwright 回归仍全部通过。

## 5. 验证记录

```text
npm run lint                                      PASS
npm run typecheck                                 PASS
npm run test:unit                                 PASS (31 files / 187 tests)
npm run build                                     PASS (172 modules)
CV_UI_SCENARIO=studio-ui-next npx playwright test PASS (17 / 17)
focused Desktop serial tests                      PASS (38 / 38)
Node --check + PowerShell AST parse               PASS
focused real WebView2 overview/projects/diagnostics PASS (3 / 3)
real WebView2 product performance scenario        PASS
git diff --check                                  PASS
```

Focused Desktop 串行集合为 `StudioUiArchitectureGuardTests`、`DesktopWebRootResolverTests`、
`StudioStartupPageResolverTests` 与 `WebView2HostTests`。本 Goal 没有启动最终完整
`workflow_dispatch`，没有执行 Release publish/no-Node、真实 DPI 矩阵、真实相机/PLC/Station 或现场硬件。

当前 CI 条件仍为：普通 push 只覆盖 `main`、`develop` 与 `v*` tag；PR 只覆盖 base 为 `main` 或
`develop`；`workflow_dispatch` 才在本分支适用。workflow_dispatch 中 Operator Industrial Gate 运行，
Code Quality、Release Build 与 Create Release 不适用或 skipped。本 Goal 不把普通分支 push 记作完整 CI。

## 6. Goal 2 明确接口

1. Operator Catalog 继续阻断；先在 identity-only partial gate 与完整 Runtime/算法合同扩权两条路线中
   做明确选择，未批准前不得实现 Catalog decoder/page；
2. Station、Results 及其 endpoint/权限/fixture 需要各自 API Contract Freeze 后才能进入产品路由；
3. Station SSE 继续延期，直到共享 authenticated stream ADR 冻结 Bearer、授权、脱敏、replay、
   reconnect、上限与 dispose；不得建立第二 fetch/stream client；
4. Project 创建、保存、删除、Flow 工作台、Preview、ImageCanvas、ROI、Inspection、Settings 与认证写操作
   均不从 Goal 1 推断授权；
5. 首次启动登录 handoff、默认入口切换、Release publish/no-Node、最终 workflow_dispatch 与用户产品级视觉
   确认仍是后续门禁，Goal 1 的预置会话预览不能替代这些证据。

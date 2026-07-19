# Studio UI Next F04 — G4 产品壳层、导航、Leave Guard 与视觉自动门禁闭环

## 1. Closure

```text
G4_STATUS=DONE
G4_PRODUCT_SHA=df373f24bd7da50db96a8cee1f522e91670abb00
G4_TEST_SHA=39093bf6daba43d0004ea1187b906c46cd6798aa
G4_EVIDENCE_SOURCE_SHA=39093bf6daba43d0004ea1187b906c46cd6798aa

VISIBLE_ROUTE_AUDIT=PASS
SHELL_SEMANTIC_DRIFT=CLOSED
UNIFIED_LEAVE_GUARD=PASS
KEYBOARD_ACCESSIBILITY=PASS
DPI_VISUAL_GATE=PASS
F04_PRODUCT_VISUAL_AUTOMATED_GATE=PASS
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER

F04-B30-VISIBLE-ROUTE-UNAUDITED=CLOSED
F04-B31-SHELL-SEMANTIC-DRIFT=CLOSED
F04-B32-VISUAL-CONFIRMATION-MISSING=OPEN_NON_ENGINEERING_APPROVAL
F04-B33-LEAVE-GUARD-BYPASS=CLOSED

G5_ENTRY=APPROVED
```

G4 只整理已经批准的产品入口、壳层语义、路由可见性、统一 Leave Guard 与产品视觉状态；没有新增第二 composition root、route guard、Project authority、save chain、Canvas 内核、HTTP client、EventBus 或 HostBridge。

## 2. Product shell and navigation

- Shell 已明确为“可编辑工程工作台”；Save 与 Formal Run 由后端权威链负责，Preview 不被描述为正式运行等价物。
- Engineer 正式导航稳定为 `/overview`、`/projects`、`/operators`、`/results`、`/diagnostics`、`/about`。
- Stations 在默认 profile 下不出现在导航，直接访问按 `Studio2.StationsRead` fail closed 到 `/forbidden`。
- internal Labs 不进入产品导航；真实 Desktop 访问 internal `/labs/canvas` 返回 `/forbidden`，没有为取证放宽正式 route guard。
- forbidden 与 not-found 分离；直接 URL 继续同时受 role/profile route guard 与后端 permission 保护。

## 3. Unified Leave Guard

唯一 `productLeaveGuardOwner` 覆盖：

```text
dirty draft
saving / save conflict / save unknown-outcome
admitting / executing / cancel-requested / run unknown-outcome
project create/delete unknown-outcome
project update conflict
logout / change-password
project switch / route leave / host close
```

- 可丢弃本地 draft 与 update conflict 使用可聚焦、可键盘确认的 prompt。
- save/run/project command 的 active 或 unknown authority 必须 settle/reconcile；不能用 UI 状态猜测服务端成功，也不能强制离开。
- route、Auth、Project delete 与 host close 复用同一个 guard owner；Feature/runtime dispose 后订阅与 owner ledger 回到 0。
- project-switch soak 在每轮等待唯一 `projectLifecycleCommandOwner` 的正式 `succeeded` 终态；节点自动 Preview settle 后才编辑，避免晚到投影污染下一轮。

## 4. Browser and accessibility evidence

final-SHA Browser 证据：

```text
SOURCE_SHA=39093bf6daba43d0004ea1187b906c46cd6798aa
BROWSER_RELEVANT_SUITE=PASS (51 passed, 2 optional F02 capture tests skipped)
F03_WORKSPACE_FULL=PASS (43/43)
STUDIO_UI_UNIT=PASS (480/480, 75 files)
LINT=PASS
TYPECHECK=PASS
DESKTOP_DEBUG_BUILD=PASS (0 warnings, 0 errors)
```

键盘与可访问性覆盖包括：

- skip-link 与 focus ring；
- route change 后主内容聚焦；
- Leave Guard dialog focus trap、Escape 保持、关闭后 focus restoration；
- destructive delete 初始焦点、Tab 顺序与 Enter 确认；
- setup/login labels、disabled/loading、live/error announcement；
- reduced-motion 下功能不失效。

## 5. Product visual evidence

repo-relative evidence：

```text
MANIFEST=.tmp/studio-ui-next/f04/visual-39093bf6-final/manifest.json
SCREENSHOTS=30
METADATA=30
SCENARIOS=19
VIEWPORTS=1366x768,1600x1000,1920x1080
BROWSER_EMULATED_DPR=1,1.25,1.5,2
RUNTIME_ERROR_RECORDS=0
MISSING_SCREENSHOTS=0
HORIZONTAL_OVERFLOW_ISSUES=0
SCREENSHOT_HASH_MISMATCHES=0
CODEX_VISUAL_QA=PASS
```

场景覆盖 setup、login、Overview、Projects empty/populated、create、Project Detail、Workspace empty/idle/dirty、Formal Run、Results、project/workspace conflict、unknown outcome、forbidden、Leave Guard prompt 与 destructive delete。

Codex 工程视觉抽查确认层级、截断、状态颜色、dialog 遮罩/焦点、三档 viewport 与高 DPR shell 没有自动门禁外的明显问题。该结论不是产品负责人批准：

```text
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
```

## 6. Real WebView2 and DPI evidence

正式 Product WebView2：

```text
EVIDENCE=.tmp/studio-ui-next/f04/g4-product-39093bf6/evidence/studio-ui-webview2-g4-product-39093bf6.json
EXPECTATION=studio-product
SEED_WORKSPACE=true
FORMAL_RUN=true
STATUS=PASS
MEANINGFUL_CONSOLE_ERRORS=0
MEANINGFUL_REQUEST_FAILURES=0
CLEANUP=PASS
```

该链使用真实 authenticated HTTP/API、显式 Project open、Workspace GET/PUT、Preview artifact、Formal Run admission/execute/stop/reconcile、Results handoff 与 20 次 Workspace lifecycle；不是 Browser fixture 或 internal Lab 替代。

正式 Product Workspace DPI：

```text
EVIDENCE=.tmp/studio-ui-next/f04/g4-dpi-39093bf6/studio-ui-dpi-evidence.json
STATUS=PASS
SCALES=1,1.25,1.5,2
CANVAS_EVIDENCE_SOURCE=formal-product-workspace
NATIVE_AWARENESS=PerMonitorV2
```

四档均验证 WebView2 force scale、JS DPR、CDP layout、screenshot pixels、native window、Canvas logical/backing size 与 pointer hit。DPR 2 的正式 Workspace Canvas 为 logical `843×300`、backing `1686×600`，节点 hit/Inspector selection 成功。

## 7. F04 final-SHA 复验

G4 产品壳层、导航、统一 Leave Guard、键盘合同和视觉门禁已在 F04 final code SHA 上重新采集：

```text
F04_FINAL_CODE_SHA=0c78962d2a005ebea165eaee8a98558aca88c99c
VISUAL_MANIFEST=.tmp/studio-ui-next/f04/visual-0c78962d-final-r3/manifest.json
VISUAL_STATUS=PASS (30 screenshots, 19 scenarios)
TARGETED_VISUAL_CONTRACT=PASS (9/9)
BROWSER_FULL=PASS (78 passed, 17 optional visual captures skipped)
DPI_MATRIX=PASS (1, 1.25, 1.5, 2)
KEYBOARD_ACCESSIBILITY=PASS
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
```

自动复验不替代用户产品视觉确认；完整证据和两次既有 Browser fixture 偶发失败的披露见 [G6 隔离 E2E 与 Final Evidence](./F04_G6_隔离E2E与FinalEvidence闭环.md)。

## 8. Preserved boundaries

```text
Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
FORMAL_DEFAULTS_CHANGED=NO
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO

F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

Browser-emulated DPR、真实 WebView2/DPI 与用户产品视觉确认分别记录，互不替代。G4 完成只批准进入 G5；没有批准正式默认启用、Legacy retirement 或 F05。

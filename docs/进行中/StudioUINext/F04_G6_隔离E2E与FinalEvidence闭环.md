# Studio UI Next F04 — G6 隔离 E2E、Final Evidence 与最终决策

## 1. 当前 closure

```text
G6_IMPLEMENTATION=YES
G6_LOCAL_EVIDENCE=PASS
G6_STATUS=LOCAL_DONE_REMOTE_CI_PENDING

G6_EVIDENCE_SHA=0c78962d2a005ebea165eaee8a98558aca88c99c
F04_FINAL_CODE_SHA=0c78962d2a005ebea165eaee8a98558aca88c99c

FINAL_SHA_USER_JOURNEY=PASS
20_CYCLE=PASS
RELEASE_PUBLISH=PASS
SANITIZED_PATH=PASS
STARTUP_TRUTH_TABLE=PASS
DPI_MATRIX=PASS
KEYBOARD_ACCESSIBILITY=PASS
ROLLBACK=PASS
F04_PRODUCT_VISUAL_AUTOMATED_GATE=PASS
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER

REMOTE_CI=PENDING_PUSH
FINAL_GATE=PENDING_REMOTE_CI
```

G6 没有新增 Auth、Project、Result、HTTP、EventBus、ServiceRegistry、Canvas、HostBridge 或保存权威。最终旅程继续复用现有 authenticated HTTP/API、`ProjectSaveCoordinator`、Inspection admission/execute/reconcile 与正式 Result repository。

## 2. Final-SHA 用户旅程

正式 manifest：

```text
MANIFEST=.tmp/studio-ui-next/f04/final/g6-final-0c78962d/studio-ui-final-evidence.json
SOURCE_SHA=0c78962d2a005ebea165eaee8a98558aca88c99c
STATUS=PASS
DESKTOP_PROCESSES=3
```

隔离旅程分三个独立 Desktop 进程执行：

1. fresh database 上通过真实 UI 完成 setup、auto-login、Overview、blank create、create response-loss reconcile、list/detail、explicit open、Workspace、算子添加与配置、Preview、Save、Formal Run、Results、返回 Workspace、再次修改与保存、logout，并保留数据库。
2. 重启后通过真实 UI login、recent、reopen 同一 Project，核对 Project/Flow/Result identity，完成 rename、delete response-loss reconcile，验证 list/detail/open tombstone 404，logout 后删除数据库。
3. 在另一 fresh database 完成 20 轮 login → open → Workspace → Preview → Formal Run → Results → logout，结束后删除数据库。

权威结果：

```text
FRESH_DATABASE=YES
UI_SETUP_AUTO_LOGIN=PASS
CREATE_RESPONSE_LOSS_RECONCILE=PASS
SAME_DATABASE_REUSED_AFTER_RESTART=YES
SAME_USER_IDENTITY=YES
DELETE_RESPONSE_LOSS_RECONCILE=PASS
TOMBSTONE_NOT_FOUND=PASS
DATABASE_REMOVED_AFTER_EVIDENCE=YES
```

## 3. 20-cycle 与泄漏判定

```text
CYCLES=20/20
UNIQUE_RESULTS=20/20
GC_GATE=PASS
WEAK_REFERENCE_GATE=PASS
POST_SOAK_DISPOSAL_SETTLE=PASS
PRIMARY_LEAK_GATE=OWNER_RESOURCE_WEAKREF_AND_STABLE_LOGIN_DOM_COUNTERS
```

开发态诊断最初看到 `Performance.getMetrics` 的 Nodes、listeners 与 JS heap 线性增长。根因不是 ProductRuntime 多 owner，而是 WebView2 Playwright 场景忽略 `page.waitForSelector()` 返回的 `ElementHandle`，导致外部 CDP driver 钉住已卸载 DOM。修复后：

- selector wait 改为不返回远程 DOM handle 的 locator wait；
- `waitForFunction` 返回的 JSHandle 显式 dispose；
- 每轮记录 login、Workspace、Results、logout 四阶段；
- `HeapProfiler.collectGarbage` 必须成功；
- 同时记录 `Memory.getDOMCounters`、native memory/handle 与 Workspace resource ledger；
- Product shell、Workspace shell、Results page、Project lifecycle、Leave Guard 与 Workspace diagnostics 均由 WeakRef 证明上一代在下一轮释放；
- 第 20 轮后额外执行一次只登录/退出的 disposal settle，确认全部受跟踪对象回收。

final-SHA 稳定 Overview 形态趋势：

| Metric | First | Last | Delta | Result |
| --- | ---: | ---: | ---: | --- |
| DOM Nodes | 447 | 447 | 0 | PASS |
| JS event listeners | 69 | 69 | 0 | PASS |
| Documents | 2 | 2 | 0 | PASS |
| JS heap used | 6,960,584 | 8,525,260 | +1,564,676 | PASS，低于 2 MiB monotonic 判定线 |
| Working set | 226,230,272 | 227,217,408 | +987,136 | PASS |
| Private memory | 85,606,400 | 85,123,072 | -483,328 | PASS |
| Handles | 769 | 767 | -2 | PASS |

logout 时当前 Results tree 会由 Vue Router 保留一代，其 Nodes 随新增一条 Result history 精确增加 49、listener 增加 1。WeakRef 证明前一代已释放，最终 settle 也全部回收，因此该数据形态增长不属于多代泄漏。稳定门禁采用下一轮 login 后的恒定 Overview 形态，没有通过提高原 64 MiB 阈值掩盖问题。

## 4. 验证矩阵

```text
BUILD=PASS (1 existing System.Collections.Immutable conflict warning, 0 errors)
LINT=PASS
TYPECHECK=PASS
STUDIO_UI_UNIT=PASS (480/480, 75 files)
AUTH_AND_PROJECT_FOCUSED=PASS (90/90)
SERVICES_REGRESSION=PASS (505/505)
RUNTIME_FOCUSED=PASS (102/102)
PHASE42_REGRESSION=PASS (143/143)
DESKTOP_ENDPOINTS=PASS (316/316)
ARCHITECTURE_GUARDS=PASS (9/9)
BROWSER_FULL=PASS (78 passed, 17 optional visual captures skipped)
REAL_WEBVIEW2_FINAL=PASS (3/3 processes)
```

Browser full 的 17 个 skip 都是仅在显式证据目录存在时执行的可选截图用例；F04 视觉合同另行启用证据目录运行并通过。

## 5. Release、sanitized path、DPI、profiles 与 rollback

```text
MATRIX=.tmp/studio-ui-next/f04/matrix/g6-matrix-0c78962d/studio-ui-webview2-matrix.json
STATUS=PASS
RUNS=12/12
RELEASE_PUBLISH=PASS
PUBLISH_STATIC_AUDIT=PASS
PUBLISHED_PRODUCT_RUNTIME=PASS
SANITIZED_PATH=PASS
DPI_AUTHORITY=PASS
SCALES=1,1.25,1.5,2
LOCAL_NO_NODE_AUDIT=PASS
CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
```

Release self-contained publish、Legacy/Next assets、manifest、发布目录真实启动、Overview、正式 Workspace DPI、missing-assets diagnostic、无 silent Legacy fallback，以及不携带 `node_modules`、数据库、user-data 或 dev assets 均已验证。临时 publish、missing-assets sample、build artifacts 与 runtime 目录已清理。

本机 publish 不等于独立无 Node 目标机验证；后者继续保持 accepted-deferred，不能写成 PASS。

final-SHA profiles：

```text
MANIFEST=.tmp/studio-ui-next/f04/profiles/g6-profiles-0c78962d/studio-ui-profile-evidence.json
STATUS=PASS
RUNS=8/8
LEGACY_DEFAULT=PASS
NEXT_PILOT=PASS
NEXT_FULL_CANDIDATE=PASS
TRUTH_TABLE=PASS (4/4 independent processes)
MISSING_ASSET_DIAGNOSTIC=PASS
DOUBLE_ROOT_GUARD=PASS
```

final-SHA rollback：

```text
MANIFEST=.tmp/studio-ui-next/f04/rollback/g6-rollback-0c78962d/studio-ui-rollback-evidence.json
STATUS=PASS
SEQUENCE=NEXT_PILOT -> LEGACY_DEFAULT -> NEXT_PILOT
RESTARTS=3
PROJECT_FLOW_RESULT_IDENTITY=SAME
DATABASE_REMOVED_AFTER_EVIDENCE=YES
```

## 6. 视觉与可访问性

```text
VISUAL_MANIFEST=.tmp/studio-ui-next/f04/visual-0c78962d-final-r3/manifest.json
STATUS=PASS
SCREENSHOTS=30
METADATA=30
SCENARIOS=19
VIEWPORTS=1366x768,1600x1000,1920x1080
BROWSER_EMULATED_DPR=1,1.25,1.5,2
RUNTIME_ERRORS=0
MISSING_SCREENSHOTS=0
HORIZONTAL_OVERFLOW_ISSUES=0
SCREENSHOT_HASH_MISMATCHES=0
CODEX_VISUAL_QA=PASS
```

Codex 抽查 setup、login、Overview、Projects empty/populated、create、Project Detail、Workspace empty/idle/dirty、Formal Run、Results、Project/Workspace conflict、unknown outcome、forbidden、Leave Guard prompt、destructive delete 与高 DPR shell，未发现自动门禁外的明显遮挡、截断、层级或状态色问题。

前两次全套视觉采集分别偶发失败于既有 100/150 Canvas selection 用例和既有 20-cycle project-switch 用例。两次均未修改代码或阈值，也不是视觉 capture hook 失败。随后精确运行 9 个视觉 capture-hook 测试，`9/9 PASS` 并生成完整 30 张 final-SHA 证据；失败目录仅保留在 `.tmp`，不作为 PASS 证据。

键盘、focus ring、skip link、dialog trap、Escape、focus restoration、destructive confirmation、disabled/loading 与 reduced motion 由 Browser full 和视觉合同共同覆盖。

```text
F04_PRODUCT_VISUAL_AUTOMATED_GATE=PASS
F04_PRODUCT_VISUAL_CONFIRMATION=AWAITING_USER
```

自动截图和 Codex QA 不替代产品负责人确认。

## 7. Remote CI、blocker 与最终决策

```text
F04-B50-FINAL-SHA-EVIDENCE-MISMATCH=CLOSED
F04-B51-REMOTE-CI-NOT-GREEN=OPEN_PENDING_REMOTE_CI
F04-B52-SCOPE-CREEP=CLOSED
```

本地工程与自动证据已经闭合；push 和远端 Final Gate 完成前，delivery 状态保持：

```text
PLAN_STATUS=LOCAL_IMPLEMENTED_REMOTE_CI_PENDING
F04_STATUS=LOCAL_COMPLETE_REMOTE_CI_PENDING
F04_IMPLEMENTED=YES
NEXT_PILOT_PROFILE_AVAILABLE=YES
NEXT_DEFAULT_ENTRY_RECOMMENDATION=DEFER
AWAITING_PRODUCT_VISUAL_CONFIRMATION=YES
DEFAULT_ENTRY_CHANGE_NOT_AUTHORIZED=YES
```

远端 CI 通过后，本文件记录 run ID/attempt 并把工程 closure 更新为 `COMPLETE`。即使届时 F04 工程完成，未获得用户视觉确认前仍不得建议或修改正式默认入口。

## 8. Preserved boundaries

```text
Studio:StudioUiEnabled=false
Studio:WorkspaceCapabilityEnabled=false
FORMAL_DEFAULTS_CHANGED=NO
LEGACY_RETIREMENT=NOT_APPROVED
F05_STARTED=NO

CLEAN_NO_NODE_TARGET_MACHINE_STATUS=NOT_PERFORMED
CLEAN_NO_NODE_TARGET_MACHINE_DISPOSITION=ACCEPTED_DEFERRED
CLEAN_NO_NODE_TARGET_MACHINE_GOVERNANCE=RESOLVED
CLEAN_NO_NODE_TARGET_MACHINE_BLOCKING=NO
```

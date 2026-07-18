# Studio UI Next F04 — G5 受控 Profiles、启动真值表与回滚闭环

## 1. Closure

```text
G5_STATUS=DONE
G5_PROFILE_SHA=b5ca0f9166570f4485ef0b2e93cfb616506bb269
G5_SUPPORTING_FIX_SHA=dbccc90e5df08c220f530036bf4ded9aaab8e565
G5_TEST_SHA=17e261136a51da701cfe8e453cfe662ea000aca3
G5_EVIDENCE_SOURCE_SHA=17e261136a51da701cfe8e453cfe662ea000aca3
F04_FINAL_CODE_SHA=17e261136a51da701cfe8e453cfe662ea000aca3

LEGACY_DEFAULT_PROFILE=PASS
NEXT_PILOT_PROFILE=PASS
NEXT_FULL_CANDIDATE_PROFILE=PASS
STARTUP_TRUTH_TABLE=PASS
MISSING_ASSET_DIAGNOSTIC=PASS
DOUBLE_ROOT_GUARD=PASS
NEXT_LEGACY_NEXT_ROLLBACK=PASS

F04-B40-SILENT-LEGACY-FALLBACK=CLOSED
F04-B41-DOUBLE-ROOT=CLOSED
F04-B42-NEXT-LEGACY-DATA-DIVERGENCE=CLOSED
F04-B43-ROLLBACK-DATA-LOSS=CLOSED
F04-B44-FLAG-NAME-MIXED=CLOSED

G6_ENTRY=APPROVED
G6_STATUS=NOT_STARTED
NEXT_PILOT_PROFILE_AVAILABLE=YES
```

G5 只建立受控启动标签、结构化启动诊断、隔离证据配置和显式共享数据库回滚演练。命名 profile 不覆盖配置权威；它只验证实际 flags 与名称一致，配置切换必须通过进程重启。

## 2. Profiles and startup diagnostics

`StudioStartupProfileCatalog` 冻结：

```text
LEGACY_DEFAULT
NEXT_PILOT
NEXT_FULL_CANDIDATE
ISOLATED_TRUTH_TABLE
```

- `LEGACY_DEFAULT` 只接受 false/false。
- `NEXT_PILOT` 与 `NEXT_FULL_CANDIDATE` 只接受 true/true；两者当前共享 G1 已批准的产品面，不引入 deferred 页面。
- false/true 与 true/false 不允许伪装为命名 profile，自动记录为 `ISOLATED_TRUTH_TABLE`。
- 正式 `appsettings.json` 仍是 false/false，因此默认 profile 仍为 `LEGACY_DEFAULT`。

每次 Desktop 启动输出唯一一条：

```text
[StudioStartup] {JSON}
```

JSON 固定记录 `profile`、`pageKind`、`initialPageUri`、`assetRoot`、`sourceSha`、`authMode`、`configurationRequiresRestart=true` 与 canonical flags。证据 wrapper 要求日志可解析、记录数为 1、source SHA 与当前提交一致。

## 3. Startup truth table and missing assets

正式 manifest：

```text
MANIFEST=.tmp/studio-ui-next/f04/profiles/g5-profiles-17e26113/studio-ui-profile-evidence.json
SOURCE_SHA=17e261136a51da701cfe8e453cfe662ea000aca3
STATUS=PASS
INDEPENDENT_DESKTOP_PROCESSES=8
```

覆盖：

| 证据 | `StudioUiEnabled` | `WorkspaceCapabilityEnabled` | 启动结果 | owner 结论 |
|---|---:|---:|---|---|
| `named-legacy` | false | false | Legacy / `LEGACY_DEFAULT` | Next owner=0 |
| `named-next-pilot` | true | true | Next / `NEXT_PILOT` | Studio root=1 |
| `named-next-full` | true | true | Next / `NEXT_FULL_CANDIDATE` | Studio root=1 |
| `truth-00` | false | false | Legacy / `LEGACY_DEFAULT` | Next owner=0 |
| `truth-01` | false | true | Legacy / `ISOLATED_TRUTH_TABLE` | Next owner=0，无副作用 |
| `truth-10` | true | false | Next / `ISOLATED_TRUTH_TABLE` | product owners=1，Workspace owner=0 |
| `truth-11` | true | true | Next / `NEXT_FULL_CANDIDATE` | product owners=1，Workspace owner=0（Overview） |
| `missing-assets` | true | true | Diagnostic | Legacy=0，Next=0 |

缺资产证据从临时 Desktop 运行副本移除 `wwwroot/studio`，保留 Legacy assets；resolver 返回列出具体缺失项的 diagnostic page，没有导航到 `/index.html`，也没有挂载任一产品 root。临时运行副本在证据结束后清理。

## 4. Next → Legacy → Next rollback

正式 PASS manifest：

```text
MANIFEST=.tmp/studio-ui-next/f04/rollback/g5-rollback-17e26113-r2/studio-ui-rollback-evidence.json
SOURCE_SHA=17e261136a51da701cfe8e453cfe662ea000aca3
STATUS=PASS
RESTARTS=3
```

演练使用一个显式位于 `.tmp/studio-ui-next` 的隔离 SQLite 文件和同一 authenticated user identity：

```text
NEXT_PILOT / NEXT_CREATE
→ seeded persisted Project
→ Workspace Preview / Save / Formal Run / Results
→ retain database

LEGACY_DEFAULT / LEGACY_VERIFY
→ reuse database
→ Next owner=0
→ read the same Project / Flow / Result through existing HTTP authority
→ retain database

NEXT_PILOT / NEXT_REOPEN
→ reuse database
→ explicit Project open
→ Workspace owner=1
→ compare the same authority
→ remove database artifacts
```

三次进程比较并通过：

```text
AUTHENTICATED_USER_IDENTITY=SAME
PROJECT_ID=SAME
CURRENT_PERSISTENCE_REVISION=SAME
FLOW_ID=SAME
RESULT_ID=SAME
RESULT_PROJECT_REVISION=SAME
EXECUTION_SNAPSHOT_ID=SAME
FLOW_HASH=SAME
DECISION_HASH=SAME
IMAGE_ID_AND_REFERENCE=SAME
RESULT_HISTORY_ROW=PRESENT
EVIDENCE_STATUS=AVAILABLE
DATABASE_REMOVED_AFTER_EVIDENCE=YES
```

Inspection history detail 已有投影提供图像、history 与 flow hash，但不投影 execution snapshot、result project revision 和 decision hash。G5 没有扩展后端合同；它复用现有 `/api/inspection/reconcile`，用首次 admission/execute 已验证的 identity 在每次重启后恢复同一正式 Result，再与 history/detail 组合核对。

## 5. Retry disclosure

首次 final-SHA rollback 尝试保留为：

```text
MANIFEST=.tmp/studio-ui-next/f04/rollback/g5-rollback-17e26113/studio-ui-rollback-evidence.json
STATUS=FAIL
CAUSE=TRANSIENT_LOCAL_HTTP_FETCH_FAILED_BEFORE_FIRST_BUSINESS_AUTHORITY_READ
CODE_CHANGED_BEFORE_RERUN=NO
```

该次 Desktop 启动、`[StudioStartup]`、setup/login 与 CDP 均成功，Node 在首次业务 API fetch 时失败；没有生成 Project/Flow/Result 身份，也没有出现数据分歧。端口释放后使用新的端口、数据库与 run name 从 `NEXT_CREATE` 完整重跑，三段全部通过。

## 6. Verification

```text
DESKTOP_FOCUSED_AND_ARCHITECTURE=PASS (46/46)
DESKTOP_DEBUG_BUILD=PASS (0 warnings, 0 errors)
STUDIO_UI_LINT=PASS
STUDIO_UI_TYPECHECK=PASS
STUDIO_UI_UNIT=PASS (480/480, 75 files)
NODE_SYNTAX=PASS
POWERSHELL_PARSE=PASS
PROFILE_WEBVIEW2=PASS (8/8 independent processes)
ROLLBACK_WEBVIEW2=PASS (3/3 sequential restarts, full rerun)
```

## 7. Preserved boundaries

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

G5 完成只批准进入 G6 final-SHA 证据。它不批准正式默认入口切换、Legacy retirement、用户产品视觉确认或 F05。

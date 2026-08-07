# F09 G1 Legacy / Next 终局能力矩阵

```text
F09_G1_STATE=DONE_WITH_DEFERRED_LEGACY_FALLBACKS
DEFAULT_ENTRY_DECISION=NEXT_DEFAULT_CANDIDATE_ONLY
LEGACY_PHYSICAL_REMOVAL=NOT_IN_F09
```

状态含义：`MIGRATED` 表示 Next 代码、路由和 owner 已存在；不代表真实 WebView2、现场设备或独立 no-Node 验收已经完成。`DEFERRED_WITH_LEGACY_FALLBACK` 必须保留清晰入口与后端 authority，不能被误写成迁移完成。

| CapabilityId | LegacyPath | NextRoute | Navigation | Roles | ReadAuthority | WriteAuthority | Owner | Evidence | Decision | IssueId | PlannedGoal |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| auth-session | `wwwroot/src/features/auth/` | `/login`, `/setup` | startup/router | all | existing auth endpoints | existing auth endpoints | auth lifecycle root | router/auth tests | MIGRATED | - | G4/G5 |
| projects-blank-lifecycle | `projectManager.js` | `/projects` | top bar | Admin, Engineer | ProjectService | ProjectLifecycleCoordinator / ProjectSaveCoordinator | project lifecycle owner | project lifecycle tests | MIGRATED | - | G4/G6 |
| projects-demo-template | `projectManager.js:createDemoProject` | none | Legacy only | Admin, Engineer | DemoProjectService | Legacy repository path, protected by `CanEditProject` | Legacy project manager | `DemoEndpointsTests`; source audit | DEFERRED_WITH_LEGACY_FALLBACK | F09-I003 | G7 |
| workspace-flow-canvas | `flow-editor/` | `/projects/:id/workspace` | project workspace | Admin, Engineer | project/flow services | canonical project save chain | workspace runtime | F03 owner ledger | MIGRATED | F09-I004 | G5/G6 |
| operator-catalog | `operators/` | `/operators`, workspace rail | More/workspace | all | operator catalog endpoint | existing project mutation path | operators read/runtime | architecture/navigation tests | MIGRATED | - | G5 |
| inspector-parameters | `property-panel/` | workspace | workspace | Admin, Engineer | Project DTO | ProjectSaveCoordinator | inspector owner | F03 composition / save tests | MIGRATED | - | G4 |
| preview-roi | `image-preview/`, `roi/` | workspace | workspace | Admin, Engineer | preview/asset contracts | canonical project update | preview/image/ROI owners | F03 owner ledger | MIGRATED | F09-I004 | G5/G6 |
| npoint-calibration-draft | calibration workbench | workspace ROI | workspace | Admin, Engineer | draft endpoint | formal asset save endpoint | calibration draft owner | calibration endpoint tests | PARTIAL | - | G7 |
| globals-final-decision | global variable / decision panels | workspace | global action/workspace | Admin, Engineer | ProjectService | ProjectSaveCoordinator | workspace persistence owner | F04/F08 tests | MIGRATED | - | G4 |
| formal-run | legacy run controls | workspace | toolbar | Admin, Engineer | inspection endpoints | InspectionRuntimeCoordinator | formal run owner | F08 run evidence | MIGRATED | F09-I001 | G4/G6 |
| continuous-inspection | inspection view | `/inspection`, project inspection | top bar | Admin, Engineer | inspection state/events | existing authenticated HTTP/SSE | inspection owner | F08 tests | MIGRATED | F09-I001 | G4/G6 |
| results-evidence-compare | result views | `/results` | top bar | Admin, Engineer, Operator (read only) | result/history endpoints | existing result services | results runtime | F08 result evidence; F09 G3 Operator read-only scenario | MIGRATED | F09-I001 | G4 |
| runtime-package | project export | workspace | workspace action | Admin, Engineer | package endpoint | existing package exporter | package owner | F08 package evidence | MIGRATED | - | G4 |
| stations | station workspace | `/stations` | More | feature/role constrained | station endpoints/events | station command service | stations runtime | F08 station evidence; F09 Operator route assertion pending final-SHA rerun | MIGRATED | F09-I005 | G5/G6 |
| settings-general-storage-runtime-security | settings tabs | `/settings` | top bar | Admin, Engineer | existing settings endpoints | existing settings service | settings owner | F07 settings evidence | MIGRATED | - | G4 |
| database-status-backup | system settings | `/settings` | top bar | Admin, Engineer | database settings endpoint | backup endpoint | settings database panel | F07 settings tests | MIGRATED | - | G4 |
| database-advanced-maintenance | `systemTabs.js` | none | Legacy only | Admin | database service | Legacy controlled endpoint | Legacy settings owner | source audit | DEFERRED_WITH_LEGACY_FALLBACK | F09-I006 | G7 |
| plc-tcp-camera-station-ai-settings | settings tabs | `/settings` | top bar | Admin, Engineer | existing settings endpoints | existing settings services | settings owner | F07 endpoint tests | MIGRATED | F09-I005 | G5 |
| ai-plan-build-handoff | legacy AI panel | `/ai`, project AI | top bar | Admin, Engineer | AgentRun/session endpoints | existing AgentRun authority | AI workbench owner | F06/F08 evidence | MIGRATED | - | G4/G6 |
| overview-diagnostics-about | dashboard/help | `/overview`, `/diagnostics`, `/about` | More | role constrained | local/status endpoints | no new authority | product shell / diagnostics | app mount and navigation tests | MIGRATED | - | G5 |
| startup-cutover-rollback | WebView root | process startup | configuration | profile constrained | StudioOptions | process startup selection | WebView2Host | Profile/rollback scripts and Desktop tests | PARTIAL | F09-I001, F09-I002, F09-I010 | G5/G6 |
| real-host-deployment | WinForms/WebView2/field | n/a | n/a | n/a | real host | existing deployment chain | host/runtime/station | not yet run for F09 SHA | NON_FRONTEND_ACCEPTANCE_GAP | F09-I005 | G5/G7 |

## Product decision

主体能力已具备 Next 承载；仍保留的数据库高级维护和示例工程创建归 Legacy fallback。Operator 已有结果和工作站的只读投影，但 `/inspection`、正式运行、PLC 操作与工程写入仍由服务端拒绝，因此不能把 G3 只读验证写成 G6 运行 Pilot。任何默认入口切换都不得同时挂载 Legacy 与 Next 的写 owner，也不得以 feature flag 的 CSS 隐藏冒充卸载。

## Current TODO execution reconciliation (2026-08-07)

以下细粒度状态以 `AUDIT_BASELINE_HEAD=68e6e4286d008433f804ef90de00c8017184c177` 加当前 scoped working-tree diff 为证据锚点；旧表中的主体迁移结论保留，但不能覆盖这里的操作级状态。

| CapabilityId | 当前操作级状态 | 当前 owner / authority | 当前证据 | Issue |
| --- | --- | --- | --- | --- |
| inspector-file-color-path | `IMPLEMENTED` | FilePickerPort + InspectorOwner -> canonical Flow draft | FilePickerPort/Inspector unit；F03 file picker test | - |
| ai-pending-file-parameter | `IMPLEMENTED_PARTIAL` | AI Pending owner -> shared FilePickerPort；AgentRun 仍是后端 authority | unit + F03 fixture coverage | CV-AUDIT-050 |
| flow-templates | `IMPLEMENTED_PARTIAL_EVIDENCE` | Template owner -> canonical Flow draft -> ProjectSaveCoordinator | template contract/owner unit | CV-AUDIT-046 |
| projects-json-import-export | `BLOCKED_BY_CONTRACT` | 未建立 owner；Project lifecycle remains authority | 当前 API/route audit 未发现 Next contract | CV-AUDIT-047 |
| npoint-calibration-draft | `PARTIAL` | Calibration owner -> draft solve / Project asset formal save | calibration unit + Desktop endpoint 4/4 | CV-AUDIT-045 |
| planar-scale-offset-calibration | `BLOCKED_BY_CONTRACT` | 未建立第二 calibration owner | 当前后端合同取证 | CV-AUDIT-048 |
| global-variables-runtime-values | `IMPLEMENTED_PARTIAL_EVIDENCE` | runtime value owner 与 ProjectSaveCoordinator 分离 | owner unit + current workspace integration | - |
| results-analysis | `IMPLEMENTED_PARTIAL_EVIDENCE` | read-only analysis owner -> existing results queries | analysis contract/owner unit；source switch dispose regression | CV-AUDIT-049 |
| line-sequence-auto-tune | `BLOCKED_BY_CONTRACT` | 不在前端实现检测算法或私有 command | endpoint/authority gap audit | CV-AUDIT-050 |
| station-test-package-deploy | `BLOCKED_BY_CONTRACT` | 不建立第二 Station command owner | Station command/package contract gap audit | CV-AUDIT-050 |
| settings-advanced-maintenance | `DEFERRED_WITH_LEGACY_FALLBACK` | Legacy controlled endpoint；Next 不触碰数据库文件 | 既有 F09-I006 source audit | F09-I006 |

`npoint-calibration-draft` 的正式 asset save 已有 `CanEditProject`，不要因为 draft solve 的权限语义未闭合而把正式保存描述成无权限保护。真实 WebView2、125% DPI、独立 no-Node、现场硬件、Remote CI/Final Gate 和生产 soak 仍属于 `real-host-deployment` 的 acceptance debt。

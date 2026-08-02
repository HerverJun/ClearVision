# ClearVision Studio UI Next F08 G1：Canonical Identity、结果持久化与有效准入加固

## 1. 状态与结论

```text
F08_G1_STATE=DONE
F08_G1_AUDIT=PASS
F08_G1_STOP_CONDITION=NONE
F08_G2_ENTRY=READY_AFTER_G1_COMMIT
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_RETIREMENT=NOT_APPROVED
REMOTE_STATION_IMAGE_UPLOAD=EXCLUDED
```

G1 在既有 `InspectionResultBackgroundService`、`InspectionResultRepository`、history endpoint、`StationPackageStore`、Runtime package validator 与 `RuntimeHost` 上完成 additive 加固。没有新增第二 result endpoint、package store、identity table、HTTP 基础设施、EventBus、RuntimeHost 或 Station authority。

## 2. 实现事实

### 2.1 本机结果 spool 与 persistence snapshot

- `InspectionResultSpoolRecord` 当前写入 `schemaVersion=2`，完整保存 canonical execution/decision outcome、judgment signal/source/reason、error、flow/calibration/session、execution snapshot、project revision、decision hash、runtime package、execution source/run mode/shadow role、output/analysis、image 和 defects。
- reader 接受 schema 0/1/2；未来未知版本进入既有 dead-letter 路径。双轴 outcome 齐全时调用 `SetOutcome`，缺轴旧行使用 `RestoreLegacyResult`，保留 canonical fields 为 null，并只在读取展示时执行 legacy projection。
- `InspectionResultPersistenceSnapshot.WithoutOutputImage` 同样区分 canonical 与 legacy，修复了旧 spool 在 replay 写库前被 `GetOutcome()` 伪升级成 canonical outcome 的退化路径。
- 新增标量形式 `RestoreExecutionTraceability`，只用于从可信持久化字段恢复现有 domain result，不创建新的 identity authority。
- SQLite repository 的 `FindByExecutionSnapshotIdAsync` 已实测可按 project + execution snapshot 精确找到回放后的正式结果，不依赖时间窗或“最新结果”。

### 2.2 History traceability

`InspectionHistoryDetail`、repository projection 与既有 detail response 的 `traceability` 已 additive 补齐：

- `ExecutionSnapshotId`
- `ProjectPersistenceRevision`
- `DecisionConfigurationHash`
- `RuntimePackageId` / `packageId`
- `ExecutionSource`
- `ExecutionRunMode`
- `ShadowRole`

列表合同和现有 endpoint 路径未改变；旧行字段保持 null。

### 2.3 Station package identity

- `StationPackageManifestDto`、`StationPackageRecordEntity` 和现有 `StationPackageStore` 保存 runtime manifest 的 source project id/revision、flow hash 与 decision hash。
- 新 migration `20260802000000_AddStationPackageIdentityProjection` 只增加三个 nullable 列；SQLite schema version 从 6 升为 7，兼容维护入口也可为历史数据库补列。
- runtime manifest 是否包含 revision 由结构化 JSON property 检测，不把缺失字段等同于显式零值；旧 manifest 与旧数据库行重读时 identity 保持 null。
- 没有新增 package metadata store；导入、读取、production filter 与部署继续使用同一个 `StationPackageStore`。

### 2.4 Runtime effective admission

- `RuntimeHost` 在状态切到 `Running`、创建 background execution task、读取输入和 enqueue result 之前，先克隆并应用 active site profile，生成唯一 effective execution snapshot。
- 同一个 effective snapshot 贯穿单次运行和一次目录运行，不再在 worker 内重新读取 profile 或生成另一份 identity。
- profile schema 错误统一返回 `ADMISSION_SITE_PROFILE_INVALID`；effective flow/adaptor/executor validation 返回 `ADMISSION_EFFECTIVE_FLOW_INVALID`。
- package decision hash mismatch 与 missing resources 继续由既有 loader/validator 返回稳定 issue code，并在 writer factory 调用前拒绝。
- applied-profile executor validation 失败时 Runtime 状态保持 `Loaded`，`ResultAvailable` 为空，result writer enqueue 为 0。

## 3. 门禁证据

所有 `.csproj` 测试均通过 `scripts/run-dotnet-test-serial.ps1` 串行执行。

```text
SOLUTION_BUILD=PASS (0 errors, 1 existing System.Collections.Immutable warning)
PRODUCT_G1_PRIMARY=46/46 PASS
  InspectionResultBackgroundServiceTests
  InspectionResultPersistenceSnapshotTests
  InspectionResultRepositoryTests
  RuntimeMvpTests
DESKTOP_G1_PRIMARY=34/34 PASS
  StationPackageStoreTests
  ApiEndpointsInspectionHistoryTests
  VisionDatabaseInitializerTests
  InspectionRunEndpointsTests
PRODUCT_IDENTITY_PACKAGE_REGRESSION=101/101 PASS
  RuntimePackageExporterValidationTests
  RuntimePackageExporterTests
  RuntimePackageLoaderTests
  InspectionRuntimeCoordinatorTests
  InspectionServiceSingleRunTests
  InspectionWorkerTests
DESKTOP_STATION_REGRESSION=25/25 PASS
  StationResultMapperTests
  StationRegistryServiceTests
  StationPackageDeploymentServiceTests
STATIC_CODE_AUDIT=PASS
GIT_DIFF_CHECK=PASS
```

构建中的 `System.Collections.Immutable` 8.0/9.0 解析 warning 来自既有 `OperatorLibraryReadOnlyAuditRunner` 依赖图；本轮未修改该工具，warning 未导致 build/test 失败，也不改写为零警告。

## 4. 计划门禁逐条结论

| 门禁 | 结论 | 证据 |
| --- | --- | --- |
| 数据库失败、落盘、重启、回放后完整 outcome/identity/defects | PASS | v2 spool 故障回放测试与 exact snapshot lookup |
| 旧 spool / 旧 Station package additive 兼容，不伪造 identity | PASS | 手写 schema 0 fixture、旧 manifest、旧 SQLite row 测试 |
| persistence snapshot、repository、history endpoint、Formal reconciliation identity 一致 | PASS | primary + 101/101 identity/package regression |
| exporter/loader/validator 与 Station store/mapper/deployment 合同兼容 | PASS | 101/101 + 25/25 |
| profile、decision mismatch、resource missing 在 runtime side effect 前失败并返回稳定码 | PASS | RuntimeHost admission tests；writer factory/enqueue 均为 0 |
| migration nullable、旧数据 null/unknown | PASS | migration、model snapshot、database maintenance 与 initializer/store tests |

## 5. 停止条件审计

- spool replay 不再调用 `SetResult`；canonical 双轴与 execution snapshot 不丢失。
- Formal reconciliation 继续使用 `ProjectId + ExecutionSnapshotId` exact lookup；没有新增时间窗猜测。
- site profile 规则仍由 Runtime validator/admission 单一维护，前端没有复制规则。
- migration 不删除、不重写现有结果或 package identity；无法证明的旧值保持 null。

因此 G1 停止条件均未触发，审计结论为 `PASS`。

## 6. 未执行证据边界

```text
BROWSER_PLAYWRIGHT=NOT RUN
REAL_WEBVIEW2=NOT RUN
WINDOWS_DPI_MATRIX=NOT RUN
RELEASE_PUBLISH=NOT RUN
NO_NODE_TARGET=NOT RUN
REMOTE_CI=NOT PERFORMED
REAL_STATION_CAMERA_PLC_TCP=NOT PERFORMED
```

G1 是后端 identity/persistence/admission 加固，不改变默认入口或前端视觉。上述证据不能由本轮 build/unit tests 替代，留待相应后续 Goal/final evidence 门禁。

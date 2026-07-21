# Studio UI Next F04-R G2 Run、Result、Evidence 与 Runtime Package 合同

> 状态：`FROZEN`
> 代码事实基线：`483a212783d4bc66f9f434e0a22de4be944e46c7`

## 1. Formal Run outcome 合同

Formal Run 只消费已保存 Project snapshot。Preview、草稿 Flow、相机捕获帧和浏览器缓存都不能作为正式执行 authority。现有 admission/execute/stop/reconcile endpoint 和 `runCommandOwner` 是唯一执行入口。

| outcome | Workspace 落点 | 允许动作 | 禁止动作 |
|---|---|---|---|
| Completed OK | 留在当前 Workspace；显示 execution succeeded + judgment OK、耗时、result identity、摘要 | “查看本次结果”带真实 `projectId`、`resultId`/run identity；继续查看 Canvas/Inspector/Preview | 自动跳 Results；把 judgment OK 当作执行成功唯一字段 |
| Completed NG | 留在 Workspace；显示 execution succeeded + judgment NG、原因/来源、result identity | 同上；允许打开本条结果和 evidence | 显示为 execution failed；自动跳页 |
| Failed | 留在 Workspace；显示执行阶段、诊断码/原因、影响和恢复动作 | 重试前按原因修复；若已有 unknown identity 先 reconcile | 生成伪造结果、跳空详情、自动重复 execute |
| Cancelled | 留在 Workspace；显示停止阶段和取消原因 | 返回就绪前先等 stop/reconcile authority；允许查看已存在的明确结果 | 把取消当 NG；跳空详情 |
| Unknown outcome | Workspace mutation gate 锁定重复写入；显示 identity 和“结果未知” | 只调用现有 reconcile；以权威结果恢复；未确认前不允许再次运行 | 自动重试 execute/stop；用本地 toast 判定成败 |

## 2. Run identity 与恢复

每次 admission、execute、stop、reconcile 关联：

```text
projectId
clientSnapshotId / executionSnapshotId
PersistenceRevision
canonicalFlowHash
decisionConfigurationHash
```

`InspectionResult` 的 traceability 必须能回到 `projectId`、result/run/session、Flow hash、decision hash、Project revision；UI 本地 revision 只用于 draft/stale，不得填充正式 identity。当前 run execute/reconcile response 已带 Project revision 与 decision hash，但 history detail 的 `traceability` 只投影 Flow hash、calibration、session/run，并把 package/station 置空；Prompt 3 必须在既有 detail DTO/endpoint 上补齐可获得的正式 identity，不能由前端拼接。

### 2.1 取消和离开

- Preview 的 AbortController 取消不等于 Formal Run Stop。
- Formal Run 已 admission 后离开 Workspace 由 Leave Guard 阻止或要求处理；Stop 请求完成前不卸载 run owner。
- 401 后 `AuthLifecycleOwner` quarantine ProjectRuntime，保留 run identity，重新认证后先 reconcile。
- 403 是 policy 结果；不重试；404 identity mismatch 终止当前操作并回到工程/结果列表。

## 3. Results 最小闭环

### 3.1 当前单条结果详情

既有 `GET /api/inspection/history/{projectId}/{resultId}` 返回 execution/judgment 双轴、defects、时间、traceability、image reference、diagnostic、output/analysis preview 和 Evidence summary。Next 必须把真实 `projectId`、`resultId` 放入 query/route，不从全局“最近结果”猜测。

首屏顺序（缺失字段须由既有 endpoint 投影补齐后再展示）：

1. execution outcome 与 judgment outcome 分开显示；
2. result ID、Project ID、运行/快照 identity、Flow/decision hash；
3. 诊断码、中文原因、影响和下一步；
4. 图像/输出数据/分析摘要；
5. Evidence manifest 状态和本条导出。

当前 Next `resultsContracts.ts` 已解码双轴和 traceability，但尚未解码 `hasEvidenceManifest`、`evidenceStatus`、manifest reference、retention/checksum 等字段；这是 G3 结果 capability 的明确文件边界，不得复制 Legacy result panel。

### 3.2 Evidence manifest

正式 authority 是 `IInspectionEvidenceManifestService` 与：

```text
GET /api/inspection/history/{projectId}/{resultId}/evidence/manifest
GET /api/inspection/history/{projectId}/{resultId}/evidence/export
```

Manifest v1 包含 `ManifestId`、Project/Result identity、Status/Outcome、Flow hash、CalibrationBundleId、Run/Session、retention、TotalBytes、items、checksum、redaction summary。Item 必须保留 role、MIME、相对路径、size、SHA-256、可用性和缺失原因。

Evidence 状态：

| status | UI 语义 | 操作 |
|---|---|---|
| `available` | 清单可用 | 显示摘要、checksum/retention；允许当前单条导出 |
| `partial` | 部分条目不可用 | 列出缺失原因；仍允许导出并显示 omissions |
| `expired` | retention 已过期 | 显示不可恢复/保留期限；不伪造文件 |
| `missing` | 未生成或已清理 | 显示证据缺失原因；不把详情失败当作结果失败 |
| `disabled` | 服务配置关闭 | 显示“证据未启用”；不显示下载按钮为成功 |
| `401/403/404/409/413` | transport/policy/identity/大小错误 | 保留结果上下文，按 HTTP 语义恢复；不自动重试导出 |

当前单条 evidence export 是 F04-R 范围；批量导出、compare、realtime、KPI/趋势/CPK/MTBF 延后 F05。

## 4. Runtime Package 合同

### 4.1 入口与前置条件

F04-R 交付资产固定为 Admin Runtime Package，使用既有：

```text
POST /api/projects/{id}/runtime-package/export
policy = RequireAdmin
authority = RuntimePackageExporter + StationPackageStore
```

Next 请求不携带 draft `Flow` override；工程必须先通过统一 Project save 并以 fresh Project revision 为准。导出前界面显示：工程 ID/名称、`PersistenceRevision`、dirty 状态、资产/变量/参数校验结果、包身份“由服务端生成”。成功后显示 PackageId/FlowHash；`RuntimePackageManifest` 已有 DecisionConfigurationHash，但当前 endpoint success response 未投影该字段，Prompt 3 必须在同一 endpoint 上补充响应投影后才显示。客户端不生成或持久化包格式。

RuntimePackageExporter 当前会阻止：参数错误、缺失资源、疑似 secret、GlobalVariables validation error、资产 revision/hash 不一致；成功后生成 manifest 并可注册 Station package。正式 package id 是服务端生成的 `cvpkg-...` identity。

### 4.2 真实状态

| 状态 | 冻结行为 |
|---|---|
| success | 显示 PackageId、PackageName、FlowHash、DecisionHash、路径/注册状态；浏览器下载不是 authority |
| 401 | 回登录，保留 project/revision，不能静默再次导出 |
| 403 | 中文显示“需要管理员权限”；不显示 disabled 假成功 |
| 404 | 工程不存在；返回工程列表，不猜缓存 |
| 409 | 工程运行中、revision/asset conflict 或已有 mutation 占用；保留 dirty/draft，要求保存/停止/协调 |
| validation 400 | 按 `RPA*`/变量/参数问题定位前置条件；不创建伪包 |
| network failure | 状态为“导出结果未知”，不改写为失败，不自动重试；让 Admin 按 Package/Station 列表和服务日志核对后再由用户明确发起下一次导出 |
| unknown outcome | 现有 endpoint 没有 export mutation id/reconcile contract；因此 UI 必须锁定自动重试，记录 project/revision/请求时间，必要时通过既有 `GET /api/station-packages`（RequireStationAdmin）核对已注册包，不能声称已导出 |

可编辑工程 import/export 不属于 F04-R；Legacy JSON 只作历史语义参考，不能变成正式格式或 Next 私有 authority。

## 5. Owner 与文件边界

| Capability | 唯一 owner / query | Prompt 3 边界 |
|---|---|---|
| Run | `runCommandOwner` | `runContracts.ts`、owner 和 Workspace Run 状态；不新增 EventBus/执行服务 |
| Workspace result handoff | `WorkspacePage`/Workspace owner projection | 改为留在 Workspace + 明确 link；query 必带 identity |
| Local results | Results route query owners + shared `ReadQueryClient` | `resultsContracts.ts`、`resultsQueries.ts`、detail/evidence adapter；不复制 Legacy result panel |
| Evidence | Results capability-local read/download adapter | 只读 manifest/export；blob cleanup 归 shared transport/owner |
| Runtime Package | Project/Workspace capability-local export owner | 复用 shared transport + existing endpoint；不创建前端 zip/JSON authority |

## 6. 状态

```text
RUN_RESULT_EVIDENCE_CONTRACT=FROZEN
RUNTIME_PACKAGE_CONTRACT=FROZEN
RESULTS_SINGLE_DETAIL_SCOPE=FROZEN
EVIDENCE_SINGLE_RESULT_EXPORT_SCOPE=FROZEN
BATCH_COMPARE_REALTIME_ANALYTICS=F05
IMPLEMENTATION=FORBIDDEN
```

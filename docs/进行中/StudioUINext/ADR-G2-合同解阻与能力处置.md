# ADR-G2：合同解阻与能力处置

状态：`DONE`（合同处置已冻结）
日期：2026-08-09
适用分支：`studio-ui-next`
实现 checkpoint：`98cb8c7f54d2d51ea5b59ca534aafd51544b773f`
关联台账：[F10_ContractAndProductionPlan.md](./F10_ContractAndProductionPlan.md)

## 决策范围

本 ADR 是 G2 的合同和能力处置记录。它不创建第二 API、第二保存链、第二资源库、第二运行权威或第二
Canvas owner。Project、Flow、GlobalVariables 和正式 assets 仍由现有 Application Service 与
`ProjectSaveCoordinator` 负责；Runtime、Inspection、Station 和 AgentRun 仍是各自领域的权威。

`MIGRATE` 表示当前后端合同足够，Next 只做受控投影；`RELOCATE` 表示入口应回到已有领域 owner；
`DEFER` 表示先保留 Legacy/既有后端边界，缺口补齐前 Next 不暴露新的写入口；
`RETIRE_WITH_APPROVAL` 表示只有获得产品 owner 的单独批准后才可删除旧入口。本轮由主协调 Owner
冻结 `DEFER` / `RELOCATE` 决策，作为 G2 的正式退出结论；`DEFER` 不等于能力已迁移、生产验收或
Legacy 退役，未来重新进入仍需对应产品/后端 owner 批准新的合同与实施范围。

## 合同矩阵

| 项目 | 处置 | 唯一 owner / authority | 权限 | 并发身份 | 错误与 reconcile | 当前状态 |
| --- | --- | --- | --- | --- | --- | --- |
| G2.1 AI attachment | `DEFER` | 待后端 `AgentRun` resource store；Next 不持有本地路径或 Blob authority | 上传者、Project/AgentRun 可见性和下载权限必须由后端判定 | `resourceId + version`，上传和引用都需 `clientOperationId` | 上传、过期、撤销、恢复和未知提交必须有状态查询；未具备前不允许恢复到 Flow | `DEFERRED` |
| G2.2 CV model artifact | `DEFER` | 待明确 Project asset/model registry；`/api/ai/models` 仅是 LLM provider/model 配置 | Project 资产权限与模型发布权限分离 | `modelAssetId + version/contentHash` | 缺失、版本不兼容、撤销、下载失败和未知发布必须可查询；Next 不把 `ModelPath` 当资产 ID | `DEFERRED` |
| G2.3 TemplateMatching artifact | `DEFER` | 待 Project asset/template artifact owner；`/api/templates` 只拥有 Flow template | Project 读写和模板使用权限由后端决定 | `templateArtifactId + version/contentHash` | 产物缺失、跨工程引用、过期、权限拒绝和版本冲突必须有结构化错误与对账 | `DEFERRED` |
| G2.4 Calibration projection | `DEFER` | 现有 Project calibration asset owner；待新增正式 asset 到 numeric `Scale/Offset` projection contract | 沿用 calibration asset 与 Project 编辑权限 | `assetId + assetVersion + persistenceRevision` | 投影必须返回 source asset、单位、数值、适用节点和 revision；缺失/过期/单位不兼容不得静默回退 | `DEFERRED` |
| G2.5 Database advanced | `DEFER` | Settings 后端 Admin maintenance service；Next 当前只读 status/安全 backup 投影 | Admin-only；repair/restore/cleanup/reset 逐项授权 | `clientOperationId + databaseRevision/backupId` | 备份前置、互斥、审计、超时、unknown-outcome、状态查询和恢复步骤缺一不可；Next 不提供破坏性按钮 | `DEFERRED` |
| G2.6 Station test package/device command | `MIGRATE` | 既有 Station package/command endpoint 与 Next Station owner | Station Admin/既有 command policy | package identity、target Station、`clientRequestId` | 过期、重复请求、取消、查询和终态 reconcile 已由既有 endpoint/owner 投影；现场联调仍留在 G6 | `DONE`（合同） |

### 可实施合同

#### GlobalVariables

- 唯一写入口仍是 `WorkspaceGlobalVariablesOwner`；Flow 节点和端口只从当前
  `FlowCanvasOwner.projection.draft` 读取，不复制一份 Flow catalog。
- 绑定候选只保留可映射到 `String`、`Int64`、`Double`、`Boolean` 的端口/参数。`Image`、集合、几何、
  `Any` 和未知类型不会作为标量变量候选。
- 应用前再次按 `operatorId + outputPortId` 或 `operatorId + parameterId` 查当前 Flow；算子、端口、参数
  不存在或类型不兼容时阻断进入工程保存草稿，使用 `GV009`、`GV010`、`GV011`、`GV014`、`GV015`。
- 定义和绑定的正式保存继续进入现有 Project save chain；运行值继续使用后端版本和现有
  `committed/rejected/unknown-outcome/reconciled` 语义。运行值未知时不自动重发。
- 权限、PersistenceRevision 冲突和后端字段诊断仍由现有 Project Service/SaveCoordinator 处理；本次没有
  新增 GlobalVariables endpoint。

#### Line Sequence Preview

- `LineSequenceOwner` 继续是唯一 capability owner，分析使用现有
  `POST /api/autotune/flow-node/preview`，推荐使用现有 `POST /api/autotune/scenario`，二者均由后端
  `Engineer/Admin` 权限和 `AutoTunePreview` admission 约束。
- 最近图像只从同一工作区的 `PreviewOwner` 投影读取，优先使用后端返回的输入图，其次使用输出图；结果过期、
  流程变化或预览未完成时不发送旧图。该输入只用于当前预览请求，不写入 Project/localStorage。
- 请求会携带 `InputImageBase64`；响应的 `InputImageBase64`、`PreviewImageBase64`、`Outputs` 和 scenario
  `FinalPreview` 在合同边界解码后投影到线序工作台。Apply 仍只通过
  `FlowCanvasOwner.commands.patchNodeParameters` 修改草稿，正式保存仍由 Project save chain 完成。
- 线序 AI parameter-only follow-up 暂 `DEFER`：当前 Next 没有已批准的跨 capability AI composer/queue
  contract。诊断和建议继续显示在现有线序工作台；不得自行调用 AI endpoint、复制第二 session owner 或把
  follow-up 写入 localStorage。

## 旧版能力处置

| 项目 | 处置 | Next 入口/保留边界 | 影响、fallback 与重新进入条件 |
| --- | --- | --- | --- |
| G2.7 N 点标定高级工作流 | `DEFER` | 当前 Workspace Inspector 保留采集、编辑、solve、candidate、正式 asset save；不复制旧版 JSON/候选提取器 | 9 点模板、粘贴导入、备注/排序、像素编辑、复制/JSON 导出和 overlay 仍以 Legacy/后端为准。补齐统一 observation/asset projection、权限、revision 和误差 overlay 合同后重新进入 |
| G2.8 GlobalVariables | `MIGRATE` | Workspace 的全局变量工作台；定义/绑定进入现有保存链，运行值进入既有 runtime endpoint | 已实现候选类型过滤、当前 Flow identity 校验和兼容性错误；由 owner/unit tests 与后续全量门禁验证 |
| G2.9 通用 AutoTune | `DEFER` | 现有 generic endpoint 保留；Next 只提供已冻结的线序场景，不伪造 Thresholding、Filtering、GaussianBlur、BlobAnalysis、SharpnessEvaluation 入口 | 需要按算子逐项确认目标、图像输入、参数白名单、权限、admission、operation identity 和结果 reconcile 后再迁移 |
| G2.10 Line Sequence | `MIGRATE`（预览）+ `DEFER`（AI follow-up） | Inspector 线序工作台，使用 Preview owner 的最近图并展示后端预览图 | 设备写入、正式保存和 AI follow-up 不在当前入口；分别满足 command authority 或跨 capability AI 合同后再扩展 |
| G2.11 连续检测保护/恢复 | `RELOCATE` | Runtime/Inspection 执行协调、互斥和现场恢复策略；Next 只投影状态 | 缺料超时、连续 NG 保护和恢复策略不得由前端 timer/lock 重建。需要现场 Runtime/Station 证据后再补 UI 投影测试 |
| G2.12 Demo/示例工程 | `RELOCATE` | 使用后端 `/api/demo/create`、`create-simple`、`guide` 和受控 Project lifecycle | Next 不复制 Flow JSON。若要成为产品入口，需后端创建/权限/保存/reconcile 合同与项目 owner 批准；否则保持受控 demo/Legacy-only |
| G2.13 Camera 标定与 Settings 高风险操作 | `MIGRATE`（标定）+ `RELOCATE`（Settings） | Camera 标定落在 Workspace NPoint owner；系统高风险维护落在 Settings Admin 边界 | 不用隐藏入口替代决策。Database repair/restore/cleanup/reset 合同批准前保持不可用；真实 Camera/Settings 验收留到 G6 |

## 错误、状态和测试语义

- 中文状态必须区分 `未授权`、`无权访问`、`资源缺失`、`类型不兼容`、`草稿已过期`、`请求已取消`、
  `结果未知` 和 `已协调`；不能把权限错误显示为空数据。
- 当前 G2 工作树定向证据：GlobalVariables owner `7/7`，Line Sequence owner/contract `11/11`，
  TypeScript `typecheck` 通过。完整 lint、全量 unit、build、Browser、.NET、WebView2、DPI、no-Node、
  Remote CI、现场硬件和生产 soak 不因这些定向结果自动通过。
- 五个合同缺口已由主协调 Owner 冻结为 `DEFERRED`；它们没有 Vue 私有替代实现，重新进入条件完整，
  因此 G2 合同决策 Gate 为 `DONE`。这不表示延期能力已迁移，也不改变 G6 的现场与产品验收门禁。

## 重新进入清单

以下延期能力未来重新进入时，必须先取得 owner 签字、接口契约、错误矩阵和当前 SHA 证据：attachment
resource、CV model artifact、TemplateMatching artifact、calibration asset projection、database advanced，
以及 N 点高级工作流、generic AutoTune 和 Line Sequence AI follow-up。重新进入不是本次 G2 退出条件；
每次重新进入仍必须先刷新远端、冻结 clean candidate，再更新本 ADR、F10 和 `TODO.md`。

# ADR-ParityAlignment-Wave0：行为、风险与处置冻结包

状态：`APPROVED`
提案日期：2026-08-14
批准日期：2026-08-23
签字人：`HerverJun`（声明有权代表 Product、Security、QA/Release 及本 ADR 涉及的 capability owner）
适用分支：`studio-ui-next`
审计基线：`22a3d26a00a2d3b8098165aab5489ce54f5bc95b` 加审计时 dirty working tree
关联计划：[功能对齐 TODO 计划](../../归档/历史归档/2026-08-14-StudioUI-Parity-Alignment-Review-22a3d26/08_Parity_Alignment_TODO.md)
关联台账：[F10_ContractAndProductionPlan.md](./F10_ContractAndProductionPlan.md)

## 状态与边界

本文件为本次 parity alignment 的 Wave 0 签字包，不改写 F10 中历史 G0/G1/G2 checkpoint 的结论。
它记录当前代码与 Legacy 语义的已核事实、已批准的 Option D G0 范围决定，以及后续 Gate 不得越过的 authority 边界。
2026-08-23 的具名批准将 G0-01、G0-03、G0-04 和 G0-05 在本轮范围内结案；G0-02 明确为 `DEFERRED`，
subgraph 明确为 `NOT_APPLICABLE`。延后或不适用不是功能 PASS，也不授权私有 endpoint、第二 owner 或替代状态机。

本 ADR 不创建第二 API transport、HostBridge、Canvas、ImageCanvas、EventBus、ServiceRegistry、
Project save client 或 Station command authority。Project、Flow、GlobalVariables 和正式 assets 仍由
既有 Application Service 与 `ProjectSaveCoordinator` 负责；Preview、Inspection、Runtime 与 Station
仍使用既有 authenticated HTTP/SSE 与 lifecycle owner。

## G0-01 Canvas 行为合同

### 已核事实

| 主题 | 当前事实 | 代码锚点 |
| --- | --- | --- |
| Legacy 运行到节点 | 右键菜单在 `nodeRunEnabled` 时提供“运行到此节点/调试预览”；其实现先设置 active node，再请求当前节点预览。 | `wwwroot/src/core/canvas/flowCanvas.js:2766-2768,2919-2932` |
| Next Canvas | Canonical FlowCanvas 仍为唯一 Canvas kernel，但显式设置 `nodeRunEnabled = false`。 | `StudioUI/src/platform/canvas/canonicalFlowCanvas.ts:297,711-712` |
| Preview 请求 | 已有唯一 Preview transport：`POST flows/preview-node`，请求/响应 identity 包含 `projectId`、`targetNodeId`、`debugSessionId`、`clientRequestSequence`、`flowRevision`。 | `StudioUI/src/capabilities/project-workspace/preview/previewTransport.ts:14-20,217-230`; `previewContracts.ts:13-19,256-290` |
| Legacy 双击 | 旧 Canvas 对节点双击调用 `onNodeDoubleClicked(node)`。 | `wwwroot/src/core/canvas/flowCanvas.js:2435-2440` |
| Next 子图 | 未发现 Next subgraph cursor、breadcrumb、enter/leave command 或其数据合同。 | 审计范围：`project-workspace/flow/` 与 `canonicalFlowCanvas.ts` |

### 已批准的本轮处置

| 行为 | 本轮处置 | 约束 |
| --- | --- | --- |
| 运行到节点 | `DEFERRED`；不纳入本轮 Option D | 保持 canonical FlowCanvas 当前全流程正式执行语义；不新增入口、快捷键、运行模式或状态模型 |
| active node | `DEFERRED`；不纳入本轮 Option D | 不新增 active-node 投影、选择语义或持久化字段 |
| 现有 Preview stale / cancel | `RETAIN_CURRENT` | 继续由现有 Preview owner 按 project/node/debugSession/requestSequence/flowRevision 取消和丢弃晚到结果；不扩展为 run-to-node |
| 双击 / subgraph | `NOT_APPLICABLE` | 本轮 deterministic fixture 不包含 subgraph host；不新增 child flow、breadcrumb、嵌套、保存或 leave 语义 |

### 统一状态与错误矩阵

| 情况 | 必须投影的中文状态 | 实现边界 |
| --- | --- | --- |
| 不支持的节点 | `此节点不支持调试预览`，并给出原因 | 不发送请求；不得用 disabled 按钮隐藏原因。 |
| 无权限 / 未认证 | `没有调试预览权限` / `登录状态已失效` | 沿用 authenticated HTTP 的 403/401；不创建 WebMessage 旁路。 |
| 资源或输入缺失 | `预览无法开始：{资源或输入原因}` | 使用现有 Preview diagnostics/missing-resources 投影。 |
| 请求取消或已过期 | `调试预览已取消` / `调试预览已过期` | 不展示旧图或旧 artifact；不自动重发。 |
| 执行失败 | `调试预览失败：{节点或诊断原因}` | 不把 Preview failure 表示成 Formal Run failure。 |
| owner dispose | 不保留请求、SSE、timer、controller 或写操作 | 生命周期仍由既有 owner 负责。 |

### 签字条件

`HerverJun` 已于 2026-08-23 代表 FlowCanvas、Preview/Run 和 Product owner 批准上述本轮处置。
`W1-WS-01` / `W1-WS-02` 不进入本轮 Option D；未来重新进入时必须新建合同与 fixture，不得从本次 G0 PASS 外推授权。

## G0-02 Inspector 参数推荐合同

### 已核事实

| 主题 | 当前事实 | 代码锚点 |
| --- | --- | --- |
| Endpoint | 现有 `POST /api/operators/{type}/recommend-parameters` 接收 `{ ImageBase64 }`；当前 endpoint 本身未见 `.RequireClearVisionPermission(...)`，相邻 endpoint 的 Engineer/Admin 链不能外推给它。 | `Endpoints/ApiEndpoints.cs:77-80,1672-1691` |
| 后端 allowlist | 当前 recommender 只对 Thresholding、Filtering/GaussianBlur、BlobAnalysis、SharpnessEvaluation 返回参数；其他算子返回空字典。 | `Infrastructure/Services/ParameterRecommender.cs:13-27` |
| 输入失败 | 缺失、格式错误、解码失败返回 400；超过 25 MiB 返回 413。 | `Endpoints/ImagePayloadDecoder.cs:7-18,20-151` |
| Next 缺口 | Inspector 当前只有参数草稿编辑；没有 recommendation owner、candidate diff、accept/revert 或 endpoint caller。 | `StudioUI/src/capabilities/project-workspace/inspector/InspectorPanel.vue` |
| Legacy 权限行为 | Legacy recommendation button 只按 operator type 显示，前端不检查 role；当前 endpoint 也没有权限 filter，故 Operator 可能已能调用。 | `wwwroot/src/features/flow-editor/propertyPanel.js:428-443,2295-2298,2358-2379` |

### 未来重新进入时的合同边界

| 项目 | `PROPOSED` 合同 | 禁止事项 |
| --- | --- | --- |
| 唯一 owner | 新能力只能由 `inspectorOwner` 承载；它调用既有 API transport，且在 selection/flow revision/owner dispose 时 abort 并丢弃晚到响应。 | 复制 Line Sequence owner、创建第二 recommendation transport 或在组件内直接持有请求 controller。 |
| 触发输入 | 只使用当前 Preview owner 的明确当前图像投影；没有图像时不发请求，并说明“当前没有可用于推荐的预览图像”。 | 使用 localStorage、历史图像或非当前工程图像作为隐式输入。 |
| 权限 | Product 与 backend owner 必须选择并签字：`Engineer/Admin`（建议，与既有 Project edit policy 对齐）或明确允许 Operator。无论选择哪种，都要有 endpoint-specific authorization、可审计策略证据和不同的 401/403 中文投影。 | 依靠调用方隐藏按钮、把相邻 endpoint 权限外推给本 endpoint，或在前端自行判定权限。 |
| Candidate | endpoint 返回值解码为只读 candidate diff，显示参数名、当前值、建议值、可预览状态和来源 operator。 | 直接 patch Flow、直接保存 Project、把本地 revision 作为 `PersistenceRevision`。 |
| accept / revert | accept 仅调用 canonical `FlowCanvasOwner.commands.patchNodeParameters` 写入 Flow draft；revert 只丢弃 candidate；正式保存继续由 `ProjectSaveCoordinator` 完成。 | 在 accept 或 preview 时调用 Project save endpoint。 |
| 不支持 | allowlist 外的 operator 在入口处说明不支持；空 recommendation 必须区分“无可用建议”和 transport/validation failure。 | 用空结果伪装成功，或把未支持算子交给通用 AutoTune。 |

### 需补齐的测试样例

1. allowlist 的四个 operator 各有可解码 candidate；不支持 operator 不发请求或得到明确不可用状态。
2. 400、413、401、403、网络失败、validation error、空 recommendation、stale selection 和 dispose 都不污染当前 inspector。
3. preview 仅为可丢弃调试投影；accept/revert 与并发参数编辑可区分；accept 后仍需经过正式 save/reload 与 `PersistenceRevision` 冲突路径。

### 签字条件

本轮决定为 `DEFERRED`：保持当前 Inspector 参数编辑与校验能力，不调用 recommendation endpoint，
不新增 candidate/accept/revert UI。`W1-WS-03` 不进入本轮 Option D；未来重新进入时，仍需 Inspector owner、
ParameterRecommender backend owner 与 Product owner 对上述边界另行签字。

## G0-03 Station 命令风险矩阵

### 已核事实

| 主题 | 当前事实 | 代码锚点 |
| --- | --- | --- |
| 命令枚举 | `Ping`、`StartRuntime`、`StopRuntime`、`ReloadPackage`、`DeployPackage`、`ApplySiteProfile`、`CollectLogs`。 | `Runtime.Abstractions/StationSyncContracts.cs:90-99` |
| 既有 authority | 命令与部署 endpoint 均要求 Station Admin，并以 `clientRequestId` 提供幂等身份；部署有 package/station/admission 检查。 | `Endpoints/StationEndpoints.cs:123-299,538-553,681-700` |
| Legacy 语义 | Legacy 对停止、正式包部署、测试包下发使用确认；确认文本显示 Station、影响、包与审计提示。 | `wwwroot/src/features/stations/stationMonitorView.js:793-795,909-931,1232-1236` |
| Next 缺口 | 既有 Next Station owner 能投影提交与 reconcile，但未保留风险分级确认。 | `StudioUI/src/capabilities/stations-read/` |

### 未来扩展时的风险表（本轮不实施）

| 命令 | 已核事实 | 待批准的风险等级与确认要求 |
| --- | --- | --- |
| `Ping` | 诊断性命令 | 明确是否允许无确认；若需要审计，使用既有 command identity。 |
| `CollectLogs` | 可能导出诊断数据 | 明确数据敏感度、审计字段与是否需要普通确认。 |
| `StartRuntime` / `StopRuntime` | 影响现场运行节拍 | 明确是否要求 Station 名称输入、二次授权、运行中禁用和超时处理。Legacy 至少对停止要求确认。 |
| `ReloadPackage` / `ApplySiteProfile` | 可能改变运行配置 | 明确配置影响、是否需要目标名称输入、回退/恢复和审批边界。 |
| `DeployPackage` | endpoint 仅接受 Production package 且有 station/package admission | 明确是否要求 Station 名称与 package identity 双确认、二次授权以及 unknown outcome 时的 reconcile-only 行为。Legacy 至少要求普通确认。 |

### 确认和结果合同

- 确认 UI 只能由 `stationAdminCommandOwner` 的窄 command API 驱动；modal 不能直接调 API。
- 确认必须显示 Station 名称/ID、当前连接和运行状态、命令、操作者、`clientRequestId`、可用时的 package
  name/version/hash/Flow identity，以及对现场的影响。
- 取消不创建请求。提交后禁用重复提交；网络未知或 owner dispose 只能按既有 `clientRequestId` 查询/reconcile，
  不得无身份重发。
- 中文结果至少区分未授权、无权、目标不存在、准入拒绝、提交中、结果未知、已协调、已接受、执行中、成功、失败、
  超时和已取消。

### 签字条件

`HerverJun` 已于 2026-08-23 代表 Station command、Security 与 Product owner 批准
`APPROVED_RETAIN_CURRENT`：保持现有 `StationAdminCommandOwner`、后端准入和 reconcile 行为；
本轮不新增高风险命令、确认弹窗或命令入口。未来扩展才需对上述风险表另行签字。

## G0-04 产品处置板

下表记录 2026-08-23 具名批准的 Option D 本轮处置。处置不删除后端 endpoint，不授权新 Next 写入入口，
也不把 Legacy fallback 保留外推为 Legacy 退役批准。

| 能力 | 当前事实 | 已批准处置 | 约束 | 签字人 | 目标波次 | 当前状态 |
| --- | --- | --- | --- | --- | --- | --- |
| Demo / 示例工程 | 后端 demo create/guide 仍在，Next 不得复制 Flow JSON。 | `RETAIN_LEGACY_FALLBACK` | 本轮不新增 Option D Demo UI | `HerverJun` | 本轮 | `APPROVED` |
| 独立本地图像加载 | Legacy 有独立调试入口。 | `RETAIN_LEGACY_FALLBACK` | 不绕过 `FilePickerPort` / ImageCanvas owner | `HerverJun` | 本轮 | `APPROVED` |
| Runtime Preview Pilot | 当前只允许 default-off、metadata-only、developer-only 的内部 pilot。 | `RETAIN_LEGACY_FALLBACK` | 继续 default-off/internal-only，不包装为正式能力 | `HerverJun` | 本轮 | `APPROVED` |
| Station token 安全分发 | 当前只支持 regenerate，长期明文 reveal 已被后端排除。 | `RETAIN_CURRENT_REGENERATE_ONLY` | 不显示明文，不实现 preserve/replace | `HerverJun` | 本轮 | `APPROVED` |
| Storage cleanup | 破坏性操作缺少 scope、backup、operation identity、审计与 reconcile 合同。 | `RETIRE_WITH_APPROVAL` | 本轮不提供破坏性入口 | `HerverJun` | 本轮 | `APPROVED` |
| 工程/版本/FPS 持续状态 | P3，需先满足 125% 空间预算，不得挤压 Canvas/Inspector/Preview。 | `RETAIN_LEGACY_FALLBACK` | 等待 DPI budget，不挤压 Workspace 核心面 | `HerverJun` | 本轮 | `APPROVED` |

## G0-05 Fixture 与证据目录

已冻结单一可复现工程 fixture，包含普通节点、Preview、ROI、双向全局变量绑定、正式判定和正式结果证据。
subgraph 按 G0-01 记为 `NOT_APPLICABLE`，不是 fixture 缺口。每个证据运行必须写入：

```text
.tmp/studio-ui-next/parity-alignment/<wave>/<sourceSha>/<runId>/
```

每个 manifest 至少记录 `sourceSha`、working-tree diff identity、profile、Windows scale、native DPI、
client/viewport size、fixture identity、配置、用户/权限、覆盖的成功与错误状态、请求/operation identity、
owner cleanup、端口/user-data/database/result/publish cleanup。未执行的证据明确写 `NOT RUN` 或
`NOT PERFORMED`。发布产物只能写入 `.tmp/publish-check/`。

冻结代码：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/e2e/studio-ui-next/option-d-g0-deterministic-fixture.ts`。
验收用例：`f03-workspace.spec.ts` 中 `Option D G0 consumes one frozen Project, Preview, Run and Results evidence fixture`。
该用例实际通过，并证明初始草稿未产生 PUT、Preview 不冒充 Formal Run/Results、离开 Workspace 后全部 owner/resource 归零。

## 批准记录

下列决定由 `HerverJun` 于 2026-08-23 具名批准；其授权声明覆盖表中全部必需角色。

| 范围 | 必需批准人 | 批准状态 | 日期 / 证据 |
| --- | --- | --- | --- |
| G0-01 Canvas 行为合同 | FlowCanvas owner、Preview/Run owner、Product owner | `APPROVED` | `HerverJun / 2026-08-23`；run-to-node/active-node `DEFERRED`，subgraph `NOT_APPLICABLE` |
| G0-02 Inspector 推荐合同 | Inspector owner、ParameterRecommender backend owner、Product owner | `DEFERRED` | `HerverJun / 2026-08-23`；保持当前编辑/校验 |
| G0-03 Station 风险矩阵 | Station command owner、Security owner、Product owner | `APPROVED_RETAIN_CURRENT` | `HerverJun / 2026-08-23` |
| G0-04 产品处置板 | 对应 capability owner、Product owner；涉及 token/cleanup 时另加 Security owner | `APPROVED` | `HerverJun / 2026-08-23`；六项处置见上表 |
| G0-05 fixture/evidence | QA/Release owner、上述 capability owner | `PASS` | `HerverJun / 2026-08-23`；冻结 fixture + Playwright/owner cleanup 证据 |

本次批准只解除 Option D G0 的前置阻塞。明确 `DEFERRED` / `NOT_APPLICABLE` 的工作包不进入实现队列；
G1 只能在 G0 证据 manifest、独立复核与文档同步全部通过后标记 `READY`。

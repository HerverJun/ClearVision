# ADR-ParityAlignment-Wave0：行为、风险与处置冻结包

状态：`PROPOSED_FOR_OWNER_REVIEW`
日期：2026-08-14
适用分支：`studio-ui-next`
审计基线：`22a3d26a00a2d3b8098165aab5489ce54f5bc95b` 加审计时 dirty working tree
关联计划：[功能对齐 TODO 计划](../../归档/历史归档/2026-08-14-StudioUI-Parity-Alignment-Review-22a3d26/08_Parity_Alignment_TODO.md)
关联台账：[F10_ContractAndProductionPlan.md](./F10_ContractAndProductionPlan.md)

## 状态与边界

本文件为本次 parity alignment 的 Wave 0 签字包，不改写 F10 中历史 G0/G1/G2 checkpoint 的结论。
它记录当前代码与 Legacy 语义的已核事实、需要明确的产品/安全决定，以及 Wave 1/2 不得越过的
authority 边界。所有 `PROPOSED` 行均未获 Product、Security 或对应后端 owner 批准；在签字前，
对应工作包保持 `BLOCKED_BY_CONTRACT`，不得编写产品 UI、私有 endpoint、第二 owner 或替代状态机。

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

### 待签字的行为表

| 行为 | `PROPOSED` 归属与约束 | 仍需 owner 决定 |
| --- | --- | --- |
| 运行到节点 | `FlowCanvasOwner` 只把已选 node identity 交给既有 Preview owner；由 Preview owner 使用现有 `flows/preview-node` transport、identity 与取消机制。结果明确标为“调试预览”，不等同正式运行。 | 哪些 operator/node 可预览；disabled、缺相机、无输入、运行中时的可用性规则；是否允许从 context menu、快捷键或两者进入。 |
| active node | active node 只作为 Workspace/Preview 的可丢弃 UI 投影；不写 Flow，不替代选择 authority，不生成第二 Flow catalog。 | active node 与普通 selection 是否可不同；工程切换、节点删除、leave guard 时的清除与恢复语义。 |
| stale / cancel | 新请求、node/flow revision 变化、工程切换、权限变化或 owner dispose 必须 abort/丢弃旧请求；identity 不匹配不得显示为当前预览。 | 用户主动取消入口、超时预算与用户可见中文文案。 |
| 双击 / subgraph | 只在批准的 subgraph host 上响应；进入、退出和键盘返回均由同一 FlowCanvas/Workspace owner 管理，不复制 Flow state。 | 合法 host 类型、child flow identity、breadcrumb 表达、空子图、嵌套深度、保存与 leave guard 的确切语义。 |

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

FlowCanvas owner、Preview/Run owner 与 Product owner 必须共同确认行为表、允许节点 fixture、状态矩阵和
subgraph 数据模型。确认前，`W1-WS-01` 与 `W1-WS-02` 不得开始。

## G0-02 Inspector 参数推荐合同

### 已核事实

| 主题 | 当前事实 | 代码锚点 |
| --- | --- | --- |
| Endpoint | 现有 `POST /api/operators/{type}/recommend-parameters` 接收 `{ ImageBase64 }`；当前 endpoint 本身未见 `.RequireClearVisionPermission(...)`，相邻 endpoint 的 Engineer/Admin 链不能外推给它。 | `Endpoints/ApiEndpoints.cs:77-80,1672-1691` |
| 后端 allowlist | 当前 recommender 只对 Thresholding、Filtering/GaussianBlur、BlobAnalysis、SharpnessEvaluation 返回参数；其他算子返回空字典。 | `Infrastructure/Services/ParameterRecommender.cs:13-27` |
| 输入失败 | 缺失、格式错误、解码失败返回 400；超过 25 MiB 返回 413。 | `Endpoints/ImagePayloadDecoder.cs:7-18,20-151` |
| Next 缺口 | Inspector 当前只有参数草稿编辑；没有 recommendation owner、candidate diff、accept/revert 或 endpoint caller。 | `StudioUI/src/capabilities/project-workspace/inspector/InspectorPanel.vue` |
| Legacy 权限行为 | Legacy recommendation button 只按 operator type 显示，前端不检查 role；当前 endpoint 也没有权限 filter，故 Operator 可能已能调用。 | `wwwroot/src/features/flow-editor/propertyPanel.js:428-443,2295-2298,2358-2379` |

### 待签字的合同

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

Inspector owner、ParameterRecommender backend owner 与 Product owner 必须批准 allowlist、candidate 字段、输入图来源、
endpoint-specific authorization、权限投影、accept/revert 语义和上述 test fixtures。确认前，`W1-WS-03` 保持
`BLOCKED_BY_CONTRACT`。

## G0-03 Station 命令风险矩阵

### 已核事实

| 主题 | 当前事实 | 代码锚点 |
| --- | --- | --- |
| 命令枚举 | `Ping`、`StartRuntime`、`StopRuntime`、`ReloadPackage`、`DeployPackage`、`ApplySiteProfile`、`CollectLogs`。 | `Runtime.Abstractions/StationSyncContracts.cs:90-99` |
| 既有 authority | 命令与部署 endpoint 均要求 Station Admin，并以 `clientRequestId` 提供幂等身份；部署有 package/station/admission 检查。 | `Endpoints/StationEndpoints.cs:123-299,538-553,681-700` |
| Legacy 语义 | Legacy 对停止、正式包部署、测试包下发使用确认；确认文本显示 Station、影响、包与审计提示。 | `wwwroot/src/features/stations/stationMonitorView.js:793-795,909-931,1232-1236` |
| Next 缺口 | 既有 Next Station owner 能投影提交与 reconcile，但未保留风险分级确认。 | `StudioUI/src/capabilities/stations-read/` |

### 风险表草案（必须由 Security + Product owner 批准）

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

Station command owner、Security owner 与 Product owner 必须在风险表逐项填入风险等级、确认方式、审计字段、
取消/超时/unknown-outcome 策略和验收人。确认前，`W1-ST-01` 不得实现。

## G0-04 产品处置板

下表只记录待签字处置，不以现有 Legacy fallback、后端 endpoint 或旧 ADR 代替产品决定。`保守建议` 是基于
当前 authority 与安全边界给审批人准备的起点，不是批准，也不会改变当前 UI、Legacy fallback 或对外状态。

| 能力 | 当前事实 | 保守建议（未批准） | 必须选择的处置 | 必需签字人 | 目标波次 | 当前状态 |
| --- | --- | --- | --- | --- | --- | --- |
| Demo / 示例工程 | 后端 demo create/guide 仍在，Next 缺入口；不得复制 Flow JSON。 | `RETAIN_LEGACY_FALLBACK` | `MIGRATE` / `RETAIN_LEGACY_FALLBACK` / `RETIRE_WITH_APPROVAL` | Product + Project lifecycle owner | Wave 3 | `PENDING_DECISION` |
| 独立本地图像加载 | Legacy 有独立调试入口；Next 未冻结 `FilePickerPort` 输入语义。 | `RETAIN_LEGACY_FALLBACK` | 同上 | Product + ImageCanvas/Host owner | Wave 2/3 | `PENDING_DECISION` |
| Runtime Preview Pilot | 当前只允许 default-off、metadata-only、developer-only 的内部 pilot。 | `RETAIN_LEGACY_FALLBACK` | 同上 | Product + Runtime/Settings owner | Wave 3 | `PENDING_DECISION` |
| Station token 安全分发 | 当前只支持 regenerate，长期明文 reveal 已被后端排除。 | `NO_RECOMMENDATION_SECURITY_REQUIRED` | 同上；不得将长期明文回显作为默认 | Security + Product + Station owner | Wave 2/3 | `PENDING_SECURITY_DECISION` |
| Storage cleanup | 破坏性操作缺少 scope、backup、operation identity、审计与 reconcile 合同。 | `RETIRE_WITH_APPROVAL` | 同上；未批准前不加控制 | Security + Product + Settings owner | Wave 3 | `PENDING_CONTRACT` |
| 工程/版本/FPS 持续状态 | P3，需先满足 125% 空间预算，不得挤压 Canvas/Inspector/Preview。 | `RETAIN_LEGACY_FALLBACK` | 同上 | Product + Shell owner | Wave 2/3 | `PENDING_DPI_BUDGET` |

## G0-05 Fixture 与证据目录

通过 G0 前至少冻结一个可复现工程 fixture，必须包含普通节点、可预览节点、批准的 subgraph host、全局变量绑定、
ROI 和正式判定。每个证据运行必须写入：

```text
.tmp/studio-ui-next/parity-alignment/<wave>/<sourceSha>/<runId>/
```

每个 manifest 至少记录 `sourceSha`、working-tree diff identity、profile、Windows scale、native DPI、
client/viewport size、fixture identity、配置、用户/权限、覆盖的成功与错误状态、请求/operation identity、
owner cleanup、端口/user-data/database/result/publish cleanup。未执行的证据明确写 `NOT RUN` 或
`NOT PERFORMED`。发布产物只能写入 `.tmp/publish-check/`。

当前没有可直接通过本项的完整 fixture。`f03-workspace.spec.ts` 的 deterministic flow 已包含普通节点与 ROI，
但 `decisionConfiguration` 为 `null`、全局变量绑定为空且没有 subgraph host；现有 WebView2 parity audit
seed 已包含 preview、ROI 和正式判定，却同样没有明确的全局变量绑定或 subgraph host。因此它们只能作为
新 fixture 的输入，不得作为 G0-05 通过证据。

## 批准记录

本 ADR 在下列签字均完成前不是 Wave 0 的完成证据：

| 范围 | 必需批准人 | 批准状态 | 日期 / 证据 |
| --- | --- | --- | --- |
| G0-01 Canvas 行为合同 | FlowCanvas owner、Preview/Run owner、Product owner | `PENDING` | `NOT PERFORMED` |
| G0-02 Inspector 推荐合同 | Inspector owner、ParameterRecommender backend owner、Product owner | `PENDING` | `NOT PERFORMED` |
| G0-03 Station 风险矩阵 | Station command owner、Security owner、Product owner | `PENDING` | `NOT PERFORMED` |
| G0-04 产品处置板 | 对应 capability owner、Product owner；涉及 token/cleanup 时另加 Security owner | `PENDING` | `NOT PERFORMED` |
| G0-05 fixture/evidence | QA/Release owner、上述 capability owner | `PENDING` | `NOT PERFORMED` |

批准后必须同步更新本 ADR、F10、功能对齐 TODO、能力矩阵、回归清单和 evidence index，并将相应
`W1-*` 或 `W2-*` 工作包从 `BLOCKED_BY_CONTRACT` 移入唯一 owner 的串行实现队列。

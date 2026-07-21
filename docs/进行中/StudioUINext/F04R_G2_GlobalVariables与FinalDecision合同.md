# Studio UI Next F04-R G2 GlobalVariables 与 FinalDecision 合同

> 状态：`FROZEN`
> 代码事实基线：`483a212783d4bc66f9f434e0a22de4be944e46c7`
> 本文冻结唯一 Next owner 的职责，不创建代码或 endpoint。

## 1. 共同保存边界

Project DTO 已包含 `PersistenceRevision`、`Flow`、`GlobalVariables` 和 `Assets`；Flow DTO 已包含 `DecisionConfiguration`。正式定义、binding 和判定配置必须在同一 Workspace draft 中进入：

```text
workspace owner
-> workspacePersistenceOwner
-> PUT /api/projects/{id}
-> ProjectService
-> ProjectSaveCoordinator
-> backend PersistenceRevision / fresh Project
```

现有 `PUT /api/projects/{id}/global-variables` 是历史服务入口，仍走 ProjectService/保存协调链；Next 不把它作为第二 Project store 或独立 draft writer。Next 的变量与判定编辑提交必须使用统一 Project payload，以保证 Flow、变量、判定原子保存；运行时 values endpoint 只读消费，不能成为定义权威。

## 2. GlobalVariables 合同

### 2.1 数据字段与身份

| 对象 | 必须保留的字段/语义 |
|---|---|
| Definition | `Id` 不变；`Name` 是表达式/合同标识；`DisplayName` 是 UI 文案；`Description`；`ValueType`=`String/Int64/Double/Boolean`；`InitialValue`；`Min/Max`（仅数值）；`ManualWriteAllowed`；`IncludeInResultMetadata`；`Order` |
| Source binding | `Id`、`VariableId`、`OperatorId`、`OutputPortId`、operator/output 名称快照、可选 `ResultPathVersion/ResultPath`、`ConversionMode`、`Expression`；每变量最多一个自动 source |
| Target binding | `Id`、`VariableId`、`OperatorId`、`ParameterId`、operator/parameter 名称快照、`ConversionMode`、`Expression`；每个 operator parameter 最多一个变量 |
| Runtime value | 来自 `GET /api/projects/{id}/global-variable-values` 的 `Value/Version/UpdatedBy/RunId/OperatorId` 投影；在 F04-R 只读，不写入 definition draft |

### 2.2 编辑与 binding 操作

1. 创建：客户端生成 non-empty Guid 作为 draft identity；后端 validator 校验并随 Project 原样持久化，保存成功后以返回 Project 中的 identity 为准。
2. 修改：同一 Id 更新 definition；修改类型必须重新转换初始值、范围和所有 source/target 兼容性，失败则字段级阻止应用。
3. 重命名：Id 与 bindings 不变；不做未经 parser 证明的字符串全局替换。表达式若仍引用旧名称，由 backend `GV033`/字段错误阻止保存并列出受影响 binding。
4. 删除：先展示 source/target 引用数并要求确认；一次性删除 definition 与其 bindings。表达式引用不会被静默重写；后端 validation 仍是最终阻止者。
5. Source binding：从 Preview 结构化输出或变量工作台选择已存在输出；目标必须是当前 Flow 的启用算子/输出端口；Image root 和 ResultPath 规则服从 `ProjectGlobalVariableSchemaValidator`。
6. Target binding：Inspector 的“参数来源”只是窄委托入口；只展示与参数类型/转换模式兼容的变量；绑定后固定值编辑控件只读。
7. 默认/手动值：`InitialValue` 是 Project definition；手动值仅作为显式工程编辑草稿，不能把运行时 session value 当作初始值或 Project 权威。
8. 取消/撤销：取消关闭工作台并丢弃本次 draft；应用前的单变量修改可在 capability-local draft 撤销/重做；正式 Project undo 不由变量 owner 另造。

### 2.3 类型、校验与错误

后端 `ProjectGlobalVariableSchemaValidator` 是最终 authority，Next 仅做同字段即时提示。validator 内部 diagnostic 已携带 Variable/Operator/Port/Parameter identity，但当前 `ProjectService.ThrowIfInvalid` 会把多条 diagnostic 合并为异常文本，`ToProjectUpdateFailure` 只结构化首个稳定 code；Prompt 3 必须在既有 Project update response 上补充结构化 diagnostic 列表，不能由前端解析多行字符串猜字段。至少映射：

| Code | 语义 |
|---|---|
| `GV001`/`GV002`/`GV003`/`GV004`/`GV013` | schema/version、Id、重复 Id、重复/非法名称 |
| `GV005`/`GV014`/`GV018`/`GV019`/`GV021` | 初始值、范围、有限数值与 min/max 关系 |
| `GV006`/`GV007` | source 或 target 多重绑定 |
| `GV008`/`GV009`/`GV010`/`GV011` | 变量、算子、输出端口、参数引用不存在 |
| `GV017`/`GV022` | source/target 类型或转换不兼容 |
| `GV020` | 引用禁用算子（warning，不能冒充可运行） |
| `RP101`/`RP107`/`RP122` | ResultPath 缺字段、非 canonical、index 不支持 |
| `GV033` | binding expression 无法编译 |

错误必须靠近 definition/binding 字段，同时在工作台状态带上“发生了什么、影响什么、下一步”。未知/解码失败 fail closed；不猜测变量类型或运行值。

### 2.4 dirty、revision 与运行边界

- definition、source/target binding 或相关 Flow 参数变化即 Workspace dirty；runtime value 变化不使 Project dirty。
- 本地 UI draft revision 只用于 stale 防护；正式保存使用后端 `PersistenceRevision`。
- 保存成功以响应中的新 `PersistenceRevision` 和完整 Project payload 为准；409 保留本地 draft，提供重新加载 authority/手动合并，不静默覆盖。
- 运行中由 `IInspectionRuntimeCoordinator` mutation lease 锁定正式 schema 写；运行值读取可以继续，但定义/绑定写返回 409 或前端只读。
- GlobalVariables 与 Canvas/Inspector/FinalDecision 共用 Workspace owner 树；不存在第二 store、第二 EventBus 或 localStorage authority。

## 3. FinalDecision 合同

### 3.1 候选来源与真实语义

候选只来自 `FinalDecisionConfigurationCatalog.GetEligibleOutputs(flow)`，必须是启用算子且在 backend catalog 声明的 output。当前 catalog 的真实类别包括：

- `Boolean`：`IsOk`、`IsMatch`、`Accepted`、`Result` 等布尔输出；由 `TrueMeansOk` 决定 OK/NG。
- `StringMap`：`ResultJudgment.JudgmentResult` 固定 `OK/NG`；`JudgmentValue` 固定 `1/0`；其他字符串输出按后端候选规则映射。
- `NumericComparison`：Integer/Float 测量或统计输出；使用 comparator + finite threshold。

配置字段固定为 `SourceOperatorId`、可选 port Id/name、`DataType`、`Rule`、`TrueMeansOk`、`OkValue`/`NgValue`、`Comparator`、`Threshold` 及 `MissingDecisionPolicy`=`Undetermined/NotApplicable/Invalid`。

### 3.2 编辑、保存与失效

- 候选下拉只消费 validation response 的 `EligibleOutputs`，不由前端按名字猜候选。
- 选择候选后只创建 Flow draft；“应用”更新 shared Flow projection 并标记 dirty，不调用独立保存 endpoint。
- 规则字段按后端 candidate 的 rule 显示：布尔映射、字符串 OK/NG、数值 comparator/threshold；固定 string map 的 required values 不能被改写为任意字符串。
- 节点删除、禁用、输出端口重命名或类型变化后，旧 binding 保留为可诊断 draft 但 validation 返回字段级 error；不得自动选择其他候选。
- 无候选或无 binding 显示“未配置/无可用候选”，正式 Run admission 被拒绝；不能把未判定伪装成 OK/NG。
- Preview 的结构化输出只能帮助用户选择/绑定；Preview 结果不消费正式 Judgment，也不代替 Formal Run。

### 3.3 后端校验与字段级错误

`POST /api/inspection/decision-configuration/validate` 返回 `IsValid`、`Issues`、`EligibleOutputs`；每个 issue 当前包含 code/message 及可选 operatorId/outputName，没有显式 field path。Prompt 3 可在既有 response 上增加稳定 field key，或由已冻结的 code-to-field 映射定位，但不能从英文 message 解析字段。Next 至少保留并定位：

```text
DECISION_FLOW_REQUIRED
DECISION_BINDING_REQUIRED
DECISION_SOURCE_OPERATOR_NOT_FOUND
DECISION_SOURCE_OPERATOR_DISABLED
DECISION_SOURCE_OUTPUT_NOT_FOUND
DECISION_SOURCE_OUTPUT_MISMATCH
DECISION_SOURCE_TYPE_MISMATCH
DECISION_SOURCE_OUTPUT_INELIGIBLE
DECISION_RULE_CONTRACT_MISMATCH
DECISION_RULE_TYPE_MISMATCH
DECISION_STRING_MAP_VALUES_REQUIRED
DECISION_STRING_MAP_VALUES_CONFLICT
DECISION_STRING_MAP_CONSTRAINT_MISMATCH
DECISION_NUMERIC_COMPARISON_REQUIRED
```

运行时缺失或类型异常由 `FinalDecisionResolver.Resolve` 产生 `DECISION_SIGNAL_MISSING*`、`DECISION_VALUE_TYPE_INVALID` 或 `DECISION_STRING_VALUE_UNMAPPED`，映射为 Judgment `Undetermined/NotApplicable/Invalid/NG` 的真实 outcome，不在 UI 中重算。

### 3.4 identity、hash 与 admission

- Flow draft 的 canonical hash 和 decision configuration hash 必须在 Project 正式保存后由后端权威生成/投影；前端本地 fingerprint 只做 stale 防护。
- `Project.PersistenceRevision`、Flow hash、Decision hash 与 Workspace run identity 不得混用。
- `POST /api/inspection/admission`、`execute`、`stop`、`reconcile` 必须携带并校验 `projectId`、`clientSnapshotId`、`expectedPersistenceRevision`、`expectedCanonicalFlowHash`、`expectedDecisionConfigurationHash`。
- Run outcome 的 `ExecutionOutcome` 与 `DecisionOutcome` 是双轴；`JudgmentResult/JudgmentValue` 只是 source output 语义，不能当作 UI 自己的最终状态字段。

## 4. 唯一 owner 与文件边界

| Capability | 唯一 Next owner | 不允许出现 |
|---|---|---|
| GlobalVariables | `workspaceGlobalVariablesOwner`（Workspace child） | 独立 Pinia store、localStorage authority、独立 save client、Inspector 第二 writer |
| Target binding | Inspector extension 仅调用 GlobalVariables owner 的窄 command | Inspector 自己直接改 schema |
| Source binding | Preview/Variables workbench 委托同一 owner | Preview 私有 binding cache |
| FinalDecision | `finalDecisionOwner`（Workspace child） | 顶栏第二 panel、Canvas 私有 decision writer、独立 save endpoint |
| Project save | 现有 `workspacePersistenceOwner` / `ProjectSaveCoordinator` | GlobalVariables/FinalDecision 各自 PUT 后再拼 Project |
| Runtime values | Results/Workspace read projection | 运行值回写 definition 或成为 Project 权威 |

## 5. 状态

```text
GLOBAL_VARIABLES_CONTRACT=FROZEN
FINAL_DECISION_CONTRACT=FROZEN
PROJECT_SAVE_AUTHORITY=ProjectSaveCoordinator
RUNTIME_VALUE_BOUNDARY=READ_ONLY
IMPLEMENTATION=FORBIDDEN
```

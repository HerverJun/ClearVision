---
title: "ClearVision Station 现场可调参数 / Profile 覆盖机制 TODO"
doc_type: "task-list"
status: "superseded"
topic: "runtime-station-site-tunable-parameters"
created: "2026-05-03"
updated: "2026-05-03"
superseded_by: "docs/进行中/当前计划/ClearVision-Station现场可调参数Profile机制定稿TODO-2026-05-03.md"
sources:
  - "docs/进行中/当前计划/ClearVision-ClearFrost分体式Runtime现场化落地TODO-2026-05-02.md"
  - "docs/进行中/当前计划/ClearVision-ClearFrost分体式Runtime现场化整合计划-2026-04-29.md"
  - "ClearVision.Product/src/ClearVision.Product.Runtime.Abstractions/RuntimeContracts.cs"
  - "ClearVision.Product/src/ClearVision.Product.Runtime/RuntimePackage.cs"
  - "ClearVision.Product/src/ClearVision.Product.Runtime/RuntimePackageExporter.cs"
  - "ClearVision.Product/src/ClearVision.Product.Runtime/RuntimePackageLoader.cs"
  - "ClearVision.Product/src/ClearVision.Product.Runtime/RuntimeHost.cs"
  - "ClearVision.Product/src/ClearVision.Product.Station/MainForm.cs"
---

# ClearVision Station 现场可调参数 / Profile 覆盖机制 TODO

> 本文已被定稿版取代：`docs/进行中/当前计划/ClearVision-Station现场可调参数Profile机制定稿TODO-2026-05-03.md`。本文保留为草案和背景材料，实际执行请以定稿版为准。

> 目标一句话：Station 不再为某个算子硬编码调参界面，而是读取 Studio 发布出来的“现场可调参数 schema”，动态生成受控调参面板，并把现场修改保存为独立的 deployment profile。

---

## 0. 背景与核心判断

当前分体式 Station 的方向是正确的：现场低配工控机不应承载完整 Studio 的编辑、AI、WebView2、Kestrel 和工程构建负担。

但端子线序检测项目暴露了一个新问题：深度学习推理需要现场微调 `Confidence`，于是 Station 里开始出现 `ONNX 参数` 这类专用面板。如果以后每个算子、每种组合、每个现场变量都在 Station 里手工加一个界面，Station 会逐步退化成第二个 Studio。

本 TODO 的架构判断是：

```text
不要把所有算子的调参功能硬塞进 Station。
要让 Studio 发布“哪些参数允许现场调、如何显示、如何校验、如何生效”的元数据。

Station 只实现一套通用参数渲染、校验、保存、应用、回滚能力。
```

最终产品边界：

```text
Studio
  负责流程设计、算子组合、参数默认值、参数开放策略、Runtime Package 导出。

Runtime Package
  负责携带流程、资源、默认参数、现场可调参数 schema、默认 profile。

Station
  负责加载包、运行流程、显示生产状态、渲染已开放参数、保存现场 profile、应用覆盖值。

Deployment Profile / Site Profile
  负责保存现场可变参数，不改变包本身，不要求每次重新导出包。
```

---

## 1. 当前代码事实

当前仓库已经具备 Runtime / Station 的第一轮拆分基础：

| 位置 | 当前事实 | 对本 TODO 的影响 |
|---|---|---|
| `ClearVision.Product.Runtime.Abstractions/RuntimeContracts.cs` | 已有 `RuntimePackageManifest`、`RuntimeProfile`、`RuntimeFieldExtensions`、`StationLocalSettings` 等 DTO | 可在这里扩展现场参数 schema / profile 的跨项目契约 |
| `ClearVision.Product.Runtime/RuntimePackage.cs` | 当前包加载后包含 `Manifest`、`Flow`、`RuntimeProfile`、`ValidationReport` | 需要新增 `ParameterSchema` 和 `DefaultSiteProfile` |
| `ClearVision.Product.Runtime/RuntimePackageExporter.cs` | Studio 导出 Runtime Package 的核心入口已经存在 | 需要在导出包时生成现场可调参数 schema 和默认 profile |
| `ClearVision.Product.Runtime/RuntimePackageLoader.cs` | Station 加载包的核心入口已经存在 | 需要加载并校验 `runtime-parameters.json` / `station-profile.default.json` |
| `ClearVision.Product.Runtime/RuntimeHost.cs` | Station 运行包并执行流程 | 需要在运行前把现场 profile 覆盖到执行流副本，不直接污染包内默认 flow |
| `ClearVision.Product.Station/MainForm.cs` | 当前已经硬编码了 `ONNX 参数` / `DeepLearning.Confidence` 控件 | 这块应迁移成通用参数面板的第一个样本 |

当前必须纠正的趋势：

```text
MainForm.cs 里不应长期存在针对 DeepLearning.Confidence 的专用逻辑。
DeepLearning.Confidence 应成为 runtime-parameters.json 中的一条参数定义。
Station 通过通用控件渲染它。
```

---

## 2. 术语定义

| 术语 | 含义 |
|---|---|
| Runtime Package | Studio 导出的不可变运行包，包含流程、资源、默认参数和 schema |
| Parameter Schema | Studio 发布的现场可调参数定义，描述参数路径、显示方式、范围、权限和生效方式 |
| Site Profile | Station 本地保存的现场覆盖值，和包分离 |
| Override | 某个现场参数的实际覆盖值 |
| Apply Mode | 参数修改后如何生效，例如下一帧、下一次运行、重载流程、停止后重载 |
| Package Default | Studio 导出包时的默认值 |
| Current Site Value | 当前 Station 对该包生效的现场值 |
| Profile Revision | Station 每次保存现场参数后递增的版本号 |

建议包内文件结构：

```text
runtime-package/
  package.json
  flow.json
  runtime-profile.json
  validation-report.json
  runtime-parameters.json
  station-profile.default.json
  resources/
    models/
    templates/
```

Station 本地持久化结构建议：

```text
%ProgramData%/ClearVision/Station/
  settings.json
  packages/
    {packageId}_{flowHash}/
      station-profile.json
      station-profile.history.jsonl
      last-good-profile.json
```

---

## 3. 数据契约草案

### 3.1 Parameter Schema 示例

```json
{
  "schemaVersion": "1.0",
  "packageId": "terminal-wire-inspection",
  "flowHash": "sha256:...",
  "groups": [
    {
      "id": "wire-detection",
      "displayName": "线序检测",
      "order": 10
    }
  ],
  "parameters": [
    {
      "id": "node.deep-learning-1.Confidence",
      "nodeId": "deep-learning-1",
      "operatorType": "DeepLearning",
      "parameterName": "Confidence",
      "displayName": "线序检测置信度",
      "description": "低于该置信度的检测结果不参与线序判定。",
      "groupId": "wire-detection",
      "valueType": "number",
      "uiKind": "slider",
      "defaultValue": 0.6,
      "min": 0.1,
      "max": 0.95,
      "step": 0.01,
      "unit": "",
      "siteTunable": true,
      "visibleInStation": true,
      "pinned": true,
      "permission": "Engineer",
      "applyMode": "NextRun",
      "requiresStop": false,
      "changeReasonRequired": false,
      "order": 10
    }
  ]
}
```

### 3.2 Site Profile 示例

```json
{
  "profileVersion": "1.0",
  "profileId": "station-a-terminal-wire",
  "packageId": "terminal-wire-inspection",
  "flowHash": "sha256:...",
  "stationId": "ST-A-01",
  "lineName": "端子线序-A线",
  "revision": 7,
  "updatedAtUtc": "2026-05-03T08:30:00Z",
  "updatedBy": "engineer",
  "overrides": [
    {
      "parameterId": "node.deep-learning-1.Confidence",
      "value": 0.72,
      "packageDefaultValue": 0.6,
      "updatedAtUtc": "2026-05-03T08:30:00Z",
      "updatedBy": "engineer",
      "reason": "现场光照变化后降低误检"
    }
  ]
}
```

### 3.3 推荐枚举

```text
RuntimeParameterValueType
  Number
  Integer
  Boolean
  String
  Enum
  Path
  Roi
  ReadonlyText

RuntimeParameterUiKind
  NumericInput
  Slider
  Toggle
  Select
  TextBox
  PathPicker
  RoiEditor
  Readonly

RuntimeParameterApplyMode
  Immediate
  NextFrame
  NextRun
  ReloadFlow
  ReloadPackage
  RestartStation

RuntimeParameterPermission
  Operator
  Engineer
  Admin
```

第一轮可以只实现：

```text
Number / Integer / Boolean / Enum / String
NumericInput / Slider / Toggle / Select / TextBox / Readonly
Immediate / NextRun / ReloadPackage
Operator / Engineer
```

`RoiEditor`、`PathPicker`、`Admin` 权限、完整用户登录可以放到第二轮。

---

## 4. 修改总纲

### 4.1 总体原则

- Runtime Package 默认不可变，现场参数覆盖保存到 Site Profile。
- Studio 决定哪些参数能在现场调，Station 不自行暴露完整算子参数树。
- Station UI 不认识具体算子业务，只认识通用参数类型和控件类型。
- Runtime 执行时应用 Profile 覆盖值，但不直接修改包内 `flow.json`。
- Profile 有版本、时间、操作者、变更历史和回滚能力。
- 参数修改必须经过 schema 校验，不能写入未知参数、只读参数、越界值或类型错误值。
- 热更新能力分级实现，不要让拖动滑条触发模型重载或流程重建。

### 4.2 建议分层

```text
ClearVision.Product.Runtime.Abstractions
  RuntimeParameterSchema
  RuntimeParameterDefinition
  RuntimeParameterGroup
  RuntimeSiteProfile
  RuntimeParameterOverride
  参数类型、控件类型、生效模式、权限枚举

ClearVision.Product.Runtime
  RuntimeParameterSchemaLoader
  RuntimeSiteProfileStore
  RuntimeParameterOverrideApplier
  RuntimeParameterValidator
  RuntimePackageExporter 扩展
  RuntimePackageLoader 扩展
  RuntimeHost 覆盖应用入口

ClearVision.Product.Desktop / Studio
  参数开放配置 UI
  导出包时生成 schema
  导入现场 profile 并对比默认值

ClearVision.Product.Station
  通用参数面板
  Profile 本地保存/导入/导出/回滚
  运行前应用 profile
  移除 ONNX 专用置信度面板
```

### 4.3 包和 profile 的哈希边界

建议明确两个哈希：

```text
FlowHash
  只代表 Studio 发布出来的 flow.json 默认内容。
  Site Profile 修改不改变 FlowHash。

ProfileHash / ProfileRevision
  代表现场覆盖值当前版本。
  运行记录里同时记录 FlowHash 和 ProfileRevision。
```

运行结果中建议补充：

```text
PackageId
FlowHash
ProfileId
ProfileRevision
AppliedOverrideCount
```

这样以后追溯“同一个包为什么现场结果不同”时有证据链。

---

## 5. Studio 需要改什么

### 5.1 参数元数据来源

需要为算子参数建立“可发布的参数元数据”来源。建议优先复用现有 `ParameterDto` / 算子定义里的字段，缺失项再增加 Runtime 专用元数据。

每个可调参数至少需要：

```text
稳定 ID
所属节点 ID
算子类型
参数名
显示名
说明
类型
默认值
范围 / 选项
单位
分组
排序
是否现场可调
是否在 Station 显示
是否常用 pinned
权限
生效模式
```

稳定 ID 建议：

```text
node.{nodeId}.{parameterName}
```

注意事项：

- 不要只用显示名作为 ID。
- 不要只用算子类型和参数名作为 ID，因为一个流程里可能有多个同类型算子。
- 如果 nodeId 当前不稳定，先补齐 Studio 流程节点的稳定 ID 机制。

### 5.2 Studio UI：发布前的现场参数配置

在 Runtime Package 导出流程里增加一步：

```text
现场可调参数
```

界面建议：

```text
左侧：当前流程节点 / 算子列表
中间：该节点参数列表
右侧：Station 显示配置
  显示名
  分组
  说明
  最小值 / 最大值 / 步进
  单位
  控件类型
  常用参数
  权限
  生效模式
  是否要求填写修改原因
```

第一轮也可以不做复杂 UI，先提供保守默认策略：

```text
DeepLearning.Confidence
  默认标记为 siteTunable
  默认显示名：检测置信度
  默认范围：0.00 - 1.00
  默认步进：0.01
  默认 applyMode：NextRun
```

但导出包里必须产出标准 schema，不能继续让 Station 识别 DeepLearning 算子后自行造控件。

### 5.3 Studio 导出包

修改 `RuntimePackageExporter`：

- 生成 `runtime-parameters.json`。
- 生成 `station-profile.default.json`。
- 在 `package.json` / `RuntimeFieldExtensions` 中登记这两个相对路径。
- 导出时校验所有 `siteTunable` 参数都能在 `flow.json` 中定位。
- 导出时校验默认值符合 schema 范围。
- 导出时校验路径参数不能泄露工程师本机绝对路径，除非资源已复制到包内。
- 对旧包保持兼容：没有 `runtime-parameters.json` 时 Station 仍能运行，只是不显示参数面板。

建议扩展：

```text
RuntimeFieldExtensions.RuntimeParameters = "runtime-parameters.json"
RuntimeFieldExtensions.DefaultSiteProfile = "station-profile.default.json"
```

### 5.4 Studio 导入现场 profile

第二轮增加 Studio 反向吸收能力：

```text
导入 Station Profile
  校验 packageId / flowHash
  展示 package default vs site override diff
  允许工程师选择性吸收到工程默认参数
  生成下一版 Runtime Package
```

这一步能解决现场经验散落在各台工控机的问题。

### 5.5 Studio 验收点

- Studio 导出的端子线序检测包包含 `runtime-parameters.json`。
- `DeepLearning.Confidence` 不需要 Station 硬编码也能出现在 schema 中。
- 未被标记为 `siteTunable` 的算子参数不会出现在 schema。
- 导出的默认 profile 不包含现场私有信息。
- 导入现场 profile 时能显示差异，不直接静默覆盖工程参数。

---

## 6. Runtime 需要改什么

### 6.1 Runtime Abstractions DTO

在 `ClearVision.Product.Runtime.Abstractions` 增加契约：

```text
RuntimeParameterSchema
RuntimeParameterGroup
RuntimeParameterDefinition
RuntimeParameterNumericConstraints
RuntimeParameterEnumOption
RuntimeSiteProfile
RuntimeParameterOverride
RuntimeParameterChangeRecord
RuntimeParameterValidationIssue
```

字段设计要对 JSON 友好。对 `value` 这种多类型字段，建议用：

```text
JsonElement
```

或已有项目里统一的 JSON value 表示方式，避免 `object` 反序列化后出现 `double` / `decimal` / `JsonElement` 混乱。

### 6.2 Package Loader / Validator

修改 `RuntimePackageLoader`：

- 如果 manifest 指向 `runtime-parameters.json`，加载 schema。
- 如果没有 schema，返回空 schema，不视为错误。
- 加载 `station-profile.default.json`，没有则自动构造空默认 profile。
- 校验所有 schema 路径都在 flow 中能找到。
- 校验默认 profile 的 override 只能引用 schema 中存在且可调的参数。
- 校验数值范围、枚举选项、布尔和字符串类型。

修改 `RuntimePackageValidator`：

- 增加参数 schema 文件存在性、路径安全、schemaVersion 支持范围检查。
- 对 unknown applyMode / valueType / uiKind 给出明确错误。
- 对只读参数出现在 default profile overrides 中给 warning 或 error。

### 6.3 Override Applier

新增 `RuntimeParameterOverrideApplier`：

```text
输入：
  OperatorFlowDto packageFlow
  RuntimeParameterSchema schema
  RuntimeSiteProfile siteProfile

输出：
  OperatorFlowDto executionFlowClone
  AppliedOverrideSummary
```

关键要求：

- 不直接修改 `RuntimePackage.Flow`。
- 每次运行前基于包内默认 flow 克隆出执行副本，再应用 profile。
- 对未知参数、只读参数、类型错误、越界值拒绝应用。
- 对 `ReloadPackage` / `RestartStation` 参数，如果当前运行不允许热更新，返回明确状态给 Station。

### 6.4 RuntimeHost

修改 `RuntimeHost`：

- 加载包时同时加载默认 profile。
- 提供 `GetParameterSchema()` / `GetActiveSiteProfile()`。
- 提供 `ApplySiteProfileAsync(profile)` 或 `SetPendingSiteProfile(profile)`。
- 单次运行、目录运行前应用当前 profile 到执行流副本。
- 运行结果记录 `ProfileId`、`ProfileRevision`、`AppliedOverrideCount`。
- 运行中修改参数时遵循 applyMode：
  - `Immediate` / `NextFrame`：第一轮可先降级为 `NextRun`，但 UI 必须显示真实生效时机。
  - `NextRun`：当前运行不受影响，下一次运行生效。
  - `ReloadPackage`：要求停止后重载。

### 6.5 Runtime 验收点

- 无 schema 的旧包仍能运行。
- 有 schema 的包加载后能返回参数列表。
- 应用 profile 后仅执行流副本变化，包内默认 flow 不变。
- 越界值、未知 parameterId、只读参数 override 都会被拒绝。
- 同一个 package + 不同 profile 的运行记录能区分 profile revision。

---

## 7. Station 需要改什么

### 7.1 UI 总体结构

Station 首页保持生产视角，不要把调参控件堆在主看板上。

建议结构：

```text
生产页
  运行 / 停止
  当前 OK/NG/Error
  图像预览
  最近结果
  产量统计
  当前包
  当前 profile 摘要

参数页 / 参数抽屉 / 参数对话框
  常用参数
  分组参数
  高级参数
  修改历史
  导入 / 导出 profile
  恢复默认 / 恢复上次可用
```

第一轮 WinForms 可以做成：

```text
右侧 TabControl
  生产状态
  参数
  日志
```

或保留当前主界面，把现有 `ONNX 参数` 卡片替换成：

```text
现场参数
  常用参数
  打开全部参数...
```

### 7.2 通用参数渲染器

新增 Station 侧组件：

```text
RuntimeParameterPanel
RuntimeParameterControlFactory
RuntimeParameterEditorState
RuntimeParameterProfileController
```

控件映射：

| valueType / uiKind | WinForms 控件 |
|---|---|
| Number + Slider | TrackBar + NumericUpDown |
| Number + NumericInput | NumericUpDown |
| Integer | NumericUpDown |
| Boolean | CheckBox |
| Enum | ComboBox |
| String | TextBox |
| ReadonlyText | Label |
| Path | 第一轮先 TextBox readonly，第二轮再加文件选择 |
| Roi | 第一轮显示“需 Studio 调整”，第二轮做图像 ROI 编辑 |

渲染规则：

- 只显示 `visibleInStation = true` 且 `siteTunable = true` 的参数。
- `pinned = true` 的参数进入常用参数区域。
- 按 `group.order`、`parameter.order` 排序。
- 运行中对不能热更新的参数禁用编辑或标记“下次运行生效”。
- 每个控件显示当前值、默认值、生效方式。
- 修改后进入 dirty 状态，点击“应用”才写入 profile。
- 提供“恢复默认”和“撤销未保存修改”。

### 7.3 移除 ONNX 专用逻辑

需要从 `ClearVision.Product.Station/MainForm.cs` 迁移掉这些职责：

```text
_onnxParameterDetailLabel
_confidenceNumericUpDown
_updatingOnnxParameterControls
BuildOnnxParameterContent()
RefreshOnnxParameterControls()
ApplyConfidenceFromUi()
GetDeepLearningOperators()
EnsureParameter(op, "Confidence", ...)
```

注意不是简单删除功能，而是迁移到通用机制：

```text
DeepLearning.Confidence
  由 runtime-parameters.json 定义。
  由 RuntimeParameterPanel 渲染。
  由 RuntimeSiteProfile 保存。
  由 RuntimeParameterOverrideApplier 应用。
```

### 7.4 Profile 持久化

Station 增加 profile store：

```text
StationSiteProfileStore
  LoadActiveProfile(packageId, flowHash)
  SaveActiveProfile(profile)
  AppendHistory(changeRecord)
  ExportProfile(targetPath)
  ImportProfile(sourcePath)
  ResetToPackageDefault()
  RestoreLastGoodProfile()
```

保存规则：

- 每个 packageId + flowHash 一份 active profile。
- 每次应用参数都递增 revision。
- 每次保存写入 history jsonl。
- 参数 profile 写入必须异步或轻量，不要卡住图像推理。
- 禁止每帧保存 profile，只在用户点击应用时保存。

### 7.5 权限与操作模式

第一轮可以先做轻量权限：

```text
默认 Operator 模式
  只能查看参数或修改 permission = Operator 的参数。

工程师模式
  输入简单维护密码或通过本地设置开启。
  可修改 permission = Engineer 的参数。
```

如果暂时没有登录体系，至少要有：

- 参数页入口明显区别于生产操作。
- 修改前需要切换到“工程师模式”。
- 运行记录和 profile history 写入 `updatedBy`，没有登录时写 `local-engineer`。

### 7.6 性能要求

Station 的参数面板不应带来明显性能开销。实现时注意：

- 参数面板只在加载包、打开参数页、应用 profile 后刷新，不随每帧图像刷新。
- 控件值变化先进入内存 dirty state，不立即重建流程。
- 滑条变化做 debounce，不连续写 profile。
- `NextRun` 参数只影响下一次运行，不重载 ONNX session。
- `ReloadPackage` 参数必须停止后应用。
- 参数数量超过 100 时，使用分组折叠、搜索、延迟创建高级分组控件。
- 运行线程和 UI 线程之间只传递轻量状态，不传递大图或模型对象。

### 7.7 Station 验收点

- Station 加载端子线序包后，能显示 `线序检测置信度`，但不硬编码 ONNX 文案。
- Station 不显示未开放的算子参数。
- 修改置信度后保存 profile，下一次运行使用新值。
- 关闭并重启 Station 后，仍自动加载该包对应的现场 profile。
- 可以一键恢复包默认值。
- 可以导出 profile 文件，另一台 Station 导入后得到相同覆盖值。
- 运行中修改 `NextRun` 参数不会卡顿当前运行。
- 参数页空 schema 时显示“当前包未开放现场参数”，不报错。

---

## 8. 分阶段 TODO

### P0：契约设计与现状清理

- [ ] 梳理 `ParameterDto` 当前字段，确认是否已有显示名、类型、默认值、范围字段。
- [ ] 确认流程节点 ID 是否稳定；如果不稳定，先补稳定 nodeId。
- [ ] 在 `RuntimeContracts.cs` 增加参数 schema / profile DTO。
- [ ] 扩展 `RuntimeFieldExtensions`，登记 `RuntimeParameters` 和 `DefaultSiteProfile` 路径。
- [ ] 明确 value JSON 表示方式，避免 `object` 反序列化不稳定。

### P1：Runtime Package 支持 schema/profile

- [ ] 修改 `RuntimePackage`，增加 `ParameterSchema` 和 `DefaultSiteProfile`。
- [ ] 修改 `RuntimePackageExporter`，导出 `runtime-parameters.json`。
- [ ] 修改 `RuntimePackageExporter`，导出 `station-profile.default.json`。
- [ ] 修改 `RuntimePackageLoader`，加载 schema/profile，旧包无 schema 时兼容为空。
- [ ] 修改 `RuntimePackageValidator`，校验 schema 文件、参数定位、默认值范围。
- [ ] 增加 schema/profile JSON 序列化单元测试。

### P2：Runtime 覆盖应用能力

- [ ] 新增 `RuntimeParameterValidator`。
- [ ] 新增 `RuntimeParameterOverrideApplier`。
- [ ] 确保 profile 覆盖应用到执行流副本，不修改包内默认 flow。
- [ ] 修改 `RuntimeHost`，运行前应用 active profile。
- [ ] 修改运行结果或诊断信息，记录 `ProfileRevision` 和 override 数量。
- [ ] 增加单测：`DeepLearning.Confidence` 从 0.60 覆盖为 0.72 后执行流副本生效。

### P3：Studio 导出现场可调参数

- [ ] 在 Studio 导出路径中收集当前流程可开放参数。
- [ ] 第一轮至少支持 `DeepLearning.Confidence` 自动生成 schema。
- [ ] 增加发布配置入口，允许工程师选择参数是否 `siteTunable`。
- [ ] 支持编辑显示名、分组、范围、步进、权限、生效模式。
- [ ] 导出包前展示现场参数摘要。
- [ ] 导出包时拒绝无效 schema。

### P4：Station 通用参数面板

- [ ] 新增 `RuntimeParameterPanel`。
- [ ] 新增 `RuntimeParameterControlFactory`。
- [ ] 新增 `StationSiteProfileStore`。
- [ ] 加载包后读取 schema/profile 并渲染参数面板。
- [ ] 支持 Number / Boolean / Enum / String 控件。
- [ ] 支持应用、取消、恢复默认。
- [ ] 保存 profile 和 history。
- [ ] 移除 `MainForm.cs` 中 ONNX/Confidence 专用 UI 和专用修改逻辑。

### P5：Profile 导入导出和回滚

- [ ] Station 支持导出当前 profile。
- [ ] Station 支持导入 profile，并校验 packageId / flowHash。
- [ ] Station 支持恢复包默认 profile。
- [ ] Station 支持恢复 last-good profile。
- [ ] Studio 支持导入 Station profile 并展示 diff。
- [ ] Studio 支持选择性吸收现场值到工程默认参数。

### P6：质量、性能与低配机验证

- [ ] 增加参数 schema/profile 单元测试。
- [ ] 增加 package 导出/加载集成测试。
- [ ] 增加 Station profile store 测试。
- [ ] 增加旧包兼容测试。
- [ ] 增加无 schema 空状态测试。
- [ ] 验证参数面板 100 个参数渲染耗时。
- [ ] 验证拖动滑条不触发模型重载。
- [ ] 验证目录批量运行时 profile 不被每帧写盘。

---

## 9. 验收标准

### 9.1 功能验收

- [ ] Studio 可以导出包含 `runtime-parameters.json` 的 Runtime Package。
- [ ] 端子线序检测包中的 `DeepLearning.Confidence` 通过 schema 暴露为 `线序检测置信度`。
- [ ] Station 加载包后，只显示被 Studio 明确开放的参数。
- [ ] Station 不再通过硬编码 DeepLearning/ONNX 逻辑生成置信度控件。
- [ ] Station 修改置信度后，无需重新导出包即可在下一次运行生效。
- [ ] Station 重启后能继续使用上次保存的现场 profile。
- [ ] Station 可以恢复包默认参数。
- [ ] Station 可以导出和导入 profile。
- [ ] Studio 可以导入现场 profile 并展示和包默认值的差异。

### 9.2 架构验收

- [ ] Runtime Package 内的 `flow.json` 仍是 Studio 发布默认值。
- [ ] Site Profile 修改不改变 `FlowHash`。
- [ ] 运行记录能追溯 `PackageId`、`FlowHash`、`ProfileId`、`ProfileRevision`。
- [ ] Station 不暴露完整算子参数树。
- [ ] Station 不新增对 Studio 专用 UI、WebView2、Kestrel、wwwroot 的依赖。
- [ ] Runtime 覆盖逻辑位于 Runtime 层，不散落在 WinForms 控件事件里。

### 9.3 安全与可维护性验收

- [ ] 未在 schema 中出现的参数不能被 profile 覆盖。
- [ ] `siteTunable = false` 或 readonly 参数不能被覆盖。
- [ ] 数值越界会被拒绝并显示清晰错误。
- [ ] profile 导入时校验 packageId / flowHash，不允许静默套用到不兼容包。
- [ ] 每次参数应用都写入 revision 和历史记录。
- [ ] 参数修改不会泄露工程师本机路径、API key 或 Studio 临时文件。

### 9.4 性能验收

- [ ] 空 schema 或少量参数时，Station 加载包体感无明显变慢。
- [ ] 100 个参数以内的面板首次渲染目标小于 200ms。
- [ ] 用户拖动数值控件不会连续写盘。
- [ ] `NextRun` 参数修改不会重载 ONNX 模型。
- [ ] 批量运行时 profile 不参与每帧热路径。
- [ ] Station 空闲 CPU、内存占用相比改造前没有明显异常增长。

---

## 10. 推荐测试命令

遵守仓库 `AGENTS.md` 的 .NET 测试约束：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName RuntimePackageExporterTests,RuntimePackageLoaderTests,RuntimeParameterOverrideApplierTests `
  -NoBuild `
  -NoRestore
```

如果是第一次跑或相关项目还没有成功构建过，不要加 `-NoBuild -NoRestore`。

后续可增加 Station 相关测试：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName StationSiteProfileStoreTests,RuntimeParameterValidatorTests
```

不要对同一个 `.csproj` 并行启动多个 `dotnet test`。

---

## 11. 风险与处理建议

| 风险 | 表现 | 处理 |
|---|---|---|
| Station 继续堆专用面板 | 后续每个算子都要改 UI | 明确禁止算子专用 Station 参数面板，统一走 schema |
| 参数 ID 不稳定 | Studio 重新保存后 profile 对不上 | 使用稳定 nodeId + parameterName，并在导出时校验 |
| Profile 直接改包内 flow | 无法区分包默认值和现场值 | Runtime 运行前 clone flow，再应用覆盖 |
| 拖动滑条触发模型重载 | 低配工控机卡顿 | 按 applyMode 分级，默认 NextRun，写盘 debounce |
| object 类型反序列化混乱 | double/decimal/JsonElement 比较失败 | 统一使用 JsonElement 或明确的 RuntimeParameterValue |
| 旧包不兼容 | 之前导出的包无法加载 | 无 schema 视为空参数，不影响运行 |
| 现场 profile 失控 | 不知道谁改了什么 | revision + history jsonl + 恢复默认 + last-good |
| Studio 吸收现场值过于随意 | 现场临时调参污染工程默认 | 导入 profile 只展示 diff，由工程师选择性吸收 |

---

## 12. 总控 Prompt

下面这段可直接给 Codex 作为实施总控 prompt：

```text
你在 C:\Users\11234\Desktop\ClearVision 仓库中工作。请按照 docs/进行中/当前计划/ClearVision-Station现场可调参数Profile机制TODO-2026-05-03.md 实施“Station 现场可调参数 / Profile 覆盖机制”。

目标：
1. Runtime Package 携带 Studio 发布的现场可调参数 schema 和默认 site profile。
2. Station 读取 schema 动态渲染参数 UI，只显示 Studio 明确开放的参数。
3. Station 将现场修改保存为独立 profile，不修改包内 flow.json。
4. RuntimeHost 运行前应用 profile 覆盖到执行流副本。
5. 迁移当前 Station 中硬编码的 ONNX / DeepLearning.Confidence 面板，让它成为 schema 驱动的第一条参数。

实施要求：
- 先阅读 AGENTS.md 和本 TODO。
- 先检查当前 Runtime / Station / Desktop 导出代码，基于现有模式小步修改。
- 不要把 Station 做成完整 Studio，不要暴露完整算子参数树。
- 不要新增 WebView2 / Kestrel / wwwroot 到 Station。
- Runtime 层负责 schema/profile 校验和覆盖应用，WinForms 层只负责渲染和用户交互。
- 旧 Runtime Package 没有 schema 时必须继续可加载、可运行，只显示空参数状态。
- 所有 profile 覆盖必须校验 parameterId、类型、范围、权限、siteTunable。
- 参数修改要有 revision/history，并支持恢复包默认值。
- 使用 apply_patch 做手工文件编辑。
- 测试遵守 AGENTS.md：同一个 .csproj 不要并行跑 dotnet test，优先使用 scripts/run-dotnet-test-serial.ps1。

建议阶段：
P0 DTO 和 manifest 字段。
P1 RuntimePackageExporter / Loader / Validator 支持 runtime-parameters.json 和 station-profile.default.json。
P2 RuntimeParameterOverrideApplier 和 RuntimeHost profile 应用。
P3 Studio 导出 DeepLearning.Confidence schema。
P4 Station 通用参数面板和 profile store，移除 ONNX 专用 UI。
P5 测试与验收。

交付时请说明：
- 改了哪些文件。
- schema/profile 文件格式是什么。
- DeepLearning.Confidence 如何从硬编码迁移为 schema。
- 运行了哪些测试，结果如何。
- 仍有哪些第二轮事项。
```

---

## 13. 验收 Prompt

下面这段可给另一个 Codex 或当前 Codex 在实现后做验收：

```text
请以代码审查和验收测试的姿态检查本次“Station 现场可调参数 / Profile 覆盖机制”实现。

请重点验证：
1. Runtime Package 是否导出 runtime-parameters.json 和 station-profile.default.json。
2. RuntimePackageLoader 是否兼容旧包：无 schema 时仍可运行。
3. RuntimeParameterOverrideApplier 是否只修改执行流副本，不污染 RuntimePackage.Flow。
4. profile 覆盖是否校验 parameterId、类型、范围、siteTunable、readonly。
5. Station 是否通过通用 schema 渲染参数面板，而不是继续硬编码 ONNX / DeepLearning.Confidence。
6. Station 是否只显示 Studio 明确开放的参数，不显示完整算子参数树。
7. 修改参数后是否保存 station-profile.json、递增 revision、写入 history。
8. Station 重启后是否能加载该包对应的 active profile。
9. 恢复默认、导入 profile、导出 profile 是否可用。
10. 参数修改是否不会导致每次控件变化都重载 ONNX 模型或重建完整流程。
11. 运行结果或日志是否能追溯 packageId、flowHash、profileId、profileRevision。
12. Station 项目是否仍不依赖 WebView2 / Kestrel / wwwroot / Desktop UI。

请执行或建议执行以下验证：
- 构建相关项目。
- 使用 scripts/run-dotnet-test-serial.ps1 串行运行 Runtime 参数相关测试。
- 构造一个包含 DeepLearning.Confidence 的测试包，检查 schema 内容。
- 在 Station 加载该包，修改置信度，确认下一次运行使用新值。
- 关闭并重启 Station，确认 profile 仍然生效。
- 导入不匹配 flowHash 的 profile，确认被拒绝或明确提示。

输出格式：
先列阻塞问题和风险，带文件路径与行号。
再列已通过验收项。
最后给出是否建议合并，以及需要补的测试。
```

---

## 14. 第一轮建议最小闭环

如果要最快落地，不要一口吃完所有 UI 和权限。第一轮只做这个闭环：

```text
Studio 导出 DeepLearning.Confidence schema
Runtime 加载 schema/profile
Station 通用 Number 控件渲染它
Station 保存 station-profile.json
RuntimeHost 下一次运行应用该 profile
Station 可恢复默认
```

这个闭环完成后，再扩展：

```text
更多参数类型
分组和搜索
工程师权限
profile 导入导出
Studio 反向吸收现场 profile
ROI / 光源 / 曝光 / PLC 超时等高级现场参数
```

判断是否走对方向的标准很简单：

```text
以后新增一个现场可调参数时，主要改 Studio 的参数开放配置和 schema，不再改 Station 专用 UI。
```

---
title: "ClearVision Station 现场可调参数 / Profile 机制定稿 TODO"
doc_type: "task-list"
status: "closed"
topic: "runtime-station-site-tunable-parameters-v1"
created: "2026-05-03"
updated: "2026-05-03"
v1_completed: "2026-05-03"
decision: "V1 只交付 schema-driven DeepLearning.Confidence 最小闭环，其余能力进入 V2+"
sources:
  - "docs/进行中/当前计划/ClearVision-Station现场可调参数Profile机制TODO-2026-05-03.md"
  - "C:/Users/11234/.gemini/antigravity/brain/d463a10c-9ad6-4e59-ae46-f4686d7a304a/audit_report.md.resolved"
  - "Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeContracts.cs"
  - "Acme.Product/src/Acme.Product.Runtime/RuntimePackageExporter.cs"
  - "Acme.Product/src/Acme.Product.Runtime/RuntimePackageLoader.cs"
  - "Acme.Product/src/Acme.Product.Runtime/RuntimeHost.cs"
  - "Acme.Product/src/Acme.Product.Station/MainForm.cs"
  - "Acme.Product/src/Acme.Product.Station/StationLocalSettingsStore.cs"
---

# ClearVision Station 现场可调参数 / Profile 机制定稿 TODO

> 北极星指标：以后新增一个现场可调参数时，主要改 Studio/Runtime 导出的参数 schema，不再给 Station 写算子专用 UI。

本文件是定稿执行版。上一版计划方向正确，但范围偏大；结合审计意见后，V1 只做最小闭环：把当前 Station 中硬编码的 `ONNX 参数 / DeepLearning.Confidence` 改成 schema-driven 的现场参数机制。

---

## 0. 最终决策

### 0.1 V1 做什么

```text
Studio/Exporter
  导出 field/runtime-parameters.json
  导出 field/station-profile.default.json
  V1 先自动发现 DeepLearning.Confidence 并生成一条数值参数定义

Runtime
  加载 schema/profile
  校验 profile 覆盖值
  运行前 clone package.Flow，再把 profile override 应用到 clone
  不污染 RuntimePackage.Flow

Station
  加载包后读取 schema
  用通用 NumericUpDown 渲染 Confidence
  把现场值保存到本地 site-profile.json
  下一次运行应用该 profile
  支持恢复包默认值
  移除 ONNX/DeepLearning.Confidence 专用 UI 和专用修改逻辑
```

### 0.2 V1 不做什么

以下内容全部进入 V2+，不要混入 V1：

- ROI / ROI 编辑器。
- Path / PathPicker。
- Studio 导入现场 profile 并反向吸收。
- Profile 导入 / 导出。
- 三级权限体系。
- `changeReasonRequired`。
- `history.jsonl` 历史追踪。
- `Immediate` / `NextFrame` 热更新。
- ProfileHash。
- 100+ 参数面板性能优化。
- 完整参数搜索、折叠、高级分组。

V1 的验收标准只有一个：端子线序检测里的 `DeepLearning.Confidence` 不再由 Station 硬编码，而是完整走 schema -> profile -> runtime override。

---

## 1. 关键边界

### 1.1 Runtime Package 不可变

包内 `flow.json` 是 Studio 发布默认值。Station 现场调参不能直接改 `RuntimePackage.Flow`，也不能改包内文件。

当前 `MainForm.ApplyConfidenceFromUi()` 会直接修改 `_runtimeHost.LoadedPackage.Flow` 上的参数，这个实现必须在 V1 中移除。替代路径是：

```text
Station UI 修改值
  -> 保存本地 RuntimeSiteProfile
  -> RuntimeHost 记录 active profile
  -> ExecuteSingleCoreAsync 每次运行前 clone package.Flow
  -> RuntimeParameterOverrideApplier 把 override 应用到 clone
  -> RuntimeFlowAdapter.ToEntity(executionFlowClone)
```

### 1.2 Profile 绑定 packageId + flowHash

V1 参数 ID 可以使用包内稳定标识：

```text
node.{OperatorDto.Id:D}.{ParameterName}
```

原因：

- V1 profile 只绑定同一个 `packageId + flowHash`。
- 如果 Studio 重新导出导致 flow 变化，`flowHash` 不匹配，Station 必须拒绝套用旧 profile。
- 因此 V1 不需要先解决跨版本参数迁移问题。

V2 如果要支持“现场 profile 导入 Studio 并迁移到新包”，再引入稳定 `nodeAlias` 或参数迁移表。

### 1.3 schema 文件统一放在 field 目录

当前导出器已创建 `field/` 并写入 `station-profile.json`、`trigger-profile.json`、`result-mapping-profile.json`、`model-assets.json`。

V1 新增文件也放在 `field/` 下：

```text
field/runtime-parameters.json
field/station-profile.default.json
```

Manifest 扩展：

```text
RuntimeFieldExtensions.RuntimeParameters = "field/runtime-parameters.json"
RuntimeFieldExtensions.DefaultSiteProfile = "field/station-profile.default.json"
```

保留现有：

```text
RuntimeFieldExtensions.StationProfile = "field/station-profile.json"
```

V1 不把 `field/station-profile.json` 当作现场 override 文件；它仍是现场部署描述草稿。真正的 active profile 保存在 Station 本地。

### 1.4 Station 本地 profile 存储沿用现有根目录

当前 `StationLocalSettingsStore` 使用：

```text
%LocalAppData%/ClearVisionStation/
```

V1 不切换到 `%ProgramData%`，避免两套持久化根目录。新增：

```text
%LocalAppData%/ClearVisionStation/profiles/{packageId}_{flowHash}/site-profile.json
```

`flowHash` 用于目录名时要做文件名安全处理，例如去掉 `sha256:`、替换非法字符。

---

## 2. V1 数据契约

只实现 V1 需要的最少字段，避免远景字段污染当前代码。

### 2.1 RuntimeFieldExtensions

在 `RuntimeContracts.cs` 中扩展：

```csharp
public sealed class RuntimeFieldExtensions
{
    public string? StationProfile { get; set; }
    public string? TriggerProfile { get; set; }
    public string? ResultMappingProfile { get; set; }
    public string? ModelAssets { get; set; }
    public string? RuntimeParameters { get; set; }
    public string? DefaultSiteProfile { get; set; }
}
```

### 2.2 RuntimeParameterSchema

```csharp
public sealed class RuntimeParameterSchema
{
    public string SchemaVersion { get; set; } = "1.0";
    public string PackageId { get; set; } = string.Empty;
    public string FlowHash { get; set; } = string.Empty;
    public List<RuntimeParameterDefinition> Parameters { get; set; } = [];
}
```

### 2.3 RuntimeParameterDefinition

```csharp
public sealed class RuntimeParameterDefinition
{
    public string Id { get; set; } = string.Empty;
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string OperatorType { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GroupName { get; set; } = "现场参数";
    public RuntimeParameterValueType ValueType { get; set; } = RuntimeParameterValueType.Number;
    public RuntimeParameterUiKind UiKind { get; set; } = RuntimeParameterUiKind.NumericInput;
    public JsonElement DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? Step { get; set; }
    public bool SiteTunable { get; set; } = true;
    public bool RequiresEngineerMode { get; set; } = true;
    public RuntimeParameterApplyMode ApplyMode { get; set; } = RuntimeParameterApplyMode.NextRun;
    public int Order { get; set; }
}
```

V1 枚举只保留：

```csharp
public enum RuntimeParameterValueType
{
    Number = 0
}

public enum RuntimeParameterUiKind
{
    NumericInput = 0
}

public enum RuntimeParameterApplyMode
{
    NextRun = 0
}
```

### 2.4 RuntimeSiteProfile

```csharp
public sealed class RuntimeSiteProfile
{
    public string ProfileVersion { get; set; } = "1.0";
    public string ProfileId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string FlowHash { get; set; } = string.Empty;
    public int Revision { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = "local-engineer";
    public List<RuntimeParameterOverride> Overrides { get; set; } = [];
}
```

### 2.5 RuntimeParameterOverride

```csharp
public sealed class RuntimeParameterOverride
{
    public string ParameterId { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
}
```

说明：

- V1 使用 `JsonElement` 承载值，避免 `object` 反序列化后的 `double` / `decimal` / `JsonElement` 混乱。
- V1 不存 `reason`、`updatedBy`、`history` 到每个 override，先由 profile 顶层记录。

---

## 3. 文件格式示例

### 3.1 field/runtime-parameters.json

```json
{
  "schemaVersion": "1.0",
  "packageId": "cvpkg-20260503-abc",
  "flowHash": "9f0b...",
  "parameters": [
    {
      "id": "node.7d5c1f29-08ef-4b12-91d8-b1f7f4d94871.Confidence",
      "operatorId": "7d5c1f29-08ef-4b12-91d8-b1f7f4d94871",
      "operatorName": "线序检测",
      "operatorType": "DeepLearning",
      "parameterName": "Confidence",
      "displayName": "线序检测置信度",
      "description": "低于该置信度的检测结果不参与线序判定。",
      "groupName": "现场参数",
      "valueType": "Number",
      "uiKind": "NumericInput",
      "defaultValue": 0.6,
      "min": 0.0,
      "max": 1.0,
      "step": 0.01,
      "siteTunable": true,
      "requiresEngineerMode": true,
      "applyMode": "NextRun",
      "order": 10
    }
  ]
}
```

### 3.2 field/station-profile.default.json

```json
{
  "profileVersion": "1.0",
  "profileId": "package-default",
  "packageId": "cvpkg-20260503-abc",
  "flowHash": "9f0b...",
  "revision": 0,
  "updatedAtUtc": "2026-05-03T00:00:00Z",
  "updatedBy": "ClearVision Studio",
  "overrides": []
}
```

### 3.3 Station 本地 site-profile.json

```json
{
  "profileVersion": "1.0",
  "profileId": "local-site",
  "packageId": "cvpkg-20260503-abc",
  "flowHash": "9f0b...",
  "revision": 3,
  "updatedAtUtc": "2026-05-03T08:30:00Z",
  "updatedBy": "local-engineer",
  "overrides": [
    {
      "parameterId": "node.7d5c1f29-08ef-4b12-91d8-b1f7f4d94871.Confidence",
      "value": 0.72
    }
  ]
}
```

---

## 4. 分阶段 TODO

### P0：前置确认与契约落地

涉及文件：

```text
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeContracts.cs
Acme.Product/src/Acme.Product.Application/DTOs/OperatorDto.cs
```

任务：

- [x] 确认 V1 profile 作用域为 `packageId + flowHash`，不承诺跨包版本迁移。
- [x] 确认 `OperatorDto.Id` 在导出的 `flow.json` 中存在且唯一。
- [x] 确认 `ParameterDto.Name` 对同一算子内唯一；V1 parameterId 使用 `OperatorDto.Id + ParameterName`。
- [x] 在 `RuntimeFieldExtensions` 增加 `RuntimeParameters` 和 `DefaultSiteProfile`。
- [x] 新增 V1 DTO：`RuntimeParameterSchema`、`RuntimeParameterDefinition`、`RuntimeSiteProfile`、`RuntimeParameterOverride`。
- [x] 新增 V1 枚举：`RuntimeParameterValueType.Number`、`RuntimeParameterUiKind.NumericInput`、`RuntimeParameterApplyMode.NextRun`。

验收：

- [x] DTO 可 JSON 序列化 / 反序列化。
- [x] 不引入 ROI、Path、权限三级、热更新等 V2 字段。

### P1：Runtime Package 导出 schema/profile

涉及文件：

```text
Acme.Product/src/Acme.Product.Runtime/RuntimePackageExporter.cs
Acme.Product/src/Acme.Product.Runtime/RuntimePackage.cs
Acme.Product/src/Acme.Product.Runtime/RuntimePackageExportResult.cs
```

任务：

- [x] `RuntimePackageExporter` 在 `FieldExtensions` 中写入：
  - `RuntimeParameters = "field/runtime-parameters.json"`
  - `DefaultSiteProfile = "field/station-profile.default.json"`
- [x] 导出 `field/runtime-parameters.json`。
- [x] 导出 `field/station-profile.default.json`。
- [x] V1 只自动发现 `OperatorType.DeepLearning` 且存在 `Confidence` 参数的节点。
- [x] `Confidence` 默认值优先取 `Value`，没有则取 `DefaultValue`，还没有则用 `0.5`。
- [x] `Confidence` 范围优先取参数原有 `MinValue/MaxValue`，没有则用 `0.0/1.0`，步进用 `0.01`。
- [x] 如果没有任何可调参数，也导出空 `parameters: []`，Station 显示空状态。
- [x] 不在 Station 侧补造 `Confidence` 参数；包里没有就不显示。

验收：

- [x] 端子线序检测包导出后包含 `field/runtime-parameters.json`。
- [x] 文件中包含 `DeepLearning.Confidence` 定义。
- [x] 旧的 `field/station-profile.json`、`trigger-profile.json`、`result-mapping-profile.json`、`model-assets.json` 仍照常导出。

### P2：Runtime 加载、校验和 override 应用

涉及文件：

```text
Acme.Product/src/Acme.Product.Runtime/RuntimePackageLoader.cs
Acme.Product/src/Acme.Product.Runtime/RuntimePackageValidator.cs
Acme.Product/src/Acme.Product.Runtime/RuntimeHost.cs
新增：Acme.Product/src/Acme.Product.Runtime/RuntimeParameterOverrideApplier.cs
新增：Acme.Product/src/Acme.Product.Runtime/RuntimeParameterValidator.cs
```

任务：

- [x] `RuntimePackage` 增加 `ParameterSchema` 和 `DefaultSiteProfile` 属性。
- [x] `RuntimePackageLoader` 加载 `field/runtime-parameters.json`，缺失时返回空 schema，不报错。
- [x] `RuntimePackageLoader` 加载 `field/station-profile.default.json`，缺失时构造空默认 profile。
- [x] `RuntimeParameterValidator` 校验：
  - profile 的 `packageId` 和 `flowHash` 必须匹配。
  - override 的 `parameterId` 必须存在于 schema。
  - definition 必须 `SiteTunable = true`。
  - V1 value 必须是 number。
  - value 必须在 `Min/Max` 范围内。
- [x] `RuntimeParameterOverrideApplier` 负责 clone `package.Flow` 并应用 override。
- [x] override 只改 clone，不改 `RuntimePackage.Flow`。
- [x] `RuntimeHost` 增加 active site profile 状态。
- [x] `RuntimeHost.ExecuteSingleCoreAsync` 改为：
  - clone + apply profile。
  - 对 clone 调用 `RuntimeFlowAdapter.ToEntity(...)`。
- [x] profile 无效时，运行返回明确错误或拒绝应用，并写入日志。

验收：

- [x] 无 schema 的旧包仍能加载和运行。
- [x] 越界 confidence 被拒绝。
- [x] 未知 parameterId 被拒绝。
- [x] 执行一次带 override 的运行后，`RuntimePackage.Flow` 默认值不变。

### P3：Station 本地 profile store

涉及文件：

```text
Acme.Product/src/Acme.Product.Station/StationLocalSettingsStore.cs
新增：Acme.Product/src/Acme.Product.Station/StationSiteProfileStore.cs
```

任务：

- [x] 新增 `StationSiteProfileStore`。
- [x] 存储根目录沿用：
  - `%LocalAppData%/ClearVisionStation/`
- [x] active profile 路径：
  - `profiles/{packageId}_{flowHash}/site-profile.json`
- [x] 加载包后：
  - 如果本地 active profile 存在且匹配，加载它。
  - 否则从 package default profile 构造本地 profile。
- [x] 保存 profile 时：
  - 递增 `Revision`。
  - 更新 `UpdatedAtUtc`。
  - `UpdatedBy` 暂写 `local-engineer`。
- [x] 支持 `ResetToPackageDefault`：删除 overrides，revision 递增。

验收：

- [x] Station 关闭重启后，仍能加载上次保存的 profile。
- [x] flowHash 不匹配的 profile 不会被应用。
- [x] 恢复默认后，profile overrides 为空。

### P4：Station 通用参数面板，移除 ONNX 专用 UI

涉及文件：

```text
Acme.Product/src/Acme.Product.Station/MainForm.cs
新增：Acme.Product/src/Acme.Product.Station/RuntimeParameterPanel.cs
新增：Acme.Product/src/Acme.Product.Station/RuntimeParameterControlFactory.cs
```

任务：

- [x] 新增通用 `RuntimeParameterPanel`。
- [x] V1 只渲染 `RuntimeParameterValueType.Number + NumericInput` 为 `NumericUpDown`。
- [x] 参数控件显示：
  - 显示名。
  - 当前值。
  - 默认值。
  - 范围。
  - “下次运行生效”。
- [x] 提供按钮：
  - 应用。
  - 取消修改。
  - 恢复默认。
- [x] 加载包后，Station 从 schema 渲染参数。
- [x] 用户点击应用后，写入 `StationSiteProfileStore`，并通知 `RuntimeHost` 使用新 active profile。
- [x] 空 schema 时显示”当前运行包未开放现场参数”。
- [x] 移除 `MainForm.cs` 中以下专用逻辑：
  - `_onnxParameterDetailLabel`
  - `_confidenceNumericUpDown`
  - `_updatingOnnxParameterControls`
  - `BuildOnnxParameterContent()`
  - `RefreshOnnxParameterControls()`
  - `ApplyConfidenceFromUi()`
  - `GetDeepLearningOperators()`
  - 针对 `Confidence` 的 `EnsureParameter(...)` 调用
- [x] Station UI 文案不再出现”ONNX 参数”作为专用卡片标题，改为”现场参数”。

验收：

- [x] 端子线序包加载后，能显示”线序检测置信度”。
- [x] 该控件来自 schema，而不是 Station 识别 DeepLearning 后手写。
- [x] 修改数值并点击应用后，下一次运行使用新值。
- [x] 当前运行中修改不会重载模型；V1 只承诺 `NextRun`。

### P5：测试与收口

涉及文件：

```text
Acme.Product/tests/Acme.Product.Tests/Runtime/
Acme.Product/tests/Acme.Product.Tests/Station/
scripts/run-dotnet-test-serial.ps1
```

任务：

- [x] 增加 DTO JSON roundtrip 测试。
- [x] 增加 exporter 测试：DeepLearning.Confidence schema 导出。
- [x] 增加 loader 测试：旧包无 schema 兼容。
- [x] 增加 validator 测试：越界值、未知 parameterId、flowHash mismatch。
- [x] 增加 applier 测试：override 应用到 clone，不污染 package flow。
- [x] 增加 StationSiteProfileStore 测试：保存、重载、恢复默认。
- [ ] 手工验证 Station 加载端子线序包后的 UI 和运行闭环。

推荐测试命令：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName RuntimeParameterOverrideApplierTests,RuntimeParameterValidatorTests,RuntimePackageLoaderTests,RuntimePackageExporterTests,StationSiteProfileStoreTests
```

如果同一项目本轮尚未成功 build，不要加 `-NoBuild -NoRestore`。后续重复跑同一项目再加。

验收：

- [x] 相关测试通过。
- [x] 未新增 Station 对 WebView2 / Kestrel / wwwroot / Desktop UI 的依赖。
- [x] `MainForm.cs` 中不再存在 `ONNX 参数` / `ApplyConfidenceFromUi` / `BuildOnnxParameterContent`。

---

## 5. 阶段验收标准

### V1 必须通过

- [x] Runtime Package 包含 `field/runtime-parameters.json` 和 `field/station-profile.default.json`。
- [x] `DeepLearning.Confidence` 通过 schema 暴露为现场参数。
- [x] Station 只渲染 schema 中的参数。
- [x] Station 修改值保存到本地 `site-profile.json`。
- [x] RuntimeHost 下一次运行应用 profile override。
- [x] `RuntimePackage.Flow` 不被 Station 修改。
- [x] 旧包没有 schema 时仍可运行。
- [x] Station 不再硬编码 ONNX/Confidence 面板。

### V1 明确不验收

- [x] 不验收 ROI。
- [x] 不验收 PathPicker。
- [x] 不验收 profile 导入导出。
- [x] 不验收 Studio 吸收现场 profile。
- [x] 不验收热更新。
- [x] 不验收多级权限。
- [x] 不验收历史审计 jsonl。

---

## 6. 关键实现提示

### 6.1 RuntimeParameterOverrideApplier

实现建议：

```text
1. 用 RuntimeJson.SerializerOptions 把 package.Flow 序列化再反序列化，得到 clone。
2. 遍历 profile.Overrides。
3. 按 parameterId 找 definition。
4. 按 definition.OperatorId 找 clone.Operators。
5. 按 definition.ParameterName 找 op.Parameters。
6. 校验 value。
7. 设置 ParameterDto.Value。
8. 返回 clone 和 AppliedOverrideCount。
```

不要在 applier 里访问 WinForms、Station store 或文件系统。

### 6.2 JsonElement 数值处理

V1 只接收：

```text
JsonValueKind.Number
```

读取用：

```csharp
value.TryGetDouble(out var number)
```

写入 `ParameterDto.Value` 时使用 `double`，避免把 `JsonElement` 传到算子执行器里。

### 6.3 Exporter 的 Confidence 发现规则

V1 规则：

```text
foreach op in flow.Operators
  if op.Type == OperatorType.DeepLearning
    param = op.Parameters.FirstOrDefault(p => p.Name == "Confidence")
    if param != null
      emit definition
```

显示名优先：

```text
param.DisplayName
op.Name + "置信度"
"检测置信度"
```

V1 不在 exporter 中给所有算子建立完整参数 schema。

### 6.4 Station UI

V1 不追求复杂布局，建议直接把当前右侧 `ONNX 参数` 卡片替换为 `现场参数` 卡片：

```text
现场参数
  线序检测置信度 [NumericUpDown]
  默认值：0.60，范围：0.00 - 1.00
  下次运行生效
  [应用] [取消] [恢复默认]
```

如果参数为空：

```text
当前运行包未开放现场参数
```

---

## 7. 总控 Prompt

```text
你在 C:\Users\11234\Desktop\ClearVision 仓库工作。请严格按照 docs/进行中/当前计划/ClearVision-Station现场可调参数Profile机制定稿TODO-2026-05-03.md 实施 V1。

本次只做 V1 最小闭环，不做 V2+ 内容。

目标：
1. Runtime Package 导出 field/runtime-parameters.json 和 field/station-profile.default.json。
2. V1 只支持 DeepLearning.Confidence 这一类 Number + NumericInput 参数。
3. Station 通过 schema 通用渲染现场参数，不再硬编码 ONNX/Confidence UI。
4. Station 将现场修改保存到 %LocalAppData%/ClearVisionStation/profiles/{packageId}_{flowHash}/site-profile.json。
5. RuntimeHost 下一次运行前 clone package.Flow 并应用 active profile override。
6. Station 不能直接修改 RuntimePackage.Flow。
7. 旧包没有 schema 时仍然可加载运行，只显示空参数状态。

不要做：
- ROI / PathPicker。
- Profile 导入导出。
- Studio 导入现场 profile。
- 三级权限。
- history.jsonl。
- Immediate / NextFrame 热更新。
- ProfileHash。

请先完成 P0-P2 的 Runtime 契约、导出、加载、override，再做 P3-P4 的 Station UI 和本地 profile store，最后做 P5 测试。

测试遵守 AGENTS.md：
- 同一个 .csproj 不要并行 dotnet test。
- 优先使用 ./scripts/run-dotnet-test-serial.ps1。
- 后续重复测试同一项目且已成功 build 后，再使用 -NoBuild -NoRestore。

交付时说明：
- 改了哪些文件。
- field/runtime-parameters.json 的格式。
- Station 中 ONNX 专用代码如何被移除。
- RuntimeHost 如何保证不污染 RuntimePackage.Flow。
- 跑了哪些测试。
```

---

## 8. 验收 Prompt

```text
请验收“Station 现场可调参数 / Profile 机制 V1”实现，重点检查是否严格遵守定稿范围。

必须检查：
1. RuntimePackageExporter 是否导出 field/runtime-parameters.json 和 field/station-profile.default.json。
2. runtime-parameters.json 是否只包含 Studio/Exporter 明确开放的参数，V1 至少包含 DeepLearning.Confidence。
3. RuntimePackageLoader 是否兼容旧包：无 schema 时仍能加载运行。
4. RuntimeParameterValidator 是否拒绝 packageId/flowHash 不匹配、未知 parameterId、非 number、越界值。
5. RuntimeParameterOverrideApplier 是否 clone flow 后应用 override，不修改 RuntimePackage.Flow。
6. RuntimeHost.ExecuteSingleCoreAsync 是否使用应用 profile 后的 clone flow。
7. Station 是否从 schema 通用渲染 NumericUpDown，而不是硬编码 DeepLearning/ONNX。
8. MainForm.cs 是否已移除 ONNX 参数卡片、ApplyConfidenceFromUi、BuildOnnxParameterContent 等专用逻辑。
9. Station 是否把 profile 保存到 %LocalAppData%/ClearVisionStation/profiles/{packageId}_{flowHash}/site-profile.json。
10. 恢复默认是否能清空 overrides 并让下一次运行回到包默认值。

明确不要求：
- ROI。
- PathPicker。
- Profile 导入导出。
- Studio 反向吸收 profile。
- Immediate / NextFrame 热更新。
- 三级权限。
- history.jsonl。

请输出：
1. 阻塞问题，带文件路径和行号。
2. 已通过验收项。
3. 是否发现 V2+ 功能被提前混入。
4. 建议是否合并。
```

---

## 9. V2+ 停车场

V1 已于 2026-05-03 完成。以下按 **投入产出比** 和 **依赖关系** 分层排列，供挑下一轮时参考。

### Tier 1 — 框架自然延伸，改动小、收益大

| 编号 | 方向 | 概述 | 前置依赖 | 预估工作量 | 推荐理由 |
|:---:|:---|:---|:---|:---|:---|
| T1-a | **Bool / Enum / String 参数类型** | 扩展 `RuntimeParameterValueType` 和 `RuntimeParameterUiKind` 枚举，`RuntimeParameterControlFactory` 增加 `CheckBox`、`ComboBox`、`TextBox` 分支。 | 无（纯框架扩展） | 1-2 天 | 当前 V1 只支持 Number，但实际业务中布尔开关（启用/禁用）、枚举选择（模式 A/B/C）、字符串输入（阈值名称）很常见。改动集中在 ControlFactory 和 Validator，风险低。 |
| T1-b | **Profile 导入 / 导出** | Station 提供导出为 `.json` 文件和从文件导入的按钮，导入时校验 `packageId + flowHash` 匹配。 | 无 | 1 天 | 现场多台同型号设备部署时，工程师调好一台的参数后可以批量复制，减少重复劳动。实现简单：序列化 `RuntimeSiteProfile` + 文件对话框。 |
| T1-c | **参数分组与折叠** | 按 `GroupName` 分组渲染，支持折叠/展开。 | 无 | 0.5 天 | 当前 V1 只有一个参数所以不明显，但一旦参数超过 5 个就需要分组，否则界面混乱。在 `RuntimeParameterPanel` 的 `TableLayoutPanel` 外层加 `GroupBox` 即可。 |

### Tier 2 — 业务驱动，需要 Studio 或硬件配合

| 编号 | 方向 | 概述 | 前置依赖 | 预估工作量 | 推荐理由 |
|:---:|:---|:---|:---|:---|:---|
| T2-a | **光源 / 曝光 / PLC 超时等硬件参数** | Exporter 自动发现光源控制器、相机、PLC 算子的可调参数并写入 schema。 | 需确认哪些算子有硬件参数可暴露 | 3-5 天 | 比 Confidence 更高频——生产线换料时光源亮度、曝光时间经常要调。需要和算子开发者对齐参数命名和范围。 |
| T2-b | **ROI 参数** | 支持 `RuntimeParameterValueType.Roi`，Station 端渲染 ROI 框选编辑器。 | 需设计 ROI 的 JsonElement 表示格式 | 3-5 天 | 很多视觉算子的检测区域需要现场微调。但 ROI 编辑涉及坐标系、图像叠加渲染，UI 复杂度高。建议先定义数据格式，再做 UI。 |
| T2-c | **Path / PathPicker 参数** | 支持 `RuntimeParameterValueType.Path`，Station 端渲染文件/目录选择器。 | 需定义相对路径 vs 绝对路径策略 | 1-2 天 | 模型文件路径、输出目录等在不同设备上可能不同。实现相对简单，但需要考虑路径安全（防止目录遍历）。 |
| T2-d | **Immediate / NextFrame 热更新** | 扩展 `RuntimeParameterApplyMode`，支持 `Immediate`（当前帧生效）和 `NextFrame`（下一帧生效）。 | 需 RuntimeHost 支持运行中参数注入 | 3-5 天 | 对于调光源/曝光等实时性要求高的参数很有用。但需要 RuntimeHost 在运行循环中检查参数变更，涉及并发安全。 |

### Tier 3 — 流程与治理，适合生产稳定后

| 编号 | 方向 | 概述 | 前置依赖 | 预估工作量 | 推荐理由 |
|:---:|:---|:---|:---|:---|:---|
| T3-b | **参数修改历史 (history.jsonl)** | 每次修改 profile 时追加一行到 `history.jsonl`，记录时间、操作人、旧值、新值。 | 无 | 1 天 | 审计追溯需求。V1 的 `UpdatedAtUtc` / `UpdatedBy` 只记录最后一次，不保留历史。jsonl 格式简单追加，不需数据库。 |
| T3-c | **Studio 导入现场 profile 并选择性吸收** | Studio 端可以读取 Station 导出的 profile，选择性地将现场值写回 `flow.json` 默认值。 | 需 Studio UI 支持 | 3-5 天 | 用于"现场调好后回写到工程包"的闭环。依赖 T1-b（导入导出）作为基础。 |
| T3-d | **跨包版本的 profile 迁移** | Studio 重新导出包后，旧 profile 能通过参数迁移表映射到新包。 | 需设计稳定参数标识（`nodeAlias` 或迁移表） | 3-5 天 | 当 flowHash 变化时旧 profile 会被拒绝。如果 Studio 频繁迭代包版本，工程师每次都要重新调参。需要引入稳定的参数别名机制。 |

### 推荐下一轮组合

如果要挑一个 V2 sprint，建议组合：

1. **T1-a（Bool/Enum/String 类型）** — 扩展框架，为后续更多参数类型打基础。
2. **T1-b（Profile 导入导出）** — 现场工程师立刻能用的实用功能。
3. **T3-b（参数修改历史）** — 低成本审计能力，生产合规需要。

这三者互相无依赖，可以并行开发，总工作量约 3-4 天。

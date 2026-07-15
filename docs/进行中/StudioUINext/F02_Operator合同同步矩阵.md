# F02 Operator 合同同步矩阵

> 状态：Goal 1 审计已由 Goal 2 identity-only scoped gate 关闭
>
> 适用分支：`studio-ui-next`
>
> 审计目标：仅识别 F02 Operator Catalog 所需的权威 metadata；禁止夹带算法、Runtime、AI、Legacy UI 或稳定线生成文档快照

## 1. Git 基线与审计来源

```text
F02_INITIAL_SHA=f6d4d98a53914bac088cd62cda261b2c08a11670
STABLE_LINE_SOURCE_SHA=f1efcfc11a8b31f389f3dcfef321763f84ff3a2b
MERGE_BASE=e1bad492fecb6dff2c0a8f848db9ebfa18acf093
TARGET_BRANCH=studio-ui-next
SOURCE_REF=origin/codex初稿
```

事实：

- `F02_INITIAL_SHA` 与 `origin/codex初稿` 不是线性祖先关系；共同基点为 `e1bad492`。
- `git cherry F02_INITIAL_SHA origin/codex初稿` 显示本矩阵审计的 19 个提交全部为 `+`，没有可按 patch-id 判定为整提交 `SKIP_ALREADY_PRESENT` 的提交。
- F02 Initial 的生成 catalog 与 merge-base 的生成 catalog 在 Operator 数据上相同，均为 158 个算子。
- 稳定线最终仍为 158 个算子，但 metadata 结构与内容已发生实质变化：52 个 `displayName`、101 个 `description`、138 个 `category`、17 个 `version` 与 Initial 不同。
- 稳定线最终分布为：Stable 150、Experimental 5、Reference 2、Legacy 1；默认隐藏 1 个算子。
- 稳定线最终有 20 个算子包含 parameter rules、6 个包含 output rules、97 个包含 image input contracts。
- Initial 的生成 catalog blob 为 657,185 bytes，稳定线为 1,455,133 bytes。该数值只证明生成文档体积变化，不能代替 `/api/operators/library` 的真实 HTTP payload 基线。

## 2. Goal 2 scoped gate 结论

```text
F02_OPERATOR_GATE_AUDIT_SHA=4958ecab5873160d96b8c34efcf5f488257ea4df
F02_OPERATOR_CONTRACT_SHA=52e4687e925a8f9a4f90df3ab47746795790043a
OPERATOR_IDENTITY_METADATA_SYNC=PASS
OPERATOR_CONTRACT_GATE=PASS
F02_OPERATOR_CONTRACT_SCOPE=CATALOG_IDENTITY_AND_CURRENT_BRANCH_RUNTIME_METADATA
STABLE_LINE_FULL_OPERATOR_CONTRACT_SYNC=DEFERRED
OPERATOR_SIDE_EFFECT_METADATA=NOT_AVAILABLE
OPERATOR_READINESS_METADATA=NOT_AVAILABLE
```

Goal 2 仅同步 `DisplayName / Description / CategoryId / Lifecycle / LifecycleNote / DefaultHidden /
IconName / Keywords / Tags`。158 个 Operator 文件去除 `[OperatorMeta]` block 后与 Goal 2 入口
`a23022be48c1e580198a41912c72ad0bbed753fd` 完全一致；DeepLearning、GaussianBlur、MeasureDistance
等稳定线 Version、conditional output rules、image-depth contracts、算法、Runtime、AI 与 Legacy 行为未同步。

Gate 验证事实：Catalog 总数 158、类型/显示名唯一、14 个 CategoryId 唯一且分布固定；默认 endpoint 157、
`includeCompatibility=true` 158；types/library/detail 对齐；scanner/runtime 的当前 input/output ports 与
parameters shape 一致。仓库 pre-commit 只基于当前分支 authority 刷新生成索引，未复制稳定线生成快照，
不得据此宣称完整稳定线 Operator 合同一致。

## 2.1 Goal 1 审计时结论

```text
OPERATOR_CONTRACT_GATE=BLOCKED
BLOCKED_OPERATOR_SYNC_SCOPE_EXPANSION
BLOCKED_OPERATOR_METADATA_FIELD_NOT_AUTHORITATIVE
OPERATOR_SIDE_EFFECT_METADATA=NOT_AVAILABLE
OPERATOR_READINESS_METADATA=NOT_AVAILABLE
```

阻断原因：

1. `CategoryId`、`Lifecycle`、`DefaultHidden`、中文显示名、描述和关键词等 identity metadata 可以从稳定线做受控语义同步。
2. `988681cf` 的 output rules 与 DeepLearning、MeasureDistance 等真实输出行为修改同提交耦合；不能仅复制声明并假定 Initial 分支的算法行为已经满足声明。
3. `08125e8a` 与 `82e34837` 的 image input contracts 与 Runtime admission evaluator、contract providers、多个算法算子及 OperatorLibrary descriptor 紧密耦合。
4. 在明确禁止算法、Runtime、AI 扩权的 Goal 1 中，只能完成 identity 子集，不能把该子集冒充为完整稳定线合同同步。

因此，在用户或主协调 owner 做出新的范围决定前，不得记录：

```text
F02_OPERATOR_CONTRACT_SHA=<sha>
STABLE_LINE_OPERATOR_SYNC=PASS
OPERATOR_METADATA_FIELDS_AUTHORITATIVE=PASS
```

## 3. 分类定义

| 分类 | 含义 |
|---|---|
| `DIRECT` | 文件是独立、纯合同定义，可在验证依赖后直接采用。 |
| `SEMANTIC_ADAPT` | 只移植与 F02 metadata 相关的最终语义或特定 hunk，不整提交、不整文件粗暴覆盖。 |
| `SKIP_ALREADY_PRESENT` | 当前分支已存在 patch-equivalent 或语义等价实现。本次没有 commit-wide 命中。 |
| `DEFER_OUT_OF_SCOPE` | 属于算法、Runtime、AI、Legacy、测试治理、性能证据或非 F02 Catalog 必需文档。 |
| `CONFLICT_RISK` | metadata 声明与算法/Runtime 权威紧耦合，脱离依赖同步会产生不真实合同。 |

## 4. 稳定线 19 个提交逐项分类

| Source SHA | Subject | 分类 | 文件与处置 |
|---|---|---|---|
| `ac7701ffdc35b896765ae9d786d3757070ca9bb4` | `fix: align operator display-name semantics` | `SEMANTIC_ADAPT` | 8 个 Operator 文件以及 `OperatorMetadataLocalization.cs`、`OperatorService.cs` 的显示名/描述语义。最终 identity 应从 `fe39d379` 的 attribute authority 吸收；生成文档、AI knowledge、Legacy 测试不直接同步。 |
| `fe4b42f13be1465976b365e2ba6d39e108486160` | `fix: synchronize corrected operator display names` | `SEMANTIC_ADAPT` | 仅吸收 PhaseClosure、RoiManager、TryCatch 的最终 metadata 语义；`DemoProjectService`、AI、Legacy UI、生成文档均排除。 |
| `4485c1d07b5bc3c16478e50a9e2bb4c6bfd82762` | `docs: align operator knowledge graph counts` | `DEFER_OUT_OF_SCOPE` | 仅 AI knowledge graph 数量文档，与 F02 Catalog HTTP 合同无直接关系。 |
| `0ebbb6ecc7e8ca15db516d3f9005e67471cbec2a` | `feat(operators): expand generalized operator capabilities` | `DEFER_OUT_OF_SCOPE` | 修改算法能力、AI、RuntimePackage、Flow/Preview、模型测试数据及多类测试；端口、参数和版本变化与实现绑定。 |
| `58545fab1a6ca031bb2de609733df13fccb5ab33` | `fix(operators): preserve generalized capability contracts` | `DEFER_OUT_OF_SCOPE` | 继续修复 generalized capability 真实执行合同，不是纯 Catalog metadata。 |
| `924e3afaf7261270c5af0bc74e4660b93e4dbca3` | `fix(operators): close display-name catalog drift` | `SEMANTIC_ADAPT` | localization 漂移收口；其最终 identity 结果由 `fe39d379` 的权威 attribute 形态取代。AI、Legacy UI 与其测试不进入 Goal 1。 |
| `2d941da14aeaa057fdf3c4b611fa2e708ba75352` | `fix(operators): align naming handbook contract` | `DEFER_OUT_OF_SCOPE` | 手册与命名兼容测试；可复用测试思想，不同步稳定线文档集合。 |
| `dca45286787c1fb37ecffe00dc15e29b88780d03` | `test(operators): harden naming compatibility contract` | `DEFER_OUT_OF_SCOPE` | legacy naming compatibility 测试与生成文档；不属于本次最小权威 metadata 文件集。 |
| `01e88c5e82ddc7c8ddbf3138383c225272f7331a` | `test(operators): cover all legacy display-name searches` | `DEFER_OUT_OF_SCOPE` | Legacy display-name search 测试，不迁入 F02 产品地基。 |
| `fe39d379ced2533fd3d06129f678140f6bea48b2` | `refactor(metadata): establish operator product metadata source` | `DIRECT` + `SEMANTIC_ADAPT` | 本次安全 identity 同步的主来源。新 Category/Lifecycle 定义可 `DIRECT`；attribute、scanner、factory merge、endpoint 和 158 个 Operator 的 `[OperatorMeta]` hunk 必须 `SEMANTIC_ADAPT`。Application service、AI、Legacy UI 排除。 |
| `988681cf6d16aa8155f095bb538a16c76314ea25` | `feat(metadata): unify conditional operator contracts` | `CONFLICT_RISK` | 纯 attribute 类型定义可审计，但 provider/factory/DI、18 个 Operator、AI、Runtime、Flow/Preview 和 Legacy property panel 相互耦合；DeepLearning、MeasureDistance、OperatorBase 明确包含执行语义修改。 |
| `6976c7bcc1b041ab1d8ebd18e28a30783d72522a` | `docs(metadata): regenerate governed operator catalogs` | `SEMANTIC_ADAPT` | fingerprint builder 与 generator 应基于最终获批合同做语义适配；所有 catalog/card/version-history 必须在目标分支重新生成，禁止直接复制稳定线快照。AI 与内存测试排除。 |
| `59a6aede4bc832946c6731e49a6b6f8aed18a264` | `fix(operators): correct zscore and laplacian semantics` | `DEFER_OUT_OF_SCOPE` | ZScore/Laplacian 算法语义、测试与版本变更。 |
| `d5ef2232ac325db39b862434186bb5658e65eb43` | `fix(operators): reject non-finite zscore inputs` | `DEFER_OUT_OF_SCOPE` | 算法输入校验与版本变更。 |
| `08125e8a7d1418411d97f134a98ebb1fd41d169e` | `feat(contracts): govern image depth domains` | `CONFLICT_RISK` | image contract 类型、OperatorLibrary 映射、Runtime evaluator/providers、14 个 Operator、AI、生成器和测试形成整体合同；禁止拆出声明冒充运行权威。 |
| `82e34837a3a29b132cc6384ab7f28c79c7850d05` | `fix(contracts): make image depth evidence mode-exact` | `CONFLICT_RISK` | 继续大幅修改 mode-exact evaluator/providers、算法合同、OperatorLibrary 及生成器；不能在 Goal 1 局部同步。 |
| `6c0fa1f029362239c6c5f252152bec44b3fb773c` | `test(governance): separate test semantics and quality lanes` | `DEFER_OUT_OF_SCOPE` | 全仓测试治理、CI lane、测试分类与大量测试项目变更。 |
| `5667bfbe75bebe491f1d4c533e93faf78f0f43c7` | `feat: complete stage 4 operator quality improvements` | `DEFER_OUT_OF_SCOPE` | Stage 4 算法质量、性能工具、Operator 版本和结果语义。 |
| `f1efcfc11a8b31f389f3dcfef321763f84ff3a2b` | `docs: record stage 4 performance evidence` | `DEFER_OUT_OF_SCOPE` | 仅 Stage 4 性能证据。 |

## 5. 安全 identity 同步白名单

以下白名单只定义“若主协调批准 identity-only partial gate，可以修改什么”，不表示完整合同门禁已通过。

### 5.1 `DIRECT`

Source SHA：`fe39d379ced2533fd3d06129f678140f6bea48b2`

```text
ClearVision.Product/src/ClearVision.Product.Core/Enums/OperatorCategoryId.cs
ClearVision.Product/src/ClearVision.Product.Core/Enums/OperatorLifecycle.cs
```

### 5.2 `SEMANTIC_ADAPT`

```text
ClearVision.Product/src/ClearVision.Product.Core/Attributes/OperatorMetaAttribute.cs
ClearVision.Product/src/ClearVision.Product.Core/Services/IOperatorFactory.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorMetadataScanner.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorFactoryMetadataMerge.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorMetadataLocalization.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorMetadataTextLocalization.cs
ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/ApiEndpoints.cs
ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/OperatorCatalogEndpointTests.cs
ClearVision.Product/tests/ClearVision.Product.Tests/Services/OperatorProductMetadataGovernanceTests.cs
```

另外允许修改 `fe39d379` 涉及的 158 个：

```text
ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/**/*.cs
```

但白名单只覆盖该提交中 `[OperatorMeta]` attribute 的 identity hunk：

- `DisplayName`
- `Description`
- `CategoryId`
- `Lifecycle`
- `LifecycleNote`
- `IconName`
- `Keywords`
- `Tags`

禁止借同文件白名单修改算法方法、执行输出、参数解析、Runtime admission、版本或测试数据。

`IOperatorFactory.cs` 在 identity-only 路线只允许增加：

- `CategoryId`
- `Lifecycle`
- `LifecycleNote`
- `DefaultHidden`

`ApiEndpoints.cs` 只允许同步：

- `includeCompatibility=false` 时过滤 `DefaultHidden`；
- 按 category order、displayName 稳定排序；
- `/api/operators/types` 继续只作为轻量索引，不成为显示名、分类或业务语义 authority。

## 6. 明确排除的文件与目录

Goal 1 不得因 Operator 合同同步修改：

```text
ClearVision.Product/src/ClearVision.Product.Application/Services/IOperatorService.cs
ClearVision.Product/src/ClearVision.Product.Application/Services/OperatorService.cs
ClearVision.Product/src/ClearVision.Product.Application/Services/DemoProjectService.cs
ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/**
ClearVision.Product/src/ClearVision.Product.Runtime/**
ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/**
ClearVision.OperatorLibrary/**
models/**
quality/**
docs/operators/**
docs/算子资料/**
算子资料/**
scripts/OperatorDocGenerator/**
```

同时排除：

- DeepLearning、MeasureDistance、Filtering、Image depth、ZScore、Laplacian 等算法主体；
- `ImageInputRuntimeContractEvaluator`、image contract providers、Runtime package validation；
- AI knowledge graph、prompt、tool catalog 和 AI 参数规则；
- Legacy operator palette、property panel 和 operator library；
- 稳定线生成的 catalog/card/version-history 快照；
- Stage 4 性能工具、证据与测试治理。

说明：F02 的三个 Operator GET 端点直接消费 `IOperatorFactory`，因此 Application `OperatorService` 不是 Catalog read contract 的必要依赖，不能以“保持服务一致”为由扩大 Goal 1 范围。

## 7. 为什么 Goal 1 不同步算法或 Runtime

Goal 1 的交付范围明确排除 Operator Catalog 产品页面、Flow 工作台、Preview、ImageCanvas、Runtime 与现场执行链。稳定线后半段的 Operator 合同不是独立 DTO 变更，而是以下整体：

```text
metadata declaration
  -> scanner/factory projection
  -> runtime admission/evaluator
  -> operator execution behavior
  -> OperatorLibrary adapter
  -> generated catalog/fingerprint
  -> AI/Legacy consumers
```

只复制 declaration 会产生“页面展示支持，但当前分支执行实现并不满足”的虚假合同；整批复制又会违反 Goal 1 的算法、Runtime、AI 边界。因此 Goal 1 只能冻结事实并保留阻断，不得通过扩大实现范围来消除阻断。

## 8. Goal 2 必须决定的接口

Goal 2 若要实施 Operator Catalog，用户或主协调 owner 必须在以下两条路线之间明确决定：

### 8.1 路线 A：identity-only partial gate

允许同步第 5 节白名单，形成：

```text
OPERATOR_IDENTITY_METADATA_SYNC=PASS
OPERATOR_CONTRACT_GATE=BLOCKED_OR_PARTIAL
PARAMETER_OUTPUT_RULE_SYNC=DEFERRED
IMAGE_INPUT_CONTRACT_SYNC=DEFERRED
```

该路线可冻结名称、类型、分类、关键词、Lifecycle 与 hidden 语义，但不能宣称完整 F02 Catalog detail contract 已满足。若页面仍展示 parameter/output rules、资源需求或图像输入限制，只能基于当前分支已有且已重新验证的字段，缺失处显示“暂无元数据”，不得从稳定线文档或类型名称推断。

### 8.2 路线 B：扩权完成完整同步

需要显式批准扩展到至少：

- `988681cf` conditional parameter/output contract 的运行语义依赖；
- `08125e8a` 与 `82e34837` image contract 的 Runtime/OperatorLibrary/算法依赖；
- 受影响 Product、Desktop、OperatorLibrary 项目的完整串行门禁；
- generator 语义同步和目标分支重新生成；
- payload、decoder、200 operators fixture 和 WebView2 范围重新冻结。

在批准前保持：

```text
BLOCKED_OPERATOR_SYNC_SCOPE_EXPANSION
BLOCKED_OPERATOR_METADATA_FIELD_NOT_AUTHORITATIVE
```

## 9. Source SHA 矩阵

```text
AUDIT_SOURCE_HEAD=f1efcfc11a8b31f389f3dcfef321763f84ff3a2b
IDENTITY_METADATA_SOURCE=fe39d379ced2533fd3d06129f678140f6bea48b2
CONDITIONAL_RULE_SOURCE=988681cf6d16aa8155f095bb538a16c76314ea25
GENERATED_CATALOG_SOURCE=6976c7bcc1b041ab1d8ebd18e28a30783d72522a
IMAGE_CONTRACT_SOURCE=82e34837a3a29b132cc6384ab7f28c79c7850d05
LATEST_ALGORITHM_QUALITY_SOURCE=5667bfbe75bebe491f1d4c533e93faf78f0f43c7
```

## 10. 建议验证命令

本矩阵是只读审计，以下命令尚未因本矩阵而执行。若批准 identity-only partial gate，先运行定向测试：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "./ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -FullyQualifiedName `
    "ClearVision.Product.Tests.Services.OperatorProductMetadataGovernanceTests",`
    "ClearVision.Product.Tests.Services.OperatorNamingSemanticContractTests",`
    "ClearVision.Product.Tests.Services.OperatorServiceTests" `
  -Configuration Release
```

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "./ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/ClearVision.Product.Desktop.Tests.csproj" `
  -FullyQualifiedName "ClearVision.Product.Desktop.Tests.OperatorCatalogEndpointTests" `
  -Configuration Release
```

由于 identity 迁移涉及 158 个 Operator attribute 文件，focused tests 不能替代完整 Product 门禁：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "./ClearVision.Product/tests/ClearVision.Product.Tests/ClearVision.Product.Tests.csproj" `
  -Configuration Release

& "./scripts/run-tests-desktop-endpoints.ps1" -Configuration Release

& "./scripts/dotnet.ps1" build `
  "./ClearVision.Product/ClearVision.Product.sln" `
  -c Release

git diff --check
```

若批准完整 image/conditional contract 同步，还必须增加：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "./ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests/ClearVision.OperatorLibrary.SmokeTests.csproj" `
  -Configuration Release
```

并串行运行 image depth/runtime contract、Operator Industrial Gate、generator 重建与生成物漂移检查。不得以 focused Product tests 代替受影响项目的完整门禁。

## 11. 审计真值声明

- 本矩阵仅记录 Git、当前代码与生成 catalog 的只读事实。
- 未运行 build、test、HTTP endpoint、WebView2、Operator Industrial Gate 或 generator。
- 未测量 `/api/operators/library` 的真实 payload、序列化 enum 形态或 decoder 时间。
- 未创建 `F02_OPERATOR_CONTRACT_SHA`。
- 未修改任何产品、算子、Runtime、AI、Legacy UI 或生成文档文件。

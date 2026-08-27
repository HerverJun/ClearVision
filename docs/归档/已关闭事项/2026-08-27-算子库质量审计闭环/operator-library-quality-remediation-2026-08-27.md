---
title: "算子库质量审计修复记录"
doc_type: "audit-remediation-record"
status: "closed"
topic: "算子审计"
created: "2026-08-27"
updated: "2026-08-27"
source_audit: "docs/审计资料/算子审计/operator-library-quality-audit-2026-08-25.md"
archive_note: "闭环说明.md"
---

# 算子库质量审计修复记录（2026-08-27）

本记录逐项签收 [2026-08-25 全量合同审计](../../../审计资料/算子审计/operator-library-quality-audit-2026-08-25.md)的开放项。每项只有在实现复审、定向源码重编译和相关回归全部通过后才转为完成；原审计报告保留发现时事实，不回写为历史上未发现问题。

## 状态

| ID | 状态 | 复审结论 | 验证 |
| --- | --- | --- | --- |
| F01 | 已完成 | 通过 | .NET 38/38；Node 32/32 |
| F02 | 已完成 | 通过 | 定向 46/46；服务回归相关项 540/540 |
| F03 | 已完成 | 通过 | .NET 38/38 |
| F04 | 已完成 | 通过 | .NET 11/11 |
| F05 | 已完成 | 通过 | .NET 78/78 |
| F06 | 已完成 | 通过 | .NET 13/13 |
| F07 | 已完成 | 通过 | Node 50/50 |
| R01 | 已处置 | 通过 | .NET 8/8 |
| R02 | 已处置 | 通过 | .NET 5/5 |
| R03 | 已处置 | 通过 | .NET 4/4 |
| R04 | 已处置 | 通过 | .NET 19/19 |

## F01 旧端口迁移与连接原子性

### 实现

- 旧占位端口按声明顺序规范名称、方向、类型和必填状态，同时保留已有非空 GUID，避免连接语义漂移。
- 仅当缺失 GUID 能唯一对应一个端口时同步重写连接；空端口列表、多候选空 GUID、缺失算子或悬空端口均以包含连接上下文的异常拒绝迁移。
- 前端反序列化在显式 `sourcePortId` / `targetPortId` 无法解析时拒绝恢复，不再静默回退到端口索引 0。

### 复审

- 多输入算子的第二端口连接在迁移后仍绑定原 GUID，DTO 再实体化后语义不变。
- 无语义证据的历史连接在修改 DTO 前被拒绝；不会生成可被后续保存的错误连接。
- 仅使用旧前端索引且未提供 Port ID 的历史前端数据仍保留原兼容路径。

### 验证

- `ProjectServiceTests`：38/38 通过；当前源码完成 restore/build 后执行。
- `canvas-core.test.mjs`：32/32 通过。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。
- 非阻塞构建告警：`OperatorLibraryReadOnlyAuditRunner` 仍报告 `System.Collections.Immutable` 8.0/9.0 引用冲突，未影响编译或本项测试结果。

## F02 条件输出与全量输出旁路

### 实现

- 普通连接只传播声明的源端口值；兼容范围收敛为精确端口名和单输出图像算子的显式 `Image/Mask/Edges` 别名，不再合并源算子的整个输出字典。
- `OperatorOutputRule` 的当前模式可用性检查接入 `ValidateFlow`，因此正式执行准入会拒绝不可用输出。
- 常规与调试执行入口重复执行同一检查；即使旧调用绕过准入，也会在任何算子执行前以 `STRUCT_006` 失败。
- 条件分支自身的激活端口路由及 `ConditionResult/Condition/ActualValue` 显式诊断合同保持不变。

### 复审

- `Measurement.Angle -> ConditionalBranch.Value` 且 `MeasureType=PointToPoint` 时，准入、常规执行、调试执行均拒绝，两个执行器调用次数为 0。
- 直接调用输入准备并仅提供源输出 `Value/Distance` 时，连接声明的 `Angle` 缺失不会借用同名目标字段，也不会注入其他诊断键。
- 多端口、条件分支、空间上下文和图像引用生命周期相关既有测试保持通过。

### 验证

- `FlowExecutionServiceTests` + `FlowConnectionScenariosTests`：46/46 通过；当前源码重编译执行。
- `services-regression`：545 条中 540 通过，5 条失败均为并行工作区新增 `BlobDefectMeasurement/BlobDefectJudgment` 后，158→160 身份总数及生成目录/快照未同步；失败类仅为 `OperatorProductMetadataGovernanceTests` 和 `OperatorNamingSemanticContractTests`，与 F02 修改路径无关。
- 服务回归产物：`.tmp/test_results/classified/services-regression/services-regression-20260827-203644.trx`。

## F03 StringFormat 参数与端口输入隔离

### 实现

- 索引占位符固定映射为 `{0}=Arg1`、`{1}=Arg2`，不再依赖混合输入字典的枚举顺序。
- 命名占位符只解析正式端口 `{Arg1}`、`{Arg2}`；`Template/Mode/Separator` 等参数键和任意额外键不可见。
- Join 与显式空模板拼接同样只按 `Arg1/Arg2` 声明顺序读取。

### 复审

- 缺少 `Arg1` 时 `Arg2` 仍只替换 `{1}`，不会左移污染 `{0}`。
- 经 `PrepareOperatorInputs` 执行时，即使 `Template` 参数被注入混合字典，结果仍保留 `{Template}` 原文。

### 验证

- `StringFormatOperatorTests` + `FlowExecutionServiceTests`：38/38 通过；当前源码重编译执行。

## F04 StringFormat 正式属性与输出合同

### 实现

- 保留仓库已实现的 `Template/Join/Date` 三种产品模式，正式声明 `Mode` 枚举及 `Template/Separator/DateFormat` 属性。
- 为三个模式专属属性声明禁用、隐藏和忽略规则，避免编辑器、验证器和运行时对无关字段产生不同理解。
- 正式声明运行时已有的 `Length: Integer` 与 `IsEmpty: Boolean` 输出端口。

### 复审

- `OperatorFactory` 的有效元数据包含 4 项属性和 3 项输出；不是仅在源码上增加未被扫描的特性。
- Template、Join、Date 三种状态下分别只有对应专属属性处于启用状态。
- 未运行全量文档生成器：当前工作区另有用户新增的两个 Blob 算子，生成器即使使用 `--only` 仍会重写全量 160 算子目录；为避免混入无关派生改动，StringFormat 名片安排与 F05 合并精确更新。

### 验证

- `StringFormatOperatorTests` + `OperatorMetadataMigrationTests`：11/11 通过；当前源码重编译执行。

## F05 StringFormat 缺失模板默认值

### 实现

- 运行时 `Template` 缺失或为 null 时回退为元数据默认 `Result is {0} and {1}`。
- 显式空字符串不触发默认回退，继续按正式输入端口顺序直接拼接，保留用户配置语义。
- 精确更新 `docs/算子资料/算子名片/StringFormat.md`，合并记录 F03-F05 的属性、输出、条件和模板行为。

### 复审

- 通用参数读取 API 已具备缺失/null 与空串区分能力，本项没有引入新的反射或序列化分支。
- ProjectService 迁移会为旧工程物化新增元数据默认；直接实体和不完整 DTO 仍由运行时 fallback 防御。

### 验证

- `StringFormatOperatorTests` + `FlowExecutionServiceTests` + `ProjectServiceTests`：78/78 通过；当前源码重编译执行。

## F06 HttpRequest 显式请求体边界

### 实现

- 移除缺少 `Body` 时将混合输入字典整体序列化为 JSON 的兼容旁路。
- 仅当正式 `Body` 输入存在且非 null 时创建请求体；`Headers` 仍独立解析并发送。
- 通过真实 `FlowExecutionService` 执行链覆盖 GET、POST、PUT、DELETE，防止运行时注入的算子参数重新污染请求体。

### 复审

- 无 `Body` 值但存在 `Headers` 时，请求体为空且不发送 `Content-Type`，自定义 Header 保持可用。
- URL、Method、Timeout、Retry 和 ContentType 等注入参数不会成为隐式 JSON 字段。
- 显式 `Body` 与 `Headers` 的既有发送行为及响应兼容字段保持不变。

### 验证

- `HttpRequestOperatorTests`：13/13 通过；当前源码重编译执行。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。
- 非阻塞构建告警：`OperatorLibraryReadOnlyAuditRunner` 仍报告 `System.Collections.Immutable` 8.0/9.0 引用冲突，未影响编译或本项测试结果。

## F07 预览状态一致性

### 实现

- `operatorResultViewModel` 正式覆盖 `blocked` 与 `auth-error`，分别输出“安全拦截”和“登录状态无效”，不再落入“未运行”回退。
- 保持预览摘要区既有状态渲染，模块结果区复用规范化后的 `status/statusText/stateMessage`。
- 补充模型级断言，并对两种状态的完整面板容器执行冲突文案负向断言。

### 复审

- `blocked` 时结果区和模块结果区均表达安全拦截，且不出现“未运行”或“预览失败”。
- `auth-error` 时两区均表达登录状态无效和重新登录提示，且不出现“未运行”或“预览失败”。
- `loading/canceled/stale/error/success/disabled` 等既有正式状态保持原映射。

### 验证

- `preview-panel-memory.test.mjs`：50/50 通过。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。

## R01 端口恢复歧义

### 补证与处置

- 静态复核确认旧实现会在名称不匹配且存在多个类型兼容候选时选择集合中的首项，恢复结果受 DTO 端口顺序影响，因此风险成立。
- 精确端口 ID 仍保持最高优先级；ID 缺失或不兼容时，先接受唯一同名兼容候选，否则只接受唯一类型兼容候选。
- 两端 ID 均缺失时只接受唯一同名兼容端口对或唯一类型兼容端口对；多候选抛出包含算子、端口和候选列表的明确错误。

### 复审

- 既有 `DeepLearning.ObjectCount -> BoxFilter.Detections` 错配仍能唯一恢复为 `Objects -> Detections`。
- 两个 `DetectionList` 源输出同时兼容 `Detections` 时不再选择首项，端口顺序不能改变结果。
- 无兼容候选仍沿用未恢复路径；本项没有放宽端口类型兼容规则。

### 验证

- `FlowDataMappingTests` + 唯一恢复端点用例：8/8 通过；当前源码重编译执行。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。

## R02 PolarUnwrap 外半径缺失语义

### 补证与处置

- 历史追踪确认最初无元数据版本使用图像短边一半；随后公开元数据将 `OuterRadius` 固定为 100。
- 当前 `OperatorFactory` 新建、`ProjectService` 迁移和两份生成名片均以 100 为正式合同，历史工程加载时也会物化该默认值。
- 将手工构造或不完整实体的执行 fallback 从自适应半径改为 100，并将验证 fallback 从 1 改为 100，消除同一实体的三套语义。

### 复审

- 图像尺寸仍作为运行时上界；当短边一半小于 100 时，通用参数读取会把外半径限制到可执行范围。
- 显式保存的 `OuterRadius` 不受影响；本项仅改变字段缺失路径。
- 缺失 `OuterRadius` 且 `InnerRadius=20` 时验证通过，300x300 图像按 100 生成高度 80 的展开结果。

### 验证

- `PolarUnwrapOperatorTests`：5/5 通过；当前源码重编译执行。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。

## R03 PointSetTool 过滤边界缺失语义

### 补证与处置

- 历史追踪确认最初无元数据版本以 `double.MinValue/MaxValue` 作为缺失回退，后续正式属性改为 `±1e9`。
- 当前工厂、工程迁移和公开名片均把 `±1e9` 作为 Filter 的正式默认边界，不是仅用于显示的 Min/Max 限制。
- 将四个运行时缺失 fallback 统一为 `FilterMinX/FilterMinY=-1e9`、`FilterMaxX/FilterMaxY=1e9`。

### 复审

- 变更只影响 `Operation=Filter` 且边界字段缺失的不完整实体；显式边界值及其他 Operation 不变。
- 缺失边界时，坐标位于范围内的点保留，超过正向或负向边界的点均被排除。
- 未扩展到边界顺序校验或其他点集算法重构。

### 验证

- `PointSetToolOperatorTests`：4/4 通过；当前源码重编译执行。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。

## R04 预览 Artifact 用户隔离

### 补证与处置

- LAN 模式允许多个认证用户访问同一 Desktop 实例，因此不可把不可猜测的 Artifact ID 作为唯一授权凭据；本批次选择按用户隔离，不采用“持有即授权”的能力令牌策略。
- `PreviewArtifactOwnerScope` 增加认证用户 ID；节点预览和标定草稿两个物化入口均从 `ClaimTypes.NameIdentifier` 写入 owner。
- Artifact GET/DELETE 从当前认证声明取得用户 ID，并由存储层执行恒等匹配；缺失身份、跨用户访问、无效 ID 和不存在 ID 均返回 404，避免泄露资源存在性。

### 复审

- 用户 B 持有用户 A 的有效 Artifact ID 时，读取和删除均失败；拒绝删除后用户 A 仍可读取，证明未发生跨用户状态修改。
- owner 索引包含用户 ID，同一 project/node/session/sequence/revision 下不同用户的批次不会互相替换或撤销。
- 校验位于 `PreviewArtifactStore`，不是仅依赖端点调用约定；所有公开读取和删除调用均使用新签名，未保留不校验 owner 的兼容重载。

### 验证

- `PreviewArtifactStoreTests` + `CalibrationDraftEndpointsTests` + 节点预览 Artifact 模式端点用例：19/19 通过；当前源码重编译执行。
- `git diff --check`：通过；仅有仓库既有 LF/CRLF 提示。

## 最终回归与发布建议

### 计划签收

- F01-F07 与 R01-R04 共 11 项均已完成实现、逐项复审、当前源码重编译和定向验证；TODO 不再存在开放修复项。
- `services-regression-20260827-214818.trx`：541/546 通过。5 条失败仅属于 `OperatorProductMetadataGovernanceTests` 和 `OperatorNamingSemanticContractTests`，共同根因是工作区另行新增 `BlobDefectMeasurement/BlobDefectJudgment` 后运行时身份由 158 增至 160，而生成目录、知识图谱和身份快照尚未同步。
- `desktop-endpoints-20260827-214938.trx`：335/335 通过。
- `canvas-core.test.mjs` + `preview-panel-memory.test.mjs`：82/82 通过；UI 全套 990/991 通过，唯一失败是测试依赖的 `docs/进行中/当前计划/VisionAgent_RuntimePreview_Pilot_Gate.md` 在工作区不存在。
- `git diff --check` 通过；仅输出仓库既有 LF/CRLF 转换提示。

### 发布建议

- 本算子审计修复批次可以按其自身范围签收，11 个发现项未留下开放实现或定向回归缺口。
- 不建议把当前工作区标记为“全仓发布门禁通过”：应先由两个 Blob 算子的所有者同步 160 算子派生目录/快照，并由 RuntimePreview 计划所有者恢复或调整缺失文档合同，然后重新执行对应全量门禁。
- 原审计报告继续保留 2026-08-25 的发现事实；本文件和 TODO 作为 2026-08-27 的处置与验证证据，不回写历史结论。

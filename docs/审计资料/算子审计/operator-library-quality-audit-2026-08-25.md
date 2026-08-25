# ClearVision 算子库质量审计报告

- 审计日期：2026-08-25
- 审计模式：只读审计，不修改产品源码
- 静态审计基线：`e76c74e392bb14ffe02ef9ea9c7a614cb8987f04`
- 当前 HEAD：`ee00cc7aa83ef2b2548e31ffafaff141e2606c5f`
- 逐算子矩阵：[`operator-library-quality-audit-matrix-2026-08-25.csv`](./operator-library-quality-audit-matrix-2026-08-25.csv)
- 原始机器可读静态证据：[`operator-library-quality-audit-static-evidence-2026-08-25.json`](./operator-library-quality-audit-static-evidence-2026-08-25.json)
- 静态审计摘要：[`operator-library-quality-audit-static-summary-2026-08-25.json`](./operator-library-quality-audit-static-summary-2026-08-25.json)
- 最终人工审计记录：[`operator-library-quality-audit-record-2026-08-25.json`](./operator-library-quality-audit-record-2026-08-25.json)

## 1. 总体结论

本轮建立了 158 个规范算子的完整清单，并逐项核查了声明端口、声明属性、运行时读写信号和预览策略；另核对了 4 个旧类型别名。矩阵有 158 行、158 个唯一算子，与 `canonicalOperators` 逐名比对后无缺失、无多余、无重复。

最终确认 7 项缺陷：阻断 0 项、严重 5 项、一般 2 项。另有 4 项证据不足以定性为缺陷的“待确认风险”和 1 项建议。最应优先处理的是：端口迁移破坏连接 ID（F01）、条件输出被全量输出旁路（F02），以及参数与端口输入混入同一字典后造成的 `StringFormat`/`HttpRequest` 行为错误（F03、F06）。这些问题都位于共享链路，影响面高于单个算子元数据字段错误。

| 结论类型 | 数量 | 说明 |
| --- | ---: | --- |
| 阻断 | 0 | 未发现必然导致整个算子库不可启动或所有流程不可运行的问题 |
| 严重 | 5 | F01、F02、F03、F05、F06 |
| 一般 | 2 | F04、F07 |
| 待确认风险 | 4 | R01 至 R04；缺少产品意图、历史工程或多用户威胁模型证据 |
| 建议 | 1 | S01；当前为未被生产调用的 DTO 映射技术债 |

本报告中的“确认缺陷”是人工跨层追踪后的结论。原始 JSON 中的 `confirmedCount=2` 仅表示自动静态审计的两个既有身份差异基线，不代表本轮最终缺陷总数。

## 2. 审计范围与方法

审计按以下链路交叉核对，而不是只读取算子特性声明：

1. 由 `OperatorMetadataScanner.Scan`、`OperatorFactory.GetAllMetadata`、基础设施执行器源码、AI 合同目录和 `OperatorModuleCatalog` 建立身份集合。
2. 对每个算子提取输入端口、输出端口、属性、输出类型及静态运行时信号，形成 158 行矩阵。
3. 逐项复核端口声明与运行时输入读取、输出字典、默认值和验证逻辑；低置信扫描结果保留在矩阵中，但不直接作为缺陷。
4. 横向检查项目序列化/迁移、连接恢复、执行输入准备、正式准入、属性编辑模型、预览状态模型、通用预览和副作用预览兜底。
5. 对重要结论使用已有测试和最小代码路径复现进行确认；未能证明产品意图或生产可达性的项降级为“待确认风险”。

审计基线与当前 HEAD 之间，`ClearVision.Product/src`、`ClearVision.OperatorLibrary/src` 和相关测试目录的 `git diff --name-only` 为空；两次提交之间只有归档文档变化。因此静态产物仍适用于当前算子、执行、迁移和预览源码。

### 审计边界

- 本轮没有对 158 个算子逐一连接真实相机、PLC、数据库、HTTP 服务或 AI 模型；硬件、外部服务和数据依赖行为仍有残余风险。
- 没有发现算子执行器在运行时调用 `AddInputPort`/`AddOutputPort` 或按属性增删正式端口。当前所谓动态端口行为主要是固定端口的条件可用性，F02 即来自该链路。
- 当前仓库内未发现额外插件注册或动态生成算子身份。部署时从仓库外加载的第三方插件不在本次可审计范围内。
- `ClearVision.OperatorLibrary` 已打包程序集没有可读取的正式身份索引，不能作为独立枚举面计数；其源码模块目录已核查。

## 3. 算子覆盖清单

### 3.1 注册面一致性

| 注册/暴露面 | 数量 | 结论 | 关键证据 |
| --- | ---: | --- | --- |
| 元数据扫描器 | 158 | 完整 | `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorMetadataScanner.cs:32-35,53-84` |
| 工厂规范目录 | 158 | 与扫描器一致 | `ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/OperatorFactory.cs:71-93` |
| 基础设施执行器源码 | 158 | 与规范身份一致 | 原始 JSON `infrastructureSource` surface |
| AI 算子合同 | 158 | 由工厂元数据生成，无身份差异 | `ClearVision.Product/src/ClearVision.Product.Infrastructure/AI/Tools/VisionAgentOperatorContractCatalog.cs:35-46` |
| 包公开模块目录 | 157 | 有意排除 1 个内部算子 | `ClearVision.OperatorLibrary/src/ClearVision.OperatorLibrary.Modules/OperatorModuleCatalog.cs:24-57` |
| 桌面算子库 API | 158 | 直接使用工厂元数据 | `ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/ApiEndpoints.cs:1025-1050` |

模块目录唯一少于规范集合的算子是 `FrameChangeTrigger`。它被明确列入 `InternalOnlyTypes`，属于有意的包边界，不记为缺陷，也不算审计遗漏。

旧类型别名共 4 个，均归一到规范类型：

| 旧别名 | 规范类型 |
| --- | --- |
| `GaussianBlur` | `Filtering` |
| `ModbusRtuCommunication` | `ModbusCommunication` |
| `OnnxInference` | `DeepLearning` |
| `Preprocessing` | `Filtering` |

### 3.2 分类覆盖

| 分类 | 算子数 |
| --- | ---: |
| 采集 | 1 |
| 图像预处理 | 28 |
| 分割与区域 | 17 |
| 特征提取 | 13 |
| 匹配与定位 | 17 |
| 缺陷检测 | 4 |
| 测量 | 17 |
| 标定与坐标 | 12 |
| AI 推理 | 4 |
| 3D 点云 | 6 |
| 数据处理 | 18 |
| 流程控制 | 8 |
| 通信 | 8 |
| 输出与辅助 | 5 |
| **合计** | **158** |

已核查数 158，遗漏数 0。包程序集独立身份索引和仓库外插件属于“不可独立验证面”，不是已知遗漏算子。

## 4. 逐算子审计矩阵

完整矩阵位于 [`operator-library-quality-audit-matrix-2026-08-25.csv`](./operator-library-quality-audit-matrix-2026-08-25.csv)。矩阵恰好包含 158 条数据行，名称集合与原始 JSON 的 `canonicalOperators` 完全相同。

字段含义：

| 字段 | 含义 |
| --- | --- |
| `declared_inputs` / `declared_outputs` | 正式元数据声明的端口数量 |
| `declared_properties` | 正式元数据声明的可配置属性数量 |
| `output_types` | 正式输出端口类型集合，保留顺序 |
| `U` | `RUNTIME_OUTPUT_UNDOCUMENTED` 静态信号数 |
| `D` | `RUNTIME_OUTPUT_DYNAMIC_UNPROVEN` 静态信号数 |
| `P` | `GET_PARAM_DEFAULT_MISMATCH` 静态信号数 |
| `N` | `OUTPUT_KEY_NO_SUCCESS_PATH` 静态信号数 |
| `S` | 注册/身份 surface 差信号数 |
| `manual_review` | 人工复核结论或关联问题编号；`NR` 表示未确认算子特有缺陷 |
| `preview_review` | `PF` 为通用类型预览/状态/兜底链；`PS` 为副作用算子的拦截/摘要预览链 |

预览策略分布为 `PF=147`、`PS=11`。F01 和 F07 是跨算子公共链路问题，不在 158 行中重复标记；R01 和 R04 同理。

自动审计原始产物包含 778 条候选信号；逐算子矩阵另保留 1 条已接受的 surface 身份差信号。文档目录的排序身份差异只存在于原始 JSON，不对应某个算子行。

| 信号 | 数量 | 解释 |
| --- | ---: | --- |
| `RUNTIME_OUTPUT_UNDOCUMENTED` | 648 | 多数是诊断字段，或分析器无法穿透共享输出 helper；不能直接等同未声明业务端口 |
| `RUNTIME_OUTPUT_DYNAMIC_UNPROVEN` | 98 | 分析器无法静态证明动态字典输出路径，不等同真正的动态端口 |
| `GET_PARAM_DEFAULT_MISMATCH` | 19 | 已逐项复核；包含有意 fail-closed、已修复、假阳性、待确认和 F05 |
| `OUTPUT_KEY_NO_SUCCESS_PATH` | 13 | 多数来自封装/失败分支分析局限，保留作回归线索 |
| `SURFACE_IDENTITY_MISMATCH` | 1 | `FrameChangeTrigger` 的内部目录排除，属设计行为 |

### 4.1 跨层审计结果摘要

| 审计面 | 结果 |
| --- | --- |
| 身份与注册 | 158 个规范身份在 scanner、factory、executor/source 和 AI contract 一致；包目录的 157 是有意边界 |
| 输入/输出端口 | 正式端口均为固定声明；确认 F01、F02，保留 R01；未发现其他可证明的方向、数量或必填状态缺陷 |
| 属性与编辑合同 | 确认 `StringFormat` 的 F04、F05，保留 R02、R03；其余属性未发现可证明的无效配置项，但真实硬件/模型属性未做全环境执行 |
| 序列化与恢复 | 通用保存格式能保留端口 ID，但旧端口重建链存在 F01；多候选语义恢复存在 R01 |
| 运行时输入/输出 | 确认全量输出旁路 F02、参数污染 F03 和隐式 HTTP body F06；诊断字典键不批量视为业务输出端口 |
| 预览工作台 | 147 个算子走类型化公共预览/安全兜底，11 个副作用算子走拦截/摘要链；确认状态矛盾 F07，Artifact 多用户隔离保留 R04 |

公共预览模型已覆盖未选择、禁用、空结果、运行中、取消、过期、失败和成功状态；副作用链另覆盖安全拦截和鉴权失败。未知或无专用展示的数据仍有结构化摘要/原始诊断兜底。大对象由 Artifact store 的 TTL、单项/总量和条目数上限约束。该结论来自共享链路和相关测试，不等同于 158 个算子都完成了真实数据的视觉验收。

## 5. 已确认问题

### F01 严重：旧端口迁移重建 GUID，但不原子更新连接

**证据**

- 项目加载后无条件调用迁移：`ClearVision.Product/src/ClearVision.Product.Application/Services/ProjectService.cs:146-156`。
- `MigrateFlowDto` 规范化每个算子的输入/输出端口，但除 `PixelStatistics` 特例外没有迁移通用连接：同文件 `849-883`。
- `NormalizePorts` 在空端口或旧占位端口条件下清空列表，并为每个新端口生成 `Guid.NewGuid()`：同文件 `1168-1195`。
- DTO 转实体时直接使用连接内原 ID：`ClearVision.Product/src/ClearVision.Product.Application/DTOs/OperatorFlowDto.cs:104-113`。
- 前端按 ID 查找失败时保留 `conn.sourcePort ?? 0` / `conn.targetPort ?? 0`，即静默退到索引 0：`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/core/canvas/flowCanvas.js:2197-2215`。
- 再次保存会把索引 0 对应的新端口 ID 持久化：同文件 `2057-2093`。
- 后端实体路径遇到不存在的端口 ID 会抛出“端口不存在”：`ClearVision.Product/src/ClearVision.Product.Core/Entities/OperatorFlow.cs:201-208`。

**触发/复现**

1. 准备一个旧工程：算子端口为空，或端口数量等于当前元数据且名称为 `input/output/in/out` 或 ID 为空；连接仍保存旧端口 ID。
2. 加载项目触发 `MigrateFlowDto`。
3. 观察端口被重建为新 GUID，而连接仍保留旧 GUID。
4. 走 DTO 转实体路径会直接失败；走画布路径时，多端口算子的连接会静默落到第 0 个端口，再保存后错误关系被永久写回。

**影响**

影响所有满足迁移条件的算子。单输入/单输出算子可能表面正常，多输入或多输出算子会出现连线错位、丢失、错误恢复或后端拒绝加载，且画布路径可能把错误悄悄持久化。

**建议修复**

在一次迁移事务中先建立旧端口到新端口的语义映射（方向、规范名、类型、序号），再同时重写所有连接 ID，最后替换端口列表。无法唯一映射时应返回明确迁移错误或隔离该连接，不能默认第 0 个端口。前端也应在提供了端口 ID 但找不到时拒绝恢复并显示诊断，而不是退到索引 0。

### F02 严重：条件输出不可用时，全量输出旁路可把其他同名字段送入下游

**证据**

- `Measurement.Angle` 只在 `LineToLine` 或 `ThreePointAngle` 模式可用：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/MeasureDistanceOperator.cs:27-35`。
- `PointToPoint` 路径生成 `Distance/DeltaX/DeltaY`，不生成 `Angle`，但公共结果始终包含 `Value`：同文件 `203-211,370-384`。
- 执行输入准备先尝试按连接源端口精确取值，随后仍把源算子的所有其他输出键合并到目标输入：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs:2214-2248`。
- `ConditionalBranch` 只需名为 `Value` 的输入：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/ConditionalBranchOperator.cs:55-63`。
- `FlowLinter` 已能以 `STRUCT_006` 报告当前模式不可用的输出：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowLinter.cs:211-250`。
- 正式准入最终使用的 `FlowExecutionService.ValidateFlow` 仅验证算子参数和输入/输出算子存在性，没有执行 `STRUCT_006`：`ClearVision.Product/src/ClearVision.Product.Core/Services/ExecutionAdmissionService.cs:203-224`、`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/GovernedFlowExecutionService.cs:89-95`、`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs:1607-1651`。

**触发/复现**

建立 `Measurement(MeasureType=PointToPoint).Angle -> ConditionalBranch.Value`。当前模式不产生 `Angle`，精确映射失败；全量合并却把测量公共结果的 `Value` 注入 `ConditionalBranch`，下游会对距离值进行判断，而不是因缺少角度而失败。

**影响**

任何带条件输出且结果字典还包含通用/诊断键的算子都可能发生语义串线。在检测决策链中表现为静默走错分支，比显式失败更危险。

**建议修复**

执行引擎应按“连接的源端口”传播单一值；历史兼容只能使用该端口的显式别名集合，不能对每条连接合并整个源输出字典。同时把输出可用性检查接入正式准入和执行前验证，执行时仍需 fail-closed 防御旧工程或绕过准入的调用。

### F03 严重：`StringFormat` 的索引占位符被算子参数污染

**证据**

- `PrepareOperatorInputs` 先把所有算子参数写入输入字典，再加入连线输入：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs:2128-2147`。
- `StringFormat.FormatTemplate` 按 `inputs.Values` 的枚举顺序处理 `{0}`、`{1}`：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/StringFormatOperator.cs:94-125`。
- `Template` 本身是算子参数，因此在规范工厂创建的流程里通常成为第一个索引值：同文件 `34-37,55-64`。

**触发/复现**

使用默认模板 `Result is {0} and {1}`，将 `Arg1=A`、`Arg2=B` 接入正式流程。输入准备先插入 `Template`，随后才插入 `Arg1/Arg2`。典型规范顺序下 `{0}` 被模板自身替换，结果类似 `Result is Result is {0} and A and A`，而不是 `Result is A and B`。

**影响**

正式流程中的字符串报告、文件名、日志和外部报文可稳定地产生错误内容；旧工程多出的参数还会进一步改变索引语义。

**建议修复**

从执行模型上分离“属性参数”和“端口输入”。`StringFormat` 的索引参数应严格按声明端口顺序读取 `Arg1`、`Arg2`，命名占位符也只面向端口输入；不要枚举混合字典。补充经 `PrepareOperatorInputs` 的端到端测试，不能只直接调用算子。

### F04 一般：`StringFormat` 的正式属性和输出合同不完整

**证据**

- 元数据只声明 `Arg1/Arg2 -> Result` 和属性 `Template`：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/StringFormatOperator.cs:28-38`。
- 运行时还读取未声明属性 `Separator`、`Mode`、`DateFormat`：同文件 `55-73,130-135`。
- 运行时返回未声明输出 `Length`、`IsEmpty`：同文件 `82-87`。

**触发/复现**

在属性面板中创建 `StringFormat`：无法通过正式元数据选择 `Join/Date` 模式，也无法配置分隔符和日期格式；执行后虽存在 `Length/IsEmpty`，但它们没有正式端口，不能可靠连线或按合同预览。直接 DTO 或旧数据可写入这些属性，造成界面能力与运行时能力不一致。

**影响**

功能呈现不完整，序列化、AI 合同、属性编辑器、连线校验和运行时输出对同一算子的理解不一致。

**建议修复**

先确定产品合同：若保留三种模式，应声明 `Mode` 枚举、`Separator`、`DateFormat` 及正确的显隐/启用规则，并声明 `Length/IsEmpty` 输出；若不对外支持，应删除相应运行时分支或把附加值放入明确的诊断区，而非公共输出字典。

### F05 严重：`StringFormat.Template` 缺失时的运行时默认值与元数据不一致

**证据**

- 元数据默认值为 `Result is {0} and {1}`：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/StringFormatOperator.cs:37`。
- 运行时缺失回退为空串；空模板会直接拼接全部输入值：同文件 `55,94-99`。
- 原始审计 `OAR-012` 已确认该分支可由历史工程或直接 DTO 进入，并标记为 `open-production-defect`。

**触发/复现**

加载或构造一个缺少 `Template` 参数的 `StringFormat`，接入 `Arg1=A`、`Arg2=B`。运行时输出 `AB`，而按正式默认合同应输出 `Result is A and B`。

**影响**

旧版本数据和不完整反序列化数据在没有任何校验错误的情况下改变业务输出。即使修复 F03，缺失属性路径仍会产生不同语义。

**建议修复**

让执行和验证的缺失回退与元数据默认值一致，或在统一反序列化阶段物化正式默认值。增加“缺失值”和“显式空串”两类测试，避免把用户明确配置的空模板误当成缺失值。

### F06 严重：`HttpRequest` 无 `Body` 连线时仍会发送整个配置字典

**证据**

- `HttpRequest` 正式声明可选 `Body/Headers` 输入和 6 个属性：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/HttpRequestOperator.cs:31-47`。
- 正式执行前，6 个属性会被注入 `inputs`：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Services/FlowExecutionService.cs:2137-2143`。
- 未找到 `Body` 但 `inputs.Count > 0` 时，算子把整个输入字典序列化为 JSON：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/HttpRequestOperator.cs:76-88`。
- 默认 `Content-Type` 随后会把该字符串装入 `HttpRequestMessage.Content`；该逻辑不区分 GET/POST/PUT/DELETE：同文件 `91-105,177-205`。

**触发/复现**

创建默认 `HttpRequest`，不连接 `Body`，请求一个能回显请求体的本地端点。请求体会包含 `Url/Method/TimeoutMs/RetryCount/ContentType/RetryDelayMs` 等配置；GET 也可能携带该非预期请求体。

**影响**

可能向外部系统泄露配置、触发 API 合同拒绝、污染签名/幂等语义，或让本应无 body 的 GET/DELETE 在代理和服务端出现兼容性问题。

**建议修复**

只有显式 `Body` 端口有值时才设置请求体；`Headers` 也只读取对应端口。不要把混合输入字典作为隐式 body。建议同时处理 F03 的根因，将参数与端口输入分离，并为 `HttpClient` 注入可测试 handler，覆盖各 HTTP method 的 body 规则。

### F07 一般：预览工作台在 `blocked/auth-error` 时同屏显示矛盾状态

**证据**

- `operatorResultViewModel.getStatusInfo` 只处理 `loading/canceled/stale/error/success`，其他状态回退为“未运行”：`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/operatorResultViewModel.mjs:1162-1238`。
- 预览面板上层状态正确识别 `blocked` 和 `auth-error`：`ClearVision.Product/src/ClearVision.Product.Desktop/wwwroot/src/features/flow-editor/previewPanelCapabilityOwner.mjs:249-263`，并有相应结果区：同文件 `1661-1677`。
- “模块结果”仍直接显示旧模型的 `statusText/stateMessage`：同文件 `1733-1747`。
- 现有 blocked 测试只断言出现“安全拦截”且不出现“预览失败”，没有断言页面中不存在“未运行”：`ClearVision.Product/tests/ClearVision.Product.UI.Tests/tests/unit/preview-panel-memory.test.mjs:1651-1675`。

**触发/复现**

预览 `TcpCommunication` 等副作用算子使状态为 `blocked`，或让请求进入 `auth-error`。上方结果区显示“安全拦截/登录状态无效”，同屏“模块结果”却显示“未运行”。

**影响**

不会改变执行结果，但会误导用户判断算子是否执行、是否被安全策略拦截，增加重复操作和错误诊断概率。

**建议修复**

让 `operatorResultViewModel` 覆盖全部正式预览状态，或让所有区域共享同一个规范化 `statusInfo`。测试应对整个容器做负向断言：`blocked/auth-error` 时不得出现“未运行”或其他冲突状态。

## 6. 待确认风险

### R01：端口 ID 丢失时按“首个兼容端口”恢复，可能恢复到错误语义

`FlowEntityMapper.TryResolveConnectionPorts` 在首选端口不存在或不兼容时，会按名称/类型寻找，最终遍历并选择第一个兼容组合：`ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/FlowEntityMapper.cs:499-571`。现有测试明确接受把错误的 `DeepLearning.ObjectCount` 修复为 `Objects`：`ClearVision.Product/tests/ClearVision.Product.Desktop.Tests/PreviewNodeEndpointsTests.cs:3510-3593`。

当一个算子有多个同类型输出且端口 ID 已丢失时，“第一个兼容”无法证明语义正确。尚无历史工程证明已经发生误连，也缺少产品规定的恢复优先级，因此不定性为缺陷。建议只在兼容候选唯一时自动恢复；多候选时要求名称/别名映射，无法唯一判定则报告歧义并拒绝静默修复。

### R02：`PolarUnwrap.OuterRadius` 存在三套缺失值语义

- 元数据默认 100：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PolarUnwrapOperator.cs:29`。
- 执行缺失时使用图像短边的一半：同文件 `58-60`。
- 验证缺失时使用 1：同文件 `110-120`。

历史工程缺失该字段时行为可达，但无法从仓库判断“自适应半径”是否是有意的旧合同。需要产品确认默认应为固定 100 还是随图像自适应，并用历史工程样本验证后再修改。

### R03：`PointSetTool` 过滤边界的元数据与缺失回退不一致

元数据将四个过滤边界设为 `±1e9`，运行时缺失则使用 `±double.MaxValue`：`ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators/PointSetToolOperator.cs:28-33,57-68`。差异只在 `Operation=Filter`、属性缺失且坐标超出 `±1e9` 时可见。尚无生产点集证明该范围有业务意义，故保留风险。需要明确编辑器的 `±1e9` 是真实限制还是 UI 近似无穷；随后统一元数据、验证和执行语义。

### R04：预览 Artifact 端点未按用户/owner 校验读取和删除

GET/DELETE 端点只凭 `artifactId` 调用 `TryRead/Delete`：`ClearVision.Product/src/ClearVision.Product.Desktop/Endpoints/PreviewArtifactEndpoints.cs:12-46`。存储内部有 project/node/session owner 索引，但读取和删除接口不接收 owner：`ClearVision.Product/src/ClearVision.Product.Desktop/PreviewArtifacts/PreviewArtifactStore.cs:64-128`；owner 结构也没有用户 ID：`ClearVision.Product/src/ClearVision.Product.Desktop/PreviewArtifacts/PreviewArtifactContracts.cs:27-32`。

降低风险的证据是：端点位于全局 `AuthMiddleware` 之后（`ClearVision.Product/src/ClearVision.Product.Desktop/Program.cs:326-350`），默认只监听 localhost（同文件 `314-323`），artifact ID 由 32 字节密码学随机数生成（`PreviewArtifactStore.cs:388-395`）。但 LAN 模式允许多用户访问。需要产品确认这些 URL 是否被设计成“持有即授权”的能力令牌；若要求用户隔离，应将用户/会话写入 owner 并在 GET/DELETE 校验，而不是只依赖不可猜测 ID。

## 7. 建议项

### S01：清理或补全未被生产消费的 `OperatorService.MapEntityToDto`

`MapEntityToDto` 只映射基础字段和属性，不映射输入/输出端口：`ClearVision.Product/src/ClearVision.Product.Application/Services/OperatorService.cs:362-378`。源码引用检索显示 `IOperatorService` 只有 DI 注册（`ClearVision.Product/src/ClearVision.Product.Infrastructure/DependencyInjection/VisionRuntimeServiceCollectionExtensions.cs:298`）和测试，没有生产调用方；实际算子库端点直接使用 `IOperatorFactory`，因此当前不定性为用户可达缺陷。建议删除未使用服务，或在启用前补全端口映射并增加合同测试，避免未来接入后返回残缺 DTO。

## 8. 已接受差异与非缺陷信号

以下项目已有明确证据，不应为了“清零告警”而修改：

- `FrameChangeTrigger` 从包公开模块目录排除是明确的内部边界。
- `HttpRequest.Url`、`ImageSave.Directory`、部分 PLC 地址/端口在字段缺失时 fail-closed，与正常创建设置默认值不同，属于副作用安全策略。
- `MqttPublish` 当前是 placeholder-disabled；其缺失 Broker/Topic 校验不代表可用发布路径缺陷。
- `WidthMeasurement.SampleCount` 的 0 是内部哨兵，最终缺失行为仍回到正式默认 24，属于静态分析假阳性。
- `ForEach.FailFast`、`ImageSave.FileNameTemplate`、`PyramidShapeMatch.AngleRange`、`VariableIncrement.ResetThreshold`、`VariableWrite.StaticValue` 的既有默认值缺陷已在基线中修复并有测试，不作为本轮开放问题。
- 648 条“未声明运行时输出”多数是诊断数据而非可连线业务端口。是否将诊断字段提升为正式输出应由明确产品需求决定，不能批量声明。

## 9. 自动化验证与边界

| 测试层 | 通过 | 失败 | 说明 |
| --- | ---: | ---: | --- |
| Product .NET 相关测试 | 190 | 0 | 元数据、属性默认、执行/连接及相关合同回归 |
| Desktop .NET 相关测试 | 80 | 0 | 端点、预览、连接恢复和 Artifact 相关回归 |
| UI Node 测试 | 214 | 0 | 属性/预览状态和工作台相关回归 |
| **合计** | **484** | **0** | 仅代表现有测试覆盖到的行为 |

重要限制：常规 `dotnet test` 还原因 NuGet 源 TLS/凭据错误 `NU1301` 无法完成。本轮 .NET 验证改用时间与相关源码基线一致的现有 Debug 程序集，通过 `dotnet vstest` 执行；这不是当前源码的干净重编译证明。UI 测试直接运行当前 JavaScript 测试。相关源码从基线到当前 HEAD 无 diff，降低了二进制漂移风险，但不能替代恢复依赖后进行一次 clean build/test。

## 10. 当前缺失或不足的自动化测试

1. 通用端口迁移后，所有连接 ID 必须保持或被原子改写；覆盖多输入、多输出、重排、旧占位名和空 GUID。
2. 前端反序列化遇到已提供但不存在的端口 ID 时必须显式失败，禁止默认索引 0；另测再次保存不得把误连持久化。
3. 正式准入和执行链覆盖 `Measurement(PointToPoint).Angle -> ConditionalBranch.Value`，既要在准入报 `STRUCT_006`，也要验证绕过准入时 fail-closed。
4. `StringFormat` 必须经真实 `PrepareOperatorInputs` 测试默认模板、命名占位符、索引占位符、缺失 Template 和显式空 Template。
5. `HttpRequest` 使用可控本地 handler 验证：无 Body 连线时请求体为空；GET/DELETE 不携带隐式 body；只有显式 Body 被发送。
6. `blocked` 和 `auth-error` 预览状态下，整个面板不得同时出现“未运行”；覆盖模块结果区而不只上层提示区。
7. 同类型多输出在端口 ID 丢失时的恢复歧义测试；候选不唯一应拒绝静默恢复。
8. Artifact 多用户策略测试：若产品要求 owner 隔离，验证用户 A 不能读取或删除用户 B 的 artifact。
9. 158 个算子的声明属性与运行时读取属性、声明输出与可连线业务输出的生成式合同测试，避免未来再依赖低置信静态字符串分析。
10. 恢复 NuGet 后执行当前 HEAD 的 clean build，并在隔离环境补充相机、PLC、HTTP、数据库、文件系统和模型算子的真实依赖测试。

## 11. 修复优先级

1. **F01**：先阻止工程加载/保存过程中形成永久误连；同步收紧前端未知端口 ID 行为。
2. **F02**：移除每条连接上的全量输出旁路，并把条件输出可用性接入正式准入。
3. **F03 + F06**：在执行输入模型中分离属性与端口值；分别修正字符串索引和 HTTP body。
4. **F05 + F04**：统一 `StringFormat` 默认值并确定完整正式合同。
5. **F07**：统一预览状态模型，消除安全拦截/鉴权错误下的矛盾文案。
6. **R01**：在继续扩展端口迁移前，先定义多候选端口恢复策略。

结论：算子身份集合和主要注册面总体一致，158 个规范算子没有清单遗漏；真正的高风险集中在跨层基础设施，而不是大量单算子声明错误。当前不应依据 778 条静态信号批量扩充端口或属性。优先修复 F01、F02 和混合输入模型，能同时降低连线错误、静默误判、错误字符串和意外网络请求四类生产风险。

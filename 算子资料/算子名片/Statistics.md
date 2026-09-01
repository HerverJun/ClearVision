# 统计分析 / Statistics

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `StatisticsOperator` |
| 枚举值 (Enum) | `OperatorType.Statistics` |
| 暴露分类 (Exposure) | `package-public` |
| 暴露原因 (Exposure Reason) | Supported package-public operator. |
| 分类 ID (CategoryId) | `DataProcessing` |
| 分类 (Category) | 数据处理 |
| 分类顺序 (CategoryOrder) | 11 |
| 版本 (Version) | `1.0.1` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| Execution | `Implemented` |
| AlgorithmQuality | `Unknown` |
| ProductionReadiness | `Unknown` |
| FieldValidation | `NotValidated` |
| Quality Evidence Refs |  |
| 标签 (Tags) | `AlgorithmQuality:Unknown`, `Execution:Implemented`, `FieldValidation:NotValidated`, `ProductionReadiness:Unknown`, `分类:DataProcessing`, `分类显示:数据处理`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于基于滚动历史计算均值、标准差和 Cpk 统计结果。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Value`；缺失时通常返回失败结果。
- 参数解析覆盖 5 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Math.Min`
- `Math.Round`
- `Math.Sqrt`
- `Math.Max`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `USL` | 规格上限 | `double` | "" | - | Yes | 可选项。当同时提供 USL 和 LSL 时才会计算 Cpk。 |
| `LSL` | 规格下限 | `double` | "" | - | Yes | 可选项。当同时提供 USL 和 LSL 时才会计算 Cpk。 |
| `WindowSize` | 窗口大小 | `int` | 1000 | [2, 50000] | Yes | - |
| `StateTtlMinutes` | 状态保留时间（分钟） | `int` | 120 | [1, 10080] | Yes | - |
| `Reset` | 重置历史 | `bool` | false | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | Input Value | `Float` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Mean` | 均值 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `StdDev` | StdDev | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Count` | Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Min` | Min | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Max` | Max | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `Cpk` | Cpk | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `IsCapable` | Is Capable | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`710F28CB4814E73B1059B1212C77D40EC4F1581D3EFCE546EAD06B260EEE6293`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `CPL` | `Any` | 源码通过输出字典索引赋值写入。 |
| `CPU` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Cp` | `Any` | 源码通过输出字典索引赋值写入。 |
| `Range` | `Any` | 源码输出字典初始化中可见字段。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | 通常随输入集合、字符串长度或字段数量线性增长。 |
| 典型耗时 (Typical Latency) | 未固定；一般由输入数据规模和运行时调度开销决定。 |
| 内存特征 (Memory Profile) | 主要由输出字典、集合和少量中间对象决定。 |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 1 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入数据结构稳定、下游明确消费当前输出字段的常规流程节点。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
3. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
4. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-08-31 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |

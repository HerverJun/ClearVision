# 触发模块 / TriggerModule

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TriggerModuleOperator` |
| 枚举值 (Enum) | `OperatorType.TriggerModule` |
| 分类 ID (CategoryId) | `FlowControl` |
| 分类 (Category) | 流程控制 |
| 分类顺序 (CategoryOrder) | 12 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:FlowControl`, `分类显示:流程控制`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于生成软件、定时或外部触发信号。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
该类算子主要对上游值、集合或流程状态做判断、转换、聚合或路由，不直接改写图像像素。

## 实现策略 / Implementation Strategy
- 输入端口均为可选或该算子不依赖外部输入，执行时会优先读取可用输入并使用参数默认值兜底。
- 可选输入用于覆盖或补充参数配置：`Signal`。
- 参数解析覆盖 3 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Math.Abs`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `TriggerMode` | 触发模式 | `enum` | Software | Software/软件触发；Timer/定时触发；ExternalSignal/外部信号 | Yes | - |
| `Interval` | 间隔（ms） | `int` | 1000 | [1, 3600000] | Yes | - |
| `AutoRepeat` | 自动重复 | `bool` | true | - | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Signal` | Signal | `Boolean` | No | 可选输入；提供时会参与当前算子处理或覆盖部分参数配置。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Triggered` | Triggered | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `Timestamp` | Timestamp | `String` | 文本结果，可用于显示、日志、保存或外部接口传输。 |
| `TriggerCount` | Trigger Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

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
- 组合指纹 (Generation Fingerprint)：`D13BAD545E6930D13C5F82CB232FAD61B046864ED2E69E041EED877BC16C6B7A`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 推断类型 (Inferred Type) | 说明 (Description) |
|------|------|------|
| `StateKey` | `Any` | 源码输出字典初始化中可见字段。 |
| `StateScope` | `Any` | 源码输出字典初始化中可见字段。 |

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
- 适合 (Suitable)：需要对上游结果做判断、转换、聚合、计数、延时或流程路由的场景。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。
2. 运行时附加输出字段来自源码输出字典，部分字段未声明为可连线端口，下游稳定连线应优先使用输出端口表。
3. 源码包含状态缓存或实例级状态，长流程运行时需要关注状态清理、并发调用和实例复用边界。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |

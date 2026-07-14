# 边线交点 / EdgeIntersection

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `EdgeIntersectionOperator` |
| 枚举值 (Enum) | `OperatorType.EdgeIntersection` |
| 分类 ID (CategoryId) | `MatchingAndLocalization` |
| 分类 (Category) | 匹配与定位 |
| 分类顺序 (CategoryOrder) | 5 |
| 版本 (Version) | `1.0.0` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:MatchingAndLocalization`, `分类显示:匹配与定位`, `生命周期:Stable`, `算法类型:自研` |

## 算法原理 / Algorithm Principle
该算子用于计算两条直线的交点和夹角。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
处理过程遵循统一算子框架：输入检查、参数解析、核心计算、输出封装和可选参数校验分层完成。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`Line1`、`Line2`；缺失时通常返回失败结果。
- 参数解析覆盖 1 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `OperatorBase.Get*Param(...)`
- `Math.Sqrt`
- `Math.Clamp`
- `Math.Acos`
- `Math.PI`
- `Math.Abs`
- `Math.Min`
- `Math.Max`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `IntersectionMode` | Intersection Mode | `enum` | InfiniteLine | InfiniteLine；SegmentOnly | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Line1` | Line 1 | `LineData` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |
| `Line2` | Line 2 | `LineData` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Point` | 点 | `Point` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `Angle` | 角度 | `Float` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |
| `HasIntersection` | Has Intersection | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |
| `SegmentsIntersect` | Segments Intersect | `Boolean` | 布尔判定结果，适合连接条件分支、结果判定或通信写入。 |

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
- 组合指纹 (Generation Fingerprint)：`9CCBC23B4EF47D98AD518D6D7AFFA3B1096B285DE1497AA0D06A8F6B60431B41`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

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
- 执行失败契约：源码中发现 4 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：输入数据结构稳定、下游明确消费当前输出字段的常规流程节点。
- 不适合 (Not Suitable)：上游输入字段不稳定、参数缺少验收范围或下游依赖未声明输出字段的场景。

## 已知限制 / Known Limitations
1. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
2. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.0 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |

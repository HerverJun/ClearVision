# 数据聚合 / Aggregator

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `AggregatorOperator` |
| 枚举值 (Enum) | `OperatorType.Aggregator` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `merge` |
| 关键词 (Keywords) | 聚合, 合并, 汇总, 最大值, 最小值, 均值, 多路合并, Aggregate, Merge, Max, Min, Average |

## 算法原理 / Algorithm Principle
> **中文：** 将最多 3 路输入合并为列表，并在可解析为有限数值（finite double）的输入上计算统计极值与均值。
> 内部通过 `TryConvertToFiniteDouble` 将各种 CLR 类型（double/float/int/long/decimal/string/IFormattable 等）
> 统一转换为 `double`，过滤掉 NaN/Infinity，仅保留有限数值参与统计。
>
> **English:** Merges up to 3 input channels into a list, then computes statistical extremes and mean over inputs
> that can be parsed to finite `double` values. Internally normalizes CLR types (double/float/int/long/decimal/string/IFormattable)
> via `TryConvertToFiniteDouble`, filtering out NaN/Infinity so only finite values participate in statistics.

## 实现策略 / Implementation Strategy
- 3 路输入（Value1/Value2/Value3）均为可选，遍历后合并为 `mergedList`。
- 从 `mergedList` 中提取可转换为有限数值的元素到 `numericValues` 列表。
- `Merge` 模式直接输出原始合并列表；`Max/Min/Average` 模式要求至少存在 1 个有限数值输入，否则直接失败。
- 数值转换使用 `CultureInfo.InvariantCulture`，支持带千分位分隔符的字符串。
- `Result` 端口根据模式动态切换输出类型：Merge 返回列表，其余返回标量。

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "Mode", "Merge")` -> 校验模式合法性
2. 遍历 `Value1/Value2/Value3` -> 合并为 `mergedList`
3. `TryConvertToFiniteDouble(item, out parsed)` -> 提取有限数值到 `numericValues`
4. `numericValues.Max()` / `.Min()` / `.Average()` -> 计算统计指标
5. 模式 switch -> 设置 `Result` 端口值
6. `OperatorExecutionOutput.Success(output)` -> 返回 6 个输出端口

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"Merge"` | `Merge` / `Max` / `Min` / `Average` | 聚合模式。Merge=输出合并列表；Max/Min/Average=对有限数值输入求极值或均值。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value1` | 值 1 | `Any` | No | 第一路输入。 |
| `Value2` | 值 2 | `Any` | No | 第二路输入。 |
| `Value3` | 值 3 | `Any` | No | 第三路输入。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 结果 | `Any` | 当前模式下的主输出：Merge=列表，Max/Min/Average=标量。 |
| `MergedList` | 合并列表 | `Any` | 所有非空输入合并后的列表（始终输出）。 |
| `MaxValue` | 最大值 | `Float` | 有限数值输入中的最大值（无数值时为 0.0）。 |
| `MinValue` | 最小值 | `Float` | 有限数值输入中的最小值（无数值时为 0.0）。 |
| `Average` | 均值 | `Float` | 有限数值输入的算术平均值（无数值时为 0.0）。 |
| `NumericCount` | 数值数量 | `Integer` | 成功参与统计的有限数值个数。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n)，n 为输入数量（最多 3 路），常数时间操作 |
| 典型耗时 (Typical Latency) | < 1ms |
| 内存特征 (Memory Profile) | O(n) 存储 mergedList 和 numericValues 临时列表 |

## 适用场景 / Use Cases
- 适合 (Suitable)：多路传感器数值的汇总与极值比较
- 适合 (Suitable)：将多路检测结果合并为统一列表后传递给下游
- 适合 (Suitable)：需要快速判断多路输入中最大/最小/平均值的场景
- 不适合 (Not Suitable)：超过 3 路输入的聚合（需上游先合并）
- 不适合 (Not Suitable)：输入为非数值且需要统计的场景（非数值会被静默忽略）
- 不适合 (Not Suitable)：需要加权平均或标准差等高级统计（请使用 StatisticsOperator）

## 已知限制 / Known Limitations
1. 仅内置 3 路输入端口；更长序列应由上游先整理成集合后再处理。
2. 统计模式（Max/Min/Average）只认有限数值，无法解析的输入会被忽略而不会自动转换。
3. 统计模式下若 `NumericCount == 0`，算子直接返回失败而非输出 0。
4. `MaxValue/MinValue/Average` 在 Merge 模式下也会计算并输出，但 Result 端口不使用它们。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充算法原理、API 调用链、性能特征与适用场景细节 |
| 1.0.0 | 2026-04-12 | 新增 `NumericCount` 输出，收口统计模式下无有效数值时直接失败 |

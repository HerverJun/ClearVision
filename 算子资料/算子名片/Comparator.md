# 数值比较 / Comparator

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ComparatorOperator` |
| 枚举值 (Enum) | `OperatorType.Comparator` |
| 分类 (Category) | 逻辑控制 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | compare |
| 关键词 (Keywords) | 比较, 判断, 大于, 小于, 等于, 超限, 阈值判定, 公差, Compare, Threshold, GreaterThan, LessThan |

## 算法原理 / Algorithm Principle

**中文：**
数值比较算子对两个浮点数执行指定条件比较，输出布尔判定结果和差值。核心算法：

1. **输入解析**：`ValueA` 为必需输入，通过 `TryConvertToDouble` 支持 double/float/int/long/decimal 及字符串解析。`ValueB` 为可选输入，未连接时使用参数 `CompareValue` 作为默认比较值。
2. **条件评估**：支持 7 种比较条件 —— GreaterThan（>）、GreaterThanOrEqual（>=）、LessThan（<）、LessThanOrEqual（<=）、Equal（容差内相等）、NotEqual（容差外不等）、InRange（范围检查）。Equal/NotEqual 使用参数化容差 `Tolerance`，判定公式为 `|ValueA - ValueB| <= Tolerance`。
3. **差值计算**：始终输出 `Difference = ValueA - ValueB`（带符号），供下游算子使用。
4. **InRange 模式**：使用独立的 `RangeMin` 和 `RangeMax` 参数，与 ValueB 无关，判定公式为 `RangeMin <= ValueA <= RangeMax`。

**English:**
A numeric comparison operator that evaluates a specified condition between two floating-point values and outputs a boolean result plus the difference. Core algorithm:

1. **Input parsing**: `ValueA` is required, parsed via `TryConvertToDouble` supporting double/float/int/long/decimal and string. `ValueB` is optional; when disconnected, the `CompareValue` parameter is used as default.
2. **Condition evaluation**: Supports 7 comparison conditions — GreaterThan (>), GreaterThanOrEqual (>=), LessThan (<), LessThanOrEqual (<=), Equal (within tolerance), NotEqual (outside tolerance), InRange (range check). Equal/NotEqual use the parameterized `Tolerance`, with formula `|ValueA - ValueB| <= Tolerance`.
3. **Difference calculation**: Always outputs `Difference = ValueA - ValueB` (signed) for downstream use.
4. **InRange mode**: Uses independent `RangeMin` and `RangeMax` parameters, independent of ValueB, with formula `RangeMin <= ValueA <= RangeMax`.

## 实现策略 / Implementation Strategy

- **可选输入端口设计**：ValueB 设计为可选端口，未连接时自动回退到 CompareValue 参数，减少简单场景的连线复杂度。
- **类型安全转换**：`TryConvertToDouble` 对原始类型做模式匹配（double/float/int/long/decimal 直接转换，其他走 `double.TryParse`），避免装箱拆箱开销。
- **容差取绝对值**：`Tolerance` 参数在使用前取 `Math.Abs()`，防止用户误输入负值导致逻辑反转。
- **InRange 独立参数**：范围检查使用独立的 RangeMin/RangeMax 而非 ValueB，语义更清晰。
- **同步执行**：纯内存计算，无异步 I/O。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── TryReadRequired(inputs, "ValueA", out a)    // 必需输入解析
  │   └── TryConvertToDouble(obj, out value)       // 类型安全转换
  ├── TryReadOptional(inputs, "ValueB", out b)     // 可选输入解析
  │   └── GetDoubleParam("CompareValue")           // 回退到默认比较值
  ├── GetStringParam("Condition")                  // 获取比较条件
  ├── GetDoubleParam("Tolerance")                  // 获取容差（取绝对值）
  ├── GetDoubleParam("RangeMin")                   // 范围下限
  ├── GetDoubleParam("RangeMax")                   // 范围上限
  ├── diff = a - b                                 // 计算差值
  └── condition.ToLower() switch
      ├── "greaterthan"         => a > b
      ├── "greaterthanorequal"  => a >= b
      ├── "lessthan"            => a < b
      ├── "lessthanorequal"     => a <= b
      ├── "equal"               => |diff| <= tolerance
      ├── "notequal"            => |diff| > tolerance
      └── "inrange"             => a >= min && a <= max
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Condition` | `enum` | `"GreaterThan"` | GreaterThan / GreaterThanOrEqual / LessThan / LessThanOrEqual / Equal / NotEqual / InRange | 比较条件。Equal/NotEqual 使用容差判断；InRange 使用 RangeMin/RangeMax |
| `CompareValue` | `double` | `0.0` | - | 当 ValueB 端口未连线时使用的默认比较值 |
| `Tolerance` | `double` | `0.0001` | [0.0, +inf) | Equal/NotEqual 判断的容差，使用前自动取绝对值。判定公式：`|ValueA - ValueB| <= Tolerance` |
| `RangeMin` | `double` | `0.0` | - | InRange 模式的范围下限（含） |
| `RangeMax` | `double` | `1.0` | - | InRange 模式的范围上限（含） |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `ValueA` | 数值 A | `Float` | Yes | 被比较值（左侧操作数），必须为可解析为 double 的数值类型 |
| `ValueB` | 数值 B | `Float` | No | 比较值（右侧操作数），未连接时回退到 CompareValue 参数 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 判定结果 | `Boolean` | 条件评估结果（true=条件成立, false=条件不成立） |
| `Difference` | 差值 | `Float` | ValueA - ValueB 的带符号差值，供下游使用 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 单次数值比较，无循环 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（2 个键值对） |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 测量值超限判定：尺寸、距离、角度等测量结果与公差比较
- 阈值门控：如灰度值 > 阈值则触发后续处理
- 范围检查：InRange 模式判断值是否在合格区间内
- 差值分析：利用 Difference 输出进行偏差计算和趋势监控
- 容差相等判断：避免浮点精度问题的相等比较

**不适合 (Not Suitable)：**
- 字符串比较（应使用 ConditionalBranch 算子的 Contains/Equal 条件）
- 多条件组合判定（应使用 ResultJudgment 算子或多个 Comparator 级联）
- 非数值类型的比较（输入必须可解析为 double）

## 已知限制 / Known Limitations

1. **输出字典包含未声明字段**：实际输出仅含 `Result` 和 `Difference`，但运行时可能附加其他诊断字段。
2. **InRange 与 ValueB 无关**：InRange 模式使用 RangeMin/RangeMax 参数，忽略 ValueB 输入，可能造成混淆。
3. **ValidateParameters 无校验**：参数验证始终返回 Valid，不检查 RangeMin < RangeMax 等逻辑约束。
4. **容差仅适用于 Equal/NotEqual**：GreaterThan/LessThan 等条件不使用容差，严格使用 `>` / `<` 运算符。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充可选输入端口设计、类型安全转换、容差机制、InRange 模式原理；修正参数表描述；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

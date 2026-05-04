# 结果判定 / ResultJudgment

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ResultJudgmentOperator` |
| 枚举值 (Enum) | `OperatorType.ResultJudgment` |
| 分类 (Category) | 流程控制 Flow Control |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.1 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | result-judgment |
| 关键词 (Keywords) | judgment, ok, ng, condition, threshold |

## 算法原理 / Algorithm Principle

**中文：**
通用业务判定算子，对输入值执行数值或字符串条件检查，输出 OK/NG 判定结果。核心算法为：

1. **置信度门控**：若输入端口 `Confidence` 的值低于参数 `MinConfidence`，则直接输出 NG（短路判定），不执行后续条件评估。
2. **条件评估**：根据 `Condition` 参数选择 7 种比较模式之一（Equal、NotEqual、GreaterThan、LessThan、GreaterOrEqual、LessOrEqual、Range）。数值比较时使用 `NearlyEqual` 双容差算法：先检查绝对容差 `absTol`，再检查相对容差 `relTol = max(|a|,|b|) * relTol`，满足任一即视为相等。
3. **类型自适应**：通过 `TryParseDoubleInvariant` 将 double/float/decimal/int/long 及 InvariantCulture 字符串统一转换为 double，实现数值/字符串双模式比较。
4. **字段解析**：`FieldName` 参数指定从输入字典中按名取值，若未找到则回退到默认 `Value` 端口。

**English:**
A generic business judgment operator that evaluates numeric or string conditions against input values and outputs OK/NG results. Core algorithm:

1. **Confidence gating**: If the `Confidence` input port value is below the `MinConfidence` parameter, the operator immediately outputs NG (short-circuit), skipping condition evaluation.
2. **Condition evaluation**: Selects one of 7 comparison modes via the `Condition` parameter (Equal, NotEqual, GreaterThan, LessThan, GreaterOrEqual, LessOrEqual, Range). Numeric comparisons use a dual-tolerance `NearlyEqual` algorithm: checks absolute tolerance `absTol` first, then relative tolerance `relTol = max(|a|,|b|) * relTolParam`; either satisfied means equal.
3. **Type adaptation**: `TryParseDoubleInvariant` converts double/float/decimal/int/long and InvariantCulture strings to double, enabling dual-mode numeric/string comparison.
4. **Field resolution**: The `FieldName` parameter specifies which key to extract from the input dictionary; falls back to the default `Value` port if not found.

## 实现策略 / Implementation Strategy

- **双容差相等判断**：与 Halcon 的固定阈值不同，本算子同时支持绝对容差和相对容差，适用于大范围数量级变化的测量值比较（如微米级尺寸与毫米级尺寸混合判定）。
- **置信度前置门控**：在条件评估前先检查置信度，避免对低置信度结果做无意义比较，适用于 AI 推理结果后处理场景。
- **同步执行模型**：所有判定逻辑为纯内存计算，`ExecuteCoreAsync` 实际为同步返回（`Task.FromResult`），无 I/O 阻塞。
- **字段名动态解析**：支持从上游复合对象中按字段名提取值，无需额外拆包算子。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── GetStringParam("FieldName")          // 解析目标字段名
  ├── GetStringParam("Condition")          // 获取比较条件
  ├── GetDoubleParam("MinConfidence")      // 获取置信度阈值
  ├── GetDoubleParam("NumericAbsTolerance") // 获取绝对容差
  ├── GetDoubleParam("NumericRelTolerance") // 获取相对容差
  ├── ResolveActualValue(inputs, fieldName) // 按字段名从输入字典取值
  │   └── GetInputValue(inputs, fieldName)  // 忽略大小写的键查找
  ├── TryParseDoubleInvariant(Confidence)   // 解析置信度
  │   └── confidence < minConfidence ?      // 置信度门控
  │       └── CreateOutput(false, "MinConfidenceGate", ...)
  └── EvaluateCondition(actualValue, condition, ...)
      ├── TryParseDoubleInvariant(actual/expect/min/max) // 类型转换
      ├── NearlyEqual(a, b, absTol, relTol)              // 双容差判断
      │   └── diff <= absTol || diff <= max(|a|,|b|) * relTol
      └── CreateOutput(isOk, condition, details, actualValue)
          └── {JudgmentResult, IsOk, ConditionResult, JudgmentValue, Details, Condition, ActualValue}
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FieldName` | `string` | `"Value"` | - | 从上游输入字典中读取的字段名，如 `DefectCount`、`Distance`、`Score`；未找到时回退到 `Value` 端口 |
| `Condition` | `enum` | `"Equal"` | Equal / NotEqual / GreaterThan / LessThan / GreaterOrEqual / LessOrEqual / Range | 比较条件。Equal/NotEqual 对数值使用双容差判断；Range 模式需同时设置 ExpectValueMin 和 ExpectValueMax |
| `ExpectValue` | `string` | `"1"` | - | 判定目标值（Equal/NotEqual/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual 模式使用），支持数值和字符串 |
| `ExpectValueMin` | `string` | `""` | - | Range 模式的范围下限（含），必须为数值 |
| `ExpectValueMax` | `string` | `""` | - | Range 模式的范围上限（含），必须为数值 |
| `MinConfidence` | `double` | `0.0` | [0.0, 1.0] | 置信度门控阈值。输入 Confidence 低于此值时直接判定 NG，0 表示不检查置信度 |
| `NumericAbsTolerance` | `double` | `1e-4` | [0.0, 1000000.0] | 数值相等判断的绝对容差。当 |a-b| <= absTol 时视为相等 |
| `NumericRelTolerance` | `double` | `1e-6` | [0.0, 1.0] | 数值相等判断的相对容差。当 |a-b| <= max(|a|,|b|) * relTol 时视为相等 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | Value | `Any` | No | 默认输入值，当 FieldName 指定的字段未找到时使用 |
| `Confidence` | Confidence | `Float` | No | 置信度值（0-1），低于 MinConfidence 时判定为 NG |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `JudgmentResult` | Judgment Result | `String` | 判定结果文本：`"OK"` 或 `"NG"` |
| `IsOk` | Is OK | `Boolean` | 判定是否通过（true=OK, false=NG） |
| `ConditionResult` | Condition Result | `Boolean` | 条件评估结果，与 IsOk 相同 |
| `JudgmentValue` | Judgment Value | `String` | 判定数值：OK 时为 `"1"`，NG 时为 `"0"`，适用于 PLC 写入 |
| `Details` | Details | `String` | 判定详情，包含实际值、比较值和判定逻辑，如 `"3.14 == 3.14 => True"` |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 纯内存标量计算，无循环 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（约 7 个键值对） |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- OK/NG 质量判定流水线：螺钉计数、尺寸超限、缺陷数量阈值判定
- AI 推理结果后处理：结合 Confidence 端口过滤低置信度结果
- PLC 信号映射：JudgmentValue 输出 `"1"`/`"0"` 可直接写入 PLC 寄存器
- 多条件组合判定：配合 ForEach 算子对批量检测结果逐条判定
- 范围判定：测量值是否在公差范围内（如 9.95 <= length <= 10.05）

**不适合 (Not Suitable)：**
- 复杂多分支路由（应使用 ConditionalBranch 算子）
- 需要对判定结果做数学运算的场景（应配合 MathOperation 算子）
- 浮点高精度科学计算（容差机制为工业测量优化，非科学计算精度）

## 已知限制 / Known Limitations

1. **输出字典包含未声明字段**：实际输出字典中包含 `Condition` 和 `ActualValue` 两个额外字段，未在 `[OutputPort]` 属性中声明，下游算子可使用但不会在 UI 端口列表中显示。
2. **Range 模式端点含等号**：Range 判断使用 `>=` 和 `<=`（含端点），无法配置为开区间。
3. **字符串比较为 Ordinal**：非数值字符串的 Equal/NotEqual 使用 `StringComparison.Ordinal`（区分大小写），不支持忽略大小写比较。
4. **置信度门控无详情输出**：当因低置信度触发 NG 时，Details 字段固定为 `"Confidence below MinConfidence"`，不包含实际置信度数值。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部 OperatorMeta/InputPort/OutputPort/OperatorParam 属性，补充双容差算法原理、ExecuteCoreAsync 调用链、置信度门控机制、字段解析策略；修正参数表（移除已不存在的 OkOutputValue/NgOutputValue，补充 NumericAbsTolerance/NumericRelTolerance）；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

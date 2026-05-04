# 逻辑门 / LogicGate

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `LogicGateOperator` |
| 枚举值 (Enum) | `OperatorType.LogicGate` |
| 分类 (Category) | 通用 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | logic |

## 算法原理 / Algorithm Principle

**中文：**
逻辑门算子对布尔输入执行标准逻辑运算，输出布尔结果。支持 6 种逻辑操作：

| 操作 | 公式 | 说明 |
|------|------|------|
| AND | A & B | 两个输入均为 true 时输出 true |
| OR | A \| B | 任一输入为 true 时输出 true |
| NOT | !A | 仅使用 InputA，取反输出 |
| XOR | A ^ B | 两个输入不同时输出 true |
| NAND | !(A & B) | AND 结果取反 |
| NOR | !(A \| B) | OR 结果取反 |

**输入类型自适应**：`TryConvertToBool` 支持将多种类型转换为布尔值 —— bool 直接使用；int/long 非零为 true；double/float 非零为 true；字符串支持 `"true"/"false"`、`"1"/"0"`、`"yes"/"no"`、`"on"/"off"`（不区分大小写）。

**English:**
A logic gate operator that performs standard boolean logic operations on inputs and outputs a boolean result. Supports 6 logic operations:

| Operation | Formula | Description |
|-----------|---------|-------------|
| AND | A & B | True when both inputs are true |
| OR | A \| B | True when either input is true |
| NOT | !A | Uses InputA only, negates output |
| XOR | A ^ B | True when inputs differ |
| NAND | !(A & B) | Negated AND |
| NOR | !(A \| B) | Negated OR |

**Input type adaptation**: `TryConvertToBool` supports converting multiple types — bool used directly; int/long non-zero is true; double/float non-zero is true; strings support `"true"/"false"`, `"1"/"0"`, `"yes"/"no"`, `"on"/"off"` (case-insensitive).

## 实现策略 / Implementation Strategy

- **宽类型输入**：不同于严格的布尔端口，本算子通过 `TryConvertToBool` 接受数值、字符串等多种类型，减少上游类型转换算子的使用。
- **NOT 单输入模式**：NOT 操作仅需 InputA，InputB 可不连接，避免不必要的连线。
- **二元操作强制双输入**：除 NOT 外的 5 种操作均要求 InputB 提供有效布尔值，否则返回失败。
- **同步执行**：纯布尔运算，无 I/O 操作。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── GetStringParam("Operation")               // 获取逻辑操作类型
  ├── TryConvertToBool(InputA, out inputA)       // 输入 A 类型转换
  │   ├── bool   → 直接使用
  │   ├── int/long → != 0
  │   ├── double/float → abs > epsilon
  │   └── string → TryParse("true"/"false") || TryParse(int) || "yes"/"on" → true
  ├── (operation != "NOT") ?
  │   └── TryConvertToBool(InputB, out inputB)   // 输入 B 类型转换（二元操作）
  └── operation.ToUpperInvariant() switch
      ├── "AND"  => inputA && inputB
      ├── "OR"   => inputA || inputB
      ├── "NOT"  => !inputA
      ├── "XOR"  => inputA ^ inputB
      ├── "NAND" => !(inputA && inputB)
      └── "NOR"  => !(inputA || inputB)
  └── output = {Result, InputA, InputB, Operation}
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Operation` | `enum` | `"AND"` | AND / OR / NOT / XOR / NAND / NOR | 逻辑操作类型。NOT 为单输入操作（仅使用 InputA），其余为双输入操作 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `InputA` | 输入 A | `Boolean` | Yes | 第一个布尔输入。接受 bool/int/long/double/float/string 类型，自动转换为布尔值 |
| `InputB` | 输入 B | `Boolean` | No | 第二个布尔输入（NOT 操作时可不连接）。接受与 InputA 相同的类型转换规则 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | 输出 | `Boolean` | 逻辑运算结果 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 单次布尔运算，无循环 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（4 个键值对） |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 多条件组合判定：将多个检测结果用 AND/OR 组合为最终判定
- 条件取反：使用 NOT 操作反转上游判定结果
- 异或检测：两个条件恰好满足其一时触发（XOR）
- 信号组合：将多个传感器/检测结果组合为单一布尔信号
- 类型适配：上游输出数值 0/1 或字符串 "true"/"false" 时自动转换

**不适合 (Not Suitable)：**
- 多输入逻辑（>2 输入）：需级联多个 LogicGate 算子
- 位运算：不支持按位 AND/OR/XOR（仅布尔逻辑）
- 短路求值优化：OR/AND 不做短路求值，两个输入均会被计算

## 已知限制 / Known Limitations

1. **无短路求值**：AND/OR 操作不支持短路求值，两个输入端口的值均会被解析和转换，即使逻辑上不需要。
2. **字符串转换规则有限**：仅支持 `"true"/"false"`、`"1"/"0"`、`"yes"/"no"`、`"on"/"off"` 这几种字符串布尔值，其他字符串（如 `"OK"/"NG"`）无法转换，会返回失败。
3. **输出字典包含未声明字段**：实际输出包含 `InputA`、`InputB`、`Operation` 三个额外诊断字段。
4. **epsilon 阈值**：double/float 到布尔的转换使用 `Math.Abs(value) > Epsilon` 作为阈值，极小的非零浮点数（如 1e-300）会被视为 false。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充 6 种逻辑操作真值表、TryConvertToBool 类型转换规则、NOT 单输入模式说明；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

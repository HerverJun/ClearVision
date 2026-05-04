# 条件分支 / ConditionalBranch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ConditionalBranchOperator` |
| 枚举值 (Enum) | `OperatorType.ConditionalBranch` |
| 分类 (Category) | 控制 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | branch |
| 关键词 (Keywords) | 条件, 分支, 判断, 如果, 否则, IF, Branch, Condition, Switch |

## 算法原理 / Algorithm Principle

**中文：**
条件分支算子根据输入值与比较值的关系，将流程路由到 True 或 False 两条分支之一。核心算法：

1. **值解析**：从输入端口 `Value` 获取待判断值。若设置了 `FieldName` 参数且输入为 `Dictionary<string, object>`，则从字典中按字段名提取子值。
2. **条件评估**：支持 7 种条件模式 —— GreaterThan（大于）、LessThan（小于）、Equal（等于）、NotEqual（不等于）、Contains（包含）、StartsWith（前缀匹配）、EndsWith（后缀匹配）。前 4 种自动尝试数值比较（`double.TryParse`），成功则用数值比较，否则回退到字符串比较。Contains/StartsWith/EndsWith 始终为字符串操作。
3. **分支路由**：条件成立时，原始输入值透传到 `True` 端口，`False` 端口输出 null；条件不成立时反之。对于 `ImageWrapper` 类型，使用 `AddRef()` 增加引用计数以保证生命周期安全。
4. **输出元数据**：输出字典包含 `Condition`、`CompareValue`、`ActualValue`、`Result`、`FieldName` 等诊断字段。

**English:**
A conditional branching operator that routes the workflow to either the True or False branch based on the relationship between the input value and a comparison value. Core algorithm:

1. **Value resolution**: Retrieves the value from the `Value` input port. If the `FieldName` parameter is set and the input is a `Dictionary<string, object>`, extracts the sub-value by field name.
2. **Condition evaluation**: Supports 7 condition modes — GreaterThan, LessThan, Equal, NotEqual, Contains, StartsWith, EndsWith. The first 4 automatically attempt numeric comparison via `double.TryParse`; on success, uses numeric comparison, otherwise falls back to string comparison. Contains/StartsWith/EndsWith are always string operations.
3. **Branch routing**: When the condition is true, the original input value passes through to the `True` port and `False` outputs null; vice versa when false. For `ImageWrapper` types, `AddRef()` is called to ensure reference-counted lifetime safety.
4. **Output metadata**: The output dictionary includes diagnostic fields: `Condition`, `CompareValue`, `ActualValue`, `Result`, `FieldName`.

## 实现策略 / Implementation Strategy

- **双类型自适应比较**：不同于 Halcon 的严格类型分支，本算子先尝试数值解析再回退字符串，减少用户配置负担。
- **ImageWrapper 引用计数保护**：输出时对 ImageWrapper 调用 `AddRef()`，防止下游算子持有悬空引用，这是 ClearVision 框架特有的内存安全机制。
- **null 安全路由**：未激活的分支输出 null 而非抛异常，下游算子需自行处理 null 输入。
- **同步执行**：纯内存逻辑，`ExecuteCoreAsync` 通过 `Task.FromResult` 同步返回。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── inputs.TryGetValue("Value", out value)    // 获取输入值
  ├── GetStringParam("Condition")               // 获取比较条件
  ├── GetStringParam("CompareValue")            // 获取比较目标值
  ├── GetStringParam("FieldName")               // 获取字段名
  ├── dict.ContainsKey(fieldName) ? dict[fieldName] : value  // 字段解析
  └── EvaluateCondition(actualValue, condition, compareValueStr)
      ├── double.TryParse(actualValue)           // 尝试数值解析
      ├── double.TryParse(compareValueStr)       // 尝试数值解析
      └── condition.ToLower() switch
          ├── "greaterthan"  => actualNum > compareNum
          ├── "lessthan"     => actualNum < compareNum
          ├── "equal"        => numeric ? num==num : str==str
          ├── "notequal"     => numeric ? num!=num : str!=str
          ├── "contains"     => actualStr.Contains(compareStr)
          ├── "startswith"   => actualStr.StartsWith(compareStr)
          └── "endswith"     => actualStr.EndsWith(compareStr)
  ├── PreserveOutputValue(value)                // ImageWrapper.AddRef()
  └── outputData["True"/"False"] = result ? value : null
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Condition` | `enum` | `"GreaterThan"` | GreaterThan / LessThan / Equal / NotEqual / Contains | 比较条件。GreaterThan/LessThan/Equal/NotEqual 自动尝试数值比较；Contains 为字符串包含检查 |
| `CompareValue` | `string` | `"0"` | - | 比较目标值，支持数值和字符串。对于 GreaterThan/LessThan 等数值条件，会自动解析为 double |
| `FieldName` | `string` | `""` | - | 当输入 Value 为字典时，按此字段名提取子值进行比较。为空时直接使用 Value 端口的原始值 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | 判断值 | `Any` | Yes | 待判断的输入值，支持数值、字符串、字典等任意类型 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `True` | True分支 | `Any` | 条件成立时输出原始输入值（ImageWrapper 会增加引用计数），条件不成立时为 null |
| `False` | False分支 | `Any` | 条件不成立时输出原始输入值（ImageWrapper 会增加引用计数），条件成立时为 null |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 单次条件判断，无循环 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（约 7 个键值对）+ 值引用传递（不复制） |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- OK/NG 判定路由：根据检测结果将流程导向不同后续处理路径
- 数值阈值分流：如测量值 > 上限则报警，否则继续
- 字符串内容判断：根据 OCR 识别结果是否包含特定文本来分流
- 字典字段条件路由：从上游复合结果中提取特定字段做分支判断

**不适合 (Not Suitable)：**
- 多路分支（>2 路）路由（应使用多个级联 ConditionalBranch 或 Script 算子）
- 需要同时输出 True 和 False 两个分支数据的场景（当前设计为互斥输出）
- 浮点精度敏感的相等判断（应使用 Comparator 算子的容差机制）

## 已知限制 / Known Limitations

1. **输出字典包含未声明字段**：实际输出中包含 `Condition`、`CompareValue`、`ActualValue`、`Result`、`FieldName` 等额外诊断字段，未在 `[OutputPort]` 中声明。
2. **Equal 使用严格相等**：数值比较使用 `==` 运算符而非容差比较，浮点精度敏感场景应使用 Comparator 算子。
3. **ValidateParameters 识别更多条件**：验证逻辑支持 `StartsWith` 和 `EndsWith`，但 `[OperatorParam]` 的 Options 列表中未声明这两个选项。
4. **null 输入直接失败**：当 `Value` 端口未连接或值为 null 时，返回失败而非输出 False 分支。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充双类型自适应比较原理、ImageWrapper 引用计数保护机制、字段解析逻辑、7 种条件模式详情；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

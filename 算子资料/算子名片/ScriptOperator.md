# 脚本算子 / ScriptOperator

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ScriptOperator` |
| 枚举值 (Enum) | `OperatorType.ScriptOperator` |
| 分类 (Category) | Logic Tools（逻辑工具） |
| 图标 (Icon) | `script` |
| 关键词 (Keywords) | `script`, `custom`, `code`, `expression`, `formula` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：该算子执行用户自定义的 C# 表达式或脚本片段。支持两种语言模式：**CSharpExpression**（单表达式求值）和 **CSharpScript**（多语句执行）。执行流程：先将代码按换行符和分号拆分为语句列表，逐条解析。每条语句检查是否为赋值语句（含 `=`），若是则求值右侧表达式并将结果写入变量上下文或输出端口；否则直接求值表达式赋给 Output1。表达式求值引擎支持：引号字面量、布尔/数值字面量、变量替换（正则 `\b{key}\b` 匹配后替换为数值）、以及通过 `DataTable.Compute` 进行四则运算和比较运算。

> English: This operator executes user-defined C# expressions or script snippets. Two language modes are supported: **CSharpExpression** (single expression evaluation) and **CSharpScript** (multi-statement execution). The execution flow splits code by newlines and semicolons into statements, then parses each one. If a statement contains `=`, the right-hand side is evaluated and the result is written to the variable context or an output port; otherwise the expression is evaluated and assigned to Output1. The expression engine supports: quoted literals, boolean/numeric literals, variable substitution (regex `\b{key}\b` match replaced with numeric values), and arithmetic/comparison via `DataTable.Compute`.

## 实现策略 / Implementation Strategy
> 中文：算子有 4 个可选输入端口（Input1-4）和 2 个输出端口（Output1-2）。执行时先通过 `BuildContext` 将所有输入和默认值（Input1-4 默认为 0d）合并为一个不区分大小写的字典。代码拆分使用 `SplitStatements`（按 `\n` 和 `;` 分割，去除空白）。语句解析时自动去除 `return ` 前缀。赋值语句的目标若是 `Output1`/`Output2` 则写入对应输出，否则存入上下文字典供后续语句引用。表达式求值有五级优先级：引号字面量 -> 上下文直接查找 -> 布尔解析 -> 数值解析 -> 变量替换后 `DataTable.Compute`。通过 `CancellationTokenSource.CancelAfter(timeoutMs)` 实现超时控制。

> English: The operator has 4 optional input ports (Input1-4) and 2 output ports (Output1-2). During execution, `BuildContext` merges all inputs with defaults (Input1-4 default to 0d) into a case-insensitive dictionary. Code splitting uses `SplitStatements` (split by `\n` and `;`, trim whitespace). Statement parsing auto-strips `return ` prefixes. If an assignment target is `Output1`/`Output2`, the result is written to the corresponding output; otherwise it's stored in the context dictionary for subsequent statements. Expression evaluation has five priority levels: quoted literal -> context lookup -> boolean parse -> numeric parse -> variable substitution + `DataTable.Compute`. Timeout is enforced via `CancellationTokenSource.CancelAfter(timeoutMs)`.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam(@operator, "ScriptLanguage")` - 读取脚本语言
2. `GetStringParam(@operator, "Code")` - 读取代码字符串
3. `GetIntParam(@operator, "Timeout")` - 读取超时时间
4. `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(timeoutMs)` - 创建超时令牌
5. `BuildContext(inputs)` - 合并输入为不区分大小写的字典，补全 Input1-4 默认值
6. `SplitStatements(code)` - 按换行/分号拆分语句
7. 逐条处理: `NormalizeStatement` -> `TryParseAssignment` -> `EvaluateExpression`
8. `EvaluateExpression` 内部:
   - `IsQuotedLiteral` -> 返回字符串字面量
   - `context.TryGetValue` -> 返回上下文变量
   - `bool.TryParse` / `double.TryParse` -> 字面量解析
   - `ReplaceVariables(expr, context)` -> 正则变量替换
   - `DataTable.Compute(expr, null)` -> 数学表达式求值
9. 返回 `OperatorExecutionOutput.Success(...)` 包含 Output1, Output2

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `ScriptLanguage` | `enum` | `"CSharpExpression"` | `CSharpExpression` / `CSharpScript` | 脚本语言模式（当前两种模式行为一致，均按多语句解析） |
| `Code` | `string` | `"Input1 + Input2"` | 非空字符串 | 用户代码；支持多语句（换行或分号分隔）；赋值语法 `Output1 = expr` |
| `Timeout` | `int` | `5000` | 1 ~ 120000 | 执行超时时间（毫秒） |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input1` | Input 1 | `Any` | No | 第一个输入值（未连接时默认为 0d） |
| `Input2` | Input 2 | `Any` | No | 第二个输入值（未连接时默认为 0d） |
| `Input3` | Input 3 | `Any` | No | 第三个输入值（未连接时默认为 0d） |
| `Input4` | Input 4 | `Any` | No | 第四个输入值（未连接时默认为 0d） |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Output1` | Output 1 | `Any` | 主输出；赋值语句 `Output1 = expr` 或最后一条表达式的值 |
| `Output2` | Output 2 | `Any` | 辅助输出；仅通过赋值语句 `Output2 = expr` 写入 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(s * v) - s 为语句数，v 为上下文变量数（变量替换正则） |
| 典型耗时 (Typical Latency) | < 10 ms（简单表达式）；复杂多语句脚本可能更长 |
| 内存特征 (Memory Profile) | O(v + s) - 上下文字典 + 语句列表 |
| 安全机制 | 通过 `CancellationTokenSource` 实现超时中断（最长 120 秒） |

## 适用场景 / Use Cases
- 适合 (Suitable)：自定义数学计算，如 `Input1 * 2 + Input2 / 3`
- 适合 (Suitable)：条件表达式求值，如 `Input1 > 100`
- 适合 (Suitable)：多步计算逻辑，利用赋值语句存储中间结果
- 适合 (Suitable)：简单的字符串字面量输出（引号包裹的文本）
- 适合 (Suitable)：快速原型验证，无需编写完整算子
- 不适合 (Not Suitable)：需要完整 C# 语法（类、方法、LINQ 等）的复杂逻辑
- 不适合 (Not Suitable)：安全性敏感场景（代码在进程内执行，无沙箱隔离）
- 不适合 (Not Suitable)：需要访问外部资源（文件、网络、数据库）的脚本

## 已知限制 / Known Limitations
1. `DataTable.Compute` 仅支持基本四则运算、比较运算和部分数学函数，不支持三角函数、对数等高级数学运算。
2. 变量替换使用 `\b{key}\b` 正则逐个替换，若变量名是其他变量名的子串（如 `A` 和 `AB`）可能导致意外替换。
3. `CSharpExpression` 和 `CSharpScript` 两种模式在当前实现中行为完全一致（均按多语句解析），未做区分。
4. 未连接的输入端口默认为 `0d`（double），而非 null 或 string 空，可能导致非预期的类型行为。
5. `return ` 前缀去除使用固定 7 字符截断 (`trimmed[7..]`)，不处理 `return;`（无值返回）的情况。
6. 引号字面量支持双引号和单引号，但不支持转义字符（如 `\"` 或 `\'`）。
7. 超时仅通过 `CancellationToken` 实现，`DataTable.Compute` 本身不支持取消，实际超时精度有限。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写文档：精确定位五级表达式求值优先级链、发现 CSharpExpression/CSharpScript 行为一致的问题、明确变量替换正则的子串匹配风险、补充 DataTable.Compute 能力边界和超时精度限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

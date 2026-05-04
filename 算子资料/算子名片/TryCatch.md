# 异常捕获 / TryCatch

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `TryCatchOperator` |
| 枚举值 (Enum) | `OperatorType.TryCatch` |
| 分类 (Category) | 流程控制 |
| 成熟度 (Maturity) | 稳定 Stable |
| 版本 (Version) | 1.0.0 |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | trycatch |

## 算法原理 / Algorithm Principle

**中文：**
异常捕获算子是 Try-Catch 流程控制的标记节点，用于在流程图中定义异常处理分支。核心设计：

1. **标记性设计**：算子本身不执行异常捕获逻辑，而是输出统一的流程控制契约 —— 将输入值透传到 `Try` 端口，`Catch` 端口初始为 null，`HasError` 为 false，`Error` 为空字符串。
2. **异常捕获由执行引擎处理**：实际的 try-catch 语义由 `FlowExecutionService` 在子图执行层面实现。当前端算子（Try 分支中的算子）抛出异常时，执行引擎捕获异常并将错误信息路由到 Catch 分支。
3. **参数控制**：`EnableCatch` 参数标记是否启用异常捕获（当前算子层面仅记录日志，实际生效由执行引擎决定）。`CatchOutputError` 和 `CatchOutputStackTrace` 预留用于控制错误信息输出格式。
4. **ImageWrapper 引用安全**：透传输入值时，对 `ImageWrapper` 类型调用 `AddRef()` 增加引用计数。

**English:**
The TryCatch operator is a marker node for Try-Catch flow control, defining exception handling branches in the workflow graph. Core design:

1. **Marker design**: The operator itself does not perform exception catching; instead, it outputs a unified flow control contract — passing the input to the `Try` port, with `Catch` initially null, `HasError` false, and `Error` an empty string.
2. **Exception catching by execution engine**: Actual try-catch semantics are implemented by `FlowExecutionService` at the sub-graph execution level. When an operator in the Try branch throws an exception, the engine catches it and routes error information to the Catch branch.
3. **Parameter control**: The `EnableCatch` parameter marks whether exception catching is enabled (at the operator level, only logs; actual effect determined by the execution engine). `CatchOutputError` and `CatchOutputStackTrace` are reserved for controlling error output format.
4. **ImageWrapper reference safety**: When passing through input values, `AddRef()` is called on `ImageWrapper` types to increment the reference count.

## 实现策略 / Implementation Strategy

- **算子-引擎分离**：异常捕获的算子节点仅定义契约（Try/Catch/Error/HasError 端口），实际的 try-catch 逻辑由 `FlowExecutionService` 实现，遵循关注点分离原则。
- **预留参数**：`CatchOutputError` 和 `CatchOutputStackTrace` 参数已声明但当前未在算子逻辑中使用，为未来扩展预留。
- **同步执行**：算子本身仅做数据透传和默认值设置，`ExecuteCoreAsync` 通过 `Task.FromResult` 同步返回。
- **诊断日志**：通过 `Logger.LogDebug` 记录异常处理节点激活状态和 Catch 启用状态。

## 核心 API 调用链 / Core API Call Chain

```
ExecuteCoreAsync
  ├── GetBoolParam("EnableCatch", true)             // 获取 Catch 启用标志
  ├── GetBoolParam("CatchOutputError", true)         // （预留）错误信息输出标志
  ├── GetBoolParam("CatchOutputStackTrace", false)    // （预留）堆栈输出标志
  ├── inputs.TryGetValue("Input", out input)          // 获取输入值
  ├── PreserveOutputValue(input)                      // ImageWrapper.AddRef()
  └── output = {
      │   "Try":      input ?? null,                  // 透传到 Try 分支
      │   "Catch":    null,                           // 初始为 null（由引擎填充）
      │   "Error":    "",                             // 初始为空（由引擎填充）
      │   "HasError": false                           // 初始为 false（由引擎设置）
      │ }
```

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `EnableCatch` | `bool` | `true` | - | 是否启用异常捕获。当前算子层面仅记录日志，实际生效由 FlowExecutionService 决定 |
| `CatchOutputError` | `bool` | `true` | - | 是否输出错误信息到 Error 端口（预留参数，当前未在算子逻辑中使用） |
| `CatchOutputStackTrace` | `bool` | `false` | - | 是否输出异常堆栈信息（预留参数，当前未在算子逻辑中使用） |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Input` | 输入 | `Any` | No | 需要被保护的数据，透传到 Try 分支。ImageWrapper 类型会增加引用计数 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Try` | Try分支 | `Any` | 正常执行路径的输出端口。初始值为 Input 的透传，由执行引擎在异常发生时清空 |
| `Catch` | Catch分支 | `Any` | 异常执行路径的输出端口。初始为 null，由执行引擎在异常发生时填充异常信息 |
| `Error` | 错误信息 | `String` | 异常的错误消息文本。初始为空字符串，由执行引擎在异常发生时设置 |
| `HasError` | 是否有错 | `Boolean` | 是否发生异常的标志。初始为 false，由执行引擎在异常发生时设置为 true |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1) — 仅数据透传和默认值设置 |
| 典型耗时 (Typical Latency) | < 0.1ms，同步执行无 I/O |
| 内存特征 (Memory Profile) | 极低，仅分配输出字典（4 个键值对） |

## 适用场景 / Use Cases

**适合 (Suitable)：**
- 通信异常保护：包裹 HTTP 请求、Modbus 通信等可能失败的 I/O 操作
- 设备指令保护：发送 PLC/相机指令时捕获超时或通信异常
- 流程容错：在关键路径上添加异常捕获，确保单点失败不中断整个流程
- 错误信息收集：通过 Error/HasError 端口将异常信息传递给日志或报警算子

**不适合 (Not Suitable)：**
- 数据验证（应使用 ResultJudgment 或 ConditionalBranch 进行条件判断）
- 异常重试逻辑（应配合 ForEach/CycleCounter 实现重试循环）
- 算子参数校验（应使用 ValidateParameters 方法）

## 已知限制 / Known Limitations

1. **算子本身不执行异常捕获**：TryCatch 算子仅定义端口契约，实际的 try-catch 语义完全依赖 FlowExecutionService 的实现。
2. **预留参数未生效**：`CatchOutputError` 和 `CatchOutputStackTrace` 参数已在元数据中声明，但当前算子逻辑中仅读取值未使用（使用 `_` 丢弃）。
3. **输出字典包含未声明字段**：实际输出中 `Try`、`Catch`、`Error`、`HasError` 的初始值在算子层面设置，但异常发生时由引擎覆盖。
4. **无 Keywords 元数据**：`[OperatorMeta]` 中未设置 Keywords 数组，可能影响搜索发现。
5. **无 Version 元数据**：`[OperatorMeta]` 中未设置 Version 字段。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：提取全部属性元数据，补充标记性设计原理、算子-引擎分离架构、ImageWrapper 引用计数保护、预留参数说明；细化适用场景与已知限制 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

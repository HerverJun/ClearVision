# 双模态投票 / DualModalVoting

## 基本信息 / Basic Info

| 项目 (Field)      | 值 (Value)                       |
| ----------------- | -------------------------------- |
| 类名 (Class)      | `DualModalVotingOperator`      |
| 枚举值 (Enum)     | `OperatorType.DualModalVoting` |
| 分类 (Category)   | AI Detection                     |
| 成熟度 (Maturity) | 稳定 Stable                      |
| 作者 (Author)     | 蘅芜君                           |

## 算法原理 / Algorithm Principle

该算子把深度学习结果和传统规则结果融合成最终 OK/NG 判定。它不执行视觉推理，而是消费上游输出的 `DetectionResult` 或带 `IsOk/Confidence`、`DefectCount` 的字典，并按指定投票策略得到 `IsOk`、`Confidence` 和 `JudgmentValue`。

> English: Combines deep learning and traditional inspection results into a final judgment.

## 实现策略 / Implementation Strategy

- `DLResult` 和 `TraditionalResult` 均可来自 `DetectionResult`，也可来自字典结构。
- 支持 `WeightedAverage`、`Unanimous`、`Majority`、`PrioritizeDeepLearning`、`PrioritizeTraditional` 五种策略。
- 缺失单侧输入时会转为保守的 failed/neutral 结果；两侧都无有效输入时直接失败。
- `OkOutputValue` 和 `NgOutputValue` 用于把布尔判定转成 PLC 或后续通信算子需要的字符串值。

## 核心 API 调用链 / Core API Call Chain

1. `ExtractDetectionResult(inputs, "DLResult")`
2. `ExtractDetectionResult(inputs, "TraditionalResult")`
3. `NormalizeStrategy(...)`
4. `ToOkProbability(...)`
5. strategy-specific fusion
6. 输出 `IsOk`、`Confidence`、`JudgmentValue`

## 参数说明 / Parameters

| 参数名 (Name)           | 类型 (Type) | 默认值 (Default)    | 范围 (Range)   | 说明 (Description)              |
| ----------------------- | ----------- | ------------------- | -------------- | ------------------------------- |
| `VotingStrategy`      | `enum`    | `WeightedAverage` | 见策略列表     | 融合策略。                      |
| `DLWeight`            | `double`  | `0.6`             | `[0.0, 1.0]` | 加权平均时深度学习结果权重。    |
| `TraditionalWeight`   | `double`  | `0.4`             | `[0.0, 1.0]` | 加权平均时传统结果权重。        |
| `ConfidenceThreshold` | `double`  | `0.5`             | `[0.0, 1.0]` | OK 概率达到该阈值时判 OK。      |
| `OkOutputValue`       | `string`  | `1`               | -              | `IsOk=true` 时输出的判定值。  |
| `NgOutputValue`       | `string`  | `0`               | -              | `IsOk=false` 时输出的判定值。 |

## 输入/输出端口 / Input/Output Ports

### 输入 / Inputs

| 名称 (Name)           | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description)       |
| --------------------- | -------------------- | ------------------- | --------------- | ------------------------ |
| `DLResult`          | Deep learning result | `Any`             | Yes             | 深度学习检测结果或字典。 |
| `TraditionalResult` | Traditional result   | `Any`             | Yes             | 传统算法检测结果或字典。 |

### 输出 / Outputs

| 名称 (Name)       | 显示名 (DisplayName)             | 数据类型 (DataType) | 说明 (Description)        |
| ----------------- | -------------------------------- | ------------------- | ------------------------- |
| `IsOk`          | Whether the final result is OK   | `Boolean`         | 最终 OK/NG。              |
| `Confidence`    | Confidence of the final judgment | `Float`           | 最终判定置信度。          |
| `JudgmentValue` | Final judgment value             | `String`          | 映射后的 OK/NG 字符串值。 |

## 性能特征 / Performance

| 指标 (Metric)                | 值 (Value)                                                                                                                   |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| 时间复杂度 (Time Complexity) | `O(1)`，仅处理两路输入和少量字段转换。                                                                                     |
| 典型耗时 (Typical Latency)   | `DualModalVoting_contract_baseline.md` 记录 31/31 passed，总运行约 51 ms；单次执行通常为毫秒以下级别，主要受框架调度影响。 |
| 内存特征 (Memory Profile)    | 常量级，仅分配少量结果对象和输出字典。                                                                                       |

## 证据与失败契约 / Evidence & Failure Contracts

- Contract baseline：`quality/evals/reports/DualModalVoting_contract_baseline.md`，31/31 pass
- ed。
- 覆盖范围：五种策略、OK 概率转换、缺失输入、`DefectCount` 提取、自定义输出值、策略解析、权重校验和失败路径。
- 失败契约包括两路输入都不可解析、非法策略、`WeightedAverage` 权重和为 0、以及加权模式下权重和不约等于 1。

## 适用场景 / Use Cases

- 适合：深度学习和规则检测共同参与最终判定，且需要统一输出 `IsOk/JudgmentValue` 的流程。
- 适合：将视觉结果转换为 PLC、通信或结果判定算子的稳定输入。
- 不适合：把融合后的置信度理解为模型校准概率，或用它替代上游模型/规则的质量评估。

## 已知限制 / Known Limitations

1. 算子只融合已有结果，不会提高上游检测本身的召回率或定位精度。
2. 字典输入的字段语义需要上游保持一致；`DefectCount` 路径属于规则型兜底解析。
3. 当前 contract baseline 锁定的是决策契约，不代表任何视觉模型准确率。

## 变更记录 / Changelog

| 版本 (Version) | 日期 (Date) | 变更内容 (Changes)                                             |
| -------------- | ----------- | -------------------------------------------------------------- |
| 1.0.3          | 2026-04-28  | 回写 31/31 contract baseline、投票策略、失败契约和真实限制说明 |
| 1.0.2          | 2026-03-14  | 第二轮基于源码深化实现行为、性能与限制说明                     |
| 1.0.1          | 2026-03-14  | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制     |
| 1.0.0          | 2026-03-03  | 自动生成文档骨架 / Generated skeleton                          |

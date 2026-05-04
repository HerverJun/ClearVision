# 双模态投票 / DualModalVoting

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `DualModalVotingOperator` |
| 枚举值 (Enum) | `OperatorType.DualModalVoting` |
| 分类 (Category) | AI Detection |
| 显示名 (DisplayName) | Dual Modal Voting |
| 图标 (Icon) | `voting` |
| 版本 (Version) | `1.0.0` |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle

**中文：** 该算子把深度学习结果和传统规则结果融合成最终 OK/NG 判定。它不执行视觉推理，而是消费上游输出的 `DetectionResult` 或带 `IsOk/Confidence`、`DefectCount` 的字典，将两侧结果转换为 OK 概率（`ToOkProbability`），然后按指定投票策略得到 `IsOk`（布尔）、`Confidence`（置信度）和 `JudgmentValue`（映射后的字符串值）。

OK 概率转换规则：
- 成功的 `DetectionResult`：`IsOk=true` 时 OK 概率 = `confidence`；`IsOk=false` 时 OK 概率 = `1 - confidence`。
- 失败/未收到的结果：OK 概率 = 0.5（中性）。

**English:** This operator combines deep learning and traditional inspection results into a final OK/NG judgment. It does not perform visual inference; instead, it consumes upstream `DetectionResult` objects or dictionaries with `IsOk/Confidence` or `DefectCount` fields, converts both sides to OK probabilities (`ToOkProbability`), then applies the specified voting strategy to produce `IsOk` (boolean), `Confidence` (float), and `JudgmentValue` (mapped string value).

OK probability conversion rules:
- Successful `DetectionResult`: When `IsOk=true`, OK probability = `confidence`; when `IsOk=false`, OK probability = `1 - confidence`.
- Failed/unreceived results: OK probability = 0.5 (neutral).

## 实现策略 / Implementation Strategy

**中文：** 源码中的关键实现策略：

1. **输入解析灵活性**：`ExtractDetectionResult` 支持三种输入格式：
   - `DetectionResult` 对象（直接使用）
   - 字典含 `IsOk` + `Confidence` 字段（直接映射）
   - 字典含 `DefectCount` 字段（`DefectCount=0` -> IsOk=true，否则遍历 `Defects` 列表取最大置信度）
2. **缺失输入处理**：单侧输入缺失时转为 `DetectionResult.Failed(...)`（OK 概率 0.5）；两侧都无有效输入时直接失败。
3. **五种投票策略**：
   - `WeightedAverage`：加权 OK 概率，超过 `ConfidenceThreshold` 判 OK。验证时要求权重和约等于 1.0（误差 0.01）。
   - `Unanimous`：两侧都 OK 才判 OK，置信度取两侧 OK 概率的较小值。
   - `Majority`：两侧一致时取一致结果；不一致时取置信度高的一侧。
   - `PrioritizeDeepLearning`：直接取深度学习侧结果。
   - `PrioritizeTraditional`：直接取传统侧结果。
4. **输出映射**：`OkOutputValue` 和 `NgOutputValue` 将布尔判定映射为 PLC 或通信算子需要的字符串值（如 `"1"`/`"0"`）。
5. **策略归一化**：`NormalizeStrategy` 做大小写无关匹配，非法策略返回 null 触发失败。

**English:** Key implementation strategies:

1. **Flexible input parsing**: `ExtractDetectionResult` supports three formats:
   - `DetectionResult` object (used directly)
   - Dictionary with `IsOk` + `Confidence` fields (mapped directly)
   - Dictionary with `DefectCount` field (`DefectCount=0` -> IsOk=true; otherwise scans `Defects` list for max confidence)
2. **Missing input handling**: Single-side missing converts to `DetectionResult.Failed(...)` (OK probability 0.5); both sides missing causes immediate failure.
3. **Five voting strategies**:
   - `WeightedAverage`: Weighted OK probability exceeding `ConfidenceThreshold` judges OK. Validation requires weight sum approximately 1.0 (tolerance 0.01).
   - `Unanimous`: Both sides must be OK; confidence is the minimum of both OK probabilities.
   - `Majority`: When sides agree, uses the agreed result; when they disagree, uses the higher-confidence side.
   - `PrioritizeDeepLearning`: Uses the deep learning side result directly.
   - `PrioritizeTraditional`: Uses the traditional side result directly.
4. **Output mapping**: `OkOutputValue` and `NgOutputValue` map boolean judgments to string values needed by PLC or communication operators (e.g., `"1"`/`"0"`).
5. **Strategy normalization**: `NormalizeStrategy` does case-insensitive matching; invalid strategies return null triggering failure.

## 核心 API 调用链 / Core API Call Chain
1. `GetStringParam / GetDoubleParam` -- 读取参数
2. `NormalizeStrategy(strategy)` -- 归一化投票策略
3. `ExtractDetectionResult(inputs, "DLResult")` -- 解析深度学习结果
4. `ExtractDetectionResult(inputs, "TraditionalResult")` -- 解析传统结果
5. `ToOkProbability(dlResult)` / `ToOkProbability(traditionalResult)` -- 转换为 OK 概率
6. 策略特定融合逻辑（switch on normalizedStrategy）
7. `ToOutputConfidence(isOk, okProbability)` -- 计算输出置信度
8. 输出 `IsOk`、`Confidence`、`JudgmentValue`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `VotingStrategy` | `enum` | `WeightedAverage` | `WeightedAverage` / `Unanimous` / `Majority` / `PrioritizeDeepLearning` / `PrioritizeTraditional` | 融合策略。 |
| `DLWeight` | `double` | `0.6` | `[0.0, 1.0]` | 加权平均时深度学习结果权重。`WeightedAverage` 模式下与 `TraditionalWeight` 之和须约等于 1.0。 |
| `TraditionalWeight` | `double` | `0.4` | `[0.0, 1.0]` | 加权平均时传统结果权重。 |
| `ConfidenceThreshold` | `double` | `0.5` | `[0.0, 1.0]` | OK 概率达到该阈值时判 OK。仅 `WeightedAverage` 模式使用。 |
| `OkOutputValue` | `string` | `1` | - | `IsOk=true` 时输出的判定值字符串。 |
| `NgOutputValue` | `string` | `0` | - | `IsOk=false` 时输出的判定值字符串。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `DLResult` | Deep learning result | `Any` | Yes | 深度学习检测结果（`DetectionResult` 对象或字典）。 |
| `TraditionalResult` | Traditional result | `Any` | Yes | 传统算法检测结果（`DetectionResult` 对象或字典）。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `IsOk` | Whether the final result is OK | `Boolean` | 最终 OK/NG 判定。 |
| `Confidence` | Confidence of the final judgment | `Float` | 最终判定置信度（0-1）。 |
| `JudgmentValue` | Final judgment value | `String` | 映射后的 OK/NG 字符串值（由 `OkOutputValue`/`NgOutputValue` 决定）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(1)`，仅处理两路输入和少量字段转换。`DefectCount` 路径会遍历 `Defects` 列表取最大置信度，但列表通常很短。 |
| 典型耗时 (Typical Latency) | `DualModalVoting_contract_baseline.md` 记录 31/31 passed，总运行约 51 ms；单次执行通常为毫秒以下级别，主要受框架调度影响。 |
| 内存特征 (Memory Profile) | 常量级，仅分配少量结果对象和输出字典。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：深度学习和规则检测共同参与最终判定，且需要统一输出 `IsOk/JudgmentValue` 的流程。
- **适合 (Suitable)**：将视觉结果转换为 PLC、通信或结果判定算子的稳定输入。
- **适合 (Suitable)**：需要灵活切换投票策略（严格一致、加权、优先级）的工程场景。
- **不适合 (Not Suitable)**：把融合后的置信度理解为模型校准概率，或用它替代上游模型/规则的质量评估。
- **不适合 (Not Suitable)**：上游检测结果格式不一致且未做预处理的场景（字典字段语义需要上游保持一致）。

## 已知限制 / Known Limitations
1. 算子只融合已有结果，不会提高上游检测本身的召回率或定位精度。
2. 字典输入的字段语义需要上游保持一致；`DefectCount` 路径属于规则型兜底解析，当 `Defects` 列表中有多个缺陷时取最大置信度作为 label confidence。
3. `WeightedAverage` 模式下，`DLWeight + TraditionalWeight` 必须约等于 1.0（误差 0.01），否则参数校验失败。
4. 当前 contract baseline 锁定的是决策契约，不代表任何视觉模型准确率。
5. 输出置信度的含义随策略不同而变化：`WeightedAverage` 是加权概率，`Unanimous` 是最小概率，`Majority`/优先级模式是所选侧的标签置信度。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面重写文档至金标准质量：补全所有 [OperatorParam]/[InputPort]/[OutputPort] 属性元数据；新增 OK 概率转换规则、三种输入格式解析、五种策略的详细行为说明、权重校验约束；统一五列参数表；补全英文算法原理 |
| 1.0.3 | 2026-04-28 | 回写 31/31 contract baseline、投票策略、失败契约和真实限制说明 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

# 数组索引器 / ArrayIndexer

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ArrayIndexerOperator` |
| 枚举值 (Enum) | `OperatorType.ArrayIndexer` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `index` |

## 算法原理 / Algorithm Principle
> **中文：** 从可枚举集合（或 `DetectionList`）中按指定策略提取单个元素。
> 支持 6 种提取模式：按索引、最大置信度、最大/最小面积、首元素、末元素。
> 可选通过 `LabelFilter` 先按标签过滤候选集，再在子集内执行选择。
> 所有 `MaxConfidence/MaxArea/MinArea` 模式使用单次遍历（非全排序），时间复杂度 O(n)。
>
> **English:** Extracts a single element from an enumerable collection (or `DetectionList`) using a specified strategy.
> Supports 6 extraction modes: by index, max confidence, max/min area, first, last.
> Optional `LabelFilter` pre-filters candidates by label before selection.
> All `MaxConfidence/MaxArea/MinArea` modes use single-pass selection (not full sort), O(n) time complexity.

## 实现策略 / Implementation Strategy
- 严格只接受 `List` 输入键，通过 `ParseItems` 将 `DetectionList` 或 `IEnumerable` 统一为 `List<IndexedItem>`。
- `LabelFilter` 非空时先过滤，再按 `Mode` 选择目标元素；过滤后无结果则返回 `Found=false, Index=-1`。
- `Index` 输出固定为**原始输入列表索引**，而非过滤后子集索引。
- `MaxConfidence/MaxArea/MinArea` 要求所有候选元素必须是 `DetectionResult` 类型，否则校验失败。
- `SelectByMetric` 使用单次遍历维护最优候选，避免全排序开销。

## 核心 API 调用链 / Core API Call Chain
1. `inputs.TryGetValue("List", out itemsObj)` -> `ParseItems(itemsObj)` -> 统一为 `List<IndexedItem>`
2. `GetStringParam(@operator, "Mode", "Index")` + `GetIntParam(@operator, "Index", 0)` + `GetStringParam(@operator, "LabelFilter", "")`
3. `TryValidateCandidateMode(candidates, mode, labelFilter, ...)` -> 校验 DetectionResult 兼容性
4. `candidates.Where(i => MatchesLabel(i.Item, labelFilter))` -> 标签过滤
5. 模式分支：`Index` 直接下标 / `SelectByMetric` 单次遍历 / `First`/`Last` 取首尾
6. `OperatorExecutionOutput.Success(...)` -> Item, Found, Index, TotalCount, Message

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Mode` | `enum` | `"Index"` | `Index` / `MaxConfidence` / `MaxArea` / `MinArea` / `First` / `Last` | 提取模式。 |
| `Index` | `int` | `0` | >= 0（Index 模式下） | `Mode=Index` 时使用的下标。 |
| `LabelFilter` | `string` | `""` | - | 按标签过滤候选集（仅对 `DetectionResult` 有效，大小写不敏感）。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `List` | 列表 | `Any` | Yes | 可枚举集合或 `DetectionList`。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Item` | 元素 | `Any` | 选中的元素（未找到时为 null）。 |
| `Found` | 是否找到 | `Boolean` | 是否成功找到元素。 |
| `Index` | 原始索引 | `Integer` | 选中元素在原始输入列表中的索引（未找到时为 -1）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `TotalCount` | `Integer` | 过滤后候选集大小。 |
| `Message` | `String` | 附加信息（如最大置信度值、最大面积值等）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(n)（单次遍历选择，n 为候选元素数量） |
| 典型耗时 (Typical Latency) | < 1ms（千级元素） |
| 内存特征 (Memory Profile) | O(n) 存储 IndexedItem 列表副本 |

## 适用场景 / Use Cases
- 适合 (Suitable)：从检测结果中提取置信度最高或面积最大/最小的目标
- 适合 (Suitable)：按索引从列表中取特定位置的元素
- 适合 (Suitable)：先按标签过滤再选择的两阶段提取
- 不适合 (Not Suitable)：需要返回多个元素的场景（本算子只返回单个元素）
- 不适合 (Not Suitable)：非 DetectionResult 类型使用 MaxConfidence/MaxArea/MinArea 模式
- 不适合 (LikLabelFilter 对非 DetectionResult 类型无效，会返回 Found=false

## 已知限制 / Known Limitations
1. `MaxConfidence/MaxArea/MinArea` 和 `LabelFilter` 要求所有候选元素必须是 `DetectionResult` 类型。
2. `LabelFilter` 对非 `DetectionResult` 类型的元素无效（`MatchesLabel` 始终返回 false）。
3. `Index` 参数在非 Index 模式下被忽略。
4. 空列表输入返回 `Found=false, Index=-1` 而非失败。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充 6 种模式详细行为、单次遍历算法说明、运行时附加输出 |
| 1.1.0 | 2026-04-12 | 收口 `List` 单输入、声明 `LabelFilter`、`Index` 语义改为原始索引、极值选择改为单次遍历 |

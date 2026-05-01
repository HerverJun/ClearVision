# Region Complement / RegionComplement

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionComplementOperator` |
| 枚举值 (Enum) | `OperatorType.RegionComplement` |
| 分类 (Category) | Region |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Computes the complement of a region relative to an image size.。
> English: Computes the complement of a region relative to an image size..

## 实现策略 / Implementation Strategy
> 中文：Clips input runs to the explicit image bounds, groups valid runs by row, and emits the gaps in each row as the complement region.。
> English: Clips input runs to the explicit image bounds, groups valid runs by row, and emits the gaps in each row as the complement region..

## 核心 API 调用链 / Core API Call Chain
- `Region.RunLengths -> ClipRunToBounds -> row gap emission -> Region`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| - | - | - | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | - |
| `ImageWidth` | Image Width | `Integer` | No | - |
| `ImageHeight` | Image Height | `Integer` | No | - |
| `Image` | Reference Image (optional) | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Complement Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `Area` | Complement Area | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(R log R + H + K) |
| 典型耗时 (Typical Latency) | Avg 0.186 ms, max 4.522 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(R+H+K) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Building background masks or inverse ROIs inside a known image width and height.
- 不适合 (Not Suitable)：Unbounded geometric complement without a finite image domain.

## 已知限制 / Known Limitations
1. Explicit ImageWidth/ImageHeight or a reference image should be supplied for deterministic output bounds.
2. Input runs outside the explicit bounds are clipped or ignored before complement generation.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |

# Region Skeleton / RegionSkeleton

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `RegionSkeletonOperator` |
| 枚举值 (Enum) | `OperatorType.RegionSkeleton` |
| 分类 (Category) | Morphology |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Extracts skeleton using Zhang-Suen thinning algorithm. Preserves topology and connectivity.。
> English: Extracts skeleton using Zhang-Suen thinning algorithm. Preserves topology and connectivity..

## 实现策略 / Implementation Strategy
> 中文：Converts the region to a padded binary mask, applies iterative Zhang-Suen thinning, translates the skeleton back to original coordinates, and reports endpoint/branchpoint diagnostics.。
> English: Converts the region to a padded binary mask, applies iterative Zhang-Suen thinning, translates the skeleton back to original coordinates, and reports endpoint/branchpoint diagnostics..

## 核心 API 调用链 / Core API Call Chain
- `Region.ToMat -> ZhangSuenThinning -> Region.FromMat -> AnalyzeSkeleton`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `MaxIterations` | `int` | 100 | [1, 1000] | - |
| `PreserveTopology` | `bool` | true | - | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Region` | Input Region | `Any` | Yes | - |
| `Image` | Reference Image (Optional) | `Image` | No | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Region` | Skeleton Region | `Any` | - |
| `Image` | Visualization | `Image` | - |
| `SkeletonLength` | Skeleton Length | `Integer` | - |
| `BranchPoints` | Branch Point Count | `Integer` | - |
| `EndPoints` | End Point Count | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(I*W*H) |
| 典型耗时 (Typical Latency) | Avg 1.438 ms, max 18.477 ms over 100 synthetic golden cases |
| 内存特征 (Memory Profile) | O(W*H) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Extracting pixel skeletons for topology checks, centerline-like diagnostics, and coarse branch/end point counting.
- 不适合 (Not Suitable)：Subpixel centerline extraction, metrology-grade medial-axis fitting, or topology guarantees beyond the implemented Zhang-Suen rules.

## 已知限制 / Known Limitations
1. Endpoint and branchpoint counts are based on discrete 8-neighborhood diagnostics and may over-count near thick junctions.
2. PreserveTopology is reported in output metadata, but the execution path currently always uses the Zhang-Suen thinning implementation.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |

# 体素下采样 / VoxelDownsample

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VoxelDownsampleOperator` |
| 枚举值 (Enum) | `OperatorType.VoxelDownsample` |
| 分类 (Category) | 3D |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
> 中文：Voxel grid downsampling for point clouds (centroid per voxel).。
> English: Voxel grid downsampling for point clouds (centroid per voxel)..

## 实现策略 / Implementation Strategy
> 中文：Bins each point into a leaf-size voxel, accumulates point coordinates and optional color/normal channels per voxel, then emits one centroid representative for every occupied voxel.。
> English: Bins each point into a leaf-size voxel, accumulates point coordinates and optional color/normal channels per voxel, then emits one centroid representative for every occupied voxel..

## 核心 API 调用链 / Core API Call Chain
- `VoxelGridFilter.Downsample -> VoxelKey dictionary -> centroid/color/normal accumulation`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `LeafSize` | `double` | 0.01 | >= 1E-06 | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | Point Cloud | `Any` | Yes | - |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PointCloud` | Point Cloud | `Any` | - |
| `PointCount` | Point Count | `Integer` | - |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by point-cloud unit and flow tests |
| 内存特征 (Memory Profile) | O(V) |

## 适用场景 / Use Cases
- 适合 (Suitable)：Reducing dense point clouds before registration, clustering, or surface inspection.
- 适合 (Suitable)：Keeping approximate geometry while preserving averaged colors and normalized normals per voxel.
- 不适合 (Not Suitable)：Applications that require preserving every raw point or organized point-cloud topology.
- 不适合 (Not Suitable)：Very small leaf sizes that produce nearly one voxel per input point and little reduction.

## 已知限制 / Known Limitations
1. Output is always unorganized even when the input cloud is organized.
2. Voxel representatives are centroids rather than nearest original samples, so exact raw-point identity is not preserved.

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-04-24 | 自动生成文档骨架 / Generated skeleton |

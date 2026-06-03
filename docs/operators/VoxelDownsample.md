# 体素下采样 / VoxelDownsample

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VoxelDownsampleOperator` |
| 枚举值 (Enum) | `OperatorType.VoxelDownsample` |
| 分类 (Category) | 3D |
| 版本 (Version) | `1.0.1` |
| 成熟度 (Maturity) | 稳定 Stable |
| 标签 (Tags) | `功能域:检测`, `成熟度:稳定`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
当前元数据描述为：Voxel grid downsampling for point clouds (centroid per voxel)。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
算法类型以 `Voxel grid centroid downsampling` 为主；元数据未声明更多细分时，以当前源码实现为准。
源码中包含 OpenCV 调用，核心处理通常围绕图像矩阵、ROI、阈值、几何计算或可视化结果图展开。

## 实现策略 / Implementation Strategy
- 先校验必填输入：`PointCloud`；缺失时通常返回失败结果。
- 参数解析覆盖 1 个当前元数据字段，默认值、范围和枚举项以参数表为准。
- `ValidateParameters` 已提供参数合法性检查，部分越界或非法组合会在运行前被拦截。
- 源码包含异常捕获路径，外部依赖或运行时异常会被转为失败输出或诊断信息。
- 非图像输出直接以 `Dictionary<string, object>` 返回，字段名称以输出端口和运行时附加输出表为准。

## 核心 API 调用链 / Core API Call Chain
- `VoxelGridFilter.Downsample -> VoxelKey dictionary -> centroid/color/normal accumulation`
- `OperatorBase.Get*Param(...)`
- `OperatorExecutionOutput.Success(...)`
- `OperatorExecutionOutput.Failure(...)`

## 参数说明 / Parameters
| 参数名 (Name) | 显示名 (DisplayName) | 类型 (Type) | 默认值 (Default) | 范围/选项 (Range/Options) | 必填 (Required) | 说明 (Description) |
|--------|------|------|--------|------|------|------|
| `LeafSize` | Leaf Size | `double` | 0.01 | >= 1E-06 | Yes | - |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `PointCloud` | Point Cloud | `Any` | Yes | 必填输入，缺失时算子通常返回失败或无法产生有效结果。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `PointCloud` | Point Cloud | `Any` | 业务输出字段，具体结构以源码输出和运行时结果为准。 |
| `PointCount` | Point Count | `Integer` | 数值结果，可用于测量、阈值判定、统计或报表输出。 |

### 运行时附加输出 / Runtime Additional Outputs
- 未在源码中发现除声明输出端口外的稳定附加输出字段；下游连线以输出端口表为准。

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(N) |
| 典型耗时 (Typical Latency) | No dedicated golden benchmark yet; covered by point-cloud unit and flow tests |
| 内存特征 (Memory Profile) | O(V) |

## 证据与失败契约 / Evidence & Failure Contracts
- 单元/契约测试：已在 `ClearVision.Product/tests/ClearVision.Product.Tests/Operators` 中发现对应测试入口。
- Golden/回放证据：质量报告中存在通过的 baseline 证据。
- 参数失败契约：源码包含 `ValidateParameters`，非法参数会被明确拦截或返回错误说明。
- 执行失败契约：源码中发现 3 条 `OperatorExecutionOutput.Failure(...)` 路径。

## 适用场景 / Use Cases
- 适合 (Suitable)：Reducing dense point clouds before registration, clustering, or surface inspection.
- 适合 (Suitable)：Keeping approximate geometry while preserving averaged colors and normalized normals per voxel.
- 不适合 (Not Suitable)：Applications that require preserving every raw point or organized point-cloud topology.
- 不适合 (Not Suitable)：Very small leaf sizes that produce nearly one voxel per input point and little reduction.

## 已知限制 / Known Limitations
1. Output is always unorganized even when the input cloud is organized.
2. Voxel representatives are centroids rather than nearest original samples, so exact raw-point identity is not preserved.
3. 必填输入必须由上游节点提供；缺失输入时无法依靠默认参数自动补齐业务数据。
4. 参数范围和枚举项来自当前元数据；旧流程若保存了过期参数值，加载后需要重新校验。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 1.0.1 | 2026-05-16 | 按当前 `OperatorMetadataScanner` 口径重刷参数、端口、运行时附加输出、算法说明和限制 / Regenerated from current source metadata |

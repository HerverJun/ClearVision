# 体素下采样 / VoxelDownsample

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `VoxelDownsampleOperator` |
| 枚举值 (Enum) | `OperatorType.VoxelDownsample` |
| 分类 ID (CategoryId) | `PointCloud3D` |
| 分类 (Category) | 3D点云 |
| 分类顺序 (CategoryOrder) | 10 |
| 版本 (Version) | `1.0.1` |
| 生命周期 (Lifecycle) | 稳定 `Stable` |
| 生命周期说明 (Lifecycle Note) | - |
| 默认隐藏 (Default Hidden) | No |
| AI 默认推荐 (Default AI Recommendation) | Yes |
| AI 必须披露状态 (Requires Disclosure) | No |
| 标签 (Tags) | `分类:PointCloud3D`, `分类显示:3D点云`, `生命周期:Stable`, `算法类型:基于OpenCV` |

## 算法原理 / Algorithm Principle
该算子用于对点云执行体素网格下采样（每个体素取质心）。运行时从声明输入端口读取数据，按参数表解析配置，并把处理结果写入输出字典。
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
| `LeafSize` | 体素尺寸 | `double` | 0.01 | >= 1E-06 | Yes | - |

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

## 模式与资源契约 / Mode & Resource Contracts
### 参数条件 / Parameter Conditions
| 参数 (Parameter) | 必填条件 (Required) | 可见条件 (Visible) | 启用/禁用条件 (Enabled/Disabled) | 忽略条件 (Ignored) | 资源 (Resource) | 输入可满足 (Satisfied By Inputs) | 原因码 (Reason) |
|------|------|------|------|------|------|------|------|
| - | - | - | - | - | - | - | - |

### 输出条件 / Output Conditions
| 输出 (Output) | 保证可用条件 (Available When) | 原因码 (Reason) |
|------|------|------|
| - | - | - |

## 生成依赖 / Generation Dependencies
- 组合指纹 (Generation Fingerprint)：`43713255CDABA388E6034872BAA0C2A828C9D44F8A953DB11EA8306ABD748830`
- 显式共享依赖：无；指纹由最终运行时元数据与算子源码组成。

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
| 1.0.1 | 2026-07-14 | 按当前最终运行时元数据、条件契约和显式依赖口径重生成 / Regenerated from effective runtime metadata and declared dependencies |

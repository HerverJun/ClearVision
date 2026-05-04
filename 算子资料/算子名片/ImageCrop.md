# 图像裁剪 / ImageCrop

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageCropOperator` |
| 枚举值 (Enum) | `OperatorType.ImageCrop` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子从输入图像中提取指定矩形区域（ROI, Region of Interest）。通过左上角坐标 `(X, Y)` 和尺寸 `(Width, Height)` 定义裁剪窗口：

1. 使用 OpenCV `Mat` 的 ROI 构造函数 `new Mat(src, roi)` 获取子矩阵视图（零拷贝引用）。
2. 将 ROI 内容拷贝到从 `MatPool.Shared` 租借的独立 Mat 中，确保输出不依赖源图像生命周期。
3. 执行边界安全检查：若 `X` 或 `Y` 超出图像范围，自动钳制到图像边缘；若 `X + Width` 或 `Y + Height` 超出边界，自动缩小裁剪尺寸。

> English: The operator extracts a rectangular ROI from the input image using OpenCV Mat's ROI constructor, then copies the region to a pooled Mat for zero-copy-safe output. Boundary clamping prevents out-of-bounds access.

## 实现策略 / Implementation Strategy
- 使用 `new Mat(src, roi)` 获取 ROI 子矩阵，这是 OpenCV 的标准零拷贝机制，不复制像素数据。
- 输出时通过 `MatPool.Shared.Rent` 从对象池租借 Mat 并执行 `CopyTo`，保证输出 Mat 独立于源图像，避免源图像释放后输出悬空。
- 边界检查采用钳制策略（而非报错），保证在参数轻微越界时仍能输出有效结果，适合现场参数漂移场景。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)`
2. `GetIntParam(@operator, "X", 0, 0)` / `GetIntParam("Y", ...)` / `GetIntParam("Width", ...)` / `GetIntParam("Height", ...)`
3. `imageWrapper.GetMat()`
4. `new Rect(x, y, width, height)`
5. `new Mat(src, roi)` — ROI 子矩阵视图
6. `MatPool.Shared.Rent(width, height, src.Type())` — 从对象池租借输出 Mat
7. `cropped.CopyTo(dst)` — 像素拷贝到独立 Mat
8. `CreateImageOutput(dst)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `X` | `int` | `0` | [0, +inf) | 裁剪区域左上角 X 坐标（像素）。超出图像宽度时自动钳制。 |
| `Y` | `int` | `0` | [0, +inf) | 裁剪区域左上角 Y 坐标（像素）。超出图像高度时自动钳制。 |
| `Width` | `int` | `100` | [1, +inf) | 裁剪区域宽度（像素）。与 X 之和超出图像宽度时自动缩小。 |
| `Height` | `int` | `100` | [1, +inf) | 裁剪区域高度（像素）。与 Y 之和超出图像高度时自动缩小。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待裁剪的源图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 裁剪后的 ROI 子图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度（裁剪后）。 |
| `Height` | `Integer` | 输出图像高度（裁剪后）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(ROI_W x ROI_H)，线性于裁剪区域像素数。 |
| 典型耗时 (Typical Latency) | 1080p 图像裁剪 100x100 ROI 约 0.1-0.5 ms。 |
| 内存特征 (Memory Profile) | 通过 MatPool 租借输出 Mat，峰值内存为裁剪区域大小；对象池复用减少 GC 压力。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：从大视野图像中提取感兴趣检测区域，如定位特定工位或窗口。
- **适合 (Suitable)**：将全景图裁剪为多个子区域分别送入不同检测算子。
- **适合 (Suitable)**：去除图像边缘无效黑边或标定伪影。
- **不适合 (Not Suitable)**：需要旋转裁剪或不规则形状裁剪的场景，应配合 `ImageRotate` 使用。
- **不适合 (Not Suitable)**：需要根据图像内容自适应确定裁剪区域的场景，需先用检测算子定位 ROI。

## 已知限制 / Known Limitations
1. 边界检查采用钳制而非报错，当参数严重超出图像范围时，实际裁剪区域可能远小于预期，但不会提示错误。
2. 不支持旋转裁剪（Rotated Rect），只能裁剪轴对齐矩形。
3. 当裁剪区域被钳制后尺寸变化时，下游依赖固定尺寸的算子可能出错。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充 ROI 构造函数机制、MatPool 对象池、边界钳制策略和 API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

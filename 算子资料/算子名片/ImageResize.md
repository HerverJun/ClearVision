# 图像缩放 / ImageResize

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageResizeOperator` |
| 枚举值 (Enum) | `OperatorType.ImageResize` |
| 分类 (Category) | 预处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
该算子通过 `Cv2.Resize` 将输入图像缩放至指定尺寸或按比例缩放。支持两种模式：

**绝对尺寸模式**（`UseScale=false`）：将图像缩放到精确的 `(Width, Height)` 目标尺寸，不保持原始宽高比。
```
Cv2.Resize(src, dst, new Size(Width, Height), 0, 0, interpFlag)
```

**比例模式**（`UseScale=true`）：按 `ScaleFactor` 等比例缩放图像。
```
Cv2.Resize(src, dst, new Size(), ScaleFactor, ScaleFactor, interpFlag)
```

支持四种插值算法：
- `Nearest`（最近邻）：速度最快，放大时有明显锯齿，适合标签图/掩码。
- `Linear`（双线性）：默认算法，速度与质量平衡。
- `Cubic`（三次）：质量最高，速度较慢，适合放大高分辨率图像。
- `Area`（区域）：缩小时基于像素区域平均，避免摩尔纹，适合缩略图生成。

> English: The operator resizes images via `Cv2.Resize` using either absolute target dimensions or a scale factor, with four interpolation methods: Nearest, Linear, Cubic, and Area.

## 实现策略 / Implementation Strategy
- 使用 `Cv2.Resize` 而非手动重采样，利用 OpenCV 内部的 SIMD 和多线程优化。
- 提供 `UseScale` 开关切换两种模式，避免用户在比例缩放时需手动计算目标尺寸。
- 四种插值方法通过枚举参数切换，用户可根据精度和速度需求选择。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, "Image", ...)`
2. `GetIntParam(@operator, "Width", 640, 1, 8192)` / `GetIntParam("Height", ...)`
3. `GetDoubleParam(@operator, "ScaleFactor", 1.0, 0.01, 10.0)`
4. `GetStringParam(@operator, "Interpolation", "Linear")`
5. `GetBoolParam(@operator, "UseScale", false)`
6. `imageWrapper.GetMat()`
7. **UseScale=true**: `Cv2.Resize(src, dst, new Size(), scaleFactor, scaleFactor, interpFlag)`
8. **UseScale=false**: `Cv2.Resize(src, dst, new Size(targetWidth, targetHeight), 0, 0, interpFlag)`
9. `CreateImageOutput(dst)`

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Width` | `int` | `640` | [1, 8192] | 目标宽度（像素）。仅在 `UseScale=false` 时生效。 |
| `Height` | `int` | `480` | [1, 8192] | 目标高度（像素）。仅在 `UseScale=false` 时生效。 |
| `ScaleFactor` | `double` | `1.0` | [0.01, 10.0] | 缩放比例。仅在 `UseScale=true` 时生效。1.0 为原始尺寸。 |
| `Interpolation` | `enum` | `"Linear"` | `Nearest` / `Linear` / `Cubic` / `Area` | 插值方法。Nearest 最快；Linear 平衡；Cubic 最佳质量；Area 适合缩小。 |
| `UseScale` | `bool` | `false` | `true` / `false` | `true` 使用 ScaleFactor 比例缩放；`false` 使用 Width/Height 绝对尺寸。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待缩放的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Image` | 图像 | `Image` | 缩放后的输出图像。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Width` | `Integer` | 输出图像宽度。 |
| `Height` | `Integer` | 输出图像高度。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(srcW x srcH + dstW x dstH)，线性于源和目标像素总数。 |
| 典型耗时 (Typical Latency) | 1080p -> 640x480 (Linear) 约 1-3 ms；Nearest 最快，Cubic 最慢。 |
| 内存特征 (Memory Profile) | 额外分配一幅目标尺寸的输出 Mat。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：将高分辨率工业相机图像缩小到检测模型要求的输入尺寸（如 640x640）。
- **适合 (Suitable)**：生成缩略图用于快速预览或界面展示。
- **适合 (Suitable)**：用 Area 插值缩小图像以减少数据量，同时避免摩尔纹。
- **不适合 (Not Suitable)**：需要保持宽高比的缩放场景，当前绝对尺寸模式会强制拉伸到目标尺寸。
- **不适合 (Not Suitable)**：超分辨率放大（如 2x/4x 放大并保持清晰度），本算子仅做传统插值。

## 已知限制 / Known Limitations
1. 绝对尺寸模式（`UseScale=false`）不保持宽高比，可能导致图像变形。
2. 目标尺寸上限为 8192 像素，超出范围会被参数校验拦截。
3. `ScaleFactor=1.0` 时输出与输入相同，但会产生一次完整的内存拷贝。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-03 | 基于源码全面重写：补充绝对尺寸/比例两种模式、四种插值算法对比、API 调用链 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

# 单位换算 / UnitConvert

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `UnitConvertOperator` |
| 枚举值 (Enum) | `OperatorType.UnitConvert` |
| 分类 (Category) | 数据处理 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |
| 图标 (Icon) | `unit` |
| 关键词 (Keywords) | unit convert, pixel to mm, mm, um, inch |

## 算法原理 / Algorithm Principle
> **中文：** 在像素(Pixel)、毫米(mm)、微米(um)、英寸(inch)之间执行单位转换。
> 内部以毫米为中间单位：先将输入值转换为毫米，再从毫米转换为目标单位。
>
> 转换公式（以毫米为基准）：
> - Pixel -> mm: `value * pixelSize`
> - um -> mm: `value / 1000`
> - inch -> mm: `value * 25.4`
> - mm -> Pixel: `mmValue / pixelSize`
> - mm -> um: `mmValue * 1000`
> - mm -> inch: `mmValue / 25.4`
>
> 当 `UseCalibration=true` 时，像素当量从 `PixelSize` 端口读取（动态标定值）；
> 否则使用 `Scale` 参数作为像素当量。
>
> **English:** Converts values between Pixel, mm, um, and inch.
> Uses mm as intermediate unit: converts input to mm first, then from mm to target unit.
>
> When `UseCalibration=true`, pixel size is read from the `PixelSize` input port (dynamic calibration);
> otherwise the `Scale` parameter is used as pixel size.

## 实现策略 / Implementation Strategy
- 单位名称统一通过 `NormalizeUnit` 规范化：`"px"` -> `"pixel"`，`"μm"` -> `"um"`。
- 支持的单位集合：`pixel`/`mm`/`um`/`inch`（大小写不敏感）。
- 涉及像素的转换（from 或 to 为 pixel）需要像素当量：`UseCalibration=true` 时从端口读取，否则使用 `Scale`。
- 所有输入输出必须为有限数（finite），NaN/Infinity 直接拒绝。
- `Scale` 和像素当量必须为正的有限数（> 0）。
- 输出包含 `UsedPixelSize` 字段（仅在涉及像素转换时），记录实际使用的像素当量。

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputDouble(inputs, "Value", out value)` -> 类型转换 + 有限数校验
2. `NormalizeUnit(GetStringParam(@operator, "FromUnit", "Pixel"))` + `NormalizeUnit(ToUnit)`
3. `TryGetFiniteDoubleParameter(@operator, "Scale", 1.0, out scale)` -> 校验正有限数
4. `GetBoolParam(@operator, "UseCalibration", false)` -> 条件读取 `PixelSize` 端口
5. `ConvertToMillimeter(value, fromUnit, pixelSize)` -> 转为毫米
6. `ConvertFromMillimeter(mmValue, toUnit, pixelSize)` -> 转为目标单位
7. `double.IsFinite(mmValue) && double.IsFinite(result)` -> 结果校验
8. `OperatorExecutionOutput.Success(...)` -> Result, Unit, [UsedPixelSize]

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `FromUnit` | `enum` | `"Pixel"` | `Pixel` / `mm` / `um` / `inch` | 源单位。 |
| `ToUnit` | `enum` | `"mm"` | `Pixel` / `mm` / `um` / `inch` | 目标单位。 |
| `Scale` | `double` | `1.0` | [1E-9, 1000000.0] | 像素当量（mm/px）。UseCalibration=false 时用于像素转换。 |
| `UseCalibration` | `bool` | `false` | - | 是否从 PixelSize 端口读取动态像素当量。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Value` | Value | `Float` | Yes | 待转换的数值。 |
| `PixelSize` | Pixel Size | `Float` | No | 动态像素当量（mm/px），UseCalibration=true 时必填。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `Result` | Result | `Float` | 转换后的数值。 |
| `Unit` | Unit | `String` | 转换后的单位显示名（px/mm/um/inch）。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `UsedPixelSize` | `Float` | 实际使用的像素当量（仅在涉及像素转换时输出）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | O(1)（常数时间乘除运算） |
| 典型耗时 (Typical Latency) | < 0.1ms |
| 内存特征 (Memory Profile) | O(1)（仅存储输入输出标量） |

## 适用场景 / Use Cases
- 适合 (Suitable)：像素测量结果转换为物理单位（mm/um/inch）
- 适合 (Suitable)：不同物理单位之间的换算（mm <-> um <-> inch）
- 适合 (Suitable)：配合标定算子动态获取像素当量进行转换
- 不适合 (Not Suitable)：面积或体积的单位转换（本算子仅处理线性值）
- 不适合 (Not Suitable)：需要坐标系变换的场景（请使用 CoordinateTransformOperator）
- 不适合 (Not Suitable)：角度单位转换（度/弧度）

## 已知限制 / Known Limitations
1. 仅支持线性值的单位转换，不支持面积（mm^2）或体积（mm^3）。
2. `UseCalibration=true` 时若 `PixelSize` 端口未连接或值无效，直接返回失败。
3. 涉及像素转换时，`Scale` 和 `PixelSize` 端口的语义相同，同时提供时 `UseCalibration` 决定优先级。
4. `UsedPixelSize` 输出仅在涉及像素转换时存在，非像素间转换时不输出此字段。
5. 单位别名 `"px"` 自动映射为 `"pixel"`，`"μm"` 映射为 `"um"`。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 全面升级至 gold standard 文档；补充转换公式、UseCalibration 优先级、UsedPixelSize 输出 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 |

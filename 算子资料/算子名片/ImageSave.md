# 图像保存 / ImageSave

## 基本信息 / Basic Info
| 项目 (Field) | 值 (Value) |
|------|------|
| 类名 (Class) | `ImageSaveOperator` |
| 枚举值 (Enum) | `OperatorType.ImageSave` |
| 分类 (Category) | 输出 |
| 成熟度 (Maturity) | 稳定 Stable |
| 作者 (Author) | 蘅芜君 |

## 算法原理 / Algorithm Principle
中文：该算子负责将检测图像保存到本地硬盘，支持 PNG、JPEG、BMP 三种格式。核心流程为：从输入端获取图像 -> 解析目录与文件名模板 -> 自动创建目录 -> 替换模板中的时间戳/GUID 占位符 -> 根据扩展名选择编码参数 -> 调用 OpenCV `ImWrite` 写盘。JPEG 格式支持通过 `Quality` 参数控制压缩质量（1-100）。当目标文件已存在且未启用覆盖时，自动在文件名后追加递增序号（`_001`, `_002`, ...）以避免覆盖。文件名模板支持 `{timestamp}`、`{date}`、`{time}`、`{year}`、`{month}`、`{day}`、`{Guid}` 等占位符，运行时自动替换为当前时间或随机 GUID。

> English: This operator saves inspection images to local disk in PNG, JPEG, or BMP format. It resolves a directory and filename template, auto-creates the directory, replaces timestamp/GUID placeholders at runtime, selects encoding parameters by extension, and calls OpenCV `ImWrite`. JPEG quality is configurable (1-100). When a file already exists and overwrite is disabled, an incrementing counter suffix is appended. Supported filename placeholders include `{timestamp}`, `{date}`, `{time}`, `{year}`, `{month}`, `{day}`, and `{Guid}`.

## 实现策略 / Implementation Strategy
- **参数兼容层**：实现了一套双名解析策略（`Directory`/`FolderPath`、`FileNameTemplate`/`FileName`、`Quality`/`JpegQuality`），新参数优先，旧参数作为 fallback，保证向后兼容。
- **格式自动推断**：当未显式指定 `Format` 参数时，从文件名扩展名自动推断输出格式；无法推断时默认 PNG。
- **文件名安全校验**：`TryValidateFileName` 阻止绝对路径、目录分隔符、`..` 路径遍历和非法字符，防止目录逃逸。
- **冲突自动避让**：文件存在且 `Overwrite=false` 时，循环追加 `_001`、`_002` 序号直到找到不冲突的文件名。
- **显式配置检测**：`IsExplicitlyConfigured` 比较 `ValueJson` 与 `DefaultValueJson`，仅在用户真正修改了参数值时才使用该参数，避免默认值覆盖 legacy 参数。

> English: The implementation uses a dual-name parameter resolution layer for backward compatibility, auto-infers output format from filename extension, validates filenames against path traversal, appends incrementing counters on conflict, and uses explicit-configuration detection to avoid default values overriding legacy parameters.

## 核心 API 调用链 / Core API Call Chain
1. `TryGetInputImage(inputs, out imageWrapper)` -- 获取输入图像
2. `ResolveDirectory(@operator)` -- 解析保存目录（`Directory` 优先，`FolderPath` 兜底）
3. `ResolveFileNameTemplate(@operator)` -- 解析文件名模板（`FileNameTemplate` 优先，`FileName` 兜底）
4. `ResolveFormat(@operator, fileName)` -- 从显式参数或扩展名推断格式
5. `ResolveJpegQuality(@operator)` -- 解析 JPEG 质量（`Quality` 优先，`JpegQuality` 兜底）
6. `Directory.CreateDirectory(folderPath)` -- 自动创建目录
7. `ReplaceFileNameTemplate(fileName)` -- 替换 `{timestamp}`、`{Guid}` 等占位符
8. `TryValidateFileName(actualFileName)` -- 文件名安全校验
9. `File.Exists(fullPath)` + 序号递增循环 -- 冲突避让
10. `Cv2.ImWrite(fullPath, mat, formatParams)` -- OpenCV 写盘

## 参数说明 / Parameters
| 参数名 (Name) | 类型 (Type) | 默认值 (Default) | 范围 (Range) | 说明 (Description) |
|--------|------|--------|------|------|
| `Directory` | `string` | `"C:\\ClearVision\\NG_Images"` | 有效目录路径 | 图像保存目录。不存在时自动创建（需 `CreateFolder=true`）。兼容旧参数名 `FolderPath`。 |
| `FileNameTemplate` | `string` | `"NG_{yyyyMMdd_HHmmss}_{Guid}.jpg"` | 合法文件名 | 文件名模板。支持 `{timestamp}`、`{date}`、`{time}`、`{year}`、`{month}`、`{day}`、`{Guid}` 占位符。兼容旧参数名 `FileName`。 |
| `Quality` | `int` | `90` | [1, 100] | JPEG 压缩质量。仅对 `.jpg`/`.jpeg` 格式生效。兼容旧参数名 `JpegQuality`。 |

## 输入/输出端口 / Input/Output Ports
### 输入 / Inputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 必填 (Required) | 说明 (Description) |
|------|------|------|------|------|
| `Image` | 图像 | `Image` | Yes | 待保存的输入图像。 |

### 输出 / Outputs
| 名称 (Name) | 显示名 (DisplayName) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|------|
| `FilePath` | 保存路径 | `String` | 实际写入的完整文件路径。 |
| `IsSuccess` | 是否成功 | `Boolean` | 保存是否成功。 |

### 运行时附加输出 / Runtime Additional Outputs
| 名称 (Name) | 数据类型 (DataType) | 说明 (Description) |
|------|------|------|
| `Success` | `Boolean` | 与 `IsSuccess` 相同，兼容旧版输出契约。 |
| `FileName` | `String` | 实际保存的文件名（含序号后缀）。 |
| `Format` | `String` | 实际使用的图像格式（`png`/`jpg`/`bmp`）。 |
| `Width` | `Integer` | 保存图像宽度。 |
| `Height` | `Integer` | 保存图像高度。 |
| `FileSize` | `Long` | 保存后的文件大小（字节）。 |

## 性能特征 / Performance
| 指标 (Metric) | 值 (Value) |
|------|------|
| 时间复杂度 (Time Complexity) | `O(W*H*C)`，其中 `W`、`H` 为图像宽高，`C` 为通道数。编码时间取决于格式：PNG 最慢（无损压缩），JPEG 中等，BMP 最快（无压缩）。 |
| 典型耗时 (Typical Latency) | 1920x1080 彩色图：PNG 约 50-150ms，JPEG 约 10-40ms，BMP 约 5-15ms（取决于磁盘 I/O 速度）。 |
| 内存特征 (Memory Profile) | 峰值内存约为输入图像大小 + 编码缓冲区。BMP 无额外压缩缓冲；PNG/JPEG 需要额外编码缓冲。 |

## 适用场景 / Use Cases
- **适合 (Suitable)**：NG 图像存档，将不合格检测图像保存到本地或网络磁盘。
- **适合 (Suitable)**：检测结果保存，为每张检测图生成带时间戳的唯一文件名。
- **适合 (Suitable)**：调试图像输出，在流程调试阶段将中间结果落盘查看。
- **不适合 (Not Suitable)**：高吞吐实时流保存场景，磁盘 I/O 可能成为瓶颈。
- **不适合 (Not Suitable)**：远程存储（S3、FTP 等），当前仅支持本地文件系统。

## 已知限制 / Known Limitations
1. 仅支持本地文件系统写入，不支持 S3、FTP 等远程存储。
2. 文件名模板占位符不支持自定义格式字符串（如 `{timestamp:yyyyMMdd}`），仅支持预定义的固定格式。
3. 当 `Overwrite=false` 且同名文件大量存在时，序号递增循环可能产生轻微性能开销。
4. PNG 编码未提供压缩级别参数控制，使用 OpenCV 默认压缩级别。
5. 旧参数名 `FolderPath`、`FileName`、`JpegQuality` 仍受支持但已标记为 legacy，新流程应使用 `Directory`、`FileNameTemplate`、`Quality`。

## 变更记录 / Changelog
| 版本 (Version) | 日期 (Date) | 变更内容 (Changes) |
|------|------|----------|
| 2.0.0 | 2026-05-04 | 基于源码全面重写：补充文件名模板占位符机制、格式自动推断、冲突序号避让、文件名安全校验、双名参数兼容层等核心实现细节；重写算法原理、实现策略、API 调用链、参数语义、运行时输出与适用场景 |
| 1.0.2 | 2026-03-14 | 第二轮基于源码深化实现行为、性能与限制说明 |
| 1.0.1 | 2026-03-14 | 基于源码补充算法原理、调用链、参数语义、适用场景与已知限制 |
| 1.0.0 | 2026-03-03 | 自动生成文档骨架 / Generated skeleton |

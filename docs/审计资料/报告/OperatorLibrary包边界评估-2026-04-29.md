---
title: "OperatorLibrary 包边界评估"
doc_type: "audit-evidence"
status: "active"
created: "2026-04-29"
updated: "2026-04-29"
---

# OperatorLibrary 包边界评估

## 当前包边界

`Acme.OperatorLibrary` 当前是兼容全量包，目标框架为 `net8.0`，通过 MSBuild linked compile items 复用主工程的算子、Core contracts、PLC、图像处理、标定、点云和部分 AI runtime 代码。

当前直接依赖族：

- Vision: OpenCvSharp4、OpenCvSharp4.runtime.win、System.Drawing.Common、ZXing.Net。
- AI: Microsoft.ML.OnnxRuntime、PaddleOCRSharp。
- Communication: NModbus、S7NetPlus、System.IO.Ports。
- Database: Microsoft.Data.Sqlite、Microsoft.Data.SqlClient、MySqlConnector。
- Runtime abstractions: Microsoft.Extensions.DependencyInjection.Abstractions、Microsoft.Extensions.Logging.Abstractions。

## 消费者压力

| 压力项 | 影响 | 当前策略 |
|---|---|---|
| 原生运行时体积 | OpenCV、ONNX、PaddleOCR、相机/图像 SDK 相关依赖会放大安装体积 | 保留全量包，消费端用 smoke test 验证部署 |
| 平台限定 | 当前包包含 Windows 视觉/串口/PLC 依赖，不是纯跨平台 abstractions 包 | README 标注部署前确认依赖 |
| AI/DB/PLC 非必需依赖 | 只消费图像处理时仍会 restore 全量依赖 | P2 后续评估拆包 |
| 版本耦合 | linked source 与主工程实现共演进 | 保留 SourceRevisionId/RepositoryCommit 元数据 |

## 建议拆分方案

| 包 | 内容 | 迁移优先级 |
|---|---|---|
| `Acme.OperatorLibrary.Abstractions` | Operator metadata、Core enums、输入输出契约、模块索引 | 高 |
| `Acme.OperatorLibrary.VisionCore` | OpenCV 图像处理、测量、匹配、标定基础能力 | 高 |
| `Acme.OperatorLibrary.AI` | DeepLearning、OCR、ONNX/Paddle 相关算子 | 中 |
| `Acme.OperatorLibrary.Communication` | PLC、Modbus、串口通信算子 | 中 |
| `Acme.OperatorLibrary` | 兼容全量包，聚合或保留当前行为 | 必须保留 |

## 版本策略

- `1.x`：保留当前 `Acme.OperatorLibrary` 全量包兼容性。
- `1.x` 后续 minor：新增拆分包时不移除全量包 API。
- `2.0`：只有在消费方迁移验证完成后，才考虑重新定义默认包边界。

## 验证

本轮已保留当前 `pack + smoke` 流程，不改变包名、版本前缀和 smoke test 项目入口。

```powershell
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release
```

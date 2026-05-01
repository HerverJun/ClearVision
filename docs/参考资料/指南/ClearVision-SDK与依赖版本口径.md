---
title: "ClearVision SDK 与依赖版本口径"
doc_type: "guide"
status: "active"
created: "2026-04-29"
updated: "2026-04-29"
---

# ClearVision SDK 与依赖版本口径

## SDK 决策

本仓库固定使用 .NET SDK `10.0.101`。

`global.json`：

```json
{
  "sdk": {
    "version": "10.0.101",
    "rollForward": "latestFeature"
  }
}
```

口径说明：

- 主应用和算子库仍以 `net8.0` / `net8.0-windows` 为目标框架。
- SDK 使用 `10.0.101` 是为了让本机、CI、发布验证和当前 `Microsoft.Extensions.* 10.0.0` 依赖口径一致。
- `rollForward: latestFeature` 允许同一 major/minor 下的 feature band 修复，但不会自动跨 major 升级。
- CI 使用 `actions/setup-dotnet@v4` 的 `global-json-file: global.json`，因此应与本地 `dotnet --info` 的 global.json 解析结果一致。

## Microsoft.Extensions 口径

直接引用的 `Microsoft.Extensions.*` 包统一为 `10.0.0`：

- `Acme.Product.Application`
- `Acme.Product.Desktop`
- `Acme.Product.Infrastructure`
- `Acme.PlcComm`
- `Acme.OperatorLibrary`

保留 `Microsoft.EntityFrameworkCore 8.0.0`、`Microsoft.AspNetCore.* 2.2.x` 等非 `Microsoft.Extensions.*` 历史依赖不在本轮强行升级；它们需要单独做兼容性回归，不能和 SDK 口径冻结混在一次整改里。

## OperatorLibrary 消费者兼容性

`Acme.OperatorLibrary` 当前仍发布为 `net8.0` 包，消费方需要：

- 使用兼容 `net8.0` 的运行时。
- 接受包的直接依赖，包括 `Microsoft.Extensions.DependencyInjection.Abstractions 10.0.0` 与 `Microsoft.Extensions.Logging.Abstractions 10.0.0`。
- 对 OpenCvSharp、PaddleOCRSharp、ONNX Runtime、串口/PLC/数据库相关依赖做部署侧确认。

当前暂不拆包。包拆分评估另见 P2-3，建议方向是保留 `Acme.OperatorLibrary` 全量兼容包，同时评估 `Abstractions`、`VisionCore`、`AI`、`Communication` 子包。

## 验证命令

```powershell
dotnet --info
dotnet build Acme.Product/Acme.Product.sln --configuration Debug
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release
```

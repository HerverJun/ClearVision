---
title: "ClearVision SDK 与依赖版本口径"
doc_type: "guide"
status: "active"
created: "2026-04-29"
updated: "2026-05-09"
---

# ClearVision SDK 与依赖版本口径

## SDK 事实来源

本仓库以根目录 [`global.json`](../../../global.json) 为 SDK 版本事实来源。当前配置为：

```json
{
  "sdk": {
    "version": "9.0.300",
    "rollForward": "latestMajor"
  }
}
```

本机 `dotnet --version` 可能解析为已安装的兼容版本，例如 `9.0.304`。记录验证环境时请同时保留：

- `global.json` 中的固定版本；
- `dotnet --version` 的实际解析结果；
- `dotnet --info` 中的 SDK 列表和 global.json 路径。

## 版本口径

- 主应用和算子库仍以 `net8.0` / `net8.0-windows` 为目标框架。
- SDK 口径不等同于目标框架口径；升级 SDK 前需要单独确认构建、测试、发布和 NuGet 依赖兼容性。
- 若决定切换到 .NET SDK 10.x，应同步更新 `global.json`、README、构建规则文档和 CI 配置，并跑完整质量门禁。
- `Acme.Product/Directory.Build.targets` 中的 SDK `10.0.101` 条件是 Windows roll-forward 兼容保护；当前常规事实来源仍是根目录 `global.json`。

## Microsoft.Extensions 口径

直接引用的 `Microsoft.Extensions.*` 包可能与 SDK 版本不是同一条版本线。调整这些依赖时，应按项目实际兼容性和测试结果处理，不要仅因为 SDK 版本变化而批量改包。

保留 `Microsoft.EntityFrameworkCore 8.x`、`Microsoft.AspNetCore.* 2.2.x` 等历史依赖时，需要单独做兼容性回归。

## 验证命令

```powershell
dotnet --version
dotnet --info
dotnet build Acme.Product/Acme.Product.sln --configuration Debug
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release
```

.NET 测试请优先使用仓库串行 runner 或固定回归脚本，避免同一个 `.csproj` 并发执行：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName OperatorContractReconciliationTests `
  -NoBuild `
  -NoRestore
```

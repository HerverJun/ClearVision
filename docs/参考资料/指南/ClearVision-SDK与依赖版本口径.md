---
title: "ClearVision SDK 与依赖版本口径"
doc_type: "guide"
status: "active"
created: "2026-04-29"
updated: "2026-05-09"
---

# ClearVision SDK 与依赖版本口径

## SDK 事实来源

仓库根目录 [`global.json`](../../../global.json) 是 SDK 事实来源：

```json
{
  "sdk": {
    "version": "9.0.300",
    "rollForward": "latestFeature"
  }
}
```

本机 `dotnet --version` 可能解析到同一 feature band 内的更高补丁版本。记录验证环境时请同时保留：

- `global.json` 固定版本；
- `dotnet --version` 实际解析结果；
- `dotnet --info` 中的 SDK 列表和 global.json 路径。

## 版本口径

- 主应用和算子库仍以 `net8.0` / `net8.0-windows` 为目标框架。
- SDK 版本不等于目标框架版本；升级 SDK 前必须单独验证 restore、build、test、pack 和发布。
- 当前不跨大版本 roll-forward；历史 SDK 10 `csc` workaround 已删除。
- 若未来切换到 .NET SDK 10.x，应同步更新 `global.json`、README、CI 和依赖治理文档，并跑完整质量门。

## 依赖口径

- Product 与 OperatorLibrary 使用 `Directory.Packages.props` 做中央包版本管理。
- OperatorLibrary 使用 `packages.lock.json`，CI/release 使用 locked restore。
- `Microsoft.Extensions.*` 在 OperatorLibrary 中保持 .NET 8 lane：`DependencyInjection.Abstractions 8.0.2`、`Logging.Abstractions 8.0.3`。
- 旧 ASP.NET Core 2.2 package references 已从 active csproj 中移除；历史 build artifacts 或审计归档中的旧记录不代表当前依赖口径。

## 验证命令

```powershell
dotnet --version
dotnet --info
dotnet restore Acme.Product/Acme.Product.sln
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
dotnet restore Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --locked-mode
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore
```

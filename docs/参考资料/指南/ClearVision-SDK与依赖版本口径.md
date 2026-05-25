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
    "rollForward": "disable"
  }
}
```

本机裸 `dotnet` 可能因为 PATH 顺序命中其他安装位置。仓库脚本 [`scripts/dotnet.ps1`](../../../scripts/dotnet.ps1) 会读取 `global.json` 并选择包含 SDK `9.0.300` 的 dotnet host；缺失时可用 `-InstallIfMissing` 将 SDK `9.0.300` 和 .NET 8 Core / ASP.NET / WindowsDesktop runtime 安装到用户目录。记录验证环境时请同时保留：

- `global.json` 固定版本；
- `.\scripts\dotnet.ps1 --version` 实际解析结果；
- `.\scripts\dotnet.ps1 -PrintPath` 实际 dotnet host 路径。

## 版本口径

- 主应用和算子库仍以 `net8.0` / `net8.0-windows` 为目标框架。
- SDK 版本不等于目标框架版本；升级 SDK 前必须单独验证 restore、build、test、pack 和发布。
- 当前不做 SDK roll-forward；历史 SDK 10 `csc` workaround 已删除。
- 若未来切换到 .NET SDK 10.x，应同步更新 `global.json`、README、CI 和依赖治理文档，并跑完整质量门。

## 依赖口径

- Product 与 OperatorLibrary 使用 `Directory.Packages.props` 做中央包版本管理。
- OperatorLibrary 使用 `packages.lock.json`，CI/release 使用 locked restore。
- `Microsoft.Extensions.*` 在 OperatorLibrary 中保持 .NET 8 lane：`DependencyInjection.Abstractions 8.0.2`、`Logging.Abstractions 8.0.3`。
- 旧 ASP.NET Core 2.2 package references 已从 active csproj 中移除；历史 build artifacts 或审计归档中的旧记录不代表当前依赖口径。

## 验证命令

```powershell
.\scripts\dotnet.ps1 --version
.\scripts\dotnet.ps1 -PrintPath
.\scripts\dotnet.ps1 restore Acme.Product/Acme.Product.sln --locked-mode
.\scripts\dotnet.ps1 build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
.\scripts\dotnet.ps1 restore Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --locked-mode
.\scripts\dotnet.ps1 build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore
```

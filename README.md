# ClearVision

ClearVision 是一个面向工业视觉检测的 .NET 8 项目，核心由桌面端流程编排、算子运行时、独立算子库打包、质量评测与算子文档体系组成。

当前仓库不是一个单纯的 OpenCV 示例集合，而是把图像预处理、检测、测量、标定、匹配定位、AI 推理、通信和流程控制统一到 Operator 元数据、端口、参数和质量证据框架里。

## 当前状态

- 主应用：`Acme.Product`，Windows 桌面应用，基于 `net8.0-windows`、WinForms、WebView2 与本地后端端点。
- 算子库：`Acme.OperatorLibrary`，以 NuGet 包形态封装共享算子实现，当前包版本前缀为 `1.0.2`，包元数据使用 MIT license expression。
- 算子规模：正式口径为 **155 个算子**；运行时兼容元数据包含 4 个 legacy alias，因此运行时可见口径为 159。
- 文档口径：当前活跃算子文档位于 [`docs/算子资料/`](./docs/算子资料/)，仓库根 [`算子资料/`](./算子资料/) 是兼容镜像。
- 质量治理：`docs/active/ClearVision_Quality_Flywheel_TODO_v0.2.md` 已记录 C 级清零、核心 golden/contract baseline 扩展、Calibration synthetic baseline 等阶段性进展；`quality/evals/reports/operator_quality_matrix.md` 是质量矩阵入口。

## 阅读入口

- [项目总览](./docs/项目总览.md)：当前进度、代码结构、质量证据与风险边界。
- [文档导航](./docs/导航.md)：仓库文档区总入口。
- [算子资料导航](./docs/算子资料/导航.md)：算子目录、名片、手册、质量说明的入口。
- [算子目录](./docs/算子资料/算子目录.md)：155 个正式算子的分类索引。
- [算子名片索引](./docs/算子资料/算子名片/CATALOG.md)：逐算子卡片入口。
- [算子文档现状对齐说明](./docs/算子资料/算子文档现状对齐说明-2026-04.md)：代码、目录、名片、手册之间的口径说明。
- [算子库 README](./Acme.OperatorLibrary/README.md)：独立算子库打包与验收范围。

## 常用命令

```powershell
dotnet restore Acme.Product/Acme.Product.sln
dotnet build Acme.Product/Acme.Product.sln --configuration Debug
dotnet run --project scripts/OperatorDocGenerator/OperatorDocGenerator.csproj -- .
```

测试执行请优先使用仓库脚本，例如 `./scripts/run-dotnet-test-serial.ps1` 或固定回归脚本，避免同一 `.csproj` 同时启动多个 `dotnet test`。

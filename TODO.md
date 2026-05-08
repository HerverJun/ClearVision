---
title: "ClearVision 全面提升 TODO"
doc_type: "task-list"
status: "active"
topic: "project-improvement"
created: "2026-05-09"
updated: "2026-05-09"
scope: "基于现有 ClearVision 功能的稳定性、质量、现场化、验证与发布提升；不发散新增产品功能"
---

# ClearVision 全面提升 TODO

> 本清单由 5 个 GPT-5.5 子代理并行只读审阅后汇总：核心后端/运行时、桌面 UI/API、算子库与质量矩阵、CI/发布/安全、现场集成/PLC/Station。
> 目标不是扩张新功能，而是把已有功能从“能跑”推进到“可证明、可交付、可维护、现场可诊断”。

## 执行原则

- P0 先处理数据丢失、误导性质量声明、权限边界、CI 空跑和现场不可读问题。
- Station 本地检测自治优先；Studio 离线不能阻塞 Station 检测。
- StationSync 不传图片，不把大文件塞进 SignalR；结果只传摘要，包下载走 HTTP。
- 所有测试优先使用仓库脚本，尤其是 `& "./scripts/run-dotnet-test-serial.ps1" ...`；不要并行跑同一个 `.csproj` 的 `dotnet test`。
- 发布材料必须区分：功能可用、synthetic/public dataset evidence、field-substitute replay、真实产线签核。

## P0：立即止血

### P0-1 结果持久化失败不能丢批次

- [ ] 为 `InspectionResultBackgroundService` 增加失败重试、死信/本地 JSONL spool 和健康告警。
- [ ] `SaveBatchAsync` 失败时不得清空 batch；重启后应能回放未持久化结果。
- [ ] 增加仓储写入失败、SQLite 短暂锁、进程重启后的回放测试。
- 依据：`Acme.Product/src/Acme.Product.Infrastructure/Services/InspectionResultBackgroundService.cs`

### P0-2 StationSync 结果队列容量真正生效

- [ ] 将 `StationSyncHostedService` 的 result ingress 从 unbounded channel 改为 bounded/backpressure 策略。
- [ ] 队列满时保护 Runtime 回调延迟：记录 drop/backpressure 计数，不阻塞检测主路径。
- [ ] 将 dropped result summaries、backpressure events、spool trimming range 暴露到 health/log/alarm。
- [ ] 更新 `docs/runtime/station-studio-sync.md`，让代码、文档和 SOP 口径一致。
- 依据：`Acme.Product/src/Acme.Product.Station/Sync/StationSyncHostedService.cs`、`StationSpoolStore.cs`

### P0-3 CI 不再绕过串行测试 runner

- [ ] `.github/workflows/ci.yml` 中 Product/Desktop/Operator smoke 的直接 `dotnet test` 改为 `scripts/run-dotnet-test-serial.ps1`。
- [ ] 为 CI TRX 增加 `MinimumTotalTests` / `MinimumPassedTests`，防止空跑。
- [ ] 保留 coverage artifact，同时确保失败时上传 TRX。
- 依据：`.github/workflows/ci.yml`、`scripts/run-dotnet-test-serial.ps1`

### P0-4 OperatorLibrary locked restore 进入 CI/release

- [ ] OperatorLibrary CI/release restore 使用 `--locked-mode`。
- [ ] 明确 `packages.lock.json` 的更新流程：依赖升级 PR 必须包含 lock diff。
- [ ] `pack.ps1 -RunSmokeTest` 与 CI 包 smoke 使用同一包版本和 locked restore 口径。
- 依据：`Acme.OperatorLibrary/Acme.OperatorLibrary.csproj`、`Acme.OperatorLibrary/packages.lock.json`

### P0-5 Station 命令、部署、测试包端点补权限

- [ ] `POST /api/stations/{stationId}/commands` 增加管理员或指定角色校验。
- [ ] `POST /api/stations/{stationId}/deploy-package` 增加同级角色校验与审计。
- [ ] `POST /api/station-packages/test` 不应对普通登录用户开放。
- [ ] `AuthMiddleware` 当前把 session 放在 `HttpContext.Items["CurrentUser"]`，Station endpoints 不要继续依赖 `context.User?.Identity?.Name` 的空值。
- 依据：`Acme.Product/src/Acme.Product.Desktop/Endpoints/StationEndpoints.cs`、`AuthMiddleware.cs`

### P0-6 结果面板移除假高级分析

- [ ] 删除或禁用 `resultPanel.js` 中 mock CPK、MTBF、缺陷聚类等占位数据。
- [ ] 已接后端的数据只走现有 `/api/analysis/statistics|defect-distribution|trend|report/{projectId}`。
- [ ] 未接通的高级分析按钮显示“暂无数据/未接入”，不得展示固定样例值。
- 依据：`Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/results/resultPanel.js`

### P0-7 算子正式口径对齐

- [ ] 处理 `FrameChangeTrigger` 已实现但未进入 155 正式算子目录/名片/质量矩阵的问题。
- [ ] 如果正式发布：补名片、目录、版本记录、质量矩阵、suite evidence。
- [ ] 如果仅内部使用：在文档生成器和质量矩阵中显式排除，并说明原因。
- 依据：`Acme.Product/src/Acme.Product.Infrastructure/Operators/FrameChangeTriggerOperator.cs`、`docs/算子资料/算子目录.md`

### P0-8 工业验证声明设硬门禁

- [ ] 发布材料禁止把 public dataset、semi-synthetic、field-substitute replay 表述为真实产线签核。
- [ ] Core20 `accepted=0` 或 real industrial validation complete = 0 时，不得宣称工业验证闭环。
- [ ] README、项目总览、质量矩阵、发布说明统一使用“功能可用但未完成真实产线签核”的口径。
- 依据：`quality/evals/reports/operator_quality_matrix.md`、`QualityFlywheel_core20_proof_baseline.md`

### P0-9 DeepLearning real-model gate 改名或提门槛

- [ ] `AP50/Precision/Recall = 0` 但 `Accepted=True` 的 COCO real-model 报告只能作为推理链路 smoke。
- [ ] 如果要作为模型精度证据，设置非零指标门槛并记录模型 hash、数据集版本、标签契约。
- [ ] 更新质量矩阵中的 precision claim，避免将 smoke 误读为模型质量验收。
- 依据：`quality/evals/reports/DeepLearning_coco_real_model_baseline.md`

### P0-10 修复现场可见乱码

- [ ] 修复 Station Monitor、PLC endpoint、runtime/log、部署 bat/README、根目录规范文档中的 mojibake。
- [ ] 统一脚本生成文本编码；现场 bat 可保留 ASCII，面向人读的 md/txt 使用 UTF-8。
- [ ] 增加轻量编码扫描脚本，至少检查 `�`、常见 mojibake 片段和不可读中文。
- 依据：`Acme.Product/src/Acme.Product.Desktop/wwwroot/src/features/stations/stationMonitorView.js`、`scripts/package-portable-deployment.ps1`

### P0-11 PLC 虚拟联调入口收口

- [ ] 用现有 start/test virtual PLC 脚本串起 Modbus、MC、FINS opt-in .NET 测试。
- [ ] `run-tests-plc-regression.ps1` 增加明确的 virtual PLC regression 模式，避免 gate 只跑到非 socket 子集。
- [ ] 产出一份联调证据：服务启动、点位读写、握手、错误路径、测试结果。
- 依据：`tools/virtual-plc/*`、`scripts/run-tests-plc-regression.ps1`

## P1：稳定化与现场闭环

### P1-1 Project Flow 持久化来源收敛

- [ ] 明确 DB `Project.Flow` 与 `App_Data/ProjectFlows/*.json` 的优先级。
- [ ] `JsonFileProjectFlowStorage` 改为 temp + replace 原子写。
- [ ] 增加 flow version/hash，读取坏 JSON 时进入显式错误或 last-good 恢复，不静默 fallback。
- [ ] 增加并发保存测试和崩溃中断写入测试。
- 依据：`ProjectService.cs`、`JsonFileProjectFlowStorage.cs`

### P1-2 变量上下文引入执行作用域

- [ ] `IVariableContext` 不再以全局 singleton 共享所有项目/会话变量。
- [ ] 为 project/session/flow/run 提供作用域，明确 preview、single run、realtime run 之间的隔离规则。
- [ ] `CycleCount` 从全局计数改为执行上下文内计数。
- 依据：`VisionRuntimeServiceCollectionExtensions.cs`、`FlowExecutionService.cs`、`IVariableContext.cs`

### P1-3 Runtime package 导出路径受控

- [ ] `TargetRootDirectory` 限制到受控目录，例如 `.tmp/publish-check/`、用户选择的导出目录或配置白名单。
- [ ] 导出端点补角色校验、路径审计和拒绝越界错误。
- [ ] 对任意绝对路径、相对逃逸、系统目录写入增加测试。
- 依据：`ApiEndpoints.cs`、`RuntimePackageExporter.cs`

### P1-4 SSE/事件总线背压与 replay 明确化

- [ ] Inspection SSE 每连接 channel 改 bounded，并定义慢消费者策略。
- [ ] `InMemoryEventStore` 的每项目 100 条 replay 容量变为配置项或文档化约束。
- [ ] 暴露 event dropped/replayed/slow consumer 指标。
- [ ] 高频连续检测下验证 WebView 不堆积内存。
- 依据：`InspectionEventEndpoints.cs`、`InMemoryEventStore.cs`

### P1-5 Inspection 错误语义分层

- [ ] 区分业务 NG、流程校验错误、图像采集错误、系统异常、持久化失败。
- [ ] API 返回体保留用户可读结果，同时给调用方稳定 error code。
- [ ] 单次检测不应所有异常都被包装成 `200 OK + Error` 而没有系统级信号。
- 依据：`InspectionService.cs`、`ApiEndpoints.cs`

### P1-6 配置服务并发与快照边界

- [ ] `JsonConfigurationService` 增加读写锁。
- [ ] `GetCurrent()` 返回 clone/snapshot，避免调用方修改缓存对象。
- [ ] 保存使用 temp + replace，并记录配置 revision。
- 依据：`JsonConfigurationService.cs`

### P1-7 数据库 schema 演进统一

- [ ] 减少 `Program.cs` 手写 DDL 与 EF model 双维护。
- [ ] 为 Station sync 表选择 EF migration 或明确 migration-light 机制，不能长期两套真相源。
- [ ] 清理或标记 `Persistence/AppDbContext` 与实际 `Data/VisionDbContext` 的边界。
- 依据：`Program.cs`、`VisionDbContext.cs`、`AppDbContext.cs`

### P1-8 统计查询下推数据库

- [ ] `InspectionResultRepository.GetStatisticsAsync` 改为数据库端聚合。
- [ ] 增加大数据量分页、索引和日期范围查询验证。
- [ ] 结果面板加载历史时避免整表拉取后内存筛选。
- 依据：`InspectionResultRepository.cs`

### P1-9 InspectionPanel legacy 分支清理

- [ ] 删除或隔离 `_legacyHandleRunSingleDuplicate*`、`_legacyHandleRunContinuousDuplicate*`、`_legacyHandleStopDuplicate*` 等重复路径。
- [ ] 保留单一运行/停止/结果处理路径。
- [ ] 对连续 NG 停止、SSE 回写、运行保护补 UI 单测或 Playwright smoke。
- 依据：`wwwroot/src/features/inspection/inspectionPanel.js`

### P1-10 实时结果通道统一

- [ ] 结果页复用现有 inspection SSE/history，不再保留未实现 `/hub/inspection-results` 占位。
- [ ] `inspectionController.js`、`InspectionEventEndpoints.cs`、`resultPanel.js` 使用同一实时结果语义。
- [ ] 断线重连、Last-Event-ID、历史补页行为写入前端通信说明。
- 依据：`resultPanel.js`、`inspectionController.js`、`InspectionEventEndpoints.cs`

### P1-11 FlowData 契约文档化

- [ ] 写一份“前端序列化 -> 后端 ToEntity -> Runtime export”的 flow contract 文档。
- [ ] 收敛 `CanvasFlowDataDto`、`FlowDataDto`、`UpdateFlowRequest` 的 legacy shape。
- [ ] 新增 contract test 固定 nodes/operators、ports、parameters 的兼容矩阵。
- 依据：`FlowEntityMapper.cs`、`CanvasFlowDataModels.cs`、`flowCanvas.js`

### P1-12 Station health 接入 PLC 状态

- [ ] 将现有 PLC connection state/连接池快照映射到 `StationHealthSnapshotDto.PlcStatusSummary`。
- [ ] 区分 `NotConfigured`、`Disconnected`、`Connecting`、`Ready`、`Error`。
- [ ] Station Monitor 显示 PLC 不可用原因。
- 依据：`StationSyncHostedService.cs`、`PlcCommunicationOperatorBase.cs`

### P1-13 Alpha trial 脚本加预检与摘要

- [ ] `run-station-alpha-trial.ps1` 增加 Studio/Station/Simulator 连通性预检。
- [ ] 运行开始即确认能采到心跳/health/result 样本，避免长跑后才发现无效。
- [ ] 结束输出关键证据路径、站点数、结果数、drop/backpressure/spool 摘要。
- 依据：`scripts/run-station-alpha-trial.ps1`

### P1-14 线序场景包 checksum 与发布检查

- [ ] 为可提交资产补齐 `ChecksumSha256`：template、rules、labels、samples manifest。
- [ ] 外部模型保持不入库时，在现场包 manifest 或部署包 manifest 中补 hash。
- [ ] 视频流模板导入/发布前检查 `parametersNeedingReview`，避免 ROI `0,0,0,0` 未调直接上现场。
- 依据：`线序检测/scenario-package-wire-sequence/manifest.json`

### P1-15 MC/FINS 虚拟 PLC 测试扩展

- [ ] 从 connect/ping 扩展到算子读写路径。
- [ ] 覆盖寄存器读写、错误码、断连恢复。
- [ ] 与 Modbus virtual PLC regression 使用同一证据目录。
- 依据：`tools/virtual-plc/mc-fins/`、`VirtualMcFinsPlcConnectionTests.cs`

### P1-16 OperatorModuleCatalog 口径收敛

- [ ] 不再直接 `Enum.GetValues<OperatorType>()` 全量曝光包侧模块。
- [ ] 对齐 `OperatorTypeAliasResolver`、正式 catalog 或 `OperatorMetadataScanner`。
- [ ] legacy alias 和未纳入正式质量矩阵的算子必须显式标注。
- 依据：`Acme.OperatorLibrary/src/Acme.OperatorLibrary.Modules/OperatorModuleCatalog.cs`

### P1-17 包级代表性验收扩展

- [ ] `Acme.OperatorLibrary` smoke/acceptance 增加匹配、Region/Morphology、频域、SemanticSegmentation、AnomalyDetection、SurfaceDefectDetection 的最小路径。
- [ ] 每类至少覆盖正常、参数错误或资源缺失、输出契约。
- [ ] 维持 smoke 可快速运行，重数据集验证放质量 suite。
- 依据：`Acme.OperatorLibrary/tests/Acme.OperatorLibrary.SmokeTests/RepresentativeOperatorAcceptanceTests.cs`

### P1-18 质量等级拆成两条线

- [ ] 将“功能成熟度”和“证据成熟度”拆开展示。
- [ ] 保留 155 全 A 的功能口径时，同时突出 Contract/Golden/Dataset/Field replay 缺口。
- [ ] README/项目总览不要只展示单一 A 级数字。
- 依据：`quality/evals/reports/operator_quality_matrix.md`

### P1-19 AI/模型 release gate 附件

- [ ] DeepLearning、SemanticSegmentation、AnomalyDetection、SurfaceDefectDetection 等模型相关算子补 gate 附件。
- [ ] 附件字段：model sha256、license、labels contract、provider fallback、dataset version、hardware profile、report ID。
- [ ] 模型文件外部交付时，必须有 manifest 绑定。
- 依据：`models/README.md`、`models/model_catalog.json`

### P1-20 SDK 与构建版本口径收紧

- [ ] 评估 `global.json` 的 `rollForward: latestMajor` 是否应改为更保守策略。
- [ ] 消化或删除 SDK 10 csc workaround 的长期依赖。
- [ ] 文档统一 `.NET SDK 9.0.300` 与实际 CI runner 解析结果。
- 依据：`global.json`、`Acme.Product/Directory.Build.targets`

### P1-21 工业 gate 进入 CI

- [ ] `run-operator-library-industrial-gate.ps1` 接入 `workflow_dispatch`、nightly 或 release gate。
- [ ] 上传 `summary.json/.md`、TRX、performance reports。
- [ ] PR gate 只跑 quick profile，release/nightly 跑 industrial profile。
- 依据：`scripts/run-operator-library-industrial-gate.ps1`

### P1-22 UI 测试补齐已有 npm 脚本

- [ ] CI 除 `npx playwright test` 外，补跑 `npm run test:unit`。
- [ ] `test:preview-smoke` 放入 PR quick 或 nightly，并明确失败 artifact。
- [ ] Station Monitor 前端增加最小渲染和 SSE event apply 单测。
- 依据：`Acme.Product/tests/Acme.Product.UI.Tests/package.json`

### P1-23 发布包口径统一

- [ ] 区分 CI desktop zip 与现场 portable package。
- [ ] 如果 release 面向现场交付，纳入 `scripts/package-portable-deployment.ps1` 的产物或等效流程。
- [ ] `README-site-deploy.txt`、bat 启动名、依赖安装说明与 CI release artifact 对齐。
- 依据：`.github/workflows/ci.yml`、`scripts/package-portable-deployment.ps1`

## P2：维护性、文档与长期治理

### P2-1 Runtime 依赖边界瘦身

- [ ] 逐步减少 `Acme.Product.Runtime` 对 `Application` / `Infrastructure` 的直接引用。
- [ ] 明确 Runtime 的纯运行依赖面和 Desktop/Station 宿主依赖面。
- [ ] 用 architecture guard 防止 Runtime 引入 WebView2/Kestrel/wwwroot/Desktop。
- 依据：`Acme.Product/src/Acme.Product.Runtime/Acme.Product.Runtime.csproj`

### P2-2 前端全局变量退场

- [ ] 新交互优先走 `serviceRegistry` / `eventBus`。
- [ ] 对 `legacyGlobals.js` 中暴露的对象逐个标注保留原因和替换路径。
- [ ] 迁移完成后减少 `window.*` 状态串扰。
- 依据：`wwwroot/src/core/app/legacyGlobals.js`、`wwwroot/src/app.js`

### P2-3 前端调试日志挂 debug flag

- [ ] 无条件 `console.log/warn` 改为统一 debug logger。
- [ ] 沿用 `window.__FLOW_CANVAS_DEBUG__` 或扩展全局调试开关。
- [ ] 生产 WebView 控制台只保留错误和必要告警。
- 依据：`flowEditorInteraction.js`、`inspectionPanel.js`、`flowCanvas.js`

### P2-4 Dataview 与文档入口修复

- [ ] 修复 `docs/Dataview工作台.md`、Studio/Station TODO、Runtime 边界文档编码与链接。
- [ ] `docs/README.md` 指向新的根 `TODO.md` 或当前活跃计划入口。
- [ ] 归档旧计划时同步更新 `docs/进行中/README.md`。
- 依据：`docs/Dataview工作台.md`、`docs/runtime/Desktop-Studio-Boundary.md`

### P2-5 现场证据包与临时产物约定

- [ ] 新增一页说明 `logs/`、`artifacts/`、`test_results/`、`.tmp/`、`.tmp/publish-check/` 的保留/清理/禁止提交边界。
- [ ] 脚本输出默认写入已忽略或约定目录。
- [ ] Alpha trial、PLC regression、industrial gate 统一证据目录命名。
- 依据：`.gitignore`、`AGENTS.md`、`docs/runtime/station-studio-sync.md`

### P2-6 Core20 名片人工复核

- [ ] 对 Core20 算子人工补齐算法边界、失败模式、典型输入输出和不可用场景。
- [ ] 减少模板化描述，保留生成器可重复生成的结构。
- [ ] 复核后更新质量矩阵 evidence source。
- 依据：`docs/算子资料/算子名片/`、`scripts/OperatorDocGenerator/Program.cs`

### P2-7 full155 quality suite 增加趋势和新增算子检查

- [ ] 新增算子必须有证据入口检查。
- [ ] 输出 Contract/Golden/Dataset/Field replay 缺口趋势。
- [ ] suite validate-only 之外增加“质量口径漂移”检查。
- 依据：`quality/evals/suites/full155_quality_suite.json`

### P2-8 SBOM 与依赖合规自动化

- [ ] Markdown SBOM 之外补 CycloneDX 或 SPDX artifact。
- [ ] release artifact 中同时包含 SBOM、THIRD-PARTY-NOTICES、dependency-report。
- [ ] 自动输出依赖漏洞和许可证待审清单，特别跟踪 `S7NetPlus` license metadata 缺口。
- 依据：`Acme.OperatorLibrary/SBOM.md`、`THIRD-PARTY-NOTICES.md`、`analyze-deps.ps1`

### P2-9 Coverage 从 artifact 变成趋势

- [ ] Product/Desktop coverage 先设置低门槛趋势监控，不急于高阈值阻塞。
- [ ] 把 coverage summary 写入 CI artifact 和 PR summary。
- [ ] 核心运行时、Station sync、端点权限相关代码优先纳入覆盖统计。
- 依据：`.github/workflows/ci.yml`

### P2-10 Product 依赖可复现构建

- [ ] 评估 Product solution 的 lock-file 策略。
- [ ] 考虑中央包版本管理，减少 csproj 依赖版本漂移。
- [ ] NuGet audit 与 locked restore 在 release 前至少 dry run。
- 依据：`Acme.Product/Acme.Product.sln`、`nuget.config`

### P2-11 虚拟 PLC 脚本易用性

- [ ] start/test virtual PLC 脚本使用 `Push-Location/Pop-Location`。
- [ ] 增加端口占用提示和已有进程提示。
- [ ] README 增加 Windows 本机与 Docker 两条路径的最小命令。
- 依据：`scripts/start-virtual-*-plc.ps1`、`tools/virtual-plc/*/README.md`

### P2-12 预提交安全动作

- [ ] `install-githooks.ps1` 或开发文档中加入 `scripts/scan-secrets.ps1` 提交前建议。
- [ ] 对误报提供 `<REDACTED>` 或测试 fixture 白名单说明。
- [ ] 不把 secret scan 变成难以使用的阻塞项，但保持 CI P0 阻断。
- 依据：`scripts/scan-secrets.ps1`、`.githooks/`

## 建议执行节奏

| 阶段 | 时间 | 目标 | 推荐交付 |
|---|---|---|---|
| M0 | 1-3 天 | P0 止血 | 持久化不丢、队列限流、CI runner、权限、假数据、口径声明 |
| M1 | 1-2 周 | Runtime/Station 稳定化 | Flow 原子写、变量作用域、SSE 背压、PLC health、alpha trial 证据 |
| M2 | 2-4 周 | 算子与发布证据 | 算子口径、包级验收、AI gate 附件、工业 gate CI、portable 发布 |
| M3 | 持续 | 文档与治理 | SBOM/SPDX、coverage 趋势、Core20 复核、schema 演进统一 |

## 推荐验证命令

```powershell
dotnet build Acme.Product/Acme.Product.sln --configuration Debug

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName RuntimeMvpTests,StationSyncContractsSerializationTests `
  -NoRestore `
  -Verbosity minimal

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Desktop.Tests/Acme.Product.Desktop.Tests.csproj" `
  -FullyQualifiedName StationRegistryServiceTests,StationEndpointsTests,StationIngressSecurityTests `
  -NoRestore `
  -Verbosity minimal

& "./scripts/run-tests-plc-regression.ps1" -NoBuild -NoRestore -Verbosity minimal

& "./scripts/run-operator-library-industrial-gate.ps1" -Profile quick -NoBuild -NoRestore

Push-Location Acme.Product/tests/Acme.Product.UI.Tests
npm run test:unit
npx playwright test
Pop-Location
```

## 关闭条件

- [ ] 所有 P0 项完成并有测试或审计证据。
- [ ] P1 至少完成 Runtime/Station 稳定性、Station 命令权限、Flow 持久化、CI/发布四条主线。
- [ ] 质量矩阵能同时展示功能成熟度和证据成熟度。
- [ ] 发布说明不再混淆 synthetic/public/field-substitute 与真实产线签核。
- [ ] 新增或更新的证据入口已链接到 `docs/README.md` 或 `docs/进行中/README.md`。

---
title: "ClearVision / ClearFrost 分体式 Runtime 现场化落地 TODO"
doc_type: "task-list"
status: "active"
topic: "runtime-station-field-delivery"
created: "2026-05-02"
updated: "2026-05-02"
sources:
  - "docs/进行中/当前计划/ClearVision-ClearFrost分体式Runtime现场化整合计划-2026-04-29.md"
  - "docs/进行中/当前计划/ClearVision-现场节点与业务流程改进计划-2026-04-29.md"
  - "docs/进行中/当前计划/ClearVision-审计整改TODO-2026-04-29.md"
  - "docs/项目总览.md"
  - "Acme.Product/src/Acme.Product.Desktop/Program.cs"
  - "Acme.Product/src/Acme.Product.Desktop/DependencyInjection.cs"
  - "Acme.Product/src/Acme.Product.Core/Services/IFlowExecutionService.cs"
  - "Acme.Product/src/Acme.Product.Infrastructure/Services/FlowExecutionService.cs"
  - "Acme.Product/src/Acme.Product.Infrastructure/Services/InspectionWorker.cs"
---

# ClearVision / ClearFrost 分体式 Runtime 现场化落地 TODO

> 设计口径：以工业现场稳定落地为金标准。  
> 本文不是重写上一份计划，而是把上一份计划转成更贴合当前仓库的可执行 TODO：先拆出能稳定运行的轻量 Station，再逐步吸收设备、触发、映射、统计等现场能力。

---

## 0. 顶层判断

当前最优路线不是让 `Acme.Product.Desktop` 继续承载所有现场运行职责，而是把产品边界稳定拆成：

```text
ClearVision Studio
  现有 Acme.Product.Desktop：WinForms + WebView2 + Kestrel + wwwroot
  职责：工程编辑、AI 生成、调试预览、模板/配方维护、Runtime Package 导出。

ClearVision Runtime
  新增共享运行核心：Package Loader / Validator / RuntimeHost / FlowAdapter / ResultNormalizer / 队列。
  职责：不启动 Web，不碰画布 UI，复用 FlowExecutionService 与现有算子执行器。

ClearVision Station
  新增轻量 Native WinForms：无 WebView2、无 wwwroot、无 Kestrel。
  职责：加载包、选择图片或目录、执行、显示 OK/NG/Error、日志、性能与最近结果。

Runtime Package
  Studio 导出，Station 导入。
  职责：把流程、运行 profile、质量验证、后续现场扩展 schema 封成可审计、可回滚、可迁移的离线包。
```

核心策略：

```text
第一轮：只证明 Station 能脱离 Desktop/WebView2/Kestrel，稳定执行同一套流程。
第二轮：接入现场工程包、设备中心、触发中心、结果映射、模型资产、运行台。
第三轮：再进入真实相机/PLC/协议构建/多工位统计/现场配方验收。
```

---

## 1. 当前项目事实

这些是 TODO 设计必须贴着走的现实边界：

| 事实 | 当前落点 | 对 TODO 的影响 |
|---|---|---|
| Desktop 是重型 Studio | `Acme.Product.Desktop.csproj` 引用 WebView2、AspNetCore、复制 `wwwroot` | Station 必须新建项目，不能引用 Desktop |
| Kestrel 与静态文件由 Desktop 启动 | `Acme.Product.Desktop/Program.cs` | Runtime/Station 禁止复用这条启动路径 |
| 算子注册目前集中在 Desktop DI | `Acme.Product.Desktop/DependencyInjection.cs` | 必须先抽出共享 Runtime DI，否则 Station 会被迫引用 Desktop 或复制注册 |
| 流程执行核心已经存在 | `IFlowExecutionService` + `FlowExecutionService` | Runtime 不重写执行引擎，围绕它做 package、host、normalizer |
| 实时检测已有 Coordinator/Worker | `InspectionRuntimeCoordinator`、`InspectionWorker` | 借鉴状态机、取消、关机兜底；MVP 不直接把完整实时检测链路搬进 Station |
| 项目/流程 DTO 与映射已有基础 | `OperatorFlowDto`、`FlowEntityMapper`、`ProjectService` | Studio 导出 Runtime Package 应复用现有 DTO/mapper 口径 |
| 相机/PLC 基础已有 | `ICameraManager`、`CameraFrameStreamCoordinator`、`PlcEndpoints`、`Acme.PlcComm` | 第一轮只做 simulation/replay；真实硬件放入现场化阶段 |
| 质量治理与 CI 已有入口 | `quality/field_replay`、`.github/workflows/ci.yml`、回归脚本 | Runtime MVP 必须纳入构建、测试、性能 smoke、字段回放证据 |

架构红线：

- [x] Station 不引用 `Acme.Product.Desktop`。
- [x] Station 不引用 `Microsoft.Web.WebView2`。
- [x] Station 不复制 `wwwroot`。
- [x] Station 不启动 Kestrel / `WebApplication`。
- [x] Runtime 不复制任何算子实现。
- [x] Runtime 必须复用 `IFlowExecutionService` / `FlowExecutionService`。
- [x] Runtime Package 不包含 API key、个人本机路径密钥、历史结果大包、Studio 临时噪声。
- [x] 任意连续运行、图片保存、记录落盘、日志输出都必须有上限、取消、超时和失败统计。

---

## 2. 目标架构边界

| 组件 | 新增/既有 | 允许依赖 | 禁止依赖 | 现场标准 |
|---|---|---|---|---|
| `Acme.Product.Desktop` | 既有 | Core / Application / Infrastructure / Contracts / WebView2 / Kestrel | Station 运行职责 | 只做 Studio，不常驻产线 |
| `Acme.Product.Runtime.Abstractions` | 新增 | Core 基础类型或纯 DTO | Desktop / Infrastructure / Web / OpenCvSharp 重依赖 | DTO 稳定、schema 可版本化 |
| `Acme.Product.Runtime` | 新增 | Core / Application / Infrastructure / Logging | Desktop / WebView2 / Kestrel / wwwroot | package 校验、执行、队列、normalizer 可测 |
| `Acme.Product.Station` | 新增 | Runtime / Runtime.Abstractions / 必要 WinForms | Desktop / WebView2 / Kestrel / wwwroot | 低配工控机可启动、可停止、可诊断 |
| `quality/runtime` | 新增 | 测试数据、manifest、报告 | 大体积现场原图无脱敏 | 一致性、性能、回放证据 |

建议新增工程：

```text
Acme.Product/src/Acme.Product.Runtime.Abstractions/
Acme.Product/src/Acme.Product.Runtime/
Acme.Product/src/Acme.Product.Station/

Acme.Product/tests/Acme.Product.Tests/Runtime/
Acme.Product/tests/Acme.Product.Station.Tests/
quality/runtime/
```

建议优先抽取的共享注册：

```text
Acme.Product/src/Acme.Product.Infrastructure/DependencyInjection/VisionRuntimeServiceCollectionExtensions.cs
```

目标是把当前 `Acme.Product.Desktop.DependencyInjection.AddVisionServices()` 中的非 Desktop 专属注册拆成共享 Runtime 注册：

```text
共享 Runtime 注册：
- IFlowExecutionService
- IOperatorFactory
- IOperatorExecutor 全量注册
- IVariableContext
- 必要的 image/model/calibration/runtime helper
- logging / metrics 基础能力

Desktop 专属注册：
- WebMessageHandler
- Auth endpoints/middleware
- Kestrel/WebApplication
- wwwroot/static files
- WebView2 host
- Desktop endpoint mapping
- Studio AI 生成入口
```

验收标准：

- [x] Desktop 仍能通过原有 build/test。
- [x] Station 可通过共享注册获得同一套 `FlowExecutionService` 和算子执行器。
- [x] 没有第二套算子注册表。
- [x] 新增架构测试扫描 Station/Runtime 不含 `WebView2|Kestrel|wwwroot|MapVisionApiEndpoints|WebApplication`。

---

## 3. Runtime Package V1 设计

第一轮使用目录包，不急着做压缩包和安装器：

```text
runtime-package/
├── package.json
├── flow.json
├── runtime-profile.json
├── README.runtime.md
└── quality/
    └── validation-report.json
```

V1 manifest 建议字段：

```json
{
  "packageId": "cvpkg-20260502-001",
  "packageName": "WireSequence-MVP",
  "runtimeApiVersion": "1.0",
  "minStationVersion": "0.1.0",
  "createdAt": "2026-05-02T00:00:00Z",
  "createdBy": "ClearVision Studio",
  "sourceProjectId": "00000000-0000-0000-0000-000000000000",
  "entryFlow": "flow.json",
  "flowHash": "sha256:<hash>",
  "operatorCatalogVersion": "155+legacy4",
  "exportAllowed": true,
  "pendingParameters": [],
  "missingResources": [],
  "fieldExtensions": {
    "stationProfile": "field/station-profile.json",
    "triggerProfile": "field/trigger-profile.json",
    "resultMappingProfile": "field/result-mapping-profile.json"
  }
}
```

V1.1 预留目录，但 V1 Station 可以忽略：

```text
runtime-package/
└── field/
    ├── station-profile.json
    ├── device-profile.json
    ├── trigger-profile.json
    ├── result-mapping-profile.json
    └── model-assets.json
```

导出/加载硬规则：

- [x] `package.json`、`flow.json`、`runtime-profile.json`、`quality/validation-report.json` 必须存在。
- [x] `runtimeApiVersion` 必须兼容。
- [x] `entryFlow` 必须在 package 根目录下，禁止 `..` 跳出包。
- [x] `flowHash` 必须可复算。
- [x] `exportAllowed` 必须为 `true`。
- [x] `pendingParameters` 必须为空。
- [x] `missingResources` 必须为空；simulation/replay 模式可降级为 warning，但必须写入报告。
- [x] 发现疑似 secret 的字段值必须阻断导出。
- [x] 可忽略 `field/`，但不能因为存在未知扩展目录而失败。
- [x] 加载失败必须给操作员可读错误，不只给 stack trace。

---

## 4. 分阶段 TODO

### M0：架构冻结与依赖解耦

目标：先把 Station 能否独立存在的工程边界打稳。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M0-01 | P0 | 冻结 Studio / Runtime / Station / Package 边界 | 本文 + `docs/runtime/ClearVision-Runtime-Design.md` | 明确第一轮不做真实相机、真实 PLC、协议构建器、多工位 |
| RT-M0-02 | P0 | 从 Desktop DI 抽出共享 Runtime 服务注册 | `Acme.Product.Infrastructure/DependencyInjection` | Desktop 与 Station 共用同一套算子注册 |
| RT-M0-03 | P0 | 新增 `Runtime.Abstractions` 与 `Runtime` 空工程 | `Acme.Product/src` | 加入 solution，build 通过 |
| RT-M0-04 | P0 | 新增 `Station` 空 WinForms 工程 | `Acme.Product/src/Acme.Product.Station` | build 通过，不含 WebView2/Kestrel/wwwroot |
| RT-M0-05 | P0 | 新增依赖扫描测试 | `Acme.Product/tests/Acme.Product.Tests/Runtime` | 扫描 Station/Runtime 项目文件和源码，违规引用即失败 |
| RT-M0-06 | P1 | 记录当前 Desktop 启动链路与不可迁移清单 | `docs/runtime/Desktop-Studio-Boundary.md` | 后续 review 可据此判断是否误把 Studio 能力塞进 Station |

不做：

- [x] 不迁移 Web 前端。
- [x] 不改造 FlowCanvas。
- [x] 不拆算子源码。
- [x] 不改当前 Desktop 发布流程。

### M1：Runtime Package 导出与验证

目标：Studio 能导出一个 Station 可以拒绝/接受的离线包。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M1-01 | P0 | 定义 Package DTO/schema | `Runtime.Abstractions` | manifest/profile/report DTO 有版本字段 |
| RT-M1-02 | P0 | 实现 Package Loader | `Acme.Product.Runtime` | 合法目录包可加载；路径穿越被拒绝 |
| RT-M1-03 | P0 | 实现 Package Validator | `Acme.Product.Runtime` | 缺文件、版本不兼容、hash 不匹配、validation failed 都可诊断 |
| RT-M1-04 | P0 | Studio 导出 Runtime Package API | `Acme.Product.Desktop/Endpoints` 或 Application service | 从当前工程/flow 导出 V1 包 |
| RT-M1-05 | P0 | 导出前完整性检查 | `ProjectService` / exporter | pending 参数、缺资源、疑似 secret 阻断导出 |
| RT-M1-06 | P1 | Studio UI 增加“导出 Runtime Package”入口 | `wwwroot/src/features/project` 或 flow editor | 工程师可从 Studio 触发导出 |
| RT-M1-07 | P1 | 生成 `README.runtime.md` | exporter | 包内说明包含来源、hash、运行要求、未启用现场扩展 |
| RT-M1-08 | P1 | 非法包测试集 | `Acme.Product/tests/TestData/runtime-packages` | 至少覆盖 8 类失败场景 |

关键判断：

- [x] Package export 复用 `OperatorFlowDto` / `FlowEntityMapper` / `OperatorTypeAliasResolver` 的口径。
- [x] 不把 `vision.db`、用户 token、AI key、历史结果、Studio cache 打进包。
- [x] 如果流程依赖模型/标定/模板文件，V1 要么随包声明并校验 hash，要么明确阻断导出。

### M2：RuntimeHost 与执行核心

目标：Runtime 能在无 Desktop/Web 的进程中执行同一份 flow。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M2-01 | P0 | 实现 `RuntimeHost` 状态机 | `Acme.Product.Runtime` | Idle/Loaded/Running/Stopping/Faulted 可测 |
| RT-M2-02 | P0 | 实现 `RuntimeFlowAdapter` | `Acme.Product.Runtime` | package flow 可转换为 `OperatorFlow` |
| RT-M2-03 | P0 | 直接调用 `IFlowExecutionService` 执行 Single Run | `RuntimeHost` | 不绕 `InspectionService` 的 DB/project 门面 |
| RT-M2-04 | P0 | 实现 `RuntimeResultNormalizer` | `Acme.Product.Runtime` | OK/NG/Error、耗时、主要输出、诊断码统一 |
| RT-M2-05 | P0 | 引入 `RunId` / `PackageId` / `FlowHash` / `ImageId` 追踪字段 | Runtime DTO | 每条结果可追溯 |
| RT-M2-06 | P0 | `StopAsync` 幂等与限时退出 | `RuntimeHost` | 重复 stop 不异常；超时有 pending/dropped/failed 统计 |
| RT-M2-07 | P1 | 图片保存队列 | `Runtime/Queues` | 有容量上限；NG 优先；OK 可按策略丢弃 |
| RT-M2-08 | P1 | 记录写入队列 | `Runtime/Queues` | JSONL 落盘异步化，有失败计数 |
| RT-M2-09 | P1 | UI 事件限频机制 | Runtime event model | Station 订阅时不会被高频日志压死 |

运行记录建议：

```text
%LocalAppData%/ClearVisionStation/runs/yyyyMMdd/runtime-results.jsonl
%LocalAppData%/ClearVisionStation/logs/station-yyyyMMdd.log
%LocalAppData%/ClearVisionStation/images/yyyyMMdd/NG/
%LocalAppData%/ClearVisionStation/images/yyyyMMdd/ERROR/
```

第一轮不要写入 package 目录本身，避免现场误把运行结果混入可回滚包。

### M3：Station MVP

目标：操作员能用轻量 Station 跑包，不需要打开 Studio。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M3-01 | P0 | Station 主窗体骨架 | `Acme.Product.Station/MainForm.cs` | 原生 WinForms 启动，无 WebView2 |
| RT-M3-02 | P0 | 加载 Package | Station UI + Runtime Loader | 成功显示 package 名称、版本、flow hash |
| RT-M3-03 | P0 | 选择单张图片运行 | Station UI + RuntimeHost | 显示 OK/NG/Error、耗时、错误原因 |
| RT-M3-04 | P0 | 背景执行与 UI 线程隔离 | Station | 执行时 UI 不假死，按钮状态正确 |
| RT-M3-05 | P0 | 停止按钮 | Station + RuntimeHost | 运行中可停止，状态最终收敛 |
| RT-M3-06 | P1 | 选择目录序列运行 | Station + RuntimeHost | 30-100 张图片可跑完或停止 |
| RT-M3-07 | P1 | 基础统计 | Station | 总数、OK、NG、Error、平均、P95 |
| RT-M3-08 | P1 | 限频日志窗口 | Station | 高频运行时 UI 可读、不卡顿 |
| RT-M3-09 | P1 | 最近结果列表 | Station | 最近 10-50 条结果可查看 |

MVP UI 边界：

```text
顶部：Package / State / FlowHash
工具：加载包、选择图片、运行一次、选择目录、运行目录、停止
左侧：图像预览
右侧：大状态灯、耗时、统计、最近结果
底部：限频日志
```

不做：

- [x] 不做全量画布编辑。
- [x] 不做 AI 生成。
- [x] 不做算子库浏览。
- [x] 不做复杂设备中心。
- [x] 不做远程 Web 管理。

### M4：Studio / Station 一致性与回放证据

目标：证明 Station 不是第二套行为。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M4-01 | P0 | Studio Preview 与 Station Single Run 一致性测试 | `Acme.Product.Tests/Runtime` | 同 package + 同图片，状态与关键输出一致 |
| RT-M4-02 | P0 | Runtime result contract tests | `RuntimeResultNormalizerTests` | 缺判定信号、算子失败、异常、正常 OK/NG 都可归一 |
| RT-M4-03 | P0 | flow hash 稳定性测试 | `RuntimePackageExportTests` | 同 flow 导出 hash 稳定 |
| RT-M4-04 | P1 | `quality/runtime/runtime-mvp-smoke.json` | `quality/runtime` | 记录 30-100 张 replay 结果 |
| RT-M4-05 | P1 | 性能 smoke 报告 | `quality/runtime/runtime-performance-smoke.md` | 启动、加载、单张、目录运行、停止耗时有记录 |
| RT-M4-06 | P1 | 内存增长观察 | `quality/runtime` | 目录运行后无明显持续增长，先记录基线再收紧 |
| RT-M4-07 | P1 | 失败包 triage 报告 | `quality/runtime` | 常见导入失败有原因分类与修复建议 |

建议验收命令：

```powershell
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Runtime" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal

dotnet build Acme.Product/src/Acme.Product.Station/Acme.Product.Station.csproj `
  --configuration Debug `
  --no-restore
```

依赖扫描建议：

```powershell
Select-String -Path "Acme.Product/src/Acme.Product.Station/**/*.cs","Acme.Product/src/Acme.Product.Runtime/**/*.cs","Acme.Product/src/Acme.Product.Station/*.csproj","Acme.Product/src/Acme.Product.Runtime/*.csproj" `
  -Pattern "WebView2|Microsoft.Web.WebView2|wwwroot|Kestrel|WebApplication|MapVisionApiEndpoints"
```

### M5：现场化扩展骨架

目标：为真实现场能力预留结构，但不污染 MVP。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M5-01 | P0 | `field/station-profile.json` schema 草案 | `Runtime.Abstractions` / docs | V1 可忽略，V1.1 可启用 |
| RT-M5-02 | P0 | `field/result-mapping-profile.json` schema 草案 | docs/runtime | 先定义 OK/NG、测量值、错误码映射 |
| RT-M5-03 | P1 | `field/trigger-profile.json` schema 草案 | docs/runtime | Manual/Timer/Replay 先行，PLC/TCP 后续 |
| RT-M5-04 | P1 | `field/model-assets.json` schema 草案 | docs/runtime | 模型路径、hash、labels、task、input size |
| RT-M5-05 | P1 | Station 显示 StationId/LineName 可选字段 | Station | 无字段时不影响 MVP |
| RT-M5-06 | P1 | 模拟输出 sink | Runtime | JSONL / mock PLC writeback 可预演 |
| RT-M5-07 | P2 | 设备模拟器接入 | `quality/runtime` 或 tools | 无真实硬件可演练触发、超时、断连 |

关键边界：

- [x] V1 Station 可以读取并忽略 `field/`。
- [x] V1.1 才启用 StationProfile / TriggerProfile / ResultMappingProfile。
- [x] 协议构建 UI 留在 Studio，不放进 Station。
- [x] Station 只执行协议模板，不编辑协议模板。

### M6：现场稳定性硬化

目标：从“能跑”进入“能常驻、能定位、能回滚”。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M6-01 | P0 | 启停状态审计 | RuntimeHost / Station | Running -> Stopping -> Stopped 有确定性事件 |
| RT-M6-02 | P0 | 队列容量与丢弃策略 | Runtime queues | OK 可降级，NG/Error 优先保护 |
| RT-M6-03 | P0 | 日志字段标准化 | Runtime logging | `RunId/PackageId/FlowHash/ImageId/StationId` 全链路可查 |
| RT-M6-04 | P0 | 异常分级 | RuntimeResult | PackageInvalid / FlowInvalid / ExecutionFailed / ResourceMissing / Canceled / OutputFailed |
| RT-M6-05 | P1 | 上次成功包指针 | Station local config | 启动可提示最近成功包，不自动覆盖 |
| RT-M6-06 | P1 | 回滚机制 | Station | 可选择历史 package，失败包不替换 last-good |
| RT-M6-07 | P1 | 崩溃恢复记录 | Station local data | 下次启动提示上次异常退出和最后 run id |
| RT-M6-08 | P1 | 单机性能预算 | `quality/runtime` | 先记录，再按现场硬件收紧 |
| RT-M6-09 | P2 | 安装包/发布包拆分 | CI release | Studio 与 Station 可分别发布 |

建议稳定性预算初稿：

| 指标 | MVP 记录项 | 后续收紧方向 |
|---|---|---|
| Station 冷启动 | 记录启动耗时 | 低配工控机可接受 |
| Package 加载 | 记录加载耗时、schema 验证耗时 | 包大小增长时可追踪 |
| Single Run | 记录 avg/p95/max | 按黄金场景设预算 |
| StopAsync | 记录停止耗时、pending/dropped/failed | 超时必须可诊断 |
| 目录运行 | 记录内存前后差、队列峰值 | 长时运行前必须建立趋势 |

### M7：真实设备试点

目标：在 Runtime MVP 稳定后，逐步接真实现场链路。

| 编号 | 优先级 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|---|
| RT-M7-01 | P1 | Camera binding 从 package field profile 进入 Station | Runtime field adapter | 先接 File/Mock，再接一个真实相机 |
| RT-M7-02 | P1 | PLC/TCP 输出映射 simulation | Runtime output sink | 模拟写回成功/失败/超时 |
| RT-M7-03 | P1 | Manual/Timer/Replay trigger | Runtime trigger loop | 不依赖真实 PLC 即可连续运行 |
| RT-M7-04 | P2 | PLC trigger | Runtime trigger adapter | 真实 PLC 前必须有模拟器 contract |
| RT-M7-05 | P2 | 结果写回真实 PLC/TCP | Runtime output adapter | 写回失败有回执、重试、报警 |
| RT-M7-06 | P2 | 运行台升级 | Station | 工位状态、设备状态、最近 NG、报警解释 |
| RT-M7-07 | P2 | 黄金场景现场配方 | Studio exporter + quality replay | 至少 1 个场景从导出到 Station 回放闭环 |

真实硬件接入顺序：

```text
Replay 图片目录
-> File/Mock camera
-> Manual/Timer trigger
-> simulated PLC/TCP output
-> one real camera
-> one real PLC/TCP writeback
-> PLC/TCP trigger
-> multi-station statistics
```

---

## 5. 推荐实施节奏

| 时间 | 主目标 | 必须交付 | 不允许拖入 |
|---|---|---|---|
| 第 1 周 | M0 + M1 | 工程骨架、共享 DI、Package DTO/Loader/Validator、Studio 导出最小包 | 真实硬件、运行台大改 |
| 第 2 周 | M2 + M3 | RuntimeHost、Station 加载包、单图运行、停止、依赖扫描 | 设备中心、协议构建器 |
| 第 3 周 | M4 | Studio/Station 一致性、目录运行、性能 smoke、非法包测试 | 多工位统计 |
| 第 4 周 | M5 + M6 | field schema 预留、队列/日志/回滚、MVP 验收报告 | 真实 PLC trigger |
| 第 5 周以后 | M7 | simulation -> 单真实相机 -> 单真实写回 -> 现场配方 | 在未过 MVP 前扩全设备矩阵 |

---

## 6. P0 总清单

这些任务不完成，不建议进入现场试用：

- [x] 抽出共享 Runtime DI，Station 不引用 Desktop。
- [x] 新增 Runtime/Station 工程并加入 solution。
- [x] Runtime Package V1 DTO、Loader、Validator 完成。
- [x] Studio 可导出 Runtime Package。
- [x] Station 可加载 Runtime Package。
- [x] Station 可选择单张图片并执行 Single Run。
- [x] RuntimeHost `StopAsync` 幂等、限时、可诊断。
- [x] Station/Runtime 依赖扫描门禁通过。
- [x] Studio Preview 与 Station Single Run 一致性测试通过。
- [x] 图片目录 replay 能跑 30-100 张，UI 不假死。
- [x] Runtime MVP 验证报告落地。

---

## 7. P1 总清单

这些任务决定能不能进入稳定试点：

- [x] Runtime 图片保存队列和记录队列有容量上限。
- [x] 结果 JSONL 与日志都带 `RunId/PackageId/FlowHash/ImageId`。
- [x] Station 最近结果、统计、限频日志可用。
- [x] field schema 预留：StationProfile / TriggerProfile / ResultMappingProfile / ModelAssets。
- [ ] 模拟输出 sink 可预演 OK/NG 写回。<!-- ❌ mock PLC writeback 未实现 -->
- [~] Package 回滚机制可用。<!-- ⚠️ last-good 指针有；自动回滚逻辑未实现 -->
- [x] 崩溃恢复提示可用。
- [~] 性能 smoke 形成 baseline。<!-- ⚠️ 软件基线已建立；工控机硬件数字待现场采集 -->

---

## 8. P2 总清单

这些任务很有价值，但不该压进第一轮 MVP：

- [ ] 真实相机连续采集。
- [ ] 真实 PLC trigger。
- [ ] 真实 PLC/TCP writeback。
- [ ] 协议构建 UI。
- [ ] 多工位统计。
- [ ] 独立 Engine Worker 进程。
- [ ] Station 远程 Web 管理。
- [ ] 安装包系统与自动升级。
- [ ] 大规模模型资产管理。

---

## 9. 验收标准

MVP 完成标准：

- [x] `Acme.Product.Desktop` 原有功能未被破坏。
- [x] `Acme.Product.Station` 可独立启动。
- [x] Station 无 WebView2 / wwwroot / Kestrel / Desktop 引用。
- [x] Studio 可导出 Runtime Package。
- [x] Runtime 可拒绝非法 package，并给出可读原因。
- [x] Station 可加载合法 package。
- [x] Station 可执行单张图片。
- [x] Station 可执行图片目录。
- [x] 同一 package + 同一图片，Studio Preview 与 Station Single Run 关键结果一致。
- [x] UI 不假死，停止可用。
- [x] 队列、日志、结果记录都有边界。
- [x] 有 `quality/runtime/runtime-performance-smoke.md`。
- [x] 有 `docs/runtime/Runtime-MVP-Validation-Report.md` 或等价验收报告。

现场试点前标准：

- [~] 至少一个黄金场景 package 可回放。<!-- ⚠️ 测试用 mock 包可回放；真实生产工程包待 Studio 导出验证 -->
- [ ] 模拟设备/触发/写回链路可跑通。<!-- ❌ mock PLC writeback 未实现 -->
- [x] 缺模型、缺相机、缺 mapping、package 版本不兼容都能提前诊断。
- [~] last-good package 回滚可用。<!-- ⚠️ 手动 Load Last Good 可用；自动回滚逻辑未实现 -->
- [x] StopAsync、异常退出、写队列失败都有记录。
- [x] 现场操作员不需要进入 Studio 画布即可启动、停止、看结果、看报警。

---

## 10. 风险与控制

| 风险 | 早期信号 | 控制策略 |
|---|---|---|
| Station 悄悄变成第二个 Desktop | 引入 WebView2/Kestrel/wwwroot/Desktop 引用 | 依赖扫描测试 + code review 红线 |
| 复制算子注册或算子实现 | Station/Runtime 出现第二套 executor 注册 | 共享 Runtime DI，禁止复制算子实现 |
| Runtime 与 Studio 结果分叉 | Preview 与 Station 同图结果不一致 | 一致性 contract tests，统一 normalizer |
| Stop 停不下来 | 目录运行停止超时、后台任务泄漏 | StopAsync 幂等、限时、pending/dropped/failed 统计 |
| 队列撑爆内存 | OK 图和日志无限堆积 | 有界队列，NG/Error 优先，OK 按策略丢弃 |
| 现场问题不可诊断 | 错误只有 stack trace 或“失败” | 错误分级 + 可读原因 + run id 追踪 |
| 包不可迁移 | 绝对路径、模型缺失、secret 混入 | 导出前校验、hash、相对路径、secret scan |
| 过早接硬件导致主线发散 | MVP 前开始相机/PLC 大量适配 | simulation/replay 先闭环，真实设备按 M7 顺序推进 |

---

## 11. 总控 Prompt

后续实现时可用这个口径派发任务：

```text
请基于当前 ClearVision 仓库实现分体式 Runtime MVP。

当前事实：
- Acme.Product.Desktop 是 Studio，保留 WinForms + WebView2 + Kestrel + wwwroot。
- 当前算子注册集中在 Acme.Product.Desktop.DependencyInjection，必须抽出共享 Runtime DI。
- ClearVision 已有 Core / Contracts / Application / Infrastructure 分层和 IFlowExecutionService / FlowExecutionService。
- InspectionWorker / InspectionRuntimeCoordinator 可借鉴状态机、取消、关机兜底，但 Station MVP 不直接搬完整实时检测链路。
- ClearFrost 只作为低配工控机运行经验参考，不整仓合并，不复制 DetectionService。

首轮目标：
1. 新增 Acme.Product.Runtime.Abstractions。
2. 新增 Acme.Product.Runtime。
3. 新增 Acme.Product.Station。
4. 抽出共享 Runtime 服务注册，Station 不引用 Desktop。
5. Runtime Package V1 DTO / Loader / Validator。
6. Studio 导出 Runtime Package。
7. Station 加载 Runtime Package。
8. Station 选择本地图像 Single Run。
9. Station 图片目录 replay。
10. Studio Preview / Station Single Run 一致性测试。
11. Runtime MVP 验证报告和性能 smoke。

硬性禁止：
- 不重写 Studio。
- 不重写 FlowCanvas。
- 不复制算子实现。
- 不复制 ClearFrost DetectionService。
- Station 不引用 Microsoft.Web.WebView2。
- Station 不启动 Kestrel。
- Station 不复制 wwwroot。
- 第一轮不接真实相机、不接真实 PLC、不做协议构建器、不做多工位。

验收：
- dotnet build 通过。
- Runtime/Station 依赖扫描通过。
- Runtime Package 合法/非法加载测试通过。
- 同一 package + 同一图片，Studio 与 Station 输出一致。
- 图片序列运行 UI 不假死，Stop 可用。
- 队列、日志、结果记录有容量、失败统计和可诊断错误。
```

---

## 12. 审查 Prompt

后续 review 时使用：

```text
请以“低配 Windows 工控机上的 ClearVision Station Runtime MVP”标准审查这次改动。

重点检查：
1. Station/Runtime 是否引用了 Desktop、WebView2、wwwroot、Kestrel、MapVisionApiEndpoints。
2. 是否复制算子实现或复制 executor 注册表。
3. 是否复用了 IFlowExecutionService / FlowExecutionService。
4. Runtime Package 是否有版本、hash、完整性、资源、secret 校验。
5. Package 加载失败是否能给操作员可读错误。
6. Station UI 是否会被执行线程、日志、图片保存阻塞。
7. StopAsync 是否幂等、限时、可诊断。
8. 图片保存与记录队列是否有容量和丢弃策略。
9. Studio Preview 与 Station Single Run 是否有一致性测试。
10. field 扩展 schema 是否预留但未污染首轮 MVP。
11. 日志是否包含 RunId、PackageId、FlowHash、ImageId。
12. Desktop Studio 原有功能是否保持。

请输出：
1. 必须修改的问题。
2. 建议修改的问题。
3. 可以后续处理的问题。
4. 是否建议进入现场试点。
```


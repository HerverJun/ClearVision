---
title: "ClearVision Runtime / Station 现场化整合计划"
doc_type: "roadmap"
status: "active"
topic: "runtime-station-field-workflow"
created: "2026-04-29"
updated: "2026-04-29"
sources:
  - "C:/Users/A/Desktop/ClearVision_Runtime_Design_Plan.md"
  - "C:/Users/A/Desktop/ClearVision/docs/进行中/当前计划/ClearVision-现场节点与业务流程改进计划-2026-04-29.md"
  - "C:/Users/A/Desktop/视觉平台/reverse_output/business_decompiled_pycdc/widgets"
---

# ClearVision Runtime / Station 现场化整合计划

## 0. 口径修正

本文以 `C:/Users/A/Desktop/ClearVision_Runtime_Design_Plan.md` 为主文档，之前误用的 `clearfrost_v4_industrial_platform_3day_todo.md` 不再作为本计划依据。

整合原则：

```text
第一轮严格守住 Runtime MVP，不把现场复杂度一次性塞进 Station。
第二轮把逆向平台提炼出的设备中心、触发中心、结果映射、运行台逐步接入。
```

## 1. 一句话结论

最合理的方向不是继续让 `Acme.Product.Desktop` 这个重型 WinForms + WebView2 程序直接上低配工控机，而是拆成：

```text
ClearVision Studio：工程师端，保留现有 Desktop/WebView2，负责 AI 生成、流程编辑、调试、导出 Runtime Package。
ClearVision Station：轻量运行端，新增 Native WinForms，不加载 WebView2，不启动完整 Kestrel，负责加载包、跑流程、显示结果。
ClearVision Runtime：共享运行核心，负责包加载、验证、状态机、执行编排、队列、性能计数。
Shared Engine：复用 Core / Contracts / Application / Infrastructure / Operator / FlowExecutionService。
```

逆向平台的价值不在算法实现，而在现场操作模型：

```text
工程/工位
设备中心
一键启停
触发事件
模型管理
全局变量
通信收发
运行记录
多工位统计
```

这些能力应作为 `Runtime Package` 和 `Station` 的后续现场化扩展，而不是压进第一轮 MVP。

## 2. 两份计划的职责分工

| 来源 | 主要价值 | 本整合计划如何吸收 |
|---|---|---|
| `ClearVision_Runtime_Design_Plan.md` | Studio / Runtime 拆分、轻量 Station、Package 边界、复用 FlowExecutionService、Single Run / Sequence Run、性能 smoke | 作为主线和第一轮 MVP 范围 |
| 逆向平台现场分析 | 设备中心、触发中心、全局变量、模型管理、运行记录、多工位统计、协议构建器 | 作为 Runtime Package 扩展字段和第二轮现场化路线 |
| ClearFrost 经验 | AppRuntime 生命周期、异步保存队列、低配工控机性能规则、图像链路减负 | 作为 RuntimeHost、队列、StopAsync、性能规则参考，不整仓合并 |

## 3. 分体式产品边界

| 组件 | 使用者 | 运行位置 | 必须有 | 明确不要有 |
|---|---|---|---|---|
| ClearVision Studio | 工程师 | 工程师电脑/调试机 | 全量画布、AI 生成、算子库、模板编辑、预览、导出包 | 产线常驻运行职责 |
| ClearVision Runtime | 运行核心 | Station 内部库 | 包加载、验证、状态机、执行编排、队列、结果归一化 | WebView2、wwwroot、Kestrel 端点 |
| ClearVision Station | 操作员/维护人员 | 低配 Windows x64 工控机 | 加载包、选择图片、单次运行、目录运行、OK/NG、日志、性能统计 | AI 面板、完整画布编辑、算子文档 |
| Runtime Package | Studio 导出，Station 导入 | 文件目录 | `package.json`, `flow.json`, `runtime-profile.json`, `validation-report.json` | 密钥、大量历史图片、完整 Studio 工程噪声 |

## 4. 第一轮 MVP 必须守住的边界

第一轮只证明四件事：

```text
1. Station 能脱离 WebView2 / Kestrel 启动。
2. Station 能加载 Studio 导出的 Runtime Package。
3. Station 能用同一套 FlowExecutionService 执行单张图。
4. Studio Preview 与 Station Single Run 对同一 package + 同一图片结果一致。
```

第一轮暂不做：

```text
真实相机连续采集
真实 PLC 联动
完整设备中心
完整触发中心
协议构建 UI
复杂多工位
独立 Engine Worker 进程
完整 Dashboard
安装包系统
远程 Web 管理 API
```

这样做的好处是，先把“轻量运行端能跑同一套流程”这个地基打稳，再吸收逆向平台的现场能力。

## 5. Runtime Package 分层设计

### 5.1 V1 MVP 包结构

沿用正确文档中的设计，第一版使用目录结构：

```text
runtime-package/
├── package.json
├── flow.json
├── runtime-profile.json
└── quality/
    └── validation-report.json
```

V1 加载规则：

```text
package.json 存在且可解析
runtimeApiVersion 兼容
entryFlow 指向的 flow.json 存在
runtime-profile.json 存在
validation-report.json 存在
exportAllowed = true
pendingParameters 为空
missingResources 为空，或处于 simulation mode
flow.json 可转换为现有 FlowExecutionService 可执行模型
```

### 5.2 V1.1 现场化扩展包结构

逆向平台现场能力不直接进入 V1，但可以预留扩展目录：

```text
runtime-package/
├── package.json
├── flow.json
├── runtime-profile.json
├── quality/
│   └── validation-report.json
└── field/
    ├── station-profile.json
    ├── device-profile.json
    ├── trigger-profile.json
    ├── result-mapping-profile.json
    └── model-assets.json
```

V1 Station 可以忽略 `field/` 目录；V1.1 开始逐步启用。

### 5.3 V2 生产现场包结构

后续接真实设备和模型资产时，再扩展：

```text
runtime-package/
├── assets/
│   ├── models/
│   ├── labels/
│   ├── calibration/
│   └── templates/
├── protocols/
│   └── *.json
└── acceptance/
    ├── replay-dataset.json
    └── acceptance-report.json
```

## 6. 现场能力如何接入 Runtime

| 逆向平台现场能力 | 第一轮 MVP 处理 | 第二轮现场化处理 |
|---|---|---|
| 多工程/多工位 | 不做复杂多工位，只在结果中预留 `StationId` | `station-profile.json`，支持 active station |
| 设备中心 | 不接真实设备，只保留 simulation | Station 设备页：相机/PLC/TCP 状态、一键启动/停止、连接测试 |
| 触发中心 | 单张图、图片目录手动触发 | `trigger-profile.json` 支持 Manual/Timer/Replay，后续支持 PLC/TCP |
| 模型管理 | V1 assets 可选，不强制模型包 | `model-assets.json` + 模型 hash/labels/task 校验 |
| 全局变量 | 不做变量系统 | `result-mapping-profile.json` 支持结果字段映射 |
| 通信发送/接收 | 不接真实 PLC/TCP | 先做模拟 PLC/TCP，再接真实 PLC/TCP |
| 协议构建器 | 不进 Station MVP | 放在 Studio 侧做协议模板编辑，Station 只执行模板 |
| 运行记录 | Runtime JSONL 日志和最近结果 | 运行台显示节拍、序列号、最近 NG、报警、统计 |
| 多工位统计 | 不做 | V2 支持工位/班次/日期统计 |

## 7. 工程结构调整

第一轮新增：

```text
Acme.Product.Runtime.Abstractions/
Acme.Product.Runtime/
Acme.Product.Station/
```

职责保持原设计：

| 项目 | 职责 | 禁止 |
|---|---|---|
| `Acme.Product.Runtime.Abstractions` | Runtime DTO、接口、状态枚举 | 引用 Desktop/WebView2 |
| `Acme.Product.Runtime` | Loader、Validator、RuntimeHost、FlowAdapter、ResultNormalizer、队列 | 启动 Kestrel、引用 wwwroot |
| `Acme.Product.Station` | Native WinForms 轻量 UI | WebView2、完整前端、AI 生成 |

关键禁令：

```text
Station 禁止引用 Microsoft.Web.WebView2。
Station 禁止引用 Acme.Product.Desktop。
Station 禁止复制 wwwroot。
Station 禁止启动完整 Kestrel。
Runtime 禁止复制算子实现。
Runtime 必须复用 FlowExecutionService。
```

## 8. RuntimeHost 设计整合

`RuntimeHost` 保留正确文档中的职责：

```text
持有当前 RuntimePackage
管理 RuntimeState
创建或获取 FlowExecutionService
执行单张图或图片序列
生产 RuntimeResult
投递结果到 RuntimeResultSink
管理 RuntimeImageSaveQueue / RuntimeRecordQueue
StopAsync 时停止执行、排空队列、释放资源
```

吸收 ClearFrost / 逆向平台的运行经验：

```text
StopAsync 必须幂等。
StopAsync 不能无限等待。
停止时要记录 pending / dropped / failed。
图像保存异步化。
检测记录异步化。
UI 刷新限频。
日志节流。
队列满不能无限堆积。
低优先级 OK 图可以丢弃，NG 图优先保护。
```

## 9. Station UI 从 MVP 到现场运行台

### 9.1 第一轮 MVP UI

保持正确文档中的轻量布局：

```text
Package: [未加载]    State: Idle
[加载 Package] [选择图片] [运行一次] [运行目录] [停止]

左：图像预览
右：OK / NG / Error 大状态灯、当前耗时、总数、OK、NG、Error、平均耗时、P95
底：限频日志
```

### 9.2 第二轮现场运行台

再吸收逆向平台运行记录和统计面板：

```text
左：当前工位/设备状态
中：实时图像与叠加结果
右：当前节拍、InspectionId、序列号、OK/NG、报警、最近 10 条结果
底：运行日志、输出回执、保护规则解释
```

注意：运行台不是完整画布编辑器。操作员不应面对全量算子库。

## 10. 路线图

### M0：基线与文档

产物：

```text
docs/runtime/ClearVision-Runtime-Design.md
docs/runtime/ClearFrost-Runtime-Reference.md
docs/runtime/Runtime-Field-Extension-Plan.md
```

验收：

```text
明确 ClearFrost 只作为经验参考，不整仓合并。
明确第一轮 Runtime MVP 范围。
明确逆向平台现场能力进入第二轮，不污染 MVP。
```

### M1：新增 Runtime / Station 工程骨架

产物：

```text
Acme.Product.Runtime.Abstractions
Acme.Product.Runtime
Acme.Product.Station
```

验收：

```text
dotnet build 通过。
Station 不引用 WebView2。
Station 不复制 wwwroot。
Station 不启动 Kestrel。
```

### M2：Runtime Package 加载与验证

产物：

```text
RuntimePackage DTO
RuntimePackageLoader
RuntimePackageValidator
Station Load Package UI
```

验收：

```text
能加载合法 package。
能拒绝缺文件、版本不兼容、validation failed 的 package。
能忽略 V1.1 现场扩展目录而不报错。
```

### M3：Studio 导出 Runtime Package

产物：

```text
Studio Export Runtime Package
package.json
flow.json
runtime-profile.json
quality/validation-report.json
README.runtime.md
```

验收：

```text
从当前工程导出 package。
Station 能读取该 package。
导出前能检查 pending parameters / missing resources。
```

### M4：Station Single Run

产物：

```text
Station 选择图片并执行一次。
显示 OK/NG、耗时、主要输出。
```

验收：

```text
同一 package + 同一图片，Studio Preview 与 Station Single Run 结果一致。
```

### M5：图片序列模拟连续运行

产物：

```text
Station 选择图片目录。
执行 30-100 张图片。
显示总数、OK、NG、Error、平均耗时、P95。
RuntimeImageSaveQueue / RuntimeRecordQueue 有基础统计。
```

验收：

```text
UI 不假死。
停止可用。
内存无明显持续增长。
结果日志正常。
```

### M6：性能与风险收口

产物：

```text
docs/runtime/Runtime-MVP-Validation-Report.md
quality/runtime/runtime-performance-smoke.json
quality/runtime/runtime-performance-smoke.md
```

验收：

```text
明确下一阶段接相机 / PLC / Worker 进程 / 现场包扩展的入口。
明确当前未完成风险。
```

## 11. 第二轮现场化路线

第一轮 MVP 通过后，再推进：

| 阶段 | 目标 | 产物 |
|---|---|---|
| F1 | StationProfile / RuntimeBinding | `field/station-profile.json`，结果记录包含 `StationId` |
| F2 | 设备中心轻量版 | 相机/PLC/TCP simulation 状态、一键启动/停止、连接测试 |
| F3 | 触发中心 | Manual/Timer/Replay，后续 PLC/TCP trigger |
| F4 | 结果映射 | `result-mapping-profile.json`，先写 JSONL / 模拟 PLC，再接真实 PLC/TCP |
| F5 | 模型资产 | `model-assets.json`，模型 hash/labels/task/input size 校验 |
| F6 | 操作员运行台 | 工位状态、设备状态、当前节拍、最近 NG、报警解释 |
| F7 | 场景配方 | 线序、模板定位 + 宽度测量、Blob 缺陷、OCR/条码追溯 |

## 12. 首轮文件清单

沿用正确文档的首轮清单：

```text
Acme.Product/src/Acme.Product.Runtime.Abstractions/Acme.Product.Runtime.Abstractions.csproj
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimePackageManifest.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeProfile.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeValidationReport.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeState.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/RuntimeResult.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/IRuntimePackageLoader.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/IRuntimePackageValidator.cs
Acme.Product/src/Acme.Product.Runtime.Abstractions/IRuntimeHost.cs

Acme.Product/src/Acme.Product.Runtime/Acme.Product.Runtime.csproj
Acme.Product/src/Acme.Product.Runtime/RuntimePackageLoader.cs
Acme.Product/src/Acme.Product.Runtime/RuntimePackageValidator.cs
Acme.Product/src/Acme.Product.Runtime/RuntimeHost.cs
Acme.Product/src/Acme.Product.Runtime/RuntimeFlowAdapter.cs
Acme.Product/src/Acme.Product.Runtime/RuntimeResultNormalizer.cs
Acme.Product/src/Acme.Product.Runtime/Queues/RuntimeImageSaveQueue.cs
Acme.Product/src/Acme.Product.Runtime/Queues/RuntimeRecordQueue.cs

Acme.Product/src/Acme.Product.Station/Acme.Product.Station.csproj
Acme.Product/src/Acme.Product.Station/Program.cs
Acme.Product/src/Acme.Product.Station/MainForm.cs
Acme.Product/src/Acme.Product.Station/MainForm.Designer.cs

Acme.Product/tests/Acme.Product.Tests/Runtime/RuntimePackageLoaderTests.cs
Acme.Product/tests/Acme.Product.Tests/Runtime/RuntimePackageValidatorTests.cs
Acme.Product/tests/Acme.Product.Tests/Runtime/RuntimeResultNormalizerTests.cs
Acme.Product/tests/Acme.Product.Desktop.Tests/RuntimePackageExportTests.cs

docs/runtime/ClearVision-Runtime-Design.md
docs/runtime/ClearFrost-Runtime-Reference.md
docs/runtime/Runtime-Field-Extension-Plan.md
docs/runtime/Runtime-MVP-Validation-Report.md
```

第一轮不要新增一大堆现场 profile 类到生产路径；最多在文档和 package schema 中预留。

## 13. 验收命令

```powershell
# 产品构建
dotnet build Acme.Product/Acme.Product.sln --configuration Debug

# Runtime 相关测试
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Runtime" `
  -Verbosity minimal

# Station 单独构建
dotnet build Acme.Product/src/Acme.Product.Station/Acme.Product.Station.csproj --configuration Debug

# 确认 Station/Runtime 不引用 WebView2、wwwroot、Kestrel
rg -n "WebView2|Microsoft.Web.WebView2|wwwroot|Kestrel|MapVisionApiEndpoints" `
  Acme.Product/src/Acme.Product.Station `
  Acme.Product/src/Acme.Product.Runtime
```

## 14. 最终判断标准

第一轮成功标志：

```text
[ ] Studio 未被破坏。
[ ] Station 可独立启动。
[ ] Station 无 WebView2 / wwwroot / Kestrel。
[ ] Studio 可导出 Runtime Package。
[ ] Station 可加载 Runtime Package。
[ ] Station 可执行单张图。
[ ] Station 可执行图片序列。
[ ] Studio Preview 与 Station 对同一输入结果一致。
[ ] UI 不假死。
[ ] 有最小性能和内存报告。
[ ] Runtime Package 结构为后续现场设备/触发/映射扩展预留空间。
```

第二轮成功标志：

```text
[ ] 支持 StationProfile / active station。
[ ] 支持设备状态与一键启动/停止。
[ ] 支持 TriggerProfile。
[ ] 支持 ResultMappingProfile。
[ ] 支持模型资产校验。
[ ] 支持操作员运行台。
[ ] 支持至少一个黄金场景配方回放验收。
```

## 15. 总控 Prompt

```text
请基于 ClearVision 当前仓库实现 Runtime MVP，并保留后续现场化扩展空间。

当前事实：
- Acme.Product.Desktop 是 Studio，保留 WinForms + WebView2。
- ClearVision 已有 Core / Contracts / Application / Infrastructure 分层和 Operator / FlowExecutionService。
- ClearFrost 只能作为低配工控机运行经验参考，不允许整仓合并。
- 逆向平台的设备中心、触发中心、全局变量、运行记录、多工位统计只作为第二轮现场化参考，不进入首轮 MVP 的复杂实现。

首轮目标：
1. 新增 Acme.Product.Runtime.Abstractions。
2. 新增 Acme.Product.Runtime。
3. 新增 Acme.Product.Station，第一版 Native WinForms。
4. Station 不引用 Microsoft.Web.WebView2，不复制 wwwroot，不启动完整 Kestrel。
5. 新增 Runtime Package DTO / Loader / Validator。
6. Studio 增加 Export Runtime Package 功能。
7. Station 可加载 Runtime Package。
8. Station 可选择本地图像执行 Single Run。
9. 建立 Studio Preview / Station Single Run 一致性验证。
10. Station 支持图片目录序列运行。
11. 输出 Runtime MVP 验证报告和性能 smoke。

硬性禁止：
- 不重写 Studio。
- 不重写 FlowCanvas。
- 不复制算子实现。
- 不复制 ClearFrost DetectionService 作为 ClearVision 主运行时。
- 不接真实相机作为首版验收。
- 不写真实 PLC 作为首版验收。
- 不做独立 Engine Worker。
- 不把完整设备中心/触发中心/协议构建器塞进首轮。

验收：
- dotnet build 通过。
- Station 不含 WebView2 引用。
- Runtime Package 合法/非法加载测试通过。
- 同一 package + 同一图片，Studio 与 Station 输出一致。
- 图片序列运行 UI 不假死。
- Runtime Package 结构为后续 StationProfile、TriggerProfile、ResultMappingProfile 预留扩展空间。
```

## 16. 统一审查 Prompt

```text
请以“低配工控机上的 ClearVision Runtime MVP”标准审查这次改动。

重点检查：
1. 是否把过重的 Studio 能力错误带进 Station。
2. 是否引入 WebView2 / wwwroot / Kestrel 到 Station 或 Runtime。
3. 是否复制了算子实现或 ClearFrost DetectionService，造成双运行时。
4. 是否复用了 FlowExecutionService。
5. Runtime Package 合法/非法场景是否都能处理。
6. Station UI 是否会因为检测阻塞。
7. 图片序列运行是否可停止。
8. 图像/记录队列是否可能无限增长。
9. Studio Preview 与 Station Single Run 是否可能分叉。
10. 是否给后续设备/触发/结果映射保留了 schema 空间，但没有污染首轮 MVP。
11. 是否有足够日志用于定位 package load、validation、flow conversion、execution、normalization、queue failure。
12. 是否保持旧 Studio 功能不被破坏。

请输出：
1. 必须修改的问题。
2. 建议修改的问题。
3. 可以后续处理的问题。
4. 是否建议合入。
```


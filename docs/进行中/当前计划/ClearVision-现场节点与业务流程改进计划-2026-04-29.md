---
title: "ClearVision 现场节点与业务流程改进计划"
doc_type: "roadmap"
status: "active"
topic: "field-workflow"
created: "2026-04-29"
updated: "2026-04-29"
source:
  - "C:/Users/A/Desktop/视觉平台/reverse_output/business_decompiled_pycdc/widgets"
  - "C:/Users/A/Desktop/视觉平台/reverse_output/business_decompiled_pycdc/algorithmUI"
  - "C:/Users/A/Desktop/ClearVision/Acme.Product/src"
---

# ClearVision 现场节点与业务流程改进计划

## 1. 一句话目标

把 ClearVision 从“算子很多、画布能搭”的视觉平台，推进成“现场可交付、可启动、可诊断、可统计、可复用”的工位化检测平台。

本计划不再优先扩算子数量，而是优先补齐现场交付链路：

```text
工程包 -> 工位/流程 -> 设备/模型/变量 -> 触发 -> 执行 -> 输出映射 -> 统计复盘 -> 模板复用
```

## 2. 从逆向平台提取到的现场设计线索

| 现场能力 | 逆向平台体现 | 对 ClearVision 的启发 |
|---|---|---|
| 最近工程与项目目录 | `cfg.cache` 记录多个真实项目：自动插装、制动装置、小家电落地扇、直阀、两器喷涂等 | 平台应围绕“产线项目/工位项目”组织，而不是只围绕单个流程 JSON |
| 多流程/多工位 | `flowChartWidget` 支持多个流程 tab，`MultiStationStatsPanel` 按工位统计 OK/NG/合格率 | ClearVision 需要明确 `Station/Flow` 模型，支持一个工程下多工位、多流程、统计归属 |
| 流程画布 | `flowChartView` 基于图节点、连线、保存/加载流程 | ClearVision 已有画布，但要强化现场模板、连线校验、执行路径解释 |
| 设备中心 | `DeviceManagerWidget` 聚合相机、TCP、PLC，并提供一键启动/停止 | ClearVision 设置页已有相机/PLC配置，但缺少统一“设备中心”和一键启停诊断 |
| 触发中心 | `TriggerManagerWidget` 管理事件名称、事件类型、触发配置 | ClearVision 需要把相机软触发、硬触发、PLC、TCP、定时器统一成 Trigger 资产 |
| 图像源绑定 | `ImageSourceWidget` 与流程联动 | ClearVision 的 `ImageAcquisitionOperator` 应和工位相机、离线图片、回放数据集建立统一绑定 |
| 参数面板 | `parameter_widget` 支持设备参数、模型参数、输入源、ROI、条件显示 | ClearVision 属性面板已有分组、ROI、预览，应补设备/模型选择器和现场友好的依赖显示 |
| 模型管理 | `ModelManagerWidget` 记录模型名称、路径、任务类型、大小、应用状态 | ClearVision 需要区分 LLM 模型配置和视觉模型资产管理；视觉模型要能绑定到工位/算子 |
| 全局变量 | `GlobalVariableManager` 支持变量、目标输出、脚本、手动/定时/变量变更触发 | ClearVision 需要一套类型化变量与结果映射层，承接 PLC/TCP/HTTP/MQTT/数据库输出 |
| 通信发送/接收 | TCP/PLC 管理器、通信发送/接收 UI、协议构建器 | ClearVision 有通信算子和 PLC endpoints，但缺可视化协议帧构建和接收触发流程 |
| 运行记录 | 主界面 message table：ID、序列号、处理时间、流程、参数、结果、状态 | ClearVision 结果中心应增加现场运行台视角：当前节拍、序列号、最近 NG、异常原因 |
| 统计复盘 | 多工位按日期统计 OK/NG/合格率 | ClearVision 需要将统计从“结果查询”升级到“班次/工位/项目”维度 |

## 3. ClearVision 当前底座

ClearVision 已经具备不少基础，不需要推倒重来：

| 已有能力 | 代码/文档依据 | 判断 |
|---|---|---|
| 工程管理 | `features/project/projectManager.js`, `ProjectService` | 已有工程 CRUD、最近工程、保存流程能力 |
| 流程画布 | `features/flow-editor/flowEditorInteraction.js` | 已有拖拽、连线、多选、复制、撤销、模板入口 |
| 模板选择器 | `features/flow-editor/templateSelector.js`, `FlowTemplate` | 已有模板列表、行业/标签筛选、另存和更新模板 |
| 属性面板 | `features/flow-editor/propertyPanel.js` | 已有参数分组、ROI 编辑、节点预览、部分智能推荐 |
| 运行保护 | `features/inspection/inspectionPanel.js` | 已有连续运行保护、缺料超时、连续 NG 停止、状态解释 |
| 实时事件 | `InspectionEventEndpoints.cs` | 已有 SSE 事件、事件存储和重放 |
| PLC 配置 | `PlcEndpoints.cs`, `SettingsView` | 已有 S7/MC/FINS 配置、映射、连接测试 |
| 相机配置 | `AppConfig`, `SettingsView`, camera binding DTO | 已有相机绑定、触发模式、帧率等基础字段 |
| 结果统计 | `InspectionResult`, result panel, inspection stats | 已有结果存储和统计基础 |
| 算子库 | `docs/算子资料/算子目录.md` | 155 个算子，算子能力很强 |

关键判断：ClearVision 的平台底座已经比逆向平台更规整；短板不在“没有算子”，而在“现场工程包、设备启动、触发事件、变量映射、运行台和模板闭环”还没有变成主路径。

## 4. 产品方向调整

### 4.1 从“算子平台”转向“现场工位平台”

不要让现场用户先面对 155 个算子。推荐主路径：

```text
选择行业/场景模板
-> 选择工位和设备
-> 绑定相机/PLC/模型
-> 只调 3-8 个白名单参数
-> 单节点预览
-> 单次试跑
-> 连续运行
-> 统计与异常复盘
```

### 4.2 LLM 的位置

LLM 不应该直接自由编排全量算子库，而应该负责：

- 帮用户选模板。
- 解释每个节点在现场流程里的作用。
- 根据预览指标建议调整白名单参数。
- 根据报警和运行记录生成排查建议。
- 帮工程师把一个成熟流程沉淀为模板。

## 5. 改进路线图

## Phase 1：现场工程包与工位模型

目标：让一个 ClearVision 工程不仅保存流程，还保存现场交付所需的一整包配置。

建议新增概念：

```json
{
  "workcellProfile": {
    "projectName": "直阀检测",
    "lineName": "A线",
    "stations": [
      {
        "id": "station-1",
        "name": "上料检测工位",
        "flowId": "flow-main",
        "cameraBindings": ["cam-top"],
        "plcProfile": "s7-main",
        "triggerProfile": "plc-start-signal",
        "resultMappingProfile": "ok-ng-writeback",
        "modelBindings": ["yolo-main"]
      }
    ]
  }
}
```

任务：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-1 | 定义 `WorkcellProfile` / `StationProfile` / `RuntimeBinding` 数据结构 | `Acme.Product.Core.Entities` 或新建 `RuntimeProfiles` | 工程 JSON 可保存工位、设备、触发、模型、变量、输出映射 |
| FW-2 | 工程导入/导出时包含完整现场包 | `ProjectService`, `ProjectSerializer` | 换机器导入后能看见缺失设备/模型清单 |
| FW-3 | 在工程页增加“现场包完整性检查” | `projectView.js` + API | 能列出相机未绑定、PLC 未配置、模型文件缺失、输出映射缺失 |
| FW-4 | 支持一个工程多工位多流程 | `OperatorFlowDto`, `Project` | 统计与运行结果可按工位归属 |

优先级：P0。

## Phase 2：统一设备中心与触发中心

目标：把相机、PLC、TCP、触发事件从分散设置变成现场调试面板。

逆向平台值得借鉴的交互：

- 一键启动所有设备。
- 一键停止所有设备。
- 相机/TCP/PLC 分页管理。
- 触发设置独立入口。
- 设备添加后可被流程节点选择。

ClearVision 改进方案：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-5 | 新增“设备中心”视图，聚合相机、PLC、TCP、串口、HTTP/MQTT | `features/settings` 或新增 `features/device-center` | 设备状态、连接测试、启停、最近错误在一个面板完成 |
| FW-6 | 设备运行状态 API | Desktop endpoints + Runtime coordinator | 前端能看到 `Disconnected/Connecting/Ready/Running/Error` |
| FW-7 | 新增触发中心 | Core runtime profile + 前端视图 | 支持手动、定时、相机、PLC、TCP、变量变更触发 |
| FW-8 | Trigger 与 Flow 绑定 | `StationProfile.TriggerProfile` | 指定触发事件后能启动对应工位流程 |
| FW-9 | 设备模拟器 | tests/tools 或 infrastructure simulator | 无真实硬件时可演练触发、读写、超时和异常 |

优先级：P0/P1。

## Phase 3：视觉模型资产管理

目标：把视觉模型作为现场资产管理，而不是散落在某个算子的路径参数里。

当前 ClearVision 有 AI/LLM 模型配置和深度学习算子模型路径，但还需要独立的视觉模型管理体验。

任务：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-10 | 新增 `VisionModelAsset` | Core/Infrastructure | 字段包含名称、任务类型、模型路径、标签文件、输入尺寸、版本、校验值 |
| FW-11 | 模型导入校验 | Desktop endpoint | 导入 ONNX 后能校验文件存在、大小、hash、任务类型 |
| FW-12 | 模型绑定到算子和工位 | PropertyPanel + WorkcellProfile | 深度学习、分割、OCR、条码等节点可从模型资产选择 |
| FW-13 | 模型迁移报告 | Project import/export | 缺失模型时给出可读提示，而不是运行时报错 |

优先级：P1。

## Phase 4：全局变量与结果映射

目标：把检测结果稳定写回现场系统，让流程输出从“页面里看见”变成“设备侧收到”。

借鉴逆向平台：

- 全局变量表。
- 变量当前值。
- 输出目标配置。
- 手动/定时/变量变更触发。
- 简单脚本计算输出值。

ClearVision 设计建议：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-14 | 新增类型化变量表 | Core profile + frontend panel | 支持 Bool/Int/Double/String/Json/ImageRef |
| FW-15 | 新增结果映射器 | `ResultOutputOperator` 扩展或单独 `ResultMappingProfile` | 能把 OK/NG、分数、框数量、测量值映射到 PLC/TCP/HTTP/MQTT/DB |
| FW-16 | 安全表达式引擎 | Infrastructure utility | 支持有限表达式，不允许任意系统访问 |
| FW-17 | 输出回执与失败重试 | Runtime coordinator | 输出失败可追踪、可重试、可报警 |
| FW-18 | 输出映射预演 | 前端调试面板 | 不跑相机也能用一条样例结果测试写回 |

优先级：P0/P1。

## Phase 5：协议构建器

目标：让工程师能可视化配置 TCP/串口/自定义协议帧，而不是把协议逻辑散落在脚本或算子参数里。

任务：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-19 | 新增 `ProtocolTemplate` | Core entity | 支持固定头、字段、长度、校验、尾部 |
| FW-20 | 可视化协议构建 UI | `features/device-center` | 能拖/选字段：序列号、OK/NG、测量值、时间戳 |
| FW-21 | 协议预览与十六进制显示 | 前端 + endpoint | 输入样例结果后显示最终帧 |
| FW-22 | 协议接收解析 | Runtime device layer | 接收到帧后能解析字段并触发流程或写变量 |

优先级：P2。  
说明：这是逆向平台 UI 里很有现场价值的点，但要排在工位、设备、触发、映射之后。

## Phase 6：运行台与多工位统计

目标：给操作员一个“能上产线”的界面，而不是让操作员停留在流程编辑器里。

建议运行台布局：

```text
左：当前工位/设备状态
中：实时图像与叠加结果
右：当前节拍、序列号、OK/NG、报警、最近 10 条结果
底：运行日志、输出回执、保护规则解释
```

任务：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-23 | 新增 Operator Mode / Run Desk | `features/inspection` | 操作员无需进入画布即可启动/停止/查看结果 |
| FW-24 | 多工位统计面板 | result/inspection panel | 按日期、班次、工位统计 OK、NG、总数、合格率、节拍 |
| FW-25 | 异常原因分类 | Inspection events | 缺料超时、相机断开、PLC 写回失败、模型缺失、算子异常可区分 |
| FW-26 | 运行记录导出 | Result exporter | 可导出 CSV/JSON/现场日报 |
| FW-27 | 回放模式 | Inspection service + dataset | 用历史图片重跑流程，验证模板和参数 |

优先级：P1。

## Phase 7：场景模板升级为“现场配方”

目标：模板不只是流程 JSON，而是可交付的场景配方。

配方应包含：

- 适用行业和工位。
- 必需设备。
- 必需模型。
- 输入图像约束。
- 关键可调参数白名单。
- 每个节点的预览指标。
- 验收指标。
- 常见异常与排查建议。

建议先做 4 个黄金场景：

| 场景 | 价值 | 模板重点 |
|---|---|---|
| 线序检测 | 贴近现有黄金场景资料 | 颜色/排序/位置判断、OK/NG 输出 |
| 模板定位 + 宽度测量 | 传统视觉高频 | 模板匹配、坐标补正、线/宽测量 |
| Blob 缺陷区域分析 | 工业缺陷高频 | 阈值、形态学、Blob、面积过滤 |
| OCR/条码追溯 | 现场追溯高频 | OCR/条码、序列号、数据库/PLC 写回 |

任务：

| 编号 | 任务 | 建议落点 | 验收口径 |
|---|---|---|---|
| FW-28 | 模板元数据扩展为 Recipe | `FlowTemplate` | 增加设备、模型、关键参数、验收指标 |
| FW-29 | 模板创建向导 | `templateSelector.js` | 从模板创建时要求选择工位、相机、PLC 和模型 |
| FW-30 | 白名单参数调优 | `propertyPanel.js`, AutoTune | 每个模板只开放少量关键参数给现场人员 |
| FW-31 | 模板验收脚本 | tests/quality | 每个模板至少有样例图和回放验收 |

优先级：P0/P1。

## 6. 分阶段排期

| 时间 | 目标 | 必须交付 |
|---|---|---|
| 第 1 周 | 现场工程模型收敛 | `WorkcellProfile` 草案、工位/设备/触发/结果映射字段、完整性检查原型 |
| 第 2-3 周 | 设备中心和触发中心 | 设备状态面板、一键启动/停止、手动/定时/PLC 触发配置、模拟器 |
| 第 4-5 周 | 变量与结果映射 | 类型化变量、OK/NG 写回、输出回执、失败重试、映射预演 |
| 第 6 周 | 运行台 | 操作员模式、多工位统计、异常分类、运行日志 |
| 第 7-8 周 | 场景配方 | 4 个黄金场景模板、白名单参数、回放验收 |

## 7. P0/P1/P2 优先级清单

| 优先级 | 事项 | 原因 |
|---|---|---|
| P0 | WorkcellProfile / StationProfile | 没有现场工程包，就无法组织设备、触发、模型、输出和统计 |
| P0 | 结果映射与写回 | 现场检测最终要反馈给 PLC/上位系统，仅页面展示不够 |
| P0 | 模板配方化 | 降低现场使用门槛，避免用户面对全量算子 |
| P1 | 设备中心与一键启停 | 调试效率和现场稳定性关键 |
| P1 | 触发中心 | 将相机/PLC/TCP/定时/变量变更统一成可配置事件 |
| P1 | 运行台 | 操作员需要运行界面，不应该进入工程编辑界面 |
| P1 | 视觉模型资产管理 | 深度学习类流程必须可迁移、可诊断、可版本化 |
| P2 | 协议构建器 | 很有现场价值，但可在结果映射稳定后做 |
| P2 | 高级回放/班次报表 | 先保证基本运行闭环，再强化复盘能力 |

## 8. 不建议现在做的事

- 不继续把重点放在扩充第 156、157 个算子。
- 不让现场人员直接从全量算子库自由搭流程。
- 不让 LLM 在没有预览指标和模板先验的情况下自由调参数。
- 不先做所有相机厂商 SDK，再做流程闭环；建议先用统一接口和模拟器跑通，再接真实厂商。
- 不把协议、变量、写回都塞进脚本；脚本只能作为高级扩展，主路径必须可视化、可验证。

## 9. 验收标准

一个版本是否真正完成“现场节点与业务流程”改进，不看新增了多少算子，而看下面这些事情是否顺：

- 新建一个“直阀检测”工位工程，能选择模板、绑定相机、绑定 PLC、绑定模型。
- 没有真实硬件时，能用模拟器跑完整触发、检测、写回、统计链路。
- 缺相机、缺模型、PLC 断线时，工程完整性检查能提前报出。
- 操作员在运行台能完成开始、停止、看结果、看报警、看统计。
- 工程师能把一个成熟流程另存为带设备/模型/参数白名单的场景配方。
- 任意一个黄金场景都能通过回放验收，证明不是只在 UI 上“看起来能用”。

## 10. 总结

逆向平台给 ClearVision 的最大启发是：现场平台要围绕“工程、设备、触发、变量、输出、统计”组织，而不是围绕单个算法组织。

ClearVision 的算法和平台底座已经更强，下一步最应该补的是现场主路径：

```text
场景配方化
-> 工位工程包
-> 设备/触发中心
-> 变量与结果写回
-> 操作员运行台
-> 回放与统计复盘
```

这条路走通后，ClearVision 会更像一套能落地到产线的内部视觉系统，而不是一套功能很多但需要工程师长期陪跑的算法工具箱。


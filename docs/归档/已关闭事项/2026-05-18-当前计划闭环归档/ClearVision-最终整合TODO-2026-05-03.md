---
title: "ClearVision 最终整合 TODO"
doc_type: "task-list"
status: "active"
topic: "consolidated-master-plan"
created: "2026-05-03"
updated: "2026-05-03"
consolidated_from:
  - "ClearVision-审计整改TODO-2026-04-29.md"
  - "ClearVision-现场节点与业务流程改进计划-2026-04-29.md"
  - "ClearVision-ClearFrost分体式Runtime现场化整合计划-2026-04-29.md"
  - "ClearVision-ClearFrost分体式Runtime现场化落地TODO-2026-05-02.md"
  - "ClearVision-Station现场可调参数Profile机制定稿TODO-2026-05-03.md"
  - "ClearVision_Virtual_Modbus_PLC_Codex_TODO.md"
archived:
  - "ClearVision-Station现场可调参数Profile机制定稿TODO-2026-05-03.md -> docs/归档/已关闭事项/2026-05-03-当前计划闭环归档/"
  - "ClearVision-Station现场可调参数Profile机制TODO-2026-05-03.md -> docs/归档/已关闭事项/2026-05-03-当前计划闭环归档/"
---

# ClearVision 最终整合 TODO

> 本文是所有当前计划的整合版。已完成的计划已归档到 `docs/归档/已关闭事项/2026-05-03-当前计划闭环归档/`。
> 本文只保留**尚未完成**的工作，按优先级和依赖关系分层排列。

---

## 已完成归档总览

| 计划 | 完成日期 | 归档位置 |
|---|---|---|
| Station 现场可调参数 Profile 机制 V1 | 2026-05-03 | `docs/归档/已关闭事项/2026-05-03-当前计划闭环归档/` |
| Station Profile 机制草案（被定稿取代） | 2026-05-03 | 同上 |

**已验证通过的能力：**
- Runtime Package 导出 `field/runtime-parameters.json` 和 `field/station-profile.default.json`
- DeepLearning.Confidence 通过 schema 暴露为现场参数
- Station 通用渲染 NumericUpDown，移除 ONNX 专用 UI
- Station 本地 profile 保存/加载/恢复默认
- RuntimeHost clone flow + apply override，不污染包默认值
- 旧包无 schema 仍可运行

---

## Tier 1：短期收口（本周内）

这些是已完成主体但还有尾巴的工作，或阻塞后续任务的前置项。

### T1-1：审计整改 — 人工密钥轮换确认

| 字段 | 内容 |
|---|---|
| 来源 | `ClearVision-审计整改TODO-2026-04-29.md` P0-1 |
| 状态 | 代码止血已完成，旧 key 轮换待人工确认 |
| 阻塞 | 否（不阻塞开发，但阻塞审计闭环） |

任务：

- [ ] 人工登录 AI 服务商后台，确认旧 key 已失效
- [ ] 在审计计划中登记轮换结论和时间

---

### T1-2：审计整改 — CI 测试实跑留证

| 字段 | 内容 |
|---|---|
| 来源 | `ClearVision-审计整改TODO-2026-04-29.md` P0-2 |
| 状态 | CI 配置已落地，等待下一次 CI 实跑 |

任务：

- [ ] 触发一次 CI，确认日志中可见 `Acme.Product.Tests.dll` 和 `Acme.Product.Desktop.Tests.dll` 测试数
- [ ] 确认 CI artifact 收集了 `.trx` 文件
- [ ] 在审计计划中登记 CI 证据链接

---

### T1-3：虚拟 Modbus PLC 接入

| 字段 | 内容 |
|---|---|
| 来源 | `ClearVision_Virtual_Modbus_PLC_Codex_TODO.md` |
| 状态 | 全部未开始 |
| 优先级 | P0（后续 PLC 联调、触发中心、结果映射的基础） |
| 预估 | 2-3 天 |

任务：

- [ ] 新增 `tools/virtual-plc/modbus/` 目录：`virtual_plc_modbus.py`、`test_client.py`、`requirements.txt`、`Dockerfile`、`docker-compose.yml`、`README.md`
- [ ] 实现 Modbus TCP 虚拟 PLC 服务端（支持 ReadCoils / ReadHolding / WriteSingle / WriteMultiple）
- [ ] 实现点位表：HR0 COMMAND / HR1 STATUS / HR2 SEQUENCE / HR3 SEQUENCE_ECHO / HR10 TEST_VALUE
- [ ] 实现握手逻辑（正常流程 + 错误流程）
- [ ] 实现 Python smoke test 客户端
- [ ] 实现 Docker 支持
- [ ] 新增 `scripts/start-virtual-modbus-plc.ps1` 和 `scripts/test-virtual-modbus-plc.ps1`
- [ ] 用 ClearVision `ModbusCommunicationOperator` 人工验证读写和握手
- [ ] 可选：新增 `ModbusCommunicationOperatorVirtualPlcTests.cs` 集成测试

验收：

- [ ] `test_client.py` smoke test 通过
- [ ] `ModbusCommunicationOperator` 能连接 127.0.0.1:1502 读写寄存器和完成握手
- [ ] 没有修改 `PlcClientFactory.cs`、`PlcEndpoints.cs`、`AppConfig.cs`

---

## Tier 2：现场化 MVP 补全（2-4 周）

Runtime MVP 骨架已完成，这些是进入稳定试点前必须补齐的能力。

### T2-1：模拟 PLC/TCP 输出写回

| 字段 | 内容 |
|---|---|
| 来源 | `Runtime落地TODO` P1 + `现场节点计划` Phase 4 |
| 状态 | 未实现 |
| 优先级 | P0（现场闭环核心：检测结果必须能写回设备） |

任务：

- [ ] 新增 `RuntimeOutputSink` 抽象接口
- [ ] 实现 `JsonlOutputSink`（写入 JSONL 文件）
- [ ] 实现 `MockPlcOutputSink`（模拟 PLC 写回成功/失败/超时）
- [ ] `result-mapping-profile.json` 支持 OK/NG、测量值、错误码映射到输出目标
- [ ] Station 可预演输出写回（不跑相机也能测试映射）
- [ ] 写回失败有回执、重试、报警

验收：

- [ ] 模拟写回成功/失败/超时三种场景可测
- [ ] 运行记录包含输出回执

---

### T2-2：Package 自动回滚机制

| 字段 | 内容 |
|---|---|
| 来源 | `Runtime落地TODO` P1 |
| 状态 | last-good 指针有；自动回滚逻辑未实现 |

任务：

- [ ] Station 加载包失败时，自动提示并提供恢复 last-good package 选项
- [ ] 加载成功后自动更新 last-good 指针
- [ ] 失败包不替换 last-good

验收：

- [ ] 故意加载一个非法包，Station 提示恢复上次成功包
- [ ] 恢复后能正常运行

---

### T2-3：工控机性能基线采集

| 字段 | 内容 |
|---|---|
| 来源 | `Runtime落地TODO` P1 |
| 状态 | 软件基线已建立；工控机硬件数字待采集 |

任务：

- [ ] 在目标工控机上运行 `runtime-performance-smoke`
- [ ] 记录冷启动、包加载、单张运行、目录运行、停止耗时
- [ ] 记录内存前后差、队列峰值
- [ ] 更新 `quality/runtime/runtime-performance-smoke.md`

---

### T2-4：Runtime 落地 TODO P2 稳定性收口

| 字段 | 内容 |
|---|---|
| 来源 | `Runtime落地TODO` M6 |
| 状态 | 部分完成 |

剩余任务：

- [ ] 异常分级完善：`PackageInvalid / FlowInvalid / ExecutionFailed / ResourceMissing / Canceled / OutputFailed`
- [ ] 日志字段标准化：`RunId/PackageId/FlowHash/ImageId/StationId` 全链路可查
- [ ] 崩溃恢复记录完善（上次异常退出和最后 run id 提示）

---

## Tier 3：现场能力扩展（1-2 月）

从"能跑"到"能交付产线"的关键能力。

### T3-1：设备中心（轻量版）

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 2 + `Runtime落地TODO` M7 |
| 优先级 | P1 |

任务：

- [ ] Station 新增"设备中心"视图，聚合相机、PLC、TCP 状态
- [ ] 设备运行状态 API：`Disconnected/Connecting/Ready/Running/Error`
- [ ] 一键启动/停止所有设备
- [ ] 连接测试
- [ ] 先接 File/Mock camera，再接一个真实相机

---

### T3-2：触发中心

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 2 + `Runtime落地TODO` M7 |
| 优先级 | P1 |

任务：

- [ ] `trigger-profile.json` 启用：支持 Manual / Timer / Replay 触发
- [ ] Trigger 与 Flow 绑定
- [ ] 后续支持 PLC/TCP 触发（依赖 T1-3 虚拟 PLC）

---

### T3-3：现场配方（4 个黄金场景）

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 7 |
| 优先级 | P1 |

任务：

- [ ] 模板元数据扩展为 Recipe：增加设备、模型、关键参数、验收指标
- [ ] 4 个黄金场景模板：
  - 线序检测
  - 模板定位 + 宽度测量
  - Blob 缺陷区域分析
  - OCR/条码追溯
- [ ] 每个模板有白名单参数和回放验收

---

### T3-4：视觉模型资产管理

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 3 |
| 优先级 | P1 |

任务：

- [ ] 新增 `VisionModelAsset`：名称、任务类型、路径、标签、输入尺寸、版本、hash
- [ ] 模型导入校验
- [ ] 模型绑定到算子和工位
- [ ] `model-assets.json` 启用

---

### T3-5：审计整改 — 真实现场样本回灌

| 字段 | 内容 |
|---|---|
| 来源 | `审计整改TODO` P2-1 |
| 状态 | 证据分层完成；真实现场样本待人工提供 |

任务：

- [ ] 人工提供 TemplateMatching、CaliperTool、DeepLearning、SurfaceDefectDetection、CameraCalibration 的脱敏真实样本
- [ ] 每个样本有 manifest、复现命令、triage 标签
- [ ] 至少 1 条真实 field replay 闭环进入报告

---

### T3-6：审计整改 — 硬件诊断 UI/API 暴露

| 字段 | 内容 |
|---|---|
| 来源 | `审计整改TODO` P2-4 |
| 状态 | 静默失败止血完成；UI/API 诊断面待补 |

任务：

- [ ] 在 UI 或诊断 API 中暴露相机/PLC/GPU "不可用原因"
- [ ] 为硬件不可用、SDK 缺失、权限不足、设备占用写 mock/contract 测试

---

### T3-7：审计整改 — 性能趋势 CI 门禁

| 字段 | 内容 |
|---|---|
| 来源 | `审计整改TODO` P2-2 |
| 状态 | 趋势脚本与基线完成；CI 门禁预算待接入 |

任务：

- [ ] 确定 quick smoke / nightly heavy / release gate 三种性能预算
- [ ] CI 中接入性能预算门禁

---

## Tier 4：高级现场能力（3+ 月）

产线稳定运行后的深度能力。

### T4-1：全局变量与结果映射

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 4 |

任务：

- [ ] 类型化变量表：Bool/Int/Double/String/Json/ImageRef
- [ ] 安全表达式引擎（有限表达式，不允许任意系统访问）
- [ ] 输出回执与失败重试

---

### T4-2：协议构建器

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 5 |

任务：

- [ ] `ProtocolTemplate`：固定头、字段、长度、校验、尾部
- [ ] 可视化协议构建 UI（Studio 侧，Station 只执行模板）
- [ ] 协议接收解析

---

### T4-3：运行台与多工位统计

| 字段 | 内容 |
|---|---|
| 来源 | `现场节点计划` Phase 6 |

任务：

- [ ] Operator Mode / Run Desk：操作员无需进入画布即可启停看结果
- [ ] 按日期/班次/工位统计 OK/NG/总数/合格率/节拍
- [ ] 异常原因分类：缺料超时、相机断开、PLC 写回失败、模型缺失、算子异常
- [ ] 运行记录导出 CSV/JSON/现场日报

---

### T4-4：真实硬件接入

| 字段 | 内容 |
|---|---|
| 来源 | `Runtime落地TODO` M7 P2 |

顺序：

1. 真实相机连续采集
2. 真实 PLC trigger
3. 真实 PLC/TCP writeback
4. 多工位统计

---

### T4-5：Profile 机制 V2+

| 字段 | 内容 |
|---|---|
| 来源 | `Profile机制定稿TODO` V2+ 停车场 |

推荐下一轮组合（互相无依赖，可并行）：

- T1-a：Bool / Enum / String 参数类型扩展（1-2 天）
- T1-b：Profile 导入/导出（1 天）
- T3-b：参数修改历史 history.jsonl（1 天）

后续：

- T2-a：光源/曝光/PLC 超时等硬件参数（3-5 天）
- T2-b：ROI 参数（3-5 天）
- T2-c：Path / PathPicker 参数（1-2 天）
- T2-d：Immediate / NextFrame 热更新（3-5 天）
- T3-c：Studio 导入现场 profile 并选择性吸收（3-5 天）
- T3-d：跨包版本的 profile 迁移（3-5 天）

---

## 推荐执行节奏

| 时间 | 目标 | 必须交付 |
|---|---|---|
| 本周 | Tier 1 收口 | 密钥轮换确认、CI 留证、虚拟 Modbus PLC |
| 第 2-3 周 | Tier 2 补全 | 模拟 PLC 写回、自动回滚、工控机性能基线、稳定性收口 |
| 第 4-6 周 | Tier 3 现场化 | 设备中心、触发中心、4 个黄金场景配方、模型资产 |
| 第 7+ 周 | Tier 4 深度能力 | 协议构建、运行台、真实硬件、Profile V2+ |

---

## 状态看板

| 分组 | 总数 | 未开始 | 进行中 | 已完成 |
|---|---:|---:|---:|---:|
| Tier 1 短期收口 | 3 | 2 | 1 | 0 |
| Tier 2 MVP 补全 | 4 | 4 | 0 | 0 |
| Tier 3 现场能力 | 7 | 7 | 0 | 0 |
| Tier 4 高级能力 | 5 | 5 | 0 | 0 |
| **合计** | **19** | **18** | **1** | **0** |

> Tier 1 的 T1-1（密钥轮换）和 T1-2（CI 留证）属于"等人确认"状态，不阻塞开发。
> Tier 1 的 T1-3（虚拟 Modbus PLC）是 Tier 3 触发中心和结果映射的前置依赖。

---

## 闭环出口

当满足以下条件时，本计划可以关闭并归档：

- [ ] Tier 1 全部完成
- [ ] Tier 2 全部完成
- [ ] Tier 3 至少完成设备中心、触发中心、1 个黄金场景配方
- [ ] 现场试点至少跑通一个完整闭环：模板 -> 包 -> 设备 -> 触发 -> 检测 -> 写回 -> 统计
- [ ] 新增闭环记录
- [ ] 将本文迁移到 `docs/归档/已关闭事项/` 

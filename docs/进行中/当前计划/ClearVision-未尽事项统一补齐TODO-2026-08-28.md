---
title: "ClearVision 未尽事项统一补齐 TODO"
doc_type: "plan"
status: "active"
topic: "跨计划闭环治理"
created: "2026-08-28"
updated: "2026-08-28"
baseline: "c504ae1919cca0ff4df993c956c45742440fc471"
---

# ClearVision 未尽事项统一补齐 TODO

> 本文是 2026-08-28 起七组保留文档的唯一横向责任入口。专项计划继续保留为执行规格，原始排查记录继续保留为证据快照；在本文全部关闭前，不移动或归档用户指定的七组文档。
>
> 只认当前分支 HEAD 的代码、测试与可追溯证据。仅存在于其他分支、stash、历史工作树或旧 CI SHA 的修复，不计为已闭环。

## 1. 状态口径

- `CLOSED_SYNC_ONLY`：当前 HEAD 已有实现/测试或“不再存在”证据，只需同步原文状态。
- `OPEN_CONFIRMED`：当前 HEAD 仍能由源码直接复现原问题，必须进入本计划。
- `EVIDENCE_REQUIRED`：实现可能已具备，但尚无绑定当前 HEAD 的验收证据；在补证前按未闭环管理。
- `BLOCKED_EXTERNAL`：必须依赖真实设备、授权实验室、现场数据或远端 CI 才能关闭。

统一关闭条件：每个条目必须同时具备当前 HEAD 的实现或否定结论、针对性回归、同 SHA 的验证结果、残余边界和源文档回填。仅提交代码、仅本地通过或仅更新文档都不算闭环。

## 2. 七组源文档复核结论

| 源文档 | 当前事实 | 本计划承接 |
| --- | --- | --- |
| [全面提升 TODO](./ClearVision-全面提升TODO-2026-05-09.md) | 147 个 checkbox：原文已勾 3 项；144 个未勾项中，100 项属于实现已完成但文档未同步，39 项归属于 13 个仍开放/需补证主题，末尾 5 项是随总计划验收的关闭条件 | U01-U06、U10、U12、U14 |
| [T01 测试与覆盖率治理总体计划](./测试治理/ClearVision_T01_测试与覆盖率治理总体计划_PROPOSED_AUDITED.md) | G01 主体已有阶段证据，但 G01B-R3 与 G02-G09 未闭环；G07 受 Studio2 G16 阻断，G09 依赖外部实验室 | U05-U07 |
| [Studio2](../Studio2/README.md) | G15.1 头部状态是文档同步错误，已更正为 `DONE`；G16 的唯一 production root、真实 WebView2、无 Node 目标机、DPI/分辨率矩阵和 GitHub CI 证据仍缺 | U05 |
| [持续问题排查记录](../待复核/持续问题排查记录-2026-07-06.md) | 102 项逐项对照当前 HEAD：30 项 `CLOSED_SYNC_ONLY`，72 项 `OPEN_CONFIRMED` | U08-U13 |
| [0407 Qwen 排查](../未闭环事项/0407-Qwen排查未闭环.md) | #1-#26 已由当前实现、文件移除或等价契约收敛覆盖，原“部分闭环”状态未同步 | U14 只负责回填 |
| [0418 临时问题记录](../未闭环事项/0418-临时问题记录.md) | 10 组实现已由 `6bff6f10` 与 `cc0d48d9` 及后续算子质量提交落地；原日志缺最终结果 | 工业证据尾项去重并入 U01/U03/U13，U14 回填 |
| [深度学习算子问题](../未闭环事项/深度学习算子问题.md) | 灰度/16-bit 与工程化契约已补齐；NMS/异步加载已改造，但仍有整张 tensor 复制，且缺真实模型、高负载、provider 与现场签核证据 | U01 |

## 3. 统一执行账本

### U01 DeepLearning 质量声明、性能与真实模型证据

来源：P0-9、深度学习算子性能残项、0418 工业证据尾项。Owner：算法/算子质量。

- [ ] 将 `DeepLearning_coco_real_model_baseline` 明确标为 `InferenceSmokeOnly`，或重生为带模型 hash、数据集版本、标签契约及非零 AP50/Precision/Recall 门槛的精度报告；不得再让 `Accepted=True` + 全 0 指标被读成精度验收。
- [ ] 去除或证明 `PreprocessImage()` 整张 tensor 复制不会突破预算；补真实 ONNX、灰度/16-bit、高候选量、高帧率、多线程缓存淘汰和 CPU/CUDA/TensorRT provider 选择基准。
- [ ] 报告必须绑定当前 SHA、硬件/驱动/模型与数据 checksum、p50/p95/峰值内存、失败路径和回滚结论。

### U02 数据库 schema 与生产上下文唯一权威

来源：P1-7。Owner：持久化/架构。

- [ ] 让 `VisionDbContext` 成为唯一生产上下文；删除 `AppDbContext`，或明确标记为 deprecated/non-production 并增加 architecture guard 防止误注入。
- [ ] 统一 Station sync 表的迁移机制，清除 `Program.cs` 手写 DDL 与 EF model 的双真相源；旧库升级、新库初始化和回滚测试必须通过。

### U03 场景包、算子人口与人工证据治理

来源：P1-14、P1-16、P2-6、P2-7。Owner：算子平台/质量工程。

- [ ] 对场景包 `PublishChecks` 建立可执行 gate：校验可提交资产 hash、外部模型 manifest、`parametersNeedingReview` 和零 ROI；负例必须阻断导入/发布。
- [ ] `OperatorModuleCatalog` 不再用 `Enum.GetValues` 默认公开全部枚举；新枚举必须显式分类为 public/internal/alias，FrameChangeTrigger 的包内边界需有测试。
- [ ] 为 Core20 建立 reviewer/date/card fingerprint/结论台账，人工边界、失败模式、典型 I/O 和不可用场景不得被生成器重跑覆盖。
- [ ] full155 固定人口改为从当前受治理 catalog 动态取数（当前文档人口为 158）；新增算子缺 evidence entry 即失败，并输出 Contract/Golden/Dataset/Field replay 的 delta/trend。

### U04 SDK、现场发布包与供应链证据

来源：P1-20、P1-23、P2-8。Owner：构建发布/供应链。

- [ ] 在 `global.json` 的 `latestFeature` 与更严格策略中选定并统一口径；README、项目总览、SDK 指南、CI 与 `scripts/dotnet.ps1` 使用同一可验证版本契约。
- [ ] 明确 GitHub Release 是 demo/raw 还是现场包；若面向现场，CI 必须调用 portable packaging 或等价流程，并包含一致的启动名、离线依赖说明和 `README-site-deploy.txt`。
- [ ] 从最终 nupkg/发布包生成并核验 SPDX，随 release 上传 SBOM、THIRD-PARTY-NOTICES、dependency-report；定义漏洞/许可证 fail-or-report 策略并处置 `S7NetPlus` 的 `NOASSERTION`。

### U05 Studio2 G16、Legacy 退场与 T01-G07

来源：Studio2 G16、P2-2、T01-G07。Owner：Desktop/Studio2。

- [ ] `Studio:WorkspaceV2Enabled=true`，`/v2/index.html` 成为唯一 production composition root，旧 `app.js` 不再是第二业务 root。
- [ ] 为 `legacyGlobals.js` 每个全局对象登记 owner、保留原因与替换路径；capability owner 接管后删除 accessor、过期 adapter、flag 和 `window.*` 状态。
- [ ] 完成真实 WebView2、无 Node 目标机离线启动、100/125/150/200% DPI、1366/1080/1440/4K、300/1000 primitive 性能与生命周期矩阵。
- [ ] clean clone build/publish、旧工程/旧 package/Station/Agent/Project save 回归及同 SHA GitHub CI 全绿后，才能关闭 G16、T01-G07；Studio2 Goal 卡继续整批保留。

### U06 T01 当前 HEAD 测试治理与覆盖率趋势

来源：G01B-R3、G02-G06、G08、P2-9。Owner：测试/CI。

- [ ] G01B-R3：对 21 个历史 UI fixture 逐项给出“已消失/产品回归/fixture 过期/环境问题”，定向 7 spec、UI 全量及同 SHA 远端 CI 均有 run URL 和 artifact。
- [ ] G02：同一最终 SHA 的 Safe CI、Agent Quality、UI/主 CI 全绿，历史 SHA 不得替代。
- [ ] G03：形成 A/B/C 测试分类、Owner、批准 baseline+tolerance；删除任一 A 类测试必须触发 gate 失败。
- [ ] G04：为关键状态转换补成功、拒绝、取消/异常、恢复的公共合同 Oracle，不新增未经批准的私有反射测试。
- [ ] G05：同环境至少 5 次 repeat，输出 machine-readable flake registry、p50/p95、失败签名和显式 retry 记录。
- [ ] G06：所有 Gate 报告绑定 HEAD、dirty=false、tool/data checksum 和环境指纹；blocked field 项继续阻断。
- [ ] G08/P2-9：基于多个绿色 SHA 建 report-only line/branch/关键命名空间/changed-code 趋势，覆盖下降、模块缺失、批准 baseline update 三类自测后再决定 blocking。

### U07 T01-G09 外部实验室与现场证据

来源：G09。Owner：现场验证。状态：`BLOCKED_EXTERNAL`。

- [ ] 在获授权环境完成真实 PLC、相机、Station、数据、LLM shadow 与 WebView2 人工验收。
- [ ] 证据记录 Owner、SHA、设备型号/序列号、固件、数据 checksum、pass/fail、异常恢复和回滚；无真实环境时不得用模拟结果宣称关闭。

### U08 身份、权限、Owner 与危险操作边界

来源：`CV-AUDIT-011,014,015,018,023,024,025,028,034,036,064,077,091`。Owner：Desktop 安全/权限。

- [ ] 后端 API、WebMessage bridge、Station ingress 和 artifact/image/session 读取均绑定角色、用户/站点 Owner 与资源存在性；保留最后一个 Admin，路径写入限制在批准 root。
- [ ] 前端对非授权角色隐藏或禁用 Station 命令、设置保存、AI 管理、数据库维护，并以服务端 `/auth/me` 和密码策略为权威。
- [ ] 为跨用户、跨站点、软删除资源、伪造 GUID/sessionId/commandId、最后 Admin 和路径逃逸补负例；403/409/422 语义与 UI 一致。

### U09 配置、项目与 AI 持久化的一致性

来源：`CV-AUDIT-006,009,012,021,029,040,041,042,044,069,070,079,080,082,083,089`。Owner：持久化/设置/AI。

- [ ] 所有读改写采用 expected revision、单一串行 authority 或等价 CAS；禁止锁外旧快照覆盖、GET 触发重置、加载失败后默认值擦写真实配置。
- [ ] 多文件/多存储提交使用唯一临时文件、原子 replace、失败回滚和恢复 journal；相机运行态只在持久化成功后切换，AI metrics 失败不得反向判主流程失败。
- [ ] 注入磁盘满、权限、并发、崩溃、旧 revision、半写 metadata、恢复/清理并行等故障，验证重启后一致且 API 不伪成功。

### U10 执行准入、副作用与运行状态隔离

来源：P1-2；`CV-AUDIT-032,048,049,050,051,052,053,055,056,065,068,072`。Owner：Runtime/执行安全。

- [ ] 明确 inline FlowData 的可信主体和允许面；文件、HTTP、数据库、标定、TCP/串口/PLC 等副作用必须有角色、项目 authority、allowlist/sandbox、审计与取消边界。
- [ ] AutoTune 所有入口在服务端统一 clamp/拒绝迭代上限；客户端值不能决定无限工作量。
- [ ] 变量、统计、计时、帧窗口、TriggerModule 状态使用 project/session/flow/run/operator 的复合作用域；批准 `Singleton carrier + AsyncLocal` 设计或调整 lifetime，并补并发隔离/泄漏测试。
- [ ] GenerateFlow 取消映射使用 compare-and-remove，不得退回错取消“最新任务”；客户端断开必须传递到已启动副作用。

### U11 长进程资源、缓存、连接池与保留策略

来源：`CV-AUDIT-057,058,059,060,063,066,067,071,081,084,086,087,090,093`。Owner：Runtime/Station/资源治理。

- [ ] ONNX session key 加模型版本指纹；TextSave/camera/PLC/AgentRun/preview/auth 等表或池具备容量、TTL、idle eviction、引用安全和 dispose 语义。
- [ ] 上传图与正式结果图隔离容量/Owner；相机绑定删除或换设备释放旧 provider；retention cleanup 同步裁剪持久化与内存索引。
- [ ] replay、inspection spool/deadletter、command-result spool 设总字节/条数/天数上限、trim/告警/health 指标；大图不得进入无上限 JSONL，写盘失败不得阻断正式结果。
- [ ] 通过重启回放、磁盘故障、断线重连、长时 soak 和资源上限测试证明不会单调增长或静默丢数据。

### U12 端口恢复、发布洁净度、查询和导出边界

来源：P1-8；`CV-AUDIT-001,003,074,075,076,078,088`。Owner：Desktop/数据查询/发布。

- [ ] API 端口发现始终包含默认 5000 的恢复路径；移除三个 `patch_*.ps1` 并增加 publish denylist/内容审计。
- [ ] 统计、缺陷分布、trend 和 Station 历史过滤/聚合下推数据库；限制最大时间跨度、bucket/row 数，消除一次刷新重复全量查询。
- [ ] `stationCommandUpdated` 写入 SSE replay buffer 或进入 initial snapshot；断线重连不得丢命令状态。
- [ ] Station CSV/Excel 对 `=,+,-,@` 前缀统一做公式中和，并用恶意工站字段做导出回归。

### U13 算子参数、能力声明与结果类型契约

来源：`CV-AUDIT-092,094,095,096,097,098,099,100,101,102`。Owner：算子平台/Runtime。

- [ ] 未实现 `CSharpScript`、TCP Server、Modbus RTU 时从正式 metadata 移除或标为 disabled；不得保留“可选但固定失败”的伪能力。
- [ ] DeepLearning `DetectionList` 能进入结果 Defects 持久化并被 DualModalVoting 消费；增加正式连线与历史统计集成测试。
- [ ] Comparator、PLC Operation/PollingCondition、串口 StopBits/Parity/Encoding 对未知值 fail-fast；FINS 要么实现轮询，要么移除参数声明。
- [ ] 所有正式、实时、预览、调试和单算子执行入口在执行前强制参数/flow validation，并证明不能被 inline flow 绕过。

### U14 文档同步、关闭与归档门禁

Owner：文档治理。

- [ ] 每个 U 项关闭时回填对应源条目、当前 SHA、测试命令/计数、远端或现场证据和残余风险；不得机械批量勾选。
- [ ] 全面提升 TODO 的 100 项同步债可在证据链接就位后回填；39 项残项和 5 个关闭条件随 U01-U13 实际关闭。
- [ ] 持续问题池的 30 个 `CLOSED_SYNC_ONLY` 回填关闭依据；72 个 `OPEN_CONFIRMED` 必须逐 ID 关闭，不能只按治理线整体勾选。
- [ ] 0407、0418、深度学习原文按本轮要求继续保留；Studio2 仅在 G16 关闭后整批归档 Goal 卡。
- [ ] U01-U13 全部关闭后，将本文改为 `closed`，生成闭环说明，再按文档治理标准归档七组源文档；外部阻断未解除时不得归档。

## 4. 全面提升残项映射

| 原主题 | 状态 | 唯一责任入口 |
| --- | --- | --- |
| P0-9 | `OPEN_CONFIRMED` | U01 |
| P1-2 | `EVIDENCE_REQUIRED` | U10 |
| P1-7 | `OPEN_CONFIRMED` | U02 |
| P1-8 | `OPEN_CONFIRMED` | U12 |
| P1-14、P1-16 | `OPEN_CONFIRMED` | U03 |
| P1-20、P1-23、P2-8 | `OPEN_CONFIRMED` | U04 |
| P2-2 | `OPEN_CONFIRMED` | U05 |
| P2-6 | `EVIDENCE_REQUIRED` | U03 |
| P2-7 | `OPEN_CONFIRMED` | U03 |
| P2-9 | `OPEN_CONFIRMED` | U06 |

## 5. 持续问题池 102 项覆盖映射

### 5.1 `CLOSED_SYNC_ONLY`（30）

`CV-AUDIT-002, CV-AUDIT-004, CV-AUDIT-005, CV-AUDIT-007, CV-AUDIT-008, CV-AUDIT-010, CV-AUDIT-013, CV-AUDIT-016, CV-AUDIT-017, CV-AUDIT-019, CV-AUDIT-020, CV-AUDIT-022, CV-AUDIT-026, CV-AUDIT-027, CV-AUDIT-030, CV-AUDIT-031, CV-AUDIT-033, CV-AUDIT-035, CV-AUDIT-037, CV-AUDIT-038, CV-AUDIT-039, CV-AUDIT-043, CV-AUDIT-045, CV-AUDIT-046, CV-AUDIT-047, CV-AUDIT-054, CV-AUDIT-061, CV-AUDIT-062, CV-AUDIT-073, CV-AUDIT-085`

这些 ID 只进入 U14 做证据回填；若回填时发现当前代码事实变化，立即退回对应治理线，不得沿用本次结论。

### 5.2 `OPEN_CONFIRMED`（72）

| 唯一治理线 | ID |
| --- | --- |
| U08 身份/权限/Owner | `CV-AUDIT-011, CV-AUDIT-014, CV-AUDIT-015, CV-AUDIT-018, CV-AUDIT-023, CV-AUDIT-024, CV-AUDIT-025, CV-AUDIT-028, CV-AUDIT-034, CV-AUDIT-036, CV-AUDIT-064, CV-AUDIT-077, CV-AUDIT-091` |
| U09 持久化一致性 | `CV-AUDIT-006, CV-AUDIT-009, CV-AUDIT-012, CV-AUDIT-021, CV-AUDIT-029, CV-AUDIT-040, CV-AUDIT-041, CV-AUDIT-042, CV-AUDIT-044, CV-AUDIT-069, CV-AUDIT-070, CV-AUDIT-079, CV-AUDIT-080, CV-AUDIT-082, CV-AUDIT-083, CV-AUDIT-089` |
| U10 执行准入/状态隔离 | `CV-AUDIT-032, CV-AUDIT-048, CV-AUDIT-049, CV-AUDIT-050, CV-AUDIT-051, CV-AUDIT-052, CV-AUDIT-053, CV-AUDIT-055, CV-AUDIT-056, CV-AUDIT-065, CV-AUDIT-068, CV-AUDIT-072` |
| U11 长进程资源/保留 | `CV-AUDIT-057, CV-AUDIT-058, CV-AUDIT-059, CV-AUDIT-060, CV-AUDIT-063, CV-AUDIT-066, CV-AUDIT-067, CV-AUDIT-071, CV-AUDIT-081, CV-AUDIT-084, CV-AUDIT-086, CV-AUDIT-087, CV-AUDIT-090, CV-AUDIT-093` |
| U12 查询/发布/导出 | `CV-AUDIT-001, CV-AUDIT-003, CV-AUDIT-074, CV-AUDIT-075, CV-AUDIT-076, CV-AUDIT-078, CV-AUDIT-088` |
| U13 算子契约 | `CV-AUDIT-092, CV-AUDIT-094, CV-AUDIT-095, CV-AUDIT-096, CV-AUDIT-097, CV-AUDIT-098, CV-AUDIT-099, CV-AUDIT-100, CV-AUDIT-101, CV-AUDIT-102` |

## 6. 执行顺序

1. **Wave 0：安全与不可逆副作用** — U08、U10，以及 U13 中 PLC/脚本/validation 条目。
2. **Wave 1：数据一致性与长进程稳定性** — U02、U09、U11、U12。
3. **Wave 2：产品/发布/质量证据** — U01、U03、U04、U05、U06。
4. **Wave 3：外部证据与文档归档** — U07、U14。

同一 `.csproj` 的测试必须遵守根 `AGENTS.md` 串行规则。任何 Wave 可以拆成小提交，但不得在后续 Wave 用“已提交”替代前一 Wave 的验收证据。

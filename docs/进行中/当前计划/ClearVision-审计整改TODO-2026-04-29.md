---
title: "ClearVision 审计整改 TODO"
doc_type: "task-list"
status: "active"
topic: "system-audit-remediation"
created: "2026-04-29"
updated: "2026-04-29"
source_audit: "../../审计资料/系统审计/项目全面深度审计-2026-04-29.md"
---

# ClearVision 审计整改 TODO

> 来源审计：[项目全面深度审计-2026-04-29](../../审计资料/系统审计/项目全面深度审计-2026-04-29.md)
> 定位：把系统审计发现转成可执行整改计划。本文是当前计划入口，不重复审计全文，只保留任务、顺序、验收口径和验证命令。
> 当前状态：`active`

---

## 0. 整改目标

本轮整改不继续扩张算子数量，优先补齐工程可信度短板：

```text
P0：先止血，处理密钥泄漏、CI 空跑、实时检测启停失败。
P1：再收敛，收紧本地 API 安全边界、令牌强度、密钥落盘、warning 和文档入口。
P2：再演进，扩展真实现场证据、性能趋势、硬件诊断和 OperatorLibrary 包边界。
```

最终希望达到：

- [ ] 仓库内无明文 AI/API 密钥，旧密钥已轮换或吊销。
- [ ] CI 能确定执行 `Acme.Product.Tests`、`Acme.Product.Desktop.Tests` 和 OperatorLibrary smoke tests。
- [x] `InspectionWorkerTests` 和服务回归恢复全绿。
- [x] 本地 API 不再使用任意来源 CORS，不再通过 query string 传递 token。
- [x] 主工程和 OperatorLibrary 的 Release/Debug build warning 明确清零或有到期豁免。
- [x] 活跃文档入口不再指向已归档或空目录的旧计划。

---

## 1. 执行原则

- [ ] 每个 P0/P1 任务必须绑定一个验证命令或人工验收记录。
- [ ] 同一 `.csproj` 的 .NET 测试必须使用 `./scripts/run-dotnet-test-serial.ps1` 或固定脚本串行执行。
- [ ] 安全整改不在文档中复述真实密钥，只记录 `<REDACTED>`、轮换时间和责任人。
- [ ] 涉及外部服务商控制台的动作，由人工完成后在本计划登记，不假装代码修改可以替代密钥轮换。
- [ ] 完成整改后新增闭环记录，并将本计划迁入 `docs/归档/已关闭事项/`。

---

## 2. P0 止血任务

### P0-1：明文 AI API Key 泄漏处置

| 字段 | 内容 |
|---|---|
| 状态 | [!] 代码止血完成，旧 key 轮换待人工确认 |
| 风险 | 仓库跟踪文件中存在明文 `ApiKey`，应视为已泄漏 |
| 证据 | `Acme.Product/src/Acme.Product.Desktop/appsettings.json:7` |
| 负责人 | 人工密钥负责人 + Codex |
| 目标时限 | 2026-04-29 当日 |

任务：

- [ ] 人工登录对应 AI 服务商后台，轮换或吊销审计中发现的旧 key。
- [x] 将 `Acme.Product/src/Acme.Product.Desktop/appsettings.json` 中 `AiFlowGeneration.ApiKey` 改为空字符串或占位符。
- [x] 新增本地配置说明：真实 key 只能来自环境变量、本机用户配置或受保护 secret store。
- [x] 检查 `ai_models.json`、发布输出、日志、测试结果中是否存在同类 key（当前工作区 secret scan 通过）。
- [x] 对当前工作区运行 secret scan，记录结果；Git 历史扫描未在本轮执行。

验收标准：

- [ ] 旧 key 已在服务商后台失效。
- [x] 仓库跟踪文件不再包含真实 key。
- [x] `& "./scripts/scan-secrets.ps1"` 当前工作区扫描通过；宽松 `rg` 表达式存在 `disk-used-percent` 等误报，仅作为辅助排查。
- [x] 审计闭环记录中只出现 `<REDACTED>`，不出现真实 key。

本轮记录（2026-04-29）：

- 已将默认配置中的 `AiFlowGeneration.ApiKey` 置空。
- 新增 `docs/参考资料/指南/ClearVision-AI密钥本地配置说明.md`。
- `& "./scripts/scan-secrets.ps1"` 通过。

建议验证命令：

```powershell
rg -n "sk-[A-Za-z0-9_-]{12,}|ghp_[A-Za-z0-9_]{20,}" `
  --glob "!**/bin/**" `
  --glob "!**/obj/**" `
  --glob "!**/.git/**" `
  .
```

### P0-2：CI 真实执行产品测试

| 字段 | 内容 |
|---|---|
| 状态 | [~] CI 配置已落地，等待下一次 CI 实跑留证 |
| 风险 | `dotnet test Acme.Product.sln` 当前可能无测试输出，CI 存在空跑 |
| 证据 | 审计报告 4.1；`.github/workflows/ci.yml:76-84` |
| 负责人 | CI/质量负责人 |
| 目标时限 | 2026-04-30 |

任务：

- [ ] 决定策略 A：把测试项目加入 `Acme.Product.sln`。
- [x] 或策略 B：CI 显式执行测试项目和固定回归脚本（本轮采用短期策略 B）。
- [ ] CI 日志必须能看到 `Acme.Product.Tests.dll` 和 `Acme.Product.Desktop.Tests.dll` 测试数。
- [x] 保留检测/性能/UI 等专项门禁，但不要让它们替代基础单元/服务/端点测试。
- [x] 将 `code-quality` 中 warning-as-error 改为阻塞门；`dotnet format` 改为显式 report-only 项。

建议首选方案：

```text
短期：CI 显式调用固定脚本，避免 solution 结构调整引入额外 churn。
中期：将测试项目纳入 solution，使本地和 CI 口径一致。
```

验收标准：

- [ ] CI 基础测试步骤显示非零测试总数。
- [x] `Acme.Product.Tests` 至少包含服务/运行时/算子代表性测试。
- [x] `Acme.Product.Desktop.Tests` 至少包含端点代表性测试。
- [x] CI artifact 能收集两类测试的 `.trx`。

本轮记录（2026-04-29）：

- `.github/workflows/ci.yml` 已改为显式执行 `Acme.Product.Tests.csproj` 与 `Acme.Product.Desktop.Tests.csproj`。
- 本地验证通过：服务回归 25/25，桌面端点回归 15/15，产品 `warnaserror` build 0 warning/0 error。

本地验证命令：

```powershell
& "./scripts/run-tests-services-regression.ps1" -Verbosity minimal
& "./scripts/run-tests-desktop-endpoints.ps1" -Verbosity minimal
```

### P0-3：修复 InspectionWorkerTests 失败

| 字段 | 内容 |
|---|---|
| 状态 | [x] 已完成 |
| 风险 | 实时检测启停状态事件链路可复现失败 |
| 证据 | `StopAsync_CancelsRunningTask_AndPublishesStoppedState` 复跑失败 |
| 负责人 | Runtime/服务负责人 |
| 目标时限 | 2026-04-30 |

任务：

- [x] 复现失败测试，确认当前失败栈和审计报告一致。
- [x] 梳理 `InspectionWorker.StopAsync`、`CleanupTaskAsync`、`RunWithTripleExceptionProtectionAsync` 与 `InspectionRuntimeCoordinator.MarkAsStopped` 的责任边界。
- [x] 明确 Running / Stopping / Stopped 事件发布的顺序约束与 cleanup 兜底。
- [x] 修正既有确定性测试覆盖的 `Running -> Stopped` 停止事件链路。
- [x] 不通过单纯调大 timeout 作为修复。

验收标准：

- [x] 单类复跑通过。
- [x] 服务回归脚本通过。
- [x] 既有测试能稳定证明停止状态事件发布。

本轮记录（2026-04-29）：

- `InspectionWorkerTests` 2/2 通过。
- `run-tests-services-regression.ps1` 25/25 通过。

验证命令：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Acme.Product.Tests.Services.InspectionWorkerTests" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal

& "./scripts/run-tests-services-regression.ps1" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal
```

### P0-4：secret scan 门禁

| 字段 | 内容 |
|---|---|
| 状态 | [x] 已完成 |
| 风险 | 后续仍可能把 key 提交进仓库 |
| 负责人 | CI/安全负责人 |
| 目标时限 | 2026-05-01 |

任务：

- [x] 选择 secret scan 工具或先落地最小正则扫描脚本。
- [x] 将扫描加入 CI PR gate。
- [ ] 本地 pre-commit 可选接入，但 CI 必须接入。
- [x] 扫描规则至少覆盖 `sk-*`、`ghp_*`、JWT/private key、常见云厂商 access key。

验收标准：

- [x] 人为加入测试 key 时 CI 会失败（临时目录合成 fixture 已验证脚本失败路径）。
- [x] 文档中 `<REDACTED>` 不被误判为真实泄漏。
- [x] 扫描输出不打印完整密钥。

本轮记录（2026-04-29）：

- 新增 `scripts/scan-secrets.ps1`。
- CI `build-and-test` job 已加入 `Secret Scan` 步骤。
- 当前工作区扫描通过；合成泄漏 fixture 可触发失败且只输出位置与规则名。

---

## 3. P1 收敛任务

### P1-1：收紧本地 API CORS 与 token 传输

状态：[x] 已完成
目标时限：2026-05-03

任务：

- [x] 将 `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` 改为最小允许范围。
- [x] 明确 WebView2 前端访问 API 的 origin 策略。
- [x] 删除 query string token 获取路径，SSE 如需鉴权改用带 `Authorization` 头的 fetch 流式握手。
- [x] 为 CORS 和 query token 拒绝/Authorization 成功写端点测试。

验收标准：

- [x] 任意外部 origin 不能直接跨源调用本地 API。
- [x] `AuthMiddleware` 不再从 query string 读取 token。
- [x] 端点测试覆盖认证失败和成功路径。

本轮记录（2026-04-29）：

- CORS origin 收敛为 `app.local` 与本机 loopback 5000-5010 端口范围，方法/请求头也改为显式白名单。
- 前端 SSE 订阅从 `?token=` 改为 fetch streaming + `Authorization: Bearer`。
- `InspectionEventEndpointsTests` 覆盖 Authorization 成功和 query token 拒绝；`ProgramCorsTests` 覆盖允许/拒绝 origin。
- 目标测试 12/12 通过；桌面端点回归 15/15 通过。

### P1-2：会话 token 改为 CSPRNG

状态：[x] 已完成
目标时限：2026-05-03

任务：

- [x] 将 `AuthService.GenerateToken()` 从 `Guid.NewGuid()` 改为 `RandomNumberGenerator.GetBytes`。
- [x] 使用 base64url 编码，长度不少于 256-bit。
- [x] 增加 token 非空、长度、重复性基本测试。
- [x] 确认前端 sessionStorage 兼容新 token 字符集。

验收标准：

- [x] 单测证明 token 长度和唯一性基本约束。
- [x] 登录、登出、`/api/auth/me`、改密码测试通过。

本轮记录（2026-04-29）：

- 新 token 为 32 字节随机数的 base64url，无 `=` padding，当前长度 43。
- `AuthServiceTests` 13/13 通过。
- 服务回归 25/25 通过。

建议验证：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "AuthServiceTests" `
  -Verbosity minimal
```

### P1-3：AI 模型 key 落盘保护

状态：[x] 已完成
目标时限：2026-05-05

任务：

- [x] 设计 Windows 桌面端密钥存储方案：采用 DPAPI `CurrentUser` + 用户级 `ai_model_secrets/*.dpapi` secret file。
- [x] `AiConfigStore.Save()` 不再明文写出 `ApiKey`，`ai_models.json` 仅保留空 `apiKey` 字段以兼容结构。
- [x] 对外 `/api/ai/models` 与 `/api/settings/reset` 只暴露 `hasApiKey` 语义，不返回真实 key；`GetAll()` 保留内部运行时真实 key。
- [x] 增加迁移逻辑：发现旧 `ai_models.json` 明文 key 时迁移到受保护存储并重写脱敏。
- [x] 增加设置重置、模型列表响应与落盘迁移脱敏测试。

验收标准：

- [x] 新生成 `ai_models.json` 中不含真实 key。
- [x] 旧配置可迁移，不破坏已配置模型。
- [x] UI 仍能显示“已配置密钥”。

本轮记录（2026-04-29）：

- 新增 `AiApiKeySecretStore`，用 Windows DPAPI 保护模型 key；运行时内存仍可读取真实 key，磁盘 `ai_models.json` 不再保存明文。
- 旧 `ai_models.json` 内联 key 会在首次加载时导入 secret store 并触发脱敏重写；旧 `ai_config.json` 迁移后删除。
- `/api/settings/reset` 改为返回 AI 模型脱敏投影；连续预览端点补充 `[FromServices]` 标注，避免未访问路由因轻量测试主机缺服务而导致全局 500。
- 验证通过：`AiConfigStoreTests` 17/17、`SettingsResetEndpointTests + AiModelEndpointsTests` 4/4、主工程 Debug build 0 warning/0 error、服务回归 25/25、桌面端点回归 15/15、`scripts/scan-secrets.ps1` 通过。

验证命令：

```powershell
& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Tests/Acme.Product.Tests.csproj" `
  -FullyQualifiedName "Acme.Product.Tests.AI.AiConfigStoreTests" `
  -Verbosity minimal

& "./scripts/run-dotnet-test-serial.ps1" `
  -Project "Acme.Product/tests/Acme.Product.Desktop.Tests/Acme.Product.Desktop.Tests.csproj" `
  -FullyQualifiedName "Acme.Product.Desktop.Tests.SettingsResetEndpointTests","Acme.Product.Desktop.Tests.AiModelEndpointsTests" `
  -Verbosity minimal

dotnet build Acme.Product/Acme.Product.sln --configuration Debug
& "./scripts/run-tests-services-regression.ps1" -NoBuild -NoRestore -Verbosity minimal
& "./scripts/run-tests-desktop-endpoints.ps1" -NoBuild -NoRestore -Verbosity minimal
& "./scripts/scan-secrets.ps1"
```

### P1-4：编译 warning 清零

状态：[x] 已完成
目标时限：2026-05-06

任务：

- [x] 修复主工程 build 中 9 个 warning。
- [x] 修复 OperatorLibrary Release build 中 3 个 warning。
- [x] 对 legacy AI 链路注册做决策：保留兼容注册，并以局部 `#pragma warning disable CS0618` 明确记录技术债边界。
- [x] 将 warning-as-error 门禁改为阻塞模式；`dotnet format` 因历史换行/格式债暂保留为显式 report-only。

当前 warning 清单：

```text
已清零：
- dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
- dotnet build Acme.Product/Acme.Product.sln --no-restore -warnaserror
- dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore
```

本轮记录（2026-04-29）：

- 修复 `InspectionMetrics`、`PreviewMetricsAnalyzer`、`BlobDetectionOperator`、`ResultOutputOperator`、`GeometricToleranceOperator`、`AutoTuneEndpoints` 的 nullable / unreachable warning。
- `DependencyInjection` 中 legacy AI 服务保留兼容注册，改为局部抑制 obsolete warning，避免全局吞掉真实废弃 API 风险。
- CI `code-quality` job 改为 Windows 环境执行，`dotnet build ... -warnaserror` 成为阻塞门；`dotnet format --verify-no-changes` 仍保留报告项，原因是当前仓库存在大量历史换行/格式差异，需单独格式化批次处理。
- 修正 `GradientShapeMatchOperator` 对低特征模板的契约：`InvalidTemplate` 返回失败结果，重新对齐算子说明与 `OperatorContractReconciliationTests`。

验收命令：

```powershell
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
dotnet build Acme.Product/Acme.Product.sln --no-restore -warnaserror
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore
& "./scripts/run-tests-phase42-regression.ps1"
```

### P1-5：文档入口修复

状态：[x] 已完成
目标时限：2026-05-02

任务：

- [x] 修复 `docs/README.md` 中指向已归档 Quality Flywheel TODO 的旧链接。
- [x] 修复 `docs/导航.md` 中旧链接。
- [x] 修复 `docs/项目总览.md` 中“当前计划”口径。
- [x] 修复 `docs/算子资料/导航.md` 和 `docs/算子资料/算子文档现状对齐说明-2026-04.md` 的旧链接。
- [x] 将当前计划主入口指向本文。

验收标准：

- [x] `docs/进行中/当前计划/` 不再为空。
- [x] 活跃文档不再把已归档 Quality Flywheel TODO 当作当前计划。
- [x] 新读者能从 `docs/README.md` 或 `docs/导航.md` 找到当前整改计划。

本轮记录（2026-04-29）：

- 活跃入口统一指向 `docs/进行中/当前计划/ClearVision-审计整改TODO-2026-04-29.md`。
- `rg` 复核确认 `docs/README.md`、`docs/导航.md`、`docs/项目总览.md`、`docs/算子资料/导航.md` 和对齐说明中不再引用旧当前计划链接。

### P1-6：SDK 与依赖口径冻结

状态：[x] 已完成
目标时限：2026-05-07

任务：

- [x] 决策 SDK 策略：升级并固定到当前本机/CI 可用的 `.NET SDK 10.0.101`，`rollForward` 收敛为 `latestFeature`。
- [x] 更新 `global.json` 与 README 常用命令说明。
- [x] 审查 `Microsoft.Extensions.* 8/9/10` 混用，并将直接依赖收敛到 `10.0.0`。
- [x] 对 OperatorLibrary 的消费者兼容性写入说明。

验收标准：

- [x] 本地 `dotnet --info` 与 CI setup 口径一致。
- [x] 依赖版本混用有明确说明或已收敛。
- [x] 全量 build / 代表性测试通过。

本轮记录（2026-04-29）：

- `global.json` 固定为 `10.0.101`，避免 `latestMajor` 漂移到未审计 SDK。
- `Acme.Product.Infrastructure` 与 `Acme.OperatorLibrary` 的直接 `Microsoft.Extensions.*` 依赖收敛到 `10.0.0`。
- 新增 `docs/参考资料/指南/ClearVision-SDK与依赖版本口径.md`，并在根 README、`docs/README.md`、`Acme.OperatorLibrary/README.md` 中补充入口和消费者说明。
- 验证通过：`dotnet --info`、产品 Debug build、产品 `warnaserror` build、OperatorLibrary Release build、OperatorLibrary pack + smoke。

验收命令：

```powershell
dotnet --info
dotnet restore Acme.Product/Acme.Product.sln
dotnet restore Acme.OperatorLibrary/Acme.OperatorLibrary.csproj
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
dotnet build Acme.Product/Acme.Product.sln --no-restore -warnaserror
dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore
```

---

## 4. P2 演进任务

### P2-1：真实 field replay 扩展

状态：[!] 替代证据分层完成；真实现场样本待人工提供
目标时限：2026-05-20

任务：

- [x] 区分 `field-substitute`、`internal-lab`、`real-field` 三类证据标签。
- [x] 为 5 个已有 field replay 算子补充真实/替代来源说明。
- [ ] 优先扩展 TemplateMatching、CaliperTool、DeepLearning、SurfaceDefectDetection、CameraCalibration 的真实样本回灌。
- [ ] 每个真实样本必须有脱敏、manifest、复现命令和 triage 标签。

验收标准：

- [ ] `operator_quality_matrix.md` 可区分真实现场证据与替代证据。
- [ ] 至少 1 条真实 field replay 闭环进入报告。

本轮记录（2026-04-29）：

- 新增 `docs/审计资料/报告/field-replay证据分层记录-2026-04-29.md`，明确三类证据标签和当前口径。
- 当前五个 field-substitute 算子已登记：DeepLearning、TemplateMatching、CaliperTool、SurfaceDefectDetection、CameraCalibration。
- 真实现场样本未在当前仓库中发现，不能用合成/替代数据冒充；后续需要人工提供脱敏真实样本、manifest 和复现命令。

### P2-2：性能趋势门禁

状态：[~] 趋势脚本与当前基线完成；历史基线待积累
目标时限：2026-05-24

任务：

- [x] 将当前单次 benchmark 报告升级为可比较历史趋势。
- [x] 建立 `baseline -> current -> delta` 的报告模板。
- [ ] 确定 quick smoke、nightly heavy、release gate 三种性能预算。
- [x] 避免把单机一次跑分当作发布结论。

验收标准：

- [x] benchmark 报告能显示历史趋势。
- [x] 手动脚本能输出性能 delta。
- [ ] CI 门禁预算尚未接入。

本轮记录（2026-04-29）：

- 新增 `quality/tools/compare_operator_benchmark.py`，支持 `baseline -> current -> delta` Markdown 趋势报告。
- 生成当前 smoke benchmark：`docs/审计资料/报告/operator-performance-benchmark-current.json` 与 `.md`，结果 4/4 passed，total=13.486 ms。
- 生成趋势报告：`docs/审计资料/报告/operator-performance-trend-2026-04-29.md`。当前没有历史 baseline，因此报告只作为本轮基线，不作为发布性能结论。

验证命令：

```powershell
dotnet run --project quality/tools/OperatorPerformanceBenchmarkRunner/OperatorPerformanceBenchmarkRunner.csproj --configuration Release -- --mode smoke --output docs/审计资料/报告/operator-performance-benchmark-current.json --report docs/审计资料/报告/operator-performance-benchmark-current.md
python quality/tools/compare_operator_benchmark.py --current docs/审计资料/报告/operator-performance-benchmark-current.json --output docs/审计资料/报告/operator-performance-trend-2026-04-29.md
```

### P2-3：OperatorLibrary 包边界评估

状态：[x] 已完成
目标时限：2026-05-31

任务：

- [x] 评估当前全能力包对外部消费者的依赖压力。
- [x] 给出包拆分方案：`Abstractions`、`VisionCore`、`AI`、`Communication`。
- [x] 保留当前 `Acme.OperatorLibrary` 作为兼容全量包。
- [x] 形成迁移风险和版本策略。

验收标准：

- [x] 输出包边界评估文档。
- [x] 不破坏当前 `pack + smoke` 流程。

本轮记录（2026-04-29）：

- 新增 `docs/审计资料/报告/OperatorLibrary包边界评估-2026-04-29.md`。
- `.tmp/publish-check/operatorlib-remediation` 下完成临时 pack + restore + smoke test，`Acme.OperatorLibrary.SmokeTests` 25/25 通过；验证后已清理临时目录。

### P2-4：硬件与外设诊断补强

状态：[~] 静默失败止血完成；UI/API 诊断面与 mock 测试待补
目标时限：2026-06-07

任务：

- [x] 梳理相机、PLC、串口、GPU 探测路径中的空 catch。
- [x] 将静默失败改成 Debug/Trace + 结构化诊断。
- [ ] 在 UI 或诊断 API 中暴露“不可用原因”。
- [ ] 为硬件不可用、SDK 缺失、权限不足、设备占用写 mock/contract 测试。

验收标准：

- [ ] 相机/PLC/GPU 不可用时能通过 UI/API 定位原因。
- [x] 不影响无硬件环境下的测试通过。

本轮记录（2026-04-29）：

- 新增 `docs/审计资料/报告/硬件与外设诊断补强记录-2026-04-29.md`。
- `MindVisionCamera`、`GpuAvailabilityChecker`、`ModbusCommunicationOperator` 中相关空 `catch { }` 已改为 `Debug.WriteLine` 或 `Logger.LogDebug`。
- 复核命令 `rg "catch \{ \}|catch\s*\{\s*\}" ...` 在相机、服务、算子、PLC 通信路径未发现剩余空 catch。
- 代表性回归通过：目标算子/服务测试 38/38、PLC 回归 54/54、Phase42 回归 98/98。

---

## 5. 建议执行顺序

```text
Day 0:
P0-1 密钥轮换与配置止血
P0-2 CI 测试空跑修正方案落地

Day 1:
P0-3 InspectionWorkerTests 修复
P0-4 secret scan 最小门禁

Day 2-4:
P1-1 CORS/token 收紧
P1-2 CSPRNG token
P1-5 文档入口修复

Day 5-7:
P1-3 AI key 落盘保护
P1-4 warning 清零
P1-6 SDK/依赖口径冻结

Week 2+:
P2-1 field replay 扩展
P2-2 性能趋势
P2-3 OperatorLibrary 包边界
P2-4 硬件诊断
```

---

## 6. 回归验证总清单

P0/P1 完成后至少执行：

```powershell
dotnet build Acme.Product/Acme.Product.sln --configuration Debug --no-restore
dotnet build Acme.Product/Acme.Product.sln --no-restore -warnaserror

& "./scripts/run-tests-services-regression.ps1" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal

& "./scripts/run-tests-desktop-endpoints.ps1" `
  -NoBuild `
  -NoRestore `
  -Verbosity minimal

& "./scripts/run-tests-plc-regression.ps1" -NoBuild -NoRestore -Verbosity minimal
& "./scripts/run-tests-phase42-regression.ps1" -Verbosity minimal

dotnet build Acme.OperatorLibrary/Acme.OperatorLibrary.csproj --configuration Release --no-restore

python quality/tools/run_quality_suite.py --suite quick_contract_suite --validate-only
python quality/tools/run_quality_suite.py --suite golden_core50_suite --validate-only
python quality/tools/run_quality_suite.py --suite dataset_heavy_suite --validate-only
python quality/tools/run_quality_suite.py --suite field_replay_suite --validate-only

& "./scripts/scan-secrets.ps1"
git diff --check
```

本轮验证记录（2026-04-29）：

- 产品 Debug build：0 warning / 0 error。
- 产品 `warnaserror` build：0 warning / 0 error。
- OperatorLibrary Release build：0 warning / 0 error。
- 服务回归 25/25、桌面端点回归 15/15、PLC 回归 54/54、Phase42 回归 98/98。
- OperatorLibrary smoke tests 25/25；临时 pack 输出已清理。
- `quick_contract_suite`、`golden_core50_suite`、`dataset_heavy_suite`、`field_replay_suite` manifest validate-only 均通过。
- `scripts/scan-secrets.ps1` 通过。
- `git diff --check` 无空白错误；仅提示工作区若干文件下次 Git 触碰时会发生 LF/CRLF 规范化。

涉及算子库打包时执行：

```powershell
$out = ".tmp/publish-check/operatorlib-remediation"
dotnet pack Acme.OperatorLibrary/Acme.OperatorLibrary.csproj `
  --configuration Release `
  --no-build `
  --output $out

dotnet restore Acme.OperatorLibrary/tests/Acme.OperatorLibrary.SmokeTests/Acme.OperatorLibrary.SmokeTests.csproj `
  --configfile Acme.OperatorLibrary/nuget.config `
  --source $out `
  --source "https://api.nuget.org/v3/index.json" `
  -p:AcmeOperatorLibraryPackageVersion=1.0.2

dotnet test Acme.OperatorLibrary/tests/Acme.OperatorLibrary.SmokeTests/Acme.OperatorLibrary.SmokeTests.csproj `
  --configuration Release `
  --no-restore `
  --verbosity minimal `
  -p:AcmeOperatorLibraryPackageVersion=1.0.2
```

完成后清理：

```powershell
Remove-Item -LiteralPath ".tmp/publish-check/operatorlib-remediation" -Recurse -Force
```

---

## 7. 状态看板

| 分组 | 总数 | 未开始 | 进行中 | 已完成 | 阻塞 |
|---|---:|---:|---:|---:|---:|
| P0 止血 | 4 | 0 | 1 | 2 | 1 |
| P1 收敛 | 6 | 0 | 0 | 6 | 0 |
| P2 演进 | 4 | 0 | 2 | 1 | 1 |

---

## 8. 闭环出口

当满足以下条件时，本计划可以关闭并归档：

- [ ] P0 全部完成。
- [x] P1 至少完成，或未完成项有明确 owner、到期日和风险豁免。
- [x] 本地回归验证总清单通过；CI 实跑证据仍需在流水线侧补录。
- [ ] 新增闭环记录，包含密钥轮换结论、CI 测试证据、运行时修复证据、安全边界收紧证据。
- [ ] 将本文迁移到 `docs/归档/已关闭事项/<日期>-审计整改闭环归档/`。

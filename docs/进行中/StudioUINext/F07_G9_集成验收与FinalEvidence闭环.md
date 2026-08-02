# ClearVision Studio UI Next F07 G9：集成验收与 Final Evidence 闭环

## 1. 基线与状态

本轮 G9 以执行开始时的 `HEAD` `a8854c94d2bf9abd973889c92848f10baaca08ab` 为基线。G7/G8-R 修补后，代码和测试 source evidence 固定在 `a5f017d0d0ae6bf3ba20ec85488bb5afa96e21ce`；本报告中的测试结果均以该 source tree 为准。文档提交和推送 SHA 在最终 Git 回报中记录，不将受保护未跟踪路径纳入提交。

历史实现提交链：

| 阶段 | SHA | 说明 |
| --- | --- | --- |
| G7 implementation | `e72d9f44496133b4e9e0c3e2dbfa4daab3e7b44d` | Station communication workspace |
| G8 implementation | `33f6d5f2529bf7761ea8f67bc7a6c9257a5d845c` | AI model administration |
| G8 lifecycle final before G9 | `a8854c94d2bf9abd973889c92848f10baaca08ab` | Station/AI request lifecycle gaps |
| G7/G8-R source evidence | `a5f017d0d0ae6bf3ba20ec85488bb5afa96e21ce` | token/API key/authority repair and audit guard update |

```text
F07_G9_STATE=DONE
F07_ENGINEERING_STATE=DONE
F07_SETTINGS_IMPORT_EXPORT=EXCLUDED
F07_REAL_HARDWARE_VALIDATION=NOT_PERFORMED
F07_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_SETTINGS_RETIREMENT=NOT_APPROVED
PRODUCTION_ACCEPTANCE=BLOCKED
```

G9 已完成本地集成验收和证据闭环；不进入 G10。`PRODUCTION_ACCEPTANCE=BLOCKED` 是证据边界结论，不是本地工程门禁失败。

## 2. 集成范围

- Settings Shell 仍只有一个 SettingsOwner、一个 leave guard、一个 API transport 和一个 endpoint matrix。
- Generic `/api/settings` 只服务 General、Storage、Runtime、Security；PLC、TCP、Camera、Station、AI 均走各自专用 authority。
- User CRUD、密码重置、Database backup、Station communication、AI model mutation/test 都注册 pending/unknown 语义；dirty、pending、unknown 时 refresh 不覆盖 projection/draft。
- Station 面板展示 saved、effective/current-running、restart-required、unknown 的区别；LanController 没有安全 handoff 时不允许 regenerate，token 不 reveal/export/log。
- AI 面板使用 safe/full projection；BaseUrl preserve/replace/clear、API key keep/replace/clear、Provider/Protocol/Auth 合同、LastTest metadata、connection-test authority reread 和 unknown reconcile 均已闭合。
- KeepAlive group switch、deactivated、cancel、route leave、dispose 清空 token、API key、密码等敏感字段；普通非敏感草稿继续保留。
- Database 仅覆盖 status/backup；Import/Export 和 restore/repair/cleanup/reset 明确排除。

## 3. 权威与权限结论

| 域 | authority | Admin | Engineer | Operator |
| --- | --- | --- | --- | --- |
| Generic Settings | `IConfigurationService` / `/api/settings` | read/write | read | forbidden |
| Station communication | Station communication settings store +专用 endpoints | read/write/diagnostic | forbidden | forbidden |
| AI model metadata/secret | `AiConfigStore` 与既有 secret store | read full/mutate/test | read safe/allowed reasoning diagnostic | forbidden |
| PLC/TCP/Camera | 既有设备 authority | 按 endpoint matrix | 允许读取/诊断或 runtime operation | forbidden |
| Users/password | auth/user authority | mutation | read matrix permitted | forbidden |
| Database | database maintenance authority | status/backup | forbidden | forbidden |

没有新增第二 HTTP client、HostBridge、EventBus、Owner、secret store、Station runtime manager 或 AI model selection strategy。

## 4. 证据矩阵

| Evidence | Result | Artifact / command |
| --- | --- | --- |
| StudioUI unit | `119 files / 721 tests PASS` | `StudioUI/npm run test:unit` |
| Typecheck | `PASS` | `StudioUI/npm run typecheck` |
| Lint | `PASS` | `StudioUI/npm run lint` |
| Production build | `PASS` | `StudioUI/npm run build:production` |
| Bundle gate | `PASS` | `StudioUI/npm run bundle:gate` |
| Bundle reproducibility | `PASS` | `StudioUI/npm run bundle:verify` |
| AI/Station focused Desktop | `84/84 PASS` | `.tmp/test_results/g9-final-desktop-focused/g9-final-desktop-focused.trx` |
| Architecture guard | `9/9 PASS` | `.tmp/test_results/g9-architecture-final/g9-architecture-final.trx` |
| Desktop full, source SHA | `744/744 PASS` | `.tmp/test_results/g9-final-desktop-full-source-sha/g9-final-desktop-full-source-sha.trx` |
| F07 Browser settings/device | `18/18 PASS` | `npx playwright test ...f07-settings-shell.spec.ts ...f07-device-workbench.spec.ts` |
| StudioUI Next Browser full | `159 total; 138 passed; 21 skipped; 0 failed` | `CV_UI_SCENARIO=studio-ui-next npx playwright test` |
| Virtual PLC | `83/83 PASS`; Modbus/MC/FINS smoke PASS | `.tmp/test_results/g9-final-plc-virtual/g9-final-plc-virtual.trx` |

Browser fixture tests reported no failed test and the F07 assertions did not expose a secret value. The shared visual-evidence paths reported no console/page runtime error. F07 Settings/device specs did not emit a separate global console/page/request-failure ledger, so that narrower evidence is not overstated. The 21 skipped full-suite cases are optional visual-evidence capture branches; they are not represented as PASS.

## 5. 审计中发现并修复的问题

- AI connection-test 后端此前将调用方 abort 和内部 timeout 合并为普通 timeout；现已向上传播 caller abort，避免伪造已知结果或写入 LastTest metadata。
- F02 architecture guard 未随 Settings capability 更新，误报 Settings owner、endpoint matrix 和 `/settings` route；已补齐 guard 期望并在 Desktop 全量中验证。
- G7/G8 历史完成报告保留原始阶段基线；本报告与 `F07_G7_G8_R_StationToken与AI模型Authority修补报告.md` 是当前 R/G9 证据入口，不把历史旧 SHA 或旧测试数量当作最终证据。

## 6. 未运行证据、剩余阻断与停止边界

以下项目没有运行，保持事实状态，不以 Browser 或本机 Debug 结果替代：

- 真实 Station/远程 Station 通信：`NOT PERFORMED`。
- 真实 LLM/API 产品质量与模型效果：`NOT EVALUATED`。
- 真实 WebView2 Debug/Release 宿主：`NOT PERFORMED`。
- Windows 125% DPI/多显示器矩阵：`NOT PERFORMED`。
- Release publish、clean no-Node target、完整 CI/Remote Final Gate：`NOT PERFORMED`。
- 真实硬件、现场 Station/PLC/Camera：`NOT PERFORMED`。

因此没有“无剩余 blocker”的未经验证声明。当前工程完成的是批准范围内的 G7/G8 设置能力与本地集成证据；生产验收仍被真实环境与发布证据阻断。默认入口继续保持未切换，Legacy Settings 未退役，G10 未进入。

受保护未跟踪路径状态：`ClearVision.Product/src/capabilities/` 仍存在，未修改、未删除、未暂存、未提交。

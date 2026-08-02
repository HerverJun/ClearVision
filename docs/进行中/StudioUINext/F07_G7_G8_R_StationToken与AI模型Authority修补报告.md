# ClearVision Studio UI Next F07 G7/G8-R：Station token 与 AI 模型 authority 修补报告

## 状态

本轮从 `a8854c94d2bf9abd973889c92848f10baaca08ab` 开始审计，修补内容已固定在 source evidence SHA `a5f017d0d0ae6bf3ba20ec85488bb5afa96e21ce`。本轮没有修改受保护未跟踪路径 `ClearVision.Product/src/capabilities/`。

```text
F07_G7_STATE=DONE
F07_G8_STATE=DONE
F07_G7_G8_REPAIR=PASS
F07_G9_ENTRY=AWAITING_REVIEW
F07_G9_IMPLEMENTATION=FORBIDDEN
DEFAULT_ENTRY_CHANGE=BLOCKED
LEGACY_SETTINGS_RETIREMENT=NOT_APPROVED
F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED
```

## 修补结论

### AI 脱敏与 secret authority

- BaseUrl 使用 `preserve`、`replace`、`clear` 三态。服务端返回的 `<redacted-host>`、`<redacted-path>`、`<redacted>` 等值不会被当作真实 URL 写回；未明确替换时保留后端原值。
- API key 后端严格接受 `keep`、`replace`、`clear`。未知 operation、空 replacement、masked/redacted replacement、keep 携带 key、clear 携带 replacement 都返回 `400`；创建模型也必须明确选择 key operation。
- `extraHeaders`、`extraQuery`、`extraBody` 继续只读脱敏；本轮没有提供编辑能力，写入这些字段会被拒绝，报告不将其表述为可编辑。
- Provider 支持已知预设与 Custom 路径；Anthropic、Azure OpenAI、Ollama、OpenAI-compatible 的 protocol、wire API、auth 默认组合由后端合同解析，用户主动覆盖的值不会被 preset 静默覆盖。非法枚举和明显无效组合由后端拒绝。
- Provider、Model、BaseUrl、Protocol 任一 reasoning identity 变化都会立即失效旧 reasoning-support projection；重新查询前不显示旧矩阵。

### AI operation 与未知结果

- create、update、delete、activate、default-planner、default-shadow-eval、test 均按真实 method/path 建立独立 contract；activate/default 是持久化写入，不是 runtime-only operation。
- connection test 写入 LastTestStatus、LastTestAt、LastTestLatencyMs；成功完成后 owner 重新 GET `/api/ai/models`。网络/abort/decode 不确定时登记 `ai-models` 或 `ai-model-test:<id>` unknown，只有匹配的模型 authority reread 才能清除；reasoning-support 不会清除 unknown。
- 服务端区分内部 connection timeout 与调用方 `RequestAborted`：内部 timeout 返回已知 `timeout` 结果；调用方 abort 向上传播，不伪造 timeout，也不在未知结果下写入 LastTest 元数据。

### Station token 与重启语义

- Station settings 只使用专用 `/api/station-communication/settings`；token 只使用 `/api/station-communication/token`，没有 generic `/api/settings` fallback 或第二 Station owner。
- 保存和 token operation 完成后重新读取 Station authority；UI 分离 `saved`、`effective/current-running`、`requiresRestart` 与 `unknown outcome`，不自动重启 Desktop、Station 或后台服务。
- `regenerate` 是持久化、restart-dependent mutation，不按 runtime-only feedback 展示。LanController 在没有已批准安全 handoff 时禁用 regenerate，要求手动 replace；本轮没有新增 token reveal/export/logging。reveal 不作为永远返回空值的伪能力保留。
- LocalLoopback 的既有 regenerate 合同继续可用，但页面不回显真实 token；保存、重启后确认和 unknown reconcile 都依赖服务端 reread。

### 生命周期与权限

- Station/AI 继续复用唯一 SettingsOwner、唯一 leave guard、唯一 API transport 和现有 permission matrix。Admin 执行 mutation；Engineer 只读取 safe projection 或执行允许的诊断；Operator 不进入 Settings。
- dirty draft 不因 group switch 或 refresh 静默覆盖；pending/unknown 阻止无提示 route leave 和 destructive refresh。敏感 token/API key 不作为 KeepAlive 普通 draft，deactivated、cancel、失败、route leave、dispose 后清空。
- G9 审计期间补齐并修正了过时的 StudioUI architecture guard，使其识别 Settings owner、Settings endpoint matrix 和 `/settings` 路由；未放宽生产 authority 边界。

## 验证

| 验证 | source evidence SHA 结果 |
| --- | --- |
| StudioUI unit | `119 files / 721 tests PASS` |
| typecheck、lint、production build | `PASS` |
| bundle gate、bundle reproducibility | `PASS` |
| AI/Station 定向 Desktop | `84/84 PASS`，含 caller-abort 不伪造 timeout 回归 |
| Desktop architecture guard | `9/9 PASS` |
| Desktop 全量 | `744/744 PASS` |
| F07 Station/AI/device Browser | `18/18 PASS` |
| StudioUI Next Browser 全量 | `159 total; 138 passed; 21 skipped; 0 failed` |
| Virtual PLC | `83/83 PASS`，Modbus 与 MC/FINS smoke PASS |

## 未运行与边界

- 真实 Station、真实 LLM 产品质量、真实 WebView2、Windows 125% DPI/分辨率矩阵、Release publish、无 Node 目标机和完整 CI：`NOT PERFORMED` / `NOT RUN`。
- `F06_REAL_LLM_PRODUCT_QUALITY=NOT_EVALUATED` 保持不变。
- 本轮没有实现 Settings Import/Export、数据库 restore/repair/cleanup/reset，没有修改默认入口、Legacy Settings、Station token generation/storage 安全模型或 AI Workbench owner。
- F07 G9 尚未在本报告阶段宣布完成；后续以 G9 Final Evidence 报告为准。

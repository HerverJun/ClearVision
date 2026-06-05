# Vision Agent AI Model Config Productization

日期：2026-06-06

## 现状审计

- `AiModelConfig` 已有多模型基础字段、协议字段、role bindings、priority、reasoning 配置；本轮补齐 `DisplayName`、`Remark`、`CreatedAt`、`UpdatedAt`、`LastTestStatus`、`LastTestAt`、`LastTestLatencyMs`、`IsEnabled`、`ModelRole`。
- `AiGenerationOptions` 继续作为 `AiApiClient` 的运行时 options，不重复造 API client。
- `AiConfigStore` 保存路径为运行目录下 `ai_models.json`，旧 `ai_config.json` 会迁移；API key 通过 `ai_model_secrets/*.dpapi` 保存，`ai_models.json` 只持久化空 `apiKey`。
- `AiApiClient` 与 `AiGenerationOrchestrator` 继续复用；Test Connection 和 shadow eval 均不新增业务 API client。
- `IAiConnectorFactory` 仍通过 `AiApiClientAdapterConnector` 路由到现有 `AiApiClient`。
- `settingsApi` 已新增 planner/shadow 默认模型端点。
- AI settings tab 之前只能编辑基础模型、BaseUrl、key、timeout、wireApi/reasoning；本轮增加模型列表产品化字段、key 操作、roles、Test Connection 结构化结果。
- `LlmVisionAgentPlannerCompletionSource` 继续通过 `AiGenerationOrchestrator.ResolveModelForRole` 选模型；默认 role 从 generation 收敛为 planner。
- `VisionAgentPlannerShadowEvalRunner` 保留环境变量入口，并新增 saved model config 入口；默认 CI 仍关闭真实 LLM。

## 配置字段

模型配置字段：`Provider`、`Protocol`、`WireApi`、`BaseUrl`、`Model`、`AuthMode`、`ApiKey`、`TimeoutMs`、`ModelRole`、`RoleBindings`、`IsActive`、`IsEnabled`、`Priority`、`DisplayName`、`Remark`、`CreatedAt`、`UpdatedAt`、`LastTestStatus`、`LastTestAt`、`LastTestLatencyMs`。

Role binding：

- `generation`
- `planner`
- `vision-agent-shadow-eval`
- 兼容原有 `reasoning`、`fallback`、`validation`、`vision`

## API Key 语义

- `keep`：不修改已保存 key，前端提交空 `apiKey`。
- `replace`：用新 key 覆盖旧 key。
- `clear`：清空已保存 key，并删除 secret store 中对应 key。
- `new`：新建或首次保存 key，后端按 replace 处理。

前端永不回显完整 key；后端列表返回 `hasApiKey` 和 `apiKeyMasked`，不返回明文 `ApiKey`。日志、报告、artifact、测试输出统一走 sanitizer 或 secret guard。

## Test Connection

Test Connection 使用当前模型配置发最小 JSON health-check 请求：

- 不生成 workflow。
- 不触发 RuntimePreview。
- 不触发 DeploymentPrepare。
- 不访问相机、Station、PLC。
- 不长期落盘 prompt/response 明文。

返回字段：`connectionOk`、`success`、`statusCode`、`errorCode`、`latencyMs`、`sanitizedMessage`、`message`、`provider`、`modelName`、`protocol`、`wireApi`。

错误分类：`missing_api_key`、`auth_failed`、`model_not_found`、`base_url_error`、`timeout`、`bad_response`、`provider_error`、`http_error`、`response_format_error`。

## CPA Bridge

脚本：`quality/tools/run_real_llm_shadow_eval_from_codex_config.ps1`

支持环境变量：

- `CV_AGENT_CPA_PROVIDER` / `CPA_PROVIDER` / `CODEX_CPA_PROVIDER`
- `CV_AGENT_CPA_MODEL` / `CPA_MODEL` / `CODEX_CPA_MODEL`
- `CV_AGENT_CPA_BASE_URL` / `CPA_BASE_URL` / `CODEX_CPA_BASE_URL`
- `CV_AGENT_CPA_API_KEY` / `CPA_API_KEY` / `CODEX_CPA_API_KEY`
- `CV_AGENT_CPA_AUTH_MODE`、`CV_AGENT_CPA_WIRE_API`、`CV_AGENT_CPA_PROTOCOL`、`CV_AGENT_CPA_TIMEOUT_MS`

脚本只把配置写入当前进程环境变量，并调用既有 shadow eval runner。脚本不会打印 API key 或完整 BaseUrl。

## Shadow Eval

默认关闭：

```powershell
$env:CV_AGENT_REAL_LLM_SHADOW_EVAL = 'false'
```

显式开启后只做 parse、policy check、plannedToolCalls、toolPlanMatchScore、unsafe detection、fallback suggestion。不会执行 RuntimePreview、DeploymentPrepare、workflow、打包、部署、PLC、Station、相机、真实图片或真实视觉模型文件。

新增 saved model config 入口：

```powershell
dotnet run --project quality/tools/VisionAgentPlannerShadowEvalRunner/VisionAgentPlannerShadowEvalRunner.csproj -- `
  --model-config-role vision-agent-shadow-eval `
  --model-config-dir <ai-config-storage-dir>
```

## 脱敏策略

统一工具：`AiSecretSanitizer`

覆盖：`ApiKey`、`Authorization`、`Bearer`、`x-api-key`、`api-key`、URL query token、userinfo、疑似 token path。报告中的 BaseUrl 只保留 scheme 和 `<redacted-host>`。

## CI 策略

稳定 CI 不跑真实 LLM，原因：

- 真实 LLM 成本和网络稳定性不可控。
- planner autonomy benchmark 已作为稳定门禁。
- shadow eval 只作为人工/显式配置的对比评估，不替代 mock benchmark。

## 后续增强

- 当前 Windows 下已使用 DPAPI file secret store；后续可扩展为系统凭据库/企业 KMS。
- 可以新增 UI 内部按钮触发 saved model shadow eval，但仍需 developer hidden UI 默认关闭。

## 安全边界

本轮未推进真实相机 SDK、真实 Station、真实图片读取、真实视觉模型加载、PLC 写、打包、下发或热加载。

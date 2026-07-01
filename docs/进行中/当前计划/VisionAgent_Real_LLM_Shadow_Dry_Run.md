# Vision Agent Real LLM Shadow Dry Run

## Codex Config CPA Fallback

`quality/tools/run_real_llm_shadow_eval_from_codex_config.ps1` reads explicit CPA environment variables first. If they are missing, it also reads the current Codex `config.toml` from `CODEX_CONFIG_PATH`, `CODEX_HOME/config.toml`, or `$HOME/.codex/config.toml`.

The Codex config fallback accepts a provider whose provider key, `name`, or `provider` value contains `cpa`, plus explicit aliases from `-CpaProviderAliases` or `CV_AGENT_CPA_PROVIDER_ALIASES` / `CPA_PROVIDER_ALIASES` / `CODEX_CPA_PROVIDER_ALIASES`. The default alias set is `cpa,ccswitch` because the internal Codex CPA provider can be named `ccswitch`. If the CPA provider declares `env_key`, the bridge reads that environment variable for the key without printing it.

Use inspect mode before a manual trial when you need to verify that CPA config will be selected. Inspect mode does not call the shadow eval runner and does not make network requests:

```powershell
& quality/tools/run_real_llm_shadow_eval_from_codex_config.ps1 `
  -InspectConfigOnly `
  -InspectOutput quality/evals/reports/real_llm_planner_shadow_eval.inspect.json
```

The inspect JSON reports `configSource`, `modelConfigured`, `baseUrlConfigured`, `apiKeyConfigured`, `missingReasons`, and `shadowEvalWouldRun`. It redacts BaseUrl and never includes the API key.

## 默认行为

- 稳定 CI 默认保持 `CV_AGENT_REAL_LLM_SHADOW_EVAL=false`。
- 默认只生成 skipped/sample artifact。
- 默认不读取真实 LLM 配置，不产生真实 LLM 请求。
- Mock planner autonomy benchmark 仍是稳定门禁。

## 环境变量入口

```powershell
$env:CV_AGENT_REAL_LLM_SHADOW_EVAL = 'true'
$env:CV_AGENT_REAL_LLM_MODEL = '<model-name>'
$env:CV_AGENT_REAL_LLM_PROVIDER = '<provider-name>'
$env:CV_AGENT_REAL_LLM_BASE_URL = '<provider-base-url>'
$env:CV_AGENT_REAL_LLM_AUTH_MODE = 'bearer'
$env:CV_AGENT_REAL_LLM_API_KEY = '<api-key>'
$env:CV_AGENT_REAL_LLM_MODEL_ROLE = 'vision-agent-shadow-eval'
```

```powershell
dotnet run --project quality/tools/VisionAgentPlannerShadowEvalRunner/VisionAgentPlannerShadowEvalRunner.csproj -- `
  --output quality/evals/reports/real_llm_planner_shadow_eval.manual.json `
  --report quality/evals/reports/real_llm_planner_shadow_eval.manual.md
```

## CPA Bridge 入口

```powershell
$env:CV_AGENT_CPA_MODEL = '<model-name>'
$env:CV_AGENT_CPA_BASE_URL = '<provider-base-url>'
$env:CV_AGENT_CPA_API_KEY = '<api-key>'
$env:CV_AGENT_CPA_AUTH_MODE = 'bearer'

& quality/tools/run_real_llm_shadow_eval_from_codex_config.ps1
```

Bridge 也会读取 `CPA_*`、`CODEX_CPA_*` 环境变量。脚本不会打印 API key 或完整 BaseUrl。

## Saved Model Config 入口

```powershell
$env:CV_AGENT_REAL_LLM_SHADOW_EVAL = 'true'

dotnet run --project quality/tools/VisionAgentPlannerShadowEvalRunner/VisionAgentPlannerShadowEvalRunner.csproj -- `
  --model-config-role vision-agent-shadow-eval `
  --model-config-dir '<ai-config-storage-dir>' `
  --output quality/evals/reports/real_llm_planner_shadow_eval.manual.json `
  --report quality/evals/reports/real_llm_planner_shadow_eval.manual.md
```

也可使用 `--model-config-id <id>` 精确指定模型配置。

## 报告字段

- `modelName`
- `provider`, `protocol`, `wireApi`, `authMode`
- `plannedToolCalls`
- `policyDecision`
- `parseSuccess`
- `invalidJsonRepairUsed`
- `toolPlanMatchScore`
- `unsafeToolAttempted`
- `fallbackToMockSuggested`
- `requestCount`
- `parseSuccessRate`
- `repairUsedRate`
- `unsafeAttemptRate`
- `averageToolPlanMatchScore`
- `enabledReason`
- `skippedReason`
- `configurationMissingReason`

## 配置缺失行为

- 未设置 `CV_AGENT_REAL_LLM_SHADOW_EVAL=true`：返回 skipped/sample artifact，不失败。
- 已启用但缺 model/key：runner 返回非 0，并写入 `configurationMissingReason`。
- 报告不得输出 API key。
- BaseUrl 必须脱敏为 `<redacted-host>`。

## 安全边界

- `workflowExecutionAttempted=false`
- `deploymentPrepareExecuted=false`
- `realCameraSdkTouched=false`
- `realStationTouched=false`
- `realImageFilesRead=false`
- `realModelFilesLoaded=false`
- `plcWriteAttempted=false`
- 不执行 RuntimePreview、DeploymentPrepare、workflow、打包、部署、热加载或配置写入。

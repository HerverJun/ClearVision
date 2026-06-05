# Vision Agent Real LLM Shadow Dry Run

本说明用于手动执行 Real LLM planner shadow eval。该流程不进入稳定 CI 门禁，默认关闭，只对模型 planner 输出做 parse、policy check 和 tool plan match score，不执行 RuntimePreview、DeploymentPrepare、workflow、打包、部署或配置写入。

## 默认行为

- CI 默认保持 `CV_AGENT_REAL_LLM_SHADOW_EVAL=false`。
- 默认运行只生成 skipped/sample artifact。
- 默认不读取 `CV_AGENT_REAL_LLM_*` 配置，不产生真实 LLM 请求。
- Mock planner autonomy benchmark 仍是稳定质量门禁。

## 手动开启

在本地 PowerShell 中显式设置：

```powershell
$env:CV_AGENT_REAL_LLM_SHADOW_EVAL = 'true'
$env:CV_AGENT_REAL_LLM_MODEL = '<model-name>'
$env:CV_AGENT_REAL_LLM_PROVIDER = '<provider-name>'
$env:CV_AGENT_REAL_LLM_BASE_URL = '<provider-base-url>'
$env:CV_AGENT_REAL_LLM_AUTH_MODE = 'bearer'
$env:CV_AGENT_REAL_LLM_API_KEY = '<api-key>'
```

执行 runner：

```powershell
dotnet run --project quality/tools/VisionAgentPlannerShadowEvalRunner/VisionAgentPlannerShadowEvalRunner.csproj -- `
  --output quality/evals/reports/real_llm_planner_shadow_eval.manual.json `
  --report quality/evals/reports/real_llm_planner_shadow_eval.manual.md
```

## 报告字段

- `modelName`
- `plannedToolCalls`
- `policyDecision`
- `parseSuccess`
- `invalidJsonRepairUsed`
- `toolPlanMatchScore`
- `unsafeToolAttempted`
- `fallbackToMockSuggested`
- `enabledReason`
- `skippedReason`
- `configurationMissingReason`
- `provider`, `protocol`, `wireApi`, `authMode`
- `requestCount`
- `parseSuccessRate`
- `unsafeAttemptRate`
- `averageToolPlanMatchScore`

## 配置缺失行为

- `CV_AGENT_REAL_LLM_SHADOW_EVAL` 未设为 `true`：返回 skipped/sample artifact，不失败。
- `CV_AGENT_REAL_LLM_SHADOW_EVAL=true` 但缺少 provider/model/base URL/API key 等必要配置：runner 可以返回非 0，并在报告写入 `configurationMissingReason`。
- 报告不得输出 API key。
- BaseUrl 必须脱敏。

## 安全声明

- `workflowExecutionAttempted=false`
- `deploymentPrepareExecuted=false`
- `realCameraSdkTouched=false`
- `realStationTouched=false`
- `realImageFilesRead=false`
- `realModelFilesLoaded=false`
- `plcWriteAttempted=false`
- RuntimePreview 不执行真实 adapter。
- DeploymentPrepare 不执行，非 precheck 工具只做 unsafe/denied 记录。

param(
    [string]$Output = "quality/evals/reports/real_llm_planner_shadow_eval.manual.json",
    [string]$Report = "quality/evals/reports/real_llm_planner_shadow_eval.manual.md",
    [string]$ModelConfigId = "",
    [string]$ModelConfigRole = "",
    [string]$ModelConfigDir = ""
)

$ErrorActionPreference = "Stop"

function Read-FirstEnv {
    param([string[]]$Names, [string]$Fallback = "")
    foreach ($name in $Names) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }
    return $Fallback
}

function Set-ShadowEnv {
    param([string]$Name, [string]$Value)
    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
    }
}

$provider = Read-FirstEnv @("CV_AGENT_CPA_PROVIDER", "CPA_PROVIDER", "CODEX_CPA_PROVIDER") "CPA OpenAI Compatible"
$model = Read-FirstEnv @("CV_AGENT_CPA_MODEL", "CPA_MODEL", "CODEX_CPA_MODEL", "CV_AGENT_REAL_LLM_MODEL")
$baseUrl = Read-FirstEnv @("CV_AGENT_CPA_BASE_URL", "CPA_BASE_URL", "CODEX_CPA_BASE_URL", "CV_AGENT_REAL_LLM_BASE_URL")
$apiKey = Read-FirstEnv @("CV_AGENT_CPA_API_KEY", "CPA_API_KEY", "CODEX_CPA_API_KEY", "CV_AGENT_REAL_LLM_API_KEY")
$authMode = Read-FirstEnv @("CV_AGENT_CPA_AUTH_MODE", "CPA_AUTH_MODE", "CODEX_CPA_AUTH_MODE", "CV_AGENT_REAL_LLM_AUTH_MODE") "bearer"
$wireApi = Read-FirstEnv @("CV_AGENT_CPA_WIRE_API", "CPA_WIRE_API", "CODEX_CPA_WIRE_API", "CV_AGENT_REAL_LLM_WIRE_API") "chat_completions"
$protocol = Read-FirstEnv @("CV_AGENT_CPA_PROTOCOL", "CPA_PROTOCOL", "CODEX_CPA_PROTOCOL", "CV_AGENT_REAL_LLM_PROTOCOL") "openai_compatible"
$timeoutMs = Read-FirstEnv @("CV_AGENT_CPA_TIMEOUT_MS", "CPA_TIMEOUT_MS", "CODEX_CPA_TIMEOUT_MS", "CV_AGENT_REAL_LLM_TIMEOUT_MS") "120000"

[Environment]::SetEnvironmentVariable("CV_AGENT_REAL_LLM_SHADOW_EVAL", "true", "Process")
Set-ShadowEnv "CV_AGENT_REAL_LLM_PROVIDER" $provider
Set-ShadowEnv "CV_AGENT_REAL_LLM_MODEL" $model
Set-ShadowEnv "CV_AGENT_REAL_LLM_BASE_URL" $baseUrl
Set-ShadowEnv "CV_AGENT_REAL_LLM_API_KEY" $apiKey
Set-ShadowEnv "CV_AGENT_REAL_LLM_AUTH_MODE" $authMode
Set-ShadowEnv "CV_AGENT_REAL_LLM_WIRE_API" $wireApi
Set-ShadowEnv "CV_AGENT_REAL_LLM_PROTOCOL" $protocol
Set-ShadowEnv "CV_AGENT_REAL_LLM_TIMEOUT_MS" $timeoutMs
Set-ShadowEnv "CV_AGENT_REAL_LLM_MODEL_ROLE" "vision-agent-shadow-eval"

$args = @(
    "run",
    "--project",
    "quality/tools/VisionAgentPlannerShadowEvalRunner/VisionAgentPlannerShadowEvalRunner.csproj",
    "--",
    "--output",
    $Output,
    "--report",
    $Report
)

if (-not [string]::IsNullOrWhiteSpace($ModelConfigId)) {
    $args += @("--model-config-id", $ModelConfigId)
}
if (-not [string]::IsNullOrWhiteSpace($ModelConfigRole)) {
    $args += @("--model-config-role", $ModelConfigRole)
}
if (-not [string]::IsNullOrWhiteSpace($ModelConfigDir)) {
    $args += @("--model-config-dir", $ModelConfigDir)
}

Write-Host "Starting CPA shadow eval bridge. Secrets and full BaseUrl are not printed."
& dotnet @args
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    Write-Host "CPA shadow eval bridge finished with exit code $exitCode. See the redacted manual report for details."
    exit $exitCode
}

Write-Host "CPA shadow eval bridge completed. See redacted manual report artifacts."

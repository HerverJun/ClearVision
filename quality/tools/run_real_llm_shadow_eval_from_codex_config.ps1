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

function Convert-CodexTomlValue {
    param([string]$RawValue)

    $value = $RawValue.Trim()
    if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
        return $value.Substring(1, $value.Length - 2)
    }
    if ($value.StartsWith("'") -and $value.EndsWith("'") -and $value.Length -ge 2) {
        return $value.Substring(1, $value.Length - 2)
    }
    return $value
}

function Get-CodexConfigPath {
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_CONFIG_PATH)) {
        $candidates += $env:CODEX_CONFIG_PATH
    }
    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
        $candidates += (Join-Path $env:CODEX_HOME "config.toml")
    }
    if (-not [string]::IsNullOrWhiteSpace($HOME)) {
        $candidates += (Join-Path $HOME ".codex/config.toml")
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    return ""
}

function Read-CodexConfig {
    param([string]$Path)

    $result = @{
        Root = @{}
        Providers = @{}
    }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $result
    }

    $scope = "root"
    $providerKey = ""
    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        if ($trimmed -match '^\[model_providers\.([^\]]+)\]$') {
            $scope = "provider"
            $providerKey = $Matches[1].Trim('"').Trim("'")
            if (-not $result.Providers.ContainsKey($providerKey)) {
                $result.Providers[$providerKey] = @{}
            }
            continue
        }

        if ($trimmed -match '^\[[^\]]+\]$') {
            $scope = "other"
            $providerKey = ""
            continue
        }

        if ($trimmed -notmatch '^([A-Za-z0-9_-]+)\s*=\s*(.+)$') {
            continue
        }

        $key = $Matches[1]
        $value = Convert-CodexTomlValue $Matches[2]
        if ($scope -eq "root") {
            $result.Root[$key] = $value
        }
        elseif ($scope -eq "provider" -and -not [string]::IsNullOrWhiteSpace($providerKey)) {
            $result.Providers[$providerKey][$key] = $value
        }
    }

    return $result
}

function Test-IsCpaProvider {
    param([string]$ProviderKey, [hashtable]$ProviderConfig)

    if ($ProviderKey -match 'cpa') {
        return $true
    }
    if ($ProviderConfig.ContainsKey("name") -and [string]$ProviderConfig["name"] -match 'cpa') {
        return $true
    }
    if ($ProviderConfig.ContainsKey("provider") -and [string]$ProviderConfig["provider"] -match 'cpa') {
        return $true
    }
    return $false
}

function Get-CodexCpaConfig {
    $path = Get-CodexConfigPath
    $config = Read-CodexConfig $path
    $selectedKey = ""
    $providerConfig = $null

    $rootProvider = ""
    if ($config.Root.ContainsKey("model_provider")) {
        $rootProvider = [string]$config.Root["model_provider"]
    }

    if (-not [string]::IsNullOrWhiteSpace($rootProvider) -and $config.Providers.ContainsKey($rootProvider)) {
        $candidate = $config.Providers[$rootProvider]
        if (Test-IsCpaProvider $rootProvider $candidate) {
            $selectedKey = $rootProvider
            $providerConfig = $candidate
        }
    }

    if ($null -eq $providerConfig) {
        foreach ($key in $config.Providers.Keys) {
            $candidate = $config.Providers[$key]
            if (Test-IsCpaProvider $key $candidate) {
                $selectedKey = $key
                $providerConfig = $candidate
                break
            }
        }
    }

    if ($null -eq $providerConfig) {
        return @{}
    }

    $envKey = ""
    if ($providerConfig.ContainsKey("env_key")) {
        $envKey = [string]$providerConfig["env_key"]
    }

    $apiKey = ""
    if (-not [string]::IsNullOrWhiteSpace($envKey)) {
        $apiKey = [Environment]::GetEnvironmentVariable($envKey)
    }

    return @{
        Provider = $(if ($providerConfig.ContainsKey("name")) { [string]$providerConfig["name"] } else { $selectedKey })
        Model = $(if ($config.Root.ContainsKey("model")) { [string]$config.Root["model"] } else { "" })
        BaseUrl = $(if ($providerConfig.ContainsKey("base_url")) { [string]$providerConfig["base_url"] } else { "" })
        WireApi = $(if ($providerConfig.ContainsKey("wire_api")) { [string]$providerConfig["wire_api"] } else { "chat_completions" })
        Protocol = "openai_compatible"
        AuthMode = "bearer"
        ApiKey = $apiKey
        Source = "codex-config"
    }
}

$codexCpa = Get-CodexCpaConfig

$provider = Read-FirstEnv @("CV_AGENT_CPA_PROVIDER", "CPA_PROVIDER", "CODEX_CPA_PROVIDER") "CPA OpenAI Compatible"
$model = Read-FirstEnv @("CV_AGENT_CPA_MODEL", "CPA_MODEL", "CODEX_CPA_MODEL", "CV_AGENT_REAL_LLM_MODEL")
$baseUrl = Read-FirstEnv @("CV_AGENT_CPA_BASE_URL", "CPA_BASE_URL", "CODEX_CPA_BASE_URL", "CV_AGENT_REAL_LLM_BASE_URL")
$apiKey = Read-FirstEnv @("CV_AGENT_CPA_API_KEY", "CPA_API_KEY", "CODEX_CPA_API_KEY", "CV_AGENT_REAL_LLM_API_KEY")
$authMode = Read-FirstEnv @("CV_AGENT_CPA_AUTH_MODE", "CPA_AUTH_MODE", "CODEX_CPA_AUTH_MODE", "CV_AGENT_REAL_LLM_AUTH_MODE") "bearer"
$wireApi = Read-FirstEnv @("CV_AGENT_CPA_WIRE_API", "CPA_WIRE_API", "CODEX_CPA_WIRE_API", "CV_AGENT_REAL_LLM_WIRE_API") "chat_completions"
$protocol = Read-FirstEnv @("CV_AGENT_CPA_PROTOCOL", "CPA_PROTOCOL", "CODEX_CPA_PROTOCOL", "CV_AGENT_REAL_LLM_PROTOCOL") "openai_compatible"
$timeoutMs = Read-FirstEnv @("CV_AGENT_CPA_TIMEOUT_MS", "CPA_TIMEOUT_MS", "CODEX_CPA_TIMEOUT_MS", "CV_AGENT_REAL_LLM_TIMEOUT_MS") "120000"

if (-not [string]::IsNullOrWhiteSpace($codexCpa.Provider)) {
    if ([string]::IsNullOrWhiteSpace($provider) -or $provider -eq "CPA OpenAI Compatible") { $provider = $codexCpa.Provider }
    if ([string]::IsNullOrWhiteSpace($model)) { $model = $codexCpa.Model }
    if ([string]::IsNullOrWhiteSpace($baseUrl)) { $baseUrl = $codexCpa.BaseUrl }
    if ([string]::IsNullOrWhiteSpace($apiKey)) { $apiKey = $codexCpa.ApiKey }
    if ([string]::IsNullOrWhiteSpace($wireApi)) { $wireApi = $codexCpa.WireApi }
    if ([string]::IsNullOrWhiteSpace($protocol)) { $protocol = $codexCpa.Protocol }
    if ([string]::IsNullOrWhiteSpace($authMode)) { $authMode = $codexCpa.AuthMode }
}

$missingReasons = @()
if ([string]::IsNullOrWhiteSpace($codexCpa.Provider) -and
    [string]::IsNullOrWhiteSpace($model) -and
    [string]::IsNullOrWhiteSpace($baseUrl) -and
    [string]::IsNullOrWhiteSpace($apiKey)) {
    $missingReasons += "No CPA provider was found in explicit CPA environment variables or Codex config.toml."
}
if ([string]::IsNullOrWhiteSpace($model)) {
    $missingReasons += "CPA model is missing; set CV_AGENT_CPA_MODEL, CPA_MODEL, CODEX_CPA_MODEL, or Codex root model with a CPA provider."
}
if ([string]::IsNullOrWhiteSpace($apiKey) -and $authMode -ne "none") {
    $missingReasons += "CPA API key is missing; set CV_AGENT_CPA_API_KEY, CPA_API_KEY, CODEX_CPA_API_KEY, or the Codex provider env_key variable."
}
if ([string]::IsNullOrWhiteSpace($baseUrl)) {
    $missingReasons += "CPA BaseUrl is missing; set CV_AGENT_CPA_BASE_URL, CPA_BASE_URL, CODEX_CPA_BASE_URL, or Codex provider base_url."
}
if ($missingReasons.Count -gt 0) {
    Set-ShadowEnv "CV_AGENT_REAL_LLM_CONFIGURATION_MISSING_REASON" ($missingReasons -join " ")
}

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

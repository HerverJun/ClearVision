[CmdletBinding()]
param(
    [string]$DotnetPath,

    [switch]$ValidateGlobalJsonOnly,

    [switch]$ValidateWorkflows,

    [switch]$SelfTest,

    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$globalJsonPath = Join-Path $repoRoot "global.json"
$expectedBaseline = "9.0.300"
$expectedRollForward = "latestPatch"
$acceptedVersionPattern = '^9\.0\.3[0-9]{2}$'

function Test-ResolvedSdkVersion {
    param(
        [AllowEmptyString()]
        [string]$Version
    )

    return $Version -cmatch $acceptedVersionPattern
}

function Test-GlobalJsonPolicyValues {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Sdk
    )

    return [string]$Sdk.version -ceq $expectedBaseline -and
        [string]$Sdk.rollForward -ceq $expectedRollForward
}

function Assert-GlobalJsonPolicy {
    if (-not (Test-Path -LiteralPath $globalJsonPath -PathType Leaf)) {
        throw "Cannot find repository global.json: $globalJsonPath"
    }

    $globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
    if ($null -eq $globalJson.sdk -or -not (Test-GlobalJsonPolicyValues -Sdk $globalJson.sdk)) {
        $actualVersion = [string]$globalJson.sdk.version
        $actualRollForward = [string]$globalJson.sdk.rollForward
        throw "global.json must declare sdk.version=$expectedBaseline and sdk.rollForward=$expectedRollForward; resolved values were version='$actualVersion', rollForward='$actualRollForward'."
    }

    if (-not $Quiet) {
        Write-Host "[sdk-policy] global.json: version=$expectedBaseline rollForward=$expectedRollForward"
    }
}

function Invoke-ResolvedSdkValidation {
    $dotnetCommand = $DotnetPath
    if ([string]::IsNullOrWhiteSpace($dotnetCommand)) {
        $command = Get-Command dotnet -ErrorAction Stop
        $dotnetCommand = $command.Source
    }

    Push-Location -LiteralPath $repoRoot
    try {
        $versionOutput = @(& $dotnetCommand --version 2>&1)
        $dotnetExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($dotnetExitCode -ne 0) {
        throw "'$dotnetCommand --version' failed with exit code $dotnetExitCode."
    }

    $resolvedVersion = [string]($versionOutput | Select-Object -Last 1)
    $resolvedVersion = $resolvedVersion.Trim()
    if (-not (Test-ResolvedSdkVersion -Version $resolvedVersion)) {
        throw "Resolved SDK '$resolvedVersion' is outside the allowed 9.0.3xx feature band (9.0.300-9.0.399)."
    }

    if (-not $Quiet) {
        Write-Host "[sdk-policy] resolved SDK: $resolvedVersion"
        Write-Host "[sdk-policy] dotnet host: $dotnetCommand"
    }
}

function Assert-WorkflowPolicyCoverage {
    $workflowRoot = Join-Path $repoRoot ".github\workflows"
    $workflowFiles = Get-ChildItem -LiteralPath $workflowRoot -File |
        Where-Object { $_.Extension -in @('.yml', '.yaml') }
    $setupCount = 0

    foreach ($workflowFile in $workflowFiles) {
        $lines = @(Get-Content -LiteralPath $workflowFile.FullName)
        for ($index = 0; $index -lt $lines.Count; $index += 1) {
            if ($lines[$index] -notmatch '^\s*uses:\s*actions/setup-dotnet@') {
                continue
            }

            $setupCount += 1
            $nextStepIndex = -1
            for ($cursor = $index + 1; $cursor -lt $lines.Count; $cursor += 1) {
                if ($lines[$cursor] -match '^\s*-\s+name:\s*(.+?)\s*$') {
                    $nextStepIndex = $cursor
                    break
                }
            }

            $setupStepEnd = if ($nextStepIndex -ge 0) { $nextStepIndex - 1 } else { $lines.Count - 1 }
            $setupStepBody = ($lines[$index..$setupStepEnd] -join "`n")
            if ($setupStepBody -notmatch '(?m)^\s*global-json-file:\s*global\.json\s*$') {
                throw "$($workflowFile.Name): setup-dotnet at line $($index + 1) must use global-json-file: global.json."
            }

            if ($nextStepIndex -lt 0 -or $lines[$nextStepIndex] -notmatch '^\s*-\s+name:\s*Validate Resolved \.NET SDK Policy\s*$') {
                throw "$($workflowFile.Name): setup-dotnet at line $($index + 1) must be followed immediately by the SDK policy validator step."
            }

            $nextStepEnd = $lines.Count - 1
            for ($cursor = $nextStepIndex + 1; $cursor -lt $lines.Count; $cursor += 1) {
                if ($lines[$cursor] -match '^\s*-\s+name:') {
                    $nextStepEnd = $cursor - 1
                    break
                }
            }

            $validatorStepBody = ($lines[$nextStepIndex..$nextStepEnd] -join "`n")
            if ($validatorStepBody -notmatch '\./scripts/validate-dotnet-sdk-policy\.ps1') {
                throw "$($workflowFile.Name): validator step after setup-dotnet at line $($index + 1) does not invoke scripts/validate-dotnet-sdk-policy.ps1."
            }
        }
    }

    if ($setupCount -eq 0) {
        throw "No actions/setup-dotnet usage was found under $workflowRoot."
    }

    if (-not $Quiet) {
        Write-Host "[sdk-policy] workflow coverage: $setupCount setup-dotnet step(s), $setupCount validator step(s)"
    }
}

function Invoke-PolicySelfTest {
    $versionCases = @(
        @{ Version = '9.0.300'; Expected = $true },
        @{ Version = '9.0.305'; Expected = $true },
        @{ Version = '9.0.399'; Expected = $true },
        @{ Version = '9.0.299'; Expected = $false },
        @{ Version = '9.0.400'; Expected = $false },
        @{ Version = '10.0.100'; Expected = $false },
        @{ Version = '9.0.300-preview.1'; Expected = $false },
        @{ Version = ''; Expected = $false }
    )
    $passed = 0

    foreach ($case in $versionCases) {
        $actual = Test-ResolvedSdkVersion -Version $case.Version
        if ($actual -ne $case.Expected) {
            throw "Self-test failed for SDK version '$($case.Version)': expected $($case.Expected), got $actual."
        }
        $passed += 1
    }

    $policyCases = @(
        @{ Sdk = [pscustomobject]@{ version = '9.0.300'; rollForward = 'latestPatch' }; Expected = $true },
        @{ Sdk = [pscustomobject]@{ version = '9.0.301'; rollForward = 'latestPatch' }; Expected = $false },
        @{ Sdk = [pscustomobject]@{ version = '9.0.300'; rollForward = 'latestFeature' }; Expected = $false },
        @{ Sdk = [pscustomobject]@{ version = '9.0.300'; rollForward = 'disable' }; Expected = $false }
    )

    foreach ($case in $policyCases) {
        $actual = Test-GlobalJsonPolicyValues -Sdk $case.Sdk
        if ($actual -ne $case.Expected) {
            throw "Self-test failed for global.json values version='$($case.Sdk.version)', rollForward='$($case.Sdk.rollForward)'."
        }
        $passed += 1
    }

    if (-not $Quiet) {
        Write-Host "[sdk-policy] self-test: $passed/$passed checks passed"
    }
}

try {
    if ($SelfTest) {
        Invoke-PolicySelfTest
    }
    elseif ($ValidateWorkflows) {
        Assert-GlobalJsonPolicy
        Assert-WorkflowPolicyCoverage
    }
    elseif ($ValidateGlobalJsonOnly) {
        Assert-GlobalJsonPolicy
    }
    else {
        Assert-GlobalJsonPolicy
        Invoke-ResolvedSdkValidation
    }

    exit 0
}
catch {
    if (-not $Quiet) {
        [Console]::Error.WriteLine("[sdk-policy] ERROR: $($_.Exception.Message)")
    }
    exit 1
}

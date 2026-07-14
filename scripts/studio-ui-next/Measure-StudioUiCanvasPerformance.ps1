[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceDirectory,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
if (-not (Test-Path -LiteralPath $evidenceRoot -PathType Container)) {
    throw "Canvas performance evidence directory was not found: $evidenceRoot"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $evidenceRoot "studio-ui-canvas-performance-summary.json"
} else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if ($Values.Count -eq 0) {
        return $null
    }
    $ordered = @($Values | Sort-Object)
    if ($ordered.Count -eq 1) {
        return [double]$ordered[0]
    }
    $position = ($ordered.Count - 1) * $Percentile
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    if ($lower -eq $upper) {
        return [double]$ordered[$lower]
    }
    $weight = $position - $lower
    return [double]$ordered[$lower] * (1 - $weight) + [double]$ordered[$upper] * $weight
}

function Get-SampleStatistics {
    param([double[]]$Values)

    if ($Values.Count -eq 0) {
        return [pscustomobject]@{
            count = 0
            median = $null
            p95 = $null
            max = $null
            min = $null
        }
    }
    return [pscustomobject]@{
        count = $Values.Count
        median = Get-Percentile -Values $Values -Percentile 0.5
        p95 = Get-Percentile -Values $Values -Percentile 0.95
        max = [double]($Values | Measure-Object -Maximum).Maximum
        min = [double]($Values | Measure-Object -Minimum).Minimum
    }
}

function Read-RawEvidence {
    param([System.IO.FileInfo]$File)

    try {
        $value = Get-Content -Raw -LiteralPath $File.FullName | ConvertFrom-Json
    } catch {
        throw "Invalid Canvas performance JSON '$($File.FullName)': $($_.Exception.Message)"
    }
    if ($value.schemaVersion -ne 1) {
        throw "Unsupported Canvas performance schema in '$($File.FullName)'."
    }
    if ($value.runtime -notin @("legacy", "studio")) {
        throw "Canvas performance evidence has an invalid runtime in '$($File.FullName)'."
    }
    return [pscustomobject]@{
        path = $File.FullName
        value = $value
    }
}

function Get-ScenarioStatistics {
    param([object]$Scenario)

    $samples = @($Scenario.formalSamples)
    $latencies = @($samples | ForEach-Object { [double]$_.inputToDoubleRafMs })
    $longTaskTotals = @($samples | ForEach-Object { [double]$_.longTaskTotalMs })
    $longTaskMaxima = @($samples | ForEach-Object { [double]$_.longTaskMaxMs })
    $heapUsed = @($samples | ForEach-Object {
        if ($null -ne $_.memoryAfter.cdpHeap.usedSize) {
            [double]$_.memoryAfter.cdpHeap.usedSize
        }
    })
    return [pscustomobject]@{
        latencyMs = Get-SampleStatistics -Values $latencies
        longTaskTotalMs = Get-SampleStatistics -Values $longTaskTotals
        longTaskMaxMs = Get-SampleStatistics -Values $longTaskMaxima
        cdpHeapUsedBytes = Get-SampleStatistics -Values $heapUsed
    }
}

$files = @(Get-ChildItem -LiteralPath $evidenceRoot -Recurse -File -Filter "studio-ui-canvas-performance-*.json" |
    Where-Object { $_.Name -ne "studio-ui-canvas-performance-summary.json" })
if ($files.Count -eq 0) {
    throw "No raw Canvas performance evidence was found under $evidenceRoot."
}

$raw = @($files | ForEach-Object { Read-RawEvidence -File $_ })
$groups = @{}
foreach ($entry in $raw) {
    $groupId = [string]$entry.value.comparisonGroup
    if ([string]::IsNullOrWhiteSpace($groupId)) {
        throw "Canvas performance evidence is missing comparisonGroup: $($entry.path)"
    }
    if (-not $groups.ContainsKey($groupId)) {
        $groups[$groupId] = @{}
    }
    $runtime = [string]$entry.value.runtime
    if ($groups[$groupId].ContainsKey($runtime)) {
        throw "Comparison group '$groupId' contains duplicate '$runtime' evidence."
    }
    $groups[$groupId][$runtime] = $entry
}

$hardFailures = [System.Collections.Generic.List[object]]::new()
$groupResults = [System.Collections.Generic.List[object]]::new()
foreach ($groupId in @($groups.Keys | Sort-Object)) {
    $pair = $groups[$groupId]
    if (-not $pair.ContainsKey("legacy") -or -not $pair.ContainsKey("studio")) {
        $hardFailures.Add([pscustomobject]@{
            group = $groupId
            code = "missing-runtime-pair"
            detail = "A standardized group requires one Legacy and one Studio sample set."
        })
        continue
    }

    $legacy = $pair["legacy"]
    $studio = $pair["studio"]
    foreach ($entry in @($legacy, $studio)) {
        if ($entry.value.status -ne "pass" -or $entry.value.correctness.passed -ne $true) {
            $failureDetail = if ($entry.value.PSObject.Properties.Name -contains "error") {
                [string]$entry.value.error
            } else {
                "Raw evidence did not pass correctness gates."
            }
            $hardFailures.Add([pscustomobject]@{
                group = $groupId
                runtime = $entry.value.runtime
                code = "correctness-or-runtime-failure"
                detail = $failureDetail
                evidence = $entry.path
            })
        }
        if ([int]$entry.value.warmups -lt 2 -or [int]$entry.value.formalSamples -lt 5) {
            $hardFailures.Add([pscustomobject]@{
                group = $groupId
                runtime = $entry.value.runtime
                code = "insufficient-samples"
                detail = "Every scenario requires at least 2 warmups and 5 formal samples."
                evidence = $entry.path
            })
        }
    }

    $legacyScenarios = @{}
    foreach ($scenario in @($legacy.value.scenarios)) {
        $legacyScenarios[[string]$scenario.id] = $scenario
    }
    $studioScenarios = @{}
    foreach ($scenario in @($studio.value.scenarios)) {
        $studioScenarios[[string]$scenario.id] = $scenario
    }
    $scenarioResults = [System.Collections.Generic.List[object]]::new()
    $regressions = [System.Collections.Generic.List[double]]::new()
    foreach ($scenarioId in @($legacyScenarios.Keys | Sort-Object)) {
        if (-not $studioScenarios.ContainsKey($scenarioId)) {
            $hardFailures.Add([pscustomobject]@{
                group = $groupId
                code = "missing-scenario-pair"
                detail = "Studio evidence is missing scenario '$scenarioId'."
            })
            continue
        }
        $legacyScenario = $legacyScenarios[$scenarioId]
        $studioScenario = $studioScenarios[$scenarioId]
        if ($legacyScenario.expectedFingerprint -ne $studioScenario.expectedFingerprint) {
            $hardFailures.Add([pscustomobject]@{
                group = $groupId
                scenario = $scenarioId
                code = "fixture-identity-mismatch"
                detail = "Legacy and Studio did not benchmark the same fixture fingerprint."
            })
        }
        $legacyStats = Get-ScenarioStatistics -Scenario $legacyScenario
        $studioStats = Get-ScenarioStatistics -Scenario $studioScenario
        $regression = if ($legacyStats.latencyMs.median -gt 0) {
            (($studioStats.latencyMs.median - $legacyStats.latencyMs.median) /
                $legacyStats.latencyMs.median) * 100.0
        } else {
            $null
        }
        if ($null -ne $regression) {
            $regressions.Add([double]$regression)
        }
        $scenarioResults.Add([pscustomobject]@{
            id = $scenarioId
            expectedFingerprint = $legacyScenario.expectedFingerprint
            legacy = $legacyStats
            studio = $studioStats
            medianRegressionPercent = $regression
            warningOver20Percent = $null -ne $regression -and $regression -gt 20.0
        })
    }

    foreach ($scenarioId in $studioScenarios.Keys) {
        if (-not $legacyScenarios.ContainsKey($scenarioId)) {
            $hardFailures.Add([pscustomobject]@{
                group = $groupId
                code = "missing-scenario-pair"
                detail = "Legacy evidence is missing scenario '$scenarioId'."
            })
        }
    }

    $aggregateRegression = if ($regressions.Count -gt 0) {
        Get-Percentile -Values @($regressions) -Percentile 0.5
    } else {
        $null
    }
    $scenarioWarning = @($scenarioResults | Where-Object {
        $_.warningOver20Percent
    }).Count -gt 0
    $regressedScenarios = @($scenarioResults |
        Where-Object { $_.warningOver20Percent } |
        ForEach-Object { $_.id })
    $aggregateWarning = $null -ne $aggregateRegression -and $aggregateRegression -gt 20.0
    $capturedAt = @(
        [DateTime]::Parse([string]$legacy.value.capturedAtUtc).ToUniversalTime(),
        [DateTime]::Parse([string]$studio.value.capturedAtUtc).ToUniversalTime()
    ) | Sort-Object | Select-Object -Last 1
    $groupResults.Add([pscustomobject]@{
        group = $groupId
        capturedAtUtc = $capturedAt.ToString("O")
        legacyEvidence = $legacy.path
        studioEvidence = $studio.path
        scenarios = @($scenarioResults)
        aggregateMedianRegressionPercent = $aggregateRegression
        aggregateWarningOver20Percent = $aggregateWarning
        regressedScenarios = $regressedScenarios
        warningOver20Percent = $scenarioWarning -or $aggregateWarning
    })
}

$orderedGroups = @($groupResults | Sort-Object capturedAtUtc)
$threeConsecutiveRegression = $false
$consecutiveRegressionScenario = $null
if ($orderedGroups.Count -ge 3) {
    for ($index = 2; $index -lt $orderedGroups.Count; $index += 1) {
        $first = $orderedGroups[$index - 2]
        $second = $orderedGroups[$index - 1]
        $third = $orderedGroups[$index]
        $commonScenarios = @($first.regressedScenarios | Where-Object {
            $_ -in $second.regressedScenarios -and $_ -in $third.regressedScenarios
        })
        if ($commonScenarios.Count -gt 0) {
            $threeConsecutiveRegression = $true
            $consecutiveRegressionScenario = [string]$commonScenarios[0]
            break
        }
        if ($first.aggregateWarningOver20Percent -and
            $second.aggregateWarningOver20Percent -and
            $third.aggregateWarningOver20Percent) {
            $threeConsecutiveRegression = $true
            $consecutiveRegressionScenario = "aggregate-median"
            break
        }
    }
}

$hasWarnings = @($orderedGroups | Where-Object { $_.warningOver20Percent }).Count -gt 0
$decision = if ($hardFailures.Count -gt 0 -or $threeConsecutiveRegression) {
    "BLOCKED"
} elseif ($hasWarnings) {
    "WARNING"
} else {
    "PASS"
}
$summary = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    evidenceRoot = $evidenceRoot
    rawEvidenceCount = $raw.Count
    completeComparisonGroupCount = $orderedGroups.Count
    decision = $decision
    blockedCode = if ($decision -eq "BLOCKED") { "BLOCKED_CANVAS_PERFORMANCE" } else { $null }
    policy = [pscustomobject]@{
        thresholdPercent = 20.0
        singleGroupOverThreshold = "WARNING_ONLY"
        blockRequiresConsecutiveGroups = 3
        correctnessLeakOrRuntimeError = "IMMEDIATE_BLOCK"
    }
    threeConsecutiveRegressionGroups = $threeConsecutiveRegression
    consecutiveRegressionScenario = $consecutiveRegressionScenario
    hardFailures = @($hardFailures)
    groups = $orderedGroups
}

$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
[System.IO.File]::WriteAllText(
    $OutputPath,
    (($summary | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

$summary | ConvertTo-Json -Depth 8

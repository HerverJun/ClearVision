param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Pr", "Nightly", "ReleaseManual")]
    [string]$Lane,

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsRoot,

    [switch]$SkipUi,

    [switch]$SkipOperatorLibrarySmoke,

    [switch]$CollectCoverage,

    [switch]$AcknowledgeManualRequirements,

    [switch]$FailFast
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$effectiveResultsRoot = if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    Join-Path $repoRoot "test_results\quality-lanes\$($Lane.ToLowerInvariant())\$timestamp"
}
else {
    if ([IO.Path]::IsPathRooted($ResultsRoot)) { [IO.Path]::GetFullPath($ResultsRoot) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsRoot)) }
}
$logDirectory = Join-Path $effectiveResultsRoot "logs"
$trxDirectory = Join-Path $effectiveResultsRoot "trx"
$reportDirectory = Join-Path $effectiveResultsRoot "reports"
$performanceReportDirectory = Join-Path $reportDirectory "performance"
foreach ($directory in @($logDirectory, $trxDirectory, $reportDirectory, $performanceReportDirectory)) {
    [IO.Directory]::CreateDirectory($directory) | Out-Null
}

$results = New-Object System.Collections.Generic.List[object]

function Invoke-LaneStep {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $logPath = Join-Path $logDirectory "$Name.log"
    $startedAt = Get-Date
    $exitCode = 0
    $status = "passed"
    Write-Host "[quality-lane] START $Name"
    try {
        $global:LASTEXITCODE = 0
        & $Action *>&1 | Tee-Object -LiteralPath $logPath | Out-Host
        $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        if ($exitCode -ne 0) { $status = "failed" }
    }
    catch {
        $status = "failed"
        $exitCode = 1
        $errorText = $_ | Out-String
        Add-Content -LiteralPath $logPath -Value $errorText -Encoding UTF8
        Write-Host $errorText
    }
    $finishedAt = Get-Date
    $result = [PSCustomObject]@{
        name = $Name
        status = $status
        exitCode = $exitCode
        startedAt = $startedAt.ToString("o")
        finishedAt = $finishedAt.ToString("o")
        durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
        log = $logPath
    }
    $results.Add($result)
    Write-Host "[quality-lane] END ${Name}: $status (exit $exitCode)"
    return $result
}

function Invoke-ClassifiedGate {
    param(
        [Parameter(Mandatory = $true)][string]$GateName,
        [switch]$SkipCoverage
    )
    $parameters = @{
        Gate = $GateName
        Verbosity = $Verbosity
        ResultsDirectory = $trxDirectory
        LogFileName = "$GateName.trx"
        ReturnExitCode = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $parameters.Configuration = $Configuration }
    if ($NoBuild) { $parameters.NoBuild = $true }
    if ($NoRestore) { $parameters.NoRestore = $true }
    if ($CollectCoverage -and -not $SkipCoverage) { $parameters.Collect = @("XPlat Code Coverage") }
    & (Join-Path $scriptRoot "run-classified-test-gate.ps1") @parameters
}

function Invoke-NpmUnit {
    param(
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$ScriptName
    )
    $npm = (Get-Command npm.cmd -ErrorAction Stop).Source
    & $npm --prefix $WorkingDirectory run $ScriptName
    if ($LASTEXITCODE -ne 0) {
        throw "npm unit test failed with exit code $LASTEXITCODE"
    }
}

function Stop-AfterFailure {
    param([object]$StepResult)
    return $StepResult.exitCode -ne 0 -and $FailFast
}

Write-Host "[quality-lane] Lane=$Lane"
Write-Host "[quality-lane] Results=$effectiveResultsRoot"

$governance = Invoke-LaneStep -Name "governance" -Action {
    & (Join-Path $scriptRoot "run-test-governance.ps1") `
        -ReportDirectory (Join-Path $reportDirectory "governance") `
        -ReturnExitCode
}

if (-not (Stop-AfterFailure $governance)) {
    $runPrPrerequisites = $Lane -in @("Pr", "Nightly")
    if ($runPrPrerequisites) {
        foreach ($gateName in @("product-pr", "desktop-pr")) {
            $step = Invoke-LaneStep -Name $gateName -Action { Invoke-ClassifiedGate -GateName $gateName }
            if (Stop-AfterFailure $step) { break }
        }

        if (-not $SkipOperatorLibrarySmoke -and -not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "operator-library-smoke" -Action { Invoke-ClassifiedGate -GateName "operator-library-smoke" })
        }

        if (-not $SkipUi -and -not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "frontend-v2-unit" -Action {
                Invoke-NpmUnit -WorkingDirectory (Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\FrontendV2") -ScriptName "test:unit"
            })
            if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
                [void](Invoke-LaneStep -Name "ui-contract-unit" -Action {
                    Invoke-NpmUnit -WorkingDirectory (Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.UI.Tests") -ScriptName "test:unit"
                })
            }
        }

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "stage4-operator-performance-smoke" -Action {
                & (Join-Path $scriptRoot "run-stage4-operator-benchmark.ps1") `
                    -Profile smoke `
                    -ResultsDirectory (Join-Path $performanceReportDirectory "stage4-smoke") `
                    -Label "pr-smoke" `
                    -ReturnExitCode
            })
        }
    }

    if ($Lane -eq "Nightly" -and -not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
        [void](Invoke-LaneStep -Name "ppf-regression" -Action {
            Invoke-ClassifiedGate -GateName "ppf-regression" -SkipCoverage
        })

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "product-nightly" -Action { Invoke-ClassifiedGate -GateName "product-nightly" })
        }

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "measurement-performance" -Action {
                $parameters = @{
                    GateProfile = "standard"
                    Verbosity = $Verbosity
                    ResultsDirectory = $trxDirectory
                    LogFileName = "measurement-performance.trx"
                    ReportDirectory = $performanceReportDirectory
                    ReturnExitCode = $true
                }
                if ($NoBuild) { $parameters.NoBuild = $true }
                if ($NoRestore) { $parameters.NoRestore = $true }
                & (Join-Path $scriptRoot "run-tests-measurement-performance.ps1") @parameters
            })
        }

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "detection-performance" -Action {
                $parameters = @{
                    GateProfile = "standard"
                    Verbosity = $Verbosity
                    ResultsDirectory = $trxDirectory
                    LogFileName = "detection-performance.trx"
                    ReportDirectory = $performanceReportDirectory
                    ReturnExitCode = $true
                }
                if ($NoBuild) { $parameters.NoBuild = $true }
                if ($NoRestore) { $parameters.NoRestore = $true }
                & (Join-Path $scriptRoot "run-tests-detection-performance.ps1") @parameters
            })
        }

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "product-nightly-performance-other" -Action { Invoke-ClassifiedGate -GateName "product-nightly-performance-other" })
        }


        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "stage4-operator-performance-standard" -Action {
                & (Join-Path $scriptRoot "run-stage4-operator-benchmark.ps1") `
                    -Profile standard `
                    -ResultsDirectory (Join-Path $performanceReportDirectory "stage4-standard") `
                    -Label "nightly-standard" `
                    -ReturnExitCode
            })
        }
    }

    if ($Lane -eq "ReleaseManual" -and -not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
        [void](Invoke-LaneStep -Name "operator-industrial-full" -Action {
            $parameters = @{ Profile = "industrial"; Verbosity = $Verbosity; PerfGateProfile = "acceptance"; ReturnExitCode = $true }
            if ($NoBuild) { $parameters.NoBuild = $true }
            if ($NoRestore) { $parameters.NoRestore = $true }
            & (Join-Path $scriptRoot "run-operator-library-industrial-gate.ps1") @parameters
        })

        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "product-release-manual" -Action {
                Invoke-ClassifiedGate -GateName "product-release-manual"
            })
        }


        if (-not ($FailFast -and @($results | Where-Object exitCode -ne 0).Count -gt 0)) {
            [void](Invoke-LaneStep -Name "stage4-operator-performance-acceptance" -Action {
                & (Join-Path $scriptRoot "run-stage4-operator-benchmark.ps1") `
                    -Profile acceptance `
                    -ResultsDirectory (Join-Path $performanceReportDirectory "stage4-acceptance") `
                    -Label "release-manual-acceptance" `
                    -ReturnExitCode
            })
        }

        if (-not $AcknowledgeManualRequirements) {
            $manualLog = Join-Path $logDirectory "manual-requirements.log"
            @(
                "Release / Manual evidence remains pending.",
                "Required acknowledgement covers physical PLC/camera/device evidence, human sign-off, delivery assets, SBOM, model identity, and package/source identity checks.",
                "A PR or Nightly green result cannot replace this acknowledgement."
            ) | Set-Content -LiteralPath $manualLog -Encoding UTF8
            $results.Add([PSCustomObject]@{
                name = "manual-requirements"
                status = "pending"
                exitCode = 3
                startedAt = (Get-Date).ToString("o")
                finishedAt = (Get-Date).ToString("o")
                durationSeconds = 0
                log = $manualLog
            })
        }
    }
}

$failed = @($results | Where-Object { $_.exitCode -ne 0 })
$summary = [ordered]@{
    schemaVersion = "2026-07-15.quality-lane.v1"
    lane = $Lane
    startedAt = if ($results.Count -gt 0) { $results[0].startedAt } else { (Get-Date).ToString("o") }
    finishedAt = (Get-Date).ToString("o")
    resultsRoot = $effectiveResultsRoot
    trxDirectory = $trxDirectory
    reportDirectory = $reportDirectory
    skipUi = [bool]$SkipUi
    skipOperatorLibrarySmoke = [bool]$SkipOperatorLibrarySmoke
    manualRequirementsAcknowledged = [bool]$AcknowledgeManualRequirements
    results = $results
    failed = @($failed | ForEach-Object { $_.name })
}
$summaryPath = Join-Path $effectiveResultsRoot "summary.json"
$markdownPath = Join-Path $effectiveResultsRoot "summary.md"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$markdown = @(
    "# Test Quality Lane Summary",
    "",
    "- Lane: $Lane",
    "- Results root: ``$effectiveResultsRoot``",
    "- Manual requirements acknowledged: $([bool]$AcknowledgeManualRequirements)",
    "",
    "| Step | Status | Exit | Seconds | Log |",
    "| --- | --- | ---: | ---: | --- |"
)
foreach ($result in $results) {
    $markdown += "| $($result.name) | $($result.status) | $($result.exitCode) | $($result.durationSeconds) | ``$($result.log)`` |"
}
$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8
Write-Host "[quality-lane] Summary JSON=$summaryPath"
Write-Host "[quality-lane] Summary Markdown=$markdownPath"

if ($failed.Count -gt 0) {
    exit ([int]($failed | Select-Object -First 1).exitCode)
}

exit 0

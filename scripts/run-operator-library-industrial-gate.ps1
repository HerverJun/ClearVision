param(
    [ValidateSet("quick", "industrial")]
    [string]$Profile = "industrial",

    [ValidateSet(
        "operator-library-smoke",
        "measurement-regression",
        "measurement-accuracy",
        "measurement-stability",
        "measurement-performance",
        "calibration",
        "detection-regression",
        "detection-performance",
        "plc"
    )]
    [string[]]$Gate,

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [ValidateSet("auto", "standard", "acceptance")]
    [string]$PerfGateProfile = "auto",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [switch]$DryRun,

    [switch]$FailFast
)

$ErrorActionPreference = "Stop"

function Quote-Argument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -match '[\s"`|]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }

    return $Value
}

function ConvertTo-ArgumentList {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Arguments
    )

    $argumentList = @()
    foreach ($key in $Arguments.Keys) {
        $value = $Arguments[$key]
        if ($value -is [switch] -or $value -is [bool]) {
            if ($value) {
                $argumentList += "-$key"
            }
            continue
        }

        if ($null -eq $value) {
            continue
        }

        if ($value -is [System.Array]) {
            foreach ($item in $value) {
                if ($null -ne $item -and -not [string]::IsNullOrWhiteSpace([string]$item)) {
                    $argumentList += "-$key"
                    $argumentList += [string]$item
                }
            }
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$value)) {
            $argumentList += "-$key"
            $argumentList += [string]$value
        }
    }

    return $argumentList
}

function Format-CommandPreview {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Arguments
    )

    $relativePath = Resolve-Path -LiteralPath $ScriptPath -Relative
    $parts = @("&", (Quote-Argument $relativePath))
    $parts += ConvertTo-ArgumentList -Arguments $Arguments | ForEach-Object { Quote-Argument $_ }
    return $parts -join " "
}

function Merge-Arguments {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Base,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Extra
    )

    $merged = [ordered]@{}
    foreach ($key in $Base.Keys) {
        $merged[$key] = $Base[$key]
    }

    foreach ($key in $Extra.Keys) {
        $merged[$key] = $Extra[$key]
    }

    return $merged
}

function New-TestGateArguments {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Base,

        [Parameter(Mandatory = $true)]
        [string]$GateName,

        [Parameter(Mandatory = $true)]
        [string]$TrxDirectory,

        [Parameter(Mandatory = $true)]
        [string]$Timestamp,

        [int]$MinimumTotalTests = 1,

        [System.Collections.IDictionary]$Extra
    )

    $validationArguments = [ordered]@{
        ResultsDirectory = $TrxDirectory
        LogFileName = "$GateName-$Timestamp.trx"
        MinimumTotalTests = $MinimumTotalTests
    }

    $merged = Merge-Arguments -Base $Base -Extra $validationArguments
    if ($null -ne $Extra) {
        $merged = Merge-Arguments -Base $merged -Extra $Extra
    }

    return $merged
}

function New-GateStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Arguments
    )

    return [PSCustomObject]@{
        Name = $Name
        ScriptPath = $ScriptPath
        Arguments = $Arguments
        Command = Format-CommandPreview -ScriptPath $ScriptPath -Arguments $Arguments
    }
}

function Invoke-GateStep {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Step,

        [Parameter(Mandatory = $true)]
        [string]$LogDirectory
    )

    $logPath = Join-Path $LogDirectory "$($Step.Name).log"
    $startedAt = Get-Date

    Write-Host ""
    Write-Host "[operator-industrial-gate] START $($Step.Name)"
    Write-Host "[operator-industrial-gate] Log: $logPath"
    Write-Host "[operator-industrial-gate] $($Step.Command)"

    $exitCode = 0
    $status = "passed"

    try {
        $global:LASTEXITCODE = 0
        $arguments = $Step.Arguments
        & $Step.ScriptPath @arguments *>&1 | Tee-Object -FilePath $logPath | Out-Host
        $exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        if ($exitCode -ne 0) {
            $status = "failed"
        }
    }
    catch {
        $status = "failed"
        $exitCode = 1
        $_ | Out-String | Tee-Object -FilePath $logPath -Append | Out-Host
    }

    $finishedAt = Get-Date
    $duration = $finishedAt - $startedAt
    Write-Host "[operator-industrial-gate] END $($Step.Name): $status (exit $exitCode, $([Math]::Round($duration.TotalSeconds, 1))s)"

    return [PSCustomObject]@{
        name = $Step.Name
        status = $status
        exitCode = $exitCode
        startedAt = $startedAt.ToString("o")
        finishedAt = $finishedAt.ToString("o")
        durationSeconds = [Math]::Round($duration.TotalSeconds, 3)
        log = $logPath
        command = $Step.Command
    }
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path $repoRoot "test_results\operator-library-industrial-gate\$timestamp"
$logDirectory = Join-Path $runRoot "logs"
$trxDirectory = Join-Path $runRoot "trx"
$performanceReportDirectory = Join-Path $runRoot "performance-reports"

if (-not $DryRun) {
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($trxDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($performanceReportDirectory) | Out-Null
}

$repoDotnetHome = Join-Path $repoRoot ".dotnet-home"
$repoNuGetPackages = Join-Path $repoRoot ".dotnet\.nuget\packages"

if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME) -and (Test-Path $repoDotnetHome)) {
    $env:DOTNET_CLI_HOME = $repoDotnetHome
}

if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES) -and (Test-Path $repoNuGetPackages)) {
    $env:NUGET_PACKAGES = $repoNuGetPackages
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE)) {
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
}

if ([string]::IsNullOrWhiteSpace($env:DOTNET_NOLOGO)) {
    $env:DOTNET_NOLOGO = "1"
}

$commonArguments = [ordered]@{
    Verbosity = $Verbosity
    ReturnExitCode = $true
}

if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
    $commonArguments.Configuration = $Configuration
}

if ($NoBuild) {
    $commonArguments.NoBuild = $true
}

if ($NoRestore) {
    $commonArguments.NoRestore = $true
}

$smokeArguments = [ordered]@{
    Project = (Join-Path $repoRoot "Acme.OperatorLibrary\tests\Acme.OperatorLibrary.SmokeTests\Acme.OperatorLibrary.SmokeTests.csproj")
    Verbosity = $Verbosity
    ResultsDirectory = $trxDirectory
    LogFileName = "operator-library-smoke-$timestamp.trx"
    MinimumTotalTests = 40
    ReturnExitCode = $true
}

if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
    $smokeArguments.Configuration = $Configuration
}

if ($NoBuild) {
    $smokeArguments.NoBuild = $true
}

if ($NoRestore) {
    $smokeArguments.NoRestore = $true
}

$stepMap = [ordered]@{
    "operator-library-smoke" = New-GateStep -Name "operator-library-smoke" -ScriptPath $runner -Arguments $smokeArguments
    "measurement-regression" = New-GateStep -Name "measurement-regression" -ScriptPath (Join-Path $scriptRoot "run-tests-measurement-regression.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "measurement-regression" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 19)
    "measurement-accuracy" = New-GateStep -Name "measurement-accuracy" -ScriptPath (Join-Path $scriptRoot "run-tests-measurement-accuracy.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "measurement-accuracy" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 17)
    "measurement-stability" = New-GateStep -Name "measurement-stability" -ScriptPath (Join-Path $scriptRoot "run-tests-measurement-stability.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "measurement-stability" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 1)
    "measurement-performance" = New-GateStep -Name "measurement-performance" -ScriptPath (Join-Path $scriptRoot "run-tests-measurement-performance.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "measurement-performance" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 1 -Extra ([ordered]@{ GateProfile = $PerfGateProfile; ReportDirectory = $performanceReportDirectory }))
    "calibration" = New-GateStep -Name "calibration" -ScriptPath (Join-Path $scriptRoot "run-tests-calibration-regression.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "calibration" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 11 -Extra ([ordered]@{ Gate = "regression" }))
    "detection-regression" = New-GateStep -Name "detection-regression" -ScriptPath (Join-Path $scriptRoot "run-tests-detection-regression.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "detection-regression" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 16 -Extra ([ordered]@{ Gate = "regression" }))
    "detection-performance" = New-GateStep -Name "detection-performance" -ScriptPath (Join-Path $scriptRoot "run-tests-detection-performance.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "detection-performance" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 1 -Extra ([ordered]@{ GateProfile = $PerfGateProfile; ReportDirectory = $performanceReportDirectory }))
    "plc" = New-GateStep -Name "plc" -ScriptPath (Join-Path $scriptRoot "run-tests-plc-regression.ps1") -Arguments (New-TestGateArguments -Base $commonArguments -GateName "plc" -TrxDirectory $trxDirectory -Timestamp $timestamp -MinimumTotalTests 1)
}

$profileSteps = @{
    quick = @(
        "operator-library-smoke",
        "measurement-regression",
        "calibration",
        "detection-regression",
        "plc"
    )
    industrial = @(
        "operator-library-smoke",
        "measurement-regression",
        "measurement-accuracy",
        "measurement-stability",
        "measurement-performance",
        "calibration",
        "detection-regression",
        "detection-performance",
        "plc"
    )
}

$selectedGateNames = if ($Gate.Count -gt 0) {
    $Gate
}
else {
    $profileSteps[$Profile]
}

$selectedSteps = $selectedGateNames | ForEach-Object { $stepMap[$_] }

Write-Host "[operator-industrial-gate] Profile=$Profile"
Write-Host "[operator-industrial-gate] Selected gates: $($selectedGateNames -join ', ')"
Write-Host "[operator-industrial-gate] Run root: $runRoot"
Write-Host "[operator-industrial-gate] Logs: $logDirectory"
Write-Host "[operator-industrial-gate] TRX: $trxDirectory"
Write-Host "[operator-industrial-gate] Performance reports: $performanceReportDirectory"

if ($DryRun) {
    Write-Host "[operator-industrial-gate] Dry run only; no gate commands will be executed."
    foreach ($step in $selectedSteps) {
        Write-Host "[operator-industrial-gate] DRY $($step.Name): $($step.Command)"
    }
    exit 0
}

$results = @()
foreach ($step in $selectedSteps) {
    $result = Invoke-GateStep -Step $step -LogDirectory $logDirectory
    $results += $result

    if ($FailFast -and $result.exitCode -ne 0) {
        Write-Host "[operator-industrial-gate] FailFast enabled; stopping after $($result.name)."
        break
    }
}

$failedResults = $results | Where-Object { $_.exitCode -ne 0 }
$performanceReports = @()
if (Test-Path -LiteralPath $performanceReportDirectory) {
    $performanceReports = @(Get-ChildItem -LiteralPath $performanceReportDirectory -File -Recurse |
        Sort-Object FullName |
        ForEach-Object { $_.FullName })
}

$trxFiles = @()
if (Test-Path -LiteralPath $trxDirectory) {
    $trxFiles = @(Get-ChildItem -LiteralPath $trxDirectory -Filter "*.trx" -File |
        Sort-Object FullName |
        ForEach-Object { $_.FullName })
}

$summary = [PSCustomObject]@{
    profile = $Profile
    selectedGates = $selectedGateNames
    startedAt = if ($results.Count -gt 0) { $results[0].startedAt } else { (Get-Date).ToString("o") }
    finishedAt = (Get-Date).ToString("o")
    runRoot = $runRoot
    logDirectory = $logDirectory
    trxDirectory = $trxDirectory
    performanceReportDirectory = $performanceReportDirectory
    failFast = [bool]$FailFast
    noBuild = [bool]$NoBuild
    noRestore = [bool]$NoRestore
    buildsCurrentWorktree = -not [bool]$NoBuild
    results = $results
    trxFiles = $trxFiles
    performanceReports = $performanceReports
    failed = @($failedResults | ForEach-Object { $_.name })
}

$summaryJsonPath = Join-Path $runRoot "summary.json"
$summaryMarkdownPath = Join-Path $runRoot "summary.md"
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $summaryJsonPath -Encoding UTF8

$markdown = @(
    "# Operator Library Industrial Gate Summary",
    "",
    "- Profile: $Profile",
    "- Run root: ``$runRoot``",
    "- Logs: ``$logDirectory``",
    "- TRX: ``$trxDirectory``",
    "- Performance reports: ``$performanceReportDirectory``",
    "- NoBuild: $([bool]$NoBuild)",
    "- NoRestore: $([bool]$NoRestore)",
    "- Builds current worktree: $(-not [bool]$NoBuild)",
    "",
    "| Gate | Status | Exit | Seconds | Log |",
    "| --- | --- | ---: | ---: | --- |"
)

foreach ($result in $results) {
    $markdown += "| $($result.name) | $($result.status) | $($result.exitCode) | $($result.durationSeconds) | ``$($result.log)`` |"
}

if ($trxFiles.Count -gt 0) {
    $markdown += ""
    $markdown += "## TRX Files"
    $markdown += ""
    foreach ($trx in $trxFiles) {
        $markdown += "- ``$trx``"
    }
}

if ($performanceReports.Count -gt 0) {
    $markdown += ""
    $markdown += "## Performance Reports"
    $markdown += ""
    foreach ($report in $performanceReports) {
        $markdown += "- ``$report``"
    }
}

if ($failedResults.Count -gt 0) {
    $markdown += ""
    $markdown += "Failed gates: $((@($failedResults | ForEach-Object { $_.name })) -join ', ')"
}

$markdown | Set-Content -Path $summaryMarkdownPath -Encoding UTF8

Write-Host ""
Write-Host "[operator-industrial-gate] Summary JSON: $summaryJsonPath"
Write-Host "[operator-industrial-gate] Summary MD: $summaryMarkdownPath"

if ($failedResults.Count -gt 0) {
    Write-Host "[operator-industrial-gate] FAILED: $((@($failedResults | ForEach-Object { $_.name })) -join ', ')"
    exit 1
}

Write-Host "[operator-industrial-gate] PASSED"
exit 0

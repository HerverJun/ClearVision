param(
    [Parameter(Mandatory = $true)]
    [string]$Gate,

    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = -1,

    [string[]]$Collect,

    [string[]]$DotNetTestArguments,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$configurationPath = Join-Path $repoRoot "quality\test-gates.json"
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"

if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw "Authoritative test gate configuration is missing: $configurationPath"
}

$configurationModel = Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
$matchingGates = @($configurationModel.gates | Where-Object { $_.name -ceq $Gate })
if ($matchingGates.Count -ne 1) {
    throw "Expected exactly one gate named '$Gate' in $configurationPath; found $($matchingGates.Count)."
}

$gateDefinition = $matchingGates[0]
$projectProperty = $configurationModel.projects.PSObject.Properties[$gateDefinition.project]
if ($null -eq $projectProperty -or [string]::IsNullOrWhiteSpace([string]$projectProperty.Value)) {
    throw "Gate '$Gate' references unknown project '$($gateDefinition.project)'."
}

$projectPath = Join-Path $repoRoot ([string]$projectProperty.Value)
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "Gate '$Gate' project does not exist: $projectPath"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$effectiveResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Join-Path $repoRoot ".tmp\test_results\classified\$Gate"
}
else {
    $ResultsDirectory
}
$effectiveLogFileName = if ([string]::IsNullOrWhiteSpace($LogFileName)) {
    "$Gate-$timestamp.trx"
}
else {
    $LogFileName
}
$configuredMinimum = if ($null -eq $gateDefinition.minimumTotalTests) { 1 } else { [int]$gateDefinition.minimumTotalTests }
$effectiveMinimum = if ($MinimumTotalTests -ge 0) { $MinimumTotalTests } else { [Math]::Max(1, $configuredMinimum) }

Write-Host "[classified-gate] Name=$Gate"
Write-Host "[classified-gate] Project=$($gateDefinition.project)"
Write-Host "[classified-gate] Lane=$($gateDefinition.lane)"
Write-Host "[classified-gate] Filter=$($gateDefinition.filter)"
Write-Host "[classified-gate] Minimum existence check=$effectiveMinimum (not a quality score)"

$parameters = @{
    Project = $projectPath
    Filter = [string]$gateDefinition.filter
    Verbosity = $Verbosity
    ResultsDirectory = $effectiveResultsDirectory
    LogFileName = $effectiveLogFileName
    MinimumTotalTests = $effectiveMinimum
    ReturnExitCode = $true
}

if (-not [string]::IsNullOrWhiteSpace($Configuration)) {
    $parameters.Configuration = $Configuration
}

if ($NoBuild) {
    $parameters.NoBuild = $true
}

if ($NoRestore) {
    $parameters.NoRestore = $true
}

if ($Collect.Count -gt 0) {
    $parameters.Collect = $Collect
}

if ($DotNetTestArguments.Count -gt 0) {
    $parameters.DotNetTestArguments = $DotNetTestArguments
}

$startedAt = Get-Date
& $runner @parameters
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$finishedAt = Get-Date

$resolvedResultsDirectory = if ([IO.Path]::IsPathRooted($effectiveResultsDirectory)) {
    [IO.Path]::GetFullPath($effectiveResultsDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $effectiveResultsDirectory))
}
$trxPath = Join-Path $resolvedResultsDirectory $effectiveLogFileName
$summaryPath = Join-Path $resolvedResultsDirectory ([IO.Path]::GetFileNameWithoutExtension($effectiveLogFileName) + ".gate.json")
$counters = $null

if (Test-Path -LiteralPath $trxPath -PathType Leaf) {
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counterNode = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -ne $counterNode) {
        $counters = [ordered]@{
            total = [int]$counterNode.total
            executed = [int]$counterNode.executed
            passed = [int]$counterNode.passed
            failed = [int]$counterNode.failed
            error = [int]$counterNode.error
            timeout = [int]$counterNode.timeout
            aborted = [int]$counterNode.aborted
        }
    }
}

$summary = [ordered]@{
    schemaVersion = "2026-07-15.classified-gate.v1"
    gate = $Gate
    project = [string]$gateDefinition.project
    lane = [string]$gateDefinition.lane
    filter = [string]$gateDefinition.filter
    minimumExistenceCheck = $effectiveMinimum
    startedAt = $startedAt.ToString("o")
    finishedAt = $finishedAt.ToString("o")
    durationSeconds = [Math]::Round(($finishedAt - $startedAt).TotalSeconds, 3)
    exitCode = $exitCode
    trx = $trxPath
    counters = $counters
}

[IO.Directory]::CreateDirectory($resolvedResultsDirectory) | Out-Null
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
Write-Host "[classified-gate] Summary=$summaryPath"

$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) {
    return
}

exit $exitCode

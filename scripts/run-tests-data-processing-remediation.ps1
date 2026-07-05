param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [ValidateSet("phase1", "phase2")]
    [string]$Gate = "phase2",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName,

    [int]$MinimumTotalTests = -1,

    [switch]$SkipDockerIntegration,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$runner = Join-Path $scriptRoot "run-dotnet-test-serial.ps1"
$project = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$defaultResultsDirectory = Join-Path $repoRoot ".tmp\test_results\data-processing-remediation-$Gate"
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$defaultLogFileName = "data-processing-remediation-$Gate-$timestamp.trx"
$dockerIntegrationTestClass = "DatabaseWriteOperatorMultiDbIntegrationTests"

$phase1TestClasses = @(
    "Sprint2_JsonExtractorTests",
    "Sprint3_MathOperationTests",
    "AggregatorOperatorTests",
    "UnitConvertOperatorTests",
    "BoundingBoxFilterOperatorTests",
    "BoxNmsOperatorTests",
    "PointAlignmentOperatorTests",
    "PointCorrectionOperatorTests",
    "DatabaseWriteOperatorTests",
    $dockerIntegrationTestClass,
    "OperatorContractReconciliationTests",
    "Sprint5_AIWorkflowServiceTests"
)

$phase2TestClasses = @(
    "Sprint2_ArrayIndexerTests"
) + $phase1TestClasses

$selectedTestClasses = switch ($Gate) {
    "phase1" { $phase1TestClasses }
    "phase2" { $phase2TestClasses }
    default { throw "Unsupported gate '$Gate'." }
}

if ($SkipDockerIntegration -and $selectedTestClasses -contains $dockerIntegrationTestClass) {
    $selectedTestClasses = @($selectedTestClasses | Where-Object { $_ -ne $dockerIntegrationTestClass })
}

$defaultMinimumTotalTests = switch ($Gate) {
    "phase1" { 140 }
    "phase2" { 150 }
    default { 0 }
}

$effectiveMinimumTotalTests = if ($MinimumTotalTests -ge 0) { $MinimumTotalTests } else { $defaultMinimumTotalTests }

function Test-DockerAccessible {
    try {
        $dockerInfo = Start-Process -FilePath "docker" -ArgumentList "info --format ""{{.ServerVersion}}""" -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_info.out" -RedirectStandardError "$env:TEMP\cv_docker_info.err"
        return $dockerInfo.ExitCode -eq 0
    }
    catch {
        return $false
    }
}

function Test-DockerImageAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Image
    )

    $inspect = Start-Process -FilePath "docker" -ArgumentList "image inspect $Image" -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_image.out" -RedirectStandardError "$env:TEMP\cv_docker_image.err"
    return $inspect.ExitCode -eq 0
}

function Ensure-DockerImageAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Image
    )

    if (Test-DockerImageAvailable -Image $Image) {
        return
    }

    Write-Host "[data-processing-remediation] Pulling missing image: $Image"
    $pull = Start-Process -FilePath "docker" -ArgumentList "pull $Image" -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$env:TEMP\cv_docker_pull.out" -RedirectStandardError "$env:TEMP\cv_docker_pull.err"
    if ($pull.ExitCode -ne 0) {
        throw "Docker image '$Image' is not available locally and pull failed. Ensure registry access before running gate '$Gate'."
    }
}

if ($selectedTestClasses -contains $dockerIntegrationTestClass) {
    if (-not (Test-DockerAccessible)) {
        throw "Docker access is required for $dockerIntegrationTestClass. Start Docker Desktop (or expose a reachable daemon) before running gate '$Gate', or pass -SkipDockerIntegration to run the non-Docker remediation subset."
    }

    Ensure-DockerImageAvailable -Image "mcr.microsoft.com/mssql/server:2022-latest"
    Ensure-DockerImageAvailable -Image "mariadb:11.4"
}

Write-Host "[data-processing-remediation] Gate=$Gate"
Write-Host "[data-processing-remediation] SkipDockerIntegration=$SkipDockerIntegration"
Write-Host "[data-processing-remediation] Selected test classes: $($selectedTestClasses -join ', ')"

$parameters = @{
    Project = $project
    FullyQualifiedName = $selectedTestClasses
    Verbosity = $Verbosity
    ResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) { $defaultResultsDirectory } else { $ResultsDirectory }
    LogFileName = if ([string]::IsNullOrWhiteSpace($LogFileName)) { $defaultLogFileName } else { $LogFileName }
}

if ($effectiveMinimumTotalTests -gt 0) {
    $parameters.MinimumTotalTests = $effectiveMinimumTotalTests
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

$parameters.ReturnExitCode = $true

& $runner @parameters
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$global:LASTEXITCODE = $exitCode

if ($ReturnExitCode) {
    return
}

exit $exitCode

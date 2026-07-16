param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "standard",

    [ValidateSet("baseline", "after")]
    [string]$Label = "after",

    [string]$ResultsDirectory = ".tmp/operator-product-e2e",

    [string]$ProductRoot = "",

    [string]$RunnerProject = "",

    [string]$HarnessSourceDirectory = "",

    [switch]$IncludeCandidates,

    [switch]$AdapterInjected,

    [switch]$AllowDirty,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$productRepoRoot = if ([string]::IsNullOrWhiteSpace($ProductRoot)) { $repoRoot } else { [IO.Path]::GetFullPath($ProductRoot) }
$runner = if ([string]::IsNullOrWhiteSpace($RunnerProject)) {
    Join-Path $repoRoot "quality\tools\OperatorProductE2EBenchmarkRunner\OperatorProductE2EBenchmarkRunner.csproj"
} else {
    [IO.Path]::GetFullPath($RunnerProject)
}
$harnessSource = if ([string]::IsNullOrWhiteSpace($HarnessSourceDirectory)) {
    Join-Path $repoRoot "quality\tools\OperatorProductE2EBenchmarkRunner"
} else {
    [IO.Path]::GetFullPath($HarnessSourceDirectory)
}
$manifest = Join-Path $repoRoot "quality\datasets\operator-product-e2e-v1\manifest.json"
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory))
}
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null

if ($Label -eq "baseline" -and $IncludeCandidates) {
    throw "The frozen baseline product does not contain Welsch opt-in modes; -IncludeCandidates is valid only for -Label after."
}

$settings = switch ($Profile) {
    "smoke" { @{ Warmup = 1; Iterations = 2 } }
    "standard" { @{ Warmup = 2; Iterations = 10 } }
    "acceptance" { @{ Warmup = 5; Iterations = 40 } }
}

$productSourceSha = (& git -C $productRepoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($productSourceSha)) {
    throw "Unable to resolve product source SHA from '$productRepoRoot'."
}
$productDirtyPaths = @(& git -C $productRepoRoot status --porcelain=v1 -- ClearVision.Product/src)
$productRepositoryDirty = $productDirtyPaths.Count -gt 0
if ($productRepositoryDirty -and -not $AllowDirty) {
    throw "Product source tree must be clean for evidence generation: $($productDirtyPaths -join ', ')."
}

$harnessCommitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
$allowedResultPrefix = $resultsRoot.TrimEnd([char[]]@('\', '/')) + [IO.Path]::DirectorySeparatorChar
$harnessDirtyPaths = @(& git -C $repoRoot status --porcelain=v1) | ForEach-Object {
    if ($_.Length -gt 3) { $_.Substring(3).Trim('"') } else { $_ }
} | Where-Object {
    if ([string]::IsNullOrWhiteSpace($_)) { return $false }
    $candidate = [IO.Path]::GetFullPath((Join-Path $repoRoot $_))
    return -not $candidate.StartsWith($allowedResultPrefix, [StringComparison]::OrdinalIgnoreCase)
}
$harnessRepositoryDirty = $harnessDirtyPaths.Count -gt 0
if ($harnessRepositoryDirty -and -not $AllowDirty) {
    throw "Benchmark harness tree must be clean for evidence generation: $($harnessDirtyPaths -join ', ')."
}

$outputPath = Join-Path $resultsRoot "operator-product-e2e-$Label-$Profile.json"
$reportPath = Join-Path $resultsRoot "operator-product-e2e-$Label-$Profile.md"
$sdkVersion = (& dotnet --version).Trim()

& dotnet run --project $runner --configuration Release -- `
    --manifest $manifest `
    --output $outputPath `
    --report $reportPath `
    --label $Label `
    --product-root $productRepoRoot `
    --product-source-sha $productSourceSha `
    --product-repository-dirty $productRepositoryDirty `
    --harness-commit-sha $harnessCommitSha `
    --harness-repository-dirty $harnessRepositoryDirty `
    --harness-source-directory $harnessSource `
    --adapter-injected $([bool]$AdapterInjected) `
    --sdk-version $sdkVersion `
    --warmup $settings.Warmup `
    --iterations $settings.Iterations `
    --include-candidates $([bool]$IncludeCandidates)
$exitCode = $LASTEXITCODE

if ($ReturnExitCode) {
    exit $exitCode
}
if ($exitCode -ne 0) {
    throw "Product operator E2E benchmark failed with exit code $exitCode."
}

Write-Host "Product operator E2E benchmark complete: $outputPath"

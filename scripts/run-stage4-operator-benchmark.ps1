param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "standard",

    [string]$ResultsDirectory = ".tmp/stage4-performance",

    [string]$Label = "after",

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "quality\tools\Stage4OperatorBenchmarkRunner\Stage4OperatorBenchmarkRunner.csproj"
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory))
}
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null

$settings = switch ($Profile) {
    "smoke" { @{ Warmup = 1; Iterations = 3 } }
    "standard" { @{ Warmup = 5; Iterations = 50 } }
    "acceptance" { @{ Warmup = 10; Iterations = 100 } }
}
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
$outputPath = Join-Path $resultsRoot "stage4-operator-$Profile.json"
$reportPath = Join-Path $resultsRoot "stage4-operator-$Profile.md"

& dotnet run --project $project --configuration Release -- `
    --label $Label `
    --source-sha $sourceSha `
    --warmup $settings.Warmup `
    --iterations $settings.Iterations `
    --output $outputPath `
    --report $reportPath
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0 -and $Profile -eq "acceptance") {
    $result = Get-Content -LiteralPath $outputPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $budgets = @{
        "translation_rotation_none_40_points" = @{ P95 = 2.0; Allocation = 160000 }
        "euclidean_cluster_6000_materialized" = @{ P95 = 40.0; Allocation = 20000000 }
        "euclidean_cluster_6000_indices_only" = @{ P95 = 25.0; Allocation = 17000000 }
    }
    foreach ($case in $result.cases) {
        $budget = $budgets[$case.caseId]
        if ($null -eq $budget) {
            throw "Missing acceptance budget for Stage 4 benchmark case '$($case.caseId)'."
        }
        if ([double]$case.p95Milliseconds -gt [double]$budget.P95) {
            throw "Stage 4 acceptance P95 budget exceeded for $($case.caseId): $($case.p95Milliseconds) > $($budget.P95) ms."
        }
        if ([long]$case.allocatedBytesPerIteration -gt [long]$budget.Allocation) {
            throw "Stage 4 acceptance allocation budget exceeded for $($case.caseId): $($case.allocatedBytesPerIteration) > $($budget.Allocation) bytes."
        }
        if ($case.caseId -like "euclidean_cluster_*" -and [int]$case.coreInvocationCount -ne 1) {
            throw "Stage 4 cluster core invocation budget exceeded for $($case.caseId): $($case.coreInvocationCount) != 1."
        }
    }
}

Write-Host "[stage4-benchmark] Profile=$Profile JSON=$outputPath Markdown=$reportPath Exit=$exitCode"
$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode

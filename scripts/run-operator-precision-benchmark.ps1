param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "standard",

    [ValidateSet("baseline", "after")]
    [string]$Label = "baseline",

    [string]$ResultsDirectory = ".tmp/operator-precision",

    [string]$SourceShaOverride = "",

    [switch]$AllowDirty,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "quality\tools\OperatorPrecisionBenchmarkRunner\OperatorPrecisionBenchmarkRunner.csproj"
$manifest = Join-Path $repoRoot "quality\datasets\operator-precision-v1\manifest.json"
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) {
    [IO.Path]::GetFullPath($ResultsDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory))
}
[IO.Directory]::CreateDirectory($resultsRoot) | Out-Null

$settings = switch ($Profile) {
    "smoke" { @{ Warmup = 1; Iterations = 3 } }
    "standard" { @{ Warmup = 3; Iterations = 15 } }
    "acceptance" { @{ Warmup = 5; Iterations = 40 } }
}

$outputPath = Join-Path $resultsRoot "operator-precision-$Label-$Profile.json"
$reportPath = Join-Path $resultsRoot "operator-precision-$Label-$Profile.md"
$allowedEvidencePaths = @(
    (Join-Path $resultsRoot "operator-precision-baseline-$Profile.json"),
    (Join-Path $resultsRoot "operator-precision-baseline-$Profile.md"),
    (Join-Path $resultsRoot "operator-precision-after-$Profile.json"),
    (Join-Path $resultsRoot "operator-precision-after-$Profile.md"),
    (Join-Path $resultsRoot "operator-precision-phase5-comparison.json"),
    (Join-Path $resultsRoot "operator-precision-phase5-comparison.md")
) | ForEach-Object { [IO.Path]::GetFullPath($_) }

$harnessCommitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
$sourceSha = if ([string]::IsNullOrWhiteSpace($SourceShaOverride)) { $harnessCommitSha } else { $SourceShaOverride.Trim() }
$changedPaths = @(& git -C $repoRoot diff --name-only) +
    @(& git -C $repoRoot diff --cached --name-only) +
    @(& git -C $repoRoot ls-files --others --exclude-standard)
$changedPaths = $changedPaths |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $repoRoot $_)) } |
    Sort-Object -Unique
$unexpectedDirtyPaths = @($changedPaths | Where-Object { $allowedEvidencePaths -notcontains $_ })
$repositoryDirty = $unexpectedDirtyPaths.Count -gt 0
if ($repositoryDirty -and -not $AllowDirty) {
    $relativeDirtyPaths = $unexpectedDirtyPaths | ForEach-Object {
        if ($_.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $_.Substring($repoRoot.Length).TrimStart([char[]]@('\', '/'))
        } else {
            $_
        }
    }
    throw "Operator precision evidence requires a clean source tree; unexpected changes: $($relativeDirtyPaths -join ', '). Use -AllowDirty only for local exploratory runs."
}
$sdkVersion = (& dotnet --version).Trim()

& dotnet run --project $project --configuration Release -- `
    --manifest $manifest `
    --label $Label `
    --source-sha $sourceSha `
    --harness-commit-sha $harnessCommitSha `
    --sdk-version $sdkVersion `
    --repository-dirty $repositoryDirty `
    --warmup $settings.Warmup `
    --iterations $settings.Iterations `
    --output $outputPath `
    --report $reportPath
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0 -and $Profile -eq "acceptance") {
    $result = Get-Content -LiteralPath $outputPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $metricBudgets = @{
        "Caliper/GaussianDerivative" = @{ P95 = 0.10; Allocation = 10000 }
        "Circle/OrthogonalWelsch" = @{ P95 = 1.00; Allocation = 500000 }
        "Line/Welsch" = @{ P95 = 0.50; Allocation = 300000 }
        "Anomaly/OnnxManifestPreprocess" = @{ P95 = 0.20; Allocation = 20000 }
        "AnomalyPreprocess/ManifestDeclaredRgbFloat01" = @{ P95 = 0.20; Allocation = 20000 }
    }
    if ($Label -eq "after") {
        $metricBudgets["Caliper/IntegratedGaussianDerivative"] = @{ P95 = 0.50; Allocation = 20000 }
        $metricBudgets["Circle/ProductionOrthogonalWelsch"] = @{ P95 = 1.00; Allocation = 500000 }
        $metricBudgets["Line/ProductionWelsch"] = @{ P95 = 0.50; Allocation = 300000 }
    }

    foreach ($entry in $metricBudgets.GetEnumerator()) {
        $parts = $entry.Key.Split('/')
        $expectedSplit = if ($parts[0] -eq "AnomalyPreprocess") { "contract" } else { "test" }
        $metric = $result.metrics | Where-Object { $_.domain -eq $parts[0] -and $_.algorithm -eq $parts[1] -and $_.split -eq $expectedSplit }
        if ($null -eq $metric) {
            throw "Missing acceptance metric '$($entry.Key)'."
        }
        if ([double]$metric.failureRate -ne 0.0) {
            throw "Acceptance failure rate must remain zero for $($entry.Key); actual=$($metric.failureRate)."
        }
        if ([double]$metric.latencyP95Milliseconds -gt [double]$entry.Value.P95) {
            throw "Acceptance P95 latency exceeded for $($entry.Key): $($metric.latencyP95Milliseconds) > $($entry.Value.P95) ms."
        }
        if ([long]$metric.allocatedBytesPerCase -gt [long]$entry.Value.Allocation) {
            throw "Acceptance allocation exceeded for $($entry.Key): $($metric.allocatedBytesPerCase) > $($entry.Value.Allocation) bytes."
        }
    }

    $expectedDecisions = @{
        "Caliper" = "GaussianDerivative"
        "Circle" = "OrthogonalWelsch"
        "Line" = "Welsch"
        "MeasurementUncertainty" = "ResidualHeuristic"
        "Anomaly" = "TraditionalLabGradient"
    }
    foreach ($entry in $expectedDecisions.GetEnumerator()) {
        $decision = $result.decisions | Where-Object { $_.domain -eq $entry.Key }
        if ($null -eq $decision -or $decision.winner -ne $entry.Value) {
            throw "Acceptance decision drift for $($entry.Key): expected '$($entry.Value)', actual '$($decision.winner)'."
        }
    }

    $preprocessMetric = $result.metrics | Where-Object { $_.domain -eq "AnomalyPreprocess" -and $_.algorithm -eq "ManifestDeclaredRgbFloat01" }
    if ([double]$preprocessMetric.rmse -gt 0.000001) {
        throw "ONNX manifest preprocessing reference RMSE exceeded: $($preprocessMetric.rmse)."
    }

    if ($Label -eq "after") {
        $productionPairs = @(
            @{ Domain = "Circle"; Candidate = "OrthogonalWelsch"; Production = "ProductionOrthogonalWelsch"; Mode = "Exact" },
            @{ Domain = "Line"; Candidate = "Welsch"; Production = "ProductionWelsch"; Mode = "Exact" }
        )
        foreach ($pair in $productionPairs) {
            $candidate = $result.metrics | Where-Object { $_.domain -eq $pair.Domain -and $_.algorithm -eq $pair.Candidate -and $_.split -eq "test" }
            $production = $result.metrics | Where-Object { $_.domain -eq $pair.Domain -and $_.algorithm -eq $pair.Production -and $_.split -eq "test" }
            if ($null -eq $candidate -or $null -eq $production) {
                throw "Missing production conformance pair for $($pair.Domain)."
            }
            if ($pair.Mode -eq "Exact") {
                foreach ($field in @("bias", "rmse", "p95Error", "failureRate", "ambiguityRate", "outlierRate")) {
                    if ([Math]::Abs([double]$candidate.$field - [double]$production.$field) -gt 0.000000001) {
                        throw "Production conformance drift for $($pair.Domain)/$($field): candidate=$($candidate.$field), production=$($production.$field)."
                    }
                }
            } elseif ([double]$production.rmse -gt [double]$candidate.rmse -or
                      [double]$production.p95Error -gt [double]$candidate.p95Error -or
                      [double]$production.failureRate -gt [double]$candidate.failureRate -or
                      [double]$production.ambiguityRate -gt [double]$candidate.ambiguityRate) {
                throw "Production conformance regressed for $($pair.Domain)."
            }
        }

        $caliperBaseline = $result.metrics | Where-Object { $_.domain -eq "Caliper" -and $_.algorithm -eq "LegacyGradientCentroid" -and $_.split -eq "test" }
        $caliperIntegrated = $result.metrics | Where-Object { $_.domain -eq "Caliper" -and $_.algorithm -eq "IntegratedGaussianDerivative" -and $_.split -eq "test" }
        if ($null -eq $caliperBaseline -or $null -eq $caliperIntegrated) {
            throw "Missing Caliper integration evidence."
        }
        if ([double]$caliperIntegrated.rmse -lt [double]$caliperBaseline.rmse -and
            [double]$caliperIntegrated.p95Error -lt [double]$caliperBaseline.p95Error -and
            [double]$caliperIntegrated.failureRate -le [double]$caliperBaseline.failureRate) {
            throw "Caliper integrated candidate unexpectedly became eligible; adoption decision and formal operator exposure require an explicit evidence review."
        }
    }


    $identityMetric = $result.metrics | Where-Object { $_.domain -eq "AnomalyPreprocess" -and $_.algorithm -eq "ManifestDeclaredRgbFloat01" -and $_.split -eq "contract" }
    if ([double]$identityMetric.extra.mismatchRejectedFailClosed -ne 1.0) {
        throw "ONNX feature-bank mismatch was not exercised fail-closed."
    }
}

Write-Host "[operator-precision] Profile=$Profile Label=$Label JSON=$outputPath Markdown=$reportPath Exit=$exitCode"
$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode

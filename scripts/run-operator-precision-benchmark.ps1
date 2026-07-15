param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "standard",

    [ValidateSet("baseline", "after")]
    [string]$Label = "baseline",

    [string]$ResultsDirectory = ".tmp/operator-precision",

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

$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim()
$outputPath = Join-Path $resultsRoot "operator-precision-$Label-$Profile.json"
$reportPath = Join-Path $resultsRoot "operator-precision-$Label-$Profile.md"

& dotnet run --project $project --configuration Release -- `
    --manifest $manifest `
    --label $Label `
    --source-sha $sourceSha `
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

    foreach ($entry in $metricBudgets.GetEnumerator()) {
        $parts = $entry.Key.Split('/')
        $metric = $result.metrics | Where-Object { $_.domain -eq $parts[0] -and $_.algorithm -eq $parts[1] }
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
}

Write-Host "[operator-precision] Profile=$Profile Label=$Label JSON=$outputPath Markdown=$reportPath Exit=$exitCode"
$global:LASTEXITCODE = $exitCode
if ($ReturnExitCode) { return }
exit $exitCode

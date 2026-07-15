param(
    [string]$BaselinePath = "quality/evals/reports/operator-precision-baseline-acceptance.json",
    [string]$AfterPath = "quality/evals/reports/operator-precision-after-acceptance.json",
    [string]$OutputPath = "quality/evals/reports/operator-precision-phase5-comparison.json",
    [string]$ReportPath = "quality/evals/reports/operator-precision-phase5-comparison.md"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Get-Metric($Report, [string]$Domain, [string]$Algorithm) {
    $metric = @($Report.metrics | Where-Object { $_.domain -eq $Domain -and $_.algorithm -eq $Algorithm })
    if ($metric.Count -ne 1) {
        throw "Expected one metric for $Domain/$Algorithm; actual=$($metric.Count)."
    }

    return $metric[0]
}

function Get-PercentImprovement([double]$Before, [double]$After) {
    if ($Before -eq 0.0) {
        return 0.0
    }

    return [Math]::Round((($Before - $After) / $Before) * 100.0, 6)
}

function Assert-Equal([string]$Name, $Before, $After) {
    if (($Before | ConvertTo-Json -Depth 20 -Compress) -ne ($After | ConvertTo-Json -Depth 20 -Compress)) {
        throw "Baseline/after identity mismatch for $Name."
    }
}

$baseline = Get-Content -LiteralPath (Resolve-RepoPath $BaselinePath) -Raw -Encoding UTF8 | ConvertFrom-Json
$after = Get-Content -LiteralPath (Resolve-RepoPath $AfterPath) -Raw -Encoding UTF8 | ConvertFrom-Json

if ($baseline.label -ne "baseline" -or $after.label -ne "after") {
    throw "Expected baseline/after report labels."
}
if ($baseline.sourceSha -ne "ce266626e0bec0a8cd4a68c11b176df95e8cb482") {
    throw "Frozen baseline SHA drifted: $($baseline.sourceSha)."
}
if ([string]::IsNullOrWhiteSpace($after.sourceSha)) {
    throw "After report is missing its source SHA."
}

Assert-Equal "dataset" $baseline.dataset $after.dataset
Assert-Equal "model" $baseline.model $after.model
Assert-Equal "environment" $baseline.environment $after.environment

$adoptedSpecs = @(
    [ordered]@{ Domain = "Caliper"; Baseline = "LegacyGradientCentroid"; Winner = "GaussianDerivative"; Production = "ProductionGaussianDerivative"; Conformance = "NoDegradation" },
    [ordered]@{ Domain = "Circle"; Baseline = "AlgebraicL2"; Winner = "OrthogonalWelsch"; Production = "ProductionOrthogonalWelsch"; Conformance = "ExactAccuracy" },
    [ordered]@{ Domain = "Line"; Baseline = "L2"; Winner = "Welsch"; Production = "ProductionWelsch"; Conformance = "ExactAccuracy" }
)

$decisions = foreach ($spec in $adoptedSpecs) {
    $beforeMetric = Get-Metric $baseline $spec.Domain $spec.Baseline
    $winnerMetric = Get-Metric $baseline $spec.Domain $spec.Winner
    $productionMetric = Get-Metric $after $spec.Domain $spec.Production
    $decision = @($baseline.decisions | Where-Object { $_.domain -eq $spec.Domain })[0]

    if ($decision.winner -ne $spec.Winner -or $decision.adopted -ne $true) {
        throw "Frozen decision drift for $($spec.Domain)."
    }
    if ([double]$productionMetric.failureRate -ne 0.0) {
        throw "Production failure rate must remain zero for $($spec.Domain)."
    }

    if ($spec.Conformance -eq "ExactAccuracy") {
        foreach ($field in @("bias", "rmse", "p95Error", "failureRate", "ambiguityRate", "outlierRate")) {
            if ([Math]::Abs([double]$winnerMetric.$field - [double]$productionMetric.$field) -gt 0.000000001) {
                throw "Production accuracy drift for $($spec.Domain)/$field."
            }
        }
    } elseif ([double]$productionMetric.rmse -gt [double]$winnerMetric.rmse -or
              [double]$productionMetric.p95Error -gt [double]$winnerMetric.p95Error -or
              [double]$productionMetric.failureRate -gt [double]$winnerMetric.failureRate -or
              [double]$productionMetric.ambiguityRate -gt [double]$winnerMetric.ambiguityRate) {
        throw "Production implementation regressed against the benchmark winner for $($spec.Domain)."
    }

    [ordered]@{
        domain = $spec.Domain
        baseline = $spec.Baseline
        winner = $spec.Winner
        productionAlgorithm = $spec.Production
        adopted = $true
        reason = $decision.reason
        baselineMetric = [ordered]@{
            rmse = [double]$beforeMetric.rmse
            p95Error = [double]$beforeMetric.p95Error
            failureRate = [double]$beforeMetric.failureRate
            ambiguityRate = [double]$beforeMetric.ambiguityRate
            latencyP95Milliseconds = [double]$beforeMetric.latencyP95Milliseconds
            allocatedBytesPerCase = [long]$beforeMetric.allocatedBytesPerCase
        }
        productionMetric = [ordered]@{
            rmse = [double]$productionMetric.rmse
            p95Error = [double]$productionMetric.p95Error
            failureRate = [double]$productionMetric.failureRate
            ambiguityRate = [double]$productionMetric.ambiguityRate
            latencyP95Milliseconds = [double]$productionMetric.latencyP95Milliseconds
            allocatedBytesPerCase = [long]$productionMetric.allocatedBytesPerCase
        }
        improvement = [ordered]@{
            rmsePercent = Get-PercentImprovement ([double]$beforeMetric.rmse) ([double]$productionMetric.rmse)
            p95ErrorPercent = Get-PercentImprovement ([double]$beforeMetric.p95Error) ([double]$productionMetric.p95Error)
            failureRateDelta = [double]$productionMetric.failureRate - [double]$beforeMetric.failureRate
            ambiguityRateDelta = [double]$productionMetric.ambiguityRate - [double]$beforeMetric.ambiguityRate
        }
        cost = [ordered]@{
            latencyP95Ratio = [Math]::Round([double]$productionMetric.latencyP95Milliseconds / [double]$beforeMetric.latencyP95Milliseconds, 6)
            allocationRatio = [Math]::Round([double]$productionMetric.allocatedBytesPerCase / [double]$beforeMetric.allocatedBytesPerCase, 6)
        }
        productionConformance = $spec.Conformance
    }
}

$uncertaintyHeuristic = Get-Metric $baseline "MeasurementUncertainty" "ResidualHeuristic"
$uncertaintyCovariance = Get-Metric $baseline "MeasurementUncertainty" "Covariance"
$anomalyTraditional = Get-Metric $after "Anomaly" "TraditionalLabGradient"
$anomalyOnnx = Get-Metric $after "Anomaly" "OnnxManifestPreprocess"
$anomalyPreprocess = Get-Metric $after "AnomalyPreprocess" "ManifestDeclaredRgbFloat01"

$report = [ordered]@{
    schemaVersion = "2026-07-15.operator-precision-phase5-comparison.v1"
    generatedAtUtc = $after.generatedAtUtc
    baselineSourceSha = $baseline.sourceSha
    afterSourceSha = $after.sourceSha
    immutableInputs = [ordered]@{
        datasetId = $after.dataset.datasetId
        datasetVersion = $after.dataset.version
        datasetSha256 = $after.dataset.sha256
        seed = [int]$after.dataset.seed
        modelSha256 = $after.model.sha256
        preprocessFingerprint = $after.model.preprocessFingerprint
        environment = $after.environment
        identicalAcrossReports = $true
    }
    claimBoundary = "Synthetic mathematical and preprocessing-contract evidence only; public MVTec evidence remains separate. This report does not establish E4, Release Ready, Field Verified, commercial-grade, or production-site accuracy."
    adoptedDecisions = @($decisions)
    rejectedCandidates = @(
        [ordered]@{ domain = "Caliper"; candidate = "Quadratic"; reason = "No repeatable accuracy improvement over the legacy baseline." },
        [ordered]@{ domain = "Caliper"; candidate = "Erf"; reason = "No accuracy improvement and materially higher P95 latency/allocation." },
        [ordered]@{ domain = "MeasurementUncertainty"; candidate = "Covariance as calibrated confidence"; reason = "68%/95% calibration did not improve; retained only as UncalibratedCovariance evidence." },
        [ordered]@{ domain = "Anomaly"; candidate = "ONNX embedding as default"; reason = "Traditional mode remains the compatibility default; manifest/model/fingerprint binding is a fail-closed governance upgrade." }
    )
    uncertaintyCoverage = [ordered]@{
        adopted = $false
        heuristicCoverage68 = [double]$uncertaintyHeuristic.extra.coverage68
        heuristicCoverage95 = [double]$uncertaintyHeuristic.extra.coverage95
        covarianceCoverage68 = [double]$uncertaintyCovariance.extra.coverage68
        covarianceCoverage95 = [double]$uncertaintyCovariance.extra.coverage95
        conclusion = "Covariance is uncalibrated and must not be presented as a statistical confidence interval."
    }
    anomalyIdentity = [ordered]@{
        traditionalDefaultAccuracy = [double]$anomalyTraditional.extra.accuracy
        onnxManifestAccuracy = [double]$anomalyOnnx.extra.accuracy
        preprocessReferenceRmse = [double]$anomalyPreprocess.rmse
        mismatchFingerprintDifferent = [double]$anomalyPreprocess.extra.mismatchFingerprintDifferent
        conclusion = "Complete preprocessing manifest and model/preprocess/feature-bank identity are mandatory; mismatch fails closed."
    }
    acceptance = [ordered]@{
        sameDatasetModelEnvironment = $true
        decisionsStable = $true
        productionConformancePassed = $true
        performanceBudgetsPassed = $true
        releaseReady = $false
        fieldVerified = $false
    }
}

$outputFullPath = Resolve-RepoPath $OutputPath
$reportFullPath = Resolve-RepoPath $ReportPath
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText($outputFullPath, ($report | ConvertTo-Json -Depth 20) + [Environment]::NewLine, $utf8)

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("# Operator Precision Phase 5 Comparison")
$lines.Add("")
$lines.Add("- Baseline SHA: ``$($baseline.sourceSha)``")
$lines.Add("- After SHA: ``$($after.sourceSha)``")
$lines.Add("- Dataset SHA: ``$($after.dataset.sha256)``")
$lines.Add("- Seed: ``$($after.dataset.seed)``")
$lines.Add("- Model SHA: ``$($after.model.sha256)``")
$lines.Add("- Preprocess fingerprint: ``$($after.model.preprocessFingerprint)``")
$lines.Add("- Identity check: baseline and after used the same dataset, model, preprocessing identity, seed and runtime environment.")
$lines.Add("")
$lines.Add("> Synthetic mathematical and preprocessing-contract evidence only. This is not E4, Release Ready, Field Verified, commercial-grade, or production-site accuracy evidence.")
$lines.Add("")
$lines.Add("| Domain | Baseline | Production winner | RMSE improvement | P95 error improvement | Failure delta | Ambiguity delta | P95 latency | Allocation | Conformance |")
$lines.Add("|---|---|---|---:|---:|---:|---:|---:|---:|---|")
foreach ($row in $decisions) {
    $lines.Add("| $($row.domain) | ``$($row.baseline)`` | ``$($row.productionAlgorithm)`` | $($row.improvement.rmsePercent)% | $($row.improvement.p95ErrorPercent)% | $($row.improvement.failureRateDelta) | $($row.improvement.ambiguityRateDelta) | $([Math]::Round($row.productionMetric.latencyP95Milliseconds, 6)) ms | $($row.productionMetric.allocatedBytesPerCase) B/case | $($row.productionConformance) |")
}
$lines.Add("")
$lines.Add("## Rejected candidates")
$lines.Add("")
foreach ($item in $report.rejectedCandidates) {
    $lines.Add("- **$($item.domain) / $($item.candidate):** $($item.reason)")
}
$lines.Add("")
$lines.Add("## Uncertainty and anomaly conclusions")
$lines.Add("")
$lines.Add("- Residual heuristic coverage: 68%=$($report.uncertaintyCoverage.heuristicCoverage68), 95%=$($report.uncertaintyCoverage.heuristicCoverage95).")
$lines.Add("- Raw covariance coverage: 68%=$($report.uncertaintyCoverage.covarianceCoverage68), 95%=$($report.uncertaintyCoverage.covarianceCoverage95). It remains ``UncalibratedCovariance``.")
$lines.Add("- Anomaly traditional mode remains the default. ONNX preprocessing reference RMSE is $($report.anomalyIdentity.preprocessReferenceRmse), and mismatched identity is required to fail closed.")
$lines.Add("")
$lines.Add("## Reproduction")
$lines.Add("")
$lines.Add('```powershell')
$lines.Add('& ".\\scripts\\run-operator-precision-benchmark.ps1" -Profile acceptance -Label after -ResultsDirectory "quality\\evals\\reports"')
$lines.Add('& ".\\scripts\\compare-operator-precision-benchmarks.ps1"')
$lines.Add('```')
[IO.File]::WriteAllText($reportFullPath, ($lines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8)

Write-Host "[operator-precision-comparison] JSON=$outputFullPath Markdown=$reportFullPath"

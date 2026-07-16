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

function Get-Metric($Report, [string]$Domain, [string]$Algorithm, [string]$Split = "test") {
    $metric = @($Report.metrics | Where-Object { $_.domain -eq $Domain -and $_.algorithm -eq $Algorithm -and $_.split -eq $Split })
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
if ($baseline.sourceSha -ne $baseline.harness.commitSha -or $after.sourceSha -ne $after.harness.commitSha) {
    throw "Supplemental kernel reports must bind sourceSha to the actually executed harness commit."
}
if ($baseline.sourceSha -ne $after.sourceSha) {
    throw "Supplemental baseline/after reports must execute from the same committed harness source."
}
if ($baseline.harness.repositoryDirty -or $after.harness.repositoryDirty) {
    throw "Supplemental kernel evidence must be generated from a clean harness worktree."
}

Assert-Equal "schemaVersion" $baseline.schemaVersion $after.schemaVersion
Assert-Equal "benchmarkId" $baseline.benchmarkId $after.benchmarkId
Assert-Equal "executionScope" $baseline.executionScope $after.executionScope
Assert-Equal "dataset" $baseline.dataset $after.dataset
Assert-Equal "model" $baseline.model $after.model
Assert-Equal "harness" $baseline.harness $after.harness
Assert-Equal "environment" $baseline.environment $after.environment
Assert-Equal "warmupIterations" $baseline.warmupIterations $after.warmupIterations
Assert-Equal "measurementIterations" $baseline.measurementIterations $after.measurementIterations

$baselineSharedMetrics = @($baseline.metrics | ForEach-Object { "$($_.domain)|$($_.algorithm)|$($_.split)" })
foreach ($key in $baselineSharedMetrics) {
    $parts = $key.Split('|')
    $beforeMetric = Get-Metric $baseline $parts[0] $parts[1] $parts[2]
    $afterMetric = Get-Metric $after $parts[0] $parts[1] $parts[2]
    foreach ($field in @("caseCount", "bias", "rmse", "p95Error", "failureRate", "ambiguityRate", "outlierRate", "extra")) {
        Assert-Equal "sharedMetric/$key/$field" $beforeMetric.$field $afterMetric.$field
    }
}

$adoptedSpecs = @(
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
        adopted = $false
        reason = "Kernel-level candidate selected; formal mode adoption is decided only by operator-product E2E evidence."
        kernelCandidateSupported = $true
        evidenceScope = "kernel"
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

$caliperBaseline = Get-Metric $baseline "Caliper" "LegacyGradientCentroid"
$caliperCandidate = Get-Metric $baseline "Caliper" "GaussianDerivative"
$caliperIntegrated = Get-Metric $after "Caliper" "IntegratedGaussianDerivative"
$caliperDecision = @($baseline.decisions | Where-Object { $_.domain -eq "Caliper" })[0]
$caliperWrittenBudget = [ordered]@{
    latencyP95Milliseconds = 0.50
    allocatedBytesPerCase = 20000
    latencyPassed = [double]$caliperIntegrated.latencyP95Milliseconds -le 0.50
    allocationPassed = [long]$caliperIntegrated.allocatedBytesPerCase -le 20000
}
$caliperWrittenBudget.passed = $caliperWrittenBudget.latencyPassed -and $caliperWrittenBudget.allocationPassed
if ($caliperDecision.winner -ne "GaussianDerivative" -or $caliperDecision.adopted -ne $true) {
    throw "Caliper validation-set candidate selection drifted."
}
if ([double]$caliperIntegrated.rmse -lt [double]$caliperBaseline.rmse -and
    [double]$caliperIntegrated.p95Error -lt [double]$caliperBaseline.p95Error -and
    [double]$caliperIntegrated.failureRate -le [double]$caliperBaseline.failureRate -and
    $caliperWrittenBudget.passed) {
    throw "Caliper integrated candidate now meets the adoption guard; formal exposure requires a new explicit review."
}
$caliperRow = [ordered]@{
    domain = "Caliper"
    baseline = "LegacyGradientCentroid"
    winner = "GaussianDerivative (kernel candidate)"
    productionAlgorithm = "IntegratedGaussianDerivative"
    adopted = $false
    reason = "The validation-selected localizer regressed end-to-end test RMSE/P95 when seeded by the formal detector and pair selection; its allocation also exceeded the written diagnostic budget, so it remains out of the formal operator."
    baselineMetric = [ordered]@{
        rmse = [double]$caliperBaseline.rmse
        p95Error = [double]$caliperBaseline.p95Error
        failureRate = [double]$caliperBaseline.failureRate
        ambiguityRate = [double]$caliperBaseline.ambiguityRate
        latencyP95Milliseconds = [double]$caliperBaseline.latencyP95Milliseconds
        allocatedBytesPerCase = [long]$caliperBaseline.allocatedBytesPerCase
    }
    productionMetric = [ordered]@{
        rmse = [double]$caliperIntegrated.rmse
        p95Error = [double]$caliperIntegrated.p95Error
        failureRate = [double]$caliperIntegrated.failureRate
        ambiguityRate = [double]$caliperIntegrated.ambiguityRate
        latencyP95Milliseconds = [double]$caliperIntegrated.latencyP95Milliseconds
        allocatedBytesPerCase = [long]$caliperIntegrated.allocatedBytesPerCase
    }
    improvement = [ordered]@{
        rmsePercent = Get-PercentImprovement ([double]$caliperBaseline.rmse) ([double]$caliperIntegrated.rmse)
        p95ErrorPercent = Get-PercentImprovement ([double]$caliperBaseline.p95Error) ([double]$caliperIntegrated.p95Error)
        failureRateDelta = [double]$caliperIntegrated.failureRate - [double]$caliperBaseline.failureRate
        ambiguityRateDelta = [double]$caliperIntegrated.ambiguityRate - [double]$caliperBaseline.ambiguityRate
    }
    cost = [ordered]@{
        latencyP95Ratio = [Math]::Round([double]$caliperIntegrated.latencyP95Milliseconds / [double]$caliperBaseline.latencyP95Milliseconds, 6)
        allocationRatio = [Math]::Round([double]$caliperIntegrated.allocatedBytesPerCase / [double]$caliperBaseline.allocatedBytesPerCase, 6)
    }
    writtenBudget = $caliperWrittenBudget
    productionConformance = "RejectedIntegrationRegression"
}

$uncertaintyHeuristic = Get-Metric $baseline "MeasurementUncertainty" "ResidualHeuristic"
$uncertaintyCovariance = Get-Metric $baseline "MeasurementUncertainty" "Covariance"
$anomalyTraditional = Get-Metric $after "Anomaly" "TraditionalLabGradient"
$anomalyOnnx = Get-Metric $after "Anomaly" "OnnxManifestPreprocess"
$anomalyPreprocess = Get-Metric $after "AnomalyPreprocess" "ManifestDeclaredRgbFloat01" "contract"

$report = [ordered]@{
    schemaVersion = "2026-07-16.operator-precision-phase5-comparison.v2"
    generatedAtUtc = $after.generatedAtUtc
    baselineSourceSha = $baseline.sourceSha
    afterSourceSha = $after.sourceSha
    immutableInputs = [ordered]@{
        datasetId = $after.dataset.datasetId
        datasetVersion = $after.dataset.version
        manifestSha256 = $after.dataset.manifestSha256
        generatedDataSha256 = $after.dataset.generatedDataSha256
        seed = [int]$after.dataset.seed
        modelSha256 = $after.model.sha256
        preprocessFingerprint = $after.model.preprocessFingerprint
        environment = $after.environment
        harness = $after.harness
        identicalAcrossReports = $true
    }
    claimBoundary = "Supplemental synthetic mathematical-kernel and preprocessing-contract evidence only; public MVTec and formal product-operator E2E evidence remain separate. This report does not establish historical product execution, complete operator-path improvement, E4, Release Ready, Field Verified, commercial-grade, or production-site accuracy."
    decisions = @($caliperRow) + @($decisions)
    rejectedCandidates = @(
        [ordered]@{ domain = "Caliper"; candidate = "GaussianDerivative formal integration"; reason = $caliperRow.reason },
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
        mismatchRejectedFailClosed = [double]$anomalyPreprocess.extra.mismatchRejectedFailClosed
        conclusion = "Complete preprocessing manifest and model/preprocess/feature-bank identity are mandatory; mismatch fails closed."
    }
    acceptance = [ordered]@{
        sameDatasetModelEnvironment = $true
        decisionsStable = $true
        productionConformancePassed = $false
        formalProductPathEvaluated = $false
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
$lines.Add("- Supplemental harness source SHA (both reports): ``$($baseline.sourceSha)``")
$lines.Add("- Dataset manifest SHA: ``$($after.dataset.manifestSha256)``")
$lines.Add("- Generated input/truth SHA: ``$($after.dataset.generatedDataSha256)``")
$lines.Add("- Seed: ``$($after.dataset.seed)``")
$lines.Add("- Model SHA: ``$($after.model.sha256)``")
$lines.Add("- Preprocess fingerprint: ``$($after.model.preprocessFingerprint)``")
$lines.Add("- Harness SHA: ``$($after.harness.programSha256)`` (commit ``$($after.harness.commitSha)``; dirty=$($after.harness.repositoryDirty))")
$lines.Add("- Identity check: baseline and after used the same generated input/truth, harness, model, preprocessing identity, seed and runtime environment.")
$lines.Add("")
$lines.Add("> Supplemental kernel-level synthetic mathematical and preprocessing-contract evidence only. This is not executable historical-product evidence, complete formal operator-path evidence, E4, end-to-end field accuracy, Release Ready, Field Verified, commercial-grade, or production-site evidence.")
$lines.Add("")
$lines.Add("| Domain | Baseline | Evaluated path | RMSE improvement | P95 error improvement | Failure delta | Ambiguity delta | P95 latency | Allocation | Budget | Adopted | Conformance |")
$lines.Add("|---|---|---|---:|---:|---:|---:|---:|---:|---|---|---|")
foreach ($row in $report.decisions) {
    $budgetStatus = if ($null -ne $row.writtenBudget) { $row.writtenBudget.passed } else { "Passed" }
    $lines.Add("| $($row.domain) | ``$($row.baseline)`` | ``$($row.productionAlgorithm)`` | $($row.improvement.rmsePercent)% | $($row.improvement.p95ErrorPercent)% | $($row.improvement.failureRateDelta) | $($row.improvement.ambiguityRateDelta) | $([Math]::Round($row.productionMetric.latencyP95Milliseconds, 6)) ms | $($row.productionMetric.allocatedBytesPerCase) B/case | $budgetStatus | $($row.adopted) | $($row.productionConformance) |")
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

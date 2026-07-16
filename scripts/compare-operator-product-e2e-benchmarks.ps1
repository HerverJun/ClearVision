param(
    [string]$BaselinePath = ".tmp/operator-product-e2e/operator-product-e2e-baseline-acceptance.json",
    [string]$AfterPath = ".tmp/operator-product-e2e/operator-product-e2e-after-acceptance.json",
    [string]$OutputPath = ".tmp/operator-product-e2e/operator-product-e2e-phase5-comparison.json",
    [string]$ReportPath = ".tmp/operator-product-e2e/operator-product-e2e-phase5-comparison.md"
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
function Resolve-RepoPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}
function Assert-Equal([string]$Name, $Expected, $Actual) {
    $left = $Expected | ConvertTo-Json -Depth 20 -Compress
    $right = $Actual | ConvertTo-Json -Depth 20 -Compress
    if ($left -ne $right) { throw "Identity mismatch for ${Name}: expected=$left actual=$right" }
}
function Metric($Report, [string]$Domain, [string]$Algorithm, [string]$Split) {
    $match = @($Report.metrics | Where-Object { $_.domain -eq $Domain -and $_.algorithm -eq $Algorithm -and $_.split -eq $Split })
    if ($match.Count -ne 1) { throw "Expected exactly one metric for $Domain/$Algorithm/$Split; actual=$($match.Count)." }
    return $match[0]
}
function Improvement([double]$Baseline, [double]$Candidate) {
    if (-not [double]::IsFinite($Baseline) -or -not [double]::IsFinite($Candidate) -or [Math]::Abs($Baseline) -lt 1e-12) { return [double]::NaN }
    return (($Baseline - $Candidate) / $Baseline) * 100.0
}

$baselineFile = Resolve-RepoPath $BaselinePath
$afterFile = Resolve-RepoPath $AfterPath
$outputFile = Resolve-RepoPath $OutputPath
$reportFile = Resolve-RepoPath $ReportPath
$baseline = Get-Content -LiteralPath $baselineFile -Raw -Encoding UTF8 | ConvertFrom-Json
$after = Get-Content -LiteralPath $afterFile -Raw -Encoding UTF8 | ConvertFrom-Json

if ($baseline.label -ne "baseline" -or $after.label -ne "after") { throw "Expected baseline and after labels." }
if ($baseline.productImplementation.repositorySha -ne "ce266626e0bec0a8cd4a68c11b176df95e8cb482") { throw "Frozen executable product baseline SHA drifted." }
if ($baseline.productImplementation.repositoryDirty -or $after.productImplementation.repositoryDirty) { throw "Product implementation evidence must be clean." }
if ($baseline.harness.repositoryDirty -or $after.harness.repositoryDirty) { throw "Harness evidence must be clean." }
if (-not $baseline.harness.adapterInjectedIntoProductWorktree) { throw "Baseline must be built in the isolated detached worktree with the versioned adapter." }
if ($after.harness.adapterInjectedIntoProductWorktree) { throw "After evidence must execute the current product worktree directly." }

Assert-Equal "schemaVersion" $baseline.schemaVersion $after.schemaVersion
Assert-Equal "benchmarkId" $baseline.benchmarkId $after.benchmarkId
Assert-Equal "dataset" $baseline.dataset $after.dataset
Assert-Equal "harness.commitSha" $baseline.harness.commitSha $after.harness.commitSha
Assert-Equal "harness.programSha256" $baseline.harness.programSha256 $after.harness.programSha256
Assert-Equal "harness.projectSha256" $baseline.harness.projectSha256 $after.harness.projectSha256
Assert-Equal "harness.manifestSha256" $baseline.harness.manifestSha256 $after.harness.manifestSha256
Assert-Equal "environment" $baseline.environment $after.environment
Assert-Equal "resourceMeasurement" $baseline.resourceMeasurement $after.resourceMeasurement
Assert-Equal "warmupIterations" $baseline.warmupIterations $after.warmupIterations
Assert-Equal "measurementIterations" $baseline.measurementIterations $after.measurementIterations

$conformance = @()
foreach ($entry in @(@{ Domain="Circle"; Algorithm="LegacyDefault" }, @{ Domain="Line"; Algorithm="L2Default" })) {
    foreach ($split in @("validation", "test")) {
        $old = Metric $baseline $entry.Domain $entry.Algorithm $split
        $current = Metric $after $entry.Domain $entry.Algorithm $split
        foreach ($field in @("caseCount", "bias", "rmse", "p95Error", "failureRate", "ambiguityRate", "outlierRate", "extra", "failureTaxonomy")) {
            Assert-Equal "conformance/$($entry.Domain)/$split/$field" $old.$field $current.$field
        }
        $conformance += [ordered]@{
            domain = $entry.Domain
            split = $split
            oldAlgorithm = $entry.Algorithm
            currentAlgorithm = $entry.Algorithm
            exactAccuracyAndDiagnosticConformance = $true
        }
    }
}

$manifest = Get-Content -LiteralPath (Join-Path $repoRoot "quality\datasets\operator-product-e2e-v1\manifest.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$decisions = @()
foreach ($domain in @("Circle", "Line")) {
    $baselineAlgorithm = if ($domain -eq "Circle") { "LegacyDefault" } else { "L2Default" }
    $candidateAlgorithm = "WelschOptIn"
    $validationBaseline = Metric $after $domain $baselineAlgorithm "validation"
    $validationCandidate = Metric $after $domain $candidateAlgorithm "validation"
    $testBaseline = Metric $after $domain $baselineAlgorithm "test"
    $testCandidate = Metric $after $domain $candidateAlgorithm "test"
    $budget = if ($domain -eq "Circle") { $manifest.performanceBudgets.circle } else { $manifest.performanceBudgets.line }
    $validationRmseImprovement = Improvement ([double]$validationBaseline.rmse) ([double]$validationCandidate.rmse)
    $validationP95Improvement = Improvement ([double]$validationBaseline.p95Error) ([double]$validationCandidate.p95Error)
    $testRmseImprovement = Improvement ([double]$testBaseline.rmse) ([double]$testCandidate.rmse)
    $testP95Improvement = Improvement ([double]$testBaseline.p95Error) ([double]$testCandidate.p95Error)
    $latencyIncrease = [double]$testCandidate.latencyP95Milliseconds - [double]$testBaseline.latencyP95Milliseconds
    $allocationIncrease = [double]$testCandidate.managedAllocatedBytesPerCase - [double]$testBaseline.managedAllocatedBytesPerCase
    $validationSelected = ($validationRmseImprovement -gt 0.0 -and $validationP95Improvement -gt 0.0 -and
        [double]$validationCandidate.failureRate -le [double]$validationBaseline.failureRate -and
        [double]$validationCandidate.ambiguityRate -le [double]$validationBaseline.ambiguityRate)
    $testSafe = ([double]$testCandidate.failureRate -le [double]$testBaseline.failureRate -and
        [double]$testCandidate.ambiguityRate -le [double]$testBaseline.ambiguityRate -and
        $latencyIncrease -le [double]$budget.candidateP95LatencyIncreaseMilliseconds -and
        $allocationIncrease -le [double]$budget.candidateManagedAllocationIncreaseBytesPerCase)
    $testImproved = ($testRmseImprovement -gt 0.0 -and $testP95Improvement -gt 0.0)
    $adopted = $validationSelected -and $testSafe -and $testImproved
    $reason = if (-not $validationSelected) { "RejectedByValidationAccuracyOrReliability" }
        elseif (-not $testSafe) { "RejectedByIndependentTestReliabilityOrBudget" }
        elseif (-not $testImproved) { "RejectedByIndependentTestAccuracy" }
        else { "AcceptedOptInOnFormalProductPath" }
    $decisions += [ordered]@{
        domain = $domain
        baseline = $baselineAlgorithm
        candidate = $candidateAlgorithm
        validationSelected = $validationSelected
        testRmseImprovementPercent = $testRmseImprovement
        testP95ImprovementPercent = $testP95Improvement
        testFailureRateDelta = [double]$testCandidate.failureRate - [double]$testBaseline.failureRate
        testAmbiguityRateDelta = [double]$testCandidate.ambiguityRate - [double]$testBaseline.ambiguityRate
        testP95LatencyIncreaseMilliseconds = $latencyIncrease
        testManagedAllocationIncreaseBytesPerCase = $allocationIncrease
        adopted = $adopted
        reason = $reason
        enable = if ($domain -eq "Circle") { "Method=CaliperFitV2; RefinementLoss=Welsch" } else { "Method=FitLine; FitLoss=Welsch" }
        rollback = if ($domain -eq "Circle") { "omit RefinementLoss or set Legacy" } else { "omit FitLoss or set L2" }
        evidenceScope = "mode"
    }
}

$comparison = [ordered]@{
    schemaVersion = "2026-07-16.operator-product-e2e-comparison.v1"
    benchmarkId = $after.benchmarkId
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    baselineProductSha = $baseline.productImplementation.repositorySha
    afterProductSha = $after.productImplementation.repositorySha
    dataset = $after.dataset
    harness = $after.harness
    environment = $after.environment
    resourceMeasurement = $after.resourceMeasurement
    baselineReportSha256 = (Get-FileHash -Algorithm SHA256 $baselineFile).Hash.ToLowerInvariant()
    afterReportSha256 = (Get-FileHash -Algorithm SHA256 $afterFile).Hash.ToLowerInvariant()
    oldDefaultConformance = $conformance
    decisions = $decisions
    claimBoundary = $after.dataset.claimBoundary
}
[IO.Directory]::CreateDirectory((Split-Path -Parent $outputFile)) | Out-Null
$comparison | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputFile -Encoding utf8NoBOM

$lines = [Collections.Generic.List[string]]::new()
$lines.Add("# Product Operator E2E Phase 5 Comparison")
$lines.Add("")
$lines.Add("- Frozen executable product SHA: ``$($comparison.baselineProductSha)``")
$lines.Add("- After product SHA: ``$($comparison.afterProductSha)``")
$lines.Add("- Dataset generated SHA: ``$($comparison.dataset.generatedDataSha256)``")
$lines.Add("- Harness program SHA: ``$($comparison.harness.programSha256)``")
$lines.Add("- Old/current default conformance: exact on accuracy, failure and diagnostics for Circle and Line validation/test rows.")
$lines.Add("- Managed allocation is benchmark-thread only; process working-set/private-byte observations remain separate in the source reports.")
$lines.Add("")
$lines.Add("| Domain | Baseline | Candidate | Test RMSE improvement | Test P95 improvement | Failure delta | P95 latency cost ms | Managed alloc cost B/case | Adopted | Reason |")
$lines.Add("|---|---|---|---:|---:|---:|---:|---:|---:|---|")
foreach ($decision in $decisions) {
    $lines.Add("| $($decision.domain) | $($decision.baseline) | $($decision.candidate) | $([Math]::Round($decision.testRmseImprovementPercent, 6))% | $([Math]::Round($decision.testP95ImprovementPercent, 6))% | $([Math]::Round($decision.testFailureRateDelta, 6)) | $([Math]::Round($decision.testP95LatencyIncreaseMilliseconds, 6)) | $([Math]::Round($decision.testManagedAllocationIncreaseBytesPerCase, 2)) | $($decision.adopted) | $($decision.reason) |")
}
$lines.Add("")
$lines.Add("## Claim boundary")
$lines.Add("")
$lines.Add($comparison.claimBoundary)
$lines.Add("")
$lines.Add("## Reproduction")
$lines.Add("")
$lines.Add('`& "./scripts/run-operator-product-e2e-evidence.ps1" -Profile acceptance -ResultsDirectory ".tmp/operator-product-e2e"`')
$lines | Set-Content -LiteralPath $reportFile -Encoding utf8NoBOM

Write-Host "Product E2E comparison complete: $outputFile"

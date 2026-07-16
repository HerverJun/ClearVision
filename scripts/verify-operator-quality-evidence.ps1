param(
    [string]$ManifestPath = "quality/evals/reports/operator-quality-phase5-evidence.json",

    [string]$FreshEvidenceDirectory = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
function Resolve-RepoPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-Equal([string]$Name, $Expected, $Actual) {
    $left = $Expected | ConvertTo-Json -Depth 20 -Compress
    $right = $Actual | ConvertTo-Json -Depth 20 -Compress
    if ($left -ne $right) { throw "Evidence mismatch for ${Name}: expected=$left actual=$right" }
}
function Get-LfNormalizedSha256([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    $text = [Text.Encoding]::UTF8.GetString($bytes)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $normalizedBytes = [Text.UTF8Encoding]::new($false).GetBytes($normalized)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($normalizedBytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}
function Artifact([string]$Id) {
    $match = @($manifest.artifacts | Where-Object { $_.id -eq $Id })
    if ($match.Count -ne 1) { throw "Expected exactly one artifact '$Id'; actual=$($match.Count)." }
    return $match[0]
}
function Load-Artifact([string]$Id) {
    $artifact = Artifact $Id
    return Get-Content -LiteralPath (Resolve-RepoPath $artifact.path) -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Load-Json([string]$Path) {
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}
function Operator-Evidence([string]$Type) {
    $match = @($manifest.operators | Where-Object { $_.operatorType -eq $Type })
    if ($match.Count -ne 1) { throw "Expected exactly one operator evidence entry '$Type'; actual=$($match.Count)." }
    return $match[0]
}
function Mode-Evidence($Operator, [string]$ModeId) {
    $match = @($Operator.modes | Where-Object { $_.modeId -eq $ModeId })
    if ($match.Count -ne 1) { throw "Expected exactly one mode '$ModeId' for $($Operator.operatorType); actual=$($match.Count)." }
    return $match[0]
}
function Assert-Aggregate-Default-Conformance($Comparison, [string]$Prefix) {
    $rows = @($Comparison.oldDefaultConformance)
    Assert-True ($rows.Count -eq 4) "$Prefix must contain four Circle/Line validation/test conformance rows."
    $actualKeys = @($rows | ForEach-Object { "$($_.domain)|$($_.split)" } | Sort-Object)
    Assert-Equal "$Prefix/rows" @("Circle|test", "Circle|validation", "Line|test", "Line|validation") $actualKeys
    foreach ($row in $rows) {
        Assert-True ($row.PSObject.Properties.Name -notcontains "exactAccuracyAndDiagnosticConformance") "$Prefix must not claim per-case exact diagnostic conformance."
        Assert-True ($row.aggregateAccuracyFailureAndDiagnosticSummaryConformance -eq $true) "$Prefix aggregate conformance drifted for $($row.domain)/$($row.split)."
    }
}
function Formal-Decision-Semantics($Comparison) {
    return @($Comparison.decisions | Sort-Object domain | ForEach-Object {
        [ordered]@{
            domain = $_.domain
            baseline = $_.baseline
            candidate = $_.candidate
            validationSelected = $_.validationSelected
            testRmseImprovementPercent = [double]$_.testRmseImprovementPercent
            testP95ImprovementPercent = [double]$_.testP95ImprovementPercent
            testFailureRateDelta = [double]$_.testFailureRateDelta
            testAmbiguityRateDelta = [double]$_.testAmbiguityRateDelta
            adopted = $_.adopted
            reason = $_.reason
            enable = $_.enable
            rollback = $_.rollback
            evidenceScope = $_.evidenceScope
        }
    })
}
function Kernel-Decision-Semantics($Comparison) {
    return @($Comparison.decisions | Sort-Object domain | ForEach-Object {
        [ordered]@{
            domain = $_.domain
            baseline = $_.baseline
            winner = $_.winner
            productionAlgorithm = $_.productionAlgorithm
            adopted = $_.adopted
            reason = $_.reason
            kernelCandidateSupported = $_.kernelCandidateSupported
            evidenceScope = $_.evidenceScope
            productionConformance = $_.productionConformance
            writtenBudgetPassed = if ($null -eq $_.writtenBudget) { $null } else { $_.writtenBudget.passed }
        }
    })
}
function Assert-Formal-Decision-Manifest-Binding($Comparison, [string]$Prefix) {
    $decisions = @($Comparison.decisions)
    Assert-True ($decisions.Count -eq 2) "$Prefix must contain exactly Circle and Line formal decisions."
    $circleDecision = @($decisions | Where-Object { $_.domain -eq "Circle" })
    $lineDecision = @($decisions | Where-Object { $_.domain -eq "Line" })
    Assert-True ($circleDecision.Count -eq 1 -and $lineDecision.Count -eq 1) "$Prefix must contain one Circle and one Line decision."

    $circleOperator = Operator-Evidence "CircleMeasurement"
    $lineOperator = Operator-Evidence "LineMeasurement"
    $circleMode = Mode-Evidence $circleOperator "Method=CaliperFitV2; RefinementLoss=Welsch"
    $lineMode = Mode-Evidence $lineOperator "Method=FitLine; FitLoss=Welsch"
    Assert-Equal "$Prefix/circle/mode" $circleMode.modeId $circleDecision[0].enable
    Assert-Equal "$Prefix/circle/adopted" $circleMode.adopted $circleDecision[0].adopted
    Assert-Equal "$Prefix/circle/scope" "mode" $circleDecision[0].evidenceScope
    Assert-True ($circleDecision[0].adopted -eq $false -and $circleMode.isDefault -eq $false) "$Prefix Circle Welsch must remain a non-default, not-adopted mode."
    Assert-Equal "$Prefix/line/mode" $lineMode.modeId $lineDecision[0].enable
    Assert-Equal "$Prefix/line/adopted" $lineMode.adopted $lineDecision[0].adopted
    Assert-Equal "$Prefix/line/scope" "mode" $lineDecision[0].evidenceScope
    Assert-True ($lineDecision[0].adopted -eq $true -and $lineMode.isDefault -eq $false -and $lineMode.algorithmQuality -eq "SyntheticBenchmarkValidated") "$Prefix Line Welsch must remain adopted only as a non-default validated mode."
    Assert-Equal "$Prefix/mode-bijection" @(@($circleMode.modeId, $lineMode.modeId) | Sort-Object) @($decisions.enable | Sort-Object)
}

$manifestFile = Resolve-RepoPath $ManifestPath
$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-Equal "schemaVersion" "2026-07-16.operator-quality-evidence.v1" $manifest.schemaVersion
Assert-True (-not [string]::IsNullOrWhiteSpace($manifest.claimBoundary)) "Evidence manifest claimBoundary is required."

foreach ($artifact in $manifest.artifacts) {
    $artifactPath = Resolve-RepoPath $artifact.path
    Assert-True (Test-Path -LiteralPath $artifactPath -PathType Leaf) "Evidence artifact is missing: $($artifact.path)"
    $actualHash = (Get-FileHash -Algorithm SHA256 $artifactPath).Hash.ToLowerInvariant()
    Assert-Equal "artifact/$($artifact.id)/sha256" $artifact.sha256 $actualHash
}

$baseline = Load-Artifact "product-baseline"
$after = Load-Artifact "product-after"
$comparison = Load-Artifact "product-comparison"
$productIdentity = $manifest.identities.productE2E
Assert-Equal "product/baseline/label" "baseline" $baseline.label
Assert-Equal "product/after/label" "after" $after.label
Assert-Equal "product/baseline/source" $productIdentity.baselineProductSha $baseline.productImplementation.repositorySha
Assert-Equal "product/after/source" $productIdentity.afterProductSha $after.productImplementation.repositorySha
Assert-True (-not $baseline.productImplementation.repositoryDirty -and -not $after.productImplementation.repositoryDirty) "Product E2E reports must bind clean product source trees."
Assert-True (-not $baseline.harness.repositoryDirty -and -not $after.harness.repositoryDirty) "Product E2E reports must bind a clean committed harness."
Assert-True ($baseline.harness.adapterInjectedIntoProductWorktree -and -not $after.harness.adapterInjectedIntoProductWorktree) "Baseline must use the isolated adapter; after must execute the current worktree directly."
foreach ($report in @($baseline, $after)) {
    Assert-Equal "product/dataset/id" $productIdentity.datasetId $report.dataset.id
    Assert-Equal "product/dataset/version" $productIdentity.datasetVersion $report.dataset.version
    Assert-Equal "product/dataset/manifest" $productIdentity.datasetManifestSha256 $report.dataset.manifestSha256
    Assert-Equal "product/dataset/generated" $productIdentity.generatedDataSha256 $report.dataset.generatedDataSha256
    Assert-Equal "product/harness/commit" $productIdentity.harnessCommitSha $report.harness.commitSha
    Assert-Equal "product/harness/program" $productIdentity.harnessProgramSha256 $report.harness.programSha256
    Assert-Equal "product/harness/project" $productIdentity.harnessProjectSha256 $report.harness.projectSha256
    Assert-Equal "product/claimBoundary" $manifest.claimBoundary.Contains("Evidence is scoped by operator mode") $true
}
Assert-True ($null -ne $productIdentity.currentSourceFilesSha256) "Tracked product identity must declare currentSourceFilesSha256."
foreach ($source in $productIdentity.currentSourceFilesSha256.PSObject.Properties) {
    $sourcePath = Resolve-RepoPath $source.Name
    Assert-Equal "product/current-source/$($source.Name)" $source.Value (Get-LfNormalizedSha256 $sourcePath)
}
Assert-Equal "product/comparison/baselineSha" $productIdentity.baselineProductSha $comparison.baselineProductSha
Assert-Equal "product/comparison/afterSha" $productIdentity.afterProductSha $comparison.afterProductSha
Assert-Equal "product/comparison/schema" "2026-07-16.operator-product-e2e-comparison.v2" $comparison.schemaVersion
Assert-Equal "product/comparison/baselineReportHash" (Artifact "product-baseline").sha256 $comparison.baselineReportSha256
Assert-Equal "product/comparison/afterReportHash" (Artifact "product-after").sha256 $comparison.afterReportSha256
Assert-Equal "product/comparison/claimBoundary" $after.dataset.claimBoundary $comparison.claimBoundary
Assert-Aggregate-Default-Conformance $comparison "product/comparison/default-conformance"
Assert-Formal-Decision-Manifest-Binding $comparison "product/comparison/decision-binding"
$circleDecision = @($comparison.decisions | Where-Object { $_.domain -eq "Circle" })[0]
$lineDecision = @($comparison.decisions | Where-Object { $_.domain -eq "Line" })[0]
Assert-True ($circleDecision.adopted -eq $false -and $circleDecision.evidenceScope -eq "mode") "Circle Welsch must remain not adopted at formal product-path scope."
Assert-True ($lineDecision.adopted -eq $true -and $lineDecision.evidenceScope -eq "mode") "Line Welsch must remain adopted only at mode scope."

$kernelBaseline = Load-Artifact "kernel-baseline"
$kernelAfter = Load-Artifact "kernel-after"
$kernelComparison = Load-Artifact "kernel-comparison"
$kernelIdentity = $manifest.identities.supplementalKernel
foreach ($report in @($kernelBaseline, $kernelAfter)) {
    Assert-Equal "kernel/source" $kernelIdentity.harnessCommitSha $report.sourceSha
    Assert-Equal "kernel/harness/commit" $kernelIdentity.harnessCommitSha $report.harness.commitSha
    Assert-Equal "kernel/harness/program" $kernelIdentity.harnessProgramSha256 $report.harness.programSha256
    Assert-Equal "kernel/dataset/manifest" $kernelIdentity.datasetManifestSha256 $report.dataset.manifestSha256
    Assert-Equal "kernel/dataset/generated" $kernelIdentity.generatedDataSha256 $report.dataset.generatedDataSha256
    Assert-True ($report.executionScope.Contains("supplemental") -and -not $report.harness.repositoryDirty) "Kernel evidence must be explicitly supplemental and clean."
}
Assert-Equal "kernel/model/sha" $kernelIdentity.modelSha256 $kernelAfter.model.sha256
Assert-Equal "kernel/model/manifest" $kernelIdentity.embeddingManifestSha256 $kernelAfter.model.manifestSha256
Assert-Equal "kernel/model/preprocess" $kernelIdentity.preprocessFingerprint $kernelAfter.model.preprocessFingerprint
Assert-True (@($kernelComparison.decisions | Where-Object { $_.adopted -ne $false }).Count -eq 0) "Supplemental kernel report must not make formal adoption decisions."

$anomaly = Load-Artifact "anomaly-public-dataset"
Assert-Equal "anomaly/defaultExtractor" "lab_gradient_stats" $anomaly.Summary.FeatureExtractorId
Assert-True ($anomaly.Summary.EmbeddingModelConfigured -eq $false -and $anomaly.Summary.Failed -eq 0) "MVTec evidence must remain traditional-mode-only and failure-free."

$circle = Operator-Evidence "CircleMeasurement"
$line = Operator-Evidence "LineMeasurement"
$anomalyOperator = Operator-Evidence "AnomalyDetection"
Assert-Equal "circle/wholeQuality" "SyntheticBenchmarkEvidence" $circle.algorithmQuality
Assert-Equal "line/wholeQuality" "SyntheticBenchmarkEvidence" $line.algorithmQuality
Assert-True ((Mode-Evidence $circle "Method=CaliperFitV2; RefinementLoss=Welsch").adopted -eq $false) "Circle Welsch mode verdict drifted."
$lineWelsch = Mode-Evidence $line "Method=FitLine; FitLoss=Welsch"
Assert-True ($lineWelsch.adopted -eq $true -and $lineWelsch.algorithmQuality -eq "SyntheticBenchmarkValidated" -and -not $lineWelsch.isDefault) "Line Welsch mode scope drifted."
$onnxMode = Mode-Evidence $anomalyOperator "FeatureExtractor=onnx_embedding"
Assert-True ($onnxMode.algorithmQuality -eq "SyntheticBenchmarkEvidence") "ONNX mode must not inherit traditional public-dataset accuracy evidence."

if (-not [string]::IsNullOrWhiteSpace($FreshEvidenceDirectory)) {
    $freshRoot = Resolve-RepoPath $FreshEvidenceDirectory
    Assert-True (Test-Path -LiteralPath $freshRoot -PathType Container) "Fresh evidence directory is missing: $freshRoot"
    function Fresh-Path([string]$FileName) {
        $path = Join-Path $freshRoot $FileName
        Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Fresh evidence artifact is missing: $path"
        return $path
    }

    $gitHead = (& git -C $repoRoot rev-parse HEAD).Trim()
    Assert-True ($LASTEXITCODE -eq 0 -and $gitHead -match '^[0-9a-f]{40}$') "Unable to resolve current Git HEAD for fresh evidence verification."
    $gitStatus = (& git -C $repoRoot status --porcelain) -join "`n"
    Assert-True ($LASTEXITCODE -eq 0 -and [string]::IsNullOrWhiteSpace($gitStatus)) "Fresh evidence must be verified against a clean committed worktree."

    $freshProductBaselinePath = Fresh-Path "operator-product-e2e-baseline-acceptance.json"
    $freshProductAfterPath = Fresh-Path "operator-product-e2e-after-acceptance.json"
    $freshProductComparisonPath = Fresh-Path "operator-product-e2e-phase5-comparison.json"
    $freshProductBaseline = Load-Json $freshProductBaselinePath
    $freshProductAfter = Load-Json $freshProductAfterPath
    $freshProductComparison = Load-Json $freshProductComparisonPath

    Assert-Equal "fresh/product/baseline/label" "baseline" $freshProductBaseline.label
    Assert-Equal "fresh/product/after/label" "after" $freshProductAfter.label
    Assert-Equal "fresh/product/baseline/source" $productIdentity.baselineProductSha $freshProductBaseline.productImplementation.repositorySha
    Assert-Equal "fresh/product/after/source" $gitHead $freshProductAfter.productImplementation.repositorySha
    Assert-Equal "fresh/product/baseline/harness-commit" $gitHead $freshProductBaseline.harness.commitSha
    Assert-Equal "fresh/product/after/harness-commit" $gitHead $freshProductAfter.harness.commitSha
    Assert-True (-not $freshProductBaseline.productImplementation.repositoryDirty -and -not $freshProductAfter.productImplementation.repositoryDirty) "Fresh product evidence must bind clean product sources."
    Assert-True (-not $freshProductBaseline.harness.repositoryDirty -and -not $freshProductAfter.harness.repositoryDirty) "Fresh product evidence must bind a clean current harness."
    Assert-True ($freshProductBaseline.harness.adapterInjectedIntoProductWorktree -and -not $freshProductAfter.harness.adapterInjectedIntoProductWorktree) "Fresh baseline/after adapter scope drifted."
    foreach ($report in @($freshProductBaseline, $freshProductAfter)) {
        Assert-Equal "fresh/product/schema" $baseline.schemaVersion $report.schemaVersion
        Assert-Equal "fresh/product/benchmark" $baseline.benchmarkId $report.benchmarkId
        Assert-Equal "fresh/product/dataset" $baseline.dataset $report.dataset
        Assert-Equal "fresh/product/harness/program" $productIdentity.harnessProgramSha256 $report.harness.programSha256
        Assert-Equal "fresh/product/harness/project" $productIdentity.harnessProjectSha256 $report.harness.projectSha256
        Assert-Equal "fresh/product/harness/manifest" $productIdentity.datasetManifestSha256 $report.harness.manifestSha256
    }
    Assert-Equal "fresh/product/baseline/source-files" $baseline.productImplementation.sourceFilesSha256 $freshProductBaseline.productImplementation.sourceFilesSha256
    Assert-Equal "fresh/product/after/source-files" $productIdentity.currentSourceFilesSha256 $freshProductAfter.productImplementation.sourceFilesSha256
    Assert-Equal "fresh/product/baseline/mode-claims" $baseline.modeClaims $freshProductBaseline.modeClaims
    Assert-Equal "fresh/product/after/mode-claims" $after.modeClaims $freshProductAfter.modeClaims
    foreach ($source in $freshProductAfter.productImplementation.sourceFilesSha256.PSObject.Properties) {
        $sourcePath = Resolve-RepoPath $source.Name
        Assert-Equal "fresh/product/current-source/$($source.Name)" $source.Value (Get-LfNormalizedSha256 $sourcePath)
    }
    Assert-Equal "fresh/product/comparison/schema" "2026-07-16.operator-product-e2e-comparison.v2" $freshProductComparison.schemaVersion
    Assert-Equal "fresh/product/comparison/baseline-source" $productIdentity.baselineProductSha $freshProductComparison.baselineProductSha
    Assert-Equal "fresh/product/comparison/after-source" $gitHead $freshProductComparison.afterProductSha
    Assert-Equal "fresh/product/comparison/dataset" $freshProductAfter.dataset $freshProductComparison.dataset
    Assert-Equal "fresh/product/comparison/harness" $freshProductAfter.harness $freshProductComparison.harness
    Assert-Equal "fresh/product/comparison/baseline-hash" ((Get-FileHash -Algorithm SHA256 $freshProductBaselinePath).Hash.ToLowerInvariant()) $freshProductComparison.baselineReportSha256
    Assert-Equal "fresh/product/comparison/after-hash" ((Get-FileHash -Algorithm SHA256 $freshProductAfterPath).Hash.ToLowerInvariant()) $freshProductComparison.afterReportSha256
    Assert-Equal "fresh/product/comparison/claimBoundary" $comparison.claimBoundary $freshProductComparison.claimBoundary
    Assert-Aggregate-Default-Conformance $freshProductComparison "fresh/product/default-conformance"
    Assert-Formal-Decision-Manifest-Binding $freshProductComparison "fresh/product/decision-binding"
    Assert-Equal "fresh/product/decision-semantics" (Formal-Decision-Semantics $comparison) (Formal-Decision-Semantics $freshProductComparison)

    $freshKernelBaseline = Load-Json (Fresh-Path "operator-precision-baseline-acceptance.json")
    $freshKernelAfter = Load-Json (Fresh-Path "operator-precision-after-acceptance.json")
    $freshKernelComparison = Load-Json (Fresh-Path "operator-precision-phase5-comparison.json")
    Assert-Equal "fresh/kernel/baseline/label" "baseline" $freshKernelBaseline.label
    Assert-Equal "fresh/kernel/after/label" "after" $freshKernelAfter.label
    Assert-Equal "fresh/kernel/baseline/source" $gitHead $freshKernelBaseline.sourceSha
    Assert-Equal "fresh/kernel/after/source" $gitHead $freshKernelAfter.sourceSha
    foreach ($report in @($freshKernelBaseline, $freshKernelAfter)) {
        Assert-Equal "fresh/kernel/harness/commit" $gitHead $report.harness.commitSha
        Assert-True (-not $report.harness.repositoryDirty) "Fresh supplemental evidence must bind a clean current harness."
        Assert-Equal "fresh/kernel/execution-scope" $kernelAfter.executionScope $report.executionScope
        Assert-Equal "fresh/kernel/dataset" $kernelAfter.dataset $report.dataset
        Assert-Equal "fresh/kernel/model" $kernelAfter.model $report.model
        Assert-Equal "fresh/kernel/harness/program" $kernelIdentity.harnessProgramSha256 $report.harness.programSha256
        Assert-Equal "fresh/kernel/harness/project" $kernelAfter.harness.projectSha256 $report.harness.projectSha256
        Assert-Equal "fresh/kernel/harness/run-script" $kernelAfter.harness.runScriptSha256 $report.harness.runScriptSha256
    }
    Assert-Equal "fresh/kernel/comparison/baseline-source" $gitHead $freshKernelComparison.baselineSourceSha
    Assert-Equal "fresh/kernel/comparison/after-source" $gitHead $freshKernelComparison.afterSourceSha
    Assert-Equal "fresh/kernel/comparison/claimBoundary" $kernelComparison.claimBoundary $freshKernelComparison.claimBoundary
    Assert-Equal "fresh/kernel/comparison/immutable-dataset" $kernelIdentity.generatedDataSha256 $freshKernelComparison.immutableInputs.generatedDataSha256
    Assert-Equal "fresh/kernel/comparison/immutable-model" $kernelIdentity.modelSha256 $freshKernelComparison.immutableInputs.modelSha256
    Assert-Equal "fresh/kernel/comparison/immutable-preprocess" $kernelIdentity.preprocessFingerprint $freshKernelComparison.immutableInputs.preprocessFingerprint
    Assert-Equal "fresh/kernel/decision-semantics" (Kernel-Decision-Semantics $kernelComparison) (Kernel-Decision-Semantics $freshKernelComparison)
    Assert-Equal "fresh/kernel/rejected-candidates" $kernelComparison.rejectedCandidates $freshKernelComparison.rejectedCandidates
    Assert-Equal "fresh/kernel/uncertainty" $kernelComparison.uncertaintyCoverage $freshKernelComparison.uncertaintyCoverage
    Assert-Equal "fresh/kernel/anomaly" $kernelComparison.anomalyIdentity $freshKernelComparison.anomalyIdentity
    Assert-Equal "fresh/kernel/acceptance" $kernelComparison.acceptance $freshKernelComparison.acceptance
    Assert-True (@($freshKernelComparison.decisions | Where-Object { $_.adopted -ne $false }).Count -eq 0) "Fresh supplemental kernel evidence must not make formal adoption decisions."

    Write-Host "Fresh operator evidence verified against tracked manifest: directory=$freshRoot head=$gitHead"
}

Write-Host "Operator quality evidence verified: artifacts=$(@($manifest.artifacts).Count) operators=$(@($manifest.operators).Count) manifestSha256=$((Get-FileHash -Algorithm SHA256 $manifestFile).Hash.ToLowerInvariant())"

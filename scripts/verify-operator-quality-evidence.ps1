param(
    [string]$ManifestPath = "quality/evals/reports/operator-quality-phase5-evidence.json"
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
function Artifact([string]$Id) {
    $match = @($manifest.artifacts | Where-Object { $_.id -eq $Id })
    if ($match.Count -ne 1) { throw "Expected exactly one artifact '$Id'; actual=$($match.Count)." }
    return $match[0]
}
function Load-Artifact([string]$Id) {
    $artifact = Artifact $Id
    return Get-Content -LiteralPath (Resolve-RepoPath $artifact.path) -Raw -Encoding UTF8 | ConvertFrom-Json
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
foreach ($source in $after.productImplementation.sourceFilesSha256.PSObject.Properties) {
    $sourcePath = Resolve-RepoPath $source.Name
    Assert-Equal "product/current-source/$($source.Name)" $source.Value ((Get-FileHash -Algorithm SHA256 $sourcePath).Hash.ToLowerInvariant())
}
Assert-Equal "product/comparison/baselineSha" $productIdentity.baselineProductSha $comparison.baselineProductSha
Assert-Equal "product/comparison/afterSha" $productIdentity.afterProductSha $comparison.afterProductSha
Assert-Equal "product/comparison/baselineReportHash" (Artifact "product-baseline").sha256 $comparison.baselineReportSha256
Assert-Equal "product/comparison/afterReportHash" (Artifact "product-after").sha256 $comparison.afterReportSha256
Assert-True (@($comparison.oldDefaultConformance).Count -eq 4 -and @($comparison.oldDefaultConformance | Where-Object { -not $_.exactAccuracyAndDiagnosticConformance }).Count -eq 0) "Old/current default conformance must be exact for all Circle/Line validation/test rows."
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

Write-Host "Operator quality evidence verified: artifacts=$(@($manifest.artifacts).Count) operators=$(@($manifest.operators).Count) manifestSha256=$((Get-FileHash -Algorithm SHA256 $manifestFile).Hash.ToLowerInvariant())"

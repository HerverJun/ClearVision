param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "acceptance",

    [string]$ResultsDirectory = ".tmp/operator-product-e2e",

    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$resultsRoot = if ([IO.Path]::IsPathRooted($ResultsDirectory)) { [IO.Path]::GetFullPath($ResultsDirectory) } else { [IO.Path]::GetFullPath((Join-Path $repoRoot $ResultsDirectory)) }

& (Join-Path $PSScriptRoot "reproduce-operator-precision-baseline.ps1") -Profile $Profile -ResultsDirectory $resultsRoot -AllowDirty:$AllowDirty
& (Join-Path $PSScriptRoot "run-operator-product-e2e-benchmark.ps1") -Profile $Profile -Label after -IncludeCandidates -ResultsDirectory $resultsRoot -AllowDirty:$AllowDirty
& (Join-Path $PSScriptRoot "compare-operator-product-e2e-benchmarks.ps1") `
    -BaselinePath (Join-Path $resultsRoot "operator-product-e2e-baseline-$Profile.json") `
    -AfterPath (Join-Path $resultsRoot "operator-product-e2e-after-$Profile.json") `
    -OutputPath (Join-Path $resultsRoot "operator-product-e2e-phase5-comparison.json") `
    -ReportPath (Join-Path $resultsRoot "operator-product-e2e-phase5-comparison.md")

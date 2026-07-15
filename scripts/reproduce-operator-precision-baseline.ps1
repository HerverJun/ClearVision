param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "acceptance",

    [string]$ResultsDirectory = ".tmp/operator-precision-baseline",

    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
$baselineSha = "ce266626e0bec0a8cd4a68c11b176df95e8cb482"
$runner = Join-Path $PSScriptRoot "run-operator-precision-benchmark.ps1"

# The benchmark harness was introduced after the frozen product baseline. This command
# runs the versioned v2 harness against the frozen baseline algorithm references and
# records the product baseline SHA separately from the harness commit/content SHA.
& $runner `
    -Profile $Profile `
    -Label baseline `
    -SourceShaOverride $baselineSha `
    -ResultsDirectory $ResultsDirectory `
    -AllowDirty:$AllowDirty

exit $LASTEXITCODE

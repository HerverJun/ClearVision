param(
    [ValidateSet("smoke", "standard", "acceptance")]
    [string]$Profile = "standard",

    [string]$ResultsDirectory = ".tmp/operator-product-e2e",

    [switch]$AllowDirty,

    [switch]$ReturnExitCode
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$baselineSha = "ce266626e0bec0a8cd4a68c11b176df95e8cb482"
$resolvedBaselineSha = (& git -C $repoRoot rev-parse "$baselineSha^{commit}").Trim()
if ($LASTEXITCODE -ne 0 -or $resolvedBaselineSha -ne $baselineSha) {
    throw "Frozen product baseline commit '$baselineSha' is unavailable. Fetch full history before reproducing evidence."
}

if (-not $AllowDirty) {
    $dirty = @(& git -C $repoRoot status --porcelain=v1)
    if ($dirty.Count -gt 0) {
        throw "A clean harness worktree is required before reproducing the executable baseline: $($dirty -join ', ')."
    }
}

$driveRoot = [IO.Path]::GetPathRoot($repoRoot)
$tempRoot = [IO.Path]::GetFullPath((Join-Path $driveRoot ("cv-p5-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))))
$tempPrefix = [IO.Path]::GetFullPath((Join-Path $driveRoot "cv-p5-"))
if (-not $tempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or -not ([IO.Path]::GetFileName($tempRoot)).StartsWith("cv-p5-", [StringComparison]::Ordinal)) {
    throw "Refusing unsafe temporary worktree path '$tempRoot'."
}

$sourceRunner = Join-Path $repoRoot "quality\tools\OperatorProductE2EBenchmarkRunner"
$worktreeAdded = $false
$exitCode = 1
try {
    & git -C $repoRoot worktree add --detach $tempRoot $baselineSha
    if ($LASTEXITCODE -ne 0) { throw "Unable to create detached baseline worktree." }
    $worktreeAdded = $true

    $baselineRunner = Join-Path $tempRoot "quality\tools\OperatorProductE2EBenchmarkRunner"
    [IO.Directory]::CreateDirectory($baselineRunner) | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRunner "Program.cs") -Destination (Join-Path $baselineRunner "Program.cs")
    Copy-Item -LiteralPath (Join-Path $sourceRunner "OperatorProductE2EBenchmarkRunner.csproj") -Destination (Join-Path $baselineRunner "OperatorProductE2EBenchmarkRunner.csproj")

    foreach ($name in @("Program.cs", "OperatorProductE2EBenchmarkRunner.csproj")) {
        $sourceHash = (Get-FileHash -Algorithm SHA256 (Join-Path $sourceRunner $name)).Hash
        $copyHash = (Get-FileHash -Algorithm SHA256 (Join-Path $baselineRunner $name)).Hash
        if ($sourceHash -ne $copyHash) { throw "Injected baseline adapter '$name' does not match the committed harness source." }
    }

    & (Join-Path $repoRoot "scripts\run-operator-product-e2e-benchmark.ps1") `
        -Profile $Profile `
        -Label baseline `
        -ResultsDirectory $ResultsDirectory `
        -ProductRoot $tempRoot `
        -RunnerProject (Join-Path $baselineRunner "OperatorProductE2EBenchmarkRunner.csproj") `
        -HarnessSourceDirectory $sourceRunner `
        -AdapterInjected `
        -AllowDirty:$AllowDirty `
        -ReturnExitCode
    $exitCode = $LASTEXITCODE
}
finally {
    if ($worktreeAdded) {
        & git -C $repoRoot worktree remove --force $tempRoot
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Temporary baseline worktree could not be removed automatically: $tempRoot"
        }
        & git -C $repoRoot worktree prune
    } elseif (Test-Path -LiteralPath $tempRoot) {
        $resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
        if ($resolvedTempRoot.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
        }
    }
}

if ($ReturnExitCode) { exit $exitCode }
if ($exitCode -ne 0) { throw "Executable frozen baseline reproduction failed with exit code $exitCode." }

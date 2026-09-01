[CmdletBinding()]
param(
    [string]$PackageResultPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Utf8NoBom {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Content)
    $parent = Split-Path -Parent $Path
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StreamSha256 {
    param([Parameter(Mandatory = $true)][System.IO.Stream]$Stream)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($algorithm.ComputeHash($Stream)).ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

function Read-ZipText {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name
    )
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "ZIP entry is missing: $Name" }
    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.UTF8Encoding]::new($false), $true)
    try { return $reader.ReadToEnd() }
    finally { $reader.Dispose(); $stream.Dispose() }
}

function Assert-PowerShellParses {
    param([Parameter(Mandatory = $true)][string]$Path)
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) {
        throw "PowerShell AST parse failed for $Path`: $(@($errors | ForEach-Object Message) -join '; ')"
    }
}

function Assert-HygieneFailure {
    param(
        [Parameter(Mandatory = $true)][string]$HygieneScript,
        [Parameter(Mandatory = $true)][string]$FixtureRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $caseRoot = Join-Path $FixtureRoot ([Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $caseRoot | Out-Null
    Write-Utf8NoBom -Path (Join-Path $caseRoot $RelativePath) -Content $Content
    $failedAsExpected = $false
    try {
        & $HygieneScript -PublishDirectory $caseRoot
    }
    catch {
        $failedAsExpected = $true
    }
    if (-not $failedAsExpected) { throw "Hygiene fixture unexpectedly passed: $RelativePath" }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$canonical = Join-Path $repoRoot "scripts\package-portable-deployment.ps1"
$hygiene = Join-Path $repoRoot "scripts\Test-ReleasePublishHygiene.ps1"
$wrappers = @(
    (Join-Path $repoRoot "scripts\package-studio-station-full.ps1"),
    (Join-Path $repoRoot "scripts\package-studio-station-lite.ps1")
)
$workflow = Join-Path $repoRoot ".github\workflows\ci.yml"

foreach ($script in @($canonical, $hygiene) + $wrappers) { Assert-PowerShellParses -Path $script }
foreach ($wrapper in $wrappers) {
    $text = Get-Content -LiteralPath $wrapper -Raw -Encoding UTF8
    if (-not $text.Contains("package-portable-deployment.ps1", [StringComparison]::Ordinal)) {
        throw "Packaging wrapper does not call the canonical implementation: $wrapper"
    }
    if ($text -match '(?i)\bdotnet\s+publish\b|\bCompress-Archive\b') {
        throw "Packaging wrapper contains copied publish/archive logic: $wrapper"
    }
}
$workflowText = Get-Content -LiteralPath $workflow -Raw -Encoding UTF8
foreach ($required in @(
    "package-portable-deployment.ps1",
    "-Profile field-self-contained",
    "-RuntimeIdentifier win-x64",
    '-SourceRevisionId "${{ github.sha }}"',
    "-EnforceReleasePolicy",
    "SBOM.spdx.json",
    "THIRD-PARTY-NOTICES.txt",
    "dependency-report.json",
    "identity-manifest.json",
    "SHA256SUMS"
)) {
    if (-not $workflowText.Contains($required, [StringComparison]::Ordinal)) {
        throw "Release workflow is missing canonical assertion/upload text: $required"
    }
}

$selfTestRoot = Join-Path $repoRoot (".tmp\publish-check\wave3c\self-test-" + [Guid]::NewGuid().ToString("N"))
$approvedScratchRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp\publish-check\wave3c"))
$resolvedSelfTest = [System.IO.Path]::GetFullPath($selfTestRoot)
if (-not $resolvedSelfTest.StartsWith($approvedScratchRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Self-test scratch path escaped the approved Wave 3C directory."
}
New-Item -ItemType Directory -Path $selfTestRoot | Out-Null
try {
    $good = Join-Path $selfTestRoot "good"
    New-Item -ItemType Directory -Path $good | Out-Null
    Write-Utf8NoBom -Path (Join-Path $good "Launch-ClearVision.cmd") -Content "@echo off`r`n"
    Write-Utf8NoBom -Path (Join-Path $good "appsettings.json") -Content '{"ApiKey":"","Password":null}'
    & $hygiene -PublishDirectory $good

    $badCases = @(
        @{ Path = "wwwroot\feature.test.mjs"; Content = "export {};" },
        @{ Path = "FrontendV2\app.js"; Content = "void 0;" },
        @{ Path = "node_modules\fixture.js"; Content = "void 0;" },
        @{ Path = "appsettings.Development.json"; Content = "{}" },
        @{ Path = "site.map"; Content = "{}" },
        @{ Path = "node.exe"; Content = "fixture" },
        @{ Path = "package.json"; Content = '{"dependencies":{"x":"1.0.0"}}' },
        @{ Path = "secret.json"; Content = '{"apiKey":"real-secret-value"}' },
        @{ Path = "secret.txt"; Content = "access_token=real-token-value" },
        @{ Path = "local-path.txt"; Content = "C:\Users\Fixture\model.onnx" }
    )
    foreach ($case in $badCases) {
        Assert-HygieneFailure -HygieneScript $hygiene -FixtureRoot $selfTestRoot -RelativePath $case.Path -Content $case.Content
    }
}
finally {
    if (Test-Path -LiteralPath $selfTestRoot) {
        Remove-Item -LiteralPath $selfTestRoot -Recurse -Force
    }
}

if (-not [string]::IsNullOrWhiteSpace($PackageResultPath)) {
    $resolvedResultPath = (Resolve-Path -LiteralPath $PackageResultPath).Path
    $resultRoot = Split-Path -Parent $resolvedResultPath
    $result = Get-Content -LiteralPath $resolvedResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $zipPath = Join-Path $resultRoot $result.portableZip.path
    if ((Get-Sha256 $zipPath) -ne $result.portableZip.sha256) { throw "package-result ZIP checksum mismatch." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $entryMap = @{}
        foreach ($entry in $entries) { $entryMap[$entry.FullName.Replace("\", "/")] = $entry }
        $manifest = Read-ZipText -Archive $archive -Name "release/package-content-manifest.json" | ConvertFrom-Json
        $sourceIdentity = Read-ZipText -Archive $archive -Name "release/source-identity.json" | ConvertFrom-Json
        $launchText = Read-ZipText -Archive $archive -Name "Launch-ClearVision.cmd"
        if ($launchText -match '(?i)\b(?:node|nodejs|npm|npx)\b') { throw "Launch chain calls Node tooling." }
        if ($manifest.contentFingerprint -ne $result.contentFingerprint) { throw "Content fingerprint mismatch." }
        if ($sourceIdentity.gitSha -ne $result.gitSha -or $sourceIdentity.runtimeIdentifier -ne $result.runtimeIdentifier -or $sourceIdentity.profile -ne $result.profile) {
            throw "ZIP source identity does not match package-result identity."
        }
        if (@($sourceIdentity.runtimeInventory | Where-Object { $_ -match '(?i)[A-Z]:[\\/]+Users[\\/]+|/home/[^/]+/' }).Count -gt 0) {
            throw "ZIP source identity contains a machine-local runtime installation path."
        }
        if (@($manifest.files).Count -ne [int]$manifest.fileCount) { throw "Content manifest count field is inconsistent." }
        foreach ($row in @($manifest.files)) {
            $entry = $entryMap[[string]$row.path]
            if ($null -eq $entry) { throw "Content manifest references a missing ZIP entry: $($row.path)" }
            if ($entry.Length -ne [long]$row.sizeBytes) { throw "Content manifest size mismatch: $($row.path)" }
            $stream = $entry.Open()
            try { $actualHash = Get-StreamSha256 -Stream $stream }
            finally { $stream.Dispose() }
            if ($actualHash -ne $row.sha256) { throw "Content manifest hash mismatch: $($row.path)" }
        }
        $depsCount = @($entries | Where-Object { $_.FullName -match '(?i)\.deps\.json$' }).Count
        if ($depsCount -ne 1) { throw "Portable ZIP must contain exactly one .deps.json; found $depsCount." }
        $testFiles = @($entries | Where-Object { $_.FullName -match '(?i)(?:\.tests?\.|\.test\.|\.spec\.|^|/)testhost\.' })
        if ($testFiles.Count -gt 0) { throw "Portable ZIP contains test files: $($testFiles.FullName -join ', ')" }
    }
    finally {
        $archive.Dispose()
    }

    $supplyRoot = Join-Path $resultRoot $result.supplyChainDirectory
    $validation = Get-Content -LiteralPath (Join-Path $supplyRoot "validation-summary.json") -Raw | ConvertFrom-Json
    $identity = Get-Content -LiteralPath (Join-Path $supplyRoot "identity-manifest.json") -Raw | ConvertFrom-Json
    $report = Get-Content -LiteralPath (Join-Path $supplyRoot "dependency-report.json") -Raw | ConvertFrom-Json
    $sbom = Get-Content -LiteralPath (Join-Path $supplyRoot "SBOM.spdx.json") -Raw | ConvertFrom-Json
    if (-not $validation.generationPassed -or -not $validation.artifactConsistencyPassed) {
        throw "Supply-chain validation did not pass structural consistency."
    }
    if ($identity.schemaVersion -ne "clearvision.release-identity/v1") { throw "Release identity schema is incorrect." }
    if ($identity.portablePackage.sha256 -ne $result.portableZip.sha256) { throw "Supply identity ZIP hash mismatch." }
    $reportComponents = @($report.components | ForEach-Object { "$($_.name)@$($_.version)" } | Sort-Object)
    $sbomComponents = @($sbom.packages | ForEach-Object { "$($_.name)@$($_.versionInfo)" } | Sort-Object)
    if (($reportComponents -join "`n") -ne ($sbomComponents -join "`n")) { throw "SBOM/report component sets differ." }
    $s7 = @($report.components | Where-Object { $_.name -ieq "S7NetPlus" -and $_.version -eq "0.20.0" })
    if ($s7.Count -gt 0 -and ($s7[0].license -ne "NOASSERTION" -or $s7[0].policyDisposition -ne "blocked-noassertion")) {
        throw "S7NetPlus 0.20.0 was silently approved or assigned unsupported license evidence."
    }
}

Write-Host "Canonical portable packaging self-test passed."

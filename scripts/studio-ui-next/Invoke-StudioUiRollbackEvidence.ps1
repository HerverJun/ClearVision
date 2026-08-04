[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [int]$BaseWebPort = 5800,
    [int]$BaseCdpPort = 9923,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($NoBuild) {
    throw "F09 rollback evidence requires a fresh candidate build; -NoBuild is not supported."
}
if (-not [string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    throw "F09 rollback evidence must use the freshly built canonical Desktop executable."
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"

function Assert-CleanEvidenceWorktree {
    $changes = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the Git worktree before rollback evidence."
    }
    if ($changes.Count -ne 0) {
        throw "Rollback evidence requires a clean committed worktree; commit or remove local changes first."
    }
}

Assert-CleanEvidenceWorktree

$nodeExe = if ([string]::IsNullOrWhiteSpace($NodeExecutablePath)) {
    (Get-Command node.exe -ErrorAction Stop).Source
} else {
    [System.IO.Path]::GetFullPath($NodeExecutablePath)
}
$desktopExe = if ([string]::IsNullOrWhiteSpace($DesktopExecutablePath)) {
    Join-Path $repoRoot (
        "ClearVision.Product/src/ClearVision.Product.Desktop/bin/Debug/" +
        "net8.0-windows/win-x64/ClearVision.Product.Desktop.exe")
} else {
    [System.IO.Path]::GetFullPath($DesktopExecutablePath)
}
$sourceSha = (& git -C $repoRoot rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $sourceSha -notmatch '^[0-9a-f]{40}$') {
    throw "Could not resolve a 40-character source SHA for rollback evidence."
}
if (-not (Test-Path -LiteralPath $singleRun -PathType Leaf)) {
    throw "The StudioUI WebView2 evidence wrapper was not found: $singleRun"
}
if (-not (Test-Path -LiteralPath $nodeExe -PathType Leaf)) {
    throw "The Node evidence driver was not found: $nodeExe"
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "f09-rollback-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}
if ($BaseWebPort -lt 1 -or $BaseWebPort + 3 -gt 65535) {
    throw "BaseWebPort must leave room for four isolated restarts."
}
if ($BaseCdpPort -lt 1 -or $BaseCdpPort + 3 -gt 65535) {
    throw "BaseCdpPort must leave room for four isolated restarts."
}

$relativeEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/f09/rollback/$RunName"
} else {
    $EvidenceDirectory.Replace('\', '/')
}
if ([System.IO.Path]::IsPathRooted($relativeEvidenceRoot)) {
    throw "EvidenceDirectory must be repository-relative."
}
$evidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeEvidenceRoot))
$allowedEvidenceRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot ".tmp/studio-ui-next"))
$allowedEvidencePrefix = $allowedEvidenceRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $evidenceRoot.StartsWith(
    $allowedEvidencePrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Rollback evidence must remain under .tmp/studio-ui-next."
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "Rollback evidence root already exists; use a unique RunName: $evidenceRoot"
}
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

$isolationRoot = Join-Path $evidenceRoot "isolation"
$sharedRoot = Join-Path $isolationRoot "shared"
$databasePath = Join-Path $sharedRoot "vision.db"
$rollbackStatePath = Join-Path $sharedRoot "rollback-state.json"
New-Item -ItemType Directory -Force -Path $sharedRoot | Out-Null
$publishCheckBoundary = Join-Path $isolationRoot "publish-check"
$publishCheckRoot = Join-Path $publishCheckBoundary $RunName
$missingAssetsRuntime = Join-Path $publishCheckRoot "missing-assets-runtime"

function Remove-VerifiedTemporaryDirectory {
    param([string]$Path, [string]$AllowedRoot)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $resolved = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetFullPath($AllowedRoot)
    $prefix = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe temporary directory '$resolved'; expected a child of '$root'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$randomBytes = New-Object byte[] 24
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($randomBytes)
} finally {
    $random.Dispose()
}
$username = "admin"
$password = ([Convert]::ToBase64String($randomBytes) + "Aa1!")
$authMode = "ROLLBACK_SHARED_DATABASE_AUTHORITY"

$evidenceSummaryScript = @'
const fs = require('fs');
const evidence = JSON.parse(fs.readFileSync(process.argv[1], 'utf8'));
const ledger = evidence.productPage?.ownerLedger || null;
const legacy = evidence.legacy?.projection || null;
const missing = evidence.missingAssets || null;
const rootKind = legacy ? 'legacy' : missing ? 'diagnostic' : 'studio-ui';
const owners = legacy || missing ? {
  studioRootCount: 0,
  projectLifecycleOwnerCount: 0,
  leaveGuardOwnerCount: 0,
  workspaceOwnerCount: 0
} : {
  studioRootCount: Number(ledger?.studio?.mountCount ?? -1),
  projectLifecycleOwnerCount: Number(ledger?.projectLifecycle?.ownerCount ?? -1),
  leaveGuardOwnerCount: Number(ledger?.leaveGuard?.ownerCount ?? -1),
  workspaceOwnerCount: Number(ledger?.workspace?.workspaceOwnerCount ?? -1)
};
process.stdout.write(JSON.stringify({
  status: evidence.status,
  expectation: evidence.expectation,
  route: evidence.route ?? null,
  rootKind,
  owners,
  legacyProjection: legacy,
  missingAssets: missing,
  rollback: evidence.rollback ?? null,
  meaningfulConsoleErrorCount: Array.isArray(evidence.meaningfulConsoleErrors)
    ? evidence.meaningfulConsoleErrors.length : 0,
  pageErrorCount: Array.isArray(evidence.runtimeErrors?.pageErrors)
    ? evidence.runtimeErrors.pageErrors.length : 0,
  meaningfulRequestFailureCount: Array.isArray(evidence.meaningfulRequestFailures)
    ? evidence.meaningfulRequestFailures.length : 0
}));
'@

function Read-EvidenceSummary {
    param([string]$Path)

    $output = @(& $nodeExe -e $evidenceSummaryScript $Path)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not summarize WebView2 evidence: $Path"
    }
    return ([string]::Join([Environment]::NewLine, $output) | ConvertFrom-Json)
}

function Test-DatabaseExists {
    return Test-Path -LiteralPath $databasePath -PathType Leaf
}

function Test-DatabaseRemoved {
    return -not (@(
        $databasePath,
        ($databasePath + "-shm"),
        ($databasePath + "-wal")) | Where-Object { Test-Path -LiteralPath $_ })
}

function Remove-IsolatedDatabaseArtifacts {
    param([string]$Path)

    $resolvedPath = [System.IO.Path]::GetFullPath($Path)
    $resolvedRoot = [System.IO.Path]::GetFullPath($isolationRoot)
    $requiredPrefix = $resolvedRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($requiredPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove database artifacts outside F09 evidence root: $resolvedPath"
    }

    foreach ($candidate in @($resolvedPath, ($resolvedPath + "-shm"), ($resolvedPath + "-wal"))) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Remove-Item -LiteralPath $candidate -Force
        }
    }
}

function Assert-AuthorityShape {
    param([object]$Authority)

    if ([string]::IsNullOrWhiteSpace([string]$Authority.projectId) -or
        [string]::IsNullOrWhiteSpace([string]$Authority.flowId) -or
        [string]::IsNullOrWhiteSpace([string]$Authority.resultId) -or
        [string]::IsNullOrWhiteSpace([string]$Authority.executionSnapshotId) -or
        [string]::IsNullOrWhiteSpace([string]$Authority.flowHash) -or
        [string]::IsNullOrWhiteSpace([string]$Authority.decisionHash) -or
        [int64]$Authority.persistenceRevision -lt 1 -or
        [string]$Authority.reconciliationStatus -ne "succeeded" -or
        -not [bool]$Authority.historyContainsResult) {
        throw "Rollback authority is incomplete: $($Authority | ConvertTo-Json -Depth 8)"
    }
    if (-not [bool]$Authority.hasImage -or
        [string]::IsNullOrWhiteSpace([string]$Authority.imageId) -or
        $null -eq $Authority.imageReference) {
        throw "Rollback seed did not retain an image-backed formal Result."
    }
}

function Assert-AuthorityMatches {
    param(
        [object]$Expected,
        [object]$Actual,
        [string]$Phase
    )

    foreach ($field in @(
        "projectId",
        "projectName",
        "persistenceRevision",
        "flowId",
        "resultId",
        "resultProjectRevision",
        "executionSnapshotId",
        "flowHash",
        "decisionHash",
        "hasImage",
        "imageId",
        "evidenceStatus",
        "reconciliationStatus")) {
        if (-not [string]::Equals(
            [string]$Expected.$field,
            [string]$Actual.$field,
            [System.StringComparison]::Ordinal)) {
            throw "$Phase changed rollback authority field '$field'."
        }
    }
    $expectedImage = $Expected.imageReference | ConvertTo-Json -Compress -Depth 8
    $actualImage = $Actual.imageReference | ConvertTo-Json -Compress -Depth 8
    if (-not [string]::Equals($expectedImage, $actualImage, [System.StringComparison]::Ordinal) -or
        -not [bool]$Actual.historyContainsResult) {
        throw "$Phase changed Result image/history identity."
    }
}

$runRecords = [System.Collections.Generic.List[object]]::new()
$desktopBuilt = $false

function Invoke-RollbackRun {
    param(
        [string]$Name,
        [string]$Expectation,
        [string]$StartupProfile,
        [string]$RollbackPhase,
        [int]$PortOffset,
        [bool]$SeedWorkspace,
        [bool]$FormalRun,
        [bool]$KeepDatabase,
        [bool]$ReuseDatabase,
        [bool]$AllowInitialAdminSetup,
        [string]$Route,
        [string]$ExecutablePath,
        [string]$RuntimeKind
    )

    $webPort = $BaseWebPort + $PortOffset
    $cdpPort = $BaseCdpPort + $PortOffset
    $relativeEvidence = "$relativeEvidenceRoot/runs/$Name/evidence"
    $parameters = @{
        Expectation = $Expectation
        EvidencePhase = "f09"
        Configuration = "Debug"
        RuntimeKind = $RuntimeKind
        IsolationRoot = $isolationRoot
        DesktopExecutablePath = $ExecutablePath
        NodeExecutablePath = $nodeExe
        RunName = $Name
        EvidenceDirectory = $relativeEvidence
        WebPort = $webPort
        CdpPort = $cdpPort
        Scale = 1.0
        StartupProfile = $StartupProfile
        AuthMode = $authMode
        RollbackPhase = $RollbackPhase
        RollbackStatePath = $rollbackStatePath
        DatabasePath = $databasePath
        Username = $username
        Password = $password
        WindowWidth = 1600
        WindowHeight = 1000
    }
    if ($SeedWorkspace) { $parameters["SeedWorkspace"] = $true }
    if ($FormalRun) { $parameters["FormalRun"] = $true }
    if ($KeepDatabase) { $parameters["KeepDatabase"] = $true }
    if ($ReuseDatabase) { $parameters["ReuseDatabase"] = $true }
    if ($AllowInitialAdminSetup) { $parameters["AllowInitialAdminSetup"] = $true }
    $parameters["UnattendedShutdown"] = $true
    if (-not [string]::IsNullOrWhiteSpace($Route)) { $parameters["Route"] = $Route }
    if ($script:desktopBuilt) { $parameters["NoBuild"] = $true }

    & $singleRun @parameters | Out-Host
    $script:desktopBuilt = $true

    $evidencePath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name.json"
    $cleanupPath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name-cleanup.json"
    $cleanup = Get-Content -LiteralPath $cleanupPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $summary = Read-EvidenceSummary -Path $evidencePath
    $expectedPageKind = switch ($Expectation) {
        "legacy" { "Legacy" }
        "missing-assets" { "Diagnostic" }
        default { "StudioUi" }
    }
    $requiresRollbackAssertion = -not [string]::IsNullOrWhiteSpace($RollbackPhase)
    if (-not [bool]$cleanup.passed -or
        -not [bool]$cleanup.startupLog.passed -or
        [string]$cleanup.startupLog.record.profile -ne $StartupProfile -or
        [string]$cleanup.startupLog.record.pageKind -ne $expectedPageKind -or
        [string]$cleanup.startupLog.record.sourceSha -ne $sourceSha -or
        [string]$cleanup.startupLog.record.authMode -ne $authMode -or
        [string]$summary.status -ne "pass" -or
        ($requiresRollbackAssertion -and ($null -eq $summary.rollback -or
            [string]$summary.rollback.phase -ne $RollbackPhase -or
            -not [bool]$summary.rollback.matched)) -or
        [int]$summary.meaningfulConsoleErrorCount -ne 0 -or
        [int]$summary.pageErrorCount -ne 0 -or
        [int]$summary.meaningfulRequestFailureCount -ne 0) {
        throw "Rollback phase '$RollbackPhase' did not satisfy its startup/runtime contract."
    }
    if ($Expectation -eq "legacy") {
        if ([string]$summary.rootKind -ne "legacy" -or
            [int]$summary.owners.studioRootCount -ne 0 -or
            [string]$summary.legacyProjection.studioDiagnosticsType -ne "undefined") {
            throw "Legacy rollback phase mounted a Next root."
        }
    } elseif ($Expectation -eq "missing-assets") {
        if ([string]$summary.rootKind -ne "diagnostic" -or
            [int]$summary.owners.studioRootCount -ne 0 -or
            $null -eq $summary.missingAssets) {
            throw "Failure injection did not fail closed to the missing-assets diagnostic root."
        }
    } elseif ([string]$summary.rootKind -ne "studio-ui" -or
        [int]$summary.owners.studioRootCount -ne 1 -or
        [int]$summary.owners.projectLifecycleOwnerCount -ne 1 -or
        [int]$summary.owners.leaveGuardOwnerCount -ne 1 -or
        [int]$summary.owners.workspaceOwnerCount -ne 1) {
        throw "Next rollback phase did not retain the single Workspace owner chain."
    }

    $runRecord = [pscustomobject]@{
        name = $Name
        phase = $RollbackPhase
        expectation = $Expectation
        profile = $StartupProfile
        rootKind = $summary.rootKind
        ownerLedger = $summary.owners
        authority = if ($summary.rollback) { $summary.rollback.authority } else { $null }
        databaseKept = [bool]$cleanup.runtimeCleanup.databaseKept
        databaseReused = [bool]$cleanup.runtimeCleanup.databaseReused
        databaseStatePassed = [bool]$cleanup.runtimeCleanup.databaseStatePassed
        evidencePath = $evidencePath
        cleanupPath = $cleanupPath
        status = "PASS"
    }
    $runRecords.Add($runRecord)
    return $runRecord
}

$rollbackError = $null
$databaseCleanupError = $null
$state = $null
$failureInjection = $null
try {
    $nextCreate = Invoke-RollbackRun `
        -Name "next-create" `
        -Expectation "studio-product" `
        -StartupProfile "NEXT_DEFAULT_CANDIDATE" `
        -RollbackPhase "NEXT_CREATE" `
        -PortOffset 0 `
        -SeedWorkspace $true `
        -FormalRun $true `
        -KeepDatabase $true `
        -ReuseDatabase $false `
        -AllowInitialAdminSetup $true `
        -Route "" `
        -ExecutablePath $desktopExe `
        -RuntimeKind "debug"
    if (-not (Test-DatabaseExists) -or -not (Test-Path -LiteralPath $rollbackStatePath -PathType Leaf)) {
        throw "NEXT_CREATE did not retain the shared database and rollback state."
    }
    $state = Get-Content -LiteralPath $rollbackStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.schemaVersion -ne "f09-candidate-fallback-candidate-rollback.v1" -or
        [string]$state.sourceSha -ne $sourceSha) {
        throw "Rollback state schema/source identity is invalid."
    }
    Assert-AuthorityShape -Authority $state.authority
    Assert-AuthorityMatches -Expected $state.authority -Actual $nextCreate.authority -Phase "NEXT_CREATE"

    if (-not (Test-Path -LiteralPath $desktopExe -PathType Leaf)) {
        throw "The Desktop executable was not found after NEXT_CREATE: $desktopExe"
    }
    New-Item -ItemType Directory -Force -Path $publishCheckRoot | Out-Null
    Copy-Item -LiteralPath (Split-Path -Parent $desktopExe) `
        -Destination $missingAssetsRuntime -Recurse
    $studioAssets = Join-Path $missingAssetsRuntime "wwwroot/studio"
    if (-not (Test-Path -LiteralPath $studioAssets -PathType Container)) {
        throw "Failure injection sample did not start with a StudioUI asset root."
    }
    Remove-Item -LiteralPath $studioAssets -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $missingAssetsRuntime "wwwroot/index.html") -PathType Leaf)) {
        throw "Failure injection sample lost its Legacy asset root."
    }
    $failureInjection = Invoke-RollbackRun `
        -Name "candidate-missing-assets" `
        -Expectation "missing-assets" `
        -StartupProfile "NEXT_DEFAULT_CANDIDATE" `
        -RollbackPhase "" `
        -PortOffset 1 `
        -SeedWorkspace $false `
        -FormalRun $false `
        -KeepDatabase $true `
        -ReuseDatabase $true `
        -AllowInitialAdminSetup $false `
        -Route "" `
        -ExecutablePath (Join-Path $missingAssetsRuntime "ClearVision.Product.Desktop.exe") `
        -RuntimeKind "missing-assets"
    if (-not (Test-DatabaseExists) -or -not (Test-Path -LiteralPath $rollbackStatePath -PathType Leaf)) {
        throw "Failure injection changed the shared rollback authority state."
    }

    $legacyVerify = Invoke-RollbackRun `
        -Name "legacy-verify" `
        -Expectation "legacy" `
        -StartupProfile "LEGACY_FALLBACK" `
        -RollbackPhase "LEGACY_VERIFY" `
        -PortOffset 2 `
        -SeedWorkspace $false `
        -FormalRun $false `
        -KeepDatabase $true `
        -ReuseDatabase $true `
        -AllowInitialAdminSetup $false `
        -Route "" `
        -ExecutablePath $desktopExe `
        -RuntimeKind "debug"
    if (-not (Test-DatabaseExists)) {
        throw "LEGACY_VERIFY removed the shared database before the final restart."
    }
    Assert-AuthorityMatches -Expected $state.authority -Actual $legacyVerify.authority -Phase "LEGACY_VERIFY"

    $projectRoute = "/projects/$($state.authority.projectId)/workspace"
    $nextReopen = Invoke-RollbackRun `
        -Name "next-reopen" `
        -Expectation "studio-product" `
        -StartupProfile "NEXT_DEFAULT_CANDIDATE" `
        -RollbackPhase "NEXT_REOPEN" `
        -PortOffset 3 `
        -SeedWorkspace $false `
        -FormalRun $false `
        -KeepDatabase $false `
        -ReuseDatabase $true `
        -AllowInitialAdminSetup $false `
        -Route $projectRoute `
        -ExecutablePath $desktopExe `
        -RuntimeKind "debug"
    Assert-AuthorityMatches -Expected $state.authority -Actual $nextReopen.authority -Phase "NEXT_REOPEN"
    if (-not (Test-DatabaseRemoved)) {
        throw "NEXT_REOPEN did not clean the isolated shared database artifacts."
    }
} catch {
    $rollbackError = $_
} finally {
    try {
        Remove-IsolatedDatabaseArtifacts -Path $databasePath
    } catch {
        $databaseCleanupError = $_
    }
    try {
        Remove-VerifiedTemporaryDirectory -Path $missingAssetsRuntime -AllowedRoot $publishCheckRoot
        Remove-VerifiedTemporaryDirectory -Path $publishCheckRoot -AllowedRoot $publishCheckBoundary
    } catch {
        if ($null -eq $databaseCleanupError) {
            $databaseCleanupError = $_
        }
    }
}

if ($null -eq $rollbackError -and $null -ne $databaseCleanupError) {
    $rollbackError = $databaseCleanupError
}

$phaseCountPassed = $runRecords.Count -eq 4
$failureInjectionPassed = $null -ne $failureInjection -and $failureInjection.rootKind -eq "diagnostic"
$authorityPassed = -not $rollbackError -and $null -ne $state
$databaseCleanupPassed = Test-DatabaseRemoved
$manifestPassed = -not $rollbackError -and $phaseCountPassed -and
    $authorityPassed -and $failureInjectionPassed -and $databaseCleanupPassed
$manifest = [pscustomobject]@{
    schemaVersion = 1
    evidenceKind = "F09_G6_CANDIDATE_FALLBACK_CANDIDATE_ROLLBACK"
    sourceSha = $sourceSha
    runName = $RunName
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($manifestPassed) { "PASS" } else { "FAIL" }
    error = if ($rollbackError) { $rollbackError.Exception.Message } else { $null }
    cleanup = [pscustomobject]@{
        attempted = $true
        error = if ($databaseCleanupError) { $databaseCleanupError.Exception.Message } else { $null }
        sharedDatabaseRemoved = $databaseCleanupPassed
    }
    sequence = @(
        "NEXT_DEFAULT_CANDIDATE",
        "MISSING_ASSETS_FAILURE_INJECTION",
        "LEGACY_FALLBACK",
        "NEXT_DEFAULT_CANDIDATE")
    failureInjection = [pscustomobject]@{
        kind = "missing-studio-assets"
        profile = "NEXT_DEFAULT_CANDIDATE"
        passed = $failureInjectionPassed
        rootKind = if ($failureInjection) { $failureInjection.rootKind } else { $null }
    }
    sharedDatabase = [pscustomobject]@{
        explicitPath = $databasePath
        reusedAcrossRestarts = $phaseCountPassed
        migrationPerformedByHarness = $false
        dualWrite = $false
        removedAfterEvidence = $databaseCleanupPassed
    }
    authenticatedUser = if ($state) { $state.user } else { $null }
    runIdentity = if ($state) { $state.runIdentity } else { $null }
    authority = if ($state) { $state.authority } else { $null }
    identityChecks = [pscustomobject]@{
        projectId = $authorityPassed
        persistenceRevision = $authorityPassed
        flowId = $authorityPassed
        resultId = $authorityPassed
        executionSnapshotId = $authorityPassed
        flowHash = $authorityPassed
        decisionHash = $authorityPassed
        imageReference = $authorityPassed
        historyContainsResult = $authorityPassed
    }
    ownerLedger = [pscustomobject]@{
        oneMountedRootPerRestart = $phaseCountPassed
        legacyNextOwnerCount = 0
        nextWorkspaceOwnerCount = 1
    }
    rollbackStatePath = $rollbackStatePath
    runs = @($runRecords)
}
$manifestPath = Join-Path $evidenceRoot "studio-ui-rollback-evidence.json"
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if ($rollbackError) {
    throw $rollbackError
}
if (-not $manifestPassed) {
    throw "StudioUI F09 G6 rollback evidence did not satisfy every gate: $manifestPath"
}

$manifest | ConvertTo-Json -Depth 8

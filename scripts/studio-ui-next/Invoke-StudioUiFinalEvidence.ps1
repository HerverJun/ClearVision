[CmdletBinding()]
param(
    [string]$NodeExecutablePath,
    [string]$DesktopExecutablePath,
    [string]$RunName,
    [string]$EvidenceDirectory,
    [int]$BaseWebPort = 6000,
    [int]$BaseCdpPort = 10123,
    [int]$SoakCycles = 20,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot "../.."))
$singleRun = Join-Path $scriptRoot "Invoke-StudioUiWebView2Evidence.ps1"
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
    throw "Could not resolve a 40-character source SHA for F04 G6 evidence."
}
if (-not (Test-Path -LiteralPath $singleRun -PathType Leaf)) {
    throw "The StudioUI WebView2 evidence wrapper was not found: $singleRun"
}
if (-not (Test-Path -LiteralPath $nodeExe -PathType Leaf)) {
    throw "The Node evidence driver was not found: $nodeExe"
}
if ($SoakCycles -lt 20) {
    throw "SoakCycles must be at least 20."
}
if ($BaseWebPort -lt 1 -or $BaseWebPort + 2 -gt 65535 -or
    $BaseCdpPort -lt 1 -or $BaseCdpPort + 2 -gt 65535) {
    throw "Base ports must leave room for three isolated Desktop processes."
}

if ([string]::IsNullOrWhiteSpace($RunName)) {
    $RunName = "g6-final-{0}" -f [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss-fff")
}
$RunName = ($RunName -replace '[^A-Za-z0-9_.-]+', '-').Trim('-')
if ([string]::IsNullOrWhiteSpace($RunName)) {
    throw "RunName must contain at least one safe filename character."
}

$relativeEvidenceRoot = if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    ".tmp/studio-ui-next/f04/final/$RunName"
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
    throw "F04 G6 final evidence must remain under .tmp/studio-ui-next."
}
if (Test-Path -LiteralPath $evidenceRoot) {
    throw "F04 G6 evidence root already exists; use a unique RunName: $evidenceRoot"
}
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

$journeySharedRoot = Join-Path $evidenceRoot "journey-shared"
$journeyDatabasePath = Join-Path $journeySharedRoot "vision.db"
$journeyStatePath = Join-Path $journeySharedRoot "final-journey-state.json"
$soakSharedRoot = Join-Path $evidenceRoot "soak-shared"
$soakDatabasePath = Join-Path $soakSharedRoot "vision.db"
New-Item -ItemType Directory -Force -Path $journeySharedRoot,$soakSharedRoot | Out-Null

$randomBytes = New-Object byte[] 24
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
try {
    $random.GetBytes($randomBytes)
} finally {
    $random.Dispose()
}
$username = "f04g6admin"
$password = ([Convert]::ToBase64String($randomBytes) + "Aa1!")
$runRecords = [System.Collections.Generic.List[object]]::new()
$desktopBuilt = [bool]$NoBuild

function Test-DatabaseRemoved {
    param([string]$Path)

    return -not (@(
        $Path,
        ($Path + "-shm"),
        ($Path + "-wal")) | Where-Object { Test-Path -LiteralPath $_ })
}

function Invoke-FinalRun {
    param(
        [string]$Name,
        [string]$FinalJourneyPhase,
        [string]$AuthMode,
        [string]$DatabasePath,
        [int]$PortOffset,
        [bool]$KeepDatabase,
        [bool]$ReuseDatabase,
        [bool]$AllowInitialAdminSetup,
        [string]$FinalJourneyStatePath,
        [int]$Cycles
    )

    $relativeEvidence = "$relativeEvidenceRoot/runs/$Name/evidence"
    $parameters = @{
        Expectation = "studio-product"
        EvidencePhase = "f04"
        Configuration = "Debug"
        RuntimeKind = "debug"
        DesktopExecutablePath = $desktopExe
        NodeExecutablePath = $nodeExe
        RunName = $Name
        EvidenceDirectory = $relativeEvidence
        WebPort = $BaseWebPort + $PortOffset
        CdpPort = $BaseCdpPort + $PortOffset
        Scale = 1.0
        WindowWidth = 1600
        WindowHeight = 1000
        WorkspaceCapabilityEnabled = $true
        StartupProfile = "NEXT_PILOT"
        AuthMode = $AuthMode
        DatabasePath = $DatabasePath
        Username = $username
        Password = $password
        FinalJourneyPhase = $FinalJourneyPhase
        SoakCycles = $Cycles
        DeferAuthToScenario = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($FinalJourneyStatePath)) {
        $parameters["FinalJourneyStatePath"] = $FinalJourneyStatePath
    }
    if ($KeepDatabase) { $parameters["KeepDatabase"] = $true }
    if ($ReuseDatabase) { $parameters["ReuseDatabase"] = $true }
    if ($AllowInitialAdminSetup) { $parameters["AllowInitialAdminSetup"] = $true }
    if ($script:desktopBuilt) { $parameters["NoBuild"] = $true }

    & $singleRun @parameters | Out-Host
    $script:desktopBuilt = $true

    $evidencePath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name.json"
    $cleanupPath = Join-Path $repoRoot "$relativeEvidence/studio-ui-webview2-$Name-cleanup.json"
    $evidence = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $cleanup = Get-Content -LiteralPath $cleanupPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$evidence.status -ne "pass" -or
        [string]$evidence.sourceSha -ne $sourceSha -or
        [string]$evidence.finalJourney.phase -ne $FinalJourneyPhase -or
        @($evidence.meaningfulConsoleErrors).Count -ne 0 -or
        @($evidence.runtimeErrors.pageErrors).Count -ne 0 -or
        @($evidence.meaningfulRequestFailures).Count -ne 0 -or
        -not [bool]$cleanup.passed -or
        -not [bool]$cleanup.startupLog.passed -or
        [string]$cleanup.startupLog.record.profile -ne "NEXT_PILOT" -or
        [string]$cleanup.startupLog.record.sourceSha -ne $sourceSha -or
        [string]$cleanup.startupLog.record.authMode -ne $AuthMode -or
        -not [bool]$cleanup.authenticationDeferredToScenario) {
        throw "F04 G6 run '$Name' did not satisfy its final-SHA runtime contract."
    }

    $record = [pscustomobject]@{
        name = $Name
        phase = $FinalJourneyPhase
        authMode = $AuthMode
        status = "PASS"
        sourceSha = [string]$evidence.sourceSha
        requestAudit = $evidence.finalJourney.requestAudit
        finalJourney = $evidence.finalJourney
        nativeRuntime = $evidence.nativeRuntime
        browserDpi = $evidence.browserDpi
        databaseKept = [bool]$cleanup.runtimeCleanup.databaseKept
        databaseReused = [bool]$cleanup.runtimeCleanup.databaseReused
        databaseStatePassed = [bool]$cleanup.runtimeCleanup.databaseStatePassed
        evidencePath = $evidencePath
        cleanupPath = $cleanupPath
    }
    $runRecords.Add($record)
    return $record
}

$finalError = $null
$state = $null
try {
    $first = Invoke-FinalRun `
        -Name "create-run-logout" `
        -FinalJourneyPhase "CREATE_RUN_LOGOUT" `
        -AuthMode "UI_SETUP_AUTO_LOGIN" `
        -DatabasePath $journeyDatabasePath `
        -PortOffset 0 `
        -KeepDatabase $true `
        -ReuseDatabase $false `
        -AllowInitialAdminSetup $true `
        -FinalJourneyStatePath $journeyStatePath `
        -Cycles 0
    if (-not (Test-Path -LiteralPath $journeyDatabasePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $journeyStatePath -PathType Leaf)) {
        throw "CREATE_RUN_LOGOUT did not retain the shared database and journey state."
    }
    $state = Get-Content -LiteralPath $journeyStatePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$state.schemaVersion -ne "f04-g6-final-journey.v1" -or
        [string]$state.sourceSha -ne $sourceSha -or
        [string]$state.user.username -ne $username -or
        [string]::IsNullOrWhiteSpace([string]$state.authority.projectId) -or
        [string]::IsNullOrWhiteSpace([string]$state.authority.resultId)) {
        throw "CREATE_RUN_LOGOUT produced an incomplete final-journey state."
    }

    $second = Invoke-FinalRun `
        -Name "reopen-delete" `
        -FinalJourneyPhase "REOPEN_DELETE" `
        -AuthMode "UI_LOGIN_AFTER_RESTART" `
        -DatabasePath $journeyDatabasePath `
        -PortOffset 1 `
        -KeepDatabase $false `
        -ReuseDatabase $true `
        -AllowInitialAdminSetup $false `
        -FinalJourneyStatePath $journeyStatePath `
        -Cycles 0
    if (-not (Test-DatabaseRemoved -Path $journeyDatabasePath) -or
        [int]$second.finalJourney.notFound.detailStatus -ne 404 -or
        [int]$second.finalJourney.notFound.openStatus -ne 404 -or
        [bool]$second.finalJourney.notFound.listVisible) {
        throw "REOPEN_DELETE did not close tombstone/not-found/database cleanup evidence."
    }

    $soak = Invoke-FinalRun `
        -Name "soak-$SoakCycles" `
        -FinalJourneyPhase "SOAK" `
        -AuthMode "UI_AUTH_RUN_LOGOUT_SOAK" `
        -DatabasePath $soakDatabasePath `
        -PortOffset 2 `
        -KeepDatabase $false `
        -ReuseDatabase $false `
        -AllowInitialAdminSetup $true `
        -FinalJourneyStatePath "" `
        -Cycles $SoakCycles
    if (-not (Test-DatabaseRemoved -Path $soakDatabasePath) -or
        [int]$soak.finalJourney.cycleCount -ne $SoakCycles -or
        [int]$soak.finalJourney.uniqueResultCount -ne $SoakCycles -or
        @($soak.finalJourney.cycles).Count -ne $SoakCycles -or
        -not [bool]$soak.finalJourney.gcGate -or
        -not [bool]$soak.finalJourney.weakReferenceGate -or
        -not [bool]$soak.finalJourney.postSoakDisposalSettle.allTrackedReferencesCollected -or
        @($soak.finalJourney.trends.PSObject.Properties.Value | Where-Object { -not [bool]$_.passed }).Count -ne 0) {
        throw "SOAK did not close the 20-cycle identity/resource/memory evidence."
    }
} catch {
    $finalError = $_
}

$journeyDatabaseRemoved = Test-DatabaseRemoved -Path $journeyDatabasePath
$soakDatabaseRemoved = Test-DatabaseRemoved -Path $soakDatabasePath
$runCountPassed = $runRecords.Count -eq 3
$manifestPassed = -not $finalError -and $runCountPassed -and
    $journeyDatabaseRemoved -and $soakDatabaseRemoved -and $null -ne $state
$manifest = [pscustomobject]@{
    schemaVersion = 1
    evidenceKind = "F04_G6_FINAL_USER_JOURNEY_AND_SOAK"
    sourceSha = $sourceSha
    runName = $RunName
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    status = if ($manifestPassed) { "PASS" } else { "FAIL" }
    error = if ($finalError) { $finalError.Exception.Message } else { $null }
    desktopProcesses = $runRecords.Count
    finalUserJourney = [pscustomobject]@{
        restarts = 2
        freshDatabase = $true
        uiSetupAutoLogin = $runCountPassed
        createResponseLossReconciled = if ($runRecords.Count -ge 1) {
            [int]$runRecords[0].requestAudit.operationGets -eq 1
        } else { $false }
        sameDatabaseReusedAfterRestart = if ($runRecords.Count -ge 2) {
            [bool]$runRecords[1].databaseReused
        } else { $false }
        sameUserIdentity = if ($state) { [string]$state.user.username -eq $username } else { $false }
        deleteResponseLossReconciled = if ($runRecords.Count -ge 2) {
            [int]$runRecords[1].requestAudit.operationGets -eq 1
        } else { $false }
        tombstoneNotFound = if ($runRecords.Count -ge 2) {
            [int]$runRecords[1].finalJourney.notFound.detailStatus -eq 404 -and
            [int]$runRecords[1].finalJourney.notFound.openStatus -eq 404 -and
            -not [bool]$runRecords[1].finalJourney.notFound.listVisible
        } else { $false }
        databaseRemovedAfterEvidence = $journeyDatabaseRemoved
        statePath = $journeyStatePath
    }
    soak = if ($runRecords.Count -ge 3) {
        [pscustomobject]@{
            requestedCycles = $SoakCycles
            completedCycles = [int]$runRecords[2].finalJourney.cycleCount
            uniqueResultCount = [int]$runRecords[2].finalJourney.uniqueResultCount
            primaryLeakGate = [string]$runRecords[2].finalJourney.primaryLeakGate
            gcGate = [bool]$runRecords[2].finalJourney.gcGate
            weakReferenceGate = [bool]$runRecords[2].finalJourney.weakReferenceGate
            postSoakDisposalSettle = $runRecords[2].finalJourney.postSoakDisposalSettle
            trends = $runRecords[2].finalJourney.trends
            logoutCurrentTreeObservation = $runRecords[2].finalJourney.logoutCurrentTreeObservation
            diagnosticArtifact = [string]$runRecords[2].finalJourney.diagnosticArtifact
            requestAudit = $runRecords[2].requestAudit
            databaseRemovedAfterEvidence = $soakDatabaseRemoved
            passed = [int]$runRecords[2].finalJourney.cycleCount -eq $SoakCycles -and
                [int]$runRecords[2].finalJourney.uniqueResultCount -eq $SoakCycles -and
                [bool]$runRecords[2].finalJourney.gcGate -and
                [bool]$runRecords[2].finalJourney.weakReferenceGate -and
                [bool]$runRecords[2].finalJourney.postSoakDisposalSettle.allTrackedReferencesCollected -and
                @($runRecords[2].finalJourney.trends.PSObject.Properties.Value |
                    Where-Object { -not [bool]$_.passed }).Count -eq 0 -and
                $soakDatabaseRemoved
        }
    } else { $null }
    runs = @($runRecords)
}
$manifestPath = Join-Path $evidenceRoot "studio-ui-final-evidence.json"
[System.IO.File]::WriteAllText(
    $manifestPath,
    (($manifest | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

if ($finalError) {
    throw $finalError
}
if (-not $manifestPassed) {
    throw "F04 G6 final evidence did not satisfy every gate: $manifestPath"
}

$manifest | ConvertTo-Json -Depth 10

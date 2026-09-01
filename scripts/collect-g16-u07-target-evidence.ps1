[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageZip,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedPackageSha256,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{40,64}$')][string]$GitSha,
    [Parameter(Mandatory = $true)][bool]$GitDirty,
    [Parameter(Mandatory = $true)][string]$TargetSku,
    [Parameter(Mandatory = $true)][string]$TargetProfile,
    [Parameter(Mandatory = $true)][ValidateSet('formal-sku', 'experimental')][string]$ProfileClass,
    [Parameter(Mandatory = $true)][ValidateRange(1, 32768)][int]$ResolutionWidth,
    [Parameter(Mandatory = $true)][ValidateRange(1, 32768)][int]$ResolutionHeight,
    [Parameter(Mandatory = $true)][ValidateRange(50, 500)][int]$OsScalePercent,
    [Parameter(Mandatory = $true)][string]$OperatorProfile,
    [Parameter(Mandatory = $true)][string]$ModelProfile,
    [Parameter(Mandatory = $true)][string]$DeviceProfile,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$ObservationPath = '',
    [string[]]$ScreenshotPath = @(),
    [string[]]$LogPath = @(),
    [switch]$Fixture
)

$ErrorActionPreference = 'Stop'

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

function Read-JsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-OptionalProperty {
    param([object]$Value, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Value) { return $null }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Assert-NonEmpty {
    param([Parameter(Mandatory = $true)][string]$Name, [object]$Value)
    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$Name must not be empty."
    }
}

function Assert-ExecutionStatus {
    param([Parameter(Mandatory = $true)][string]$Name, [object]$Value)
    $allowed = @('NOT_RUN', 'PASS', 'FAIL', 'BLOCKED', 'INCONCLUSIVE')
    $status = [string](Get-OptionalProperty -Value $Value -Name 'status')
    if ($allowed -notcontains $status) {
        throw "$Name.status must be one of: $($allowed -join ', ')."
    }
}

function New-NotRunExecution {
    return [ordered]@{
        startupAndHealth = [ordered]@{
            status = 'NOT_RUN'
            launched = $null
            healthEndpoint = $null
            healthStatusCode = $null
            healthy = $null
            notes = $null
            evidenceRefs = @()
        }
        noNodeVerification = [ordered]@{
            status = 'NOT_RUN'
            nodeAbsentBeforeLaunch = $null
            launched = $null
            healthy = $null
            notes = $null
            evidenceRefs = @()
        }
        performance = @(
            [ordered]@{
                primitiveCount = 300
                status = 'NOT_RUN'
                sampleCount = 0
                inputToPaintMs = [ordered]@{ p50 = $null; p95 = $null }
                rafFrameMs = [ordered]@{ p50 = $null; p95 = $null; longFrameThresholdMs = 16.7; longFrameCount = $null }
                notes = $null
                evidenceRefs = @()
            },
            [ordered]@{
                primitiveCount = 1000
                status = 'NOT_RUN'
                sampleCount = 0
                inputToPaintMs = [ordered]@{ p50 = $null; p95 = $null }
                rafFrameMs = [ordered]@{ p50 = $null; p95 = $null; longFrameThresholdMs = 16.7; longFrameCount = $null }
                notes = $null
                evidenceRefs = @()
            }
        )
        workingSet = [ordered]@{
            status = 'NOT_RUN'
            sampleCount = 0
            p50MiB = $null
            p95MiB = $null
            peakMiB = $null
            notes = $null
            evidenceRefs = @()
        }
        workflows = [ordered]@{
            projectSaveLoad = [ordered]@{ status = 'NOT_RUN'; notes = $null; evidenceRefs = @() }
            legacyProjectAndPackage = [ordered]@{ status = 'NOT_RUN'; notes = $null; evidenceRefs = @() }
            stationReconnectAndResult = [ordered]@{ status = 'NOT_RUN'; notes = $null; evidenceRefs = @() }
            agentWorkspace = [ordered]@{ status = 'NOT_RUN'; notes = $null; evidenceRefs = @() }
        }
    }
}

function Get-WebView2Runtime {
    $roots = @(
        'HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients',
        'HKCU:\SOFTWARE\Microsoft\EdgeUpdate\Clients'
    )
    $runtimeRows = @()
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
            $value = Get-ItemProperty -LiteralPath $key.PSPath -ErrorAction SilentlyContinue
            $name = [string]($value.name ?? $value.DisplayName)
            if ($name -match '(?i)WebView2') {
                $runtimeRows += [ordered]@{
                    name = $name
                    version = [string]($value.pv ?? $value.Version)
                    scope = if ($root.StartsWith('HKCU:', [StringComparison]::OrdinalIgnoreCase)) { 'user' } else { 'machine' }
                }
            }
        }
    }
    return @($runtimeRows | Sort-Object name, version -Unique)
}

function Get-MachineEnvironment {
    $os = $null
    $gpus = @()
    try { $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop } catch { $os = $null }
    try {
        $gpus = @(Get-CimInstance Win32_VideoController -ErrorAction Stop | ForEach-Object {
            [ordered]@{
                name = [string]$_.Name
                driverVersion = [string]$_.DriverVersion
                driverDate = if ($null -eq $_.DriverDate) { $null } else { ([DateTimeOffset]$_.DriverDate).ToString('o') }
                adapterRamBytes = if ($null -eq $_.AdapterRAM) { $null } else { [long]$_.AdapterRAM }
            }
        })
    } catch { $gpus = @() }

    $node = Get-Command node -ErrorAction SilentlyContinue
    $nodeVersion = $null
    if ($null -ne $node) {
        try { $nodeVersion = (& $node.Source --version 2>$null | Select-Object -First 1) } catch { $nodeVersion = $null }
    }

    return [ordered]@{
        machineName = [Environment]::MachineName
        os = [ordered]@{
            caption = if ($null -eq $os) { [Environment]::OSVersion.VersionString } else { [string]$os.Caption }
            version = if ($null -eq $os) { [Environment]::OSVersion.Version.ToString() } else { [string]$os.Version }
            build = if ($null -eq $os) { [Environment]::OSVersion.Version.Build.ToString() } else { [string]$os.BuildNumber }
            architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        }
        display = [ordered]@{
            resolutionWidth = $ResolutionWidth
            resolutionHeight = $ResolutionHeight
            osScalePercent = $OsScalePercent
            scaleWasModifiedByCollector = $false
            source = 'operator-supplied-and-target-bound'
        }
        webView2Runtimes = @(Get-WebView2Runtime)
        gpu = $gpus
        node = [ordered]@{
            present = $null -ne $node
            version = if ([string]::IsNullOrWhiteSpace([string]$nodeVersion)) { $null } else { [string]$nodeVersion }
        }
    }
}

function Copy-EvidenceFiles {
    param(
        [string[]]$Paths,
        [Parameter(Mandatory = $true)][ValidateSet('screenshot', 'log')][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Root
    )
    $rows = @()
    $index = 0
    foreach ($path in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $resolved = (Resolve-Path -LiteralPath $path).Path
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) { throw "Evidence file is not a file: $path" }
        $index += 1
        $folder = Join-Path $Root "attachments\$($Kind)s"
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        $leaf = [IO.Path]::GetFileName($resolved)
        $target = Join-Path $folder ('{0:D3}-{1}' -f $index, $leaf)
        Copy-Item -LiteralPath $resolved -Destination $target
        $relative = [IO.Path]::GetRelativePath($Root, $target).Replace('\', '/')
        $rows += [ordered]@{
            kind = $Kind
            path = $relative
            sha256 = Get-Sha256 $target
            sizeBytes = (Get-Item -LiteralPath $target).Length
        }
    }
    return $rows
}

foreach ($pair in @(
    @{ Name = 'TargetSku'; Value = $TargetSku },
    @{ Name = 'TargetProfile'; Value = $TargetProfile },
    @{ Name = 'OperatorProfile'; Value = $OperatorProfile },
    @{ Name = 'ModelProfile'; Value = $ModelProfile },
    @{ Name = 'DeviceProfile'; Value = $DeviceProfile }
)) {
    Assert-NonEmpty -Name $pair.Name -Value $pair.Value
}

$resolvedPackage = (Resolve-Path -LiteralPath $PackageZip).Path
if (-not (Test-Path -LiteralPath $resolvedPackage -PathType Leaf)) { throw "Package ZIP is not a file: $PackageZip" }
$actualPackageSha = Get-Sha256 $resolvedPackage
if ($actualPackageSha -ne $ExpectedPackageSha256.ToLowerInvariant()) {
    throw "Portable ZIP SHA-256 mismatch. Expected $($ExpectedPackageSha256.ToLowerInvariant()), actual $actualPackageSha."
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath (Join-Path $resolvedOutput 'evidence.json')) {
    throw "Output already contains evidence.json; choose a new profile/run directory: $resolvedOutput"
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$execution = New-NotRunExecution
$profileClosure = [ordered]@{
    status = 'NOT_RUN'
    independentProfile = $true
    releaseBlocking = $ProfileClass -eq 'formal-sku'
    conclusion = $null
    blockedCapabilities = @()
}
$signOff = [ordered]@{
    operatorIdentity = $null
    reviewer = $null
    deviceSerialNumber = $null
    approvalDecision = $null
    signedAtUtc = $null
}

if (-not [string]::IsNullOrWhiteSpace($ObservationPath)) {
    $resolvedObservation = (Resolve-Path -LiteralPath $ObservationPath).Path
    $observation = Read-JsonFile $resolvedObservation
    if ([string](Get-OptionalProperty $observation 'schemaVersion') -ne 'clearvision.g16-u07-target-observation/v1') {
        throw 'Observation schemaVersion is not clearvision.g16-u07-target-observation/v1.'
    }
    $observedExecution = Get-OptionalProperty $observation 'execution'
    if ($null -eq $observedExecution) { throw 'Observation execution is required.' }
    $execution = $observedExecution
    $observedClosure = Get-OptionalProperty $observation 'profileClosure'
    if ($null -ne $observedClosure) {
        $closureStatus = [string](Get-OptionalProperty $observedClosure 'status')
        if (@('NOT_RUN', 'OPEN', 'CLOSED_PASS', 'CLOSED_BLOCKED') -notcontains $closureStatus) {
            throw 'profileClosure.status is invalid.'
        }
        $profileClosure.status = $closureStatus
        $profileClosure.conclusion = Get-OptionalProperty $observedClosure 'conclusion'
        $blockedCapabilities = Get-OptionalProperty $observedClosure 'blockedCapabilities'
        $profileClosure.blockedCapabilities = @()
        if ($null -ne $blockedCapabilities) {
            $profileClosure.blockedCapabilities = @($blockedCapabilities)
        }
    }
    $observedSignOff = Get-OptionalProperty $observation 'signOff'
    if ($null -ne $observedSignOff) {
        foreach ($field in @('operatorIdentity', 'reviewer', 'deviceSerialNumber', 'approvalDecision', 'signedAtUtc')) {
            $signOff[$field] = Get-OptionalProperty $observedSignOff $field
        }
    }
}

Assert-ExecutionStatus -Name 'startupAndHealth' -Value $execution.startupAndHealth
Assert-ExecutionStatus -Name 'noNodeVerification' -Value $execution.noNodeVerification
Assert-ExecutionStatus -Name 'workingSet' -Value $execution.workingSet
foreach ($row in @($execution.performance)) { Assert-ExecutionStatus -Name "performance[$($row.primitiveCount)]" -Value $row }
foreach ($name in @('projectSaveLoad', 'legacyProjectAndPackage', 'stationReconnectAndResult', 'agentWorkspace')) {
    Assert-ExecutionStatus -Name "workflows.$name" -Value (Get-OptionalProperty $execution.workflows $name)
}
$counts = @($execution.performance | ForEach-Object { [int]$_.primitiveCount } | Sort-Object)
if (($counts -join ',') -ne '300,1000') { throw 'Observation performance must contain exactly the 300 and 1000 primitive profiles.' }

if ($Fixture -and @($signOff.Values | Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace([string]$_) }).Count -gt 0) {
    throw 'Fixture evidence cannot contain operator/reviewer/device serial/approval sign-off values.'
}

$evidenceFiles = @()
$evidenceFiles += @(Copy-EvidenceFiles -Paths $ScreenshotPath -Kind screenshot -Root $resolvedOutput)
$evidenceFiles += @(Copy-EvidenceFiles -Paths $LogPath -Kind log -Root $resolvedOutput)
$generatedAt = [DateTimeOffset]::UtcNow.ToString('o')
$report = [ordered]@{
    schemaVersion = 'clearvision.g16-u07-target-evidence/v1'
    generatedAtUtc = $generatedAt
    fixture = [bool]$Fixture
    evidenceClass = if ($Fixture) { 'FIXTURE' } else { 'TARGET_MACHINE' }
    releaseEligible = $false
    artifactBinding = [ordered]@{
        portableZip = [ordered]@{
            fileName = [IO.Path]::GetFileName($resolvedPackage)
            sha256 = $actualPackageSha
            sizeBytes = (Get-Item -LiteralPath $resolvedPackage).Length
        }
        gitSha = $GitSha.ToLowerInvariant()
        gitDirty = $GitDirty
    }
    target = [ordered]@{
        sku = $TargetSku
        profile = $TargetProfile
        profileClass = $ProfileClass
        operatorProfile = $OperatorProfile
        modelProfile = $ModelProfile
        deviceProfile = $DeviceProfile
    }
    environment = Get-MachineEnvironment
    execution = $execution
    evidenceFiles = $evidenceFiles
    profileClosure = $profileClosure
    signOff = $signOff
    nonExecutedReleaseBoundaries = @(
        'real_tag_release',
        'same_sha_ci',
        'complete_target_matrix',
        'github_release',
        'core20_human_review'
    )
}

$evidencePath = Join-Path $resolvedOutput 'evidence.json'
$reportJson = $null
try {
    $reportJson = $report | ConvertTo-Json -Depth 30
}
catch {
    foreach ($key in $report.Keys) {
        try { $report[$key] | ConvertTo-Json -Depth 30 | Out-Null }
        catch { throw "Evidence field '$key' is not JSON serializable: $($_.Exception.Message)" }
    }
    throw
}
Write-Utf8NoBom -Path $evidencePath -Content ($reportJson + "`n")

$checksumRows = @()
$checksumRows += [ordered]@{ path = 'evidence.json'; sha256 = Get-Sha256 $evidencePath }
foreach ($row in $evidenceFiles) { $checksumRows += [ordered]@{ path = $row.path; sha256 = $row.sha256 } }
$checksumText = ($checksumRows | Sort-Object path | ForEach-Object { "$($_.sha256)  $($_.path)" }) -join "`n"
Write-Utf8NoBom -Path (Join-Path $resolvedOutput 'SHA256SUMS') -Content ($checksumText + "`n")

[ordered]@{
    evidencePath = $evidencePath
    evidenceSha256 = Get-Sha256 $evidencePath
    checksumPath = Join-Path $resolvedOutput 'SHA256SUMS'
    fixture = [bool]$Fixture
    profile = $TargetProfile
    releaseEligible = $false
} | ConvertTo-Json -Compress | Write-Output

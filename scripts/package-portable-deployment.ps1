[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("Studio", "Station")]
    [string]$Application = "Studio",
    [ValidateSet("win-x64")]
    [string]$RuntimeIdentifier = "win-x64",
    [ValidateSet("field-self-contained", "diagnostic-framework-dependent")]
    [string]$Profile = "field-self-contained",
    [string]$Version = "",
    [string]$SourceRevisionId = "",
    [string]$OutputRoot = "",
    [string]$VulnerabilityReportPath = "",
    [switch]$NoRestore,
    [switch]$RunOperatorSmoke,
    [switch]$AttemptVulnerabilityScan,
    [switch]$SkipOperatorPackage,
    [switch]$SkipSupplyChain,
    [switch]$EnforceReleasePolicy,
    [switch]$KeepStaging
)

$ErrorActionPreference = "Stop"

function Ensure-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Resolve-OutputPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Value
    )
    if ([System.IO.Path]::IsPathRooted($Value)) {
        return [System.IO.Path]::GetFullPath($Value)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Value))
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string]$Parent,
        [Parameter(Mandatory = $true)][string]$Child
    )
    $parentFull = [System.IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $childFull = [System.IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to mutate a path outside the configured output root: $childFull"
    }
}

function Remove-SafePath {
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$Target
    )
    Assert-ChildPath -Parent $OutputRoot -Child $Target
    if (Test-Path -LiteralPath $Target) {
        Remove-Item -LiteralPath $Target -Recurse -Force
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )
    return [System.IO.Path]::GetRelativePath($Root, $Path).Replace("\", "/")
}

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Value,
        [int]$Depth = 20
    )
    Write-Utf8NoBom -Path $Path -Content (($Value | ConvertTo-Json -Depth $Depth) + "`n")
}

function Get-NugetPackagesRoot {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)
    $candidates = @(
        $env:NUGET_PACKAGES,
        (Join-Path $RepoRoot ".dotnet_cli_home\.nuget\packages"),
        (Join-Path ([Environment]::GetFolderPath("UserProfile")) ".nuget\packages")
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate -PathType Container)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "Unable to resolve a NuGet package cache for license evidence."
}

function Invoke-VulnerabilityAudit {
    param(
        [Parameter(Mandatory = $true)][string]$DotNetPath,
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )
    $checkedAt = [DateTimeOffset]::UtcNow.ToString("o")
    $lines = @(& $DotNetPath list $ProjectPath package --vulnerable --include-transitive --format json --no-restore 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-JsonFile -Path $TargetPath -Value ([ordered]@{
            schemaVersion = "clearvision.vulnerability-scan/v1"
            status = "unavailable"
            checkedAtUtc = $checkedAt
            dataAsOfUtc = $null
            source = "NuGet audit advisory sources"
            reason = "NuGet audit command failed or its advisory source was unavailable; this is not a zero-vulnerability result."
            vulnerabilities = @()
        })
        return
    }
    $raw = ($lines -join [Environment]::NewLine)
    $jsonStart = $raw.IndexOf("{")
    if ($jsonStart -lt 0) {
        throw "NuGet audit returned success without a JSON document."
    }
    $audit = $raw.Substring($jsonStart) | ConvertFrom-Json
    $findings = @()
    foreach ($project in @($audit.projects)) {
        foreach ($framework in @($project.frameworks)) {
            foreach ($collectionName in @("topLevelPackages", "transitivePackages")) {
                foreach ($package in @($framework.$collectionName)) {
                    foreach ($vulnerability in @($package.vulnerabilities)) {
                        if ($null -eq $vulnerability) { continue }
                        $findings += [ordered]@{
                            package = $package.id
                            version = $package.resolvedVersion
                            severity = $vulnerability.severity
                            advisoryUrl = $vulnerability.advisoryurl
                        }
                    }
                }
            }
        }
    }
    Write-JsonFile -Path $TargetPath -Value ([ordered]@{
        schemaVersion = "clearvision.vulnerability-scan/v1"
        status = "available"
        checkedAtUtc = $checkedAt
        dataAsOfUtc = $checkedAt
        source = "NuGet audit advisory sources"
        reason = $null
        vulnerabilities = @($findings)
    })
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$dotnetShimPath = Join-Path $repoRoot "scripts\dotnet.ps1"
$dotnetPathOutput = & $dotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) { throw "Unable to resolve repository .NET SDK with $dotnetShimPath." }
$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) { throw "Resolved dotnet path is empty." }

$projectMap = @{
    Studio = [ordered]@{
        Project = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\ClearVision.Product.Desktop.csproj"
        Executable = "ClearVision.Product.Desktop.exe"
        DisplayName = "ClearVision Studio"
    }
    Station = [ordered]@{
        Project = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Station\ClearVision.Product.Station.csproj"
        Executable = "ClearVision.Product.Station.exe"
        DisplayName = "ClearVision Station"
    }
}
$selected = $projectMap[$Application]
$projectPath = $selected.Project
if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) { throw "Application project does not exist: $projectPath" }

if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $SourceRevisionId = (& git -C $repoRoot rev-parse HEAD).Trim()
}
if ($SourceRevisionId -notmatch '^[0-9a-fA-F]{40}$') { throw "SourceRevisionId must be a full 40-character Git SHA." }
$shortSha = $SourceRevisionId.Substring(0, 8).ToLowerInvariant()
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (& $dotnetPath msbuild $projectPath -nologo -getProperty:InformationalVersion | Select-Object -Last 1).Trim()
}
if ([string]::IsNullOrWhiteSpace($Version) -or $Version -notmatch '^[0-9A-Za-z][0-9A-Za-z._+-]*$') {
    throw "Version must be non-empty and safe for artifact names and NuGet metadata."
}
$artifactVersion = $Version.Replace("+", ".")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = ".tmp\publish-check\wave3c\release" }
$OutputRoot = Resolve-OutputPath -RepoRoot $repoRoot -Value $OutputRoot
Ensure-Directory $OutputRoot

$isSelfContained = $Profile -eq "field-self-contained"
$packageName = "ClearVision-$Application-$artifactVersion-$shortSha-$RuntimeIdentifier-$Profile"
$stagingRoot = Join-Path $OutputRoot "staging"
$stagingDir = Join-Path $stagingRoot $packageName
$artifactDir = Join-Path $OutputRoot "artifacts"
$supplyDir = Join-Path $OutputRoot "supply-chain"
$zipPath = Join-Path $artifactDir "$packageName.zip"
$resultPath = Join-Path $OutputRoot "package-result.json"
Ensure-Directory $stagingRoot
Ensure-Directory $artifactDir
Ensure-Directory $supplyDir
Remove-SafePath -OutputRoot $OutputRoot -Target $stagingDir
if (Test-Path -LiteralPath $zipPath) {
    Assert-ChildPath -Parent $OutputRoot -Child $zipPath
    Remove-Item -LiteralPath $zipPath -Force
}
Ensure-Directory $stagingDir

if (-not $NoRestore) {
    Write-Host "[portable] Locked restore: $projectPath"
    & $dotnetPath restore $projectPath --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "Locked restore failed with exit code $LASTEXITCODE." }
}

Write-Host "[portable] Publishing $Application / $RuntimeIdentifier / $Profile"
& $dotnetPath publish $projectPath `
    --configuration $Configuration `
    --runtime $RuntimeIdentifier `
    --self-contained $isSelfContained.ToString().ToLowerInvariant() `
    --no-restore `
    -p:PublishSingleFile=false `
    -p:PublishReadyToRun=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:EnableCompressionInSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Deterministic=true `
    -p:ContinuousIntegrationBuild=true `
    -p:Version=$Version `
    -p:InformationalVersion="$Version+$SourceRevisionId" `
    -p:RepositoryCommit=$SourceRevisionId `
    -p:SourceRevisionId=$SourceRevisionId `
    --output $stagingDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$executablePath = Join-Path $stagingDir $selected.Executable
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) { throw "Published executable is missing: $($selected.Executable)" }
$launcher = @"
@echo off
setlocal
pushd "%~dp0"
start "" "%~dp0$($selected.Executable)"
popd
"@
Write-Utf8NoBom -Path (Join-Path $stagingDir "Launch-ClearVision.cmd") -Content ($launcher.Replace("`n", "`r`n"))

$supportBoundary = if ($isSelfContained) {
    ".NET runtime is bundled. Microsoft Edge WebView2 Runtime remains an explicit target-machine prerequisite for Studio."
} else {
    ".NET 8 Windows Desktop Runtime is required on the target machine. This diagnostic profile is not the tag-default field package."
}
$readme = @"
ClearVision site deployment
===========================

Package: $packageName
Application: $($selected.DisplayName)
RID: $RuntimeIdentifier
Profile: $Profile
Git SHA: $SourceRevisionId

Start
-----
Run Launch-ClearVision.cmd. The launch chain calls only the packaged .NET executable and does not call Node, npm, npx, or a development server.

Support boundary
----------------
$supportBoundary
No local database, user profile, API key, token, model, sample image, source patch, test result, Playwright report, Node runtime, node_modules, FrontendV2, or development manifest is copied into this package.
Site databases, camera SDK prerequisites, credentials, models, PLC endpoints, and project data must be provisioned and approved separately.

Evidence
--------
release/source-identity.json records source and runtime identity.
release/package-content-manifest.json and release/package-files.sha256 bind the packaged file set.
The sibling supply-chain directory contains the final ZIP/nupkg-derived SPDX SBOM, third-party notices, dependency report, identity manifest, policy disposition, and artifact checksums.

Claims not made
---------------
This package is not evidence of a GitHub Release upload, a real no-Node target machine, real WebView2/DPI coverage, real device/model validation, site performance, or same-SHA GitHub CI.
"@
Write-Utf8NoBom -Path (Join-Path $stagingDir "README-site-deploy.txt") -Content ($readme.Replace("`n", "`r`n"))

$gitDirty = [bool](& git -C $repoRoot status --porcelain)
$sourceTimestampUtc = (& git -C $repoRoot show -s --format=%cI $SourceRevisionId).Trim()
$sdkVersion = (& $dotnetPath --version | Select-Object -Last 1).Trim()
$runtimeVersions = @(& $dotnetPath --list-runtimes | ForEach-Object {
    # Runtime identity is required; installation locations are machine-local evidence and must not enter the package.
    ($_.Trim() -replace '\s+\[[^\]]+\]\s*$', '')
} | Where-Object { $_ })
$releaseMetadataDir = Join-Path $stagingDir "release"
Ensure-Directory $releaseMetadataDir
$sourceIdentityPath = Join-Path $releaseMetadataDir "source-identity.json"
$sourceIdentity = [ordered]@{
    schemaVersion = "clearvision.package-source-identity/v1"
    gitSha = $SourceRevisionId.ToLowerInvariant()
    repositoryDirty = $gitDirty
    sourceTimestampUtc = $sourceTimestampUtc
    application = $Application
    version = $Version
    runtimeIdentifier = $RuntimeIdentifier
    profile = $Profile
    configuration = $Configuration
    selfContained = $isSelfContained
    sdkVersion = $sdkVersion
    runtimeInventory = $runtimeVersions
    packagingImplementation = "scripts/package-portable-deployment.ps1"
    packagingImplementationSha256 = Get-Sha256 (Join-Path $repoRoot "scripts\package-portable-deployment.ps1")
}
Write-JsonFile -Path $sourceIdentityPath -Value $sourceIdentity

$contentManifestPath = Join-Path $releaseMetadataDir "package-content-manifest.json"
$packageChecksumsPath = Join-Path $releaseMetadataDir "package-files.sha256"
$contentFiles = @(Get-ChildItem -LiteralPath $stagingDir -Recurse -File -Force | Where-Object {
    $_.FullName -ne $contentManifestPath -and $_.FullName -ne $packageChecksumsPath
} | Sort-Object { Get-RelativePath -Root $stagingDir -Path $_.FullName })
$contentRows = @($contentFiles | ForEach-Object {
    [ordered]@{ path = Get-RelativePath -Root $stagingDir -Path $_.FullName; sizeBytes = $_.Length; sha256 = Get-Sha256 $_.FullName }
})
$contentFingerprintInput = ($contentRows | ForEach-Object { "$($_.path)`0$($_.sizeBytes)`0$($_.sha256)" }) -join "`n"
$contentFingerprintBytes = [System.Text.Encoding]::UTF8.GetBytes($contentFingerprintInput)
$contentFingerprint = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($contentFingerprintBytes)).ToLowerInvariant()
Write-JsonFile -Path $contentManifestPath -Value ([ordered]@{
    schemaVersion = "clearvision.package-content-manifest/v1"
    packageName = $packageName
    gitSha = $SourceRevisionId.ToLowerInvariant()
    runtimeIdentifier = $RuntimeIdentifier
    profile = $Profile
    fileCount = $contentRows.Count
    contentFingerprint = "sha256:$contentFingerprint"
    files = $contentRows
}) -Depth 10
$checksumFiles = @(Get-ChildItem -LiteralPath $stagingDir -Recurse -File -Force | Where-Object {
    $_.FullName -ne $packageChecksumsPath
} | Sort-Object { Get-RelativePath -Root $stagingDir -Path $_.FullName })
$checksumLines = @($checksumFiles | ForEach-Object {
    "$(Get-Sha256 $_.FullName)  $(Get-RelativePath -Root $stagingDir -Path $_.FullName)"
})
Write-Utf8NoBom -Path $packageChecksumsPath -Content (($checksumLines -join "`n") + "`n")

& (Join-Path $repoRoot "scripts\Test-ReleasePublishHygiene.ps1") `
    -PublishDirectory $stagingDir `
    -AllowedManifestRelativePath "release/package-content-manifest.json"
if ($LASTEXITCODE -ne 0) { throw "Release publish hygiene failed." }

Write-Host "[portable] Creating archive: $zipPath"
Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw "Portable ZIP was not created." }

$nupkgPath = $null
if (-not $SkipOperatorPackage) {
    $packArguments = @{
        Configuration = "Release"
        PackageVersion = $Version
        SourceRevisionId = $SourceRevisionId
        RepositoryCommit = $SourceRevisionId
        RepositoryBranch = (& git -C $repoRoot branch --show-current).Trim()
        OutputPath = $artifactDir
        # Keep the package cache path short enough for transitive packages that still contain
        # deeply nested Apple reference headers even though the smoke target is Windows.
        SmokePackageRoot = (Join-Path $repoRoot ".tmp\publish-check\wave3c\operator-smoke")
    }
    if ($RunOperatorSmoke) { $packArguments.RunSmokeTest = $true }
    & (Join-Path $repoRoot "ClearVision.OperatorLibrary\pack.ps1") @packArguments
    if ($LASTEXITCODE -ne 0) { throw "OperatorLibrary pack failed." }
    $nupkgPath = Join-Path $artifactDir "ClearVision.OperatorLibrary.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $nupkgPath -PathType Leaf)) { throw "Expected OperatorLibrary package is missing: $nupkgPath" }
}

$validationPath = $null
if (-not $SkipSupplyChain) {
    if ($null -eq $nupkgPath) { throw "Supply-chain generation requires the final OperatorLibrary nupkg." }
    $resolvedVulnerabilityReport = $VulnerabilityReportPath
    if ([string]::IsNullOrWhiteSpace($resolvedVulnerabilityReport)) {
        $resolvedVulnerabilityReport = Join-Path $supplyDir "vulnerability-scan.json"
        if ($AttemptVulnerabilityScan) {
            Invoke-VulnerabilityAudit -DotNetPath $dotnetPath -ProjectPath $projectPath -TargetPath $resolvedVulnerabilityReport
        } else {
            Write-JsonFile -Path $resolvedVulnerabilityReport -Value ([ordered]@{
                schemaVersion = "clearvision.vulnerability-scan/v1"
                status = "unavailable"
                checkedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
                dataAsOfUtc = $null
                source = "NuGet audit advisory sources"
                reason = "Network vulnerability scan was not requested for this run; this is not a zero-vulnerability result."
                vulnerabilities = @()
            })
        }
    } else {
        $resolvedVulnerabilityReport = Resolve-OutputPath -RepoRoot $repoRoot -Value $resolvedVulnerabilityReport
    }
    $pythonCommand = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $pythonCommand) { throw "Python is required for final-artifact supply-chain generation." }
    & $pythonCommand.Source (Join-Path $repoRoot "quality\tools\generate_release_supply_chain.py") `
        --portable-zip $zipPath `
        --nupkg $nupkgPath `
        --output-dir $supplyDir `
        --identity-input $sourceIdentityPath `
        --policy (Join-Path $repoRoot "quality\policies\release-supply-chain-policy.json") `
        --nuget-packages-root (Get-NugetPackagesRoot -RepoRoot $repoRoot) `
        --vulnerability-report $resolvedVulnerabilityReport
    if ($LASTEXITCODE -ne 0) { throw "Supply-chain generation failed with exit code $LASTEXITCODE." }
    $validationPath = Join-Path $supplyDir "validation-summary.json"
    $validation = Get-Content -LiteralPath $validationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $validation.generationPassed -or -not $validation.artifactConsistencyPassed) { throw "Supply-chain structural validation failed." }
    if ($EnforceReleasePolicy -and -not $validation.releaseEligible) { throw "Release policy remains blocked; see $validationPath." }
}

$zipInfo = Get-Item -LiteralPath $zipPath
$result = [ordered]@{
    schemaVersion = "clearvision.portable-package-result/v1"
    packageName = $packageName
    application = $Application
    version = $Version
    gitSha = $SourceRevisionId.ToLowerInvariant()
    repositoryDirty = $gitDirty
    runtimeIdentifier = $RuntimeIdentifier
    profile = $Profile
    portableZip = [ordered]@{ path = Get-RelativePath -Root $OutputRoot -Path $zipPath; sizeBytes = $zipInfo.Length; sha256 = Get-Sha256 $zipPath }
    operatorLibraryPackage = if ($nupkgPath) {
        [ordered]@{ path = Get-RelativePath -Root $OutputRoot -Path $nupkgPath; sizeBytes = (Get-Item -LiteralPath $nupkgPath).Length; sha256 = Get-Sha256 $nupkgPath }
    } else { $null }
    contentFingerprint = "sha256:$contentFingerprint"
    supplyChainDirectory = if (-not $SkipSupplyChain) { Get-RelativePath -Root $OutputRoot -Path $supplyDir } else { $null }
    releasePolicyEnforced = [bool]$EnforceReleasePolicy
    releaseEligible = if ($validationPath) { [bool]((Get-Content -LiteralPath $validationPath -Raw -Encoding UTF8 | ConvertFrom-Json).releaseEligible) } else { $false }
}
Write-JsonFile -Path $resultPath -Value $result
if (-not $KeepStaging) { Remove-SafePath -OutputRoot $OutputRoot -Target $stagingDir }
Write-Host "[portable] Complete"
Write-Host "  ZIP: $zipPath"
Write-Host "  SHA-256: $($result.portableZip.sha256)"
Write-Host "  Content fingerprint: $($result.contentFingerprint)"
Write-Host "  Release eligible: $($result.releaseEligible)"
Write-Output $resultPath

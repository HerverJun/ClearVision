param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$RunSmokeTest,
    [string]$PackageVersion = "",
    [string]$SourceRevisionId = "",
    [string]$RepositoryBranch = "",
    [string]$RepositoryCommit = "",
    [string]$OutputPath = "",
    [string]$SmokePackageRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$projectPath = Join-Path $scriptRoot "ClearVision.OperatorLibrary.csproj"
$nupkgPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $scriptRoot "nupkg"
}
elseif ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $OutputPath))
}
$smokeTestPath = Join-Path $scriptRoot "tests/ClearVision.OperatorLibrary.SmokeTests/ClearVision.OperatorLibrary.SmokeTests.csproj"
$smokeSupportProjectPath = Join-Path $repoRoot "quality/testing/ClearVision.Testing/ClearVision.Testing.csproj"
$serialTestRunnerPath = Join-Path $repoRoot "scripts/run-dotnet-test-serial.ps1"
$dotnetShimPath = Join-Path $repoRoot "scripts/dotnet.ps1"
$smokePackageRoot = if ([string]::IsNullOrWhiteSpace($SmokePackageRoot)) {
    Join-Path $repoRoot ".tmp/nuget-packages/operator-library-smoke"
}
elseif ([System.IO.Path]::IsPathRooted($SmokePackageRoot)) {
    [System.IO.Path]::GetFullPath($SmokePackageRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repoRoot $SmokePackageRoot))
}
$smokeLockPath = Join-Path $smokePackageRoot "packages.lock.json"
$smokeNugetConfigPath = Join-Path $smokePackageRoot "NuGet.Config"

$dotnetPathOutput = & $dotnetShimPath -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) {
    throw "[pack] unable to resolve repository .NET SDK with $dotnetShimPath."
}

$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw "[pack] resolved dotnet path is empty."
}

if ([string]::IsNullOrWhiteSpace($PackageVersion)) {
    $PackageVersion = $env:CLEARVISION_OPERATORLIB_PACKAGE_VERSION
}

if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $SourceRevisionId = $env:SOURCE_REVISION_ID
}

if ([string]::IsNullOrWhiteSpace($RepositoryBranch)) {
    $RepositoryBranch = $env:BUILD_SOURCEBRANCHNAME
}

if ([string]::IsNullOrWhiteSpace($RepositoryBranch)) {
    $RepositoryBranch = $env:GITHUB_REF_NAME
}

if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) {
    $RepositoryCommit = $env:BUILD_SOURCEVERSION
}

if ([string]::IsNullOrWhiteSpace($RepositoryCommit)) {
    $RepositoryCommit = $env:GITHUB_SHA
}

if ([string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $SourceRevisionId = $RepositoryCommit
}

$packProperties = @()
if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
    $packProperties += "-p:PackageVersion=$PackageVersion"
}
if (-not [string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    $packProperties += "-p:SourceRevisionId=$SourceRevisionId"
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryBranch)) {
    $packProperties += "-p:RepositoryBranch=$RepositoryBranch"
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryCommit)) {
    $packProperties += "-p:RepositoryCommit=$RepositoryCommit"
}

Write-Host "[pack] Project: $projectPath"
Write-Host "[pack] Output : $nupkgPath"
if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
    Write-Host "[pack] PackageVersion     : $PackageVersion"
}
if (-not [string]::IsNullOrWhiteSpace($SourceRevisionId)) {
    Write-Host "[pack] SourceRevisionId   : $SourceRevisionId"
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryBranch)) {
    Write-Host "[pack] RepositoryBranch   : $RepositoryBranch"
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryCommit)) {
    Write-Host "[pack] RepositoryCommit   : $RepositoryCommit"
}

New-Item -Path $nupkgPath -ItemType Directory -Force | Out-Null

& $dotnetPath restore $projectPath --locked-mode
if ($LASTEXITCODE -ne 0) {
    throw "[pack] dotnet restore (locked) failed with exit code $LASTEXITCODE"
}

& $dotnetPath pack $projectPath -c $Configuration -o $nupkgPath --no-restore @packProperties
if ($LASTEXITCODE -ne 0) {
    throw "[pack] dotnet pack failed with exit code $LASTEXITCODE"
}

$resolvedPackageVersion = $PackageVersion
if ([string]::IsNullOrWhiteSpace($resolvedPackageVersion)) {
    $resolvedPackageVersion = (& $dotnetPath msbuild $projectPath -nologo -getProperty:PackageVersion).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "[pack] dotnet msbuild getProperty failed with exit code $LASTEXITCODE"
    }
}

$expectedPackageName = "ClearVision.OperatorLibrary.$resolvedPackageVersion.nupkg"
$expectedPackagePath = Join-Path $nupkgPath $expectedPackageName
if (-not (Test-Path -LiteralPath $expectedPackagePath)) {
    throw "[pack] expected package was not produced: $expectedPackagePath"
}

if ($RunSmokeTest) {
    Write-Host "[pack] Running package acceptance tests with local package source..."

    New-Item -Path $smokePackageRoot -ItemType Directory -Force | Out-Null
    $localPackageCachePath = Join-Path (Join-Path $smokePackageRoot "clearvision.operatorlibrary") $resolvedPackageVersion.ToLowerInvariant()
    if (Test-Path -LiteralPath $localPackageCachePath) {
        Remove-Item -LiteralPath $localPackageCachePath -Recurse -Force
    }
    if (Test-Path -LiteralPath $smokeLockPath) {
        Remove-Item -LiteralPath $smokeLockPath -Force
    }

    $escapedPackageSource = [System.Security.SecurityElement]::Escape($nupkgPath)
    $smokeNugetConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="local-operator-library" value="$escapedPackageSource" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local-operator-library">
      <package pattern="ClearVision.OperatorLibrary" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@
    [System.IO.File]::WriteAllText(
        $smokeNugetConfigPath,
        $smokeNugetConfig,
        [System.Text.UTF8Encoding]::new($false))

    & $dotnetPath restore $smokeSupportProjectPath `
        --configfile $smokeNugetConfigPath `
        --packages $smokePackageRoot `
        --locked-mode
    if ($LASTEXITCODE -ne 0) {
        throw "[pack] dotnet restore --locked-mode (smoke support) failed with exit code $LASTEXITCODE"
    }

    & $dotnetPath restore $smokeTestPath `
        --configfile $smokeNugetConfigPath `
        --packages $smokePackageRoot `
        --no-dependencies `
        --no-cache `
        -p:NuGetLockFilePath=$smokeLockPath `
        -p:ClearVisionOperatorLibraryPackageVersion=$resolvedPackageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "[pack] dotnet restore (smoke test) failed with exit code $LASTEXITCODE"
    }

    & $dotnetPath restore $smokeTestPath `
        --configfile $smokeNugetConfigPath `
        --packages $smokePackageRoot `
        --no-dependencies `
        --locked-mode `
        -p:NuGetLockFilePath=$smokeLockPath `
        -p:ClearVisionOperatorLibraryPackageVersion=$resolvedPackageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "[pack] dotnet restore --locked-mode (smoke test) failed with exit code $LASTEXITCODE"
    }

    & $serialTestRunnerPath `
        -Project $smokeTestPath `
        -Configuration $Configuration `
        -NoRestore `
        -Verbosity minimal `
        -DotNetTestArguments "-p:ClearVisionOperatorLibraryPackageVersion=$resolvedPackageVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "[pack] dotnet test (smoke test) failed with exit code $LASTEXITCODE"
    }
}

Write-Host "[pack] Done. PackageVersion=$resolvedPackageVersion"

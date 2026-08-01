param(
    [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")]
    [string]$Verbosity = "minimal",

    [string]$Configuration,

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory,

    [string]$LogFileName = "product.trx",

    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$gateScript = Join-Path $scriptRoot "run-classified-test-gate.ps1"
$projectPath = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\ClearVision.Product.Tests.csproj"
$gateName = "product-coverage"
$testFilter = "Lane=Pr&Suite!=ServicesCoverageSensitive&Suite!=PPFRegression"
$collectorName = "XPlat Code Coverage"

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }

    return [IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Quote-Argument {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -match '[\s"`|]') {
        return '"' + ($Value -replace '"', '\"') + '"'
    }

    return $Value
}

function Get-GitValue {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $value = & git -C $repoRoot @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return (($value | Out-String).Trim())
}

function Get-TrxCounters {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected TRX file was not produced: $Path"
    }

    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counterNode = $trx.SelectSingleNode("//*[local-name()='Counters']")
    if ($null -eq $counterNode) {
        throw "TRX file does not contain a Counters node: $Path"
    }

    return [ordered]@{
        total = [int]$counterNode.total
        executed = [int]$counterNode.executed
        passed = [int]$counterNode.passed
        failed = [int]$counterNode.failed
        error = [int]$counterNode.error
        timeout = [int]$counterNode.timeout
        aborted = [int]$counterNode.aborted
    }
}

function Get-CoverageModule {
    param([Parameter(Mandatory = $true)][System.Xml.XmlElement]$Package)

    $lineHits = @{}
    $branchCovered = 0
    $branchValid = 0

    foreach ($line in @($Package.SelectNodes(".//class/methods/method/lines/line"))) {
        $classNode = $line.SelectSingleNode("ancestor::class")
        $fileName = if ($null -ne $classNode) { [string]$classNode.GetAttribute("filename") } else { "" }
        $lineKey = $fileName + ":" + [string]$line.GetAttribute("number")
        if (-not $lineHits.ContainsKey($lineKey)) {
            $lineHits[$lineKey] = [int]$line.GetAttribute("hits")
        }

        $conditionCoverage = [string]$line.GetAttribute("condition-coverage")
        if ($line.GetAttribute("branch") -eq "True" -and $conditionCoverage -match "\((\d+)\/(\d+)\)") {
            $branchCovered += [int]$Matches[1]
            $branchValid += [int]$Matches[2]
        }
    }

    $lineValid = $lineHits.Count
    $lineCovered = @($lineHits.GetEnumerator() | Where-Object { $_.Value -gt 0 }).Count
    $lineRate = if ($lineValid -gt 0) { $lineCovered / $lineValid } else { 0.0 }
    $reportedBranchRate = [double]$Package.GetAttribute("branch-rate")

    return [ordered]@{
        name = [string]$Package.GetAttribute("name")
        linesCovered = $lineCovered
        linesValid = $lineValid
        lineRate = [Math]::Round($lineRate, 8)
        branchCovered = $branchCovered
        branchValid = $branchValid
        branchRate = [Math]::Round($reportedBranchRate, 8)
        reportedLineRate = [double]$Package.GetAttribute("line-rate")
        reportedBranchRate = $reportedBranchRate
    }
}

function Format-Percent {
    param([double]$Value)

    return [string]::Format([Globalization.CultureInfo]::InvariantCulture, "{0:0.00}%", $Value * 100.0)
}

$resolvedResultsDirectory = if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    Join-Path $repoRoot ".tmp\coverage\product"
}
else {
    Resolve-RepoPath $ResultsDirectory
}
$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $resolvedResultsDirectory
}
else {
    Resolve-RepoPath $OutputDirectory
}
[IO.Directory]::CreateDirectory($resolvedResultsDirectory) | Out-Null
[IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

$resolvedProjectPath = (Resolve-Path -LiteralPath $projectPath).Path
$resolvedGateScript = (Resolve-Path -LiteralPath $gateScript).Path
$trxPath = Join-Path $resolvedResultsDirectory $LogFileName
$beforeCoverageFiles = @{}
foreach ($file in @(Get-ChildItem -LiteralPath $resolvedResultsDirectory -Filter "coverage.cobertura.xml" -Recurse -File -ErrorAction SilentlyContinue)) {
    $beforeCoverageFiles[$file.FullName] = $file.LastWriteTimeUtc
}

$sourceSha = Get-GitValue -Arguments @("rev-parse", "HEAD")
$sourceStatus = @(Get-GitValue -Arguments @("status", "--porcelain"))
$globalJsonPath = Join-Path $repoRoot "global.json"
$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$dotnetShim = Join-Path $scriptRoot "dotnet.ps1"
$dotnetPathOutput = & $dotnetShim -InstallIfMissing -PrintPath -ReturnExitCode
if ($LASTEXITCODE -ne 0) {
    throw "Unable to resolve the repository .NET SDK with $dotnetShim."
}
$dotnetPath = ($dotnetPathOutput | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    throw "Resolved dotnet path is empty."
}
$sdkVersion = ((& $dotnetPath --version) | Select-Object -Last 1).Trim()
$sdkInfo = ((& $dotnetPath --info) | Out-String).Trim()

$lockPath = Join-Path $repoRoot "ClearVision.Product\tests\ClearVision.Product.Tests\packages.lock.json"
$lockModel = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$net8Dependencies = $lockModel.dependencies.'net8.0'
$coverletVersion = [string]$net8Dependencies.'coverlet.collector'.resolved
$testSdkVersion = [string]$net8Dependencies.'Microsoft.NET.Test.Sdk'.resolved

$dotnetArguments = @(
    "test",
    $resolvedProjectPath,
    "--nologo",
    "--verbosity",
    $Verbosity
)
if ($NoBuild) { $dotnetArguments += "--no-build" }
if ($NoRestore) { $dotnetArguments += "--no-restore" }
if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $dotnetArguments += @("--configuration", $Configuration) }
$dotnetArguments += @(
    "--results-directory",
    $resolvedResultsDirectory,
    "--logger",
    "trx;LogFileName=$LogFileName",
    "--collect",
    $collectorName,
    "--filter",
    $testFilter
)
$dotnetTestCommand = "dotnet " + (($dotnetArguments | ForEach-Object { Quote-Argument $_ }) -join " ")
$gateCommand = "& " + (Quote-Argument $resolvedGateScript) + " -Gate $gateName -Verbosity $Verbosity -ResultsDirectory " + (Quote-Argument $resolvedResultsDirectory) + " -LogFileName " + (Quote-Argument $LogFileName) + " -Collect " + (Quote-Argument $collectorName)
if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $gateCommand += " -Configuration " + (Quote-Argument $Configuration) }
if ($NoBuild) { $gateCommand += " -NoBuild" }
if ($NoRestore) { $gateCommand += " -NoRestore" }

$startedAtUtc = [DateTime]::UtcNow
$gateParameters = @{
    Gate = $gateName
    Verbosity = $Verbosity
    ResultsDirectory = $resolvedResultsDirectory
    LogFileName = $LogFileName
    Collect = @($collectorName)
    ReturnExitCode = $true
}
if (-not [string]::IsNullOrWhiteSpace($Configuration)) { $gateParameters.Configuration = $Configuration }
if ($NoBuild) { $gateParameters.NoBuild = $true }
if ($NoRestore) { $gateParameters.NoRestore = $true }

Write-Host "[product-coverage] Source SHA=$sourceSha"
Write-Host "[product-coverage] Population: $testFilter; ServicesCoverageSensitive is protected by tcp-device-regression and PPFRegression by ppf-regression."
Write-Host "[product-coverage] $dotnetTestCommand"
& $resolvedGateScript @gateParameters
$exitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
$finishedAtUtc = [DateTime]::UtcNow

if ($exitCode -ne 0) {
    throw "Product coverage gate '$gateName' failed with exit code $exitCode."
}

$trxItem = Get-Item -LiteralPath $trxPath -ErrorAction Stop
if ($trxItem.LastWriteTimeUtc -lt $startedAtUtc.AddSeconds(-2)) {
    throw "TRX is stale and was not produced by this coverage run: $trxPath"
}
$trxCounters = Get-TrxCounters -Path $trxPath
if ($trxCounters.failed -gt 0 -or $trxCounters.error -gt 0 -or $trxCounters.timeout -gt 0 -or $trxCounters.aborted -gt 0) {
    throw "Product coverage TRX contains failed, error, timeout, or aborted tests."
}

$coverageCandidates = @(
    Get-ChildItem -LiteralPath $resolvedResultsDirectory -Filter "coverage.cobertura.xml" -Recurse -File -ErrorAction Stop |
        Where-Object {
            (-not $beforeCoverageFiles.ContainsKey($_.FullName) -or $_.LastWriteTimeUtc -gt $beforeCoverageFiles[$_.FullName]) -and
            $_.LastWriteTimeUtc -ge $startedAtUtc.AddSeconds(-2)
        }
)
if ($coverageCandidates.Count -eq 0) {
    throw "Expected at least one fresh Cobertura file from this run."
}
$coverageByHash = @{}
foreach ($candidate in $coverageCandidates) {
    $hash = (Get-FileHash -LiteralPath $candidate.FullName -Algorithm SHA256).Hash
    if (-not $coverageByHash.ContainsKey($hash)) {
        $coverageByHash[$hash] = $candidate
    }
}
if ($coverageByHash.Count -ne 1) {
    $candidateList = ($coverageCandidates | ForEach-Object { $_.FullName }) -join "; "
    throw "Fresh Cobertura files are not identical copies of one report: $candidateList"
}
$coverageItem = @($coverageByHash.Values)[0]
[xml]$coverageXml = Get-Content -LiteralPath $coverageItem.FullName -Raw
$coverageNode = $coverageXml.SelectSingleNode("/coverage")
if ($null -eq $coverageNode) {
    throw "Cobertura file does not contain a coverage root: $($coverageItem.FullName)"
}

$modules = @($coverageNode.SelectNodes("./packages/package") | ForEach-Object { Get-CoverageModule -Package $_ })
if ($modules.Count -eq 0) {
    throw "Cobertura file contains no emitted modules: $($coverageItem.FullName)"
}
$majorProductModules = @($modules | Where-Object {
    $_.name -eq "ClearVision.PlcComm" -or $_.name.StartsWith("ClearVision.Product.", [StringComparison]::Ordinal)
})
$sourceRoots = @($coverageNode.SelectNodes("./sources/source") | ForEach-Object { $_.InnerText })
$globalLineValid = [int]$coverageNode.GetAttribute("lines-valid")
$globalLineCovered = [int]$coverageNode.GetAttribute("lines-covered")
$globalBranchValid = [int]$coverageNode.GetAttribute("branches-valid")
$globalBranchCovered = [int]$coverageNode.GetAttribute("branches-covered")

$report = [ordered]@{
    schemaVersion = "2026-08-01.product-coverage.v1"
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    source = [ordered]@{
        sha = $sourceSha
        dirty = ($sourceStatus.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($sourceStatus[0]))
        statusPorcelain = $sourceStatus
        repoRoot = $repoRoot
    }
    sdk = [ordered]@{
        requestedVersion = [string]$globalJson.sdk.version
        actualVersion = $sdkVersion
        dotnetPath = $dotnetPath
        coverletCollector = $coverletVersion
        microsoftNetTestSdk = $testSdkVersion
        info = $sdkInfo
    }
    population = [ordered]@{
        project = "Product"
        testProject = $resolvedProjectPath
        gate = $gateName
        filter = $testFilter
        includedLane = "Pr"
        excludedSuitesFromPopulation = @("ServicesCoverageSensitive", "PPFRegression")
        coverageSensitiveProtectionGate = "tcp-device-regression"
        servicesProtectionGate = "services-regression"
        ppfProtectionGate = "ppf-regression"
        coverageGroupingPolicy = "The TCP loopback timing suite and PPF correctness suite are not merged into this coverage population; each is protected by its named Gate."
        threshold = $null
    }
    instrumentation = [ordered]@{
        collector = $collectorName
        format = "cobertura"
        exclusionRules = @(
            "No custom Coverlet include, exclude, exclude-by-file, or threshold option was supplied.",
            "The emitted module set is the collector's standard application-module selection; test, adapter, and third-party modules are not reported as Product modules.",
            "ServicesCoverageSensitive (TcpDeviceManagerTests) is excluded because its loopback timing window is not stable under instrumentation; tcp-device-regression runs all nine tests and services-regression still covers the wider service suite.",
            "PPFRegression is excluded by the test population filter, not by a coverage threshold or algorithm change; ppf-regression still runs all nine PPF tests."
        )
    }
    run = [ordered]@{
        command = $gateCommand
        dotnetTestCommand = $dotnetTestCommand
        startedAtUtc = $startedAtUtc.ToString("o")
        finishedAtUtc = $finishedAtUtc.ToString("o")
        elapsedSeconds = [Math]::Round(($finishedAtUtc - $startedAtUtc).TotalSeconds, 3)
        resultsDirectory = $resolvedResultsDirectory
        trx = $trxPath
        cobertura = $coverageItem.FullName
        coberturaArtifacts = @($coverageCandidates | ForEach-Object { $_.FullName })
    }
    trx = $trxCounters
    coverage = [ordered]@{
        sourceRoots = $sourceRoots
        population = "All tests selected by Product product-coverage filter $testFilter"
        metric = "Cobertura line coverage counts unique emitted source file/line pairs; branch coverage uses Cobertura condition coverage. Overall totals are read from the fresh Cobertura root attributes."
        lineCovered = $globalLineCovered
        lineValid = $globalLineValid
        lineRate = if ($globalLineValid -gt 0) { [Math]::Round($globalLineCovered / $globalLineValid, 8) } else { 0.0 }
        branchCovered = $globalBranchCovered
        branchValid = $globalBranchValid
        branchRate = if ($globalBranchValid -gt 0) { [Math]::Round($globalBranchCovered / $globalBranchValid, 8) } else { 0.0 }
        loadedModules = $modules
        majorProductAssemblies = $majorProductModules
    }
}

$jsonPath = Join-Path $resolvedOutputDirectory "product-coverage.json"
$markdownPath = Join-Path $resolvedOutputDirectory "product-coverage.md"
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

$markdown = New-Object System.Collections.Generic.List[string]
[void]$markdown.Add("# Product Coverage")
[void]$markdown.Add("")
[void]$markdown.Add("- Source SHA: $sourceSha")
[void]$markdown.Add("- Dirty at run start: $($report.source.dirty)")
[void]$markdown.Add("- SDK: $sdkVersion (requested $($globalJson.sdk.version))")
[void]$markdown.Add("- Coverlet collector: $coverletVersion")
[void]$markdown.Add("- Population: Product gate $gateName, filter $testFilter")
[void]$markdown.Add("- Coverage grouping: ServicesCoverageSensitive is protected by tcp-device-regression; PPFRegression is protected by ppf-regression; no coverage segments were added.")
[void]$markdown.Add("- Command: $dotnetTestCommand")
[void]$markdown.Add("- Elapsed seconds: $($report.run.elapsedSeconds)")
[void]$markdown.Add("- TRX: $trxPath (total=$($trxCounters.total), passed=$($trxCounters.passed), failed=$($trxCounters.failed))")
[void]$markdown.Add("- Cobertura: $($coverageItem.FullName)")
[void]$markdown.Add("")
[void]$markdown.Add("## Overall")
[void]$markdown.Add("")
[void]$markdown.Add("| Metric | Covered | Valid | Rate |")
[void]$markdown.Add("| --- | ---: | ---: | ---: |")
[void]$markdown.Add("| Line | $globalLineCovered | $globalLineValid | $(Format-Percent ($report.coverage.lineRate)) |")
[void]$markdown.Add("| Branch | $globalBranchCovered | $globalBranchValid | $(Format-Percent ($report.coverage.branchRate)) |")
[void]$markdown.Add("")
[void]$markdown.Add("## All Emitted Modules")
[void]$markdown.Add("")
[void]$markdown.Add("| Module | Lines | Line | Branches | Branch |")
[void]$markdown.Add("| --- | ---: | ---: | ---: | ---: |")
foreach ($module in $modules) {
    [void]$markdown.Add("| $($module.name) | $($module.linesCovered)/$($module.linesValid) | $(Format-Percent $module.lineRate) | $($module.branchCovered)/$($module.branchValid) | $(Format-Percent $module.reportedBranchRate) |")
}
[void]$markdown.Add("")
[void]$markdown.Add("## Major Product Assemblies")
[void]$markdown.Add("")
[void]$markdown.Add("| Assembly | Lines | Line | Branches | Branch |")
[void]$markdown.Add("| --- | ---: | ---: | ---: | ---: |")
foreach ($module in $majorProductModules) {
    [void]$markdown.Add("| $($module.name) | $($module.linesCovered)/$($module.linesValid) | $(Format-Percent $module.lineRate) | $($module.branchCovered)/$($module.branchValid) | $(Format-Percent $module.reportedBranchRate) |")
}
[void]$markdown.Add("")
[void]$markdown.Add("## Exclusion Rules")
[void]$markdown.Add("")
foreach ($rule in $report.instrumentation.exclusionRules) {
    [void]$markdown.Add("- $rule")
}

$markdown | Set-Content -LiteralPath $markdownPath -Encoding UTF8
Write-Host "[product-coverage] JSON=$jsonPath"
Write-Host "[product-coverage] Markdown=$markdownPath"
Write-Host "[product-coverage] Overall line=$(Format-Percent $report.coverage.lineRate), branch=$(Format-Percent $report.coverage.branchRate)"

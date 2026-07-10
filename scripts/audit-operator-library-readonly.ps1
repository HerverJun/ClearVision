[CmdletBinding()]
param(
    [string]$CatalogPath = "docs/operators/catalog.json",
    [string]$OperatorEnumPath = "ClearVision.Product/src/ClearVision.Product.Core/Enums/OperatorEnums.cs",
    [string]$OperatorSourcePath = "ClearVision.Product/src/ClearVision.Product.Infrastructure/Operators",
    [string]$OperatorTestPath = "ClearVision.Product/tests/ClearVision.Product.Tests/Operators",
    [string]$DocumentationRoot = "docs/operators",
    [string]$JsonOutputPath,
    [string]$MarkdownOutputPath,
    [string]$BaselineSha,
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return [string](Resolve-Path (Join-Path $PSScriptRoot ".."))
}

function Resolve-RepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    }
    else {
        Join-Path $RepoRoot $Path
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Read-RequiredText {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required audit input was not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8
}

function Get-PropertyValue {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { $_.Name -ieq $Name } |
        Select-Object -First 1
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-StringProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    return [string](Get-PropertyValue -Object $Object -Name $Name)
}

function Get-EnumMemberNames {
    param([Parameter(Mandatory = $true)][string]$Content)

    $match = [regex]::Match($Content, "public\s+enum\s+OperatorType\s*\{(?<body>[\s\S]*?)\r?\n\}")
    if (-not $match.Success) {
        throw "OperatorType enum block was not found."
    }

    return @(
        [regex]::Matches($match.Groups["body"].Value, "(?m)^\s*(?<name>[A-Za-z][A-Za-z0-9_]*)\s*=\s*\d+") |
            ForEach-Object { $_.Groups["name"].Value } |
            Sort-Object -Unique
    )
}

function Find-CatalogOperator {
    param(
        [Parameter(Mandatory = $true)][object[]]$Operators,
        [Parameter(Mandatory = $true)][string]$Id
    )

    return $Operators |
        Where-Object { (Get-StringProperty -Object $_ -Name "id") -ieq $Id } |
        Select-Object -First 1
}

function Find-Port {
    param(
        [Parameter(Mandatory = $true)][object]$Operator,
        [Parameter(Mandatory = $true)][string]$CollectionName,
        [Parameter(Mandatory = $true)][string]$PortName
    )

    $ports = @(Get-PropertyValue -Object $Operator -Name $CollectionName)
    return $ports |
        Where-Object { (Get-StringProperty -Object $_ -Name "name") -ieq $PortName } |
        Select-Object -First 1
}

function Get-PortType {
    param([object]$Port)
    return Get-StringProperty -Object $Port -Name "dataType"
}

function Test-OutputPathSafety {
    param(
        [Parameter(Mandatory = $true)][string[]]$InputPaths,
        [string[]]$OutputPaths
    )

    $normalizedInputs = @($InputPaths | ForEach-Object { [System.IO.Path]::GetFullPath($_).ToLowerInvariant() })
    foreach ($outputPath in @($OutputPaths)) {
        if ([string]::IsNullOrWhiteSpace($outputPath)) {
            continue
        }

        $normalizedOutput = [System.IO.Path]::GetFullPath($outputPath).ToLowerInvariant()
        if ($normalizedInputs -contains $normalizedOutput) {
            throw "Audit report output must not overwrite an audit input: $outputPath"
        }
    }
}

function Write-Utf8Report {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    [System.IO.File]::WriteAllText(
        $Path,
        $Content,
        [System.Text.UTF8Encoding]::new($false))
}

function Escape-Markdown {
    param([object]$Value)
    return ([string]$Value).Replace("|", "\\|").Replace("`r", " ").Replace("`n", " ")
}

function New-MarkdownReport {
    param([Parameter(Mandatory = $true)][object]$Report)

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# Operator Library Read-Only Audit Baseline")
    $lines.Add("")
    $lines.Add("- Schema: " + [string]$Report.schemaVersion)
    $lines.Add("- Audit mode: " + [string]$Report.auditMode)
    $lines.Add("- Baseline SHA: " + [string]$Report.baselineSha)
    $lines.Add("- Generated UTC: " + [string]$Report.generatedAtUtc)
    $lines.Add("")
    $lines.Add("The audit reads checked-in catalog, enum, operator source, documentation, tests, and package boundary files. It does not instantiate or execute operators, read images/models, access network or hardware, or mutate audit inputs.")
    $lines.Add("")
    $lines.Add("## Summary")
    $lines.Add("")
    $lines.Add("| Metric | Value |")
    $lines.Add("| --- | ---: |")
    foreach ($property in $Report.summary.PSObject.Properties) {
        $lines.Add("| $(Escape-Markdown $property.Name) | $(Escape-Markdown $property.Value) |")
    }
    $lines.Add("")
    $lines.Add("## Contract Checks")
    $lines.Add("")
    $lines.Add("| Check | Passed | Details |")
    $lines.Add("| --- | :---: | --- |")
    foreach ($check in @($Report.contractChecks)) {
        $passed = if ($check.passed) { "yes" } else { "no" }
        $lines.Add("| $(Escape-Markdown $check.name) | $passed | $(Escape-Markdown $check.details) |")
    }
    $lines.Add("")
    $lines.Add("## Coverage Gaps")
    $lines.Add("")
    $lines.Add("| Kind | Count | Sample |")
    $lines.Add("| --- | ---: | --- |")
    foreach ($gap in @($Report.coverageGaps)) {
        $sample = @($gap.items) -join ", "
        $lines.Add("| $(Escape-Markdown $gap.kind) | $($gap.count) | $(Escape-Markdown $sample) |")
    }
    $lines.Add("")
    $lines.Add("## Findings")
    $lines.Add("")
    if (@($Report.findings).Count -eq 0) {
        $lines.Add("No findings.")
    }
    else {
        foreach ($finding in @($Report.findings)) {
            $lines.Add("- $(Escape-Markdown $finding)")
        }
    }
    $lines.Add("")
    $lines.Add("## Safety Boundary")
    $lines.Add("")
    $lines.Add("- Metadata only: " + [string]$Report.safetyBoundary.metadataOnly)
    $lines.Add("- Operator execution: " + [string]$Report.safetyBoundary.operatorExecution)
    $lines.Add("- Real resources touched: " + [string]$Report.safetyBoundary.realResourcesTouched)
    $lines.Add("- Source/catalog inputs mutated: " + [string]$Report.safetyBoundary.inputsMutated)
    $lines.Add("")

    return ($lines -join [Environment]::NewLine) + [Environment]::NewLine
}

$repoRoot = Get-RepoRoot
$catalogFullPath = Resolve-RepoPath -RepoRoot $repoRoot -Path $CatalogPath
$enumFullPath = Resolve-RepoPath -RepoRoot $repoRoot -Path $OperatorEnumPath
$sourceFullPath = Resolve-RepoPath -RepoRoot $repoRoot -Path $OperatorSourcePath
$testFullPath = Resolve-RepoPath -RepoRoot $repoRoot -Path $OperatorTestPath
$docsFullPath = Resolve-RepoPath -RepoRoot $repoRoot -Path $DocumentationRoot

$sourceInputs = @($catalogFullPath, $enumFullPath)
Test-OutputPathSafety -InputPaths $sourceInputs -OutputPaths @(
    $(if ($JsonOutputPath) { Resolve-RepoPath -RepoRoot $repoRoot -Path $JsonOutputPath }),
    $(if ($MarkdownOutputPath) { Resolve-RepoPath -RepoRoot $repoRoot -Path $MarkdownOutputPath })
)

$catalog = Read-RequiredText -Path $catalogFullPath | ConvertFrom-Json
$operators = @((Get-PropertyValue -Object $catalog -Name "operators"))
if ($operators.Count -eq 0) {
    throw "Operator catalog contains no operators: $catalogFullPath"
}

$catalogIds = @($operators | ForEach-Object { Get-StringProperty -Object $_ -Name "id" } | Where-Object { $_ } | Sort-Object -Unique)
$enumNames = @(Get-EnumMemberNames -Content (Read-RequiredText -Path $enumFullPath))
$sourceFiles = @()
$testFiles = @()
$docFiles = @()
if (Test-Path -LiteralPath $sourceFullPath -PathType Container) {
    $sourceFiles = @(Get-ChildItem -LiteralPath $sourceFullPath -Recurse -File -Filter "*.cs")
}
if (Test-Path -LiteralPath $testFullPath -PathType Container) {
    $testFiles = @(Get-ChildItem -LiteralPath $testFullPath -Recurse -File -Filter "*.cs")
}
if (Test-Path -LiteralPath $docsFullPath -PathType Container) {
    $docFiles = @(Get-ChildItem -LiteralPath $docsFullPath -Recurse -File -Include "*.md", "*.json")
}

$sourceText = @($sourceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$testText = @($testFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$catalogOnlyIds = @($catalogIds | Where-Object { $enumNames -notcontains $_ })
$enumOnlyIds = @($enumNames | Where-Object { $catalogIds -notcontains $_ })
$duplicateIds = @($operators | Group-Object { Get-StringProperty -Object $_ -Name "id" } | Where-Object Count -gt 1 | ForEach-Object Name)
$missingDocs = [System.Collections.Generic.List[string]]::new()
$missingSources = [System.Collections.Generic.List[string]]::new()
$missingTests = [System.Collections.Generic.List[string]]::new()

foreach ($operator in $operators) {
    $id = Get-StringProperty -Object $operator -Name "id"
    $docPath = Get-StringProperty -Object $operator -Name "docPath"
    if ([string]::IsNullOrWhiteSpace($docPath) -or -not (Test-Path -LiteralPath (Resolve-RepoPath -RepoRoot $repoRoot -Path $docPath) -PathType Leaf)) {
        $missingDocs.Add($id)
    }

    $escapedId = [regex]::Escape($id)
    if ($sourceText -notmatch "OperatorType\.$escapedId\b") {
        $missingSources.Add($id)
    }

    $quotedIdPattern = '(?i)(?:"|'')' + $escapedId + '(?:"|'')'
    if ($testText -notmatch "OperatorType\.$escapedId\b" -and $testText -notmatch $quotedIdPattern) {
        $missingTests.Add($id)
    }
}

$contractChecks = [System.Collections.Generic.List[object]]::new()
$blobAnalysis = Find-CatalogOperator -Operators $operators -Id "BlobAnalysis"
$blobLabeling = Find-CatalogOperator -Operators $operators -Id "BlobLabeling"
$blobListType = Get-PortType -Port (Find-Port -Operator $blobAnalysis -CollectionName "outputPorts" -PortName "Blobs")
$blobFeatureType = Get-PortType -Port (Find-Port -Operator $blobAnalysis -CollectionName "outputPorts" -PortName "BlobFeatures")
$labelingBlobType = Get-PortType -Port (Find-Port -Operator $blobLabeling -CollectionName "inputPorts" -PortName "Blobs")
$featureDescription = Get-StringProperty -Object (Find-Port -Operator $blobAnalysis -CollectionName "outputPorts" -PortName "BlobFeatures") -Name "description"
$blobDocPath = Join-Path $docsFullPath "BlobAnalysis.md"
$blobDocText = if (Test-Path -LiteralPath $blobDocPath -PathType Leaf) { Get-Content -LiteralPath $blobDocPath -Raw -Encoding UTF8 } else { "" }
$featurePathDocumented = $blobDocText -match "BlobFeatures\[(?:0|i)\]\.Area"
$blobPassed = $blobListType -eq "BlobList" -and
    $blobFeatureType -eq "BlobFeatureList" -and
    $labelingBlobType -eq "BlobList" -and
    $featurePathDocumented
$contractChecks.Add([pscustomobject]@{
        name = "BlobAnalysis/BlobLabeling typed list contract"
        passed = $blobPassed
        details = "BlobAnalysis.Blobs=$blobListType; BlobAnalysis.BlobFeatures=$blobFeatureType; BlobLabeling.Blobs=$labelingBlobType; legacy feature path documented=$featurePathDocumented"
    })

$binaryRegion = Find-CatalogOperator -Operators $operators -Id "BinaryImageToRegion"
$binaryRegionType = Get-PortType -Port (Find-Port -Operator $binaryRegion -CollectionName "outputPorts" -PortName "Region")
$rectangleRegion = Find-CatalogOperator -Operators $operators -Id "RectangleRegion"
$rectangleInputCount = @((Get-PropertyValue -Object $rectangleRegion -Name "inputPorts")).Count
$rectangleOutputType = Get-PortType -Port (Find-Port -Operator $rectangleRegion -CollectionName "outputPorts" -PortName "Rectangle")
$regionPassed = $binaryRegionType -eq "Region" -and $rectangleInputCount -eq 0 -and $rectangleOutputType -eq "Rectangle"
$contractChecks.Add([pscustomobject]@{
        name = "Region source contract"
        passed = $regionPassed
        details = "BinaryImageToRegion.Region=$binaryRegionType; RectangleRegion.inputPorts=$rectangleInputCount; RectangleRegion.Rectangle=$rectangleOutputType"
    })

$packageBoundaryPaths = @(
    (Join-Path $repoRoot "ClearVision.OperatorLibrary/ClearVision.OperatorLibrary.csproj"),
    (Join-Path $repoRoot "ClearVision.OperatorLibrary/src/ClearVision.OperatorLibrary.Modules/OperatorModuleCatalog.cs"),
    (Join-Path $repoRoot "ClearVision.OperatorLibrary/tests/ClearVision.OperatorLibrary.SmokeTests/ModuleNamespaceIndexTests.cs"),
    (Join-Path $repoRoot "ClearVision.OperatorLibrary/SBOM.md"),
    (Join-Path $repoRoot "ClearVision.OperatorLibrary/THIRD-PARTY-NOTICES.md")
)
$packageBoundaryMissing = @($packageBoundaryPaths | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
$contractChecks.Add([pscustomobject]@{
        name = "OperatorLibrary package read-only boundary"
        passed = $packageBoundaryMissing.Count -eq 0
        details = if ($packageBoundaryMissing.Count -eq 0) { "Package project, module index, smoke index, SBOM, and third-party notices are present." } else { "Missing: $($packageBoundaryMissing -join ', ')" }
    })

$findings = [System.Collections.Generic.List[string]]::new()
if ($duplicateIds.Count -gt 0) { $findings.Add("Catalog contains duplicate operator ids: $($duplicateIds -join ', ')") }
if ($catalogOnlyIds.Count -gt 0) { $findings.Add("Catalog ids not present in OperatorType enum: $($catalogOnlyIds -join ', ')") }
if ($missingDocs.Count -gt 0) { $findings.Add("Catalog operators missing documentation targets: $($missingDocs -join ', ')") }
if ($missingSources.Count -gt 0) { $findings.Add("Catalog operators without a source OperatorType reference: $($missingSources -join ', ')") }
if ($missingTests.Count -gt 0) { $findings.Add("Catalog operators without an indexed unit-test reference: $($missingTests -join ', ')") }
foreach ($check in $contractChecks | Where-Object { -not $_.passed }) {
    $findings.Add("Contract check failed: $($check.name) ($($check.details))")
}

$gitSha = $BaselineSha
if ([string]::IsNullOrWhiteSpace($gitSha)) {
    try { $gitSha = (& git -C $repoRoot rev-parse HEAD 2>$null).Trim() } catch { $gitSha = "unknown" }
}
$branch = "unknown"
try { $branch = (& git -C $repoRoot branch --show-current 2>$null).Trim() } catch { }

$report = [ordered]@{
    schemaVersion = "2026-07-10.operator-library-readonly-audit.v1"
    auditMode = "read-only"
    baselineSha = if ($gitSha) { $gitSha } else { "unknown" }
    branch = $branch
    generatedAtUtc = [DateTime]::UtcNow.ToString("O")
    source = [ordered]@{
        catalogPath = $CatalogPath
        operatorEnumPath = $OperatorEnumPath
        operatorSourcePath = $OperatorSourcePath
        operatorTestPath = $OperatorTestPath
        documentationRoot = $DocumentationRoot
    }
    summary = [pscustomobject][ordered]@{
        catalogOperatorCount = $catalogIds.Count
        catalogDeclaredTotalCount = [int](Get-PropertyValue -Object $catalog -Name "totalCount")
        operatorEnumMemberCount = $enumNames.Count
        operatorSourceFileCount = $sourceFiles.Count
        operatorTestFileCount = $testFiles.Count
        documentationFileCount = $docFiles.Count
        duplicateCatalogIdCount = $duplicateIds.Count
        catalogOnlyIdCount = $catalogOnlyIds.Count
        enumOnlyIdCount = $enumOnlyIds.Count
        missingDocumentationCount = $missingDocs.Count
        missingSourceReferenceCount = $missingSources.Count
        missingTestReferenceCount = $missingTests.Count
        contractCheckCount = $contractChecks.Count
        contractFailureCount = @($contractChecks | Where-Object { -not $_.passed }).Count
        findingCount = $findings.Count
    }
    coverageGaps = @(
        [pscustomobject]@{ kind = "enum-only (legacy/internal candidates)"; count = $enumOnlyIds.Count; items = @($enumOnlyIds | Select-Object -First 20) }
        [pscustomobject]@{ kind = "missing documentation"; count = $missingDocs.Count; items = @($missingDocs | Select-Object -First 20) }
        [pscustomobject]@{ kind = "missing source reference"; count = $missingSources.Count; items = @($missingSources | Select-Object -First 20) }
        [pscustomobject]@{ kind = "missing test reference"; count = $missingTests.Count; items = @($missingTests | Select-Object -First 20) }
    )
    contractChecks = @($contractChecks)
    findings = @($findings)
    safetyBoundary = [pscustomobject][ordered]@{
        metadataOnly = $true
        operatorExecution = $false
        realResourcesTouched = $false
        inputsMutated = $false
        reportOutputsWritten = (-not [string]::IsNullOrWhiteSpace($JsonOutputPath) -or -not [string]::IsNullOrWhiteSpace($MarkdownOutputPath))
    }
}

$json = $report | ConvertTo-Json -Depth 12
$markdown = New-MarkdownReport -Report ([pscustomobject]$report)
if (-not [string]::IsNullOrWhiteSpace($JsonOutputPath)) {
    Write-Utf8Report -Path (Resolve-RepoPath -RepoRoot $repoRoot -Path $JsonOutputPath) -Content $json
}
if (-not [string]::IsNullOrWhiteSpace($MarkdownOutputPath)) {
    Write-Utf8Report -Path (Resolve-RepoPath -RepoRoot $repoRoot -Path $MarkdownOutputPath) -Content $markdown
}

Write-Host "Operator library read-only audit: catalog=$($report.summary.catalogOperatorCount), enum=$($report.summary.operatorEnumMemberCount), findings=$($report.summary.findingCount), contractsFailed=$($report.summary.contractFailureCount)"
if ($ReportOnly) {
    Write-Host "Report-only mode: findings do not fail the command."
    exit 0
}
if ($findings.Count -gt 0) {
    exit 1
}
exit 0

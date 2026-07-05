[CmdletBinding()]
param(
    [string]$CatalogPath = "docs/operators/catalog.json",
    [int]$MinimumRuntimeOperators = 155,
    [int]$MinimumCatalogOperators = 150,
    [int]$MinimumCatalogCategories = 30
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return [string](Resolve-Path (Join-Path $PSScriptRoot ".."))
}

function Resolve-RepoPath {
    param(
        [string]$RepoRoot,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "Path must not be empty."
    }

    $candidate = if ([System.IO.Path]::IsPathRooted($Path)) {
        $Path
    } else {
        Join-Path $RepoRoot $Path
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Get-BlockMap {
    param(
        [string]$Content,
        [string]$Marker
    )

    $pattern = [regex]::Escape("const $Marker = [") + "(?<body>[\s\S]*?)\r?\n\];"
    $match = [regex]::Match($Content, $pattern)
    if (-not $match.Success) {
        throw "Block not found: $Marker"
    }

    $map = @{}
    foreach ($line in ($match.Groups["body"].Value -split "\r?\n")) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or -not $trimmed.Contains("|")) {
            continue
        }

        $parts = $trimmed.Split("|", 2, [System.StringSplitOptions]::None)
        $key = $parts[0].Trim()
        $value = $parts[1].Trim()
        if ($key -and $value) {
            $map[$key] = $value
        }
    }

    return $map
}

function Get-OperatorTypeNames {
    param([string]$Content)

    $match = [regex]::Match($Content, "public enum OperatorType\s*\{(?<body>[\s\S]*?)\r?\n\}")
    if (-not $match.Success) {
        throw "OperatorType enum block not found."
    }

    return [regex]::Matches($match.Groups["body"].Value, "^\s*([A-Za-z][A-Za-z0-9_]*)\s*=", "Multiline") |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
}

function Get-OperatorCatalog {
    param(
        [string]$RepoRoot,
        [string]$CatalogPath,
        [int]$MinimumCatalogOperators
    )

    $resolvedCatalogPath = Resolve-RepoPath -RepoRoot $RepoRoot -Path $CatalogPath
    if (-not (Test-Path -LiteralPath $resolvedCatalogPath -PathType Leaf)) {
        throw "Operator catalog JSON not found: $CatalogPath"
    }

    try {
        $raw = Get-Content -LiteralPath $resolvedCatalogPath -Raw -Encoding UTF8
        $json = $raw | ConvertFrom-Json
    }
    catch {
        throw "Unable to parse operator catalog JSON at '$resolvedCatalogPath': $($_.Exception.Message)"
    }

    if ($null -eq $json -or $json -is [System.Array]) {
        throw "Operator catalog '$resolvedCatalogPath' must be a JSON object."
    }

    if ($null -eq $json.PSObject.Properties["totalCount"]) {
        throw "Operator catalog '$resolvedCatalogPath' is missing totalCount."
    }

    if ($null -eq $json.PSObject.Properties["operators"] -or $null -eq $json.operators) {
        throw "Operator catalog '$resolvedCatalogPath' is missing operators."
    }

    [int]$totalCount = 0
    if (-not [int]::TryParse([string]$json.totalCount, [ref]$totalCount)) {
        throw "Operator catalog '$resolvedCatalogPath' has a non-integer totalCount: $($json.totalCount)"
    }

    $operators = @($json.operators)
    if ($operators.Count -lt $MinimumCatalogOperators) {
        throw "Operator catalog '$resolvedCatalogPath' has $($operators.Count) operator(s); expected at least $MinimumCatalogOperators."
    }

    if ($totalCount -lt $MinimumCatalogOperators) {
        throw "Operator catalog '$resolvedCatalogPath' totalCount is $totalCount; expected at least $MinimumCatalogOperators."
    }

    if ($totalCount -ne $operators.Count) {
        throw "Operator catalog '$resolvedCatalogPath' totalCount ($totalCount) does not match operators array count ($($operators.Count))."
    }

    $invalidOperators = @(
        for ($index = 0; $index -lt $operators.Count; $index++) {
            $operator = $operators[$index]
            $id = if ($null -ne $operator.PSObject.Properties["id"]) { [string]$operator.id } else { "" }
            $category = if ($null -ne $operator.PSObject.Properties["category"]) { [string]$operator.category } else { "" }
            if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($category)) {
                "#$index"
            }
        }
    )

    if ($invalidOperators.Count -gt 0) {
        $shownInvalid = @($invalidOperators | Select-Object -First 5)
        throw "Operator catalog '$resolvedCatalogPath' has operator entries without id/category: $($shownInvalid -join ', ')"
    }

    return [pscustomobject]@{
        Json = $json
        Operators = $operators
        Path = $resolvedCatalogPath
        TotalCount = $totalCount
    }
}

$repoRoot = Get-RepoRoot
if ($MinimumRuntimeOperators -lt 1) {
    throw "MinimumRuntimeOperators must be greater than or equal to 1."
}

if ($MinimumCatalogOperators -lt 1) {
    throw "MinimumCatalogOperators must be greater than or equal to 1."
}

if ($MinimumCatalogCategories -lt 1) {
    throw "MinimumCatalogCategories must be greater than or equal to 1."
}

$visualPath = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\wwwroot\src\shared\operatorVisuals.js"
$enumPath = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Core\Enums\OperatorEnums.cs"
$appPath = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\wwwroot\src\app.js"
$flowEditorPath = Join-Path $repoRoot "ClearVision.Product\src\ClearVision.Product.Desktop\wwwroot\src\features\flow-editor\flowEditorInteraction.js"
$visualContent = Get-Content -Path $visualPath -Raw -Encoding UTF8
$enumContent = Get-Content -Path $enumPath -Raw -Encoding UTF8
$catalogInfo = Get-OperatorCatalog -RepoRoot $repoRoot -CatalogPath $CatalogPath -MinimumCatalogOperators $MinimumCatalogOperators
$catalog = $catalogInfo.Json

$aliasMap = Get-BlockMap -Content $visualContent -Marker "OPERATOR_ICON_ALIAS_BLOCKS"
$iconMap = Get-BlockMap -Content $visualContent -Marker "OPERATOR_ICON_BLOCKS"
$categoryMap = Get-BlockMap -Content $visualContent -Marker "CATEGORY_ICON_BLOCKS"
$categoryColorMap = Get-BlockMap -Content $visualContent -Marker "CATEGORY_COLOR_BLOCKS"
$enumTypes = Get-OperatorTypeNames -Content $enumContent
if ($enumTypes.Count -lt $MinimumRuntimeOperators) {
    throw "Runtime operator enum has $($enumTypes.Count) operator(s); expected at least $MinimumRuntimeOperators."
}

$runtimeMissing = @()
foreach ($type in $enumTypes) {
    if ($iconMap.ContainsKey($type)) {
        continue
    }

    if ($aliasMap.ContainsKey($type) -and $iconMap.ContainsKey($aliasMap[$type])) {
        continue
    }

    $runtimeMissing += $type
}

$canonicalTypes = $enumTypes | Where-Object { -not $aliasMap.ContainsKey($_) }
$canonicalMissingDirect = $canonicalTypes | Where-Object { -not $iconMap.ContainsKey($_) }

$catalogCategories = $catalogInfo.Operators | ForEach-Object { $_.category } | Sort-Object -Unique
if ($catalogCategories.Count -lt $MinimumCatalogCategories) {
    throw "Operator catalog '$($catalogInfo.Path)' has $($catalogCategories.Count) categor(ies); expected at least $MinimumCatalogCategories."
}

$categoryMissing = $catalogCategories | Where-Object { -not $categoryMap.ContainsKey($_) }
$categoryColorMissing = $catalogCategories | Where-Object { -not $categoryColorMap.ContainsKey($_) }

$legacyOperatorConfigs = @()
if (Select-String -Path $appPath -SimpleMatch "const operatorConfigs = {" -Quiet) {
    $legacyOperatorConfigs += $appPath
}
if (Select-String -Path $flowEditorPath -SimpleMatch "const operatorConfigs = {" -Quiet) {
    $legacyOperatorConfigs += $flowEditorPath
}

Write-Host "Runtime operator count: $($enumTypes.Count)" -ForegroundColor Cyan
Write-Host "Canonical operator count: $($canonicalTypes.Count)" -ForegroundColor Cyan
Write-Host "Alias count in runtime enum: $($enumTypes.Count - $canonicalTypes.Count)" -ForegroundColor Cyan
Write-Host "Direct icon count: $($iconMap.Count)" -ForegroundColor Cyan
Write-Host "Category icon count: $($categoryMap.Count)" -ForegroundColor Cyan
Write-Host "Category color count: $($categoryColorMap.Count)" -ForegroundColor Cyan
Write-Host "Operator catalog path: $($catalogInfo.Path)" -ForegroundColor Cyan
Write-Host "Catalog operator count: $($catalogInfo.Operators.Count)" -ForegroundColor Cyan
Write-Host "Catalog category count: $($catalogCategories.Count)" -ForegroundColor Cyan

if ($runtimeMissing.Count -gt 0) {
    Write-Error ("Missing runtime icon coverage: " + ($runtimeMissing -join ", "))
}

if ($canonicalMissingDirect.Count -gt 0) {
    Write-Error ("Missing direct canonical icons: " + ($canonicalMissingDirect -join ", "))
}

if ($categoryMissing.Count -gt 0) {
    Write-Error ("Missing category fallbacks: " + ($categoryMissing -join ", "))
}

if ($categoryColorMissing.Count -gt 0) {
    Write-Error ("Missing category color fallbacks: " + ($categoryColorMissing -join ", "))
}

if ($legacyOperatorConfigs.Count -gt 0) {
    Write-Error ("Legacy inline operatorConfigs remain in: " + ($legacyOperatorConfigs -join ", "))
}

Write-Host "Operator icon coverage check passed." -ForegroundColor Green

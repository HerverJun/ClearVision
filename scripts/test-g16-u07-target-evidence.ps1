[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Assert-PowerShellParses {
    param([Parameter(Mandatory = $true)][string]$Path)
    $tokens = $null
    $errors = $null
    [Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors) | Out-Null
    if (@($errors).Count -gt 0) {
        throw "PowerShell AST parse failed for $Path`: $(@($errors | ForEach-Object Message) -join '; ')"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$collector = Join-Path $repoRoot 'scripts\collect-g16-u07-target-evidence.ps1'
$schemaPath = Join-Path $repoRoot 'quality\schemas\g16-u07-target-evidence.schema.json'
$observationPath = Join-Path $repoRoot 'quality\evidence\g16-u07-target-kit\profile-observation.template.json'
Assert-PowerShellParses $collector
Get-Content -LiteralPath $schemaPath -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null
Get-Content -LiteralPath $observationPath -Raw -Encoding UTF8 | ConvertFrom-Json | Out-Null

$scratchParent = Join-Path $repoRoot '.tmp\publish-check'
$scratch = Join-Path $scratchParent ('g16-u07-self-test-' + [Guid]::NewGuid().ToString('N'))
$resolvedScratch = [IO.Path]::GetFullPath($scratch)
$resolvedParent = [IO.Path]::GetFullPath($scratchParent)
if (-not $resolvedScratch.StartsWith($resolvedParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Self-test scratch path escaped .tmp/publish-check.'
}
New-Item -ItemType Directory -Path $scratch | Out-Null
try {
    $payload = Join-Path $scratch 'payload.txt'
    [IO.File]::WriteAllText($payload, 'fixture portable payload', [Text.UTF8Encoding]::new($false))
    $zip = Join-Path $scratch 'fixture-portable.zip'
    Compress-Archive -LiteralPath $payload -DestinationPath $zip
    $zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $output = Join-Path $scratch 'profile-formal'

    & $collector `
        -PackageZip $zip `
        -ExpectedPackageSha256 $zipHash `
        -GitSha '0123456789abcdef0123456789abcdef01234567' `
        -GitDirty:$false `
        -TargetSku 'fixture-formal-sku' `
        -TargetProfile 'fixture-1920x1080-100' `
        -ProfileClass formal-sku `
        -ResolutionWidth 1920 `
        -ResolutionHeight 1080 `
        -OsScalePercent 100 `
        -OperatorProfile 'fixture-operator-profile' `
        -ModelProfile 'fixture-model-profile' `
        -DeviceProfile 'fixture-device-profile' `
        -OutputDirectory $output `
        -ObservationPath $observationPath `
        -Fixture | Out-Null

    $evidencePath = Join-Path $output 'evidence.json'
    $report = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $python) { throw 'Python is required for JSON Schema self-validation.' }
    & $python.Source -c "import json,sys,jsonschema; jsonschema.Draft202012Validator(json.load(open(sys.argv[2],encoding='utf-8'))).validate(json.load(open(sys.argv[1],encoding='utf-8')))" $evidencePath $schemaPath
    if ($LASTEXITCODE -ne 0) { throw 'Generated evidence failed JSON Schema validation.' }
    if (-not $report.fixture -or $report.evidenceClass -ne 'FIXTURE') { throw 'Fixture classification was not preserved.' }
    if ($report.releaseEligible -ne $false) { throw 'releaseEligible must remain false.' }
    if ($report.artifactBinding.portableZip.sha256 -ne $zipHash) { throw 'Portable ZIP binding mismatch.' }
    if ($report.environment.display.scaleWasModifiedByCollector -ne $false) { throw 'Collector must not modify OS scale.' }
    if ($report.profileClosure.status -ne 'NOT_RUN' -or -not $report.profileClosure.independentProfile) { throw 'Profile closure defaults are invalid.' }
    foreach ($field in @('operatorIdentity', 'reviewer', 'deviceSerialNumber', 'approvalDecision', 'signedAtUtc')) {
        if ($null -ne $report.signOff.$field) { throw "Fixture sign-off field was prefilled: $field" }
    }
    $statuses = @(
        $report.execution.startupAndHealth.status,
        $report.execution.noNodeVerification.status,
        $report.execution.workingSet.status,
        $report.execution.performance[0].status,
        $report.execution.performance[1].status,
        $report.execution.workflows.projectSaveLoad.status,
        $report.execution.workflows.legacyProjectAndPackage.status,
        $report.execution.workflows.stationReconnectAndResult.status,
        $report.execution.workflows.agentWorkspace.status
    )
    if (@($statuses | Where-Object { $_ -ne 'NOT_RUN' }).Count -gt 0) { throw 'Fixture unexpectedly contains executed status.' }

    $checksumLine = Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS') -Encoding UTF8 | Where-Object { $_ -match '  evidence\.json$' }
    $evidenceHash = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($checksumLine -ne "$evidenceHash  evidence.json") { throw 'evidence.json checksum mismatch.' }

    $rejected = $false
    try {
        & $collector `
            -PackageZip $zip `
            -ExpectedPackageSha256 ('0' * 64) `
            -GitSha '0123456789abcdef0123456789abcdef01234567' `
            -GitDirty:$false `
            -TargetSku 'fixture' `
            -TargetProfile 'hash-mismatch' `
            -ProfileClass experimental `
            -ResolutionWidth 1920 `
            -ResolutionHeight 1080 `
            -OsScalePercent 100 `
            -OperatorProfile 'fixture' `
            -ModelProfile 'fixture' `
            -DeviceProfile 'fixture' `
            -OutputDirectory (Join-Path $scratch 'hash-mismatch') `
            -Fixture | Out-Null
    } catch { $rejected = $true }
    if (-not $rejected) { throw 'Collector accepted a mismatched portable ZIP hash.' }
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

Write-Host 'G16/U07 target evidence kit self-test passed.'

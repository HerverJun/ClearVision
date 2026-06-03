param(
    [ValidateSet("Shadow4h", "Simulator10x1h")]
    [string]$Mode = "Shadow4h",
    [string]$Studio = "http://127.0.0.1:5000",
    [string]$Token = "",
    [string]$StationProcessName = "ClearVision.Product.Station",
    [string]$StudioProcessName = "ClearVision.Product.Desktop",
    [string]$VisionDbPath = ".\vision.db",
    [string]$SpoolDirectory = "$env:LOCALAPPDATA\ClearVisionStation\spool",
    [int]$SampleSeconds = 60,
    [string]$OutputDirectory = ".\.tmp\station-alpha-trial",
    [string]$StationHealthUrl = "",
    [string]$SimulatorHealthUrl = "",
    [switch]$SkipPreflight
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$durationSeconds = if ($Mode -eq "Shadow4h") { 4 * 60 * 60 } else { 60 * 60 }
$startedAt = Get-Date
$endedAt = $startedAt.AddSeconds($durationSeconds)
$csvPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}.csv" -f $startedAt, $Mode)
$notesPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}-notes.md" -f $startedAt, $Mode)
$summaryPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}-summary.md" -f $startedAt, $Mode)
$preflightPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}-preflight.json" -f $startedAt, $Mode)

"timestampUtc,mode,processName,pid,cpuSeconds,workingSetMb,privateMemoryMb,visionDbMb,spoolMb,spoolFiles" | Out-File -FilePath $csvPath -Encoding utf8

$preflight = [ordered]@{
    mode = $Mode
    startedAtUtc = $startedAt.ToUniversalTime().ToString("o")
    studio = $Studio
    stationHealthUrl = $StationHealthUrl
    simulatorHealthUrl = $SimulatorHealthUrl
    checks = @()
}

function Add-PreflightCheck {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Message,
        [object]$Data = $null
    )

    $script:preflight.checks += [ordered]@{
        name = $Name
        passed = $Passed
        message = $Message
        data = $Data
    }
}

function Invoke-JsonProbe {
    param(
        [string]$Url,
        [int]$TimeoutSeconds = 5
    )

    Invoke-RestMethod -Uri $Url -TimeoutSec $TimeoutSeconds -Headers (Get-AuthHeaders)
}

function Get-AuthHeaders {
    if ([string]::IsNullOrWhiteSpace($Token)) {
        return @{}
    }

    return @{ Authorization = "Bearer $Token" }
}

function Test-HttpProbe {
    param(
        [string]$Name,
        [string]$Url,
        [switch]$Required
    )

    if ([string]::IsNullOrWhiteSpace($Url)) {
        Add-PreflightCheck -Name $Name -Passed (-not $Required) -Message "Skipped: URL not provided."
        if ($Required) {
            throw "$Name preflight URL is required."
        }
        return $null
    }

    try {
        $data = Invoke-JsonProbe -Url $Url
        Add-PreflightCheck -Name $Name -Passed $true -Message "OK" -Data $data
        return $data
    } catch {
        Add-PreflightCheck -Name $Name -Passed $false -Message $_.Exception.Message
        if ($Required) {
            throw "$Name preflight failed: $($_.Exception.Message)"
        }
        return $null
    }
}

function Get-StationSummary {
    try {
        return Invoke-JsonProbe -Url "$($Studio.TrimEnd('/'))/api/stations/summary"
    } catch {
        return $null
    }
}

function Get-SummaryNumber {
    param(
        [object]$Summary,
        [string[]]$Names
    )

    if ($null -eq $Summary) {
        return 0
    }

    foreach ($name in $Names) {
        $property = $Summary.PSObject.Properties[$name]
        if ($property -and $null -ne $property.Value) {
            return [int]$property.Value
        }
    }

    return 0
}

if (-not $SkipPreflight) {
    Test-HttpProbe -Name "Studio /health" -Url "$($Studio.TrimEnd('/'))/health" -Required | Out-Null
    $stationSummary = Test-HttpProbe -Name "Studio station summary" -Url "$($Studio.TrimEnd('/'))/api/stations/summary"
    if ($StationHealthUrl) {
        Test-HttpProbe -Name "Station health" -Url $StationHealthUrl | Out-Null
    }
    if ($SimulatorHealthUrl) {
        Test-HttpProbe -Name "Simulator health" -Url $SimulatorHealthUrl | Out-Null
    }

    $initialStations = Get-SummaryNumber -Summary $stationSummary -Names @("totalStations", "TotalStations", "stationCount", "StationCount")
    Add-PreflightCheck -Name "Station sample visibility" -Passed ($initialStations -ge 0) -Message "Station summary reachable; total station count: $initialStations."
}

if ($Mode -eq "Simulator10x1h") {
    $simulatorArgs = @(
        "-NoProfile",
        "-ExecutionPolicy", "Bypass",
        "-File", ".\scripts\run-station-simulator.ps1",
        "-Studio", $Studio,
        "-Token", $Token,
        "-Stations", "10",
        "-Rate", "2",
        "-NgRate", "0.08",
        "-ErrorRate", "0.01",
        "-LogRate", "0.05",
        "-DisconnectRate", "0.01",
        "-DurationSeconds", "$durationSeconds"
    )
    Start-Process -FilePath "powershell.exe" -ArgumentList $simulatorArgs -WindowStyle Hidden | Out-Null

    if (-not $SkipPreflight) {
        Start-Sleep -Seconds 5
        $simulatorSummary = Get-StationSummary
        $simulatorStations = Get-SummaryNumber -Summary $simulatorSummary -Names @("totalStations", "TotalStations", "stationCount", "StationCount")
        Add-PreflightCheck -Name "Simulator station sample" -Passed ($simulatorStations -gt 0) -Message "Station count after simulator warmup: $simulatorStations." -Data $simulatorSummary
    }
}

function Get-DirectoryBytes {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return (Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
}

while ((Get-Date) -lt $endedAt) {
    $timestamp = (Get-Date).ToUniversalTime().ToString("o")
    $dbBytes = if (Test-Path -LiteralPath $VisionDbPath) { (Get-Item -LiteralPath $VisionDbPath).Length } else { 0 }
    $spoolBytes = Get-DirectoryBytes -Path $SpoolDirectory
    $spoolFiles = if (Test-Path -LiteralPath $SpoolDirectory) {
        (Get-ChildItem -LiteralPath $SpoolDirectory -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
    } else {
        0
    }

    foreach ($name in @($StudioProcessName, $StationProcessName)) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            $line = "{0},{1},{2},{3},{4:N2},{5:N2},{6:N2},{7:N2},{8:N2},{9}" -f `
                $timestamp,
                $Mode,
                $_.ProcessName,
                $_.Id,
                $_.CPU,
                ($_.WorkingSet64 / 1MB),
                ($_.PrivateMemorySize64 / 1MB),
                ($dbBytes / 1MB),
                ($spoolBytes / 1MB),
                $spoolFiles
            Add-Content -Path $csvPath -Value $line
        }
    }

    Start-Sleep -Seconds ([Math]::Max(5, $SampleSeconds))
}

$finalSummary = Get-StationSummary
$finalStationCount = Get-SummaryNumber -Summary $finalSummary -Names @("totalStations", "TotalStations", "stationCount", "StationCount")
$finalResultCount = Get-SummaryNumber -Summary $finalSummary -Names @("totalResults", "TotalResults", "resultCount", "ResultCount")
$finalDropCount = Get-SummaryNumber -Summary $finalSummary -Names @("droppedResults", "DroppedResults", "dropCount", "DropCount")
$finalBackpressureCount = Get-SummaryNumber -Summary $finalSummary -Names @("backpressureEvents", "BackpressureEvents", "backpressureCount", "BackpressureCount")
$finalSpoolBytes = Get-DirectoryBytes -Path $SpoolDirectory
$finalSpoolFiles = if (Test-Path -LiteralPath $SpoolDirectory) {
    (Get-ChildItem -LiteralPath $SpoolDirectory -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
} else {
    0
}

$preflight.finalSummary = $finalSummary
$preflight | ConvertTo-Json -Depth 12 | Out-File -FilePath $preflightPath -Encoding utf8

@"
# ClearVision Studio-Station Alpha Trial Notes

- Mode: $Mode
- Started: $($startedAt.ToString("o"))
- Ended: $((Get-Date).ToString("o"))
- Metrics CSV: $csvPath
- Preflight JSON: $preflightPath
- Summary: $summaryPath

Manual observations:
- UI stutter:
- Station inspection continuity:
- Studio restart/disconnect events:
- Network reconnect events:
- Rollback/deploy events:
- Operator notes:
"@ | Out-File -FilePath $notesPath -Encoding utf8

@"
# ClearVision Studio-Station Alpha Trial Summary

- Mode: $Mode
- Started: $($startedAt.ToUniversalTime().ToString("o"))
- Ended: $((Get-Date).ToUniversalTime().ToString("o"))
- Metrics CSV: $csvPath
- Notes: $notesPath
- Preflight JSON: $preflightPath
- Station count: $finalStationCount
- Result count: $finalResultCount
- Drop count: $finalDropCount
- Backpressure count: $finalBackpressureCount
- Spool files: $finalSpoolFiles
- Spool size MB: $([Math]::Round($finalSpoolBytes / 1MB, 2))

Preflight checks:
$($preflight.checks | ForEach-Object { "- $($_.name): $($_.passed) - $($_.message)" } | Out-String)
"@ | Out-File -FilePath $summaryPath -Encoding utf8

Write-Host "Alpha trial metrics: $csvPath"
Write-Host "Alpha trial notes:   $notesPath"
Write-Host "Alpha trial summary: $summaryPath"

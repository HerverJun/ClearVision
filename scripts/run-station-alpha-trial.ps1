param(
    [ValidateSet("Shadow4h", "Simulator10x1h")]
    [string]$Mode = "Shadow4h",
    [string]$Studio = "http://127.0.0.1:5000",
    [string]$Token = "",
    [string]$StationProcessName = "Acme.Product.Station",
    [string]$StudioProcessName = "Acme.Product.Desktop",
    [string]$VisionDbPath = ".\vision.db",
    [string]$SpoolDirectory = "$env:LOCALAPPDATA\ClearVisionStation\spool",
    [int]$SampleSeconds = 60,
    [string]$OutputDirectory = ".\.tmp\station-alpha-trial"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$durationSeconds = if ($Mode -eq "Shadow4h") { 4 * 60 * 60 } else { 60 * 60 }
$startedAt = Get-Date
$endedAt = $startedAt.AddSeconds($durationSeconds)
$csvPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}.csv" -f $startedAt, $Mode)
$notesPath = Join-Path $OutputDirectory ("alpha-trial-{0:yyyyMMdd-HHmmss}-{1}-notes.md" -f $startedAt, $Mode)

"timestampUtc,mode,processName,pid,cpuSeconds,workingSetMb,privateMemoryMb,visionDbMb,spoolMb,spoolFiles" | Out-File -FilePath $csvPath -Encoding utf8

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

@"
# ClearVision Studio-Station Alpha Trial Notes

- Mode: $Mode
- Started: $($startedAt.ToString("o"))
- Ended: $((Get-Date).ToString("o"))
- Metrics CSV: $csvPath

Manual observations:
- UI stutter:
- Station inspection continuity:
- Studio restart/disconnect events:
- Network reconnect events:
- Rollback/deploy events:
- Operator notes:
"@ | Out-File -FilePath $notesPath -Encoding utf8

Write-Host "Alpha trial metrics: $csvPath"
Write-Host "Alpha trial notes:   $notesPath"

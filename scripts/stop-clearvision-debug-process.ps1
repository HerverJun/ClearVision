[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $scriptRoot = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        $scriptRoot = Split-Path -Parent $PSCommandPath
    }

    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $RepoRoot = (Resolve-Path (Join-Path $scriptRoot "..")).Path
}

$targetExe = Join-Path $RepoRoot "Acme.Product\src\Acme.Product.Desktop\bin\Debug\net8.0-windows\win-x64\Acme.Product.Desktop.exe"
$targetExe = [System.IO.Path]::GetFullPath($targetExe)

$processes = Get-Process -Name "Acme.Product.Desktop" -ErrorAction SilentlyContinue |
    Where-Object {
        try {
            $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $targetExe)
        }
        catch {
            $false
        }
    }

foreach ($process in $processes) {
    Write-Host "Stopping stale ClearVision debug process $($process.Id)."

    $closed = $false
    if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
        $closed = $process.CloseMainWindow()
    }

    if ($closed -and $process.WaitForExit(3000)) {
        continue
    }

    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    try {
        $process.WaitForExit(5000) | Out-Null
    }
    catch {
        # The process may have already exited between Stop-Process and WaitForExit.
    }
}

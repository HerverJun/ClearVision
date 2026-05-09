param(
    [string]$HostAddress = "0.0.0.0",
    [int]$McPort = 5002,
    [int]$FinsPort = 9600,
    [int]$FinsServerNode = 1,
    [int]$FinsClientNode = 2,
    [switch]$DisableMc,
    [switch]$DisableFins
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\mc-fins"

if (-not $DisableMc -and (Get-NetTCPConnection -LocalPort $McPort -State Listen -ErrorAction SilentlyContinue)) {
    throw "Port $McPort is already in use. Stop the existing virtual MC PLC or pass a different -McPort."
}

if (-not $DisableFins -and (Get-NetTCPConnection -LocalPort $FinsPort -State Listen -ErrorAction SilentlyContinue)) {
    throw "Port $FinsPort is already in use. Stop the existing virtual FINS PLC or pass a different -FinsPort."
}

$arguments = @(
    "virtual_plc_mc_fins.py",
    "--host", $HostAddress,
    "--mc-port", $McPort,
    "--fins-port", $FinsPort,
    "--fins-server-node", $FinsServerNode,
    "--fins-client-node", $FinsClientNode
)

if ($DisableMc) {
    $arguments += "--disable-mc"
}

if ($DisableFins) {
    $arguments += "--disable-fins"
}

Push-Location $workdir
try {
    python @arguments
}
finally {
    Pop-Location
}

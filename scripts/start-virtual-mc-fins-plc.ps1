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

Set-Location $workdir

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

python @arguments

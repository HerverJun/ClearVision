param(
    [string]$HostAddress = "127.0.0.1",
    [int]$McPort = 5002,
    [int]$FinsPort = 9600,
    [double]$TimeoutSeconds = 5,
    [switch]$SkipMc,
    [switch]$SkipFins
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\mc-fins"

Set-Location $workdir

$arguments = @(
    "test_client.py",
    "--host", $HostAddress,
    "--mc-port", $McPort,
    "--fins-port", $FinsPort,
    "--timeout", $TimeoutSeconds
)

if ($SkipMc) {
    $arguments += "--skip-mc"
}

if ($SkipFins) {
    $arguments += "--skip-fins"
}

python @arguments

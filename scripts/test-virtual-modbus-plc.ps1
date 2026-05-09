param(
    [string]$HostAddress = "127.0.0.1",
    [int]$Port = 1502,
    [int]$UnitId = 1,
    [double]$TimeoutSeconds = 5
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\modbus"
$python = Join-Path $workdir ".venv\Scripts\python.exe"

Push-Location $workdir
try {
    if (-not (Test-Path $python)) {
        python -m venv .venv
    }

    & $python -c "import pymodbus" 2>$null
    if ($LASTEXITCODE -ne 0) {
        & $python -m ensurepip --upgrade --default-pip
        & $python -m pip install -r requirements.txt
    }

    & $python test_client.py `
        --host $HostAddress `
        --port $Port `
        --unit-id $UnitId `
        --timeout $TimeoutSeconds
}
finally {
    Pop-Location
}

param(
    [string]$HostAddress = "0.0.0.0",
    [int]$Port = 1502,
    [int]$UnitId = 1,
    [int]$CycleMs = 100,
    [int]$ProcessDelayMs = 500,
    [ValidateSet(0, 1)]
    [int]$ErrorOnCommand = 0
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$workdir = Join-Path $root "tools\virtual-plc\modbus"
$python = Join-Path $workdir ".venv\Scripts\python.exe"

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port is already in use. Stop the existing virtual Modbus PLC or pass a different -Port."
}

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

    & $python virtual_plc_modbus.py `
        --host $HostAddress `
        --port $Port `
        --unit-id $UnitId `
        --cycle-ms $CycleMs `
        --process-delay-ms $ProcessDelayMs `
        --error-on-command $ErrorOnCommand
}
finally {
    Pop-Location
}

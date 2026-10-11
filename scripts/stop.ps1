param([switch]$Force)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$mode='--stop-stack'
if($Force){$mode='--force-stop-stack'}
& (Join-Path $root 'artifacts/supervisor/Supervisor.exe') $mode (Join-Path $root '.local/panel.json')
if($LASTEXITCODE -ne 0){throw 'Complete shutdown was not confirmed. Inspect the terminal; -Force is a separate explicit option.'}

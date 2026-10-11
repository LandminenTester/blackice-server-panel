param([Parameter(Mandatory=$true)][string]$ServerRoot, [string]$Url = 'http://127.0.0.1:20772')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
node (Join-Path $PSScriptRoot 'setup.mjs') $ServerRoot $Url
if ($LASTEXITCODE -ne 0) { throw 'Setup failed.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
icacls (Join-Path $root '.local') /inheritance:r /grant:r "${identity}:(OI)(CI)F" 'SYSTEM:(OI)(CI)F' /Q | Out-Null
Get-ChildItem (Join-Path $root '.local') -File -Recurse | ForEach-Object { icacls $_.FullName /inheritance:r /grant:r "${identity}:F" 'SYSTEM:F' /Q | Out-Null }
Write-Host 'Panel settings and login file restricted to the current Windows user and SYSTEM.'

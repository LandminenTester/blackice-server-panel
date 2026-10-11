$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  npm --prefix ui ci
  if ($LASTEXITCODE -ne 0) { throw 'UI dependency restore failed.' }
  npm --prefix ui run check
  if ($LASTEXITCODE -ne 0) { throw 'UI type check failed.' }
  npm --prefix ui run build
  if ($LASTEXITCODE -ne 0) { throw 'UI build failed.' }
  dotnet publish src/Supervisor -c Release -o artifacts/supervisor
  if ($LASTEXITCODE -ne 0) { throw 'Supervisor build failed.' }
  dotnet publish src/Web -c Release -o artifacts/web
  if ($LASTEXITCODE -ne 0) { throw 'Webhost build failed.' }
} finally { Pop-Location }

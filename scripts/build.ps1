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
  Copy-Item licenses src/Web/wwwroot/licenses -Recurse -Force
  dotnet publish src/Supervisor -c Release -o artifacts/supervisor
  if ($LASTEXITCODE -ne 0) { throw 'Supervisor build failed.' }
  dotnet publish src/Web -c Release -o artifacts/web
  if ($LASTEXITCODE -ne 0) { throw 'Webhost build failed.' }
  dotnet publish src/Supervisor -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .local/launcher
  if ($LASTEXITCODE -ne 0) { throw 'Start entry build failed.' }
  Copy-Item -LiteralPath (Join-Path $root '.local/launcher/Supervisor.exe') -Destination (Join-Path $root 'Start.exe') -Force
} finally { Pop-Location }

[xml]$projectVersions = Get-Content (Join-Path $root "Directory.Build.props")
[IO.File]::WriteAllText((Join-Path $root "VERSION"), [string]$projectVersions.Project.PropertyGroup.Version)

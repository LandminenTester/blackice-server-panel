$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$state = Join-Path $root '.local'
New-Item -ItemType Directory -Force -Path $state | Out-Null
& icacls $state /inheritance:r /grant:r ($env:USERNAME + ':(OI)(CI)F') 'SYSTEM:(OI)(CI)F' | Out-Null
$configPath = Join-Path $state 'panel.json'
if (!(Test-Path (Join-Path $root 'artifacts/web/Web.exe'))) { & (Join-Path $PSScriptRoot 'build.ps1') }
$pipeName = 'replay-panel'
if (Test-Path $configPath) { $pipeName = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).pipeName }
function Connected([string]$name) { $pipe=New-Object IO.Pipes.NamedPipeClientStream('.', $name, [IO.Pipes.PipeDirection]::InOut); try{$pipe.Connect(500);return $true}catch{return $false}finally{$pipe.Dispose()} }
if (!(Connected $pipeName)) {
 Start-Process -FilePath (Join-Path $root 'artifacts/supervisor/Supervisor.exe') -ArgumentList ('"' + $configPath + '"') -WorkingDirectory (Join-Path $root 'artifacts/supervisor') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $state 'supervisor.log') -RedirectStandardError (Join-Path $state 'supervisor.err.log') | Out-Null
 for($i=0;$i -lt 40 -and !(Test-Path $configPath);$i++){Start-Sleep -Milliseconds 100}
}
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (!(Connected ($config.pipeName + '.web'))) {
 $env:REPLAY_PANEL_CONFIG=$configPath
 Start-Process -FilePath (Join-Path $root 'artifacts/web/Web.exe') -WorkingDirectory (Join-Path $root 'artifacts/web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $state 'web.log') -RedirectStandardError (Join-Path $state 'web.err.log') | Out-Null
}
Write-Host ('Panel: ' + $config.url)
if (!$config.passwordHash) { Write-Host 'Setup key: .local/supervisor.log' }

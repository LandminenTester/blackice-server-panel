$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$configPath = Join-Path $root '.local/panel.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if (!(Test-Path (Join-Path $root 'artifacts/web/Web.exe'))) { & (Join-Path $PSScriptRoot 'build.ps1') }
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $config.pipeName, [IO.Pipes.PipeDirection]::InOut)
try { $pipe.Connect(1000); $supervisorAvailable = $true } catch { $supervisorAvailable = $false } finally { $pipe.Dispose() }
if (!$supervisorAvailable) {
  Start-Process -FilePath (Join-Path $root 'artifacts/supervisor/Supervisor.exe') -ArgumentList ('"' + $configPath + '"') -WorkingDirectory (Join-Path $root 'artifacts/supervisor') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root '.local/supervisor.log') -RedirectStandardError (Join-Path $root '.local/supervisor.err.log') | Out-Null
}
$web = New-Object IO.Pipes.NamedPipeClientStream('.', ($config.pipeName + '.web'), [IO.Pipes.PipeDirection]::InOut)
try { $web.Connect(1000); $webAvailable = $true } catch { $webAvailable = $false } finally { $web.Dispose() }
if (!$webAvailable) {
  $env:REPLAY_PANEL_CONFIG = $configPath
  Start-Process -FilePath (Join-Path $root 'artifacts/web/Web.exe') -WorkingDirectory (Join-Path $root 'artifacts/web') -WindowStyle Hidden -RedirectStandardOutput (Join-Path $root '.local/web.log') -RedirectStandardError (Join-Path $root '.local/web.err.log') | Out-Null
}
Write-Host ('Panel: ' + $config.url + '. Login: .local/login.txt')

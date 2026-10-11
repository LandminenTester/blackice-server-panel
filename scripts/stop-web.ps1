$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $root '.local/panel.json') -Raw | ConvertFrom-Json
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.', ($config.pipeName + '.web'), [IO.Pipes.PipeDirection]::InOut)
try {
  $pipe.Connect(3000)
  $writer = New-Object IO.StreamWriter($pipe)
  $reader = New-Object IO.StreamReader($pipe)
  $writer.AutoFlush = $true
  $writer.WriteLine('shutdown')
  if ($reader.ReadLine() -ne 'ok') { throw 'Webhost did not acknowledge shutdown.' }
  Write-Host ('Webhost shutdown requested. Server policy: ' + $config.shutdownMode)
} finally { $pipe.Dispose() }

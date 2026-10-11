param([ValidateSet('status','start','stop','restart','force-stop','console','logs','command','settings','shutdown')][string]$Action = 'status', [string]$Command, [ValidateSet('keep-running','stop-server')][string]$ShutdownMode)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content (Join-Path $root '.local/panel.json') -Raw | ConvertFrom-Json
$pipe = New-Object IO.Pipes.NamedPipeClientStream('.', $config.pipeName, [IO.Pipes.PipeDirection]::InOut)
try {
  $pipe.Connect(3000)
  $reader = New-Object IO.StreamReader($pipe)
  $writer = New-Object IO.StreamWriter($pipe)
  $writer.AutoFlush = $true
  $writer.WriteLine((@{ action=$Action; command=$Command; shutdownMode=$ShutdownMode } | ConvertTo-Json -Compress))
  $reply = $reader.ReadLine() | ConvertFrom-Json
  if (!$reply.ok) { throw $reply.error }
  $reply
} finally { $pipe.Dispose() }

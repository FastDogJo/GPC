# CLAUDE : new file. Types the contents of a key-script file into one GPC instance (gpc_send_keys). Usage : .\SendKeys.ps1 <pid> <file> [-CharDelayMs n] [-LineDelayMs n]
param(
  [Parameter(Mandatory = $true, Position = 0)][int]$ProcId,
  [Parameter(Mandatory = $true, Position = 1)][string]$Path,
  [int]$CharDelayMs = -1,
  [int]$LineDelayMs = -1
)
. (Join-Path $PSScriptRoot "GpcPipe.ps1")
$full = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
$a = @{ path = $full }
if ($CharDelayMs -ge 0) { $a.charDelayMs = $CharDelayMs }
if ($LineDelayMs -ge 0) { $a.lineDelayMs = $LineDelayMs }
$reply = PipeCall $ProcId "gpc_send_keys" $a
if ($null -ne $reply) { Write-Host $reply }

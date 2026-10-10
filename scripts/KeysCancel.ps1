# CLAUDE : new file. Stops the running key script of one GPC instance (gpc_keys_cancel). Usage : .\KeysCancel.ps1 <pid>
param([Parameter(Mandatory = $true, Position = 0)][int]$ProcId)
. (Join-Path $PSScriptRoot "GpcPipe.ps1")
$reply = PipeCall $ProcId "gpc_keys_cancel" @{}
if ($null -ne $reply) { Write-Host $reply }

# CLAUDE : new file. State of the key script of one GPC instance (gpc_keys_status). Usage : .\KeysStatus.ps1 <pid>
param([Parameter(Mandatory = $true, Position = 0)][int]$ProcId)
. (Join-Path $PSScriptRoot "GpcPipe.ps1")
$reply = PipeCall $ProcId "gpc_keys_status" @{}
if ($null -ne $reply) { Write-Host $reply }

# CLAUDE : new file. Shared by CRTOn.ps1 / CRTOff.ps1 : switches the CRT of GPC instances on or off (display power animation only, the emulator keeps running)
#   by sending gpc_power_set through each instance's MCP pipe "<ExeName>.<pid>".
# Default : every running process called GPC.exe (or -ExeName <name>.exe). With -ProcId : only that pid. With -Dir : only the pids in Instances.txt of that RandomLaunch.ps1 run folder.
# Usage : .\CRTPower.ps1 -On $true|$false [-ProcId <pid>] [-ExeName GPC.exe] [-Dir "<TEMP>/GPCRandom/<timestamp>"]
param(
  [Parameter(Mandatory = $true)][bool]$On,
  [int]$ProcId = 0,
  [string]$ExeName = "GPC.exe",
  [string]$Dir = ""
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GpcPipe.ps1")
$pipeBase = [System.IO.Path]::GetFileNameWithoutExtension($ExeName)

function PipePower($procId)
{
  $reply = PipeCall $procId "gpc_power_set" @{ on = $On.ToString().ToLower() }
  if ($null -ne $reply) { Write-Host ("pid " + $procId + " : CRT " + $(if ($On) { "on" } else { "off" })) }
}

if ($ProcId -ne 0)
  {
    $pids = @($ProcId)
  }
elseif ($Dir -ne "")
  {
    Write-Host "Run folder $Dir"
    $pids = RunFolderPids $Dir
  }
else
  {
    $pids = @(Get-Process -Name $pipeBase -ErrorAction SilentlyContinue | Sort-Object Id | ForEach-Object { $_.Id })
  }
if ($pids.Count -eq 0) { Write-Host "Nothing to switch"; return }
foreach ($p in $pids) { PipePower $p }

# CLAUDE : new file. CRT off (power-off animation) for all GPC instances, or just one pid. Usage : .\CRTOff.ps1 [<pid>] [-ExeName GPC.exe] [-Dir "<run folder>"]
param([Parameter(Position = 0)][int]$ProcId = 0, [string]$ExeName = "GPC.exe", [string]$Dir = "")
& (Join-Path $PSScriptRoot "CRTPower.ps1") -On $false -ProcId $ProcId -ExeName $ExeName -Dir $Dir

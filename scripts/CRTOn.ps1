# CLAUDE : new file. CRT on (power-on animation) for all GPC instances, or just one pid. Usage : .\CRTOn.ps1 [<pid>] [-ExeName GPC.exe] [-Dir "<run folder>"]
param([Parameter(Position = 0)][int]$ProcId = 0, [string]$ExeName = "GPC.exe", [string]$Dir = "")
& (Join-Path $PSScriptRoot "CRTPower.ps1") -On $true -ProcId $ProcId -ExeName $ExeName -Dir $Dir

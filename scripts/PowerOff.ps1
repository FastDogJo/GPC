# CLAUDE : new file. Quits GPC instances by sending gpc_exit (discard unsaved changes) through each instance's MCP pipe "<ExeName>.<pid>".
# Default : every running process called GPC.exe (or -ExeName <name>.exe) in the process table.
# With -Dir : only the pids in Instances.txt of that RandomLaunch.ps1 run folder.
# Usage : .\PowerOff.ps1 [<pid>] [-ExeName GPC.exe] [-Dir "<TEMP>/GPCRandom/<timestamp>"]
param(
  [Parameter(Position = 0)][int]$ProcId = 0, # CLAUDE : optional single pid, e.g. .\PowerOff.ps1 1234
  [string]$ExeName = "GPC.exe",
  [string]$Dir = ""
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "GpcPipe.ps1") # CLAUDE : RunFolderPids
$pipeBase = [System.IO.Path]::GetFileNameWithoutExtension($ExeName) # the pipe is named after the exe without ".exe"

function PipeExit($procId)
{
  # CLAUDE : with a pid, the pipe base is that process's actual name
  $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
  $base = $(if (($ProcId -ne 0) -and $proc) { $proc.ProcessName } else { $pipeBase })
  $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", ($base + "." + $procId), [System.IO.Pipes.PipeDirection]::InOut)
  try
    {
      $pipe.Connect(2000)
      $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = "gpc_exit"; arguments = @{ discard = $true } } } | ConvertTo-Json -Depth 6 -Compress
      $w = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
      $w.AutoFlush = $true
      $w.WriteLine($body)
      # the instance may exit before it replies : a closed pipe is fine
      try { $r = New-Object System.IO.StreamReader($pipe); $reply = $r.ReadLine() } catch { $reply = "" }
      if (($reply -match '"isError"\s*:\s*true') -or ($reply -match '"error"')) { Write-Warning "pid $procId : $reply" }
      else { Write-Host "pid $procId : exit sent" }
    }
  catch { Write-Warning ("pid " + $procId + " : " + $_.Exception.Message) }
  finally { $pipe.Dispose() }
}

if ($ProcId -ne 0) # CLAUDE
  {
    $pids = @($ProcId)
  }
elseif ($Dir -ne "")
  {
    Write-Host "Run folder $Dir"
    $pids = RunFolderPids $Dir # CLAUDE
  }
else
  {
    $pids = @(Get-Process -Name $pipeBase -ErrorAction SilentlyContinue | Sort-Object Id | ForEach-Object { $_.Id })
  }
if ($pids.Count -eq 0) { Write-Host "Nothing to exit"; return }
foreach ($p in $pids) { PipeExit $p }

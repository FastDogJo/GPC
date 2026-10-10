# CLAUDE : new file. Shared helper (dot-source it) : one MCP tools/call to a GPC instance over its pipe "<ExeName>.<pid>".
# The pipe base name comes from the running process of that pid (falls back to GPC).

# pids listed in a RandomLaunch.ps1 run folder's Instances.txt ("<pid>|<program file>" per line)
function RunFolderPids([string]$dir)
{
  return @(Get-Content (Join-Path $dir "Instances.txt") | Where-Object { $_ -match '\|' } | ForEach-Object { [int]($_.Split('|', 2)[0]) } | Sort-Object)
}

function PipeCall([int]$procId, [string]$tool, [hashtable]$toolArgs)
{
  $proc = Get-Process -Id $procId -ErrorAction SilentlyContinue
  $pipeBase = $(if ($proc) { $proc.ProcessName } else { "GPC" })
  $pipe = New-Object System.IO.Pipes.NamedPipeClientStream(".", ($pipeBase + "." + $procId), [System.IO.Pipes.PipeDirection]::InOut)
  try
    {
      $pipe.Connect(2000)
      $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = $tool; arguments = $toolArgs } } | ConvertTo-Json -Depth 6 -Compress
      $w = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
      $w.AutoFlush = $true
      $w.WriteLine($body)
      $r = New-Object System.IO.StreamReader($pipe)
      $reply = $r.ReadLine()
      if (($null -eq $reply) -or ($reply -match '"isError"\s*:\s*true') -or ($reply -match '"error"')) { Write-Warning "pid $procId : $reply"; return $null }
      return $reply
    }
  catch { Write-Warning ("pid " + $procId + " : " + $_.Exception.Message); return $null }
  finally { $pipe.Dispose() }
}

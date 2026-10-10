# CLAUDE : new file. Stress/fuzz launcher : N GPC instances, each with a random preset from presets/List.vim, a random program from
#   programs/, and random BezelBrightness / Brightness / Tint / BehindLevel written into a temp copy of the preset (installed presets are never touched).
# Also a random [Window] X/Y per instance, fully inside a random monitor's work area.
# Windows are placed clear of a running local VFD's Controls / canvas windows (VFD-MCP get_settings) ; if VFD is not reachable it just warns.
# A "<program>.args" file next to the chosen program has its contents appended to the trs80gp command line.
# Keeps a running cost of the chosen models (assets/Cost.vim) and sends "$ n,nnn.nn" to VFD-MCP (set_param TextRow0) each time a preset is chosen. -DryRun does not send.
# Usage : .\RandomLaunch.ps1 -Count 5 [-Seed 1234] [-DelayMs 1500] [-CRTDelayMs 2000] [-KeysDelayMs 3000] [-GridCols 3] [-GridTop 100][-VfdHost localhost] [-DryRun] [-Install "path"] (default : parent of this script's folder)
param(
  [Parameter(Mandatory = $true)][int]$Count,
  [string]$Install = "", # CLAUDE : "" = parent of this script's folder (resolved below ; $PSScriptRoot is empty inside a param block on older PowerShell)
  [int]$Seed = (Get-Random -Maximum 1000000),
  [int]$DelayMs = 1500, # wait between emulator starts
  [string]$VfdHost = "localhost", # host of the VFD-MCP service ("" = do not send the running cost)
  [int]$CRTDelayMs = 2000, # CLAUDE : wait after the last start, before the power-on clicks
  [int]$KeysDelayMs = 3000, # CLAUDE : wait after the power-on clicks, before sending each program's .gpi key script
  [int]$GridCols = 0, # CLAUDE : > 0 = place the windows in an organized grid of this many columns (primary monitor, left-to-right then top-to-bottom) instead of randomly
  [int]$GridTop = -1, # CLAUDE : screen Y where the grid starts (-1 = top of the work area) ; the grid spans from here to the bottom of the work area
  [switch]$DryRun
)
$ErrorActionPreference = "Stop"
if ($Install -eq "") { $Install = (Split-Path $PSScriptRoot -Parent) } # CLAUDE

# Bright enough tint : at least one channel >= 0xB0
function RandomTint($rng)
{
  $c = @($rng.Next(0, 256), $rng.Next(0, 256), $rng.Next(0, 256))
  if ((($c | Measure-Object -Maximum).Maximum) -lt 0xB0) { $c[$rng.Next(0, 3)] = $rng.Next(0xB0, 256) }
  return ("{0:X2}{1:X2}{2:X2}" -f $c[0], $c[1], $c[2])
}

# Fisher-Yates ; returns a new shuffled array
function Shuffle($items, $rng)
{
  $a = @($items)
  for ($j = ($a.Count - 1); $j -gt 0; $j--)
    {
      $k = $rng.Next(0, ($j + 1))
      $t = $a[$j]; $a[$j] = $a[$k]; $a[$k] = $t
    }
  return ,$a
}

# Cost.vim lines "M1:48K:1197" -> @{ "M1:48K" = 1197 }
function ReadCosts($path)
{
  $c = @{}
  foreach ($line in (Get-Content $path))
    {
      if ($line -match '^\s*(M\d+):(\d+K):(\d+(\.\d+)?)\s*$') { $c[(($Matches[1] + ":" + $Matches[2]).ToUpper())] = [decimal]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture) }
    }
  return $c
}

# preset name "M1.48k.Authentic..." / "M3_48K.Cheap..." / "M4_64K..." -> cost of that model/memory (0 + warning when unknown)
function PresetCost($name, $costs)
{
  if ($name -match '^(M\d+)[._-](\d+)k')
    {
      $key = ($Matches[1].ToUpper() + ":" + $Matches[2] + "K")
      if ($costs.ContainsKey($key)) { return $costs[$key] }
    }
  Write-Warning "No cost for preset $name"
  return [decimal]0
}

# running cost text, "$ n,nnn,nnn.nn"
function CostText($total) { return ('$ ' + $total.ToString("N2", [Globalization.CultureInfo]::InvariantCulture)) }

# VFD-MCP : stateless JSON-RPC over HTTP, tool set_param TextRow0 (the first VFD text row). Failures only warn.
function VfdSend($text)
{
  if (($VfdHost -eq "") -or (-not $vfdUp)) { return } # CLAUDE
  $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = "set_param"; arguments = @{ name = "TextRow0"; text = $text } } } | ConvertTo-Json -Depth 6 -Compress
  # CLAUDE : fire and forget, write the HTTP request and close without reading the reply
  try
    {
      $bytes = [Text.Encoding]::UTF8.GetBytes($body)
      $head = [Text.Encoding]::ASCII.GetBytes("POST / HTTP/1.1`r`nHost: " + $VfdHost + ":" + $vfdPort + "`r`nContent-Type: application/json`r`nContent-Length: " + $bytes.Length + "`r`nConnection: close`r`n`r`n")
      $tcp = New-Object System.Net.Sockets.TcpClient
      $tcp.SendTimeout = 500
      $tcp.Connect($VfdHost, $vfdPort)
      $s = $tcp.GetStream()
      $s.Write($head, 0, $head.Length); $s.Write($bytes, 0, $bytes.Length); $s.Flush()
      $tcp.Close()
    }
  catch { }
}

# first property called $name anywhere in a parsed JSON object (the VFD reply nests the settings under structuredContent)
function FindKey($o, $name)
{
  if ($null -eq $o) { return $null }
  if ($o -is [System.Management.Automation.PSCustomObject])
    {
      if ($o.PSObject.Properties[$name]) { return $o.$name }
      foreach ($p in $o.PSObject.Properties) { $r = FindKey $p.Value $name; if ($null -ne $r) { return $r } }
    }
  elseif (($o -is [System.Collections.IEnumerable]) -and ($o -isnot [string]))
    {
      foreach ($e in $o) { $r = FindKey $e $name; if ($null -ne $r) { return $r } }
    }
  return $null
}

# screen rectangles @(x, y, w, h) of the running VFD's Controls window and (when visible) canvas, via VFD-MCP get_settings.
# Only for a VFD on this machine : the coordinates are that machine's desktop.
function VfdRects()
{
  $out = @()
  if ((-not $vfdUp) -or ($VfdHost -eq "") -or (-not (@("localhost", "127.0.0.1", $env:COMPUTERNAME) -contains $VfdHost))) { return ,$out }
  $body = @{ jsonrpc = "2.0"; id = 1; method = "tools/call"; params = @{ name = "get_settings"; arguments = @{} } } | ConvertTo-Json -Depth 6 -Compress
  try { $r = Invoke-RestMethod -Uri ("http://" + $VfdHost + ":" + $vfdPort + "/") -Method Post -Body $body -ContentType "application/json" -TimeoutSec 2 }
  catch { Write-Warning ("VFD-MCP get_settings : " + $_.Exception.Message + " (positions will not avoid VFD)"); return ,$out }
  $canvasShown = (FindKey $r "viewportVisible")
  foreach ($k in @("controlWindow", "canvasWindow"))
    {
      $o = FindKey $r $k
      if (($k -eq "canvasWindow") -and ($canvasShown -eq $false)) { continue }
      if (($null -ne $o) -and ($o.w -gt 0) -and ($o.h -gt 0)) { $out += ,@([int]$o.x, [int]$o.y, [int]$o.w, [int]$o.h) }
    }
  return ,$out
}

function Overlaps($x, $y, $w, $h, $rects)
{
  foreach ($r in $rects)
    {
      if (($x -lt ($r[0] + $r[2])) -and (($x + $w) -gt $r[0]) -and ($y -lt ($r[1] + $r[3])) -and (($y + $h) -gt $r[1])) { return $true }
    }
  return $false
}

function IniGet($lines, $section, $key)
{
  $cur = ""
  foreach ($line in $lines)
    {
      if ($line -match '^\[(.+)\]\s*$') { $cur = $Matches[1] }
      elseif (($cur -eq $section) -and ($line -match ('^' + [regex]::Escape($key) + '\s*=\s*(.*)$'))) { return $Matches[1].Trim() }
    }
  return $null
}

# returns the new lines ; warns when the key does not exist in the section
function IniSet($lines, $section, $key, $value)
{
  $cur = ""; $found = $false
  $out = New-Object System.Collections.Generic.List[string]
  foreach ($line in $lines)
    {
      if ($line -match '^\[(.+)\]\s*$') { $cur = $Matches[1] }
      elseif (($cur -eq $section) -and ($line -match ('^' + [regex]::Escape($key) + '\s*='))) { $line = "$key = $value"; $found = $true }
      $out.Add($line)
    }
  if (-not $found) { Write-Warning "[$section] $key not found in preset" }
  return $out.ToArray()
}

function AbsPath($baseDir, $p)
{
  return ([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($baseDir, $p)).Replace('\', '/'))
}

$presetDir = Join-Path $Install "presets"
$presets = @(Get-Content (Join-Path $presetDir "List.vim") | ForEach-Object { $_.Trim() } | Where-Object { (($_ -ne "") -and (-not $_.StartsWith("#"))) }) # CLAUDE : '#' lines are comments
$programs = @(Get-ChildItem (Join-Path $Install "programs") -File | Where-Object { (@(".cas", ".cmd", ".vim", ".txt") -contains $_.Extension.ToLower()) })
$exe = Join-Path $Install "bin/GPC.exe"
if (($presets.Count -eq 0) -or ($programs.Count -eq 0)) { throw "No presets or programs found under $Install" }

# physical-pixel monitor rectangles (GPC is per-monitor DPI aware, so a DPI-unaware PowerShell would report scaled values)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace Win -Name Dpi -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(System.IntPtr c);'
[void][Win.Dpi]::SetProcessDpiAwarenessContext([System.IntPtr](-4)) # PER_MONITOR_AWARE_V2
$screens = @([System.Windows.Forms.Screen]::AllScreens)

$rng = [System.Random]::new($Seed)
# CLAUDE : program pools. "<programs>/<MODEL#>.index" (e.g. M1.index : one program path per line, relative to programs/, lines starting with ; # // or " are comments) limits the programs for that model ;
#   a model without a usable index uses every program found above. Used programs are tracked across all models (see $used).
$progDir = Join-Path $Install "programs"
$pools = @{}; $used = New-Object 'System.Collections.Generic.HashSet[string]' # CLAUDE
function PoolFor($model)
{
  if ($pools.ContainsKey($model)) { return ,$pools[$model] }
  $pool = $programs
  $idx = Join-Path $progDir ($model + ".index")
  if (($model -ne "") -and (Test-Path -LiteralPath $idx))
    {
      $list = @()
      foreach ($l in (Get-Content -LiteralPath $idx))
        {
          $t = $l.Trim()
          if (($t -eq "") -or $t.StartsWith(";") -or $t.StartsWith("#") -or $t.StartsWith("//") -or $t.StartsWith('"')) { continue } # CLAUDE : comment lines start with ; # // or "
          $f = Join-Path $progDir $t
          if (Test-Path -LiteralPath $f -PathType Leaf) { $list += Get-Item -LiteralPath $f } else { Write-Warning "$model.index : missing program $t" }
        }
      if ($list.Count -gt 0) { $pool = $list } else { Write-Warning "$model.index has no usable programs : using all programs" }
    }
  $pools[$model] = $pool
  return ,$pool
}
# CLAUDE
function PresetModel($name) { return $(if ($name -match '^(M\d+)') { $Matches[1].ToUpper() } else { "" }) }
# CLAUDE : true when the model's pool still has a program not in $used
function HasUnused($model)
{
  foreach ($p in (PoolFor $model)) { if (-not $used.Contains($p.FullName)) { return $true } }
  return $false
}
$costs = ReadCosts (Join-Path $Install "assets/Cost.vim")
[decimal]$total = 0
# VFD-MCP port from the shared services list (fallback 11145)
$vfdPort = 11145
$svc = (Get-Content "R:/ASSETS/Services.vim" -ErrorAction SilentlyContinue | Where-Object { $_ -match '^\s*VFD-MCP\s+(\d+)' })
if ($svc) { [void]($svc[0] -match '^\s*VFD-MCP\s+(\d+)'); $vfdPort = [int]$Matches[1] }
# CLAUDE : one quick TCP probe ; when VFD-MCP is not listening every VFD call is skipped silently (no timeout warnings)
$vfdUp = $false
if ($VfdHost -ne "")
  {
    $tcp = New-Object System.Net.Sockets.TcpClient
    try { $vfdUp = ($tcp.ConnectAsync($VfdHost, $vfdPort).Wait(300) -and $tcp.Connected) } catch { $vfdUp = $false }
    $tcp.Close()
  }
$avoid = VfdRects # (VfdRects returns ,$out : an array of @(x,y,w,h) rects ; do not wrap it in @() again)
foreach ($r in $avoid) { Write-Host ("Avoiding VFD window {0},{1} {2}x{3}" -f $r[0], $r[1], $r[2], $r[3]) }
$outDir = Join-Path $env:TEMP ("GPCRandom/" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$instFile = Join-Path $outDir "Instances.txt" # CLAUDE
Write-Host "Seed $Seed  ($Count instances, files in $outDir)"

for ($i = 0; $i -lt $Count; $i++)
  {
    # CLAUDE : no program repeats until every program of every model in the preset list has been used : when the random preset's model has
    #   nothing unused left (e.g. M1/M3/M4 programs all done) but another model does (e.g. M2.index), re-pick among the presets of those models.
    #   Once nothing is unused anywhere, start a new cycle.
    $preset = $presets[$rng.Next(0, $presets.Count)]
    if (-not (HasUnused (PresetModel $preset)))
      {
        $cand = @($presets | Where-Object { (HasUnused (PresetModel $_)) })
        if ($cand.Count -gt 0) { $preset = $cand[$rng.Next(0, $cand.Count)] } else { $used.Clear() }
      }
    $total += (PresetCost $preset $costs)
    $costText = (CostText $total)
    $model = PresetModel $preset
    $pool = PoolFor $model
    $avail = @($pool | Where-Object { (-not $used.Contains($_.FullName)) })
    $prog = $avail[$rng.Next(0, $avail.Count)]
    [void]$used.Add($prog.FullName)
    $bezel = $rng.Next(15, 100)
    $bright = $rng.Next(30, 100)
    $tint = RandomTint $rng
    $behind = $rng.Next(10, 60)

    $lines = @(Get-Content (Join-Path $presetDir $preset))
    # relative paths in the preset are relative to presets/ : make them absolute because the copy lives elsewhere
    foreach ($pair in @(@("Launch", "Trs80gpPath"), @("Look", "BezelFile"), @("Glass", "File")))
      {
        $v = IniGet $lines $pair[0] $pair[1]
        if (($null -ne $v) -and ($v -ne "")) { $lines = IniSet $lines $pair[0] $pair[1] (AbsPath $presetDir $v) }
      }
    $lines = IniSet $lines "Look" "BezelBrightness" $bezel
    $lines = IniSet $lines "Look" "Brightness" $bright
    $lines = IniSet $lines "Look" "Tint" $tint
    $lines = IniSet $lines "Look" "BehindLevel" $behind
    # random position : a random monitor, then a random X/Y that keeps the whole window inside its work area (no taskbar)
    $w = 0; $h = 0
    if (-not [int]::TryParse((IniGet $lines "Window" "Width"), [ref]$w)) { $w = 1400 }
    if (-not [int]::TryParse((IniGet $lines "Window" "Height"), [ref]$h)) { $h = 1000 }
    if ($GridCols -gt 0)
      {
        # CLAUDE : grid cell (primary monitor work area split into $GridCols x rows) ; window at the cell's top-left, clamped inside the work area
        $area = ([System.Windows.Forms.Screen]::PrimaryScreen).WorkingArea
        $rows = [Math]::Ceiling(($Count / $GridCols))
        $top = $(if ($GridTop -ge 0) { $GridTop } else { $area.Top })
        $cellW = [Math]::Floor(($area.Width / $GridCols))
        $cellH = [Math]::Floor((($area.Bottom - $top) / $rows))
        $x = $area.Left + (($i % $GridCols) * $cellW) + [Math]::Floor((($cellW - $w) / 2)) # centered in the cell
        $y = $top + ([Math]::Floor(($i / $GridCols)) * $cellH) + [Math]::Floor((($cellH - $h) / 2))
        $x = [Math]::Max($area.Left, [Math]::Min($x, ($area.Right - $w)))
        $y = [Math]::Max($area.Top, [Math]::Min($y, ($area.Bottom - $h)))
      }
    else
      {
    # ... and does not overlap a running VFD window (up to 50 tries)
    $tries = 0
    do
      {
        $area = $screens[$rng.Next(0, $screens.Count)].WorkingArea
        $x = $area.Left + $rng.Next(0, [Math]::Max(1, (($area.Width - $w) + 1)))
        $y = $area.Top + $rng.Next(0, [Math]::Max(1, (($area.Height - $h) + 1)))
        $tries++
      }
    while ((Overlaps $x $y $w $h $avoid) -and ($tries -lt 50))
    if (Overlaps $x $y $w $h $avoid) { Write-Warning "#$i : could not find a spot clear of the VFD windows" }
      }
    $lines = IniSet $lines "Window" "X" $x
    $lines = IniSet $lines "Window" "Y" $y

    # CLAUDE : program path goes at the end of the preset's command line (so it is no longer passed as a separate argument)
    # trs80gp wants the program FIRST on its command line and does not cope with spaces (even quoted) : use the 8.3 short path when there are spaces
    $progPath = $prog.FullName
    if ($progPath.Contains(" "))
      {
        $short = (New-Object -ComObject Scripting.FileSystemObject).GetFile($progPath).ShortPath
        if ($short -and (-not $short.Contains(" "))) { $progPath = $short } else { Write-Warning "#$i : no space-free short path for $progPath" }
      }
    $progPath = $progPath.Replace('\', '/')
    $cl = IniGet $lines "Launch" "CommandLine"
    # CLAUDE : optional "<program>.args" next to the program : its contents (lines joined with spaces) are appended to the trs80gp command line
    $argsFile = [System.IO.Path]::ChangeExtension($prog.FullName, ".args")
    $extra = ""
    if (Test-Path -LiteralPath $argsFile) { $extra = ((@(Get-Content -LiteralPath $argsFile) | ForEach-Object { $_.Trim() } | Where-Object { ($_ -ne "") }) -join " ") }
    # CLAUDE : M2 does not load the program file itself : only the program's .args contents go on the command line
    $loadPath = $(if ($model -eq "M2") { "" } else { $progPath })
    $lines = IniSet $lines "Launch" "CommandLine" ((($loadPath + " " + $cl + " " + $extra)).Trim())
    if ($extra -ne "") { Write-Host ("     args : " + $extra) }

    $gpc = Join-Path $outDir ("{0:D2}.{1}" -f $i, $preset)
    Set-Content -Path $gpc -Value $lines -Encoding ASCII
    Write-Host ("#{0:D2} {1}  {2}  bezel={3} bright={4} tint={5} behind={6} pos={7},{8} size={9}x{10} cost={11}" -f $i, $preset, $prog.Name, $bezel, $bright, $tint, $behind, $x, $y, $w, $h, $costText)

    if (-not $DryRun)
      {
        # CLAUDE : one file "Instances.txt" in the run folder, a line per instance : "<pid>|<program file>"
        $proc = Start-Process -FilePath $exe -ArgumentList @(('"' + $gpc + '"')) -WorkingDirectory (Join-Path $Install "bin") -PassThru
        Add-Content -Path $instFile -Value ([string]$proc.Id + "|" + $prog.FullName) -Encoding ASCII
        Start-Sleep -Milliseconds $DelayMs
        VfdSend $costText # CLAUDE : sent after the DelayMs sleep
      }
  }

# CLAUDE : once everything is started, click each instance's power rect (CRT power-on animation) through its MCP pipe,
#   wait $KeysDelayMs, then gpc_send_keys the program's "<name>.gpi" sidecar (same folder, same base name) when it exists
if (-not $DryRun)
  {
    . (Join-Path $PSScriptRoot "GpcPipe.ps1")
    $insts = @(Get-Content $instFile | Where-Object { $_ -match '\|' } | ForEach-Object { $p = $_.Split('|', 2); [pscustomobject]@{ Pid = [int]$p[0]; Program = $p[1] } })
    Start-Sleep -Milliseconds $CRTDelayMs
    foreach ($inst in $insts) { [void](PipeCall $inst.Pid "gpc_power_click" @{}) }
    Start-Sleep -Milliseconds $KeysDelayMs
    foreach ($inst in $insts)
      {
        $gpi = [System.IO.Path]::ChangeExtension($inst.Program, ".gpi")
        if (Test-Path -LiteralPath $gpi)
          {
            Write-Host ("pid " + $inst.Pid + " : keys " + (Split-Path $gpi -Leaf))
            [void](PipeCall $inst.Pid "gpc_send_keys" @{ path = $gpi })
          }
      }
  }

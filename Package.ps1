# CLAUDE : new file. Builds the end-user release zip : dist\GPC-<Version>.zip
# Usage : powershell -File Package.ps1 [-Version 1.0]
# Zip layout mirrors the repo so GPC.exe finds ..\assets, and presets use ..\assets paths :
#   GPC\bin\GPC.exe  GPC\assets  GPC\presets  GPC\CAS  GPC\README.md  GPC\LICENSE
param([string]$Version = "1.0", [switch]$ExeOnly) # CLAUDE : -ExeOnly

$ErrorActionPreference = "Stop"
$root  = $PSScriptRoot

# CLAUDE : -ExeOnly = fast dev cycle : just the single-file exe -> bin\single\GPC.exe (copy it over the installed one). dist and the zip are not touched.
if ($ExeOnly)
{
  $single = Join-Path $root "bin\single"
  dotnet publish (Join-Path $root "src\APP.csproj") -c Release -o $single
  if ($LASTEXITCODE -ne 0) { throw "publish failed" }
  Get-ChildItem $single -File | Where-Object { $_.Name -ne "GPC.exe" } | Remove-Item -Force
  $exe = Get-Item (Join-Path $single "GPC.exe")
  Write-Host ("Built " + $exe.FullName + " (" + [math]::Round(($exe.Length / 1MB), 1) + " MB)")
  return
}

$dist  = Join-Path $root "dist"
$stage = Join-Path $dist "GPC"
$zip   = Join-Path $dist ("GPC-" + $Version + ".zip")

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage "bin") | Out-Null

dotnet publish (Join-Path $root "src\APP.csproj") -c Release -o (Join-Path $stage "bin")
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# Keep only the exe (no stray pdb / json / dll)
Get-ChildItem (Join-Path $stage "bin") -File | Where-Object { $_.Name -ne "GPC.exe" } | Remove-Item -Force

Copy-Item (Join-Path $root "assets")  $stage -Recurse
Copy-Item (Join-Path $root "presets") $stage -Recurse
Copy-Item (Join-Path $root "programs") $stage -Recurse # CLAUDE : CAS -> programs
Remove-Item (Join-Path $stage "programs\M2\SAMPLE.dmk") -Force -ErrorAction SilentlyContinue # CLAUDE : TRSDOS image from trs80gp, not redistributed
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage # CLAUDE
# Unused leftovers (presets use Glass.png)
Get-ChildItem (Join-Path $stage "assets") -Filter "M?Glass.png" | Remove-Item -Force

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Created " + $zip + " (" + [math]::Round(((Get-Item $zip).Length / 1MB), 1) + " MB)")

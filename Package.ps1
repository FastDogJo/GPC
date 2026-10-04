# CLAUDE : new file. Builds the end-user release zip : dist\GPC-<Version>.zip
# Usage : powershell -File Package.ps1 [-Version 1.0]
# Zip layout mirrors the repo so GPC.exe finds ..\assets, and presets use ..\assets paths :
#   GPC\bin\GPC.exe  GPC\assets  GPC\presets  GPC\CAS  GPC\README.md  GPC\LICENSE
param([string]$Version = "1.0")

$ErrorActionPreference = "Stop"
$root  = $PSScriptRoot
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
Copy-Item (Join-Path $root "CAS")     $stage -Recurse
Copy-Item (Join-Path $root "README.md") $stage
Copy-Item (Join-Path $root "LICENSE") $stage # CLAUDE
# Unused leftovers (presets use Glass.png)
Get-ChildItem (Join-Path $stage "assets") -Filter "M?Glass.png" | Remove-Item -Force

Compress-Archive -Path $stage -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("Created " + $zip + " (" + [math]::Round(((Get-Item $zip).Length / 1MB), 1) + " MB)")

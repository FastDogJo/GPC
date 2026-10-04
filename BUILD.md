# Build instructions

## Requirements
- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Build
```
cd src
dotnet build APP.csproj -c Release
```
Output goes to `..\bin\` (`GPC.exe` plus its dlls). NuGet packages (WebView2, Silk.NET) restore automatically.
`lib\nini-core.dll` is referenced directly and is included in the repo.

## Release zip (single-file exe)
```
powershell -File Package.ps1 -Version 1.0
```
Creates `dist\GPC-1.0.zip` : one `bin\GPC.exe` (framework-dependent, needs the .NET 10 Desktop Runtime) plus `assets`, `presets`, `CAS`, `README.md`.
The OverlayControls UI files are embedded in the exe.

## Run
Run `bin\GPC.exe`. On first run it creates a settings `.ini` next to the executable.
Download `trs80gp.exe` from https://48k.ca/trs80gp.html into `bin`, then set its path and its command line in the settings window.

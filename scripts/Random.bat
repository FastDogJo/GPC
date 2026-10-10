@echo off
rem CLAUDE : capture this .bat's folder now ; SHIFT below also shifts %0, which would break %~dp0 later
set "here=%~dp0"
rem CLAUDE : usage / default handling
for %%H in (help -help --help /help /? -?) do if /i "%~1"=="%%H" goto Usage
if "%~1"=="" goto NoArgs
rem CLAUDE : any unsupported argument (or an option missing its value) shows the usage ; SHIFT does not change %*
:Check
if "%~1"=="" goto Checked
set "kind="
for %%O in (-Count -Seed -Delay -DelayMs -CRTDelayMs -KeysDelayMs -GridCols -GridTop -VfdHost -Install) do if /i "%~1"=="%%O" set "kind=V"
if /i "%~1"=="-DryRun" set "kind=S"
if not defined kind goto BadArg
rem CLAUDE : remember which defaults the user overrides (a repeated parameter is a PowerShell error)
if /i "%~1"=="-Count" set "hasCount=1"
if /i "%~1"=="-Delay" set "hasDelay=1"
if /i "%~1"=="-DelayMs" set "hasDelay=1"
if /i "%~1"=="-CRTDelayMs" set "hasCrt=1"
if "%kind%"=="V" if "%~2"=="" goto BadArg
if "%kind%"=="V" shift
shift
goto Check

:BadArg
echo Unsupported argument : %~1
echo.
call :Usage
exit /b 1

:Checked
set "defs="
if not defined hasCount set "defs=%defs% -Count 5"
if not defined hasDelay set "defs=%defs% -Delay 800"
if not defined hasCrt set "defs=%defs% -CRTDelayMs 4000"
rem CLAUDE : run RandomLaunch.ps1 from the folder this .bat lives in (%~dp0), not the current directory
powershell -File "%here%RandomLaunch.ps1"%defs% %*
goto :eof

:NoArgs
rem CLAUDE : same %~dp0 fix
powershell -File "%here%RandomLaunch.ps1" -Count 1
goto :eof

:Usage
echo Usage : Random.bat [RandomLaunch options]
echo.
echo   (no arguments)   -Count 1, no -Delay, no -CRTDelayMs
echo   with arguments   defaults -Count 5 -Delay 800 -CRTDelayMs 4000 are
echo                    prepended, then your arguments are appended
echo.
echo Options : -Count n  -Seed n  -DelayMs n  -CRTDelayMs n  -KeysDelayMs n  -GridCols n  -GridTop y  -VfdHost host
echo           -DryRun  -Install "path"

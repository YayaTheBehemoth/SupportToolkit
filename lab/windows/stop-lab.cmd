@echo off
setlocal
rem Switches SupportToolkit back to the profile that was active before
rem start-lab.cmd, or to the profile named as the argument.
rem   stop-lab.cmd           switch back; the lab keeps running in WSL
rem   stop-lab.cmd --down    switch back and stop the lab (its data is kept)
rem   stop-lab.cmd <name>    switch to <name> instead

if not defined LAB_WSL_DISTRO set "LAB_WSL_DISTRO=Ubuntu-24.04"
if not defined LAB_WSL_REPO set "LAB_WSL_REPO=~/SupportToolkit"
set "PROJECT=%~dp0..\..\src\SupportToolkit"
set "PREVIOUS_FILE=%~dp0.previous-profile"

set "TARGET="
set "DOWN="
if /i "%~1"=="--down" (set "DOWN=1") else (set "TARGET=%~1")
if not defined TARGET if exist "%PREVIOUS_FILE%" set /p TARGET=<"%PREVIOUS_FILE%"

if defined TARGET (
    dotnet run --project "%PROJECT%" -- configure use %TARGET%
    if not errorlevel 1 if exist "%PREVIOUS_FILE%" del "%PREVIOUS_FILE%"
) else (
    echo No earlier profile is remembered; the active profile stays as it is.
    echo Switch with: dotnet run --project src\SupportToolkit -- configure use ^<name^>
)

if defined DOWN (
    wsl -d %LAB_WSL_DISTRO% -- bash -c "cd %LAB_WSL_REPO% && lab/up.sh --stop"
)

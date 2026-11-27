@echo off
setlocal
title SupportToolkit lab
rem Starts the second-provider lab in WSL, switches SupportToolkit to the
rem 'lab' profile and opens the web UI. When the web UI is closed (its
rem "Exit SupportToolkit" button), it switches back to the profile that was
rem active before. See lab/README.md, "Lab on your own Windows PC".
rem
rem   LAB_WSL_DISTRO  the WSL distribution (default Ubuntu-24.04)
rem   LAB_WSL_REPO    the repository's clone inside WSL (default ~/SupportToolkit)

if not defined LAB_WSL_DISTRO set "LAB_WSL_DISTRO=Ubuntu-24.04"
if not defined LAB_WSL_REPO set "LAB_WSL_REPO=~/SupportToolkit"
set "PROJECT=%~dp0..\..\src\SupportToolkit"
set "PREVIOUS_FILE=%~dp0.previous-profile"

echo == Lab (WSL: %LAB_WSL_DISTRO%, %LAB_WSL_REPO%)
wsl -d %LAB_WSL_DISTRO% -- bash -c "cd %LAB_WSL_REPO% && lab/up.sh"
if errorlevel 1 (
    echo.
    echo The lab did not start. The message above says why.
    pause
    exit /b 1
)

rem WSL stops a few seconds after no Windows program uses it, and the lab's
rem containers stop with it. A minimized window keeps it running until
rem stop-lab.cmd closes it.
taskkill /fi "WINDOWTITLE eq SupportToolkit lab - keeps WSL running" /t /f >nul 2>&1
start "SupportToolkit lab - keeps WSL running" /min wsl -d %LAB_WSL_DISTRO% -- bash -c "echo This window keeps the lab running while you work. stop-lab.cmd closes it.; exec sleep infinity"

echo.
echo == SupportToolkit
dotnet build "%PROJECT%" --nologo -v q
if errorlevel 1 (
    echo.
    echo SupportToolkit could not be built. The message above says why.
    pause
    exit /b 1
)

rem Remember the active profile, unless it is already the lab.
set "PREVIOUS="
for /f "tokens=2,*" %%a in ('dotnet run --no-build --project "%PROJECT%" -- configure status ^| findstr /b /c:"Active profile:"') do set "PREVIOUS=%%b"
if defined PREVIOUS if /i not "%PREVIOUS%"=="lab" (
    > "%PREVIOUS_FILE%" echo %PREVIOUS%
)

dotnet run --no-build --project "%PROJECT%" -- configure use lab
if errorlevel 1 (
    echo.
    echo There is no 'lab' profile yet. Create it once with:
    echo   dotnet run --project src\SupportToolkit -- configure lab
    pause
    exit /b 1
)

echo.
echo The web UI opens in your browser, using the LAB profile.
echo Leave this window open while you work. When you are finished, press
echo "Exit SupportToolkit" in the page; this window then switches back to
echo your previous profile. (Closing the window instead leaves 'lab' active:
echo run lab\windows\stop-lab.cmd then.)
echo.
dotnet run --no-build --project "%PROJECT%" -- serve --port 5050

call "%~dp0stop-lab.cmd"

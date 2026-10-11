
@echo off
setlocal EnableExtensions

rem --------------------------------------------------
rem SupportToolkit - Windows x64 distribution
rem --------------------------------------------------

set "ROOT=%~dp0.."
set "PROJECT=src\SupportToolkit\SupportToolkit.csproj"
set "PUBLISH_DIR=artifacts\publish\win-x64"
set "PACKAGE_DIR=artifacts"
set "PACKAGE_NAME=SupportToolkit-win-x64.zip"

pushd "%ROOT%" || exit /b 1

echo.
echo ========================================
echo SupportToolkit - Windows Publish
echo ========================================
echo.

rem Validate build and tests before packaging.

echo [1/4] Building project...
dotnet build -c Release
if errorlevel 1 goto failed

echo.
echo [2/4] Running tests...
dotnet test -c Release --no-build
if errorlevel 1 goto failed

echo.
echo [3/4] Publishing Windows executable...

if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
    if errorlevel 1 goto failed
)

dotnet publish "%PROJECT%" ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -o "%PUBLISH_DIR%"

if errorlevel 1 goto failed

if not exist "%PUBLISH_DIR%\SupportToolkit.exe" (
    echo ERROR: Published executable not found.
    goto failed
)

echo.
echo [4/4] Creating ZIP package...

if not exist "%PACKAGE_DIR%" mkdir "%PACKAGE_DIR%"

if exist "%PACKAGE_DIR%\%PACKAGE_NAME%" (
    del /q "%PACKAGE_DIR%\%PACKAGE_NAME%"
    if errorlevel 1 goto failed
)

tar -a -c -f "%PACKAGE_DIR%\%PACKAGE_NAME%" -C "%PUBLISH_DIR%" .
if errorlevel 1 goto failed

echo.
echo ========================================
echo Publish completed successfully.
echo ========================================
echo.
echo Executable:
echo %PUBLISH_DIR%\SupportToolkit.exe
echo.
echo Distribution:
echo %PACKAGE_DIR%\%PACKAGE_NAME%
echo.

popd
exit /b 0

:failed
echo.
echo ERROR: Build, test, or packaging failed.
echo.
popd
exit /b 1

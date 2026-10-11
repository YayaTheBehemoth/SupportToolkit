
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

rem --------------------------------------------------
rem 1. Build
rem --------------------------------------------------

echo [1/5] Building project...

dotnet build -c Release
if errorlevel 1 goto failed

rem --------------------------------------------------
rem 2. Test
rem --------------------------------------------------

echo.
echo [2/5] Running tests...

dotnet test -c Release --no-build
if errorlevel 1 goto failed

rem --------------------------------------------------
rem 3. Publish
rem --------------------------------------------------

echo.
echo [3/5] Publishing Windows executable...

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

rem --------------------------------------------------
rem 4. Create ZIP
rem --------------------------------------------------

echo.
echo [4/5] Creating ZIP package...

if not exist "%PACKAGE_DIR%" (
    mkdir "%PACKAGE_DIR%"
    if errorlevel 1 goto failed
)

if exist "%PACKAGE_DIR%\%PACKAGE_NAME%" (
    del /q "%PACKAGE_DIR%\%PACKAGE_NAME%"
    if errorlevel 1 goto failed
)

powershell -NoProfile -NonInteractive -Command ^
    "$ErrorActionPreference = 'Stop'; Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%PACKAGE_DIR%\%PACKAGE_NAME%' -CompressionLevel Optimal"

if errorlevel 1 goto failed

if not exist "%PACKAGE_DIR%\%PACKAGE_NAME%" (
    echo ERROR: ZIP package not found.
    goto failed
)

rem --------------------------------------------------
rem 5. Validate ZIP
rem --------------------------------------------------

echo.
echo [5/5] Validating ZIP package...

powershell -NoProfile -NonInteractive -Command ^
    "$ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; $archive = (Resolve-Path '%PACKAGE_DIR%\%PACKAGE_NAME%').Path; $zip = [System.IO.Compression.ZipFile]::OpenRead($archive); try { if (-not ($zip.Entries | Where-Object { $_.FullName -eq 'SupportToolkit.exe' })) { throw 'SupportToolkit.exe missing from ZIP' }; foreach ($entry in $zip.Entries) { if ($entry.Name) { $stream = $entry.Open(); try { $stream.CopyTo([System.IO.Stream]::Null) } finally { $stream.Dispose() } } }; Write-Host 'ZIP validation passed.' } finally { $zip.Dispose() }"

if errorlevel 1 goto failed

rem --------------------------------------------------
rem Complete
rem --------------------------------------------------

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
echo ERROR: Build, test, publishing, or packaging failed.
echo.

popd
exit /b 1
